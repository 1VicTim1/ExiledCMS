CREATE TABLE IF NOT EXISTS ticket_categories (
    id CHAR(36) NOT NULL PRIMARY KEY,
    name VARCHAR(120) NOT NULL,
    description VARCHAR(512) NULL,
    is_active TINYINT(1) NOT NULL DEFAULT 1,
    display_order INT NOT NULL DEFAULT 0,
    created_at_utc DATETIME(6) NOT NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    CONSTRAINT uq_ticket_categories_name UNIQUE (name)
);

CREATE TABLE IF NOT EXISTS tickets (
    id CHAR(36) NOT NULL PRIMARY KEY,
    created_by_user_id CHAR(36) NOT NULL,
    created_by_display_name VARCHAR(160) NOT NULL,
    subject VARCHAR(200) NOT NULL,
    category_id CHAR(36) NOT NULL,
    priority VARCHAR(32) NOT NULL,
    status VARCHAR(32) NOT NULL,
    assigned_staff_user_id CHAR(36) NULL,
    assigned_staff_display_name VARCHAR(160) NULL,
    created_at_utc DATETIME(6) NOT NULL,
    updated_at_utc DATETIME(6) NOT NULL,
    closed_at_utc DATETIME(6) NULL,
    last_message_at_utc DATETIME(6) NOT NULL,
    CONSTRAINT fk_tickets_category FOREIGN KEY (category_id) REFERENCES ticket_categories(id)
);

CREATE TABLE IF NOT EXISTS ticket_messages (
    id CHAR(36) NOT NULL PRIMARY KEY,
    ticket_id CHAR(36) NOT NULL,
    author_user_id CHAR(36) NOT NULL,
    author_display_name VARCHAR(160) NOT NULL,
    author_role VARCHAR(64) NOT NULL,
    is_staff_reply TINYINT(1) NOT NULL DEFAULT 0,
    body TEXT NOT NULL,
    created_at_utc DATETIME(6) NOT NULL,
    CONSTRAINT fk_ticket_messages_ticket FOREIGN KEY (ticket_id) REFERENCES tickets(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS ticket_assignments (
    id CHAR(36) NOT NULL PRIMARY KEY,
    ticket_id CHAR(36) NOT NULL,
    assigned_staff_user_id CHAR(36) NOT NULL,
    assigned_staff_display_name VARCHAR(160) NOT NULL,
    assigned_by_user_id CHAR(36) NOT NULL,
    assigned_by_display_name VARCHAR(160) NOT NULL,
    is_active TINYINT(1) NOT NULL DEFAULT 1,
    assigned_at_utc DATETIME(6) NOT NULL,
    unassigned_at_utc DATETIME(6) NULL,
    CONSTRAINT fk_ticket_assignments_ticket FOREIGN KEY (ticket_id) REFERENCES tickets(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS ticket_internal_notes (
    id CHAR(36) NOT NULL PRIMARY KEY,
    ticket_id CHAR(36) NOT NULL,
    author_user_id CHAR(36) NOT NULL,
    author_display_name VARCHAR(160) NOT NULL,
    body TEXT NOT NULL,
    created_at_utc DATETIME(6) NOT NULL,
    CONSTRAINT fk_ticket_internal_notes_ticket FOREIGN KEY (ticket_id) REFERENCES tickets(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS ticket_audit_logs (
    id CHAR(36) NOT NULL PRIMARY KEY,
    ticket_id CHAR(36) NOT NULL,
    actor_user_id CHAR(36) NOT NULL,
    actor_display_name VARCHAR(160) NOT NULL,
    actor_role VARCHAR(64) NOT NULL,
    action_type VARCHAR(64) NOT NULL,
    is_internal TINYINT(1) NOT NULL DEFAULT 0,
    details_json JSON NOT NULL,
    created_at_utc DATETIME(6) NOT NULL,
    CONSTRAINT fk_ticket_audit_logs_ticket FOREIGN KEY (ticket_id) REFERENCES tickets(id) ON DELETE CASCADE
);

CREATE TABLE IF NOT EXISTS ticket_outbox_events (
    id CHAR(36) NOT NULL PRIMARY KEY,
    subject VARCHAR(160) NOT NULL,
    event_type VARCHAR(160) NOT NULL,
    envelope_json JSON NOT NULL,
    occurred_at_utc DATETIME(6) NOT NULL,
    published_at_utc DATETIME(6) NULL,
    attempt_count INT NOT NULL DEFAULT 0,
    last_error TEXT NULL
);

CREATE INDEX ix_tickets_created_by_user_id ON tickets (created_by_user_id);
CREATE INDEX ix_tickets_category_id ON tickets (category_id);
CREATE INDEX ix_tickets_status ON tickets (status);
CREATE INDEX ix_tickets_priority ON tickets (priority);
CREATE INDEX ix_tickets_assigned_staff_user_id ON tickets (assigned_staff_user_id);
CREATE INDEX ix_tickets_updated_at_utc ON tickets (updated_at_utc);
CREATE INDEX ix_ticket_messages_ticket_id_created_at_utc ON ticket_messages (ticket_id, created_at_utc);
CREATE INDEX ix_ticket_assignments_ticket_id_is_active ON ticket_assignments (ticket_id, is_active);
CREATE INDEX ix_ticket_internal_notes_ticket_id_created_at_utc ON ticket_internal_notes (ticket_id, created_at_utc);
CREATE INDEX ix_ticket_audit_logs_ticket_id_created_at_utc ON ticket_audit_logs (ticket_id, created_at_utc);
CREATE INDEX ix_ticket_outbox_events_published_at_utc_occurred_at_utc ON ticket_outbox_events (published_at_utc, occurred_at_utc);

INSERT INTO ticket_categories (id, name, description, is_active, display_order, created_at_utc, updated_at_utc)
VALUES
    ('a31d6d93-2a95-4be2-bbd8-3d4501c77601', 'Technical Support', 'Gameplay, launcher, permissions and technical issues.', 1, 10, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('f47e3d48-d5ae-4d24-9cde-d7325a3f4b6d', 'Payments', 'Purchase, payment, store and donation issues.', 1, 20, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('2d2534fb-963d-42b0-9fc0-53cb9bd1bd31', 'Punishments and Appeals', 'Ban reports, mutes, sanctions and appeal requests.', 1, 30, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)),
    ('3d98c2b7-f1ef-4d69-bd86-bd6851c5954a', 'Other', 'General support requests that do not match another category.', 1, 40, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6))
ON DUPLICATE KEY UPDATE
    description = VALUES(description),
    is_active = VALUES(is_active),
    display_order = VALUES(display_order),
    updated_at_utc = VALUES(updated_at_utc);
