using ExiledCms.TicketsService.Api.Domain;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Extensions.Options;
using NATS.Client;

namespace ExiledCms.TicketsService.Api.Infrastructure;

public interface INatsPublisher
{
    Task PublishAsync(string subject, string payloadJson, CancellationToken cancellationToken);

    Task<bool> CanConnectAsync(CancellationToken cancellationToken);
}

public sealed class NatsPublisher : INatsPublisher
{
    private readonly IOptions<NatsOptions> _options;

    public NatsPublisher(IOptions<NatsOptions> options)
    {
        _options = options;
    }

    public Task PublishAsync(string subject, string payloadJson, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var connectionOptions = ConnectionFactory.GetDefaultOptions();
        connectionOptions.Url = _options.Value.Url;

        using var connection = new ConnectionFactory().CreateConnection(connectionOptions);
        connection.Publish(subject, System.Text.Encoding.UTF8.GetBytes(payloadJson));
        connection.Flush();
        return Task.CompletedTask;
    }

    public Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var connectionOptions = ConnectionFactory.GetDefaultOptions();
            connectionOptions.Url = _options.Value.Url;

            using var connection = new ConnectionFactory().CreateConnection(connectionOptions);
            connection.Flush();
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }
}

public sealed class OutboxDispatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<OutboxOptions> _options;
    private readonly ILogger<OutboxDispatcherService> _logger;

    public OutboxDispatcherService(
        IServiceScopeFactory scopeFactory,
        IOptions<OutboxOptions> options,
        ILogger<OutboxDispatcherService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _options.Value.DispatchIntervalSeconds)));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Failed to dispatch ticket outbox batch");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task DispatchBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var connectionFactory = scope.ServiceProvider.GetRequiredService<MySqlConnectionFactory>();
        var natsPublisher = scope.ServiceProvider.GetRequiredService<INatsPublisher>();

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var events = (await connection.QueryAsync<OutboxEventRow>(new CommandDefinition(
            commandText: """
                SELECT id AS Id, subject AS Subject, envelope_json AS EnvelopeJson
                FROM ticket_outbox_events
                WHERE published_at_utc IS NULL
                ORDER BY occurred_at_utc ASC
                LIMIT @BatchSize
                FOR UPDATE SKIP LOCKED
                """,
            parameters: new { BatchSize = Math.Max(1, _options.Value.BatchSize) },
            transaction: transaction,
            cancellationToken: cancellationToken))).ToArray();

        if (events.Length == 0)
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        foreach (var pendingEvent in events)
        {
            try
            {
                await natsPublisher.PublishAsync(pendingEvent.Subject, pendingEvent.EnvelopeJson, cancellationToken);
                await connection.ExecuteAsync(new CommandDefinition(
                    commandText: """
                        UPDATE ticket_outbox_events
                        SET published_at_utc = @PublishedAtUtc,
                            attempt_count = attempt_count + 1,
                            last_error = NULL
                        WHERE id = @Id
                        """,
                    parameters: new
                    {
                        Id = pendingEvent.Id.ToString("D"),
                        PublishedAtUtc = DateTime.UtcNow,
                    },
                    transaction: transaction,
                    cancellationToken: cancellationToken));
            }
            catch (Exception exception)
            {
                await connection.ExecuteAsync(new CommandDefinition(
                    commandText: """
                        UPDATE ticket_outbox_events
                        SET attempt_count = attempt_count + 1,
                            last_error = @LastError
                        WHERE id = @Id
                        """,
                    parameters: new
                    {
                        Id = pendingEvent.Id.ToString("D"),
                        LastError = exception.Message.Length > 4000 ? exception.Message[..4000] : exception.Message,
                    },
                    transaction: transaction,
                    cancellationToken: cancellationToken));

                _logger.LogWarning(exception, "Failed to publish outbox event {OutboxEventId}", pendingEvent.Id);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }
}

public sealed class PlatformModuleRegistration
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required string Kind { get; init; }
    public required string BaseUrl { get; init; }
    public required string HealthUrl { get; init; }
    public string? OpenApiUrl { get; init; }
    public string? SwaggerUiUrl { get; init; }
    public string? ConfigRequestSubject { get; init; }
    public string? ConfigDesiredSubject { get; init; }
    public string? ConfigReportedSubject { get; init; }
    public required DateTime RegisteredAt { get; init; }
    public required IReadOnlyCollection<string> OwnedCapabilities { get; init; }
    public required IReadOnlyCollection<string> Tags { get; init; }
    public PlatformModuleTopology? Topology { get; init; }
    public IReadOnlyCollection<PlatformDocumentationLink>? Documentation { get; init; }
}

public sealed class PlatformModuleTopology
{
    public string? DeploymentMode { get; init; }
    public IReadOnlyCollection<string>? DataSources { get; init; }
    public IReadOnlyCollection<string>? Dependencies { get; init; }
}

public sealed class PlatformDocumentationLink
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Href { get; init; }
    public string? Description { get; init; }
}

public sealed class PlatformPermissionDefinition
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required string Scope { get; init; }
    public required string Description { get; init; }
    public bool Dangerous { get; init; }
}

