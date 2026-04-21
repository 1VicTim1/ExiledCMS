package registry

import "testing"

func TestRegisterModuleNormalizesTopologyAndDocumentation(t *testing.T) {
	store := NewMemoryStore()

	err := store.RegisterModule(ModuleRegistration{
		ID:                    "tickets-service",
		Name:                  "Tickets Service",
		Version:               "1.0.0",
		Kind:                  "service",
		OpenAPIURL:            " http://tickets-service:8080/swagger/v1/swagger.json ",
		SwaggerUIURL:          " http://tickets-service:8080/swagger ",
		ConfigRequestSubject:  " platform.config.request.tickets-service ",
		ConfigDesiredSubject:  " platform.config.desired.tickets-service ",
		ConfigReportedSubject: " platform.config.reported.tickets-service ",
		OwnedCapabilities: []string{
			"support.tickets",
			"support.tickets",
			"",
		},
		Tags: []string{
			"dotnet",
			" dotnet ",
			"observability",
		},
		Topology: &ModuleTopology{
			DeploymentMode: " remote-service ",
			DataSources: []string{
				"mysql:exiledcms_tickets",
				" mysql:exiledcms_tickets ",
			},
			Dependencies: []string{
				"platform-core",
				" nats ",
				"platform-core",
			},
		},
		Documentation: []DocumentationLink{
			{Key: " sentry ", Title: "", Href: " docs/sentry.md ", Description: " sentry guide "},
			{Key: "sentry", Href: "docs/sentry.md"},
			{Key: " development ", Title: " Module Guide ", Href: " contracts/modules/development.md "},
			{Key: "", Href: "ignored.md"},
		},
	})
	if err != nil {
		t.Fatalf("expected module registration to succeed, got %v", err)
	}

	modules := store.ListModules()
	if len(modules) != 1 {
		t.Fatalf("expected one module, got %d", len(modules))
	}

	module := modules[0]
	if module.RegisteredAt.IsZero() {
		t.Fatalf("expected module registered time to be set")
	}

	if len(module.OwnedCapabilities) != 1 || module.OwnedCapabilities[0] != "support.tickets" {
		t.Fatalf("expected normalized capabilities, got %#v", module.OwnedCapabilities)
	}

	if len(module.Tags) != 2 || module.Tags[0] != "dotnet" || module.Tags[1] != "observability" {
		t.Fatalf("expected normalized tags, got %#v", module.Tags)
	}

	if module.OpenAPIURL != "http://tickets-service:8080/swagger/v1/swagger.json" || module.SwaggerUIURL != "http://tickets-service:8080/swagger" {
		t.Fatalf("expected normalized openapi metadata, got openapi=%q swagger=%q", module.OpenAPIURL, module.SwaggerUIURL)
	}

	if module.ConfigDesiredSubject != "platform.config.desired.tickets-service" || module.ConfigReportedSubject != "platform.config.reported.tickets-service" {
		t.Fatalf("expected normalized config transport subjects, got %#v", module)
	}

	if module.Topology == nil {
		t.Fatalf("expected topology to be preserved")
	}

	if module.Topology.DeploymentMode != "remote-service" {
		t.Fatalf("expected normalized deployment mode, got %q", module.Topology.DeploymentMode)
	}

	if len(module.Topology.DataSources) != 1 || module.Topology.DataSources[0] != "mysql:exiledcms_tickets" {
		t.Fatalf("expected normalized data sources, got %#v", module.Topology.DataSources)
	}

	if len(module.Topology.Dependencies) != 2 || module.Topology.Dependencies[0] != "nats" || module.Topology.Dependencies[1] != "platform-core" {
		t.Fatalf("expected normalized dependencies, got %#v", module.Topology.Dependencies)
	}

	if len(module.Documentation) != 2 {
		t.Fatalf("expected deduplicated documentation links, got %#v", module.Documentation)
	}

	if module.Documentation[0].Key != "development" || module.Documentation[0].Href != "contracts/modules/development.md" {
		t.Fatalf("expected development documentation link first, got %#v", module.Documentation[0])
	}

	if module.Documentation[1].Key != "sentry" || module.Documentation[1].Title != "sentry" || module.Documentation[1].Href != "docs/sentry.md" {
		t.Fatalf("expected sentry documentation to be normalized, got %#v", module.Documentation[1])
	}
}

func TestRegisterPluginRegistersPermissionsAndCompatibility(t *testing.T) {
	store := NewMemoryStore()

	err := store.RegisterPlugin(PluginManifest{
		ID:      "catalog-plugin",
		Name:    "Catalog Plugin",
		Version: "1.0.0",
		Capabilities: []string{
			"catalog.read",
			" catalog.read ",
			"catalog.write",
		},
		Permissions: []PermissionDefinition{
			{
				Key:         " catalog.manage ",
				DisplayName: " Manage catalog ",
				Scope:       " catalog ",
				Description: " Manage catalog entries ",
			},
		},
	})
	if err != nil {
		t.Fatalf("expected plugin registration to succeed, got %v", err)
	}

	plugins := store.ListPlugins()
	if len(plugins) != 1 {
		t.Fatalf("expected one plugin, got %d", len(plugins))
	}

	plugin := plugins[0]
	if plugin.Compatibility.PlatformAPIVersion != PlatformAPIVersion {
		t.Fatalf("expected default compatibility version %q, got %q", PlatformAPIVersion, plugin.Compatibility.PlatformAPIVersion)
	}

	if len(plugin.Capabilities) != 2 || plugin.Capabilities[0] != "catalog.read" || plugin.Capabilities[1] != "catalog.write" {
		t.Fatalf("expected normalized plugin capabilities, got %#v", plugin.Capabilities)
	}

	permissions := store.ListPermissions()
	if len(permissions) != 1 {
		t.Fatalf("expected one permission, got %d", len(permissions))
	}

	permission := permissions[0]
	if permission.Key != "catalog.manage" || permission.DisplayName != "Manage catalog" || permission.Scope != "catalog" {
		t.Fatalf("expected permission to be normalized, got %#v", permission)
	}
}

func TestKnownCapabilitiesIncludesCentralizedLogIngestion(t *testing.T) {
	items := KnownCapabilities()
	for _, item := range items {
		if item.Key == "log.ingestion" {
			return
		}
	}

	t.Fatalf("expected known capabilities to include log.ingestion")
}
