using ExiledCms.TicketsService.Api.Contracts;
using ExiledCms.TicketsService.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExiledCms.TicketsService.Api.Controllers;

[ApiController]
[Route("api/v1/ticket-categories")]
[Produces("application/json")]
public sealed class TicketCategoriesController : ControllerBase
{
    private readonly ITicketCategoryService _ticketCategoryService;

    public TicketCategoriesController(ITicketCategoryService ticketCategoryService)
    {
        _ticketCategoryService = ticketCategoryService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyCollection<TicketCategoryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<TicketCategoryResponse>>> ListAsync(CancellationToken cancellationToken)
    {
        var response = await _ticketCategoryService.ListAsync(cancellationToken);
        return Ok(response);
    }

    [HttpPost]
    [ProducesResponseType(typeof(TicketCategoryResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<TicketCategoryResponse>> CreateAsync([FromBody] CreateTicketCategoryRequest request, CancellationToken cancellationToken)
    {
        var response = await _ticketCategoryService.CreateAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }
}
