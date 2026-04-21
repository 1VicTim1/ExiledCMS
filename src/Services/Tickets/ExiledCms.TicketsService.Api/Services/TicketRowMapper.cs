using ExiledCms.TicketsService.Api.Contracts;
using ExiledCms.TicketsService.Api.Domain;
using ExiledCms.TicketsService.Api.Infrastructure;

namespace ExiledCms.TicketsService.Api.Services;

internal static class TicketRowMapper
{
    public static TicketSummaryResponse MapTicketSummary(TicketRow ticket) => new()
    {
        Id = ticket.Id,
        Subject = ticket.Subject,
        Status = ticket.Status,
        Priority = ticket.Priority,
        Category = MapCategoryReference(ticket),
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
    };

    public static TicketCategoryReferenceResponse MapCategoryReference(TicketRow ticket) => new()
    {
        Id = ticket.CategoryId,
        Name = ticket.CategoryName,
        Description = ticket.CategoryDescription,
    };

    public static TicketMessageResponse MapMessage(TicketMessageRow message) => new()
    {
        Id = message.Id,
        Author = new ActorReferenceResponse
        {
            UserId = message.AuthorUserId,
            DisplayName = message.AuthorDisplayName,
            Role = message.AuthorRole,
        },
        IsStaffReply = message.IsStaffReply,
        Body = message.Body,
        CreatedAtUtc = message.CreatedAtUtc,
    };

    public static TicketAssignmentResponse MapAssignment(TicketAssignmentRow assignment) => new()
    {
        Id = assignment.Id,
        AssignedStaff = new ActorReferenceResponse
        {
            UserId = assignment.AssignedStaffUserId,
            DisplayName = assignment.AssignedStaffDisplayName,
            Role = "staff",
        },
        AssignedBy = new ActorReferenceResponse
        {
            UserId = assignment.AssignedByUserId,
            DisplayName = assignment.AssignedByDisplayName,
            Role = "staff",
        },
        IsActive = assignment.IsActive,
        AssignedAtUtc = assignment.AssignedAtUtc,
        UnassignedAtUtc = assignment.UnassignedAtUtc,
    };

    public static TicketInternalNoteResponse MapInternalNote(TicketInternalNoteRow note) => new()
    {
        Id = note.Id,
        Author = new ActorReferenceResponse
        {
            UserId = note.AuthorUserId,
            DisplayName = note.AuthorDisplayName,
            Role = "staff",
        },
        Body = note.Body,
        CreatedAtUtc = note.CreatedAtUtc,
    };

    public static TicketAuditLogResponse MapAudit(TicketAuditLogRow audit) => new()
    {
        Id = audit.Id,
        Actor = new ActorReferenceResponse
        {
            UserId = audit.ActorUserId,
            DisplayName = audit.ActorDisplayName,
            Role = audit.ActorRole,
        },
        ActionType = audit.ActionType,
        IsInternal = audit.IsInternal,
        Details = JsonDefaults.ParseElement(audit.DetailsJson),
        CreatedAtUtc = audit.CreatedAtUtc,
    };
}
