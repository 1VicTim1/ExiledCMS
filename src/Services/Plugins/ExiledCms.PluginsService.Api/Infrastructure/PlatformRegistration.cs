using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace ExiledCms.PluginsService.Api.Infrastructure;

internal static class PlatformJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

public sealed record PlatformModulePayload(
    string Id,
    string Name,
    string Version,
    string Kind,
    string BaseUrl,
    string HealthUrl,
    string? OpenApiUrl,
    string? SwaggerUiUrl,
    DateTime RegisteredAt,
    IReadOnlyCollection<string> OwnedCapabilities,
    IReadOnlyCollection<string> Tags,
    object? Topology,
    IReadOnlyCollection<object>? Documentation);

public sealed record PlatformPermissionPayload(
    string Key,
    string DisplayName,
    string Scope,
    string Description,
    bool Dangerous = false);

public static class PluginsPlatformCatalog
{
    public static PlatformModulePayload BuildModule(ServiceOptions options) =>
        new(
            Id: options.Name,
            Name: options.DisplayName,
            Version: options.Version,
            Kind: "service",
            BaseUrl: options.BaseUrl.TrimEnd('/'),
            HealthUrl: options.BaseUrl.TrimEnd('/') + "/healthz",
            OpenApiUrl: options.GetOpenApiUrl(),
            SwaggerUiUrl: options.GetSwaggerUiUrl(),
            RegisteredAt: DateTime.UtcNow,
            OwnedCapabilities: ["platform.plugins"],
            Tags: ["dotnet", "aspnet-core", "plugins", "platform", "observability"],
            Topology: new
            {
                deploymentMode = "remote-service",
                dataSources = new[] { "platform-core registry api" },
                dependencies = new[] { "platform-core" },
            },
            Documentation:
            [
                new { key = "development", title = "Module Platform Guide", href = "contracts/modules/README.md", description = "High-level explanation of how modules work in ExiledCMS." },
                new { key = "development", title = "Module Development Guide", href = "contracts/modules/development.md", description = "Implementation details for registering a module and forwarding logs." },
                new { key = "api", title = "Plugins Service README", href = "src/Services/Plugins/ExiledCms.PluginsService.Api/README.md", description = "Service-local API and runtime guide." },
                new { key = "observability", title = "Module Observability Guide", href = "contracts/modules/observability.md", description = "Centralized logging and observability conventions for modules." },
            ]);

    public static IReadOnlyCollection<PlatformPermissionPayload> BuildPermissions() =>
    [
        new("plugins.read", "Read plugins", "plugins", "Allows viewing plugin catalog and plugin metadata."),
        new("plugins.install", "Install plugins", "plugins", "Allows installing or registering new plugins.", Dangerous: true),
        new("plugins.enable", "Enable plugins", "plugins", "Allows enabling or disabling installed plugins.", Dangerous: true),
        new("plugins.configure", "Configure plugins", "plugins", "Allows changing plugin-specific settings.", Dangerous: true),
    ];
}

public sealed class PlatformCoreRegistrationService : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<ServiceOptions> _serviceOptions;
    private readonly IOptions<PlatformCoreOptions> _platformCoreOptions;
    private readonly ILogger<PlatformCoreRegistrationService> _logger;

    public PlatformCoreRegistrationService(
        IHttpClientFactory httpClientFactory,
        IOptions<ServiceOptions> serviceOptions,
        IOptions<PlatformCoreOptions> platformCoreOptions,
        ILogger<PlatformCoreRegistrationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _serviceOptions = serviceOptions;
        _platformCoreOptions = platformCoreOptions;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_platformCoreOptions.Value.AutoRegister)
        {
            _logger.LogInformation("Platform-core auto-registration is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _platformCoreOptions.Value.RetryIntervalSeconds)));
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
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to register plugins-service in platform-core registry");
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
        var client = _httpClientFactory.CreateClient(nameof(PlatformCoreRegistrationService));
        client.BaseAddress = new Uri(_platformCoreOptions.Value.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);

        var moduleResponse = await client.PostAsJsonAsync(
            "api/v1/platform/modules",
            PluginsPlatformCatalog.BuildModule(_serviceOptions.Value),
            PlatformJson.Options,
            cancellationToken);
        moduleResponse.EnsureSuccessStatusCode();

        foreach (var permission in PluginsPlatformCatalog.BuildPermissions())
        {
            var permissionResponse = await client.PostAsJsonAsync(
                "api/v1/platform/permissions",
                permission,
                PlatformJson.Options,
                cancellationToken);
            permissionResponse.EnsureSuccessStatusCode();
        }

        _logger.LogInformation("plugins-service module and permissions registered in platform-core");
    }
}
