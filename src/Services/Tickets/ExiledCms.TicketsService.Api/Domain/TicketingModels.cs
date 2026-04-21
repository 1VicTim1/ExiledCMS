using System.Text.Json;

namespace ExiledCms.TicketsService.Api.Domain;

public static class TicketStatuses
{
    public const string Open = "open";
    public const string InProgress = "in_progress";
    public const string WaitingUser = "waiting_user";
    public const string Resolved = "resolved";
    public const string Closed = "closed";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Open,
        InProgress,
        WaitingUser,
        Resolved,
        Closed,
    };
}

public static class TicketPriorities
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";
    public const string Urgent = "urgent";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Low,
        Medium,
        High,
        Urgent,
    };
}

public static class TicketPermissions
{
    public const string Create = "ticket.create";
    public const string ReadOwn = "ticket.read.own";
    public const string ReadAll = "ticket.read.all";
    public const string ReplyOwn = "ticket.reply.own";
    public const string ReplyStaff = "ticket.reply.staff";
    public const string Assign = "ticket.assign";
    public const string ChangeStatus = "ticket.change_status";
    public const string ManageCategories = "ticket.manage_categories";
    public const string ViewInternalNotes = "ticket.view_internal_notes";
}

public static class TicketAuditActions
{
    public const string TicketCreated = "ticket.created";
    public const string MessageAdded = "ticket.message.added";
    public const string Assigned = "ticket.assigned";
    public const string StatusChanged = "ticket.status.changed";
    public const string Closed = "ticket.closed";
    public const string InternalNoteAdded = "ticket.internal_note.added";
}

public sealed class TicketRow
{
    public string Id { get; init; } = string.Empty;
    public string CreatedByUserId { get; init; } = string.Empty;
    public string CreatedByDisplayName { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string CategoryId { get; init; } = string.Empty;
    public string CategoryName { get; init; } = string.Empty;
    public string? CategoryDescription { get; init; }
    public string Priority { get; init; } = TicketPriorities.Medium;
    public string Status { get; init; } = TicketStatuses.Open;
    public string? AssignedStaffUserId { get; init; }
    public string? AssignedStaffDisplayName { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
    public DateTime? ClosedAtUtc { get; init; }
    public DateTime LastMessageAtUtc { get; init; }
    public int MessageCount { get; init; }
}

public sealed class TicketMessageRow
{
    public string Id { get; init; } = string.Empty;
    public string TicketId { get; init; } = string.Empty;
    public string AuthorUserId { get; init; } = string.Empty;
    public string AuthorDisplayName { get; init; } = string.Empty;
    public string AuthorRole { get; init; } = string.Empty;
    public bool IsStaffReply { get; init; }
    public string Body { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
}

public sealed class TicketAssignmentRow
{
    public string Id { get; init; } = string.Empty;
    public string TicketId { get; init; } = string.Empty;
    public string AssignedStaffUserId { get; init; } = string.Empty;
    public string AssignedStaffDisplayName { get; init; } = string.Empty;
    public string AssignedByUserId { get; init; } = string.Empty;
    public string AssignedByDisplayName { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public DateTime AssignedAtUtc { get; init; }
    public DateTime? UnassignedAtUtc { get; init; }
}

public sealed class TicketCategoryRow
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsActive { get; init; }
    public int DisplayOrder { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; init; }
}

public sealed class TicketInternalNoteRow
{
    public string Id { get; init; } = string.Empty;
    public string TicketId { get; init; } = string.Empty;
    public string AuthorUserId { get; init; } = string.Empty;
    public string AuthorDisplayName { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public DateTime CreatedAtUtc { get; init; }
}

public sealed class TicketAuditLogRow
{
    public string Id { get; init; } = string.Empty;
    public string TicketId { get; init; } = string.Empty;
    public string ActorUserId { get; init; } = string.Empty;
    public string ActorDisplayName { get; init; } = string.Empty;
    public string ActorRole { get; init; } = string.Empty;
    public string ActionType { get; init; } = string.Empty;
    public bool IsInternal { get; init; }
    public string DetailsJson { get; init; } = "{}";
    public DateTime CreatedAtUtc { get; init; }
}

public sealed class OutboxEventRow
{
    public string Id { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string EnvelopeJson { get; init; } = "{}";
}

public sealed class EventEnvelope
{
    public Guid EventId { get; init; }
    public string EventType { get; init; } = string.Empty;
    public string Version { get; init; } = "1.0";
    public DateTime OccurredAt { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public string? CausationId { get; init; }
    public JsonElement Payload { get; init; }
}
