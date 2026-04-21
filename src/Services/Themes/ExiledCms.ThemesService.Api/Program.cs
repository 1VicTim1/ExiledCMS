using ExiledCms.BuildingBlocks.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddExiledCmsPlatformCoreLogging();
var app = builder.Build();

app.MapGet("/", () => "Hello World!");

app.Run();
