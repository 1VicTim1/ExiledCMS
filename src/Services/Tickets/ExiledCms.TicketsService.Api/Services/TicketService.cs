using System.Data;
using System.Text;
using System.Text.Json;
using Dapper;
using ExiledCms.TicketsService.Api.Contracts;
using ExiledCms.TicketsService.Api.Domain;
using ExiledCms.TicketsService.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace ExiledCms.TicketsService.Api.Services;

public interface ITicketService
{
    Task<TicketDetailResponse> CreateAsync(CreateTicketRequest request, CancellationToken cancellationToken);

    Task<PagedResponse<TicketSummaryResponse>> ListAsync(TicketsQueryRequest request, CancellationToken cancellationToken);

    Task<TicketDetailResponse> GetByIdAsync(Guid ticketId, CancellationToken cancellationToken);

    Task<TicketMessageResponse> AddMessageAsync(Guid ticketId, AddTicketMessageRequest request, CancellationToken cancellationToken);

    Task<TicketAssignmentResponse> AssignAsync(Guid ticketId, AssignTicketRequest request, CancellationToken cancellationToken);

    Task<TicketDetailResponse> ChangeStatusAsync(Guid ticketId, ChangeTicketStatusRequest request, CancellationToken cancellationToken);

    Task<TicketInternalNoteResponse> AddInternalNoteAsync(Guid ticketId, AddInternalNoteRequest request, CancellationToken cancellationToken);
}

public sealed class TicketService : ITicketService
{
    private readonly MySqlConnectionFactory _connectionFactory;
    private readonly IRequestActorAccessor _actorAccessor;
    private readonly IOptions<NatsOptions> _natsOptions;

    public TicketService(
        MySqlConnectionFactory connectionFactory,
        IRequestActorAccessor actorAccessor,
        IOptions<NatsOptions> natsOptions)
    {
        _connectionFactory = connectionFactory;
        _actorAccessor = actorAccessor;
        _natsOptions = natsOptions;
    }

