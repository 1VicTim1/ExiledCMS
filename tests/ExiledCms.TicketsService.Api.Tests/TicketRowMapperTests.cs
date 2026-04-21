using ExiledCms.TicketsService.Api.Domain;
using ExiledCms.TicketsService.Api.Services;

namespace ExiledCms.TicketsService.Api.Tests;

public sealed class TicketRowMapperTests
{
    [Fact]
    public void MapTicketSummary_PreservesGuidIdentifiers()
    {
        var ticketId = Guid.NewGuid();
        var createdByUserId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var assignedStaffUserId = Guid.NewGuid();

        var summary = TicketRowMapper.MapTicketSummary(new TicketRow
        {
            Id = ticketId,
            CreatedByUserId = createdByUserId,
            CreatedByDisplayName = "Reporter",
            Subject = "Broken page",
            CategoryId = categoryId,
            CategoryName = "Support",
            CategoryDescription = "General support",
            Priority = TicketPriorities.High,
            Status = TicketStatuses.Open,
            AssignedStaffUserId = assignedStaffUserId,
            AssignedStaffDisplayName = "Moderator",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            LastMessageAtUtc = DateTime.UtcNow,
            MessageCount = 3,
        });

        Assert.Equal(ticketId, summary.Id);
        Assert.Equal(createdByUserId, summary.CreatedBy.UserId);
        Assert.Equal(categoryId, summary.Category.Id);
        Assert.NotNull(summary.AssignedTo);
        Assert.Equal(assignedStaffUserId, summary.AssignedTo!.UserId);
    }

    [Fact]
    public void MapAudit_ParsesJsonAndPreservesActorGuid()
    {
        var auditId = Guid.NewGuid();
        var actorUserId = Guid.NewGuid();

        var audit = TicketRowMapper.MapAudit(new TicketAuditLogRow
        {
            Id = auditId,
            TicketId = Guid.NewGuid(),
            ActorUserId = actorUserId,
            ActorDisplayName = "Admin",
            ActorRole = "admin",
            ActionType = TicketAuditActions.StatusChanged,
            IsInternal = true,
            DetailsJson = "{\"status\":\"closed\"}",
            CreatedAtUtc = DateTime.UtcNow,
        });

        Assert.Equal(auditId, audit.Id);
        Assert.Equal(actorUserId, audit.Actor.UserId);
        Assert.Equal("closed", audit.Details.GetProperty("status").GetString());
    }
}
