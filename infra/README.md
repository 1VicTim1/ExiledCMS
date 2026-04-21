# Infrastructure Notes

`infra/compose.yaml` intentionally starts only shared stateless dependencies for local
and test environments:

- `redis`
- `nats`

MySQL/MariaDB is expected to run outside this compose project, typically as a system
service on the host or as a managed database. This matches the production topology
where `platform-core` and modules may run on different hosts or in Kubernetes, while
the database remains a separate service.

Recommended database setup:

- Create one dedicated SQL user for the whole ExiledCMS deployment.
- Grant that user `CREATE` on the server plus full privileges on the `exiledcms_%`
  schema namespace so each module can create and migrate its own database.
- Keep `exiledcms_platform` reserved for `platform-core`.

Example bootstrap SQL:

```sql
CREATE DATABASE IF NOT EXISTS exiledcms_platform CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

CREATE USER IF NOT EXISTS 'exiledcms_app'@'127.0.0.1' IDENTIFIED BY 'change-me';
CREATE USER IF NOT EXISTS 'exiledcms_app'@'localhost' IDENTIFIED BY 'change-me';

GRANT CREATE ON *.* TO 'exiledcms_app'@'127.0.0.1';
GRANT CREATE ON *.* TO 'exiledcms_app'@'localhost';
GRANT ALL PRIVILEGES ON `exiledcms\_%`.* TO 'exiledcms_app'@'127.0.0.1';
GRANT ALL PRIVILEGES ON `exiledcms\_%`.* TO 'exiledcms_app'@'localhost';
FLUSH PRIVILEGES;
```

When `platform-core` distributes module configuration over NATS, the
`databaseConnectionString` values should also use this dedicated SQL user.
