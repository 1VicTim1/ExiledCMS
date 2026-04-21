namespace ExiledCms.ThemesService.Api.Infrastructure;

public sealed class ServiceOptions
{
    public string Name { get; set; } = "themes-service";

    public string DisplayName { get; set; } = "Themes Service";

    public string Version { get; set; } = "1.0.0";

    public string BaseUrl { get; set; } = "http://themes-service:8080";

    public string OpenApiJsonPath { get; set; } = "/swagger/v1/swagger.json";

    public string SwaggerUiPath { get; set; } = "/swagger";

    public string GetOpenApiUrl() => $"{BaseUrl.TrimEnd('/')}{NormalizePath(OpenApiJsonPath)}";

    public string GetSwaggerUiUrl() => $"{BaseUrl.TrimEnd('/')}{NormalizePath(SwaggerUiPath)}";

    private static string NormalizePath(string? value)
    {
        var path = string.IsNullOrWhiteSpace(value) ? "/" : value.Trim();
        return path.StartsWith('/') ? path : "/" + path;
    }
}

public sealed class PlatformCoreOptions
{
    public string BaseUrl { get; set; } = "http://platform-core:8080";

    public bool AutoRegister { get; set; } = true;

    public int RetryIntervalSeconds { get; set; } = 30;
}
