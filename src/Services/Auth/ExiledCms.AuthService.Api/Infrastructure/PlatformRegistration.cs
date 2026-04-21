using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ExiledCms.AuthService.Api.Domain;
using Microsoft.Extensions.Options;

namespace ExiledCms.AuthService.Api.Infrastructure;

// Keep serializer options local — consistent with how other modules talk to
// platform-core (camelCase, nulls dropped).
internal static class PlatformJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

// Mirrors the JSON shape platform-core expects on POST /api/v1/platform/modules.
// Kept minimal: the auth service has no outbox or module-config-sync to declare.
internal sealed record PlatformModulePayload(
    string Id,
    string Name,
    string Version,
    string Kind,
    string BaseUrl,
    string HealthUrl,
    string? OpenApiUrl,
    string? SwaggerUiUrl,
    string? ConfigRequestSubject,
    string? ConfigDesiredSubject,
    string? ConfigReportedSubject,
    DateTime RegisteredAt,
    IReadOnlyCollection<string> OwnedCapabilities,
    IReadOnlyCollection<string> Tags,
    object? Topology,
    IReadOnlyCollection<object>? Documentation);

internal sealed record PlatformPermissionPayload(
    string Key,
    string DisplayName,
    string Scope,
    string Description);

// Background service that registers this module (and its permission catalog)
// with platform-core at startup, then retries on a timer so restarting the
// control-plane does not orphan registrations.
public sealed class PlatformCoreRegistrationService : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<AuthServiceOptions> _authOptions;
    private readonly IOptions<PlatformCoreOptions> _platformOptions;
    private readonly ILogger<PlatformCoreRegistrationService> _logger;

    public PlatformCoreRegistrationService(
        IHttpClientFactory httpClientFactory,
        IOptions<AuthServiceOptions> authOptions,
        IOptions<PlatformCoreOptions> platformOptions,
        ILogger<PlatformCoreRegistrationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _authOptions = authOptions;
        _platformOptions = platformOptions;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_platformOptions.Value.AutoRegister)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _platformOptions.Value.RetryIntervalSeconds)));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RegisterAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "auth-service registration with platform-core failed");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RegisterAsync(CancellationToken cancellationToken)
    {
        var auth = _authOptions.Value;
        var client = _httpClientFactory.CreateClient(nameof(PlatformCoreRegistrationService));
        client.BaseAddress = new Uri(_platformOptions.Value.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);

        var payload = new PlatformModulePayload(
            Id: auth.Name,
            Name: "Auth Service",
            Version: auth.Version,
            Kind: "service",
            BaseUrl: auth.BaseUrl.TrimEnd('/'),
            HealthUrl: auth.BaseUrl.TrimEnd('/') + "/healthz",
            OpenApiUrl: auth.GetOpenApiUrl(),
            SwaggerUiUrl: auth.GetSwaggerUiUrl(),
            ConfigRequestSubject: PlatformConfigSubjects.Request(auth.Name),
            ConfigDesiredSubject: PlatformConfigSubjects.Desired(auth.Name),
            ConfigReportedSubject: PlatformConfigSubjects.Reported(auth.Name),
            RegisteredAt: DateTime.UtcNow,
            OwnedCapabilities: ["auth.identity", "auth.rbac"],
            Tags: ["dotnet", "aspnet-core", "auth", "identity", "rbac", "observability"],
            Topology: new
            {
                deploymentMode = "remote-service",
                dataSources = new[]
                {
                    "platform-core distributed database config",
                    "platform-core distributed jwt config",
                    "nats",
                    "platform-core registry api",
                },
                dependencies = new[] { "platform-core", "mysql", "nats" },
            },
            Documentation: new object[]
            {
                new { key = "development", title = "Module Platform Guide", href = "contracts/modules/README.md", description = "High-level explanation of how modules work in ExiledCMS." },
                new { key = "development", title = "Module Development Guide", href = "contracts/modules/development.md", description = "Implementation details for registering a module and forwarding logs." },
                new { key = "api", title = "Auth Service README", href = "src/Services/Auth/ExiledCms.AuthService.Api/README.md", description = "Service-local API and runtime guide." },
                new { key = "observability", title = "Module Observability Guide", href = "contracts/modules/observability.md", description = "Centralized logging and observability conventions for modules." },
                new { key = "sentry", title = "Sentry Topology Guide", href = "contracts/modules/observability.md#recommended-sentry-topology", description = "How centralized core routing and module-local Sentry can coexist." },
            });

        var moduleResponse = await client.PostAsJsonAsync("api/v1/platform/modules", payload, PlatformJson.Options, cancellationToken);
        moduleResponse.EnsureSuccessStatusCode();

        foreach (var (key, display, description) in AuthPermissions.All)
        {
            var permissionResponse = await client.PostAsJsonAsync(
                "api/v1/platform/permissions",
                new PlatformPermissionPayload(key, display, "auth", description),
                PlatformJson.Options,
                cancellationToken);
            permissionResponse.EnsureSuccessStatusCode();
        }

        _logger.LogInformation("auth-service module and permissions registered in platform-core");
    }
}
