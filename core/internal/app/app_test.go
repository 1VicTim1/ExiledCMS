package app

import (
	"testing"

	"github.com/exiledcms/platform-core/internal/config"
)

func TestNewSeedsCoreRegistryAndLogging(t *testing.T) {
	application := New(config.Config{
		ServiceName:         "platform-core",
		Environment:         "test",
		HTTPPort:            8080,
		ModuleConfigJSON:    `{"tickets-service":{"databaseConnectionString":"Server=mysql;Database=exiledcms_tickets;"}}`,
		LogBufferMaxEntries: 64,
	})

	if application.Logger == nil {
		t.Fatalf("expected application logger to be initialized")
	}

	if application.Logs == nil || application.Logs.Store() == nil {
		t.Fatalf("expected application log router to be initialized")
	}

	if application.Configs == nil {
		t.Fatalf("expected module config store to be initialized")
	}

	if _, ok := application.Configs.DesiredFor("tickets-service"); !ok {
		t.Fatalf("expected desired tickets-service config to be loaded from core config")
	}

	modules := application.Registry.ListModules()
	if len(modules) != 1 {
		t.Fatalf("expected platform-core to be seeded as one module, got %d", len(modules))
	}

	module := modules[0]
	if module.ID != "platform-core" {
		t.Fatalf("expected platform-core module registration, got %#v", module)
	}

	if module.Topology == nil || module.Topology.DeploymentMode != "control-plane" {
		t.Fatalf("expected seeded module topology, got %#v", module.Topology)
	}

	if module.OpenAPIURL == "" || module.SwaggerUIURL == "" {
		t.Fatalf("expected core module to expose centralized openapi metadata, got %#v", module)
	}

	if len(module.Documentation) < 3 {
		t.Fatalf("expected seeded documentation links, got %#v", module.Documentation)
	}

	permissions := application.Registry.ListPermissions()
	foundLogsView := false
	for _, permission := range permissions {
		if permission.Key == "platform.logs.view" {
			foundLogsView = true
			break
		}
	}

	if !foundLogsView {
		t.Fatalf("expected platform.logs.view permission to be seeded")
	}

	if application.Handler() == nil {
		t.Fatalf("expected application handler to be available")
	}
}
