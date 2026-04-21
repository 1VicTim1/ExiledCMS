using System.Text;
using Dapper;
using ExiledCms.AuthService.Api.Domain;
using ExiledCms.BuildingBlocks.Hosting;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace ExiledCms.AuthService.Api.Infrastructure;

public sealed class MySqlConnectionFactory
{
    private readonly IOptions<AuthServiceOptions> _options;
    private readonly SemaphoreSlim _databaseInitializationLock = new(1, 1);
    private volatile bool _databaseInitialized;

    public MySqlConnectionFactory(IOptions<AuthServiceOptions> options)
    {
        _options = options;
    }

    public async Task<MySqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var connectionString = _options.Value.MySqlConnectionString;
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Auth.MySqlConnectionString is empty — set Auth__MySqlConnectionString or configure appsettings.");
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

// Runs SQL migration scripts from Migrations/Scripts in lexical order. A single
// schema_migrations table records which scripts have been applied so the same
// binary can safely restart without re-running migrations.
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
            return;
        }

        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "CREATE TABLE IF NOT EXISTS schema_migrations (script_name VARCHAR(255) NOT NULL PRIMARY KEY, applied_at_utc DATETIME(6) NOT NULL)",
            cancellationToken: cancellationToken));

        var applied = new HashSet<string>(
            await connection.QueryAsync<string>(new CommandDefinition("SELECT script_name FROM schema_migrations", cancellationToken: cancellationToken)),
            StringComparer.OrdinalIgnoreCase);

        foreach (var scriptFile in scriptFiles)
        {
            var scriptName = Path.GetFileName(scriptFile);
            if (applied.Contains(scriptName))
            {
                continue;
            }

            var script = await File.ReadAllTextAsync(scriptFile, cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            foreach (var statement in SplitStatements(script))
            {
                await connection.ExecuteAsync(new CommandDefinition(statement, transaction: transaction, cancellationToken: cancellationToken));
            }
            await connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO schema_migrations (script_name, applied_at_utc) VALUES (@ScriptName, @AppliedAtUtc)",
                new { ScriptName = scriptName, AppliedAtUtc = DateTime.UtcNow },
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

// Repository kept intentionally narrow — only the operations the MVP auth flow
// needs. Extra queries (user listing, audit logs, password resets) can be added
// as new methods without touching the domain surface.
public interface IUserRepository
{
    Task<User?> FindByEmailNormalizedAsync(string emailNormalized, CancellationToken cancellationToken);
    Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken);
    Task InsertAsync(User user, CancellationToken cancellationToken);
    Task UpdateLastLoginAsync(Guid userId, DateTime utc, CancellationToken cancellationToken);
    Task UpdatePasswordAsync(Guid userId, PasswordHasher.HashedPassword password, DateTime utc, CancellationToken cancellationToken);
    Task UpdateEmailVerificationAsync(Guid userId, bool emailVerified, string? verificationToken, DateTime utc, CancellationToken cancellationToken);
    Task UpdateTotpAsync(Guid userId, string? totpSecret, bool totpEnabled, DateTime utc, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<string>> GetRoleKeysForUserAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<string>> GetPermissionsForRolesAsync(IEnumerable<string> roleKeys, CancellationToken cancellationToken);
    Task AssignRoleAsync(Guid userId, string roleKey, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<User>> ListAsync(CancellationToken cancellationToken);
}

public sealed class UserRepository : IUserRepository
{
    private readonly MySqlConnectionFactory _connectionFactory;

    public UserRepository(MySqlConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<User?> FindByEmailNormalizedAsync(string emailNormalized, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<UserRow>(new CommandDefinition(
            UserSelect + " WHERE email_normalized = @Email",
            new { Email = emailNormalized },
            cancellationToken: cancellationToken));
        return row?.ToDomain();
    }

    public async Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var row = await connection.QuerySingleOrDefaultAsync<UserRow>(new CommandDefinition(
            UserSelect + " WHERE id = @Id",
            new { Id = id.ToString("D") },
            cancellationToken: cancellationToken));
        return row?.ToDomain();
    }

    public async Task InsertAsync(User user, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO auth_users
                (id, email, email_normalized, email_verified, email_verification_token,
                 display_name, password_hash, password_salt, password_algorithm, password_iterations,
                 totp_secret, totp_enabled, status, created_at_utc, updated_at_utc, last_login_at_utc)
            VALUES
                (@Id, @Email, @EmailNormalized, @EmailVerified, @EmailVerificationToken,
                 @DisplayName, @PasswordHash, @PasswordSalt, @PasswordAlgorithm, @PasswordIterations,
                 @TotpSecret, @TotpEnabled, @Status, @CreatedAtUtc, @UpdatedAtUtc, @LastLoginAtUtc)
            """,
            new
            {
                Id = user.Id.ToString("D"),
                user.Email,
                user.EmailNormalized,
                EmailVerified = user.EmailVerified ? 1 : 0,
                user.EmailVerificationToken,
                user.DisplayName,
                user.PasswordHash,
                user.PasswordSalt,
                user.PasswordAlgorithm,
                user.PasswordIterations,
                user.TotpSecret,
                TotpEnabled = user.TotpEnabled ? 1 : 0,
                user.Status,
                user.CreatedAtUtc,
                user.UpdatedAtUtc,
                user.LastLoginAtUtc,
            },
            cancellationToken: cancellationToken));
    }

    public async Task UpdateLastLoginAsync(Guid userId, DateTime utc, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "UPDATE auth_users SET last_login_at_utc = @Utc, updated_at_utc = @Utc WHERE id = @Id",
            new { Id = userId.ToString("D"), Utc = utc },
            cancellationToken: cancellationToken));
    }

    public async Task UpdatePasswordAsync(Guid userId, PasswordHasher.HashedPassword password, DateTime utc, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE auth_users
            SET password_hash = @PasswordHash,
                password_salt = @PasswordSalt,
                password_algorithm = @PasswordAlgorithm,
                password_iterations = @PasswordIterations,
                updated_at_utc = @Utc
            WHERE id = @Id
            """,
            new
            {
                Id = userId.ToString("D"),
                PasswordHash = password.Hash,
                PasswordSalt = password.Salt,
                PasswordAlgorithm = password.Algorithm,
                PasswordIterations = password.Iterations,
                Utc = utc,
            },
            cancellationToken: cancellationToken));
    }

    public async Task UpdateEmailVerificationAsync(Guid userId, bool emailVerified, string? verificationToken, DateTime utc, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE auth_users
            SET email_verified = @EmailVerified,
                email_verification_token = @EmailVerificationToken,
                updated_at_utc = @Utc
            WHERE id = @Id
            """,
            new
            {
                Id = userId.ToString("D"),
                EmailVerified = emailVerified ? 1 : 0,
                EmailVerificationToken = verificationToken,
                Utc = utc,
            },
            cancellationToken: cancellationToken));
    }

    public async Task UpdateTotpAsync(Guid userId, string? totpSecret, bool totpEnabled, DateTime utc, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE auth_users
            SET totp_secret = @TotpSecret,
                totp_enabled = @TotpEnabled,
                updated_at_utc = @Utc
            WHERE id = @Id
            """,
            new
            {
                Id = userId.ToString("D"),
                TotpSecret = totpSecret,
                TotpEnabled = totpEnabled ? 1 : 0,
                Utc = utc,
            },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyCollection<string>> GetRoleKeysForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT r.key_name
            FROM auth_roles r
            INNER JOIN auth_user_roles ur ON ur.role_id = r.id
            WHERE ur.user_id = @UserId
            """,
            new { UserId = userId.ToString("D") },
            cancellationToken: cancellationToken));
        return rows.ToArray();
    }

    public async Task<IReadOnlyCollection<string>> GetPermissionsForRolesAsync(IEnumerable<string> roleKeys, CancellationToken cancellationToken)
    {
        var keys = roleKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (keys.Length == 0)
        {
            return Array.Empty<string>();
        }
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<string>(new CommandDefinition(
            """
            SELECT DISTINCT rp.permission_key
            FROM auth_role_permissions rp
            INNER JOIN auth_roles r ON r.id = rp.role_id
            WHERE r.key_name IN @Keys
            """,
            new { Keys = keys },
            cancellationToken: cancellationToken));
        return rows.ToArray();
    }

    public async Task AssignRoleAsync(Guid userId, string roleKey, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT IGNORE INTO auth_user_roles (user_id, role_id, assigned_at_utc)
            SELECT @UserId, id, @AssignedAtUtc FROM auth_roles WHERE key_name = @RoleKey
            """,
            new { UserId = userId.ToString("D"), RoleKey = roleKey, AssignedAtUtc = DateTime.UtcNow },
            cancellationToken: cancellationToken));
    }

    public async Task<IReadOnlyCollection<User>> ListAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<UserRow>(new CommandDefinition(
            UserSelect + " ORDER BY created_at_utc DESC",
            cancellationToken: cancellationToken));
        return rows.Select(static row => row.ToDomain()).ToArray();
    }

    private const string UserSelect = """
        SELECT id AS Id, email AS Email, email_normalized AS EmailNormalized, email_verified AS EmailVerified,
               email_verification_token AS EmailVerificationToken, display_name AS DisplayName,
               password_hash AS PasswordHash, password_salt AS PasswordSalt, password_algorithm AS PasswordAlgorithm,
               password_iterations AS PasswordIterations, totp_secret AS TotpSecret, totp_enabled AS TotpEnabled,
               status AS Status, created_at_utc AS CreatedAtUtc, updated_at_utc AS UpdatedAtUtc,
               last_login_at_utc AS LastLoginAtUtc
        FROM auth_users
        """;

    // Row DTO mirrors the MySQL schema so Dapper can bind column types directly
    // (tinyint → bool) without a custom handler.
    internal sealed class UserRow
    {
        public Guid Id { get; set; }
        public string Email { get; set; } = "";
        public string EmailNormalized { get; set; } = "";
        public bool EmailVerified { get; set; }
        public string? EmailVerificationToken { get; set; }
        public string DisplayName { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string PasswordSalt { get; set; } = "";
        public string PasswordAlgorithm { get; set; } = "";
        public int PasswordIterations { get; set; }
        public string? TotpSecret { get; set; }
        public bool TotpEnabled { get; set; }
        public string Status { get; set; } = "active";
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public DateTime? LastLoginAtUtc { get; set; }

        public User ToDomain() => new()
        {
            Id = Id,
            Email = Email,
            EmailNormalized = EmailNormalized,
            EmailVerified = EmailVerified,
            EmailVerificationToken = EmailVerificationToken,
            DisplayName = DisplayName,
            PasswordHash = PasswordHash,
            PasswordSalt = PasswordSalt,
            PasswordAlgorithm = PasswordAlgorithm,
            PasswordIterations = PasswordIterations,
            TotpSecret = TotpSecret,
            TotpEnabled = TotpEnabled,
            Status = Status,
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = UpdatedAtUtc,
            LastLoginAtUtc = LastLoginAtUtc,
        };
    }
}
