using ExiledCms.BuildingBlocks.Hosting;
using ExiledCms.ThemesService.Api.Infrastructure;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.AddExiledCmsPlatformCoreLogging();
builder.Services.Configure<ServiceOptions>(builder.Configuration.GetSection("Service"));
builder.Services.Configure<PlatformCoreOptions>(builder.Configuration.GetSection("PlatformCore"));
builder.Services.AddHttpClient(nameof(PlatformCoreRegistrationService));
builder.Services.AddHostedService<PlatformCoreRegistrationService>();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", (IOptions<ServiceOptions> options) => Results.Ok(new
{
    service = options.Value.Name,
    displayName = options.Value.DisplayName,
    version = options.Value.Version,
    status = "running",
    time = DateTime.UtcNow,
}));

app.MapGet("/healthz", () => Results.Ok(new
{
    status = "ok",
    time = DateTime.UtcNow,
}));

app.MapGet("/readyz", (IOptions<ServiceOptions> serviceOptions, IOptions<PlatformCoreOptions> platformCoreOptions) =>
{
    var baseUrlConfigured = !string.IsNullOrWhiteSpace(serviceOptions.Value.BaseUrl);
    var platformCoreConfigured = !string.IsNullOrWhiteSpace(platformCoreOptions.Value.BaseUrl);
    var isReady = baseUrlConfigured && platformCoreConfigured;

    return isReady
        ? Results.Ok(new { status = "ready", baseUrlConfigured, platformCoreConfigured })
        : Results.Json(new { status = "degraded", baseUrlConfigured, platformCoreConfigured }, statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.MapGet("/api/v1/metadata/module-registration", (IOptions<ServiceOptions> options) =>
    Results.Ok(ThemesPlatformCatalog.BuildModule(options.Value)));

app.MapGet("/api/v1/metadata/permissions", () =>
    Results.Ok(new { items = ThemesPlatformCatalog.BuildPermissions() }));

app.MapGet("/api/v1/themes/info", (IOptions<ServiceOptions> options) => Results.Ok(new
{
    service = options.Value.Name,
    message = "themes-service is connected to platform-core and ready for feature implementation.",
}));

app.Run();
