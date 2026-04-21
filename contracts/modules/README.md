# Module Platform Guide

This document explains how modules work in ExiledCMS.
It is the main entry point for developers who are building a new module or service.

## What is a module

In ExiledCMS, a module is an isolated unit of functionality that integrates with `platform-core`.
A module can be:

- a standalone backend service
- a plugin-like backend component
- a future integration adapter
- a domain service with its own API and storage

`platform-core` is not the business-logic host for every module.
It is the control plane.

Its responsibilities are:

- module registry
- permission catalog
- capability catalog
- theme/plugin/module metadata
- centralized operational visibility
- centralized log ingestion and routing

The module itself owns its domain logic.

## Where modules get data from

A module can get data from several places.

### 1. Its own storage

This is the preferred option for domain ownership.
Examples:

- `tickets-service` owns `exiledcms_tickets`
- a payments module could own `exiledcms_payments`
- a store module could own its own catalog schema

Rule of thumb:

- if the data is part of the module's domain, the module should own it
- `platform-core` should not become a shared database for every feature

### 2. Other service APIs

A module may call:

- `platform-core` registry endpoints
- auth/user services
- other domain services
- external APIs

This is useful when data is authoritative elsewhere.

Examples:

- reading the permission catalog from `platform-core`
- calling an auth service for user identity validation
- calling an external billing API

### 3. Event streams

A module may react to asynchronous events through NATS.

Use this when:

- immediate synchronous response is not required
- eventual consistency is acceptable
- you want looser coupling between services

Examples:

- notification fanout
- analytics updates
- cache warmup
- audit/event projections

### 4. Short-lived cache or local memory

A module may use:

- in-memory cache
- Redis
- temporary files

This is an optimization layer, not the source of truth.

## Can modules run on another machine

Yes.
A module can be deployed:

- on the same machine as `platform-core`
- in another container
- on another VM
- on another host in the same network
- in theory even across datacenters, if network and security constraints allow it

The architecture is service-oriented.
`platform-core` does not require modules to live in the same process or on the same machine.

### Requirements for remote deployment

If a module runs on another machine, the following must still work:

- the module can call `platform-core`
- `platform-core` can reach the module `BaseURL` and `HealthURL`
- shared infrastructure endpoints are reachable if needed
  - MySQL
  - Redis
  - NATS
  - Sentry
- DNS or service discovery resolves correctly
- security policy allows the traffic

### What must be reachable

At minimum, a module usually needs:

- outbound access to `platform-core`
- outbound access to its own storage and broker dependencies
- optionally inbound access from other services or reverse proxy

`platform-core` usually needs:

- access to the module `BaseURL`
- access to the module `HealthURL`

## Ownership model

The most important rule is ownership.

### `platform-core` owns

- module registration metadata
- permission definitions
- capability catalog
- centralized operational visibility metadata
- centralized log ingestion and Sentry routing policy

### modules own

- business logic
- domain models
- domain APIs
- domain storage
- domain events
- local transactional integrity

### modules should not assume

- that `platform-core` stores their business entities
- that `platform-core` is their primary database
- that `platform-core` should implement their domain workflow

## Recommended interaction patterns

### Sync interaction

Use HTTP when:

- you need an immediate answer
- the caller cannot continue without a response
- the operation is query-like or command-like with fast feedback

Examples:

- register module metadata
- call an auth/user service
- expose health endpoints

### Async interaction

Use NATS when:

- the receiver can process later
- the sender should not block on the consumer
- eventual consistency is acceptable

Examples:

- notification workflows
- secondary projections
- analytics
- cross-service integrations

### Database access

Prefer this order:

- module uses its own database
- module consumes other services via API or events
- avoid direct cross-service database coupling

## Minimal lifecycle of a module

1. start the process
2. connect to required infra
3. expose health/readiness endpoints
4. register in `platform-core`
5. register permissions if needed
6. start serving API/events
7. forward logs to `platform-core`

## Minimal integration surface with `platform-core`

A production-like module should typically provide:

- `BaseURL`
- `HealthURL`
- module registration payload
- permission metadata if it exposes protected capabilities
- documentation metadata
- centralized log forwarding

## Module registration metadata

`platform-core` accepts module registrations through:

- `POST /api/v1/platform/modules`

The registration payload may include:

- core identity fields
- topology metadata
- documentation links

Example:

```json
{
  "id": "tickets-service",
  "name": "Tickets Service",
  "version": "1.0.0",
  "kind": "service",
  "baseUrl": "http://tickets-service:8080",
  "healthUrl": "http://tickets-service:8080/healthz",
  "registeredAt": "2026-04-19T18:20:00Z",
  "ownedCapabilities": ["support.tickets"],
  "tags": ["dotnet", "tickets"],
  "topology": {
    "deploymentMode": "remote-service",
    "dataSources": [
      "mysql:exiledcms_tickets",
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
      "title": "Module Development Guide",
      "href": "contracts/modules/development.md"
    },
    {
      "key": "observability",
      "title": "Observability Guide",
      "href": "contracts/modules/observability.md"
    },
    {
      "key": "sentry",
      "title": "Sentry Integration Guide",
      "href": "contracts/modules/observability.md#sentry-topology"
    }
  ]
}
```

