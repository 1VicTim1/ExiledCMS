namespace ExiledCms.TicketsService.Api.Infrastructure;

public sealed class ServiceOptions
{
    public string Name { get; set; } = "tickets-service";

    public string Version { get; set; } = "1.0.0";

    public string BaseUrl { get; set; } = "http://tickets-service:8080";

    // Local fallback used during startup if platform-core config sync is not ready yet.
    public string MySqlConnectionString { get; set; } = string.Empty;

    public string OpenApiJsonPath { get; set; } = "/swagger/v1/swagger.json";

    public string SwaggerUiPath { get; set; } = "/swagger";

    public string GetOpenApiUrl() => BuildAbsoluteUrl(OpenApiJsonPath);

    public string GetSwaggerUiUrl() => BuildAbsoluteUrl(SwaggerUiPath);

    private string BuildAbsoluteUrl(string relativePath)
    {
        var baseUrl = BaseUrl.TrimEnd('/');
        var suffix = string.IsNullOrWhiteSpace(relativePath) ? string.Empty : "/" + relativePath.Trim().TrimStart('/');
        return baseUrl + suffix;
    }
}

public sealed class PlatformCoreOptions
{
    public string BaseUrl { get; set; } = "http://platform-core:8080";

    public bool AutoRegister { get; set; } = true;

    public int RetryIntervalSeconds { get; set; } = 30;
}

public sealed class NatsOptions
{
    public string Url { get; set; } = "nats://localhost:4222";

    public string EventVersion { get; set; } = "1.0";
}

public sealed class OutboxOptions
{
    public int DispatchIntervalSeconds { get; set; } = 5;

    public int BatchSize { get; set; } = 50;
}

public sealed class ModuleConfigSyncOptions
{
    public int RequestTimeoutSeconds { get; set; } = 5;

    public int ReportIntervalSeconds { get; set; } = 15;
}
