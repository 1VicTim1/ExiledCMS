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
builder.Services.Configure<NatsOptions>(builder.Configuration.GetSection("Nats"));
builder.Services.Configure<ModuleConfigSyncOptions>(builder.Configuration.GetSection("ModuleConfigSync"));
builder.Services.PostConfigure<JwtOptions>(options =>
{
    if (!string.IsNullOrWhiteSpace(options.Secret))
    {
        return;
    }

    if (builder.Environment.IsDevelopment())
    {
        options.Secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(48));
    }
});

builder.Services.AddHttpClient(nameof(PlatformCoreRegistrationService));
builder.Services.AddSingleton<ModuleRuntimeConfigurationStore>();
builder.Services.AddSingleton<JwtRuntimeOptionsAccessor>();
builder.Services.AddSingleton<PlatformCoreModuleConfigSyncService>();
builder.Services.AddSingleton<MySqlConnectionFactory>();
builder.Services.AddSingleton<SqlMigrationRunner>();
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<TotpService>();
builder.Services.AddSingleton<JwtIssuer>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddHostedService(static serviceProvider => serviceProvider.GetRequiredService<PlatformCoreModuleConfigSyncService>());
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
    var configSync = scope.ServiceProvider.GetRequiredService<PlatformCoreModuleConfigSyncService>();
    var startupLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    try
    {
        await configSync.BootstrapAsync(CancellationToken.None);
    }
    catch (Exception exception)
    {
        startupLogger.LogWarning(exception, "Initial platform-core config bootstrap failed; continuing with local fallback configuration if available");
    }

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

app.MapGet("/readyz", (IOptions<AuthServiceOptions> options, JwtRuntimeOptionsAccessor jwtOptionsAccessor, ModuleRuntimeConfigurationStore configurationStore) =>
{
    var databaseConfigured =
        !string.IsNullOrWhiteSpace(configurationStore.GetDatabaseConnectionStringOrNull()) ||
        !string.IsNullOrWhiteSpace(options.Value.MySqlConnectionString);
    var jwtConfigured = !string.IsNullOrWhiteSpace(jwtOptionsAccessor.GetCurrent().Secret);
    var isReady = databaseConfigured && jwtConfigured;
    return isReady
        ? Results.Ok(new { status = "ready", databaseConfigured, jwtConfigured })
        : Results.Json(new { status = "degraded", databaseConfigured, jwtConfigured }, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapControllers();

app.Run();
