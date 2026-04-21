# ExiledCMS Tickets Service

`tickets-service` is a standalone ASP.NET Core service that owns all support-ticket business logic for ExiledCMS.

## Responsibilities

- user ticket creation
- ticket listing and filtering
- single ticket view with message history
- user/staff replies
- staff assignment and workflow status changes
- category management
- internal staff-only notes
- audit trail and history
- permission-aware access control
- NATS domain events via transactional outbox
- platform-core module and permission registration metadata

## Architecture

### Boundaries

- `platform-core` stays the control plane / registry / permissions catalog
- `tickets-service` owns the ticket domain and its own MySQL database `exiledcms_tickets`
- NATS is used for domain event publication
- internal notes and internal audit entries never leak to non-staff actors

### Access model

The service resolves the current actor from claims first and falls back to headers:

- `X-User-Id`
- `X-User-Name`
- `X-User-Role`
- `X-User-Permissions`
- `X-Correlation-Id`
- `X-Causation-Id`

This keeps the service decoupled from the final gateway/auth implementation while still enforcing permissions today.

## Project structure

```text
src/Services/Tickets/ExiledCms.TicketsService.Api/
  Controllers/
  Contracts/
  Domain/
  Infrastructure/
  Migrations/Scripts/
  Services/
  Program.cs
  Dockerfile
  .env.example
  README.md
```

## Configuration

Environment variables are documented in `.env.example`.

Key values:

- `Service__MySqlConnectionString`
- `PlatformCore__BaseUrl`
- `PlatformCore__AutoRegister`
- `Nats__Url`
- `Service__BaseUrl`

`platform-core` remains the authoritative source for module runtime config, but
`Service__MySqlConnectionString` can be provided as a local fallback so the
service can still start and run migrations while NATS or platform-core are
coming up. This is important for docker-compose and distributed bootstrap
scenarios where startup ordering is not deterministic.

## API

### Ticket endpoints

- `POST /api/v1/tickets`
- `GET /api/v1/tickets`
- `GET /api/v1/tickets/{id}`
- `POST /api/v1/tickets/{id}/messages`
- `POST /api/v1/tickets/{id}/assign`
- `POST /api/v1/tickets/{id}/status`
- `POST /api/v1/tickets/{id}/internal-notes`

### Category endpoints

- `GET /api/v1/ticket-categories`
- `POST /api/v1/ticket-categories`

### Metadata endpoints

- `GET /api/v1/metadata/module-registration`
- `GET /api/v1/metadata/permissions`

### Health endpoints

- `GET /healthz`
- `GET /readyz`

### Swagger

- `GET /swagger`

## Permissions

Registered in platform-core:

- `ticket.create`
- `ticket.read.own`
- `ticket.read.all`
- `ticket.reply.own`
- `ticket.reply.staff`
- `ticket.assign`
- `ticket.change_status`
- `ticket.manage_categories`
- `ticket.view_internal_notes`

## MySQL schema

The service uses an isolated database:

- `ticket_categories`
- `tickets`
- `ticket_messages`
- `ticket_assignments`
- `ticket_internal_notes`
- `ticket_audit_logs`
- `ticket_outbox_events`
- `schema_migrations`

The module creates its own database automatically on startup when the configured
MySQL user has the required privileges, then applies schema migrations from
`Migrations/Scripts`.

## Events

Published through the outbox dispatcher:

- `ticket.created`
- `ticket.message.added`
- `ticket.assigned`
- `ticket.status.changed`
- `ticket.closed`

Detailed event contracts live in `contracts/events/tickets-service.events.md`.

## platform-core registration

The service exposes metadata endpoints and also periodically registers itself into `platform-core` when `PlatformCore__AutoRegister=true`:

- module registration -> `POST /api/v1/platform/modules`
- permission registration -> `POST /api/v1/platform/permissions`

## Running locally

1. Start infra from `infra/compose.yaml`
2. Ensure an external MySQL/MariaDB instance is running and pass `Service__MySqlConnectionString`
3. Ensure `platform-core` is available if auto-registration is enabled
4. Run the service with the variables from `.env.example`
5. Open `/swagger`

## Docker

Build from the repository root:

```bash
docker build -f src/Services/Tickets/ExiledCms.TicketsService.Api/Dockerfile -t exiledcms/tickets-service .
```

## Notes

- user-visible history is filtered from `ticket_audit_logs` to exclude internal entries
- staff with `ticket.view_internal_notes` receive assignments, internal notes and internal audit events
- NATS publication is decoupled from request handling through `ticket_outbox_events`, preparing the service for future notification consumers
