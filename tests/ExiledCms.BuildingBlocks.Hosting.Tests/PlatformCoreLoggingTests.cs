using ExiledCms.BuildingBlocks.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExiledCms.BuildingBlocks.Hosting.Tests;

public sealed class PlatformCoreLoggingExtensionsTests
{
    [Fact]
    public async Task AddExiledCmsPlatformCoreLogging_NormalizesConfiguredOptions()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = "Sample.Module",
            EnvironmentName = Environments.Development,
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlatformCoreLogging:BaseUrl"] = "not-a-valid-url",
            ["PlatformCoreLogging:BatchSize"] = "999",
            ["PlatformCoreLogging:FlushIntervalSeconds"] = "0",
            ["PlatformCoreLogging:MaxQueueSize"] = "999999",
            ["Service:Name"] = "tickets-service",
        });

        builder.AddExiledCmsPlatformCoreLogging();

        await using var app = builder.Build();
        var options = app.Services.GetRequiredService<IOptions<PlatformCoreLogForwardingOptions>>().Value;

        Assert.Equal("http://platform-core:8080", options.BaseUrl);
        Assert.Equal("tickets-service", options.ModuleId);
        Assert.Equal("tickets-service", options.ServiceName);
        Assert.Equal(500, options.BatchSize);
        Assert.Equal(2, options.FlushIntervalSeconds);
        Assert.Equal(50000, options.MaxQueueSize);
    }

    [Fact]
    public void PlatformCoreLogForwardingQueue_DropsOldestEntriesWhenCapacityIsExceeded()
    {
        var options = new PlatformCoreLogForwardingOptions
        {
            MaxQueueSize = 2,
        };
        var monitor = new TestOptionsMonitor<PlatformCoreLogForwardingOptions>(options);
        var queue = new PlatformCoreLogForwardingQueue(monitor);

        queue.Enqueue(new PlatformCoreLogEntry { Message = "first", Level = "info" });
        queue.Enqueue(new PlatformCoreLogEntry { Message = "second", Level = "info" });
        queue.Enqueue(new PlatformCoreLogEntry { Message = "third", Level = "warn" });

        Assert.Equal(2, queue.Count);

        var batch = queue.DequeueBatch(10).ToArray();
        Assert.Equal(2, batch.Length);
        Assert.Equal("second", batch[0].Message);
        Assert.Equal("third", batch[1].Message);
    }

    [Fact]
    public void PlatformCoreLoggerProvider_UsesNullLoggerForInternalCategories_AndQueuesStructuredLogsForApplicationCategories()
    {
        var options = new PlatformCoreLogForwardingOptions
        {
            Enabled = true,
            ModuleId = "tickets-service",
            ServiceName = "Tickets Service",
            MaxQueueSize = 10,
        };
        var monitor = new TestOptionsMonitor<PlatformCoreLogForwardingOptions>(options);
        var queue = new PlatformCoreLogForwardingQueue(monitor);
        using var provider = new PlatformCoreLoggerProvider(queue, monitor);

        var internalLogger = provider.CreateLogger("ExiledCms.BuildingBlocks.Hosting.PlatformCoreLogForwarderService");
        Assert.IsType<NullPlatformCoreLogger>(internalLogger);

        var logger = provider.CreateLogger("Tickets.Service");
        logger.LogInformation("Created ticket {TicketId}", 42);

        var entry = Assert.Single(queue.DequeueBatch(10));
        Assert.Equal("tickets-service", entry.ModuleId);
        Assert.Equal("Tickets Service", entry.Service);
        Assert.Equal("info", entry.Level);
        Assert.Equal("Created ticket 42", entry.Message);
        Assert.NotNull(entry.Attributes);
        Assert.Equal("Tickets.Service", entry.Attributes!["category"]);
        Assert.Equal(42, entry.Attributes["TicketId"]);
        Assert.Equal("Created ticket {TicketId}", entry.Attributes["template"]);
    }

    [Fact]
    public void PlatformCoreLogger_IncludesStructuredExceptionFields()
    {
        var options = new PlatformCoreLogForwardingOptions
        {
            Enabled = true,
            ModuleId = "tickets-service",
            ServiceName = "Tickets Service",
            MaxQueueSize = 10,
        };
        var monitor = new TestOptionsMonitor<PlatformCoreLogForwardingOptions>(options);
        var queue = new PlatformCoreLogForwardingQueue(monitor);
        var logger = new PlatformCoreLogger("Tickets.Service", queue, monitor);

        try
        {
            throw new InvalidOperationException("database timeout");
        }
        catch (InvalidOperationException exception)
        {
            logger.LogError(exception, "Failed to create ticket {TicketId}", 42);
        }

        var entry = Assert.Single(queue.DequeueBatch(10));
        Assert.NotNull(entry.Attributes);
        Assert.Equal("System.InvalidOperationException", entry.Attributes!["exceptionType"]);
        Assert.Equal("database timeout", entry.Attributes["exceptionMessage"]);
        Assert.Contains("InvalidOperationException: database timeout", entry.Attributes["exception"]!.ToString());
        Assert.Contains("PlatformCoreLogger_IncludesStructuredExceptionFields", entry.Attributes["stackTrace"]!.ToString());
    }

    private sealed class TestOptionsMonitor<TOptions> : IOptionsMonitor<TOptions>
        where TOptions : class
    {
        public TestOptionsMonitor(TOptions currentValue)
        {
            CurrentValue = currentValue;
        }

        public TOptions CurrentValue { get; }

        public TOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<TOptions, string?> listener) => null;
    }
}
