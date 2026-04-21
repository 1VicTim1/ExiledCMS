using ExiledCms.BuildingBlocks.Hosting;
using ExiledCms.TicketsService.Api.Infrastructure;
using ExiledCms.TicketsService.Api.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddExiledCmsPlatformCoreLogging();

builder.Services.Configure<ServiceOptions>(builder.Configuration.GetSection("Service"));
builder.Services.Configure<PlatformCoreOptions>(builder.Configuration.GetSection("PlatformCore"));
builder.Services.Configure<NatsOptions>(builder.Configuration.GetSection("Nats"));
builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection("Outbox"));
builder.Services.Configure<ModuleConfigSyncOptions>(builder.Configuration.GetSection("ModuleConfigSync"));

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient(nameof(PlatformCoreRegistrationService));
builder.Services.AddSingleton<ModuleRuntimeConfigurationStore>();
builder.Services.AddSingleton<PlatformCoreModuleConfigSyncService>();
builder.Services.AddSingleton<MySqlConnectionFactory>();
builder.Services.AddSingleton<SqlMigrationRunner>();
builder.Services.AddSingleton<IRequestActorAccessor, HttpRequestActorAccessor>();
builder.Services.AddSingleton<INatsPublisher, NatsPublisher>();
builder.Services.AddSingleton<ReadinessProbe>();
builder.Services.AddHostedService(static serviceProvider => serviceProvider.GetRequiredService<PlatformCoreModuleConfigSyncService>());
builder.Services.AddHostedService<OutboxDispatcherService>();
builder.Services.AddHostedService<PlatformCoreRegistrationService>();
builder.Services.AddScoped<ITicketService, TicketService>();
builder.Services.AddScoped<ITicketCategoryService, TicketCategoryService>();
builder.Services.AddProblemDetails();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonDefaults.SerializerOptions.PropertyNamingPolicy;
    options.SerializerOptions.DictionaryKeyPolicy = JsonDefaults.SerializerOptions.DictionaryKeyPolicy;
    options.SerializerOptions.DefaultIgnoreCondition = JsonDefaults.SerializerOptions.DefaultIgnoreCondition;
});
builder.Services.AddControllers().AddJsonOptions(options =>
{
    options.JsonSerializerOptions.PropertyNamingPolicy = JsonDefaults.SerializerOptions.PropertyNamingPolicy;
    options.JsonSerializerOptions.DictionaryKeyPolicy = JsonDefaults.SerializerOptions.DictionaryKeyPolicy;
    options.JsonSerializerOptions.DefaultIgnoreCondition = JsonDefaults.SerializerOptions.DefaultIgnoreCondition;
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseExceptionHandler(handler =>
{
    handler.Run(async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;

        var problem = exception is ApiException apiException
            ? apiException.ToProblemDetails(context)
            : new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Unhandled exception",
                Detail = app.Environment.IsDevelopment() ? exception?.ToString() : "The server encountered an unexpected error.",
                Instance = context.Request.Path,
            };

        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsJsonAsync(problem, JsonDefaults.SerializerOptions);
    });
});

app.UseSwagger();
app.UseSwaggerUI();

await using (var scope = app.Services.CreateAsyncScope())
{
    var configSync = scope.ServiceProvider.GetRequiredService<PlatformCoreModuleConfigSyncService>();
    await configSync.BootstrapAsync(CancellationToken.None);

    var migrationRunner = scope.ServiceProvider.GetRequiredService<SqlMigrationRunner>();
    await migrationRunner.ApplyAsync(CancellationToken.None);
}

app.MapGet("/", (IOptions<ServiceOptions> serviceOptions) => Results.Ok(new
{
    service = serviceOptions.Value.Name,
    version = serviceOptions.Value.Version,
    status = "running",
    time = DateTime.UtcNow,
}));

app.MapGet("/healthz", () => Results.Ok(new
{
    status = "ok",
    time = DateTime.UtcNow,
}));

app.MapGet("/readyz", async (ReadinessProbe readinessProbe, CancellationToken cancellationToken) =>
{
    var result = await readinessProbe.CheckAsync(cancellationToken);
    return result.IsReady
        ? Results.Ok(result)
        : Results.Json(result, JsonDefaults.SerializerOptions, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapControllers();

app.Run();
