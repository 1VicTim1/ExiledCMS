# Themes Service

`themes-service` is currently a platform-integrated service skeleton for future theme catalog and activation features.

## Current behavior

- exposes `GET /healthz` and `GET /readyz`
- exposes Swagger on `GET /swagger`
- auto-registers itself and its permission catalog in `platform-core`
- forwards logs to `platform-core`
- exposes local metadata endpoints:
  - `GET /api/v1/metadata/module-registration`
  - `GET /api/v1/metadata/permissions`

## Current permissions

- `themes.read`
- `themes.activate`
- `themes.configure`

## Minimal bootstrap config

- `ASPNETCORE_URLS`
- `Service__BaseUrl`
- `PlatformCore__BaseUrl`

This service intentionally keeps no local database or message-bus dependency yet.
