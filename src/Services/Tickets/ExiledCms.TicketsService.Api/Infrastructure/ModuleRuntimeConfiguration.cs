using System.Text.Json;
using Microsoft.Extensions.Options;
using NATS.Client;

namespace ExiledCms.TicketsService.Api.Infrastructure;

/// <summary>
/// Shared subject naming convention for platform-core runtime configuration exchange.
/// </summary>
public static class PlatformConfigSubjects
{
    public static string Request(string moduleId) => $"platform.config.request.{moduleId.Trim()}";

    public static string Desired(string moduleId) => $"platform.config.desired.{moduleId.Trim()}";

    public static string Reported(string moduleId) => $"platform.config.reported.{moduleId.Trim()}";
}

/// <summary>
/// Desired runtime configuration published by platform-core for a module.
/// </summary>
public sealed class DesiredModuleConfiguration
{
    public string ModuleId { get; set; } = string.Empty;

    public string? Revision { get; set; }

    public DateTime PublishedAt { get; set; }

    public string? DatabaseConnectionString { get; set; }

    public string? OpenApiUrl { get; set; }

    public string? SwaggerUiUrl { get; set; }

    public Dictionary<string, string>? Settings { get; set; }
}

/// <summary>
/// Effective runtime configuration reported back to platform-core after the module applies it.
/// </summary>
public sealed class ReportedModuleConfiguration
{
    public string ModuleId { get; set; } = string.Empty;

    public DateTime ReportedAt { get; set; }

    public bool DatabaseConfigured { get; set; }

    public string? OpenApiUrl { get; set; }

    public string? SwaggerUiUrl { get; set; }

    public string? ConfigurationSource { get; set; }

    public Dictionary<string, string>? Settings { get; set; }
}

/// <summary>
/// Thread-safe in-memory accessor for the latest configuration received from platform-core.
/// </summary>
public sealed class ModuleRuntimeConfigurationStore
{
    private readonly object _sync = new();
    private DesiredModuleConfiguration _current = new();

    public DesiredModuleConfiguration Current
    {
        get
        {
            lock (_sync)
            {
                return Clone(_current);
            }
        }
    }

    public bool HasDatabaseConnectionString => !string.IsNullOrWhiteSpace(Current.DatabaseConnectionString);

    /// <summary>
    /// Applies the authoritative desired configuration received from platform-core.
    /// </summary>
    public void Apply(DesiredModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.ModuleId = configuration.ModuleId.Trim();
        configuration.Revision = configuration.Revision?.Trim();
        configuration.DatabaseConnectionString = configuration.DatabaseConnectionString?.Trim();
        configuration.OpenApiUrl = configuration.OpenApiUrl?.Trim();
        configuration.SwaggerUiUrl = configuration.SwaggerUiUrl?.Trim();
        configuration.Settings = NormalizeSettings(configuration.Settings);
        if (configuration.PublishedAt == default)
        {
            configuration.PublishedAt = DateTime.UtcNow;
        }

        lock (_sync)
        {
            _current = Clone(configuration);
        }
    }

    /// <summary>
    /// Returns the connection string delivered by platform-core or throws when it was not synchronized yet.
    /// </summary>
    public string GetRequiredDatabaseConnectionString()
    {
        var current = Current;
        if (!string.IsNullOrWhiteSpace(current.DatabaseConnectionString))
        {
            return current.DatabaseConnectionString;
        }

        throw new InvalidOperationException("The tickets database connection string has not been delivered by platform-core over NATS yet.");
    }

    /// <summary>
    /// Builds the effective configuration report sent back to platform-core.
    /// </summary>
    public ReportedModuleConfiguration BuildReported(ServiceOptions serviceOptions, string configurationSource)
    {
        var current = Current;
        return new ReportedModuleConfiguration
        {
            ModuleId = string.IsNullOrWhiteSpace(current.ModuleId) ? serviceOptions.Name : current.ModuleId,
            ReportedAt = DateTime.UtcNow,
            DatabaseConfigured = !string.IsNullOrWhiteSpace(current.DatabaseConnectionString),
            OpenApiUrl = string.IsNullOrWhiteSpace(current.OpenApiUrl)
                ? serviceOptions.GetOpenApiUrl()
                : current.OpenApiUrl,
            SwaggerUiUrl = string.IsNullOrWhiteSpace(current.SwaggerUiUrl)
                ? serviceOptions.GetSwaggerUiUrl()
                : current.SwaggerUiUrl,
            ConfigurationSource = configurationSource?.Trim(),
            Settings = current.Settings is { Count: > 0 }
                ? new Dictionary<string, string>(current.Settings, StringComparer.OrdinalIgnoreCase)
                : null,
        };
    }

    private static DesiredModuleConfiguration Clone(DesiredModuleConfiguration configuration) => new()
    {
        ModuleId = configuration.ModuleId,
        Revision = configuration.Revision,
        PublishedAt = configuration.PublishedAt,
        DatabaseConnectionString = configuration.DatabaseConnectionString,
        OpenApiUrl = configuration.OpenApiUrl,
        SwaggerUiUrl = configuration.SwaggerUiUrl,
        Settings = configuration.Settings is { Count: > 0 }
            ? new Dictionary<string, string>(configuration.Settings, StringComparer.OrdinalIgnoreCase)
            : null,
    };

