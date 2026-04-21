using ExiledCms.ThemesService.Api.Infrastructure;

namespace ExiledCms.ThemesService.Api.Tests;

public sealed class ThemesPlatformCatalogTests
{
    [Fact]
    public void BuildModule_ProducesStableRegistrationMetadata()
    {
        var options = new ServiceOptions
        {
            Name = "themes-service",
            DisplayName = "Themes Service",
            Version = "2.0.0",
            BaseUrl = "http://themes-service:8080",
        };

        var module = ThemesPlatformCatalog.BuildModule(options);

        Assert.Equal("themes-service", module.Id);
        Assert.Equal("Themes Service", module.Name);
        Assert.Equal("2.0.0", module.Version);
        Assert.Equal("http://themes-service:8080/healthz", module.HealthUrl);
        Assert.Equal("http://themes-service:8080/swagger/v1/swagger.json", module.OpenApiUrl);
        Assert.Contains("platform.themes", module.OwnedCapabilities);
    }

    [Fact]
    public void BuildPermissions_RegistersThemeManagementCapabilities()
    {
        var permissions = ThemesPlatformCatalog.BuildPermissions();

        Assert.Contains(permissions, permission => permission.Key == "themes.read" && !permission.Dangerous);
        Assert.Contains(permissions, permission => permission.Key == "themes.activate" && permission.Dangerous);
        Assert.Contains(permissions, permission => permission.Key == "themes.configure" && permission.Dangerous);
    }
}
