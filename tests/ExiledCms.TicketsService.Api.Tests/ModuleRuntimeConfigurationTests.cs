using ExiledCms.TicketsService.Api.Infrastructure;

namespace ExiledCms.TicketsService.Api.Tests;

public sealed class ModuleRuntimeConfigurationTests
{
    [Fact]
    public void ModuleRuntimeConfigurationStore_AppliesDesiredConfigAndBuildsReportedSnapshot()
    {
        var store = new ModuleRuntimeConfigurationStore();
        store.Apply(new DesiredModuleConfiguration
        {
            ModuleId = " tickets-service ",
            DatabaseConnectionString = " Server=mysql;Database=exiledcms_tickets; ",
            OpenApiUrl = " http://tickets-service:8080/swagger/v1/swagger.json ",
            SwaggerUiUrl = " http://tickets-service:8080/swagger ",
            Settings = new Dictionary<string, string>
            {
                [" feature "] = " enabled ",
                ["ignored"] = "",
            },
        });

        Assert.Equal("Server=mysql;Database=exiledcms_tickets;", store.GetRequiredDatabaseConnectionString());
        Assert.True(store.HasDatabaseConnectionString);

        var reported = store.BuildReported(new ServiceOptions
        {
            Name = "tickets-service",
            BaseUrl = "http://tickets-service:8080",
        }, "nats-request");

        Assert.Equal("tickets-service", reported.ModuleId);
        Assert.True(reported.DatabaseConfigured);
        Assert.Equal("http://tickets-service:8080/swagger/v1/swagger.json", reported.OpenApiUrl);
        Assert.Equal("http://tickets-service:8080/swagger", reported.SwaggerUiUrl);
        Assert.Equal("enabled", reported.Settings!["feature"]);
    }

    [Fact]
    public void PlatformConfigSubjects_UseStableNamingConvention()
    {
        Assert.Equal("platform.config.request.tickets-service", PlatformConfigSubjects.Request("tickets-service"));
        Assert.Equal("platform.config.desired.tickets-service", PlatformConfigSubjects.Desired("tickets-service"));
        Assert.Equal("platform.config.reported.tickets-service", PlatformConfigSubjects.Reported("tickets-service"));
    }

    [Fact]
    public void SqlMigrationRunner_ResolveScriptsPath_FallsBackToBaseDirectory()
    {
        var root = Directory.CreateTempSubdirectory("tickets-migrations-test");
        try
        {
            var contentRoot = Path.Combine(root.FullName, "content-root");
            var baseDirectory = Path.Combine(root.FullName, "publish-root");
            Directory.CreateDirectory(Path.Combine(baseDirectory, "Migrations", "Scripts"));

            var resolved = SqlMigrationRunner.ResolveScriptsPath(contentRoot, baseDirectory);

            Assert.Equal(Path.Combine(baseDirectory, "Migrations", "Scripts"), resolved);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void MySqlConnectionFactory_ResolveConnectionString_PrefersPlatformCoreConfig()
    {
        var resolved = MySqlConnectionFactory.ResolveConnectionString(
            " Server=platform;Database=exiledcms_tickets; ",
            "Server=local;Database=exiledcms_tickets;");

        Assert.Equal("Server=platform;Database=exiledcms_tickets;", resolved);
    }

    [Fact]
    public void MySqlConnectionFactory_ResolveConnectionString_FallsBackToLocalSetting()
    {
        var resolved = MySqlConnectionFactory.ResolveConnectionString(
            syncedConnectionString: null,
            localFallbackConnectionString: " Server=local;Database=exiledcms_tickets; ");

        Assert.Equal("Server=local;Database=exiledcms_tickets;", resolved);
    }

    [Fact]
    public void ModuleRuntimeConfigurationStore_BuildReported_UsesLocalFallbackAsDatabaseConfigured()
    {
        var store = new ModuleRuntimeConfigurationStore();

        var reported = store.BuildReported(new ServiceOptions
        {
            Name = "tickets-service",
            BaseUrl = "http://tickets-service:8080",
            MySqlConnectionString = "Server=local;Database=exiledcms_tickets;",
        }, "local-fallback");

        Assert.True(reported.DatabaseConfigured);
        Assert.Equal("local-fallback", reported.ConfigurationSource);
    }
}
