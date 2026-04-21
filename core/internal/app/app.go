package app

import (
	"log/slog"
	"net/http"
	"os"
	"time"

	"github.com/exiledcms/platform-core/internal/config"
	"github.com/exiledcms/platform-core/internal/httpapi"
	"github.com/exiledcms/platform-core/internal/logging"
	"github.com/exiledcms/platform-core/internal/moduleconfig"
	"github.com/exiledcms/platform-core/internal/registry"
	"github.com/exiledcms/platform-core/internal/version"
)

type Application struct {
	Config   config.Config
	Logger   *slog.Logger
	Registry *registry.MemoryStore
	Logs     *logging.Router
	Configs  *moduleconfig.Store
}

func New(cfg config.Config) *Application {
	logs, sentryInitErr := logging.NewRouter(logging.RouterOptions{
		ServiceName:      cfg.ServiceName,
		Environment:      cfg.Environment,
		Release:          version.BuildVersion,
		SentryDSN:        cfg.SentryDSN,
		SentryMinLevel:   cfg.SentryMinLevel,
		BufferMaxEntries: cfg.LogBufferMaxEntries,
	})

	logger := slog.New(logging.NewHandler(
		slog.NewJSONHandler(os.Stdout, &slog.HandlerOptions{
			Level: slog.LevelInfo,
		}),
		logs,
	))

	if sentryInitErr != nil {
		logger.Error("sentry integration disabled due to initialization error", "error", sentryInitErr)
	}

	store := registry.NewMemoryStore()
	moduleConfigs, moduleConfigErr := moduleconfig.NewStore(cfg.ModuleConfigJSON)
	if moduleConfigErr != nil {
		logger.Warn("module config json could not be parsed, continuing with an empty config store", "error", moduleConfigErr)
		moduleConfigs, _ = moduleconfig.NewStore("")
	}

	seedCoreRegistry(store, cfg)

	return &Application{
		Config:   cfg,
		Logger:   logger,
		Registry: store,
		Logs:     logs,
		Configs:  moduleConfigs,
	}
}

func (a *Application) Handler() http.Handler {
	return httpapi.NewServer(a.Logger, a.Config, a.Registry, a.Logs, a.Configs)
}

func seedCoreRegistry(store *registry.MemoryStore, cfg config.Config) {
	_ = store.RegisterModule(registry.ModuleRegistration{
		ID:           "platform-core",
		Name:         "Platform Core",
		Version:      version.BuildVersion,
		Kind:         "core",
		BaseURL:      "http://platform-core:" + cfg.HTTPPortString(),
		HealthURL:    "http://platform-core:" + cfg.HTTPPortString() + "/healthz",
		OpenAPIURL:   "http://platform-core:" + cfg.HTTPPortString() + "/api/v1/platform/openapi/core.json",
		SwaggerUIURL: "http://platform-core:" + cfg.HTTPPortString() + "/swagger",
		RegisteredAt: time.Now().UTC(),
		OwnedCapabilities: []string{
			"module.registry",
			"plugin.registry",
			"theme.registry",
			"permission.registry",
			"platform.capabilities",
			"log.ingestion",
			"module.config.sync",
			"openapi.aggregation",
		},
		Tags: []string{"go", "core", "control-plane", "observability"},
		Topology: &registry.ModuleTopology{
			DeploymentMode: "control-plane",
			DataSources: []string{
				"in-memory registry",
				"incoming module log stream",
			},
			Dependencies: []string{
				"registered modules",
				"sentry (optional)",
			},
		},
		Documentation: []registry.DocumentationLink{
			{Key: "development", Title: "Module Platform Guide", Href: "contracts/modules/README.md", Description: "Primary guide explaining how ExiledCMS modules work and integrate with platform-core."},
			{Key: "development", Title: "Module Development Guide", Href: "contracts/modules/development.md", Description: "Implementation-level guidance for registering modules and forwarding logs."},
			{Key: "observability", Title: "Module Observability Guide", Href: "contracts/modules/observability.md", Description: "Centralized logging, buffer review, and operational guidance."},
			{Key: "sentry", Title: "Sentry Topology Guide", Href: "contracts/modules/observability.md#recommended-sentry-topology", Description: "Explains how platform-core and modules should use Sentry together."},
		},
	})

	_ = store.RegisterPermission(registry.PermissionDefinition{
		Key:         "platform.registry.view",
		DisplayName: "View platform registry",
		Scope:       "platform",
		Description: "Allows access to the platform registry snapshot and capability metadata.",
	})

	_ = store.RegisterPermission(registry.PermissionDefinition{
		Key:         "platform.plugins.manage",
		DisplayName: "Manage plugins",
		Scope:       "platform",
		Description: "Allows plugin registration and plugin configuration changes.",
	})

	_ = store.RegisterPermission(registry.PermissionDefinition{
		Key:         "platform.themes.manage",
		DisplayName: "Manage themes",
		Scope:       "platform",
		Description: "Allows theme registration and active theme switching.",
	})

	_ = store.RegisterPermission(registry.PermissionDefinition{
		Key:         "platform.logs.view",
		DisplayName: "View buffered platform logs",
		Scope:       "platform",
		Description: "Allows access to the centralized in-memory log buffer managed by platform-core.",
	})
}
