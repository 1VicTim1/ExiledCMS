# Module Development Guide

This guide is the practical implementation manual for building a new ExiledCMS module.

If you need the high-level architecture first, start here:

- `contracts/modules/README.md`

If you need observability and Sentry guidance, also read:

- `contracts/modules/observability.md`

## 1. Mental model

A module in ExiledCMS is an isolated functional unit that integrates with `platform-core`.

Important rule:

- `platform-core` is the control plane
- modules are the business/domain plane

That means `platform-core` is responsible for:

- module registry
- permission catalog
- capability catalog
- centralized operational visibility
- centralized log ingestion and centralized Sentry routing policy

And the module is responsible for:

- business logic
- domain API
- domain data ownership
- domain events
- module-specific storage and runtime concerns

## 2. Where a module gets its data from

A module can get data from multiple sources.

### 2.1 Its own database

Preferred for domain data.

Examples:

- tickets -> `exiledcms_tickets`
- payments -> `exiledcms_payments`
- store -> its own catalog schema

Use this when the data belongs to the module's domain.

### 2.2 Other service APIs

Use HTTP APIs when another service is authoritative.

Examples:

- reading registry metadata from `platform-core`
- calling auth/user services
- calling external providers

### 2.3 Events via NATS

Use events for asynchronous integration.

Examples:

- notification fanout
- analytics
- projections
- workflow reactions

### 2.4 Cache or short-lived local state

Use in-memory cache, Redis, or temporary files only as optimization.

Do not treat temporary state as the source of truth.

## 3. Can a module run on another machine

Yes.
In theory and in practice, a module can run on another machine, another container, another VM, or another host.

The module does not need to live in the same process as `platform-core`.

### 3.1 What must still work

If the module is remote, these paths must still be reachable:

- module -> `platform-core`
- `platform-core` -> module `BaseURL`
- `platform-core` -> module `HealthURL`
- module -> its own dependencies
  - MySQL
  - Redis
  - NATS
  - Sentry

### 3.2 Practical requirements

Check:

- DNS or service discovery
- network routing
- reverse proxy / ingress rules
- TLS if needed
- firewall/security group rules
- timeout and retry policies

## 4. Ownership rules

### 4.1 What `platform-core` owns

- module registration metadata
- permission definitions
- capability catalog
- centralized documentation index
- centralized log ingestion
- centralized Sentry routing rules for platform-level operational logs

### 4.2 What the module owns

- its domain model
- its schema/database
- its business rules
- its REST API
- its event contracts
- its internal workflows

### 4.3 What to avoid

Avoid these anti-patterns:

- storing module business entities in `platform-core`
- direct cross-service database coupling
- moving domain logic into `platform-core`

## 5. Minimum capabilities of a production-like module

At minimum, a serious module should provide:

- stable identity (`id`, `name`, `version`)
- `BaseURL`
- `HealthURL`
- registration in `platform-core`
- permission metadata if needed
- module docs metadata
- centralized log forwarding to `platform-core`
- health/readiness endpoints

## 6. Startup lifecycle of a module

Typical lifecycle:

1. load configuration
2. bootstrap desired runtime config from `platform-core`
3. connect to required infra
4. start HTTP server / workers
5. expose health endpoints
6. register in `platform-core`
7. register permissions if needed
8. forward logs to `platform-core`

## 6.1 Configuration minimization

Prefer the smallest configuration surface that still lets the module run on:

- one Docker host with a shared virtual network
- multiple containers on different hosts
- Kubernetes with internal service DNS

Rule of thumb:

- keep module-local settings minimal and explicit
- avoid requiring platform-wide settings that are unrelated to the module's own domain
- if a value is operationally owned by `platform-core`, distribute it from the core
- if a value is domain-local, keep it inside the module

Good examples of module-local settings:

- module `BaseURL`
- NATS URL if the module publishes or consumes events
- feature flags that only affect the module's own behavior

Prefer not to duplicate operational secrets and shared infra coordinates into
every module deployment when `platform-core` already owns them. The usual
production pattern is:

- the module keeps only bootstrap connectivity settings
- the module requests desired runtime config from `platform-core` over NATS
- the module reports the applied runtime config back to `platform-core`

Operational recommendation:

- the module should create its own database if the configured MySQL user can do so
- the module should still own schema creation through its own migrations
- `platform-core` may distribute the connection string, but it should not run the module's migrations for it
- when using a shared MySQL user for module bootstrap, grant `CREATE` plus schema privileges for the module database namespace, for example `exiledcms_*`

Recommended bootstrap-only settings for remote modules:

- HTTP listen URL
- module `BaseURL`
- `PlatformCore:BaseUrl`
- `Nats:Url`

Good examples of `platform-core` owned data:

- module registration metadata
- permission catalog entries
- centralized log routing policy
- centralized Sentry forwarding policy
- distributed runtime secrets and connection strings that are operationally owned by the platform

## 6.2 Logging and Sentry

Modules should not treat Sentry as a directly integrated per-module concern by default.

Preferred flow:

1. module emits structured logs, exceptions, stack traces, and correlation data
2. module forwards them to `platform-core`
3. `platform-core` normalizes, buffers, enriches, and forwards selected events to Sentry

