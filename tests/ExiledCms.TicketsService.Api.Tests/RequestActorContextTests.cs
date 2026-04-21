using System.Security.Claims;
using ExiledCms.TicketsService.Api.Domain;
using ExiledCms.TicketsService.Api.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace ExiledCms.TicketsService.Api.Tests;

public sealed class RequestActorContextTests
{
    [Fact]
    public void GetRequiredActor_UsesClaimsAndHeadersToBuildActorContext()
    {
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "trace-123";
        context.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "11111111-1111-1111-1111-111111111111"),
            new Claim(ClaimTypes.Name, "Claims User"),
            new Claim(ClaimTypes.Role, "moderator"),
            new Claim("permissions", $"{TicketPermissions.Assign} {TicketPermissions.Create}"),
        }, authenticationType: "Test"));
        context.Request.Headers["X-User-Id"] = "22222222-2222-2222-2222-222222222222";
        context.Request.Headers["X-User-Permissions"] = TicketPermissions.ChangeStatus;
        context.Request.Headers["X-Correlation-Id"] = "corr-1";
        context.Request.Headers["X-Causation-Id"] = "cause-1";

        var accessor = new HttpRequestActorAccessor(new HttpContextAccessor { HttpContext = context });
        var actor = accessor.GetRequiredActor();

        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), actor.UserId);
        Assert.Equal("Claims User", actor.DisplayName);
        Assert.Equal("moderator", actor.Role);
        Assert.Equal("corr-1", actor.CorrelationId);
        Assert.Equal("cause-1", actor.CausationId);
        Assert.True(actor.HasPermission(TicketPermissions.Assign));
        Assert.True(actor.HasPermission(TicketPermissions.ChangeStatus));
        Assert.True(actor.IsStaff);
    }

    [Fact]
    public void GetRequiredActor_FallsBackToHeadersAndDefaults()
    {
        var userId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "trace-456";
        context.User = new ClaimsPrincipal(new ClaimsIdentity());
        context.Request.Headers["X-User-Id"] = userId.ToString("D");
        context.Request.Headers["X-User-Role"] = "user";

        var accessor = new HttpRequestActorAccessor(new HttpContextAccessor { HttpContext = context });
        var actor = accessor.GetRequiredActor();

        Assert.Equal(userId, actor.UserId);
        Assert.Equal(userId.ToString("D"), actor.DisplayName);
        Assert.Equal("user", actor.Role);
        Assert.Equal("trace-456", actor.CorrelationId);
        Assert.False(actor.IsStaff);
    }

    [Fact]
    public void GetRequiredActor_ThrowsUnauthorizedWhenUserIdIsInvalid()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-User-Id"] = "not-a-guid";

        var accessor = new HttpRequestActorAccessor(new HttpContextAccessor { HttpContext = context });
        var exception = Assert.Throws<ApiException>(() => accessor.GetRequiredActor());

        Assert.Equal(StatusCodes.Status401Unauthorized, exception.StatusCode);
        Assert.Equal("invalid_user_id", exception.ErrorCode);
    }
}
