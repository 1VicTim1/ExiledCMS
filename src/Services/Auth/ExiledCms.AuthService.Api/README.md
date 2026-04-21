# Auth Service

Minimal working identity service for ExiledCMS.

## Current API surface

- `POST /api/v1/auth/register`
- `POST /api/v1/auth/login`
- `GET /api/v1/auth/me`
- `POST /api/v1/auth/password/change`
- `POST /api/v1/auth/email/verification`
- `POST /api/v1/auth/email/confirm`
- `POST /api/v1/auth/2fa/setup`
- `POST /api/v1/auth/2fa/enable`
- `POST /api/v1/auth/2fa/disable`
- `GET /api/v1/auth/users`

## Security model

- passwords are stored with PBKDF2-SHA256
- JWT access tokens are HS256
- TOTP uses RFC6238-compatible 6-digit codes
- email verification is token-based

## Current limitation

Email delivery is not wired to SMTP/sendmail/postfix yet.

Because of that, `POST /api/v1/auth/email/verification` currently returns the
raw verification token so the flow can still be tested end-to-end from the
frontend account page.

## Startup behavior

On startup the service:

1. loads config from `appsettings` / env
2. creates its own MySQL database if it does not exist yet
3. applies SQL scripts from `Migrations/Scripts`
4. exposes Swagger
5. registers itself and its permission catalog in `platform-core`
6. forwards logs to `platform-core`

## Important config

`Auth` section:

- `Name`
- `Version`
- `BaseUrl`
- `MySqlConnectionString`
- `OpenApiJsonPath`
- `SwaggerUiPath`

`Jwt` section:

- `Secret`
- `Issuer`
- `Audience`
- `AccessTokenLifetimeMinutes`

`PlatformCore` section:

- `BaseUrl`
- `AutoRegister`
- `RetryIntervalSeconds`

## Tests

Auth unit tests cover:

- password hashing
- JWT issue/validate
- TOTP validation
- registration flow
- password change
- email verification
- TOTP enable/disable
