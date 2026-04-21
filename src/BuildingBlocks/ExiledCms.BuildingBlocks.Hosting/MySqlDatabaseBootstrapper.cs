using MySqlConnector;

namespace ExiledCms.BuildingBlocks.Hosting;

public sealed record MySqlDatabaseProvisioningPlan(string ServerConnectionString, string DatabaseName);

/// <summary>
/// Creates a module-owned database on first boot when the configured MySQL user
/// has enough privileges. Modules still own their schema via migrations.
/// </summary>
public static class MySqlDatabaseBootstrapper
{
    public static bool TryCreateProvisioningPlan(string? connectionString, out MySqlDatabaseProvisioningPlan? plan)
    {
        plan = null;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return false;
        }

        var builder = new MySqlConnectionStringBuilder(connectionString);
        var databaseName = builder.Database?.Trim();
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            return false;
        }

        builder.Database = string.Empty;
        plan = new MySqlDatabaseProvisioningPlan(builder.ConnectionString, databaseName);
        return true;
    }

    public static string BuildCreateDatabaseStatement(string databaseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        return $"CREATE DATABASE IF NOT EXISTS {EscapeIdentifier(databaseName.Trim())} CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;";
    }

    public static async Task EnsureDatabaseExistsAsync(string connectionString, CancellationToken cancellationToken)
    {
        if (!TryCreateProvisioningPlan(connectionString, out var plan) || plan is null)
        {
            return;
        }

        await using var connection = new MySqlConnection(plan.ServerConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = BuildCreateDatabaseStatement(plan.DatabaseName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string EscapeIdentifier(string value) => $"`{value.Replace("`", "``", StringComparison.Ordinal)}`";
}
