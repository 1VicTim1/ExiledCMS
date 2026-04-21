using System.Text.Json;
using ExiledCms.AuthService.Api.Domain;
using ExiledCms.AuthService.Api.Infrastructure;
using ExiledCms.AuthService.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ExiledCms.AuthService.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IUserService _users;
    private readonly JwtIssuer _jwt;
    private readonly IUserRepository _userRepo;

    public AuthController(IUserService users, JwtIssuer jwt, IUserRepository userRepo)
    {
        _users = users;
        _jwt = jwt;
        _userRepo = userRepo;
    }

    [HttpPost("register")]
    public async Task<ActionResult<UserProfile>> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var profile = await _users.RegisterAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Me), new { }, profile);
        }
        catch (AuthFailure failure)
        {
            return Problem(failure);
        }
    }

    [HttpPost("login")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var (user, roles, permissions) = await _users.AuthenticateAsync(request, cancellationToken);
            var token = _jwt.Issue(user, roles, permissions);
            var profile = await _users.BuildProfileAsync(user, cancellationToken);

            return Ok(new LoginResponse
            {
                AccessToken = token.Token,
                ExpiresAtUtc = token.ExpiresAtUtc,
                User = profile,
            });
        }
        catch (AuthFailure failure)
        {
            return Problem(failure);
        }
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserProfile>> Me(CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId))
        {
            return Unauthorized();
        }

        var user = await _userRepo.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        return Ok(await _users.BuildProfileAsync(user, cancellationToken));
    }

    [HttpPost("password/change")]
    public async Task<ActionResult<UserProfile>> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await _users.ChangePasswordAsync(userId, request, cancellationToken));
        }
        catch (AuthFailure failure)
        {
            return Problem(failure);
        }
    }

    [HttpPost("email/verification")]
    public async Task<ActionResult<VerificationTokenResponse>> IssueEmailVerification(CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await _users.IssueEmailVerificationTokenAsync(userId, cancellationToken));
        }
        catch (AuthFailure failure)
        {
            return Problem(failure);
        }
    }

    [HttpPost("email/confirm")]
    public async Task<ActionResult<UserProfile>> ConfirmEmail([FromBody] ConfirmEmailRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await _users.ConfirmEmailAsync(userId, request, cancellationToken));
        }
        catch (AuthFailure failure)
        {
            return Problem(failure);
        }
    }

    [HttpPost("2fa/setup")]
    public async Task<ActionResult<TotpSetupResponse>> SetupTwoFactor(CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await _users.BeginTotpSetupAsync(userId, cancellationToken));
        }
        catch (AuthFailure failure)
        {
            return Problem(failure);
        }
    }

    [HttpPost("2fa/enable")]
    public async Task<ActionResult<UserProfile>> EnableTwoFactor([FromBody] TotpCodeRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await _users.EnableTotpAsync(userId, request, cancellationToken));
        }
        catch (AuthFailure failure)
        {
            return Problem(failure);
        }
    }

    [HttpPost("2fa/disable")]
    public async Task<ActionResult<UserProfile>> DisableTwoFactor([FromBody] DisableTotpRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await _users.DisableTotpAsync(userId, request, cancellationToken));
        }
        catch (AuthFailure failure)
        {
            return Problem(failure);
        }
    }

    [HttpGet("users")]
    public async Task<ActionResult<IReadOnlyCollection<UserProfile>>> ListUsers(CancellationToken cancellationToken)
    {
        if (HttpContext.Items["auth.permissions"] is not IReadOnlyCollection<string> permissions ||
            !permissions.Contains(AuthPermissions.UsersList, StringComparer.OrdinalIgnoreCase))
        {
            return Forbid();
        }

        return Ok(await _users.ListUsersAsync(cancellationToken));
    }

    private ObjectResult Problem(AuthFailure failure)
    {
        return StatusCode(failure.StatusCode, new
        {
            status = failure.StatusCode,
            code = failure.Code,
            detail = failure.Message,
        });
    }

    private bool TryGetAuthenticatedUserId(out Guid userId)
    {
        if (HttpContext.Items["auth.user_id"] is Guid currentUserId)
        {
            userId = currentUserId;
            return true;
        }

        userId = Guid.Empty;
        return false;
    }
}

public sealed class BearerAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly JwtIssuer _jwt;

    public BearerAuthMiddleware(RequestDelegate next, JwtIssuer jwt)
    {
        _next = next;
        _jwt = jwt;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var token = header["Bearer ".Length..].Trim();
            if (_jwt.TryValidate(token, out var claims) &&
                claims.TryGetValue("sub", out var sub) &&
                sub.ValueKind == JsonValueKind.String &&
                Guid.TryParse(sub.GetString(), out var userId))
            {
                context.Items["auth.user_id"] = userId;

                if (claims.TryGetValue("permissions", out var permissions) && permissions.ValueKind == JsonValueKind.Array)
                {
                    var permissionList = new List<string>(permissions.GetArrayLength());
                    foreach (var permission in permissions.EnumerateArray())
                    {
                        if (permission.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(permission.GetString()))
                        {
                            permissionList.Add(permission.GetString()!);
                        }
                    }

                    context.Items["auth.permissions"] = permissionList;
                }
            }
        }

        await _next(context);
    }
}
