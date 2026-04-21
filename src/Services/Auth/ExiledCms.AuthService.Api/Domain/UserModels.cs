namespace ExiledCms.AuthService.Api.Domain;

// Minimal-config design: options maps 1:1 to appsettings, no module-config-sync
// round-trip. Operators fill either env vars or appsettings.json.
public sealed class AuthServiceOptions
{
    public string Name { get; set; } = "auth-service";
    public string Version { get; set; } = "1.0.0";
    public string BaseUrl { get; set; } = "http://auth-service:8080";
    public string MySqlConnectionString { get; set; } = "";
    public string OpenApiJsonPath { get; set; } = "/swagger/v1/swagger.json";
    public string SwaggerUiPath { get; set; } = "/swagger";

    public string GetOpenApiUrl() => BaseUrl.TrimEnd('/') + "/" + OpenApiJsonPath.TrimStart('/');
    public string GetSwaggerUiUrl() => BaseUrl.TrimEnd('/') + "/" + SwaggerUiPath.TrimStart('/');
}

public sealed class JwtOptions
{
    // Secret is required: the service refuses to start if this stays empty in
    // production. A random one-off secret is generated in Development only.
    public string Secret { get; set; } = "";
    public string Issuer { get; set; } = "exiledcms-auth";
    public string Audience { get; set; } = "exiledcms";
    public int AccessTokenLifetimeMinutes { get; set; } = 60;
}

public sealed class PlatformCoreOptions
{
    public string BaseUrl { get; set; } = "http://platform-core:8080";
    public bool AutoRegister { get; set; } = true;
    public int RetryIntervalSeconds { get; set; } = 30;
}

public sealed class User
{
    public required Guid Id { get; init; }
    public required string Email { get; init; }
    public required string EmailNormalized { get; init; }
    public bool EmailVerified { get; set; }
    public string? EmailVerificationToken { get; set; }
    public required string DisplayName { get; set; }
    public required string PasswordHash { get; set; }
    public required string PasswordSalt { get; set; }
    public required string PasswordAlgorithm { get; set; }
    public required int PasswordIterations { get; set; }
    public string? TotpSecret { get; set; }
    public bool TotpEnabled { get; set; }
    public string Status { get; set; } = "active";
    public DateTime CreatedAtUtc { get; init; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? LastLoginAtUtc { get; set; }
}

public sealed class UserProfile
{
    public required Guid Id { get; init; }
    public required string Email { get; init; }
    public required string DisplayName { get; init; }
    public required bool EmailVerified { get; init; }
    public required bool TotpEnabled { get; init; }
    public required string Status { get; init; }
    public required IReadOnlyCollection<string> Roles { get; init; }
    public required IReadOnlyCollection<string> Permissions { get; init; }
    public DateTime? LastLoginAtUtc { get; init; }
}

// Transport DTOs kept deliberately flat — no nested request envelopes.
public sealed class RegisterRequest
{
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class LoginRequest
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string? TotpCode { get; set; }
}

public sealed class LoginResponse
{
    public required string AccessToken { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
    public required UserProfile User { get; init; }
}

public sealed class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

public sealed class ConfirmEmailRequest
{
    public string Token { get; set; } = "";
}

public sealed class VerificationTokenResponse
{
    // Returned directly until SMTP/sendmail integration is implemented.
    public required string Token { get; init; }
    public required bool EmailVerified { get; init; }
}

public sealed class TotpSetupResponse
{
    public required string Secret { get; init; }
    public required string ManualEntryKey { get; init; }
    public required string OtpAuthUrl { get; init; }
}

public sealed class TotpCodeRequest
{
    public string Code { get; set; } = "";
}

public sealed class DisableTotpRequest
{
    public string CurrentPassword { get; set; } = "";
}

// Canonical auth-module permissions, registered with platform-core at startup.
public static class AuthPermissions
{
    public const string UsersList = "auth.users.list";
    public const string UsersView = "auth.users.view";
    public const string UsersEdit = "auth.users.edit";
    public const string UsersDelete = "auth.users.delete";
    public const string UsersBan = "auth.users.ban";
    public const string RolesManage = "auth.roles.manage";
    public const string PermissionsManage = "auth.permissions.manage";

    public static IReadOnlyCollection<(string Key, string DisplayName, string Description)> All { get; } =
    [
        (UsersList, "List users", "Allows listing the user directory."),
        (UsersView, "View user", "Allows viewing user profile details."),
        (UsersEdit, "Edit user", "Allows updating non-destructive user fields."),
        (UsersDelete, "Delete user", "Allows removing a user account."),
        (UsersBan, "Ban user", "Allows suspending a user account."),
        (RolesManage, "Manage roles", "Allows creation and maintenance of auth roles."),
        (PermissionsManage, "Manage role permissions", "Allows granting or revoking permissions on roles."),
    ];
}
