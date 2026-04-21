using Dapper;
using ExiledCms.TicketsService.Api.Contracts;
using ExiledCms.TicketsService.Api.Domain;
using ExiledCms.TicketsService.Api.Infrastructure;

namespace ExiledCms.TicketsService.Api.Services;

public interface ITicketCategoryService
{
    Task<IReadOnlyCollection<TicketCategoryResponse>> ListAsync(CancellationToken cancellationToken);

    Task<TicketCategoryResponse> CreateAsync(CreateTicketCategoryRequest request, CancellationToken cancellationToken);
}

public sealed class TicketCategoryService : ITicketCategoryService
{
    private readonly MySqlConnectionFactory _connectionFactory;
    private readonly IRequestActorAccessor _actorAccessor;

    public TicketCategoryService(MySqlConnectionFactory connectionFactory, IRequestActorAccessor actorAccessor)
    {
        _connectionFactory = connectionFactory;
        _actorAccessor = actorAccessor;
    }

    public async Task<IReadOnlyCollection<TicketCategoryResponse>> ListAsync(CancellationToken cancellationToken)
    {
        var actor = _actorAccessor.GetRequiredActor();
        var canManage = actor.HasPermission(TicketPermissions.ManageCategories);
        var canRead = actor.HasPermission(TicketPermissions.Create) || actor.HasPermission(TicketPermissions.ReadOwn) || actor.HasPermission(TicketPermissions.ReadAll) || canManage;

        if (!canRead)
        {
            throw ApiException.Forbidden("The current actor is not allowed to list ticket categories.", "ticket_category_read_forbidden");
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var sql = canManage
            ? """
                SELECT
                    id AS Id,
                    name AS Name,
                    description AS Description,
                    is_active AS IsActive,
                    display_order AS DisplayOrder,
                    created_at_utc AS CreatedAtUtc,
                    updated_at_utc AS UpdatedAtUtc
                FROM ticket_categories
                ORDER BY display_order ASC, name ASC
                """
            : """
                SELECT
                    id AS Id,
                    name AS Name,
                    description AS Description,
                    is_active AS IsActive,
                    display_order AS DisplayOrder,
                    created_at_utc AS CreatedAtUtc,
                    updated_at_utc AS UpdatedAtUtc
                FROM ticket_categories
                WHERE is_active = 1
                ORDER BY display_order ASC, name ASC
                """;

        var rows = (await connection.QueryAsync<TicketCategoryRow>(new CommandDefinition(sql, cancellationToken: cancellationToken))).ToArray();
        return rows.Select(MapCategory).ToArray();
    }

    public async Task<TicketCategoryResponse> CreateAsync(CreateTicketCategoryRequest request, CancellationToken cancellationToken)
    {
        var actor = _actorAccessor.GetRequiredActor();
        if (!actor.HasPermission(TicketPermissions.ManageCategories))
        {
            throw ApiException.Forbidden("The current actor is not allowed to manage ticket categories.", "ticket_category_manage_forbidden");
        }

        var name = NormalizeName(request.Name);
        var description = NormalizeOptionalText(request.Description, 512);
        var now = DateTime.UtcNow;
        var categoryId = Guid.NewGuid();

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var exists = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            commandText: "SELECT COUNT(*) FROM ticket_categories WHERE name = @Name",
            parameters: new { Name = name },
            cancellationToken: cancellationToken));

        if (exists > 0)
        {
            throw ApiException.Conflict("A ticket category with the same name already exists.", "ticket_category_duplicate");
        }

        await connection.ExecuteAsync(new CommandDefinition(
            commandText: """
                INSERT INTO ticket_categories (
                    id,
                    name,
                    description,
                    is_active,
                    display_order,
                    created_at_utc,
                    updated_at_utc
                ) VALUES (
                    @Id,
                    @Name,
                    @Description,
                    @IsActive,
                    @DisplayOrder,
                    @CreatedAtUtc,
                    @UpdatedAtUtc
                )
                """,
            parameters: new
            {
                Id = categoryId.ToString("D"),
                Name = name,
                Description = description,
                IsActive = request.IsActive,
                DisplayOrder = request.DisplayOrder,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            },
            cancellationToken: cancellationToken));

        return new TicketCategoryResponse
        {
            Id = categoryId,
            Name = name,
            Description = description,
            IsActive = request.IsActive,
            DisplayOrder = request.DisplayOrder,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
    }

    private static TicketCategoryResponse MapCategory(TicketCategoryRow category) => new()
    {
        Id = category.Id,
        Name = category.Name,
        Description = category.Description,
        IsActive = category.IsActive,
        DisplayOrder = category.DisplayOrder,
        CreatedAtUtc = category.CreatedAtUtc,
        UpdatedAtUtc = category.UpdatedAtUtc,
    };

    private static string NormalizeName(string name)
    {
        var normalized = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw ApiException.BadRequest("Category name is required.", "ticket_category_name_required");
        }

        return normalized.Length > 120 ? normalized[..120] : normalized;
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
}
