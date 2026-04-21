package moduleconfig

import "testing"

func TestNewStoreParsesDesiredConfigAndNormalizesSettings(t *testing.T) {
	store, err := NewStore(`{
		"tickets-service": {
			"revision": " r1 ",
			"databaseConnectionString": " Server=mysql;Database=exiledcms_tickets; ",
			"openApiUrl": " http://tickets-service:8080/swagger/v1/swagger.json ",
			"swaggerUiUrl": " http://tickets-service:8080/swagger ",
			"settings": {
				" feature_flag ": " enabled ",
				"ignored": ""
			}
		}
	}`)
	if err != nil {
		t.Fatalf("expected module config json to parse, got %v", err)
	}

	desired, ok := store.DesiredFor("tickets-service")
	if !ok {
		t.Fatalf("expected desired tickets-service config to exist")
	}

	if desired.DatabaseConnectionString != "Server=mysql;Database=exiledcms_tickets;" {
		t.Fatalf("expected normalized db connection string, got %q", desired.DatabaseConnectionString)
	}

	if desired.OpenAPIURL != "http://tickets-service:8080/swagger/v1/swagger.json" {
		t.Fatalf("expected normalized openapi url, got %q", desired.OpenAPIURL)
	}

	if desired.Settings["feature_flag"] != "enabled" {
		t.Fatalf("expected normalized settings map, got %#v", desired.Settings)
	}
}

func TestSnapshotCombinesDesiredAndReportedConfigs(t *testing.T) {
	store, err := NewStore(`{"tickets-service":{"databaseConnectionString":"Server=mysql;Database=exiledcms_tickets;"}}`)
	if err != nil {
		t.Fatalf("expected module config json to parse, got %v", err)
	}

	err = store.SetReported(ReportedConfig{
		ModuleID:            "tickets-service",
		DatabaseConfigured:  true,
		ConfigurationSource: "nats",
	})
	if err != nil {
		t.Fatalf("expected reported config to be stored, got %v", err)
	}

	snapshot := store.Snapshot()
	if len(snapshot.Items) != 1 {
		t.Fatalf("expected one module config snapshot item, got %d", len(snapshot.Items))
	}

	item := snapshot.Items[0]
	if item.Desired == nil || item.Reported == nil {
		t.Fatalf("expected both desired and reported config in snapshot, got %#v", item)
	}

	if !item.Reported.DatabaseConfigured {
		t.Fatalf("expected reported config to indicate database readiness")
	}
}

func TestSanitizedSnapshotRedactsSecretsButKeepsUsefulDiagnostics(t *testing.T) {
	store, err := NewStore(`{
		"auth-service": {
			"databaseConnectionString": "Server=mysql;Database=exiledcms_auth;User Id=auth;Password=super-secret;",
			"settings": {
				"auth.jwt.secret": "jwt-super-secret",
				"feature_flag": "enabled",
				"nested.connectionString": "Server=mysql;Database=cache;Password=cache-secret;"
			}
		}
	}`)
	if err != nil {
		t.Fatalf("expected module config json to parse, got %v", err)
	}

	if err := store.SetReported(ReportedConfig{
		ModuleID: "auth-service",
		Settings: map[string]string{
			"lastAppliedToken": "abc123",
			"configurationSource": "nats-push",
		},
	}); err != nil {
		t.Fatalf("expected reported config to be stored, got %v", err)
	}

	snapshot := store.SanitizedSnapshot()
	if len(snapshot.Items) != 1 {
		t.Fatalf("expected one module config snapshot item, got %d", len(snapshot.Items))
	}

	item := snapshot.Items[0]
	if item.Desired == nil || item.Reported == nil {
		t.Fatalf("expected desired and reported config, got %#v", item)
	}

	expectedDSN := "Server=mysql;Database=exiledcms_auth;User Id=auth;Password=[redacted];"
	if item.Desired.DatabaseConnectionString != expectedDSN {
		t.Fatalf("expected sanitized db connection string %q, got %q", expectedDSN, item.Desired.DatabaseConnectionString)
	}

	if item.Desired.Settings["auth.jwt.secret"] != "[redacted]" {
		t.Fatalf("expected jwt secret to be redacted, got %#v", item.Desired.Settings)
	}

	expectedNested := "Server=mysql;Database=cache;Password=[redacted];"
	if item.Desired.Settings["nested.connectionString"] != expectedNested {
		t.Fatalf("expected nested connection string to be sanitized, got %#v", item.Desired.Settings)
	}

	if item.Desired.Settings["feature_flag"] != "enabled" {
		t.Fatalf("expected non-sensitive settings to survive sanitization, got %#v", item.Desired.Settings)
	}

	if item.Reported.Settings["lastAppliedToken"] != "[redacted]" {
		t.Fatalf("expected reported token to be redacted, got %#v", item.Reported.Settings)
	}

	if item.Reported.Settings["configurationSource"] != "nats-push" {
		t.Fatalf("expected non-sensitive reported settings to survive sanitization, got %#v", item.Reported.Settings)
	}
}
