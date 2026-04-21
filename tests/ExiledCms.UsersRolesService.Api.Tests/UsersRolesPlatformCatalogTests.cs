using ExiledCms.UsersRolesService.Api.Infrastructure;

namespace ExiledCms.UsersRolesService.Api.Tests;

public sealed class UsersRolesPlatformCatalogTests
{
    [Fact]
    public void BuildModule_ProducesStableRegistrationMetadata()
    {
        var options = new ServiceOptions
        {
            Name = "users-roles-service",
            DisplayName = "Users Roles Service",
            Version = "3.0.0",
            BaseUrl = "http://users-roles-service:8080",
        };

        var module = UsersRolesPlatformCatalog.BuildModule(options);

        Assert.Equal("users-roles-service", module.Id);
        Assert.Equal("Users Roles Service", module.Name);
        Assert.Equal("3.0.0", module.Version);
        Assert.Equal("http://users-roles-service:8080/healthz", module.HealthUrl);
        Assert.Equal("http://users-roles-service:8080/swagger/v1/swagger.json", module.OpenApiUrl);
        Assert.Contains("auth.users-roles", module.OwnedCapabilities);
    }

    [Fact]
    public void BuildPermissions_RegistersUserAndRoleManagementCapabilities()
    {
        var permissions = UsersRolesPlatformCatalog.BuildPermissions();

        Assert.Contains(permissions, permission => permission.Key == "users.read" && !permission.Dangerous);
        Assert.Contains(permissions, permission => permission.Key == "users.manage" && permission.Dangerous);
        Assert.Contains(permissions, permission => permission.Key == "roles.read" && !permission.Dangerous);
        Assert.Contains(permissions, permission => permission.Key == "roles.manage" && permission.Dangerous);
        Assert.Contains(permissions, permission => permission.Key == "permissions.assign" && permission.Dangerous);
    }
}
