using ExiledCms.AuthService.Api.Domain;
using ExiledCms.AuthService.Api.Infrastructure;
using ExiledCms.AuthService.Api.Services;
using Microsoft.Extensions.Options;

namespace ExiledCms.AuthService.Api.Tests;

public sealed class AuthSecurityAndUserServiceTests
{
    [Fact]
    public void PasswordHasher_HashAndVerify_RoundTrips()
    {
        var hasher = new PasswordHasher();

        var hash = hasher.Hash("correct horse battery staple");

        Assert.True(hasher.Verify("correct horse battery staple", hash.Hash, hash.Salt, hash.Algorithm, hash.Iterations));
        Assert.False(hasher.Verify("wrong password", hash.Hash, hash.Salt, hash.Algorithm, hash.Iterations));
    }

    [Fact]
    public void JwtIssuer_IssueAndValidate_RoundTrips()
    {
        var issuer = CreateJwtIssuer();

        var token = issuer.Issue(
            CreateUser(),
            ["admin"],
            [AuthPermissions.UsersList]);

        Assert.True(issuer.TryValidate(token.Token, out var claims));
        Assert.Equal("ExiledCMS", claims["iss"].GetString());
        Assert.Equal("exiledcms", claims["aud"].GetString());
    }

    [Fact]
    public void TotpService_GeneratedCode_ValidatesWithinWindow()
    {
        var service = new TotpService();
        var now = DateTimeOffset.UtcNow;
        var secret = service.GenerateSecret();
        var code = service.GenerateCode(secret, now);

        Assert.True(service.ValidateCode(secret, code, now));
        Assert.False(service.ValidateCode(secret, "000000", now));
    }

    [Fact]
    public async Task UserService_Register_AssignsDefaultRoleAndVerificationToken()
    {
        var repository = new InMemoryUserRepository();
        var service = CreateUserService(repository);

        var profile = await service.RegisterAsync(new RegisterRequest
        {
            Email = "player@example.com",
            DisplayName = "PlayerOne",
            Password = "Password123!",
        }, CancellationToken.None);

        var stored = await repository.FindByIdAsync(profile.Id, CancellationToken.None);

        Assert.NotNull(stored);
        Assert.False(profile.EmailVerified);
        Assert.Contains("user", profile.Roles);
        Assert.False(string.IsNullOrWhiteSpace(stored!.EmailVerificationToken));
    }

    [Fact]
    public async Task UserService_FullSecurityLifecycle_Works()
    {
        var repository = new InMemoryUserRepository();
        var service = CreateUserService(repository);
        var registered = await service.RegisterAsync(new RegisterRequest
        {
            Email = "security@example.com",
            DisplayName = "SecurePlayer",
            Password = "Password123!",
        }, CancellationToken.None);

        var verification = await service.IssueEmailVerificationTokenAsync(registered.Id, CancellationToken.None);
        var verified = await service.ConfirmEmailAsync(registered.Id, new ConfirmEmailRequest
        {
            Token = verification.Token,
        }, CancellationToken.None);

        Assert.True(verified.EmailVerified);

        await service.ChangePasswordAsync(registered.Id, new ChangePasswordRequest
        {
            CurrentPassword = "Password123!",
            NewPassword = "NewPassword123!",
        }, CancellationToken.None);

        var setup = await service.BeginTotpSetupAsync(registered.Id, CancellationToken.None);
        var totp = new TotpService();
        var code = totp.GenerateCode(setup.Secret, DateTimeOffset.UtcNow);

        var enabled = await service.EnableTotpAsync(registered.Id, new TotpCodeRequest
        {
            Code = code,
        }, CancellationToken.None);

        Assert.True(enabled.TotpEnabled);

        var failure = await Assert.ThrowsAsync<AuthFailure>(() => service.AuthenticateAsync(new LoginRequest
        {
            Email = "security@example.com",
            Password = "NewPassword123!",
        }, CancellationToken.None));

        Assert.Equal("invalid_totp", failure.Code);

        var authenticated = await service.AuthenticateAsync(new LoginRequest
        {
            Email = "security@example.com",
            Password = "NewPassword123!",
            TotpCode = totp.GenerateCode(setup.Secret, DateTimeOffset.UtcNow),
        }, CancellationToken.None);

        Assert.Equal(registered.Id, authenticated.User.Id);

        var disabled = await service.DisableTotpAsync(registered.Id, new DisableTotpRequest
        {
            CurrentPassword = "NewPassword123!",
        }, CancellationToken.None);

        Assert.False(disabled.TotpEnabled);
    }

