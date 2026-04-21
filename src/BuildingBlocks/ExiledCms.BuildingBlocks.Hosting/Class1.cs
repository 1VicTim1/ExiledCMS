using System.Collections.Concurrent;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExiledCms.BuildingBlocks.Hosting;

public sealed class PlatformCoreLogForwardingOptions
{
    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "http://platform-core:8080";

    public string ModuleId { get; set; } = string.Empty;

    public string ServiceName { get; set; } = string.Empty;

    public int BatchSize { get; set; } = 100;

    public int FlushIntervalSeconds { get; set; } = 2;

    public int MaxQueueSize { get; set; } = 5000;
}

public static class PlatformCoreLoggingExtensions
{
    public static WebApplicationBuilder AddExiledCmsPlatformCoreLogging(this WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<PlatformCoreLogForwardingOptions>()
            .Configure<IConfiguration, IHostEnvironment>((options, configuration, environment) =>
            {
                configuration.GetSection("PlatformCoreLogging").Bind(options);

                var configuredBaseUrl = FirstNonEmpty(
                    configuration["PlatformCoreLogging:BaseUrl"],
                    configuration["PlatformCore:BaseUrl"],
                    configuration["PLATFORM_CORE_BASE_URL"],
                    options.BaseUrl);

                var configuredModuleId = FirstNonEmpty(
                    configuration["PlatformCoreLogging:ModuleId"],
                    configuration["Service:Name"],
                    configuration["SERVICE_NAME"],
                    environment.ApplicationName,
                    "module");

                var configuredServiceName = FirstNonEmpty(
                    configuration["PlatformCoreLogging:ServiceName"],
                    configuration["Service:Name"],
                    configuredModuleId);

                options.BaseUrl = NormalizeBaseUrl(configuredBaseUrl);
                options.ModuleId = configuredModuleId;
                options.ServiceName = configuredServiceName;
                options.BatchSize = options.BatchSize <= 0 ? 100 : Math.Min(options.BatchSize, 500);
                options.FlushIntervalSeconds = options.FlushIntervalSeconds <= 0 ? 2 : Math.Min(options.FlushIntervalSeconds, 30);
                options.MaxQueueSize = options.MaxQueueSize <= 0 ? 5000 : Math.Min(options.MaxQueueSize, 50000);
            });

        builder.Services.AddSingleton<PlatformCoreLogForwardingQueue>();
        builder.Services.AddHttpClient(PlatformCoreLogForwarderService.HttpClientName, (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptionsMonitor<PlatformCoreLogForwardingOptions>>().CurrentValue;
            client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
            client.Timeout = TimeSpan.FromSeconds(5);
        });
        builder.Services.AddHostedService<PlatformCoreLogForwarderService>();
        builder.Logging.Services.AddSingleton<ILoggerProvider, PlatformCoreLoggerProvider>();
        builder.Logging.AddFilter<PlatformCoreLoggerProvider>(string.Empty, LogLevel.Trace);

        return builder;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string NormalizeBaseUrl(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value)
            ? "http://platform-core:8080"
            : value.Trim();

        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            ? uri.ToString().TrimEnd('/')
            : "http://platform-core:8080";
    }
}

internal sealed class PlatformCoreLogForwardingQueue
{
    private readonly ConcurrentQueue<PlatformCoreLogEntry> _entries = new();
    private readonly IOptionsMonitor<PlatformCoreLogForwardingOptions> _optionsMonitor;
    private int _count;

    public PlatformCoreLogForwardingQueue(IOptionsMonitor<PlatformCoreLogForwardingOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    public int Count => Math.Max(0, Interlocked.CompareExchange(ref _count, 0, 0));

    public void Enqueue(PlatformCoreLogEntry entry)
    {
        _entries.Enqueue(entry);
        var count = Interlocked.Increment(ref _count);
        var maxQueueSize = Math.Max(1, _optionsMonitor.CurrentValue.MaxQueueSize);

        while (count > maxQueueSize && _entries.TryDequeue(out _))
        {
            count = Interlocked.Decrement(ref _count);
        }
    }

    public IReadOnlyCollection<PlatformCoreLogEntry> DequeueBatch(int batchSize)
    {
        var limit = batchSize <= 0 ? 100 : batchSize;
        var items = new List<PlatformCoreLogEntry>(limit);
        while (items.Count < limit && _entries.TryDequeue(out var entry))
        {
            Interlocked.Decrement(ref _count);
            items.Add(entry);
        }

        return items;
    }

    public void Requeue(IEnumerable<PlatformCoreLogEntry> items)
    {
        foreach (var item in items)
        {
            Enqueue(item);
        }
    }
}

internal sealed class PlatformCoreLogForwarderService : BackgroundService
{
    public const string HttpClientName = "PlatformCoreLogForwarding";

    private readonly PlatformCoreLogForwardingQueue _queue;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<PlatformCoreLogForwardingOptions> _optionsMonitor;

    public PlatformCoreLogForwarderService(
        PlatformCoreLogForwardingQueue queue,
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<PlatformCoreLogForwardingOptions> optionsMonitor)
    {
        _queue = queue;
        _httpClientFactory = httpClientFactory;
        _optionsMonitor = optionsMonitor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _optionsMonitor.CurrentValue.FlushIntervalSeconds)));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await FlushAsync(stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await FlushAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;
        if (!options.Enabled)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            var batch = _queue.DequeueBatch(options.BatchSize);
            if (batch.Count == 0)
            {
                return;
            }

            try
            {
                var client = _httpClientFactory.CreateClient(HttpClientName);
                using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/platform/logs")
                {
                    Content = JsonContent.Create(new PlatformCoreLogIngestRequest
                    {
                        Entries = batch,
                    }),
                };

                if (!string.IsNullOrWhiteSpace(options.ModuleId))
                {
                    request.Headers.TryAddWithoutValidation("X-Module-Id", options.ModuleId);
                }

                if (!string.IsNullOrWhiteSpace(options.ServiceName))
                {
                    request.Headers.TryAddWithoutValidation("X-Module-Service", options.ServiceName);
                }

                using var response = await client.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _queue.Requeue(batch);
                    return;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _queue.Requeue(batch);
                return;
            }
            catch
            {
                _queue.Requeue(batch);
                return;
            }
        }
    }
}

