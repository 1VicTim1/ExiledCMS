using System.Security.Cryptography;
using ExiledCms.AuthService.Api.Domain;
using ExiledCms.AuthService.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace ExiledCms.AuthService.Api.Services;

public sealed class AuthFailure : Exception
{
    public int StatusCode { get; }
    public string Code { get; }

    public AuthFailure(int statusCode, string code, string message) : base(message)
    {
        StatusCode = statusCode;
        Code = code;
    }
}

public interface IUserService
{
    Task<UserProfile> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<(User User, IReadOnlyCollection<string> Roles, IReadOnlyCollection<string> Permissions)> AuthenticateAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<UserProfile> BuildProfileAsync(User user, CancellationToken cancellationToken);
    Task<UserProfile> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken);
    Task<VerificationTokenResponse> IssueEmailVerificationTokenAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserProfile> ConfirmEmailAsync(Guid userId, ConfirmEmailRequest request, CancellationToken cancellationToken);
    Task<TotpSetupResponse> BeginTotpSetupAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserProfile> EnableTotpAsync(Guid userId, TotpCodeRequest request, CancellationToken cancellationToken);
    Task<UserProfile> DisableTotpAsync(Guid userId, DisableTotpRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<UserProfile>> ListUsersAsync(CancellationToken cancellationToken);
}

public sealed class UserService : IUserService
{
    private readonly IUserRepository _users;
    private readonly PasswordHasher _hasher;
    private readonly TotpService _totp;
    private readonly JwtOptions _jwtOptions;

    public UserService(IUserRepository users, PasswordHasher hasher, TotpService totp, IOptions<JwtOptions> jwtOptions)
    {
        _users = users;
        _hasher = hasher;
        _totp = totp;
        _jwtOptions = jwtOptions.Value;
    }

    public async Task<UserProfile> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var email = (request.Email ?? string.Empty).Trim();
        var displayName = (request.DisplayName ?? string.Empty).Trim();
        var password = request.Password ?? string.Empty;

        if (!email.Contains('@') || email.Length > 254)
        {
            throw new AuthFailure(400, "invalid_email", "A valid email is required.");
        }

        if (displayName.Length is < 2 or > 120)
        {
            throw new AuthFailure(400, "invalid_display_name", "Display name must be between 2 and 120 characters.");
        }

        if (password.Length < 8)
        {
            throw new AuthFailure(400, "weak_password", "Password must be at least 8 characters.");
        }

        var emailNormalized = email.ToLowerInvariant();
        if (await _users.FindByEmailNormalizedAsync(emailNormalized, cancellationToken) is not null)
        {
            throw new AuthFailure(409, "email_in_use", "An account with this email already exists.");
        }

        var hashed = _hasher.Hash(password);
        var now = DateTime.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            EmailNormalized = emailNormalized,
            EmailVerified = false,
            EmailVerificationToken = CreateVerificationToken(),
            DisplayName = displayName,
            PasswordHash = hashed.Hash,
            PasswordSalt = hashed.Salt,
            PasswordAlgorithm = hashed.Algorithm,
            PasswordIterations = hashed.Iterations,
            Status = "active",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        await _users.InsertAsync(user, cancellationToken);
        await _users.AssignRoleAsync(user.Id, "user", cancellationToken);

