using System.Security.Cryptography;
using ExiledCms.AuthService.Api.Controllers;
using ExiledCms.AuthService.Api.Domain;
using ExiledCms.AuthService.Api.Infrastructure;
using ExiledCms.AuthService.Api.Services;
using ExiledCms.BuildingBlocks.Hosting;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddExiledCmsPlatformCoreLogging();

builder.Services.Configure<AuthServiceOptions>(builder.Configuration.GetSection("Auth"));
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<PlatformCoreOptions>(builder.Configuration.GetSection("PlatformCore"));
builder.Services.PostConfigure<JwtOptions>(options =>
{
    if (!string.IsNullOrWhiteSpace(options.Secret))
    {
        return;
    }

    if (builder.Environment.IsDevelopment())
    {
        options.Secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(48));
        return;
    }

    throw new InvalidOperationException("Jwt:Secret must be configured outside Development.");
});

builder.Services.AddHttpClient(nameof(PlatformCoreRegistrationService));
builder.Services.AddSingleton<MySqlConnectionFactory>();
builder.Services.AddSingleton<SqlMigrationRunner>();
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<TotpService>();
builder.Services.AddSingleton<JwtIssuer>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddHostedService<PlatformCoreRegistrationService>();
builder.Services.AddProblemDetails();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseExceptionHandler(handler =>
{
    handler.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var statusCode = StatusCodes.Status500InternalServerError;
        var detail = app.Environment.IsDevelopment() ? exception?.ToString() : "The server encountered an unexpected error.";

        if (exception is AuthFailure failure)
        {
            statusCode = failure.StatusCode;
            detail = failure.Message;
        }

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = "Auth service error",
            Detail = detail,
            Instance = context.Request.Path,
        };

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    });
});

app.UseSwagger();
app.UseSwaggerUI();
app.UseMiddleware<BearerAuthMiddleware>();

await using (var scope = app.Services.CreateAsyncScope())
{
    var migrationRunner = scope.ServiceProvider.GetRequiredService<SqlMigrationRunner>();
    await migrationRunner.ApplyAsync(CancellationToken.None);
}

app.MapGet("/", (IOptions<AuthServiceOptions> options) => Results.Ok(new
{
    service = options.Value.Name,
    version = options.Value.Version,
    status = "running",
    time = DateTime.UtcNow,
}));

app.MapGet("/healthz", () => Results.Ok(new
{
    status = "ok",
    time = DateTime.UtcNow,
}));

app.MapGet("/readyz", (IOptions<AuthServiceOptions> options) =>
{
    var databaseConfigured = !string.IsNullOrWhiteSpace(options.Value.MySqlConnectionString);
    return databaseConfigured
        ? Results.Ok(new { status = "ready", databaseConfigured })
        : Results.Json(new { status = "degraded", databaseConfigured }, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapControllers();

app.Run();
