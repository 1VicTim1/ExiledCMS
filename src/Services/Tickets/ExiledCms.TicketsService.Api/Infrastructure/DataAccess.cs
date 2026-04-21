using System.Data;
using System.Text;
using Dapper;
using ExiledCms.BuildingBlocks.Hosting;
using MySqlConnector;

namespace ExiledCms.TicketsService.Api.Infrastructure;

public sealed class MySqlConnectionFactory
{
    private readonly ModuleRuntimeConfigurationStore _configurationStore;
    private readonly SemaphoreSlim _databaseInitializationLock = new(1, 1);
    private volatile bool _databaseInitialized;

    public MySqlConnectionFactory(ModuleRuntimeConfigurationStore configurationStore)
    {
        _configurationStore = configurationStore;
    }

    public async Task<MySqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionString = _configurationStore.GetRequiredDatabaseConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("The tickets database connection string has not been received from platform-core.");
        }

        await EnsureDatabaseExistsOnceAsync(connectionString, cancellationToken);

        var connection = new MySqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private async Task EnsureDatabaseExistsOnceAsync(string connectionString, CancellationToken cancellationToken)
    {
        if (_databaseInitialized)
        {
            return;
        }

        await _databaseInitializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_databaseInitialized)
            {
                return;
            }

            await MySqlDatabaseBootstrapper.EnsureDatabaseExistsAsync(connectionString, cancellationToken);
            _databaseInitialized = true;
        }
        finally
        {
            _databaseInitializationLock.Release();
        }
    }
}

public sealed class ReadinessResult
{
    public bool IsReady { get; init; }

    public DateTime CheckedAtUtc { get; init; }

    public required IReadOnlyDictionary<string, string> Infra { get; init; }
}

public sealed class ReadinessProbe
{
    private readonly MySqlConnectionFactory _connectionFactory;
    private readonly INatsPublisher _natsPublisher;

    public ReadinessProbe(MySqlConnectionFactory connectionFactory, INatsPublisher natsPublisher)
    {
        _connectionFactory = connectionFactory;
        _natsPublisher = natsPublisher;
    }

    public async Task<ReadinessResult> CheckAsync(CancellationToken cancellationToken)
    {
        var infra = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mysql"] = await CheckMySqlAsync(cancellationToken) ? "ok" : "unavailable",
            ["nats"] = await _natsPublisher.CanConnectAsync(cancellationToken) ? "ok" : "unavailable",
        };

        return new ReadinessResult
        {
            IsReady = infra.Values.All(value => string.Equals(value, "ok", StringComparison.OrdinalIgnoreCase)),
            CheckedAtUtc = DateTime.UtcNow,
            Infra = infra,
        };
    }

    private async Task<bool> CheckMySqlAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
            await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1", cancellationToken: cancellationToken));
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class SqlMigrationRunner
{
    private readonly IWebHostEnvironment _environment;
    private readonly MySqlConnectionFactory _connectionFactory;
    private readonly ILogger<SqlMigrationRunner> _logger;

    public SqlMigrationRunner(
        IWebHostEnvironment environment,
        MySqlConnectionFactory connectionFactory,
        ILogger<SqlMigrationRunner> logger)
    {
        _environment = environment;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        var scriptsPath = ResolveScriptsPath(_environment.ContentRootPath, AppContext.BaseDirectory);
        if (scriptsPath is null)
        {
            _logger.LogInformation(
                "Migrations directory does not exist at either {ContentRootScriptsPath} or {BaseDirectoryScriptsPath}",
                Path.Combine(_environment.ContentRootPath, "Migrations", "Scripts"),
                Path.Combine(AppContext.BaseDirectory, "Migrations", "Scripts"));
            return;
        }

        var scriptFiles = Directory.GetFiles(scriptsPath, "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (scriptFiles.Length == 0)
        {
            _logger.LogInformation("No SQL migration files were found in {ScriptsPath}", scriptsPath);
            return;
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await EnsureMigrationsTableAsync(connection, cancellationToken);

        var applied = new HashSet<string>(
            await connection.QueryAsync<string>(new CommandDefinition(
                "SELECT script_name FROM schema_migrations",
                cancellationToken: cancellationToken)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var scriptFile in scriptFiles)
        {
            var scriptName = Path.GetFileName(scriptFile);
            if (applied.Contains(scriptName))
            {
                continue;
            }

            var script = await File.ReadAllTextAsync(scriptFile, cancellationToken);
            var statements = SplitStatements(script);

            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            foreach (var statement in statements)
            {
                await connection.ExecuteAsync(new CommandDefinition(statement, transaction: transaction, cancellationToken: cancellationToken));
            }

            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO schema_migrations (script_name, applied_at_utc) VALUES (@ScriptName, @AppliedAtUtc)",
                new
                {
                    ScriptName = scriptName,
                    AppliedAtUtc = DateTime.UtcNow,
                },
                transaction,
                cancellationToken: cancellationToken));

            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("Applied migration {ScriptName}", scriptName);
        }
    }

    internal static string? ResolveScriptsPath(string contentRootPath, string baseDirectory)
    {
        foreach (var candidate in EnumerateScriptPathCandidates(contentRootPath, baseDirectory))
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateScriptPathCandidates(string contentRootPath, string baseDirectory)
    {
        yield return Path.Combine(contentRootPath, "Migrations", "Scripts");

        var normalizedBaseDirectory = baseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(normalizedBaseDirectory, contentRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Combine(normalizedBaseDirectory, "Migrations", "Scripts");
        }
    }

    private static async Task EnsureMigrationsTableAsync(IDbConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                script_name VARCHAR(255) NOT NULL PRIMARY KEY,
                applied_at_utc DATETIME(6) NOT NULL
            )
            """;

        await connection.ExecuteAsync(new CommandDefinition(sql, cancellationToken: cancellationToken));
    }

    private static IReadOnlyCollection<string> SplitStatements(string script)
    {
        var statements = new List<string>();
        var buffer = new StringBuilder();
        using var reader = new StringReader(script);

        while (reader.ReadLine() is { } line)
        {
            buffer.AppendLine(line);
            if (!line.TrimEnd().EndsWith(';'))
            {
                continue;
            }

            var statement = buffer.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(statement))
            {
                statements.Add(statement[..^1]);
            }

            buffer.Clear();
        }

        var remainder = buffer.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(remainder))
        {
            statements.Add(remainder);
        }

        return statements;
    }
}
