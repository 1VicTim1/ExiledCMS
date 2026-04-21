using System.Text.Json;
using ExiledCms.AuthService.Api.Domain;
using Microsoft.Extensions.Options;
using NATS.Client;

namespace ExiledCms.AuthService.Api.Infrastructure;

public static class PlatformConfigSubjects
{
    public static string Request(string moduleId) => $"platform.config.request.{moduleId.Trim()}";

    public static string Desired(string moduleId) => $"platform.config.desired.{moduleId.Trim()}";

    public static string Reported(string moduleId) => $"platform.config.reported.{moduleId.Trim()}";
}

public static class AuthRuntimeSettingKeys
{
    public const string JwtSecret = "auth.jwt.secret";
    public const string JwtIssuer = "auth.jwt.issuer";
    public const string JwtAudience = "auth.jwt.audience";
    public const string JwtAccessTokenLifetimeMinutes = "auth.jwt.accessTokenLifetimeMinutes";
}

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

    public string? GetDatabaseConnectionStringOrNull() => Current.DatabaseConnectionString;

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

    public JwtOptions ResolveJwtOptions(JwtOptions localOptions)
    {
        ArgumentNullException.ThrowIfNull(localOptions);

        var current = Current;
        var resolved = new JwtOptions
        {
            Secret = localOptions.Secret?.Trim() ?? string.Empty,
            Issuer = localOptions.Issuer?.Trim() ?? string.Empty,
            Audience = localOptions.Audience?.Trim() ?? string.Empty,
            AccessTokenLifetimeMinutes = localOptions.AccessTokenLifetimeMinutes,
        };

        if (current.Settings is { Count: > 0 })
        {
            if (TryGetSetting(current.Settings, AuthRuntimeSettingKeys.JwtSecret, out var secret))
            {
                resolved.Secret = secret;
            }

            if (TryGetSetting(current.Settings, AuthRuntimeSettingKeys.JwtIssuer, out var issuer))
            {
                resolved.Issuer = issuer;
            }

            if (TryGetSetting(current.Settings, AuthRuntimeSettingKeys.JwtAudience, out var audience))
            {
                resolved.Audience = audience;
            }

            if (TryGetSetting(current.Settings, AuthRuntimeSettingKeys.JwtAccessTokenLifetimeMinutes, out var lifetimeRaw) &&
                int.TryParse(lifetimeRaw, out var lifetime) &&
                lifetime > 0)
            {
                resolved.AccessTokenLifetimeMinutes = lifetime;
            }
        }

        if (string.IsNullOrWhiteSpace(resolved.Issuer))
        {
            resolved.Issuer = "ExiledCMS";
        }

        if (string.IsNullOrWhiteSpace(resolved.Audience))
        {
            resolved.Audience = "exiledcms";
        }

        return resolved;
    }

    public ReportedModuleConfiguration BuildReported(AuthServiceOptions serviceOptions, JwtOptions localJwtOptions, string configurationSource)
    {
        var current = Current;
        var jwt = ResolveJwtOptions(localJwtOptions);

        var settings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["jwtConfigured"] = (!string.IsNullOrWhiteSpace(jwt.Secret)).ToString().ToLowerInvariant(),
            ["jwtIssuer"] = jwt.Issuer,
            ["jwtAudience"] = jwt.Audience,
            ["jwtAccessTokenLifetimeMinutes"] = jwt.AccessTokenLifetimeMinutes.ToString(),
        };

        if (current.Settings is { Count: > 0 })
        {
            foreach (var entry in current.Settings)
            {
                if (!settings.ContainsKey(entry.Key))
                {
                    settings[entry.Key] = entry.Value;
                }
            }
        }

        return new ReportedModuleConfiguration
        {
            ModuleId = string.IsNullOrWhiteSpace(current.ModuleId) ? serviceOptions.Name : current.ModuleId,
            ReportedAt = DateTime.UtcNow,
            DatabaseConfigured =
                !string.IsNullOrWhiteSpace(current.DatabaseConnectionString) ||
                !string.IsNullOrWhiteSpace(serviceOptions.MySqlConnectionString),
            OpenApiUrl = string.IsNullOrWhiteSpace(current.OpenApiUrl)
                ? serviceOptions.GetOpenApiUrl()
                : current.OpenApiUrl,
            SwaggerUiUrl = string.IsNullOrWhiteSpace(current.SwaggerUiUrl)
                ? serviceOptions.GetSwaggerUiUrl()
                : current.SwaggerUiUrl,
            ConfigurationSource = configurationSource?.Trim(),
            Settings = settings,
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

    private static bool TryGetSetting(IReadOnlyDictionary<string, string> settings, string key, out string value)
    {
        if (settings.TryGetValue(key, out value!))
        {
            value = value.Trim();
            return !string.IsNullOrWhiteSpace(value);
        }

        value = string.Empty;
        return false;
    }

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

public sealed class JwtRuntimeOptionsAccessor
{
    private readonly ModuleRuntimeConfigurationStore _configurationStore;
    private readonly IOptions<JwtOptions> _localOptions;

    public JwtRuntimeOptionsAccessor(ModuleRuntimeConfigurationStore configurationStore, IOptions<JwtOptions> localOptions)
    {
        _configurationStore = configurationStore;
        _localOptions = localOptions;
    }

    public JwtOptions GetCurrent() => _configurationStore.ResolveJwtOptions(_localOptions.Value);
}

public sealed class PlatformCoreModuleConfigSyncService : BackgroundService
{
    private readonly ModuleRuntimeConfigurationStore _configurationStore;
    private readonly IOptions<AuthServiceOptions> _serviceOptions;
    private readonly IOptions<JwtOptions> _jwtOptions;
    private readonly IOptions<NatsOptions> _natsOptions;
    private readonly IOptions<ModuleConfigSyncOptions> _syncOptions;
    private readonly ILogger<PlatformCoreModuleConfigSyncService> _logger;

    public PlatformCoreModuleConfigSyncService(
        ModuleRuntimeConfigurationStore configurationStore,
        IOptions<AuthServiceOptions> serviceOptions,
        IOptions<JwtOptions> jwtOptions,
        IOptions<NatsOptions> natsOptions,
        IOptions<ModuleConfigSyncOptions> syncOptions,
        ILogger<PlatformCoreModuleConfigSyncService> logger)
    {
        _configurationStore = configurationStore;
        _serviceOptions = serviceOptions;
        _jwtOptions = jwtOptions;
        _natsOptions = natsOptions;
        _syncOptions = syncOptions;
        _logger = logger;
    }

    public async Task BootstrapAsync(CancellationToken cancellationToken)
    {
        var desired = await RequestDesiredConfigurationAsync(cancellationToken);
        ApplyDesiredConfiguration(desired, "nats-request");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_configurationStore.HasDatabaseConnectionString)
        {
            try
            {
                await BootstrapAsync(stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning(exception, "Initial platform-core runtime configuration bootstrap timed out; auth-service will keep running and wait for NATS updates");
            }
        }

        var connectionOptions = ConnectionFactory.GetDefaultOptions();
        connectionOptions.Url = _natsOptions.Value.Url;

        using var connection = new ConnectionFactory().CreateConnection(connectionOptions);
        using var subscription = connection.SubscribeAsync(PlatformConfigSubjects.Desired(_serviceOptions.Value.Name));
        subscription.MessageHandler += (_, args) =>
        {
            try
            {
                var desired = JsonSerializer.Deserialize<DesiredModuleConfiguration>(args.Message.Data, PlatformJson.Options)
                    ?? throw new InvalidOperationException("Desired module configuration payload was empty.");
                ApplyDesiredConfiguration(desired, "nats-push");
                PublishReportedConfiguration(connection, "nats-push");
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to apply desired auth-service runtime configuration from NATS");
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
                _logger.LogWarning(exception, "Failed to publish auth-service runtime configuration heartbeat");
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

        var desired = JsonSerializer.Deserialize<DesiredModuleConfiguration>(response.Data, PlatformJson.Options);
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
        var resolvedJwtOptions = _configurationStore.ResolveJwtOptions(_jwtOptions.Value);
        _logger.LogInformation(
            "Applied auth-service runtime configuration from platform-core for {ModuleId} via {ConfigurationSource}. Database configured: {DatabaseConfigured}. JWT configured: {JwtConfigured}",
            configuration.ModuleId,
            configurationSource,
            !string.IsNullOrWhiteSpace(configuration.DatabaseConnectionString),
            !string.IsNullOrWhiteSpace(resolvedJwtOptions.Secret));
    }

    private void PublishReportedConfiguration(IConnection connection, string configurationSource)
    {
        var payload = _configurationStore.BuildReported(_serviceOptions.Value, _jwtOptions.Value, configurationSource);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, PlatformJson.Options);
        connection.Publish(PlatformConfigSubjects.Reported(_serviceOptions.Value.Name), bytes);
        connection.Flush();
    }
}
