# Users Roles Service

`users-roles-service` is currently a platform-integrated service skeleton for future user, role and permission administration features.

## Current behavior

- exposes `GET /healthz` and `GET /readyz`
- exposes Swagger on `GET /swagger`
- auto-registers itself and its permission catalog in `platform-core`
- forwards logs to `platform-core`
- exposes local metadata endpoints:
  - `GET /api/v1/metadata/module-registration`
  - `GET /api/v1/metadata/permissions`

## Current permissions

- `users.read`
- `users.manage`
- `roles.read`
- `roles.manage`
- `permissions.assign`

## Minimal bootstrap config

- `ASPNETCORE_URLS`
- `Service__BaseUrl`
- `PlatformCore__BaseUrl`

This service intentionally keeps no local database or message-bus dependency yet.
