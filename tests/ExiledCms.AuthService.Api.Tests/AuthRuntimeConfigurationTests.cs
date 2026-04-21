using ExiledCms.AuthService.Api.Domain;
using ExiledCms.AuthService.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace ExiledCms.AuthService.Api.Tests;

public sealed class AuthRuntimeConfigurationTests
{
    [Fact]
    public void ModuleRuntimeConfigurationStore_ResolvesJwtAndReportedSnapshot_FromPlatformCoreConfig()
    {
        var store = new ModuleRuntimeConfigurationStore();
        store.Apply(new DesiredModuleConfiguration
        {
            ModuleId = " auth-service ",
            DatabaseConnectionString = " Server=mysql;Database=exiledcms_auth; ",
            OpenApiUrl = " http://auth-service:8080/swagger/v1/swagger.json ",
            SwaggerUiUrl = " http://auth-service:8080/swagger ",
            Settings = new Dictionary<string, string>
            {
                [AuthRuntimeSettingKeys.JwtSecret] = " runtime-secret ",
                [AuthRuntimeSettingKeys.JwtIssuer] = " ExiledCMS ",
                [AuthRuntimeSettingKeys.JwtAudience] = " exiledcms ",
                [AuthRuntimeSettingKeys.JwtAccessTokenLifetimeMinutes] = " 90 ",
            },
        });

        var jwt = store.ResolveJwtOptions(new JwtOptions
        {
            Secret = "local-secret",
            Issuer = "local-issuer",
            Audience = "local-audience",
            AccessTokenLifetimeMinutes = 30,
        });

        Assert.Equal("runtime-secret", jwt.Secret);
        Assert.Equal("ExiledCMS", jwt.Issuer);
        Assert.Equal("exiledcms", jwt.Audience);
        Assert.Equal(90, jwt.AccessTokenLifetimeMinutes);

        var reported = store.BuildReported(new AuthServiceOptions
        {
            Name = "auth-service",
            BaseUrl = "http://auth-service:8080",
        }, new JwtOptions(), "nats-request");

        Assert.Equal("auth-service", reported.ModuleId);
        Assert.True(reported.DatabaseConfigured);
        Assert.Equal("http://auth-service:8080/swagger/v1/swagger.json", reported.OpenApiUrl);
        Assert.Equal("http://auth-service:8080/swagger", reported.SwaggerUiUrl);
        Assert.Equal("true", reported.Settings!["jwtConfigured"]);
        Assert.Equal("ExiledCMS", reported.Settings["jwtIssuer"]);
    }

    [Fact]
    public void JwtRuntimeOptionsAccessor_FallsBackToLocalOptions_WhenCoreHasNoSettings()
    {
        var accessor = new JwtRuntimeOptionsAccessor(
            new ModuleRuntimeConfigurationStore(),
            Options.Create(new JwtOptions
            {
                Secret = "local-secret",
                Issuer = "local-issuer",
                Audience = "local-audience",
                AccessTokenLifetimeMinutes = 45,
            }));

        var current = accessor.GetCurrent();

        Assert.Equal("local-secret", current.Secret);
        Assert.Equal("local-issuer", current.Issuer);
        Assert.Equal("local-audience", current.Audience);
        Assert.Equal(45, current.AccessTokenLifetimeMinutes);
    }

    [Fact]
    public void PlatformConfigSubjects_UseStableNamingConvention()
    {
        Assert.Equal("platform.config.request.auth-service", PlatformConfigSubjects.Request("auth-service"));
        Assert.Equal("platform.config.desired.auth-service", PlatformConfigSubjects.Desired("auth-service"));
        Assert.Equal("platform.config.reported.auth-service", PlatformConfigSubjects.Reported("auth-service"));
    }

    [Fact]
    public void MySqlConnectionFactory_ResolveConnectionString_PrefersPlatformCoreConfig()
    {
        var resolved = MySqlConnectionFactory.ResolveConnectionString(
            " Server=platform;Database=exiledcms_auth; ",
            "Server=local;Database=exiledcms_auth;");

        Assert.Equal("Server=platform;Database=exiledcms_auth;", resolved);
    }

    [Fact]
    public void MySqlConnectionFactory_ResolveConnectionString_FallsBackToLocalSetting()
    {
        var resolved = MySqlConnectionFactory.ResolveConnectionString(
            syncedConnectionString: null,
            localFallbackConnectionString: " Server=local;Database=exiledcms_auth; ");

        Assert.Equal("Server=local;Database=exiledcms_auth;", resolved);
    }
}
