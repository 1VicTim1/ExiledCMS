package config

import "testing"

func TestLoadUsesDefaultsWhenEnvironmentIsMissing(t *testing.T) {
	for _, key := range []string{
		"SERVICE_NAME",
		"APP_ENV",
		"PORT",
		"MYSQL_DSN",
		"REDIS_ADDR",
		"NATS_URL",
		"MODULE_CONFIG_JSON",
		"SENTRY_DSN",
		"SENTRY_MIN_LEVEL",
		"LOG_BUFFER_MAX_ENTRIES",
	} {
		t.Setenv(key, "")
	}

	cfg := Load()

	if cfg.ServiceName != "platform-core" {
		t.Fatalf("expected default service name, got %q", cfg.ServiceName)
	}

	if cfg.Environment != "development" {
		t.Fatalf("expected default environment, got %q", cfg.Environment)
	}

	if cfg.HTTPPort != 8080 {
		t.Fatalf("expected default port, got %d", cfg.HTTPPort)
	}

	if cfg.SentryMinLevel != "error" {
		t.Fatalf("expected default sentry level, got %q", cfg.SentryMinLevel)
	}

	if cfg.LogBufferMaxEntries != 2000 {
		t.Fatalf("expected default log buffer size, got %d", cfg.LogBufferMaxEntries)
	}

	if cfg.ModuleConfigJSON != "" {
		t.Fatalf("expected default module config json to be empty, got %q", cfg.ModuleConfigJSON)
	}

	infra := cfg.InfraStatus()
	if infra["sentry"] != "missing" {
		t.Fatalf("expected sentry to be missing by default, got %q", infra["sentry"])
	}
}

func TestLoadUsesEnvironmentOverrides(t *testing.T) {
	t.Setenv("SERVICE_NAME", "core-test")
	t.Setenv("APP_ENV", "production")
	t.Setenv("PORT", "9090")
	t.Setenv("MYSQL_DSN", "mysql-dsn")
	t.Setenv("REDIS_ADDR", "redis:6379")
	t.Setenv("NATS_URL", "nats://nats:4222")
	t.Setenv("MODULE_CONFIG_JSON", `{"tickets-service":{"databaseConnectionString":"Server=mysql;Database=exiledcms_tickets;","openApiUrl":"http://tickets-service:8080/swagger/v1/swagger.json"}}`)
	t.Setenv("SENTRY_DSN", "https://example@sentry.invalid/1")
	t.Setenv("SENTRY_MIN_LEVEL", "warn")
	t.Setenv("LOG_BUFFER_MAX_ENTRIES", "99")

	cfg := Load()

	if cfg.ServiceName != "core-test" {
		t.Fatalf("expected overridden service name, got %q", cfg.ServiceName)
	}

	if cfg.Environment != "production" {
		t.Fatalf("expected overridden environment, got %q", cfg.Environment)
	}

	if cfg.HTTPPort != 9090 {
		t.Fatalf("expected overridden port, got %d", cfg.HTTPPort)
	}

	if cfg.SentryDSN == "" {
		t.Fatalf("expected sentry dsn override to be applied")
	}

	if cfg.ModuleConfigJSON == "" {
		t.Fatalf("expected module config json override to be applied")
	}

	if cfg.SentryMinLevel != "warn" {
		t.Fatalf("expected overridden sentry level, got %q", cfg.SentryMinLevel)
	}

	if cfg.LogBufferMaxEntries != 99 {
		t.Fatalf("expected overridden log buffer size, got %d", cfg.LogBufferMaxEntries)
	}

	infra := cfg.InfraStatus()
	if infra["sentry"] != "configured" {
		t.Fatalf("expected sentry to be configured, got %q", infra["sentry"])
	}
}
