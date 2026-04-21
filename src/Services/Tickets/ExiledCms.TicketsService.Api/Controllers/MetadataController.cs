using ExiledCms.TicketsService.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ExiledCms.TicketsService.Api.Controllers;

[ApiController]
[Route("api/v1/metadata")]
[Produces("application/json")]
public sealed class MetadataController : ControllerBase
{
    private readonly IOptions<ServiceOptions> _serviceOptions;

    public MetadataController(IOptions<ServiceOptions> serviceOptions)
    {
        _serviceOptions = serviceOptions;
    }

    [HttpGet("module-registration")]
    [ProducesResponseType(typeof(PlatformModuleRegistration), StatusCodes.Status200OK)]
    public ActionResult<PlatformModuleRegistration> GetModuleRegistration()
    {
        return Ok(TicketPlatformCatalog.BuildModule(_serviceOptions.Value));
    }

    [HttpGet("topology")]
    [ProducesResponseType(typeof(PlatformModuleTopology), StatusCodes.Status200OK)]
    public ActionResult<PlatformModuleTopology?> GetTopology()
    {
        return Ok(TicketPlatformCatalog.BuildModule(_serviceOptions.Value).Topology);
    }

    [HttpGet("documentation")]
    [ProducesResponseType(typeof(IReadOnlyCollection<PlatformDocumentationLink>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyCollection<PlatformDocumentationLink>?> GetDocumentation()
    {
        return Ok(TicketPlatformCatalog.BuildModule(_serviceOptions.Value).Documentation);
    }

    [HttpGet("permissions")]
    [ProducesResponseType(typeof(IReadOnlyCollection<PlatformPermissionDefinition>), StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyCollection<PlatformPermissionDefinition>> GetPermissions()
    {
        return Ok(TicketPlatformCatalog.BuildPermissions());
    }
}