This keeps:

- one operational choke point
- one Sentry routing policy
- one place for retention/buffering decisions
- one place to attach module metadata and request correlation

At minimum, include when forwarding logs:

- module id
- service name
- level
- message
- timestamp
- correlation id
- exception message
- stack trace when available
- domain-specific context that is safe to retain centrally

Recommended structured keys for exception payloads:

- `exceptionType`
- `exceptionMessage`
- `stackTrace`
- `fingerprint` when repeated failures should be grouped predictably in Sentry

## 7. Module registration

Register the module in `platform-core`:

- `POST /api/v1/platform/modules`

### 7.1 Minimal payload

```json
{
  "id": "tickets-service",
  "name": "Tickets Service",
  "version": "1.0.0",
  "kind": "service",
  "baseUrl": "http://tickets-service:8080",
  "healthUrl": "http://tickets-service:8080/healthz",
  "configRequestSubject": "platform.config.request.tickets-service",
  "configDesiredSubject": "platform.config.desired.tickets-service",
  "configReportedSubject": "platform.config.reported.tickets-service",
  "registeredAt": "2026-04-19T18:20:00Z",
  "ownedCapabilities": ["support.tickets"],
  "tags": ["dotnet", "tickets"]
}
```

### 7.2 Recommended enriched payload

```json
{
  "id": "tickets-service",
  "name": "Tickets Service",
  "version": "1.0.0",
  "kind": "service",
  "baseUrl": "http://tickets-service:8080",
  "healthUrl": "http://tickets-service:8080/healthz",
  "configRequestSubject": "platform.config.request.tickets-service",
  "configDesiredSubject": "platform.config.desired.tickets-service",
  "configReportedSubject": "platform.config.reported.tickets-service",
  "registeredAt": "2026-04-19T18:20:00Z",
  "ownedCapabilities": ["support.tickets"],
  "tags": ["dotnet", "tickets", "observability"],
  "topology": {
    "deploymentMode": "remote-service",
    "dataSources": [
      "platform-core distributed database config",
      "nats",
      "platform-core registry api"
    ],
    "dependencies": [
      "platform-core",
      "mysql",
      "nats"
    ]
  },
  "documentation": [
    {
      "key": "development",
      "title": "Module Platform Guide",
      "href": "contracts/modules/README.md"
    },
    {
      "key": "development",
      "title": "Module Development Guide",
      "href": "contracts/modules/development.md"
    },
    {
      "key": "observability",
      "title": "Module Observability Guide",
      "href": "contracts/modules/observability.md"
    },
    {
      "key": "sentry",
      "title": "Sentry Topology Guide",
      "href": "contracts/modules/observability.md#recommended-sentry-topology"
    }
  ]
}
```

## 8. Permission registration

If your module introduces protected actions, register permissions in `platform-core`:

- `POST /api/v1/platform/permissions`

Recommended fields:

- `key`
- `displayName`
- `scope`
- `description`
- `dangerous`

Example:

```json
{
  "key": "ticket.create",
  "displayName": "Create ticket",
  "scope": "tickets",
  "description": "Allows a user to create support tickets."
}
```

## 9. API design guidance

Use HTTP for operations that need an immediate answer.

Recommended patterns:

- explicit versioning like `/api/v1/...`
- health endpoint `/healthz`
- readiness endpoint `/readyz`
- stable DTO contracts
- explicit error payloads

If the module is public to other internal services, document:

- endpoints
- auth/identity assumptions
- idempotency expectations
- error model

## 10. Event design guidance

Use NATS for asynchronous integration.

Recommended:

- explicit subject naming
- stable JSON payloads
- correlation identifiers
- versioned envelope when needed
- outbox pattern when persistence + event publish must stay consistent

## 11. Health and readiness

Every module should expose:

- `GET /healthz`
- `GET /readyz`

Suggested meaning:

- `healthz` -> process is alive
- `readyz` -> infra dependencies needed for useful work are reachable

## 12. Centralized documentation index

`platform-core` now exposes:

- `GET /api/v1/platform/modules/docs`

This endpoint returns documentation metadata for connected modules.

Examples:

- `GET /api/v1/platform/modules/docs?kind=development`
- `GET /api/v1/platform/modules/docs?kind=observability`
- `GET /api/v1/platform/modules/docs?kind=sentry`
- `GET /api/v1/platform/modules/docs?kind=api`

This is the main place where newly connected modules should surface their docs.

## 13. Centralized logging contract

Modules should send logs to:

- `POST /api/v1/platform/logs`

Buffered logs can be viewed in `platform-core` through:

- `GET /api/v1/platform/logs`

Desired and reported module config can be inspected through:

- `GET /api/v1/platform/module-config`
- `GET /api/v1/platform/module-config/{id}`

Important rule:

- these HTTP diagnostics are sanitized by `platform-core`
- secrets such as passwords, tokens, signing keys, and connection-string
  passwords are redacted before they leave the core API
- raw values still stay available inside the in-memory config store and the NATS
  sync path used for module bootstrap

