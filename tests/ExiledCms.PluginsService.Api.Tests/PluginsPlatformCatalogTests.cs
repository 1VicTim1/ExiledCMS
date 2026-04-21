using ExiledCms.PluginsService.Api.Infrastructure;

namespace ExiledCms.PluginsService.Api.Tests;

public sealed class PluginsPlatformCatalogTests
{
    [Fact]
    public void BuildModule_ProducesStableRegistrationMetadata()
    {
        var options = new ServiceOptions
        {
            Name = "plugins-service",
            DisplayName = "Plugins Service",
            Version = "1.2.3",
            BaseUrl = "http://plugins-service:8080",
        };

        var module = PluginsPlatformCatalog.BuildModule(options);

        Assert.Equal("plugins-service", module.Id);
        Assert.Equal("Plugins Service", module.Name);
        Assert.Equal("1.2.3", module.Version);
        Assert.Equal("http://plugins-service:8080/healthz", module.HealthUrl);
        Assert.Equal("http://plugins-service:8080/swagger/v1/swagger.json", module.OpenApiUrl);
        Assert.Contains("platform.plugins", module.OwnedCapabilities);
    }

    [Fact]
    public void BuildPermissions_RegistersPluginManagementCapabilities()
    {
        var permissions = PluginsPlatformCatalog.BuildPermissions();

        Assert.Contains(permissions, permission => permission.Key == "plugins.read" && !permission.Dangerous);
        Assert.Contains(permissions, permission => permission.Key == "plugins.install" && permission.Dangerous);
        Assert.Contains(permissions, permission => permission.Key == "plugins.enable" && permission.Dangerous);
        Assert.Contains(permissions, permission => permission.Key == "plugins.configure" && permission.Dangerous);
    }
}
