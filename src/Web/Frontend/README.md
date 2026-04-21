# Frontend

Next.js 16 App Router frontend for ExiledCMS.

## What works now

- landing page without broken `/store`, `/news`, `/play` links
- `/auth/login`
- `/auth/register`
- `/account`
- `/tickets`
- server-side auth session cookies
- tickets proxy that uses the authenticated user instead of the old dev actor
- module-aware fallback for `/tickets`
  - regular users get a generic 403-style page
  - admins get extra registry/debug details from `platform-core`

## Required backend services

The frontend expects these internal services:

- `platform-core`
- `auth-service`
- `tickets-service`

Defaults when env vars are not provided:

- `EXILEDCMS_PLATFORM_CORE_URL=http://localhost:8080`
- `EXILEDCMS_PLATFORM_CORE_PUBLIC_URL=http://localhost:8080`
- `EXILEDCMS_AUTH_SERVICE_URL=http://localhost:8081`
- `EXILEDCMS_TICKETS_SERVICE_URL=http://localhost:8080`

Override them in development or Docker/Kubernetes so each service points to the
real internal host name in your virtual network.

If the browser reaches the frontend and `platform-core` through different public
hosts or ports, set `EXILEDCMS_PLATFORM_CORE_PUBLIC_URL` as well. The frontend
uses the internal URL for server-to-server fetches and the public URL for
browser redirects such as `/swagger`.

## Auth session model

The browser never calls `auth-service` directly.

Flow:

1. browser submits to `/api/auth/*`
2. frontend route handler proxies to `auth-service`
3. on success, frontend stores an HTTP-only session snapshot in cookies
4. other frontend BFF routes, such as `/api/tickets`, read those cookies and
   forward identity/permissions to downstream modules

Current cookies:

- `exiledcms_access_token`
- `exiledcms_user_id`
- `exiledcms_user_name`
- `exiledcms_user_role`
- `exiledcms_user_permissions`
- `exiledcms_user_email`
- `exiledcms_user_email_verified`
- `exiledcms_user_totp_enabled`

## Tickets behavior

- creating a ticket is blocked until `emailVerified=true`
- if `tickets-service` is missing from `platform-core` registry, the page does
  not try to render a broken client state
- the old dev actor path is now opt-in only via `EXILEDCMS_ALLOW_DEV_ACTOR=true`

## Commands

```bash
npm install
npm run lint
npm run build
npm run dev
```

## Notes for future work

- add real SMTP/sendmail/postfix delivery for email confirmation
- replace manual verification token display in `/account`
- add admin UI on top of the auth/users/permissions endpoints
- bind more public UI elements to module presence metadata from `platform-core`
