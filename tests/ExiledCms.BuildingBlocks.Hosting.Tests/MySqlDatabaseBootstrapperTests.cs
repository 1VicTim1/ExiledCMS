using ExiledCms.BuildingBlocks.Hosting;

namespace ExiledCms.BuildingBlocks.Hosting.Tests;

public sealed class MySqlDatabaseBootstrapperTests
{
    [Fact]
    public void TryCreateProvisioningPlan_ExtractsDatabaseAndBuildsServerConnectionString()
    {
        var created = MySqlDatabaseBootstrapper.TryCreateProvisioningPlan(
            "Server=mysql;Port=3306;Database=exiledcms_auth;User ID=exiledcms;Password=secret;SslMode=None",
            out var plan);

        Assert.True(created);
        Assert.NotNull(plan);
        Assert.Equal("exiledcms_auth", plan!.DatabaseName);
        Assert.DoesNotContain("Database=exiledcms_auth", plan.ServerConnectionString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Server=mysql", plan.ServerConnectionString, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Server=mysql;User ID=exiledcms;Password=secret;SslMode=None")]
    public void TryCreateProvisioningPlan_ReturnsFalseWhenDatabaseIsMissing(string connectionString)
    {
        var created = MySqlDatabaseBootstrapper.TryCreateProvisioningPlan(connectionString, out var plan);

        Assert.False(created);
        Assert.Null(plan);
    }

    [Fact]
    public void BuildCreateDatabaseStatement_EscapesDatabaseName()
    {
        var sql = MySqlDatabaseBootstrapper.BuildCreateDatabaseStatement("test`schema");

        Assert.Equal("CREATE DATABASE IF NOT EXISTS `test``schema` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;", sql);
    }
}
