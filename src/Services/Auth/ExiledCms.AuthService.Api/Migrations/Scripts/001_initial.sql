CREATE TABLE IF NOT EXISTS auth_users (
    id CHAR(36) NOT NULL PRIMARY KEY,
    email VARCHAR(254) NOT NULL,
    email_normalized VARCHAR(254) NOT NULL,
    email_verified TINYINT(1) NOT NULL DEFAULT 0,
    email_verification_token VARCHAR(128) NULL,
    display_name VARCHAR(120) NOT NULL,
    password_hash VARCHAR(256) NOT NULL,
    password_salt VARCHAR(128) NOT NULL,
    password_algorithm VARCHAR(32) NOT NULL,
    password_iterations INT NOT NULL,
    totp_secret VARCHAR(64) NULL,
    totp_enabled TINYINT(1) NOT NULL DEFAULT 0,
    status VARCHAR(32) NOT NULL DEFAULT 'active',
    created_at_utc DATETIME(6) NOT NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    last_login_at_utc DATETIME(6) NULL,
    CONSTRAINT uq_auth_users_email_normalized UNIQUE (email_normalized)
);

CREATE TABLE IF NOT EXISTS auth_roles (
    id CHAR(36) NOT NULL PRIMARY KEY,
    key_name VARCHAR(64) NOT NULL,
    display_name VARCHAR(120) NOT NULL,
    description VARCHAR(512) NULL,
    is_system TINYINT(1) NOT NULL DEFAULT 0,
    created_at_utc DATETIME(6) NOT NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    CONSTRAINT uq_auth_roles_key UNIQUE (key_name)
);

CREATE TABLE IF NOT EXISTS auth_role_permissions (
    role_id CHAR(36) NOT NULL,
    permission_key VARCHAR(128) NOT NULL,
    granted_at_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (role_id, permission_key),
    CONSTRAINT fk_arp_role FOREIGN KEY (role_id) REFERENCES auth_roles(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS auth_user_roles (
    user_id CHAR(36) NOT NULL,
    role_id CHAR(36) NOT NULL,
    assigned_at_utc DATETIME(6) NOT NULL,
    PRIMARY KEY (user_id, role_id),
    CONSTRAINT fk_aur_user FOREIGN KEY (user_id) REFERENCES auth_users(id) ON DELETE CASCADE,
    CONSTRAINT fk_aur_role FOREIGN KEY (role_id) REFERENCES auth_roles(id) ON DELETE CASCADE
);

-- Seed the two baseline roles. Role ids are deterministic UUIDs so seeding is idempotent.
INSERT INTO auth_roles (id, key_name, display_name, description, is_system, created_at_utc, updated_at_utc)
VALUES
    ('00000000-0000-0000-0000-000000000001', 'admin', 'Administrator', 'Full administrative access.', 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('00000000-0000-0000-0000-000000000002', 'user',  'User',          'Default role for registered users.', 1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE display_name = VALUES(display_name);

-- Baseline admin permission grants. More permissions are registered at runtime by other modules;
-- additional role_permissions rows can be added from the admin UI later.
INSERT INTO auth_role_permissions (role_id, permission_key, granted_at_utc)
VALUES
    ('00000000-0000-0000-0000-000000000001', 'auth.users.list',          UTC_TIMESTAMP(6)),
    ('00000000-0000-0000-0000-000000000001', 'auth.users.view',          UTC_TIMESTAMP(6)),
    ('00000000-0000-0000-0000-000000000001', 'auth.users.edit',          UTC_TIMESTAMP(6)),
    ('00000000-0000-0000-0000-000000000001', 'auth.users.delete',        UTC_TIMESTAMP(6)),
    ('00000000-0000-0000-0000-000000000001', 'auth.users.ban',           UTC_TIMESTAMP(6)),
    ('00000000-0000-0000-0000-000000000001', 'auth.roles.manage',        UTC_TIMESTAMP(6)),
    ('00000000-0000-0000-0000-000000000001', 'auth.permissions.manage', UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE granted_at_utc = VALUES(granted_at_utc);
