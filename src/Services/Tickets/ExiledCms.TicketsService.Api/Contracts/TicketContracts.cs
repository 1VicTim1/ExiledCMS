using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace ExiledCms.TicketsService.Api.Contracts;

public sealed class TicketCategoryReferenceResponse
{
    public Guid Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }
}

public sealed class CreateTicketRequest
{
    [Required]
    [MaxLength(200)]
    public string Subject { get; set; } = string.Empty;

    [Required]
    public Guid CategoryId { get; set; }

    [Required]
    [MaxLength(32)]
    public string Priority { get; set; } = "medium";

    [Required]
    [MaxLength(4000)]
    public string Message { get; set; } = string.Empty;
}

public sealed class AddTicketMessageRequest
{
    [Required]
    [MaxLength(4000)]
    public string Body { get; set; } = string.Empty;
}

public sealed class AssignTicketRequest
{
    public Guid? AssigneeUserId { get; set; }

    [MaxLength(160)]
    public string? AssigneeDisplayName { get; set; }
}

public sealed class ChangeTicketStatusRequest
{
    [Required]
    [MaxLength(32)]
    public string Status { get; set; } = string.Empty;

    [MaxLength(512)]
    public string? Reason { get; set; }
}

public sealed class AddInternalNoteRequest
{
    [Required]
    [MaxLength(4000)]
    public string Body { get; set; } = string.Empty;
}

public sealed class CreateTicketCategoryRequest
{
    [Required]
    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(512)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }
}

public sealed class TicketsQueryRequest
{
    public string? Status { get; set; }

    public string? Priority { get; set; }

    public Guid? CategoryId { get; set; }

    public Guid? AssignedStaffUserId { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public string? Query { get; set; }

    public int Page { get; set; } = 1;

    public int PageSize { get; set; } = 20;
}

public sealed class PagedResponse<T>
{
    public required IReadOnlyCollection<T> Items { get; init; }

    public int Page { get; init; }

    public int PageSize { get; init; }

    public long TotalCount { get; init; }
}

public sealed class ActorReferenceResponse
{
    public Guid UserId { get; init; }

    public required string DisplayName { get; init; }

    public string? Role { get; init; }
}

public sealed class TicketCategoryResponse
{
    public Guid Id { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    public bool IsActive { get; init; }

    public int DisplayOrder { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime UpdatedAtUtc { get; init; }
}

public class TicketSummaryResponse
{
    public Guid Id { get; init; }

    public required string Subject { get; init; }

    public required string Status { get; init; }

    public required string Priority { get; init; }

    public required TicketCategoryReferenceResponse Category { get; init; }

    public required ActorReferenceResponse CreatedBy { get; init; }

    public ActorReferenceResponse? AssignedTo { get; init; }

    public DateTime CreatedAtUtc { get; init; }

    public DateTime UpdatedAtUtc { get; init; }

    public DateTime? ClosedAtUtc { get; init; }

    public DateTime LastMessageAtUtc { get; init; }

    public int MessageCount { get; init; }
}

public sealed class TicketMessageResponse
{
    public Guid Id { get; init; }

    public required ActorReferenceResponse Author { get; init; }

    public bool IsStaffReply { get; init; }

    public required string Body { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}

public sealed class TicketAssignmentResponse
{
    public Guid Id { get; init; }

    public required ActorReferenceResponse AssignedStaff { get; init; }

    public required ActorReferenceResponse AssignedBy { get; init; }

    public bool IsActive { get; init; }

    public DateTime AssignedAtUtc { get; init; }

    public DateTime? UnassignedAtUtc { get; init; }
}

public sealed class TicketInternalNoteResponse
{
    public Guid Id { get; init; }

    public required ActorReferenceResponse Author { get; init; }

    public required string Body { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}

public sealed class TicketAuditLogResponse
{
    public Guid Id { get; init; }

    public required ActorReferenceResponse Actor { get; init; }

    public required string ActionType { get; init; }

    public bool IsInternal { get; init; }

    public JsonElement Details { get; init; }

    public DateTime CreatedAtUtc { get; init; }
}

public sealed class TicketDetailResponse : TicketSummaryResponse
{
    public required IReadOnlyCollection<TicketMessageResponse> Messages { get; init; }

    public required IReadOnlyCollection<TicketAssignmentResponse> Assignments { get; init; }

    public required IReadOnlyCollection<TicketInternalNoteResponse> InternalNotes { get; init; }

    public required IReadOnlyCollection<TicketAuditLogResponse> AuditTrail { get; init; }
}
