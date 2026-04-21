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