    [Fact]
    public void SqlMigrationRunner_ResolveScriptsPath_FallsBackToBaseDirectory()
    {
        var root = Directory.CreateTempSubdirectory("auth-migrations-test");
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
    public void UserRow_ToDomain_PreservesGuidIdentifier()
    {
        var id = Guid.NewGuid();
        var row = new UserRepository.UserRow
        {
            Id = id,
            Email = "player@example.com",
            EmailNormalized = "player@example.com",
            EmailVerified = true,
            EmailVerificationToken = "verify-token",
            DisplayName = "PlayerOne",
            PasswordHash = "hash",
            PasswordSalt = "salt",
            PasswordAlgorithm = "pbkdf2",
            PasswordIterations = 100000,
            TotpSecret = "totp-secret",
            TotpEnabled = true,
            Status = "active",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            LastLoginAtUtc = DateTime.UtcNow,
        };

        var domain = row.ToDomain();

        Assert.Equal(id, domain.Id);
        Assert.Equal(row.Email, domain.Email);
        Assert.True(domain.TotpEnabled);
    }

    private static UserService CreateUserService(InMemoryUserRepository repository)
    {
        return new UserService(
            repository,
            new PasswordHasher(),
            new TotpService(),
            CreateJwtOptionsAccessor());
    }

    private static JwtIssuer CreateJwtIssuer()
    {
        return new JwtIssuer(CreateJwtOptionsAccessor());
    }

    private static JwtRuntimeOptionsAccessor CreateJwtOptionsAccessor()
    {
        return new JwtRuntimeOptionsAccessor(
            new ModuleRuntimeConfigurationStore(),
            Options.Create(new JwtOptions
            {
                Secret = "test-secret-value",
                Issuer = "ExiledCMS",
                Audience = "exiledcms",
                AccessTokenLifetimeMinutes = 60,
            }));
    }

    private static User CreateUser()
    {
        var hasher = new PasswordHasher();
        var password = hasher.Hash("Password123!");

        return new User
        {
            Id = Guid.NewGuid(),
            Email = "admin@example.com",
            EmailNormalized = "admin@example.com",
            EmailVerified = true,
            DisplayName = "Admin",
            PasswordHash = password.Hash,
            PasswordSalt = password.Salt,
            PasswordAlgorithm = password.Algorithm,
            PasswordIterations = password.Iterations,
            Status = "active",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };
    }

    private sealed class InMemoryUserRepository : IUserRepository
    {
        private readonly Dictionary<Guid, User> _users = [];
        private readonly Dictionary<Guid, HashSet<string>> _roles = [];
        private readonly Dictionary<string, HashSet<string>> _permissions = new(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = new(StringComparer.OrdinalIgnoreCase),
            ["admin"] = new(StringComparer.OrdinalIgnoreCase)
            {
                AuthPermissions.UsersList,
                AuthPermissions.UsersView,
                AuthPermissions.UsersEdit,
                AuthPermissions.UsersDelete,
                AuthPermissions.UsersBan,
                AuthPermissions.RolesManage,
                AuthPermissions.PermissionsManage,
            },
        };

        public Task<User?> FindByEmailNormalizedAsync(string emailNormalized, CancellationToken cancellationToken)
        {
            return Task.FromResult(_users.Values.FirstOrDefault(user =>
                string.Equals(user.EmailNormalized, emailNormalized, StringComparison.OrdinalIgnoreCase)));
        }

        public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
        {
            _users.TryGetValue(id, out var user);
            return Task.FromResult(user);
        }

        public Task InsertAsync(User user, CancellationToken cancellationToken)
        {
            _users[user.Id] = Clone(user);
            return Task.CompletedTask;
        }

        public Task UpdateLastLoginAsync(Guid userId, DateTime utc, CancellationToken cancellationToken)
        {
            var user = _users[userId];
            user.LastLoginAtUtc = utc;
            user.UpdatedAtUtc = utc;
            return Task.CompletedTask;
        }

        public Task UpdatePasswordAsync(Guid userId, PasswordHasher.HashedPassword password, DateTime utc, CancellationToken cancellationToken)
        {
            var user = _users[userId];
            user.PasswordHash = password.Hash;
            user.PasswordSalt = password.Salt;
            user.PasswordAlgorithm = password.Algorithm;
            user.PasswordIterations = password.Iterations;
            user.UpdatedAtUtc = utc;
            return Task.CompletedTask;
        }

        public Task UpdateEmailVerificationAsync(Guid userId, bool emailVerified, string? verificationToken, DateTime utc, CancellationToken cancellationToken)
        {
            var user = _users[userId];
            user.EmailVerified = emailVerified;
            user.EmailVerificationToken = verificationToken;
            user.UpdatedAtUtc = utc;
            return Task.CompletedTask;
        }

        public Task UpdateTotpAsync(Guid userId, string? totpSecret, bool totpEnabled, DateTime utc, CancellationToken cancellationToken)
        {
            var user = _users[userId];
            user.TotpSecret = totpSecret;
            user.TotpEnabled = totpEnabled;
            user.UpdatedAtUtc = utc;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<string>> GetRoleKeysForUserAsync(Guid userId, CancellationToken cancellationToken)
        {
            _roles.TryGetValue(userId, out var roles);
            return Task.FromResult<IReadOnlyCollection<string>>((roles ?? []).ToArray());
        }

        public Task<IReadOnlyCollection<string>> GetPermissionsForRolesAsync(IEnumerable<string> roleKeys, CancellationToken cancellationToken)
        {
            var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var roleKey in roleKeys)
            {
                if (_permissions.TryGetValue(roleKey, out var rolePermissions))
                {
                    permissions.UnionWith(rolePermissions);
                }
            }

            return Task.FromResult<IReadOnlyCollection<string>>(permissions.ToArray());
        }

        public Task AssignRoleAsync(Guid userId, string roleKey, CancellationToken cancellationToken)
        {
            if (!_roles.TryGetValue(userId, out var roles))
            {
                roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                _roles[userId] = roles;
            }

            roles.Add(roleKey);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<User>> ListAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyCollection<User>>(_users.Values.Select(Clone).ToArray());
        }

        private static User Clone(User user)
        {
            return new User
            {
                Id = user.Id,
                Email = user.Email,
                EmailNormalized = user.EmailNormalized,
                EmailVerified = user.EmailVerified,
                EmailVerificationToken = user.EmailVerificationToken,
                DisplayName = user.DisplayName,
                PasswordHash = user.PasswordHash,
                PasswordSalt = user.PasswordSalt,
                PasswordAlgorithm = user.PasswordAlgorithm,
                PasswordIterations = user.PasswordIterations,
                TotpSecret = user.TotpSecret,
                TotpEnabled = user.TotpEnabled,
                Status = user.Status,
                CreatedAtUtc = user.CreatedAtUtc,
                UpdatedAtUtc = user.UpdatedAtUtc,
                LastLoginAtUtc = user.LastLoginAtUtc,
            };
        }
    }
}