    public async Task<TicketDetailResponse> CreateAsync(CreateTicketRequest request, CancellationToken cancellationToken)
    {
        var actor = _actorAccessor.GetRequiredActor();
        EnsurePermission(actor, TicketPermissions.Create);

        var subject = NormalizeSubject(request.Subject);
        var priority = NormalizePriority(request.Priority);
        var initialMessage = NormalizeBody(request.Message, "message");
        if (request.CategoryId == Guid.Empty)
        {
            throw ApiException.BadRequest("CategoryId must be provided.", "category_required");
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var category = await GetCategoryByIdAsync(connection, request.CategoryId, transaction, cancellationToken)
            ?? throw ApiException.BadRequest("The specified category does not exist.", "category_not_found");

        if (!category.IsActive)
        {
            throw ApiException.BadRequest("The specified category is inactive.", "category_inactive");
        }

        var now = DateTime.UtcNow;
        var ticketId = Guid.NewGuid();
        var messageId = Guid.NewGuid();

        await connection.ExecuteAsync(new CommandDefinition(
            commandText: """
                INSERT INTO tickets (
                    id,
                    created_by_user_id,
                    created_by_display_name,
                    subject,
                    category_id,
                    priority,
                    status,
                    assigned_staff_user_id,
                    assigned_staff_display_name,
                    created_at_utc,
                    updated_at_utc,
                    closed_at_utc,
                    last_message_at_utc
                ) VALUES (
                    @Id,
                    @CreatedByUserId,
                    @CreatedByDisplayName,
                    @Subject,
                    @CategoryId,
                    @Priority,
                    @Status,
                    NULL,
                    NULL,
                    @CreatedAtUtc,
                    @UpdatedAtUtc,
                    NULL,
                    @LastMessageAtUtc
                )
                """,
            parameters: new
            {
                Id = ticketId.ToString("D"),
                CreatedByUserId = actor.UserId.ToString("D"),
                CreatedByDisplayName = actor.DisplayName,
                Subject = subject,
                CategoryId = request.CategoryId.ToString("D"),
                Priority = priority,
                Status = TicketStatuses.Open,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                LastMessageAtUtc = now,
            },
            transaction: transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            commandText: """
                INSERT INTO ticket_messages (
                    id,
                    ticket_id,
                    author_user_id,
                    author_display_name,
                    author_role,
                    is_staff_reply,
                    body,
                    created_at_utc
                ) VALUES (
                    @Id,
                    @TicketId,
                    @AuthorUserId,
                    @AuthorDisplayName,
                    @AuthorRole,
                    @IsStaffReply,
                    @Body,
                    @CreatedAtUtc
                )
                """,
            parameters: new
            {
                Id = messageId.ToString("D"),
                TicketId = ticketId.ToString("D"),
                AuthorUserId = actor.UserId.ToString("D"),
                AuthorDisplayName = actor.DisplayName,
                AuthorRole = actor.Role,
                IsStaffReply = false,
                Body = initialMessage,
                CreatedAtUtc = now,
            },
            transaction: transaction,
            cancellationToken: cancellationToken));

        await InsertAuditLogAsync(
            connection,
            transaction,
            ticketId,
            actor,
            TicketAuditActions.TicketCreated,
            isInternal: false,
            details: new
            {
                subject,
                priority,
                status = TicketStatuses.Open,
                categoryId = request.CategoryId,
                categoryName = category.Name,
                initialMessageId = messageId,
            },
            cancellationToken: cancellationToken);

        await EnqueueOutboxEventAsync(
            connection,
            transaction,
            subject: TicketAuditActions.TicketCreated,
            eventType: TicketAuditActions.TicketCreated,
            actor: actor,
            payload: new
            {
                ticketId,
                subject,
                priority,
                status = TicketStatuses.Open,
                category = new
                {
                    id = request.CategoryId,
                    name = category.Name,
                },
                createdBy = new
                {
                    actor.UserId,
                    actor.DisplayName,
                },
                createdAtUtc = now,
            },
            cancellationToken: cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return await GetTicketDetailCoreAsync(ticketId, actor, enforceAccess: false, includeInternalArtifacts: actor.HasPermission(TicketPermissions.ViewInternalNotes), cancellationToken: cancellationToken);
    }

    public async Task<PagedResponse<TicketSummaryResponse>> ListAsync(TicketsQueryRequest request, CancellationToken cancellationToken)
    {
        var actor = _actorAccessor.GetRequiredActor();
        var canReadAll = actor.HasPermission(TicketPermissions.ReadAll);
        var canReadOwn = actor.HasPermission(TicketPermissions.ReadOwn);

        if (!canReadAll && !canReadOwn)
        {
            throw ApiException.Forbidden("The current actor is not allowed to list tickets.", "ticket_list_forbidden");
        }

        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = request.PageSize <= 0 ? 20 : Math.Min(request.PageSize, 100);
        var offset = (page - 1) * pageSize;

        var sqlFilter = new StringBuilder(
            """
            FROM tickets t
            INNER JOIN ticket_categories c ON c.id = t.category_id
            LEFT JOIN (
                SELECT ticket_id, COUNT(*) AS message_count
                FROM ticket_messages
                GROUP BY ticket_id
            ) message_counts ON message_counts.ticket_id = t.id
            WHERE 1 = 1
            """);

        var parameters = new DynamicParameters();

        if (!canReadAll)
        {
            sqlFilter.AppendLine("AND t.created_by_user_id = @ActorUserId");
            parameters.Add("ActorUserId", actor.UserId.ToString("D"));
        }
        else if (request.CreatedByUserId.HasValue && request.CreatedByUserId.Value != Guid.Empty)
        {
            sqlFilter.AppendLine("AND t.created_by_user_id = @CreatedByUserId");
            parameters.Add("CreatedByUserId", request.CreatedByUserId.Value.ToString("D"));
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = NormalizeStatus(request.Status);
            sqlFilter.AppendLine("AND t.status = @Status");
            parameters.Add("Status", status);
        }

        if (!string.IsNullOrWhiteSpace(request.Priority))
        {
            var priority = NormalizePriority(request.Priority);
            sqlFilter.AppendLine("AND t.priority = @Priority");
            parameters.Add("Priority", priority);
        }

        if (request.CategoryId.HasValue && request.CategoryId.Value != Guid.Empty)
        {
            sqlFilter.AppendLine("AND t.category_id = @CategoryId");
            parameters.Add("CategoryId", request.CategoryId.Value.ToString("D"));
        }

        if (request.AssignedStaffUserId.HasValue && request.AssignedStaffUserId.Value != Guid.Empty)
        {
            sqlFilter.AppendLine("AND t.assigned_staff_user_id = @AssignedStaffUserId");
            parameters.Add("AssignedStaffUserId", request.AssignedStaffUserId.Value.ToString("D"));
        }

        if (!string.IsNullOrWhiteSpace(request.Query))
        {
            var search = $"%{request.Query.Trim()}%";
            sqlFilter.AppendLine(
                """
                AND (
                    t.subject LIKE @Search
                    OR c.name LIKE @Search
                    OR EXISTS (
                        SELECT 1
                        FROM ticket_messages tm
                        WHERE tm.ticket_id = t.id
                          AND tm.body LIKE @Search
                    )
                )
                """);
            parameters.Add("Search", search);
        }

        parameters.Add("Offset", offset);
        parameters.Add("PageSize", pageSize);

        var countSql = $"SELECT COUNT(*) {sqlFilter}";
        var itemsSql = $"""
            SELECT
                t.id AS Id,
                t.created_by_user_id AS CreatedByUserId,
                t.created_by_display_name AS CreatedByDisplayName,
                t.subject AS Subject,
                t.category_id AS CategoryId,
                c.name AS CategoryName,
                c.description AS CategoryDescription,
                t.priority AS Priority,
                t.status AS Status,
                t.assigned_staff_user_id AS AssignedStaffUserId,
                t.assigned_staff_display_name AS AssignedStaffDisplayName,
                t.created_at_utc AS CreatedAtUtc,
                t.updated_at_utc AS UpdatedAtUtc,
                t.closed_at_utc AS ClosedAtUtc,
                t.last_message_at_utc AS LastMessageAtUtc,
                COALESCE(message_counts.message_count, 0) AS MessageCount
            {sqlFilter}
            ORDER BY t.updated_at_utc DESC, t.created_at_utc DESC
            LIMIT @Offset, @PageSize
            """;

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var totalCount = await connection.ExecuteScalarAsync<long>(new CommandDefinition(countSql, parameters, cancellationToken: cancellationToken));
        var items = (await connection.QueryAsync<TicketRow>(new CommandDefinition(itemsSql, parameters, cancellationToken: cancellationToken)))
            .Select(TicketRowMapper.MapTicketSummary)
            .ToArray();

        return new PagedResponse<TicketSummaryResponse>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
        };
    }

    public async Task<TicketDetailResponse> GetByIdAsync(Guid ticketId, CancellationToken cancellationToken)
    {
        var actor = _actorAccessor.GetRequiredActor();
        return await GetTicketDetailCoreAsync(ticketId, actor, enforceAccess: true, includeInternalArtifacts: actor.HasPermission(TicketPermissions.ViewInternalNotes), cancellationToken: cancellationToken);
    }

    public async Task<TicketMessageResponse> AddMessageAsync(Guid ticketId, AddTicketMessageRequest request, CancellationToken cancellationToken)
    {
        var actor = _actorAccessor.GetRequiredActor();
        var body = NormalizeBody(request.Body, "body");

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var ticket = await GetTicketRowAsync(connection, ticketId, transaction, cancellationToken)
            ?? throw ApiException.NotFound("Ticket was not found.", "ticket_not_found");

        if (string.Equals(ticket.Status, TicketStatuses.Closed, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Conflict("Closed tickets cannot receive new messages.", "ticket_closed");
        }

        var isOwner = ticket.CreatedByUserId == actor.UserId;
        var canReplyAsOwner = isOwner && actor.HasPermission(TicketPermissions.ReplyOwn);
        var canReplyAsStaff = actor.HasPermission(TicketPermissions.ReplyStaff);

        if (!canReplyAsOwner && !canReplyAsStaff)
        {
            throw ApiException.Forbidden("The current actor is not allowed to reply to this ticket.", "ticket_reply_forbidden");
        }

        var isStaffReply = canReplyAsStaff && actor.IsStaff;
        var messageId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await connection.ExecuteAsync(new CommandDefinition(
            commandText: """
                INSERT INTO ticket_messages (
                    id,
                    ticket_id,
                    author_user_id,
                    author_display_name,
                    author_role,
                    is_staff_reply,
                    body,
                    created_at_utc
                ) VALUES (
                    @Id,
                    @TicketId,
                    @AuthorUserId,
                    @AuthorDisplayName,
                    @AuthorRole,
                    @IsStaffReply,
                    @Body,
                    @CreatedAtUtc
                )
                """,
            parameters: new
            {
                Id = messageId.ToString("D"),
                TicketId = ticketId.ToString("D"),
                AuthorUserId = actor.UserId.ToString("D"),
                AuthorDisplayName = actor.DisplayName,
                AuthorRole = actor.Role,
                IsStaffReply = isStaffReply,
                Body = body,
                CreatedAtUtc = now,
            },
            transaction: transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            commandText: """
                UPDATE tickets
                SET updated_at_utc = @UpdatedAtUtc,
                    last_message_at_utc = @LastMessageAtUtc
                WHERE id = @TicketId
                """,
            parameters: new
            {
                TicketId = ticketId.ToString("D"),
                UpdatedAtUtc = now,
                LastMessageAtUtc = now,
            },
            transaction: transaction,
            cancellationToken: cancellationToken));

        await InsertAuditLogAsync(
            connection,
            transaction,
            ticketId,
            actor,
            TicketAuditActions.MessageAdded,
            isInternal: false,
            details: new
            {
                messageId,
                isStaffReply,
            },
            cancellationToken: cancellationToken);

        await EnqueueOutboxEventAsync(
            connection,
            transaction,
            subject: TicketAuditActions.MessageAdded,
            eventType: TicketAuditActions.MessageAdded,
            actor: actor,
            payload: new
            {
                ticketId,
                messageId,
                isStaffReply,
                author = new
                {
                    actor.UserId,
                    actor.DisplayName,
                    actor.Role,
                },
                createdAtUtc = now,
            },
            cancellationToken: cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new TicketMessageResponse
        {
            Id = messageId,
            Author = new ActorReferenceResponse
            {
                UserId = actor.UserId,
                DisplayName = actor.DisplayName,
                Role = actor.Role,
            },
            IsStaffReply = isStaffReply,
            Body = body,
            CreatedAtUtc = now,
        };
    }

    public async Task<TicketAssignmentResponse> AssignAsync(Guid ticketId, AssignTicketRequest request, CancellationToken cancellationToken)
    {
        var actor = _actorAccessor.GetRequiredActor();
        EnsurePermission(actor, TicketPermissions.Assign);

        var assigneeUserId = request.AssigneeUserId.GetValueOrDefault(actor.UserId);
        if (assigneeUserId == Guid.Empty)
        {
            throw ApiException.BadRequest("AssigneeUserId must be a non-empty GUID when specified.", "assignee_required");
        }

        var assigneeDisplayName = string.IsNullOrWhiteSpace(request.AssigneeDisplayName)
            ? assigneeUserId == actor.UserId
                ? actor.DisplayName
                : assigneeUserId.ToString("D")
            : request.AssigneeDisplayName.Trim();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var ticket = await GetTicketRowAsync(connection, ticketId, transaction, cancellationToken)
            ?? throw ApiException.NotFound("Ticket was not found.", "ticket_not_found");

        if (ticket.AssignedStaffUserId == assigneeUserId &&
            string.Equals(ticket.AssignedStaffDisplayName, assigneeDisplayName, StringComparison.Ordinal))
        {
            throw ApiException.Conflict("The ticket is already assigned to the specified staff member.", "ticket_already_assigned");
        }

        var now = DateTime.UtcNow;
        var assignmentId = Guid.NewGuid();

        await connection.ExecuteAsync(new CommandDefinition(
            commandText: """
                UPDATE ticket_assignments
                SET is_active = 0,
                    unassigned_at_utc = @UnassignedAtUtc
                WHERE ticket_id = @TicketId
                  AND is_active = 1
                """,
            parameters: new
            {
                TicketId = ticketId.ToString("D"),
                UnassignedAtUtc = now,
            },
            transaction: transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            commandText: """
                INSERT INTO ticket_assignments (
                    id,
                    ticket_id,
                    assigned_staff_user_id,
                    assigned_staff_display_name,
                    assigned_by_user_id,
                    assigned_by_display_name,
                    is_active,
                    assigned_at_utc,
                    unassigned_at_utc
                ) VALUES (
                    @Id,
                    @TicketId,
                    @AssignedStaffUserId,
                    @AssignedStaffDisplayName,
                    @AssignedByUserId,
                    @AssignedByDisplayName,
                    1,
                    @AssignedAtUtc,
                    NULL
                )
                """,
            parameters: new
            {
                Id = assignmentId.ToString("D"),
                TicketId = ticketId.ToString("D"),
                AssignedStaffUserId = assigneeUserId.ToString("D"),
                AssignedStaffDisplayName = assigneeDisplayName,
                AssignedByUserId = actor.UserId.ToString("D"),
                AssignedByDisplayName = actor.DisplayName,
                AssignedAtUtc = now,
            },
            transaction: transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            commandText: """
                UPDATE tickets
                SET assigned_staff_user_id = @AssignedStaffUserId,
                    assigned_staff_display_name = @AssignedStaffDisplayName,
                    updated_at_utc = @UpdatedAtUtc
                WHERE id = @TicketId
                """,
            parameters: new
            {
                TicketId = ticketId.ToString("D"),
                AssignedStaffUserId = assigneeUserId.ToString("D"),
                AssignedStaffDisplayName = assigneeDisplayName,
                UpdatedAtUtc = now,
            },
            transaction: transaction,
            cancellationToken: cancellationToken));

        await InsertAuditLogAsync(
            connection,
            transaction,
            ticketId,
            actor,
            TicketAuditActions.Assigned,
            isInternal: true,
            details: new
            {
                assigneeUserId,
                assigneeDisplayName,
                previousAssignedStaffUserId = ticket.AssignedStaffUserId,
                previousAssignedStaffDisplayName = ticket.AssignedStaffDisplayName,
            },
            cancellationToken: cancellationToken);

        await EnqueueOutboxEventAsync(
            connection,
            transaction,
            subject: TicketAuditActions.Assigned,
            eventType: TicketAuditActions.Assigned,
            actor: actor,
            payload: new
            {
                ticketId,
                assignmentId,
                assignee = new
                {
                    userId = assigneeUserId,
                    displayName = assigneeDisplayName,
                },
                assignedBy = new
                {
                    actor.UserId,
                    actor.DisplayName,
                    actor.Role,
                },
                assignedAtUtc = now,
            },
            cancellationToken: cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new TicketAssignmentResponse
        {
            Id = assignmentId,
            AssignedStaff = new ActorReferenceResponse
            {
                UserId = assigneeUserId,
                DisplayName = assigneeDisplayName,
                Role = "staff",
            },
            AssignedBy = new ActorReferenceResponse
            {
                UserId = actor.UserId,
                DisplayName = actor.DisplayName,
                Role = actor.Role,
            },
            IsActive = true,
            AssignedAtUtc = now,
            UnassignedAtUtc = null,
        };
    }

    public async Task<TicketDetailResponse> ChangeStatusAsync(Guid ticketId, ChangeTicketStatusRequest request, CancellationToken cancellationToken)
    {
        var actor = _actorAccessor.GetRequiredActor();
        EnsurePermission(actor, TicketPermissions.ChangeStatus);

        var normalizedStatus = NormalizeStatus(request.Status);
        var reason = NormalizeOptionalText(request.Reason, 512);

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var ticket = await GetTicketRowAsync(connection, ticketId, transaction, cancellationToken)
            ?? throw ApiException.NotFound("Ticket was not found.", "ticket_not_found");

        if (string.Equals(ticket.Status, normalizedStatus, StringComparison.OrdinalIgnoreCase))
        {
            throw ApiException.Conflict("The ticket already has the specified status.", "ticket_status_unchanged");
        }

        var now = DateTime.UtcNow;
        DateTime? closedAtUtc = string.Equals(normalizedStatus, TicketStatuses.Closed, StringComparison.OrdinalIgnoreCase)
            ? now
            : null;

        await connection.ExecuteAsync(new CommandDefinition(
            commandText: """
                UPDATE tickets
                SET status = @Status,
                    updated_at_utc = @UpdatedAtUtc,
                    closed_at_utc = @ClosedAtUtc
                WHERE id = @TicketId
                """,
            parameters: new
            {
                TicketId = ticketId.ToString("D"),
                Status = normalizedStatus,
                UpdatedAtUtc = now,
                ClosedAtUtc = closedAtUtc,
            },
            transaction: transaction,
            cancellationToken: cancellationToken));

        await InsertAuditLogAsync(
            connection,
            transaction,
            ticketId,
            actor,
            TicketAuditActions.StatusChanged,
            isInternal: false,
            details: new
            {
                previousStatus = ticket.Status,
                newStatus = normalizedStatus,
                reason,
            },
            cancellationToken: cancellationToken);

        await EnqueueOutboxEventAsync(
            connection,
            transaction,
            subject: TicketAuditActions.StatusChanged,
            eventType: TicketAuditActions.StatusChanged,
            actor: actor,
            payload: new
            {
                ticketId,
                previousStatus = ticket.Status,
                newStatus = normalizedStatus,
                changedBy = new
                {
                    actor.UserId,
                    actor.DisplayName,
                    actor.Role,
                },
                reason,
                changedAtUtc = now,
            },
            cancellationToken: cancellationToken);

        if (string.Equals(normalizedStatus, TicketStatuses.Closed, StringComparison.OrdinalIgnoreCase))
        {
            await EnqueueOutboxEventAsync(
                connection,
                transaction,
                subject: TicketAuditActions.Closed,
                eventType: TicketAuditActions.Closed,
                actor: actor,
                payload: new
                {
                    ticketId,
                    closedBy = new
                    {
                        actor.UserId,
                        actor.DisplayName,
                        actor.Role,
                    },
                    closedAtUtc = now,
                    reason,
                },
                cancellationToken: cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return await GetTicketDetailCoreAsync(ticketId, actor, enforceAccess: false, includeInternalArtifacts: actor.HasPermission(TicketPermissions.ViewInternalNotes), cancellationToken: cancellationToken);
    }

    public async Task<TicketInternalNoteResponse> AddInternalNoteAsync(Guid ticketId, AddInternalNoteRequest request, CancellationToken cancellationToken)
    {
        var actor = _actorAccessor.GetRequiredActor();
        EnsurePermission(actor, TicketPermissions.ReplyStaff);
        EnsurePermission(actor, TicketPermissions.ViewInternalNotes);

        var body = NormalizeBody(request.Body, "body");
        var noteId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        _ = await GetTicketRowAsync(connection, ticketId, transaction, cancellationToken)
            ?? throw ApiException.NotFound("Ticket was not found.", "ticket_not_found");

        await connection.ExecuteAsync(new CommandDefinition(
            commandText: """
                INSERT INTO ticket_internal_notes (
                    id,
                    ticket_id,
                    author_user_id,
                    author_display_name,
                    body,
                    created_at_utc
                ) VALUES (
                    @Id,
                    @TicketId,
                    @AuthorUserId,
                    @AuthorDisplayName,
                    @Body,
                    @CreatedAtUtc
                )
                """,
            parameters: new
            {
                Id = noteId.ToString("D"),
                TicketId = ticketId.ToString("D"),
                AuthorUserId = actor.UserId.ToString("D"),
                AuthorDisplayName = actor.DisplayName,
                Body = body,
                CreatedAtUtc = now,
            },
            transaction: transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            commandText: """
                UPDATE tickets
                SET updated_at_utc = @UpdatedAtUtc
                WHERE id = @TicketId
                """,
            parameters: new
            {
                TicketId = ticketId.ToString("D"),
                UpdatedAtUtc = now,
            },
            transaction: transaction,
            cancellationToken: cancellationToken));

        await InsertAuditLogAsync(
            connection,
            transaction,
            ticketId,
            actor,
            TicketAuditActions.InternalNoteAdded,
            isInternal: true,
            details: new
            {
                noteId,
                preview = body.Length <= 80 ? body : body[..80],
            },
            cancellationToken: cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new TicketInternalNoteResponse
        {
            Id = noteId,
            Author = new ActorReferenceResponse
            {
                UserId = actor.UserId,
                DisplayName = actor.DisplayName,
                Role = actor.Role,
            },
            Body = body,
            CreatedAtUtc = now,
        };
    }

    private async Task<TicketDetailResponse> GetTicketDetailCoreAsync(
        Guid ticketId,
        RequestActor actor,
        bool enforceAccess,
        bool includeInternalArtifacts,
        CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

        var ticket = await GetTicketRowAsync(connection, ticketId, transaction: null, cancellationToken: cancellationToken)
            ?? throw ApiException.NotFound("Ticket was not found.", "ticket_not_found");

        if (enforceAccess)
        {
            EnsureTicketReadable(actor, ticket);
        }

        var messages = (await connection.QueryAsync<TicketMessageRow>(new CommandDefinition(
            commandText: """
                SELECT
                    id AS Id,
                    ticket_id AS TicketId,
                    author_user_id AS AuthorUserId,
                    author_display_name AS AuthorDisplayName,
                    author_role AS AuthorRole,
                    is_staff_reply AS IsStaffReply,
                    body AS Body,
                    created_at_utc AS CreatedAtUtc
                FROM ticket_messages
                WHERE ticket_id = @TicketId
                ORDER BY created_at_utc ASC
                """,
            parameters: new { TicketId = ticketId.ToString("D") },
            cancellationToken: cancellationToken))).ToArray();

        var assignments = includeInternalArtifacts
            ? (await connection.QueryAsync<TicketAssignmentRow>(new CommandDefinition(
                commandText: """
                    SELECT
                        id AS Id,
                        ticket_id AS TicketId,
                        assigned_staff_user_id AS AssignedStaffUserId,
                        assigned_staff_display_name AS AssignedStaffDisplayName,
                        assigned_by_user_id AS AssignedByUserId,
                        assigned_by_display_name AS AssignedByDisplayName,
                        is_active AS IsActive,
                        assigned_at_utc AS AssignedAtUtc,
                        unassigned_at_utc AS UnassignedAtUtc
                    FROM ticket_assignments
                    WHERE ticket_id = @TicketId
                    ORDER BY assigned_at_utc ASC
                    """,
                parameters: new { TicketId = ticketId.ToString("D") },
                cancellationToken: cancellationToken))).ToArray()
            : [];

        var internalNotes = includeInternalArtifacts
            ? (await connection.QueryAsync<TicketInternalNoteRow>(new CommandDefinition(
                commandText: """
                    SELECT
                        id AS Id,
                        ticket_id AS TicketId,
                        author_user_id AS AuthorUserId,
                        author_display_name AS AuthorDisplayName,
                        body AS Body,
                        created_at_utc AS CreatedAtUtc
                    FROM ticket_internal_notes
                    WHERE ticket_id = @TicketId
                    ORDER BY created_at_utc ASC
                    """,
                parameters: new { TicketId = ticketId.ToString("D") },
                cancellationToken: cancellationToken))).ToArray()
            : [];

        var auditTrail = (await connection.QueryAsync<TicketAuditLogRow>(new CommandDefinition(
            commandText: includeInternalArtifacts
                ? """
                    SELECT
                        id AS Id,
                        ticket_id AS TicketId,
                        actor_user_id AS ActorUserId,
                        actor_display_name AS ActorDisplayName,
                        actor_role AS ActorRole,
                        action_type AS ActionType,
                        is_internal AS IsInternal,
                        details_json AS DetailsJson,
                        created_at_utc AS CreatedAtUtc
                    FROM ticket_audit_logs
                    WHERE ticket_id = @TicketId
                    ORDER BY created_at_utc ASC
                    """
                : """
                    SELECT
                        id AS Id,
                        ticket_id AS TicketId,
                        actor_user_id AS ActorUserId,
                        actor_display_name AS ActorDisplayName,
                        actor_role AS ActorRole,
                        action_type AS ActionType,
                        is_internal AS IsInternal,
                        details_json AS DetailsJson,
                        created_at_utc AS CreatedAtUtc
                    FROM ticket_audit_logs
                    WHERE ticket_id = @TicketId
                      AND is_internal = 0
                    ORDER BY created_at_utc ASC
                    """,
            parameters: new { TicketId = ticketId.ToString("D") },
            cancellationToken: cancellationToken))).ToArray();

        return new TicketDetailResponse
        {
            Id = ticket.Id,
            Subject = ticket.Subject,
            Status = ticket.Status,
            Priority = ticket.Priority,
            Category = TicketRowMapper.MapCategoryReference(ticket),
            CreatedBy = new ActorReferenceResponse
            {
                UserId = ticket.CreatedByUserId,
                DisplayName = ticket.CreatedByDisplayName,
            },
            AssignedTo = ticket.AssignedStaffUserId is null
                ? null
                : new ActorReferenceResponse
                {
                    UserId = ticket.AssignedStaffUserId.Value,
                    DisplayName = ticket.AssignedStaffDisplayName ?? ticket.AssignedStaffUserId.Value.ToString("D"),
                    Role = "staff",
                },
            CreatedAtUtc = ticket.CreatedAtUtc,
            UpdatedAtUtc = ticket.UpdatedAtUtc,
            ClosedAtUtc = ticket.ClosedAtUtc,
            LastMessageAtUtc = ticket.LastMessageAtUtc,
            MessageCount = ticket.MessageCount,
            Messages = messages.Select(TicketRowMapper.MapMessage).ToArray(),
            Assignments = assignments.Select(TicketRowMapper.MapAssignment).ToArray(),
            InternalNotes = internalNotes.Select(TicketRowMapper.MapInternalNote).ToArray(),
            AuditTrail = auditTrail.Select(TicketRowMapper.MapAudit).ToArray(),
        };
    }

    private static void EnsureTicketReadable(RequestActor actor, TicketRow ticket)
    {
        if (actor.HasPermission(TicketPermissions.ReadAll))
        {
            return;
        }

        if (actor.HasPermission(TicketPermissions.ReadOwn) && ticket.CreatedByUserId == actor.UserId)
        {
            return;
        }

        throw ApiException.Forbidden("The current actor is not allowed to view this ticket.", "ticket_read_forbidden");
    }

    private static void EnsurePermission(RequestActor actor, string permission)
    {
        if (!actor.HasPermission(permission))
        {
            throw ApiException.Forbidden($"The current actor does not have required permission '{permission}'.", "permission_missing");
        }
    }

    private static string NormalizeSubject(string subject)
    {
        var normalized = subject?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw ApiException.BadRequest("Subject is required.", "subject_required");
        }

        return normalized.Length > 200 ? normalized[..200] : normalized;
    }

    private static string NormalizeBody(string body, string fieldName)
    {
        var normalized = body?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw ApiException.BadRequest($"{fieldName} is required.", $"{fieldName}_required");
        }

        return normalized.Length > 4000 ? normalized[..4000] : normalized;
    }

    private static string? NormalizeOptionalText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length > maxLength ? normalized[..maxLength] : normalized;
    }

    private static string NormalizeStatus(string status)
    {
        var normalized = status?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || !TicketStatuses.All.Contains(normalized))
        {
            throw ApiException.BadRequest("The specified ticket status is invalid.", "invalid_ticket_status", new { allowed = TicketStatuses.All });
        }

        return normalized;
    }

    private static string NormalizePriority(string priority)
    {
        var normalized = priority?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) || !TicketPriorities.All.Contains(normalized))
        {
            throw ApiException.BadRequest("The specified ticket priority is invalid.", "invalid_ticket_priority", new { allowed = TicketPriorities.All });
        }

        return normalized;
    }

    private async Task<TicketRow?> GetTicketRowAsync(IDbConnection connection, Guid ticketId, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return await connection.QuerySingleOrDefaultAsync<TicketRow>(new CommandDefinition(
            commandText: """
                SELECT
                    t.id AS Id,
                    t.created_by_user_id AS CreatedByUserId,
                    t.created_by_display_name AS CreatedByDisplayName,
                    t.subject AS Subject,
                    t.category_id AS CategoryId,
                    c.name AS CategoryName,
                    c.description AS CategoryDescription,
                    t.priority AS Priority,
                    t.status AS Status,
                    t.assigned_staff_user_id AS AssignedStaffUserId,
                    t.assigned_staff_display_name AS AssignedStaffDisplayName,
                    t.created_at_utc AS CreatedAtUtc,
                    t.updated_at_utc AS UpdatedAtUtc,
                    t.closed_at_utc AS ClosedAtUtc,
                    t.last_message_at_utc AS LastMessageAtUtc,
                    (
                        SELECT COUNT(*)
                        FROM ticket_messages tm
                        WHERE tm.ticket_id = t.id
                    ) AS MessageCount
                FROM tickets t
                INNER JOIN ticket_categories c ON c.id = t.category_id
                WHERE t.id = @TicketId
                LIMIT 1
                """,
            parameters: new { TicketId = ticketId.ToString("D") },
            transaction: transaction,
            cancellationToken: cancellationToken));
    }

    private static async Task<TicketCategoryRow?> GetCategoryByIdAsync(IDbConnection connection, Guid categoryId, IDbTransaction? transaction, CancellationToken cancellationToken)
    {
        return await connection.QuerySingleOrDefaultAsync<TicketCategoryRow>(new CommandDefinition(
            commandText: """
                SELECT
                    id AS Id,
                    name AS Name,
                    description AS Description,
                    is_active AS IsActive,
                    display_order AS DisplayOrder,
                    created_at_utc AS CreatedAtUtc,
                    updated_at_utc AS UpdatedAtUtc
                FROM ticket_categories
                WHERE id = @CategoryId
                LIMIT 1
                """,
            parameters: new { CategoryId = categoryId.ToString("D") },
            transaction: transaction,
            cancellationToken: cancellationToken));
    }

    private async Task EnqueueOutboxEventAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string subject,
        string eventType,
        RequestActor actor,
        object payload,
        CancellationToken cancellationToken)
    {
        var occurredAt = DateTime.UtcNow;
        var envelope = new EventEnvelope
        {
            EventId = Guid.NewGuid(),
            EventType = eventType,
            Version = string.IsNullOrWhiteSpace(_natsOptions.Value.EventVersion) ? "1.0" : _natsOptions.Value.EventVersion,
            OccurredAt = occurredAt,
            CorrelationId = actor.CorrelationId,
            CausationId = actor.CausationId,
            Payload = JsonSerializer.SerializeToElement(payload, JsonDefaults.SerializerOptions),
        };

        var envelopeJson = JsonSerializer.Serialize(envelope, JsonDefaults.SerializerOptions);

        await connection.ExecuteAsync(new CommandDefinition(
            commandText: """
                INSERT INTO ticket_outbox_events (
                    id,
                    subject,
                    event_type,
                    envelope_json,
                    occurred_at_utc,
                    published_at_utc,
                    attempt_count,
                    last_error
                ) VALUES (
                    @Id,
                    @Subject,
                    @EventType,
                    @EnvelopeJson,
                    @OccurredAtUtc,
                    NULL,
                    0,
                    NULL
                )
                """,
            parameters: new
            {
                Id = envelope.EventId.ToString("D"),
                Subject = subject,
                EventType = eventType,
                EnvelopeJson = envelopeJson,
                OccurredAtUtc = occurredAt,
            },
            transaction: transaction,
            cancellationToken: cancellationToken));
    }