        return await BuildProfileAsync(user, cancellationToken);
    }

    public async Task<(User User, IReadOnlyCollection<string> Roles, IReadOnlyCollection<string> Permissions)> AuthenticateAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        var password = request.Password ?? string.Empty;
        var user = await _users.FindByEmailNormalizedAsync(email, cancellationToken);

        var dummyHash = _hasher.Hash("not-a-real-password-but-we-still-run-the-kdf");
        var valid = user is not null &&
            _hasher.Verify(password, user.PasswordHash, user.PasswordSalt, user.PasswordAlgorithm, user.PasswordIterations);

        if (user is null || !valid)
        {
            _ = dummyHash;
            throw new AuthFailure(401, "invalid_credentials", "Invalid email or password.");
        }

        if (!string.Equals(user.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            throw new AuthFailure(403, "account_not_active", "Account is not active.");
        }

        if (user.TotpEnabled && !_totp.ValidateCode(user.TotpSecret ?? string.Empty, request.TotpCode))
        {
            throw new AuthFailure(401, "invalid_totp", "A valid two-factor authentication code is required.");
        }

        await _users.UpdateLastLoginAsync(user.Id, DateTime.UtcNow, cancellationToken);

        var roles = await _users.GetRoleKeysForUserAsync(user.Id, cancellationToken);
        var permissions = await _users.GetPermissionsForRolesAsync(roles, cancellationToken);
        return (user, roles, permissions);
    }

    public async Task<UserProfile> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var user = await GetRequiredUserAsync(userId, cancellationToken);
        var currentPassword = request.CurrentPassword ?? string.Empty;
        var newPassword = request.NewPassword ?? string.Empty;

        if (!_hasher.Verify(currentPassword, user.PasswordHash, user.PasswordSalt, user.PasswordAlgorithm, user.PasswordIterations))
        {
            throw new AuthFailure(400, "invalid_current_password", "The current password is invalid.");
        }

        if (newPassword.Length < 8)
        {
            throw new AuthFailure(400, "weak_password", "Password must be at least 8 characters.");
        }

        await _users.UpdatePasswordAsync(user.Id, _hasher.Hash(newPassword), DateTime.UtcNow, cancellationToken);
        return await BuildProfileAsync(await GetRequiredUserAsync(userId, cancellationToken), cancellationToken);
    }

    public async Task<VerificationTokenResponse> IssueEmailVerificationTokenAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await GetRequiredUserAsync(userId, cancellationToken);
        if (user.EmailVerified)
        {
            return new VerificationTokenResponse
            {
                Token = string.Empty,
                EmailVerified = true,
            };
        }

        var token = CreateVerificationToken();
        await _users.UpdateEmailVerificationAsync(user.Id, false, token, DateTime.UtcNow, cancellationToken);

        return new VerificationTokenResponse
        {
            Token = token,
            EmailVerified = false,
        };
    }

    public async Task<UserProfile> ConfirmEmailAsync(Guid userId, ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        var user = await GetRequiredUserAsync(userId, cancellationToken);
        var token = (request.Token ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new AuthFailure(400, "verification_token_required", "A verification token is required.");
        }

        if (!string.Equals(user.EmailVerificationToken, token, StringComparison.Ordinal))
        {
            throw new AuthFailure(400, "invalid_verification_token", "The verification token is invalid.");
        }

        await _users.UpdateEmailVerificationAsync(user.Id, true, null, DateTime.UtcNow, cancellationToken);
        return await BuildProfileAsync(await GetRequiredUserAsync(userId, cancellationToken), cancellationToken);
    }

    public async Task<TotpSetupResponse> BeginTotpSetupAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await GetRequiredUserAsync(userId, cancellationToken);
        var secret = _totp.GenerateSecret();
        await _users.UpdateTotpAsync(user.Id, secret, false, DateTime.UtcNow, cancellationToken);

        var issuer = string.IsNullOrWhiteSpace(_jwtOptions.Issuer) ? "ExiledCMS" : _jwtOptions.Issuer;
        return new TotpSetupResponse
        {
            Secret = secret,
            ManualEntryKey = secret,
            OtpAuthUrl = _totp.BuildOtpAuthUri(issuer, user.Email, secret),
        };
    }

    public async Task<UserProfile> EnableTotpAsync(Guid userId, TotpCodeRequest request, CancellationToken cancellationToken)
    {
        var user = await GetRequiredUserAsync(userId, cancellationToken);
        if (string.IsNullOrWhiteSpace(user.TotpSecret))
        {
            throw new AuthFailure(409, "totp_not_initialized", "Two-factor authentication setup has not been started.");
        }

        if (!_totp.ValidateCode(user.TotpSecret, request.Code))
        {
            throw new AuthFailure(400, "invalid_totp", "The supplied TOTP code is invalid.");
        }

        await _users.UpdateTotpAsync(user.Id, user.TotpSecret, true, DateTime.UtcNow, cancellationToken);
        return await BuildProfileAsync(await GetRequiredUserAsync(userId, cancellationToken), cancellationToken);
    }

    public async Task<UserProfile> DisableTotpAsync(Guid userId, DisableTotpRequest request, CancellationToken cancellationToken)
    {
        var user = await GetRequiredUserAsync(userId, cancellationToken);
        var currentPassword = request.CurrentPassword ?? string.Empty;

        if (!_hasher.Verify(currentPassword, user.PasswordHash, user.PasswordSalt, user.PasswordAlgorithm, user.PasswordIterations))
        {
            throw new AuthFailure(400, "invalid_current_password", "The current password is invalid.");
        }

        await _users.UpdateTotpAsync(user.Id, null, false, DateTime.UtcNow, cancellationToken);
        return await BuildProfileAsync(await GetRequiredUserAsync(userId, cancellationToken), cancellationToken);
    }

    public async Task<IReadOnlyCollection<UserProfile>> ListUsersAsync(CancellationToken cancellationToken)
    {
        var users = await _users.ListAsync(cancellationToken);
        var profiles = new List<UserProfile>(users.Count);
        foreach (var user in users)
        {
            profiles.Add(await BuildProfileAsync(user, cancellationToken));
        }

        return profiles;
    }

    public async Task<UserProfile> BuildProfileAsync(User user, CancellationToken cancellationToken)
    {
        var roles = await _users.GetRoleKeysForUserAsync(user.Id, cancellationToken);
        var permissions = await _users.GetPermissionsForRolesAsync(roles, cancellationToken);
        return new UserProfile
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            EmailVerified = user.EmailVerified,
            TotpEnabled = user.TotpEnabled,
            Status = user.Status,
            Roles = roles,
            Permissions = permissions,
            LastLoginAtUtc = user.LastLoginAtUtc,
        };
    }

    private async Task<User> GetRequiredUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _users.FindByIdAsync(userId, cancellationToken)
            ?? throw new AuthFailure(404, "user_not_found", "The requested user account does not exist.");
    }

    private static string CreateVerificationToken()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
    }
}