public static class TicketPlatformCatalog
{
    public static IReadOnlyCollection<PlatformPermissionDefinition> BuildPermissions() =>
    [
        new PlatformPermissionDefinition { Key = "ticket.create", DisplayName = "Create ticket", Scope = "tickets", Description = "Allows a user to create support tickets." },
        new PlatformPermissionDefinition { Key = "ticket.read.own", DisplayName = "Read own tickets", Scope = "tickets", Description = "Allows access to tickets created by the current user." },
        new PlatformPermissionDefinition { Key = "ticket.read.all", DisplayName = "Read all tickets", Scope = "tickets", Description = "Allows staff to view all support tickets." },
        new PlatformPermissionDefinition { Key = "ticket.reply.own", DisplayName = "Reply to own tickets", Scope = "tickets", Description = "Allows a user to post new messages into their own tickets." },
        new PlatformPermissionDefinition { Key = "ticket.reply.staff", DisplayName = "Reply as staff", Scope = "tickets", Description = "Allows staff to reply to any ticket." },
        new PlatformPermissionDefinition { Key = "ticket.assign", DisplayName = "Assign tickets", Scope = "tickets", Description = "Allows assigning tickets to moderators or administrators." },
        new PlatformPermissionDefinition { Key = "ticket.change_status", DisplayName = "Change ticket status", Scope = "tickets", Description = "Allows staff to change ticket workflow statuses." },
        new PlatformPermissionDefinition { Key = "ticket.manage_categories", DisplayName = "Manage ticket categories", Scope = "tickets", Description = "Allows creation and maintenance of ticket categories." },
        new PlatformPermissionDefinition { Key = "ticket.view_internal_notes", DisplayName = "View internal notes", Scope = "tickets", Description = "Allows access to staff-only internal notes and internal audit entries." },
    ];

    public static PlatformModuleRegistration BuildModule(ServiceOptions serviceOptions) => new()
    {
        Id = "tickets-service",
        Name = "Tickets Service",
        Version = serviceOptions.Version,
        Kind = "service",
        BaseUrl = serviceOptions.BaseUrl.TrimEnd('/'),
        HealthUrl = $"{serviceOptions.BaseUrl.TrimEnd('/')}/healthz",
        OpenApiUrl = serviceOptions.GetOpenApiUrl(),
        SwaggerUiUrl = serviceOptions.GetSwaggerUiUrl(),
        ConfigRequestSubject = PlatformConfigSubjects.Request(serviceOptions.Name),
        ConfigDesiredSubject = PlatformConfigSubjects.Desired(serviceOptions.Name),
        ConfigReportedSubject = PlatformConfigSubjects.Reported(serviceOptions.Name),
        RegisteredAt = DateTime.UtcNow,
        OwnedCapabilities = ["support.tickets"],
        Tags = ["dotnet", "aspnet-core", "tickets", "support", "observability"],
        Topology = new PlatformModuleTopology
        {
            DeploymentMode = "remote-service",
            DataSources = ["platform-core distributed database config", "nats", "platform-core registry api"],
            Dependencies = ["platform-core", "mysql", "nats"],
        },
        Documentation =
        [
            new PlatformDocumentationLink { Key = "development", Title = "Module Platform Guide", Href = "contracts/modules/README.md", Description = "High-level explanation of how modules work in ExiledCMS." },
            new PlatformDocumentationLink { Key = "development", Title = "Module Development Guide", Href = "contracts/modules/development.md", Description = "Implementation details for registering a module and forwarding logs." },
            new PlatformDocumentationLink { Key = "api", Title = "Tickets Service README", Href = "src/Services/Tickets/ExiledCms.TicketsService.Api/README.md", Description = "Service-local API and runtime guide." },
            new PlatformDocumentationLink { Key = "events", Title = "Tickets Service Events", Href = "contracts/events/tickets-service.events.md", Description = "Domain event contracts published by tickets-service." },
            new PlatformDocumentationLink { Key = "observability", Title = "Module Observability Guide", Href = "contracts/modules/observability.md", Description = "Centralized logging and observability conventions for modules." },
            new PlatformDocumentationLink { Key = "sentry", Title = "Sentry Topology Guide", Href = "contracts/modules/observability.md#recommended-sentry-topology", Description = "How centralized core routing and module-local Sentry can coexist." },
        ],
    };
}

public sealed class PlatformCoreRegistrationService : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<ServiceOptions> _serviceOptions;
    private readonly IOptions<PlatformCoreOptions> _platformCoreOptions;
    private readonly ILogger<PlatformCoreRegistrationService> _logger;

    public PlatformCoreRegistrationService(
        IHttpClientFactory httpClientFactory,
        IOptions<ServiceOptions> serviceOptions,
        IOptions<PlatformCoreOptions> platformCoreOptions,
        ILogger<PlatformCoreRegistrationService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _serviceOptions = serviceOptions;
        _platformCoreOptions = platformCoreOptions;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_platformCoreOptions.Value.AutoRegister)
        {
            _logger.LogInformation("Platform-core auto-registration is disabled.");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _platformCoreOptions.Value.RetryIntervalSeconds)));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RegisterAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to register tickets-service in platform-core registry");
            }

            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RegisterAsync(CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(nameof(PlatformCoreRegistrationService));
        client.BaseAddress = new Uri(_platformCoreOptions.Value.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);

        var module = TicketPlatformCatalog.BuildModule(_serviceOptions.Value);
        var moduleResponse = await client.PostAsJsonAsync("api/v1/platform/modules", module, JsonDefaults.SerializerOptions, cancellationToken);
        moduleResponse.EnsureSuccessStatusCode();

        foreach (var permission in TicketPlatformCatalog.BuildPermissions())
        {
            var permissionResponse = await client.PostAsJsonAsync("api/v1/platform/permissions", permission, JsonDefaults.SerializerOptions, cancellationToken);
            permissionResponse.EnsureSuccessStatusCode();
        }

        _logger.LogInformation("tickets-service module and permissions registered in platform-core");
    }
}