The in-memory buffer is temporary and is not persistent storage.
After a core restart, buffered logs are lost.

### 13.1 Log payload format

```json
{
  "entries": [
    {
      "timestamp": "2026-04-19T18:25:00Z",
      "moduleId": "tickets-service",
      "service": "Tickets Service",
      "level": "error",
      "message": "failed to create ticket",
      "attributes": {
        "ticketId": "6f07a3a9-f9d1-4e57-90d0-130f8d9c3958",
        "categoryId": "a31d6d93-2a95-4be2-bbd8-3d4501c77601",
        "error": "mysql timeout"
      }
    }
  ]
}
```

Supported levels:

- `debug`
- `info`
- `warn`
- `error`
- `fatal`

Optional headers:

- `X-Module-Id`
- `X-Module-Service`

If a log entry does not contain `moduleId` or `service`, the core can use these headers as defaults.

## 14. How `platform-core` handles module logs

`platform-core` accepts all module logs by default.
After a log arrives, `platform-core` can:

- keep it in the in-memory buffer
- forward it to `Sentry` if the level matches `SENTRY_MIN_LEVEL`

Important rule:

- modules send logs
- `platform-core` decides centralized Sentry routing for those logs

## 15. Recommended setup for ASP.NET Core modules

Modules that reference `ExiledCms.BuildingBlocks.Hosting` should enable forwarding during startup:

```csharp
using ExiledCms.BuildingBlocks.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddExiledCmsPlatformCoreLogging();
```

The shared extension forwards logs to `platform-core` in batches.
By default it resolves:

- `BaseUrl` from `PlatformCoreLogging:BaseUrl` or `PlatformCore:BaseUrl`
- `ModuleId` from `PlatformCoreLogging:ModuleId`, `Service:Name`, `Module:Name`, or `Auth:Name`
- `ServiceName` from `PlatformCoreLogging:ServiceName`, `Service:Name`, `Module:Name`, or `Auth:Name`

Optional configuration section:

```json
{
  "PlatformCoreLogging": {
    "enabled": true,
    "baseUrl": "http://platform-core:8080",
    "moduleId": "tickets-service",
    "serviceName": "Tickets Service",
    "batchSize": 100,
    "flushIntervalSeconds": 2,
    "maxQueueSize": 5000
  }
}
```

## 16. Inspecting buffered logs

Example:

```http
GET /api/v1/platform/logs?moduleId=tickets-service&minimumLevel=warn&limit=100
```

Query parameters:

- `moduleId`
- `source`
- `minimumLevel`
- `limit`

## 17. Recommended logging behavior inside a module

- batch logs before sending them to the core
- keep only a short-lived retry queue locally
- use structured attributes instead of only free text
- always include domain identifiers such as `ticketId`, `userId`, `orderId`, or `pluginId`
- do not treat the core log buffer as permanent storage

## 18. Sentry guidance

Full details live in:

- `contracts/modules/observability.md`

Short version:

- `platform-core` owns centralized routing of forwarded operational logs into Sentry
- a module may additionally use its own Sentry SDK for local crash/error capture
- if a module has its own Sentry setup, it should publish docs links with keys `observability` and `sentry`
- all connected module Sentry docs are discoverable through `GET /api/v1/platform/modules/docs?kind=sentry`

## 19. Example for generic modules

```bash
curl -X POST http://platform-core:8080/api/v1/platform/logs \
  -H "Content-Type: application/json" \
  -H "X-Module-Id: tickets-service" \
  -H "X-Module-Service: Tickets Service" \
  -d '{
    "entries": [
      {
        "timestamp": "2026-04-19T18:25:00Z",
        "level": "warn",
        "message": "ticket creation retry scheduled",
        "attributes": {
          "ticketId": "6f07a3a9-f9d1-4e57-90d0-130f8d9c3958"
        }
      }
    ]
  }'
```

## 20. Developer checklist for a new module

Before calling a module ready, verify:

- domain ownership is clear
- data source ownership is clear
- module has its own health endpoints
- module registers itself in `platform-core`
- permissions are registered if needed
- docs links are included in module metadata
- logs are forwarded to `platform-core`
- Sentry expectations are documented
- API and event contracts are documented
- remote deployment assumptions are documented

## 21. Minimal service skeleton in this repository

If you need a very small module that still behaves correctly in Docker,
Kubernetes, or a split-host deployment, follow the pattern now used by:

- `src/Services/Plugins/ExiledCms.PluginsService.Api`
- `src/Services/Themes/ExiledCms.ThemesService.Api`
- `src/Services/UsersRoles/ExiledCms.UsersRolesService.Api`

That skeleton intentionally keeps only:

- `Service__BaseUrl`
- `PlatformCore__BaseUrl`
- `ASPNETCORE_URLS`

And it still provides:

- Swagger
- `healthz` and `readyz`
- module auto-registration in `platform-core`
- permission catalog registration
- local metadata endpoints for debugging
- centralized log forwarding through `ExiledCms.BuildingBlocks.Hosting`

## 22. Related documents

- `contracts/modules/README.md`
- `contracts/modules/observability.md`
- `contracts/events/README.md`