internal sealed class PlatformCoreLoggerProvider : ILoggerProvider
{
    private readonly PlatformCoreLogForwardingQueue _queue;
    private readonly IOptionsMonitor<PlatformCoreLogForwardingOptions> _optionsMonitor;

    public PlatformCoreLoggerProvider(
        PlatformCoreLogForwardingQueue queue,
        IOptionsMonitor<PlatformCoreLogForwardingOptions> optionsMonitor)
    {
        _queue = queue;
        _optionsMonitor = optionsMonitor;
    }

    public ILogger CreateLogger(string categoryName)
    {
        if (categoryName.StartsWith("ExiledCms.BuildingBlocks.Hosting.", StringComparison.Ordinal) ||
            categoryName.StartsWith($"System.Net.Http.HttpClient.{PlatformCoreLogForwarderService.HttpClientName}", StringComparison.Ordinal))
        {
            return NullPlatformCoreLogger.Instance;
        }

        return new PlatformCoreLogger(categoryName, _queue, _optionsMonitor);
    }

    public void Dispose()
    {
    }
}

internal sealed class PlatformCoreLogger : ILogger
{
    private readonly string _categoryName;
    private readonly PlatformCoreLogForwardingQueue _queue;
    private readonly IOptionsMonitor<PlatformCoreLogForwardingOptions> _optionsMonitor;

    public PlatformCoreLogger(
        string categoryName,
        PlatformCoreLogForwardingQueue queue,
        IOptionsMonitor<PlatformCoreLogForwardingOptions> optionsMonitor)
    {
        _categoryName = categoryName;
        _queue = queue;
        _optionsMonitor = optionsMonitor;
    }

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) =>
        logLevel != LogLevel.None &&
        _optionsMonitor.CurrentValue.Enabled;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var options = _optionsMonitor.CurrentValue;
        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message) && exception is null)
        {
            return;
        }

        var attributes = BuildAttributes(state, eventId, exception);
        _queue.Enqueue(new PlatformCoreLogEntry
        {
            Timestamp = DateTime.UtcNow,
            ModuleId = options.ModuleId,
            Service = options.ServiceName,
            Level = MapLevel(logLevel),
            Message = string.IsNullOrWhiteSpace(message) ? exception?.Message ?? _categoryName : message,
            Attributes = attributes.Count == 0 ? null : attributes,
        });
    }

    private Dictionary<string, object?> BuildAttributes<TState>(TState state, EventId eventId, Exception? exception)
    {
        var attributes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["category"] = _categoryName,
        };

        if (eventId.Id != 0)
        {
            attributes["eventId"] = eventId.Id;
        }

        if (!string.IsNullOrWhiteSpace(eventId.Name))
        {
            attributes["eventName"] = eventId.Name;
        }

        if (exception is not null)
        {
            attributes["exceptionType"] = exception.GetType().FullName;
            attributes["exceptionMessage"] = exception.Message;
            attributes["exception"] = exception.ToString();
            attributes["stackTrace"] = exception.StackTrace;
        }

        if (state is IEnumerable<KeyValuePair<string, object?>> properties)
        {
            foreach (var property in properties)
            {
                if (string.Equals(property.Key, "{OriginalFormat}", StringComparison.Ordinal) ||
                    string.Equals(property.Key, "OriginalFormat", StringComparison.Ordinal))
                {
                    attributes["template"] = NormalizeValue(property.Value);
                    continue;
                }

                attributes[property.Key] = NormalizeValue(property.Value);
            }
        }

        return attributes;
    }

    private static object? NormalizeValue(object? value) => value switch
    {
        null => null,
        string text => text,
        Guid guid => guid,
        bool boolean => boolean,
        byte number => number,
        sbyte number => number,
        short number => number,
        ushort number => number,
        int number => number,
        uint number => number,
        long number => number,
        ulong number => number,
        float number => number,
        double number => number,
        decimal number => number,
        DateTime dateTime => dateTime.ToUniversalTime(),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToUniversalTime(),
        TimeSpan timeSpan => timeSpan.ToString(),
        Enum enumeration => enumeration.ToString(),
        Exception exception => exception.ToString(),
        _ => value.ToString(),
    };

    private static string MapLevel(LogLevel logLevel) => logLevel switch
    {
        LogLevel.Trace => "debug",
        LogLevel.Debug => "debug",
        LogLevel.Information => "info",
        LogLevel.Warning => "warn",
        LogLevel.Error => "error",
        LogLevel.Critical => "fatal",
        _ => "info",
    };
}

internal sealed class PlatformCoreLogIngestRequest
{
    public required IReadOnlyCollection<PlatformCoreLogEntry> Entries { get; init; }
}

internal sealed class PlatformCoreLogEntry
{
    public DateTime Timestamp { get; init; }

    public string Service { get; init; } = string.Empty;

    public string ModuleId { get; init; } = string.Empty;

    public string Level { get; init; } = "info";

    public string Message { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, object?>? Attributes { get; init; }
}

internal sealed class NullPlatformCoreLogger : ILogger
{
    public static NullPlatformCoreLogger Instance { get; } = new();

    public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => false;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
    }
}

internal sealed class NullScope : IDisposable
{
    public static NullScope Instance { get; } = new();

    public void Dispose()
    {
    }
}
