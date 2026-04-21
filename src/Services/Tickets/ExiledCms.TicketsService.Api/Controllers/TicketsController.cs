using ExiledCms.TicketsService.Api.Contracts;
using ExiledCms.TicketsService.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExiledCms.TicketsService.Api.Controllers;

[ApiController]
[Route("api/v1/tickets")]
[Produces("application/json")]
public sealed class TicketsController : ControllerBase
{
    private readonly ITicketService _ticketService;

    public TicketsController(ITicketService ticketService)
    {
        _ticketService = ticketService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(TicketDetailResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<TicketDetailResponse>> CreateAsync([FromBody] CreateTicketRequest request, CancellationToken cancellationToken)
    {
        var response = await _ticketService.CreateAsync(request, cancellationToken);
        return CreatedAtAction(nameof(GetByIdAsync), new { id = response.Id }, response);
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<TicketSummaryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResponse<TicketSummaryResponse>>> ListAsync([FromQuery] TicketsQueryRequest request, CancellationToken cancellationToken)
    {
        var response = await _ticketService.ListAsync(request, cancellationToken);
        return Ok(response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(TicketDetailResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<TicketDetailResponse>> GetByIdAsync([FromRoute] Guid id, CancellationToken cancellationToken)
    {
        var response = await _ticketService.GetByIdAsync(id, cancellationToken);
        return Ok(response);
    }

    [HttpPost("{id:guid}/messages")]
    [ProducesResponseType(typeof(TicketMessageResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<TicketMessageResponse>> AddMessageAsync([FromRoute] Guid id, [FromBody] AddTicketMessageRequest request, CancellationToken cancellationToken)
    {
        var response = await _ticketService.AddMessageAsync(id, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPost("{id:guid}/assign")]
    [ProducesResponseType(typeof(TicketAssignmentResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<TicketAssignmentResponse>> AssignAsync([FromRoute] Guid id, [FromBody] AssignTicketRequest request, CancellationToken cancellationToken)
    {
        var response = await _ticketService.AssignAsync(id, request, cancellationToken);
        return Ok(response);
    }

    [HttpPost("{id:guid}/status")]
    [ProducesResponseType(typeof(TicketDetailResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<TicketDetailResponse>> ChangeStatusAsync([FromRoute] Guid id, [FromBody] ChangeTicketStatusRequest request, CancellationToken cancellationToken)
    {
        var response = await _ticketService.ChangeStatusAsync(id, request, cancellationToken);
        return Ok(response);
    }

    [HttpPost("{id:guid}/internal-notes")]
    [ProducesResponseType(typeof(TicketInternalNoteResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<TicketInternalNoteResponse>> AddInternalNoteAsync([FromRoute] Guid id, [FromBody] AddInternalNoteRequest request, CancellationToken cancellationToken)
    {
        var response = await _ticketService.AddInternalNoteAsync(id, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }
}