    private static async Task InsertAuditLogAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Guid ticketId,
        RequestActor actor,
        string actionType,
        bool isInternal,
        object details,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(new CommandDefinition(
            commandText: """
                INSERT INTO ticket_audit_logs (
                    id,
                    ticket_id,
                    actor_user_id,
                    actor_display_name,
                    actor_role,
                    action_type,
                    is_internal,
                    details_json,
                    created_at_utc
                ) VALUES (
                    @Id,
                    @TicketId,
                    @ActorUserId,
                    @ActorDisplayName,
                    @ActorRole,
                    @ActionType,
                    @IsInternal,
                    @DetailsJson,
                    @CreatedAtUtc
                )
                """,
            parameters: new
            {
                Id = Guid.NewGuid().ToString("D"),
                TicketId = ticketId.ToString("D"),
                ActorUserId = actor.UserId.ToString("D"),
                ActorDisplayName = actor.DisplayName,
                ActorRole = actor.Role,
                ActionType = actionType,
                IsInternal = isInternal,
                DetailsJson = JsonSerializer.Serialize(details, JsonDefaults.SerializerOptions),
                CreatedAtUtc = DateTime.UtcNow,
            },
            transaction: transaction,
            cancellationToken: cancellationToken));
    }

    private static TicketSummaryResponse MapTicketSummary(TicketRow ticket) => TicketRowMapper.MapTicketSummary(ticket);
}