## Centralized OpenAPI aggregation (the "API hub")

`platform-core` provides one UI and one same-origin URL base from which every
module's OpenAPI document is reachable. A module never needs to expose its
Swagger endpoint directly to end users.

### What the hub gives you

- `GET /swagger` — HTML page embedding Swagger UI. Loads the document index and
  populates the top-bar document switcher automatically. Works from the browser
  on the public platform-core origin, even when the module itself lives on a
  private internal network.
- `GET /api/v1/platform/openapi/documents` — JSON index of every document the
  hub knows about. Each item looks like:
  ```json
  {
    "id": "tickets-service",
    "name": "Tickets Service",
    "kind": "service",
    "openApiUrl":    "http://tickets-service:8080/swagger/v1/swagger.json",
    "aggregatedUrl": "/api/v1/platform/openapi/modules/tickets-service.json",
    "swaggerUiUrl":  "http://tickets-service:8080/swagger"
  }
  ```
  `openApiUrl` is the raw upstream the module declared. `aggregatedUrl` is the
  same-origin URL the hub (and any browser-side tooling) should prefer.
- `GET /api/v1/platform/openapi/core.json` — platform-core's own OpenAPI doc.
- `GET /api/v1/platform/openapi/modules/{id}.json` — server-side proxy for a
  registered module's OpenAPI JSON. Returns:
  - `200` with the upstream JSON body when the module is reachable.
  - `404` when the module id is unknown or did not register an `openApiUrl`.
  - `502` when the upstream is unreachable or responds with non-JSON / non-2xx.

### How a module opts in

Just include `openApiUrl` (and optionally `swaggerUiUrl`) in the module
registration payload you send to `POST /api/v1/platform/modules`:

```json
{
  "id": "tickets-service",
  "openApiUrl":   "http://tickets-service:8080/swagger/v1/swagger.json",
  "swaggerUiUrl": "http://tickets-service:8080/swagger"
}
```

The hub will pick the module up on the next refresh and proxy its document.

### Caching

Proxy responses are cached in-memory for 30 seconds per module to shield
upstreams from repeated hub reloads. A concurrent mutex ensures only one
upstream fetch happens per module even during a cache-miss stampede.

### Running the module behind NAT / private networks

Because the hub uses the proxy URL by default, it is enough for platform-core
to be able to reach the module — the browser does not need to. Ensure:

- the `openApiUrl` is resolvable from inside platform-core's network
- platform-core itself is reachable by operators / clients

## Centralized documentation index

`platform-core` exposes:

- `GET /api/v1/platform/modules/docs`

This endpoint returns documentation metadata for connected modules.
It can also be filtered by documentation kind:

- `GET /api/v1/platform/modules/docs?kind=development`
- `GET /api/v1/platform/modules/docs?kind=observability`
- `GET /api/v1/platform/modules/docs?kind=sentry`

This gives one central place to discover:

- developer docs
- architecture docs
- observability docs
- Sentry docs
- events/API docs

## Logging and observability overview

Every module should forward structured logs to `platform-core`.
`platform-core` keeps a temporary in-memory buffer and decides what to send to Sentry.

Important rule:

- modules send logs
- `platform-core` decides Sentry routing policy for centralized operational logs

Detailed guidance lives in:

- `contracts/modules/development.md`
- `contracts/modules/observability.md`

## Sentry model

There are two useful layers.

### Layer 1. Centralized operational routing through `platform-core`

This is for platform-level visibility.
Modules forward logs to `platform-core`.
`platform-core` sends selected entries to Sentry based on configured severity.

### Layer 2. Optional local module Sentry SDK

A module may also use its own Sentry SDK for:

- crash reporting
- local exception capture
- module-specific tracing or profiling

This is optional and does not replace centralized log forwarding.

If a module has its own Sentry integration, its registration metadata should include documentation links with keys such as:

- `observability`
- `sentry`

This keeps Sentry-related docs visible through the central module docs endpoint.

## Security and networking

When a module lives on another machine, verify:

- service-to-service authentication if needed
- firewall rules
- DNS/service discovery
- TLS and reverse proxy behavior
- timeout/retry policy
- broker reachability

## Recommended documentation set for every module

Each module should ideally provide links for:

- `development`
- `api`
- `events`
- `observability`
- `sentry`
- `runbook`

## Related docs

- `contracts/modules/development.md`
- `contracts/modules/observability.md`
- `contracts/events/README.md`
