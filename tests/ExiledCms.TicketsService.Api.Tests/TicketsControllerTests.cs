using ExiledCms.TicketsService.Api.Contracts;
using ExiledCms.TicketsService.Api.Controllers;
using ExiledCms.TicketsService.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExiledCms.TicketsService.Api.Tests;

public sealed class TicketsControllerTests
{
    [Fact]
    public async Task CreateAsync_ReturnsCreatedAtRoute_ForTicketLookupEndpoint()
    {
        var response = new TicketDetailResponse
        {
            Id = Guid.NewGuid(),
            Subject = "Smoke ticket",
            Status = "open",
            Priority = "medium",
            Category = new TicketCategoryReferenceResponse
            {
                Id = Guid.NewGuid(),
                Name = "Technical Support",
            },
            CreatedBy = new ActorReferenceResponse
            {
                UserId = Guid.NewGuid(),
                DisplayName = "Codex",
            },
            AssignedTo = null,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            LastMessageAtUtc = DateTime.UtcNow,
            MessageCount = 1,
            Messages =
            [
                new TicketMessageResponse
                {
                    Id = Guid.NewGuid(),
                    Author = new ActorReferenceResponse
                    {
                        UserId = Guid.NewGuid(),
                        DisplayName = "Codex",
                    },
                    IsStaffReply = false,
                    Body = "Initial message",
                    CreatedAtUtc = DateTime.UtcNow,
                },
            ],
            Assignments = [],
            InternalNotes = [],
            AuditTrail = [],
        };

        var controller = new TicketsController(new StubTicketService(response));
        var result = await controller.CreateAsync(new CreateTicketRequest
        {
            Subject = response.Subject,
            CategoryId = response.Category.Id,
            Priority = response.Priority,
            Message = response.Messages.First().Body,
        }, CancellationToken.None);

        var created = Assert.IsType<CreatedAtRouteResult>(result.Result);
        Assert.Equal(TicketsController.GetTicketByIdRouteName, created.RouteName);
        Assert.Equal(response.Id, created.RouteValues!["id"]);
        Assert.Same(response, created.Value);
    }

    private sealed class StubTicketService : ITicketService
    {
        private readonly TicketDetailResponse _createResponse;

        public StubTicketService(TicketDetailResponse createResponse)
        {
            _createResponse = createResponse;
        }

        public Task<TicketDetailResponse> CreateAsync(CreateTicketRequest request, CancellationToken cancellationToken)
            => Task.FromResult(_createResponse);

        public Task<PagedResponse<TicketSummaryResponse>> ListAsync(TicketsQueryRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TicketDetailResponse> GetByIdAsync(Guid ticketId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TicketMessageResponse> AddMessageAsync(Guid ticketId, AddTicketMessageRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TicketAssignmentResponse> AssignAsync(Guid ticketId, AssignTicketRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TicketDetailResponse> ChangeStatusAsync(Guid ticketId, ChangeTicketStatusRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TicketInternalNoteResponse> AddInternalNoteAsync(Guid ticketId, AddInternalNoteRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
