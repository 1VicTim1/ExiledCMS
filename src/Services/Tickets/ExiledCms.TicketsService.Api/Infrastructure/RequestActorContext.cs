using System.Security.Claims;
using ExiledCms.TicketsService.Api.Domain;

namespace ExiledCms.TicketsService.Api.Infrastructure;

public interface IRequestActorAccessor
{
    RequestActor GetRequiredActor();
}

public sealed class RequestActor
{
    public required Guid UserId { get; init; }

    public required string DisplayName { get; init; }

    public required string Role { get; init; }

    public required HashSet<string> Permissions { get; init; }

    public required string CorrelationId { get; init; }

    public string? CausationId { get; init; }

    public bool IsStaff =>
        Role is "admin" or "administrator" or "moderator" or "support" ||
        Permissions.Contains(TicketPermissions.ReadAll) ||
        Permissions.Contains(TicketPermissions.ReplyStaff) ||
        Permissions.Contains(TicketPermissions.Assign) ||
        Permissions.Contains(TicketPermissions.ChangeStatus);

    public bool HasPermission(string permission) => Permissions.Contains(permission);
}

public sealed class HttpRequestActorAccessor : IRequestActorAccessor
{
    private static readonly char[] PermissionSeparators = [',', ';', ' '];
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpRequestActorAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public RequestActor GetRequiredActor()
    {
        var httpContext = _httpContextAccessor.HttpContext ?? throw ApiException.Unauthorized("No active HTTP context is available.");
        var userIdValue = GetUserId(httpContext);

        if (!Guid.TryParse(userIdValue, out var userId))
        {
            throw ApiException.Unauthorized("A valid user identifier was not provided in claims or X-User-Id header.", "invalid_user_id");
        }

        var displayName = GetValue(httpContext, [ClaimTypes.Name, "name", "preferred_username"], "X-User-Name") ?? userId.ToString("D");
        var role = GetRoles(httpContext).FirstOrDefault() ?? "user";
        var permissions = GetPermissions(httpContext);
        var correlationId = httpContext.Request.Headers["X-Correlation-Id"].FirstOrDefault() ?? httpContext.TraceIdentifier;
        var causationId = httpContext.Request.Headers["X-Causation-Id"].FirstOrDefault();

        return new RequestActor
        {
            UserId = userId,
            DisplayName = displayName,
            Role = role,
            Permissions = permissions,
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("D") : correlationId,
            CausationId = causationId,
        };
    }

    private static string? GetUserId(HttpContext httpContext) =>
        GetValue(httpContext, [ClaimTypes.NameIdentifier, "sub", "user_id"], "X-User-Id");

    private static string? GetValue(HttpContext httpContext, IReadOnlyCollection<string> claimTypes, string headerName)
    {
        foreach (var claimType in claimTypes)
        {
            var claimValue = httpContext.User.FindFirstValue(claimType);
            if (!string.IsNullOrWhiteSpace(claimValue))
            {
                return claimValue;
            }
        }

        var headerValue = httpContext.Request.Headers[headerName].FirstOrDefault();
        return string.IsNullOrWhiteSpace(headerValue) ? null : headerValue.Trim();
    }

    private static IEnumerable<string> GetRoles(HttpContext httpContext)
    {
        var roleClaims = httpContext.User.Claims
            .Where(claim => claim.Type is ClaimTypes.Role or "role" or "roles")
            .SelectMany(claim => SplitValues(claim.Value));

        var headerRoles = SplitValues(httpContext.Request.Headers["X-User-Role"].ToString());
        return roleClaims.Concat(headerRoles).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> GetPermissions(HttpContext httpContext)
    {
        var values = httpContext.User.Claims
            .Where(claim => claim.Type is "permission" or "permissions" or "scope")
            .SelectMany(claim => SplitValues(claim.Value))
            .Concat(SplitValues(httpContext.Request.Headers["X-User-Permissions"].ToString()));

        return new HashSet<string>(values.Where(value => !string.IsNullOrWhiteSpace(value)), StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> SplitValues(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return [];
        }

        return rawValue
            .Split(PermissionSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value));
    }
}
