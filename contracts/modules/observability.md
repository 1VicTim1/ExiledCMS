# Module Observability Guide

This document describes logging and Sentry expectations for ExiledCMS modules and `platform-core`.

## Goals

The platform wants one operational picture of what is happening across connected modules.
For that reason:

- modules send structured logs to `platform-core`
- `platform-core` keeps a temporary in-memory buffer for inspection
- `platform-core` decides which centralized logs are forwarded to Sentry
- module-specific Sentry SDK usage remains optional

## Centralized logging flow

1. a module emits logs locally
2. the module forwarder batches logs
3. the batch is sent to `POST /api/v1/platform/logs`
4. `platform-core` stores the entries in the temporary buffer
5. `platform-core` checks `SENTRY_MIN_LEVEL`
6. matching entries are forwarded to Sentry

## Why the core decides

This keeps operational routing consistent.
A new module does not need to reinvent:

- severity policy
- buffering policy
- central review flow
- incident visibility

## Core configuration

`platform-core` supports:

- `SENTRY_DSN`
- `SENTRY_MIN_LEVEL`
- `LOG_BUFFER_MAX_ENTRIES`

### Example

```env
SENTRY_DSN=https://examplePublicKey@o0.ingest.sentry.io/0
SENTRY_MIN_LEVEL=error
LOG_BUFFER_MAX_ENTRIES=2000
```

Supported Sentry levels for centralized routing:

- `debug`
- `info`
- `warn`
- `error`
- `fatal`

## Module-side forwarding for ASP.NET Core

Modules that use `ExiledCms.BuildingBlocks.Hosting` can enable forwarding with:

```csharp
using ExiledCms.BuildingBlocks.Hosting;

var builder = WebApplication.CreateBuilder(args);
builder.AddExiledCmsPlatformCoreLogging();
```

Optional configuration:

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

## Can a module also use Sentry directly

Yes.
A module may additionally use its own Sentry SDK.
This is useful for:

- uncaught exceptions inside the module process
- module-specific breadcrumbs
- tracing/profiling
- custom local enrichment

But this does not replace centralized platform logging.
You should still forward logs to `platform-core`.

## Recommended Sentry topology

### `platform-core`

Use Sentry in the core for:

- centralized operational log routing
- cross-module incident visibility
- one control-plane view of important failures

### module-local Sentry SDK

Use module-local Sentry for:

- module crash reporting
- module-specific stack traces
- deeper diagnostics for one module

## Centralized Sentry documentation

If a module has local Sentry integration, it should publish documentation metadata in its registration payload.
Recommended doc keys:

- `observability`
- `sentry`

Example:

```json
{
  "documentation": [
    {
      "key": "observability",
      "title": "Tickets Service Observability",
      "href": "src/Services/Tickets/ExiledCms.TicketsService.Api/README.md#events"
    },
    {
      "key": "sentry",
      "title": "Tickets Service Sentry Guide",
      "href": "contracts/modules/observability.md#can-a-module-also-use-sentry-directly"
    }
  ]
}
```

Then all connected module Sentry docs become discoverable through:

- `GET /api/v1/platform/modules/docs?kind=sentry`

And broader observability docs through:

- `GET /api/v1/platform/modules/docs?kind=observability`

## Buffered log review

Buffered entries can be inspected in `platform-core`:

- `GET /api/v1/platform/logs`
- `GET /api/v1/platform/logs?moduleId=tickets-service`
- `GET /api/v1/platform/logs?minimumLevel=warn`

The buffer is temporary.
It is not persistent audit storage.

## What to put in structured log attributes

Always prefer identifiers and machine-readable values:

- `ticketId`
- `userId`
- `orderId`
- `pluginId`
- `requestId`
- `correlationId`
- `subject`
- `status`

Avoid pushing only a long human sentence without structured attributes.

## Operational recommendation

For each module, keep docs for:

- logging strategy
- Sentry usage
- alerting policy
- dashboards/runbooks if they exist
- known failure modes

Expose these through module registration metadata so they are indexed centrally.