    private static Dictionary<string, string>? NormalizeSettings(Dictionary<string, string>? settings)
    {
        if (settings is null || settings.Count == 0)
        {
            return null;
        }

        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in settings)
        {
            var key = entry.Key?.Trim();
            var value = entry.Value?.Trim();
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            normalized[key] = value;
        }

        return normalized.Count == 0 ? null : normalized;
    }
}

/// <summary>
/// NATS-backed synchronization service that bootstraps tickets-service from platform-core and
/// continuously reports the applied runtime configuration back to the control plane.
/// </summary>
public sealed class PlatformCoreModuleConfigSyncService : BackgroundService
{
    private readonly ModuleRuntimeConfigurationStore _configurationStore;
    private readonly IOptions<ServiceOptions> _serviceOptions;
    private readonly IOptions<NatsOptions> _natsOptions;
    private readonly IOptions<ModuleConfigSyncOptions> _syncOptions;
    private readonly ILogger<PlatformCoreModuleConfigSyncService> _logger;

    public PlatformCoreModuleConfigSyncService(
        ModuleRuntimeConfigurationStore configurationStore,
        IOptions<ServiceOptions> serviceOptions,
        IOptions<NatsOptions> natsOptions,
        IOptions<ModuleConfigSyncOptions> syncOptions,
        ILogger<PlatformCoreModuleConfigSyncService> logger)
    {
        _configurationStore = configurationStore;
        _serviceOptions = serviceOptions;
        _natsOptions = natsOptions;
        _syncOptions = syncOptions;
        _logger = logger;
    }

    /// <summary>
    /// Requests the desired runtime configuration from platform-core before the service accesses MySQL.
    /// </summary>
    public async Task BootstrapAsync(CancellationToken cancellationToken)
    {
        var desired = await RequestDesiredConfigurationAsync(cancellationToken);
        ApplyDesiredConfiguration(desired, "nats-request");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configurationStore.HasDatabaseConnectionString)
        {
            await BootstrapAsync(stoppingToken);
        }

        var connectionOptions = ConnectionFactory.GetDefaultOptions();
        connectionOptions.Url = _natsOptions.Value.Url;

        using var connection = new ConnectionFactory().CreateConnection(connectionOptions);
        using var subscription = connection.SubscribeAsync(PlatformConfigSubjects.Desired(_serviceOptions.Value.Name));
        subscription.MessageHandler += (_, args) =>
        {
            try
            {
                var desired = JsonSerializer.Deserialize<DesiredModuleConfiguration>(args.Message.Data, JsonDefaults.SerializerOptions)
                    ?? throw new InvalidOperationException("Desired module configuration payload was empty.");
                ApplyDesiredConfiguration(desired, "nats-push");
                PublishReportedConfiguration(connection, "nats-push");
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to apply desired tickets-service runtime configuration from NATS");
            }
        };
        subscription.Start();

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _syncOptions.Value.ReportIntervalSeconds)));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                PublishReportedConfiguration(connection, "nats-heartbeat");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to publish tickets-service runtime configuration heartbeat");
            }
        }
    }

    private async Task<DesiredModuleConfiguration> RequestDesiredConfigurationAsync(CancellationToken cancellationToken)
    {
        var connectionOptions = ConnectionFactory.GetDefaultOptions();
        connectionOptions.Url = _natsOptions.Value.Url;

        using var connection = new ConnectionFactory().CreateConnection(connectionOptions);
        var timeout = TimeSpan.FromSeconds(Math.Max(1, _syncOptions.Value.RequestTimeoutSeconds));
        var subject = PlatformConfigSubjects.Request(_serviceOptions.Value.Name);

        Msg? response = await Task.Run(() => connection.Request(subject, Array.Empty<byte>(), (int)timeout.TotalMilliseconds), cancellationToken);
        if (response is null)
        {
            throw new InvalidOperationException($"Platform-core did not return runtime configuration for {_serviceOptions.Value.Name}.");
        }

        var desired = JsonSerializer.Deserialize<DesiredModuleConfiguration>(response.Data, JsonDefaults.SerializerOptions);
        if (desired is null)
        {
            throw new InvalidOperationException("Platform-core returned an empty runtime configuration payload.");
        }

        return desired;
    }

    private void ApplyDesiredConfiguration(DesiredModuleConfiguration configuration, string configurationSource)
    {
        if (string.IsNullOrWhiteSpace(configuration.ModuleId))
        {
            configuration.ModuleId = _serviceOptions.Value.Name;
        }

        _configurationStore.Apply(configuration);
        _logger.LogInformation(
            "Applied tickets-service runtime configuration from platform-core for {ModuleId} via {ConfigurationSource}. Database configured: {DatabaseConfigured}",
            configuration.ModuleId,
            configurationSource,
            !string.IsNullOrWhiteSpace(configuration.DatabaseConnectionString));
    }

    private void PublishReportedConfiguration(IConnection connection, string configurationSource)
    {
        var payload = _configurationStore.BuildReported(_serviceOptions.Value, configurationSource);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonDefaults.SerializerOptions);
        connection.Publish(PlatformConfigSubjects.Reported(_serviceOptions.Value.Name), bytes);
        connection.Flush();
    }
}
