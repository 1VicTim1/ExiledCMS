using ExiledCms.TicketsService.Api.Controllers;
using ExiledCms.TicketsService.Api.Domain;
using ExiledCms.TicketsService.Api.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace ExiledCms.TicketsService.Api.Tests;

public sealed class ApiExceptionAndMetadataTests
{
    [Fact]
    public void ApiException_ToProblemDetails_MapsStatusInstanceAndExtensions()
    {
        var exception = ApiException.BadRequest(
            "Validation failed.",
            "validation_failed",
            new Dictionary<string, string> { ["field"] = "name" });
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/tickets";

        var problem = exception.ToProblemDetails(context);

        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal("Bad request", problem.Title);
        Assert.Equal("Validation failed.", problem.Detail);
        Assert.Equal("/api/v1/tickets", problem.Instance);
        Assert.Equal("validation_failed", problem.Extensions["errorCode"]);
        Assert.NotNull(problem.Extensions["details"]);
    }

    [Fact]
    public void TicketPlatformCatalog_BuildModule_IncludesTopologyAndDocumentation()
    {
        var module = TicketPlatformCatalog.BuildModule(new ServiceOptions
        {
            Name = "tickets-service",
            Version = "2.1.0",
            BaseUrl = "http://tickets-service:8080/",
            OpenApiJsonPath = "/swagger/v1/swagger.json",
            SwaggerUiPath = "/swagger",
        });

        Assert.Equal("tickets-service", module.Id);
        Assert.Equal("http://tickets-service:8080", module.BaseUrl);
        Assert.Equal("http://tickets-service:8080/healthz", module.HealthUrl);
        Assert.Equal("http://tickets-service:8080/swagger/v1/swagger.json", module.OpenApiUrl);
        Assert.Equal("http://tickets-service:8080/swagger", module.SwaggerUiUrl);
        Assert.Equal("platform.config.request.tickets-service", module.ConfigRequestSubject);
        Assert.Equal("platform.config.desired.tickets-service", module.ConfigDesiredSubject);
        Assert.Equal("platform.config.reported.tickets-service", module.ConfigReportedSubject);
        Assert.NotNull(module.Topology);
        Assert.Contains("platform-core distributed database config", module.Topology!.DataSources!);
        Assert.Contains(module.Documentation!, item => item.Key == "sentry");
        Assert.Contains(module.Documentation!, item => item.Key == "development" && item.Href == "contracts/modules/development.md");
    }

    [Fact]
    public void TicketPlatformCatalog_BuildPermissions_ReturnsDistinctKnownKeys()
    {
        var permissions = TicketPlatformCatalog.BuildPermissions();

        Assert.Contains(permissions, item => item.Key == TicketPermissions.Create);
        Assert.Contains(permissions, item => item.Key == TicketPermissions.ChangeStatus);
        Assert.Equal(
            permissions.Count,
            permissions.Select(item => item.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void MetadataController_ReturnsTopologyAndDocumentationFromCatalog()
    {
        var controller = new MetadataController(Options.Create(new ServiceOptions
        {
            Name = "tickets-service",
            Version = "1.0.0",
            BaseUrl = "http://tickets-service:8080",
            OpenApiJsonPath = "/swagger/v1/swagger.json",
            SwaggerUiPath = "/swagger",
        }));

        var topologyResult = Assert.IsType<OkObjectResult>(controller.GetTopology().Result);
        var topology = Assert.IsType<PlatformModuleTopology>(topologyResult.Value);
        Assert.Equal("remote-service", topology.DeploymentMode);

        var documentationResult = Assert.IsType<OkObjectResult>(controller.GetDocumentation().Result);
        var documentation = Assert.IsAssignableFrom<IReadOnlyCollection<PlatformDocumentationLink>>(documentationResult.Value);
        Assert.Contains(documentation, item => item.Key == "observability");
        Assert.Contains(documentation, item => item.Key == "sentry");

        var registrationResult = Assert.IsType<OkObjectResult>(controller.GetModuleRegistration().Result);
        var registration = Assert.IsType<PlatformModuleRegistration>(registrationResult.Value);
        Assert.Equal("http://tickets-service:8080/swagger/v1/swagger.json", registration.OpenApiUrl);
        Assert.Equal("platform.config.desired.tickets-service", registration.ConfigDesiredSubject);
    }
}
