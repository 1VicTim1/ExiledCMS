package registry

import (
	"errors"
	"slices"
	"sort"
	"strings"
	"sync"
	"time"
)

const PlatformAPIVersion = "v1"

type CapabilityDescriptor struct {
	Key         string `json:"key"`
	DisplayName string `json:"displayName"`
	Description string `json:"description"`
}

type Compatibility struct {
	PlatformAPIVersion string `json:"platformApiVersion"`
	MinCoreVersion     string `json:"minCoreVersion,omitempty"`
	MaxCoreVersion     string `json:"maxCoreVersion,omitempty"`
}

type PermissionDefinition struct {
	Key         string `json:"key"`
	DisplayName string `json:"displayName"`
	Scope       string `json:"scope"`
	Description string `json:"description"`
	Dangerous   bool   `json:"dangerous,omitempty"`
}

type PluginManifest struct {
	ID            string                 `json:"id"`
	Name          string                 `json:"name"`
	Version       string                 `json:"version"`
	Description   string                 `json:"description,omitempty"`
	Provider      string                 `json:"provider,omitempty"`
	BackendURL    string                 `json:"backendUrl,omitempty"`
	Capabilities  []string               `json:"capabilities,omitempty"`
	Compatibility Compatibility          `json:"compatibility"`
	ConfigSchema  map[string]any         `json:"configSchema,omitempty"`
	Permissions   []PermissionDefinition `json:"permissions,omitempty"`
}

type ThemeManifest struct {
	ID             string         `json:"id"`
	Name           string         `json:"name"`
	Version        string         `json:"version"`
	Description    string         `json:"description,omitempty"`
	Provider       string         `json:"provider,omitempty"`
	EntryPoint     string         `json:"entryPoint,omitempty"`
	Slots          []string       `json:"slots,omitempty"`
	Supports       []string       `json:"supports,omitempty"`
	Compatibility  Compatibility  `json:"compatibility"`
	SettingsSchema map[string]any `json:"settingsSchema,omitempty"`
}

type ModuleRegistration struct {
	ID                    string              `json:"id"`
	Name                  string              `json:"name"`
	Version               string              `json:"version"`
	Kind                  string              `json:"kind"`
	BaseURL               string              `json:"baseUrl,omitempty"`
	HealthURL             string              `json:"healthUrl,omitempty"`
	OpenAPIURL            string              `json:"openApiUrl,omitempty"`
	SwaggerUIURL          string              `json:"swaggerUiUrl,omitempty"`
	ConfigRequestSubject  string              `json:"configRequestSubject,omitempty"`
	ConfigDesiredSubject  string              `json:"configDesiredSubject,omitempty"`
	ConfigReportedSubject string              `json:"configReportedSubject,omitempty"`
	RegisteredAt          time.Time           `json:"registeredAt"`
	OwnedCapabilities     []string            `json:"ownedCapabilities,omitempty"`
	Tags                  []string            `json:"tags,omitempty"`
	Topology              *ModuleTopology     `json:"topology,omitempty"`
	Documentation         []DocumentationLink `json:"documentation,omitempty"`
}

type ModuleTopology struct {
	DeploymentMode string   `json:"deploymentMode,omitempty"`
	DataSources    []string `json:"dataSources,omitempty"`
	Dependencies   []string `json:"dependencies,omitempty"`
}

type DocumentationLink struct {
	Key         string `json:"key"`
	Title       string `json:"title"`
	Href        string `json:"href"`
	Description string `json:"description,omitempty"`
}

type Snapshot struct {
	GeneratedAt   time.Time              `json:"generatedAt"`
	ActiveThemeID string                 `json:"activeThemeId,omitempty"`
	Plugins       []PluginManifest       `json:"plugins"`
	Themes        []ThemeManifest        `json:"themes"`
	Modules       []ModuleRegistration   `json:"modules"`
	Permissions   []PermissionDefinition `json:"permissions"`
}

type MemoryStore struct {
	mu            sync.RWMutex
	plugins       map[string]PluginManifest
	themes        map[string]ThemeManifest
	modules       map[string]ModuleRegistration
	permissions   map[string]PermissionDefinition
	activeThemeID string
}

func NewMemoryStore() *MemoryStore {
	return &MemoryStore{
		plugins:     make(map[string]PluginManifest),
		themes:      make(map[string]ThemeManifest),
		modules:     make(map[string]ModuleRegistration),
		permissions: make(map[string]PermissionDefinition),
	}
}

func KnownCapabilities() []CapabilityDescriptor {
	return []CapabilityDescriptor{
		{Key: "plugin.registry", DisplayName: "Plugin Registry", Description: "Registers backend plugins and declares platform extension points."},
		{Key: "theme.registry", DisplayName: "Theme Registry", Description: "Registers storefront themes and manages the active theme selection."},
		{Key: "permission.registry", DisplayName: "Permission Registry", Description: "Registers permission keys exposed by platform modules and plugins."},
		{Key: "module.registry", DisplayName: "Module Registry", Description: "Tracks module service endpoints and module metadata."},
		{Key: "log.ingestion", DisplayName: "Centralized Log Ingestion", Description: "Accepts structured logs from modules, keeps a temporary in-memory buffer, and forwards selected entries to external sinks such as Sentry."},
		{Key: "module.config.sync", DisplayName: "Module Runtime Config Sync", Description: "Distributes authoritative runtime configuration from platform-core to modules and receives effective configuration reports back over the shared transport bus."},
		{Key: "openapi.aggregation", DisplayName: "OpenAPI Aggregation", Description: "Provides a centralized catalog of platform and module OpenAPI documents for operators and tooling."},
		{Key: "admin.page", DisplayName: "Admin Page", Description: "Allows a plugin or module to add a protected admin panel section."},
		{Key: "site.widget", DisplayName: "Site Widget", Description: "Allows a plugin or module to extend the public storefront rendering surface."},
		{Key: "payment.provider", DisplayName: "Payment Provider", Description: "Declares a pluggable payment adapter for the payments service."},
		{Key: "minecraft.bridge", DisplayName: "Minecraft Bridge", Description: "Declares a bridge capability for the future JVM plugin integration."},
	}
}

func (s *MemoryStore) RegisterPlugin(plugin PluginManifest) error {
	if strings.TrimSpace(plugin.ID) == "" {
		return errors.New("plugin id is required")
	}

	if strings.TrimSpace(plugin.Name) == "" {
		return errors.New("plugin name is required")
	}

	if strings.TrimSpace(plugin.Version) == "" {
		return errors.New("plugin version is required")
	}

	plugin.Compatibility = normalizeCompatibility(plugin.Compatibility)
	plugin.Capabilities = normalizeStringSlice(plugin.Capabilities)

	s.mu.Lock()
	defer s.mu.Unlock()

	s.plugins[plugin.ID] = plugin

	for _, permission := range plugin.Permissions {
		if strings.TrimSpace(permission.Key) == "" {
			return errors.New("plugin permission key is required")
		}

		s.permissions[permission.Key] = normalizePermission(permission)
	}

	return nil
}

func (s *MemoryStore) RegisterTheme(theme ThemeManifest) error {
	if strings.TrimSpace(theme.ID) == "" {
		return errors.New("theme id is required")
	}

	if strings.TrimSpace(theme.Name) == "" {
		return errors.New("theme name is required")
	}

	if strings.TrimSpace(theme.Version) == "" {
		return errors.New("theme version is required")
	}

	theme.Compatibility = normalizeCompatibility(theme.Compatibility)
	theme.Slots = normalizeStringSlice(theme.Slots)
	theme.Supports = normalizeStringSlice(theme.Supports)

	s.mu.Lock()
	defer s.mu.Unlock()

	s.themes[theme.ID] = theme

	if s.activeThemeID == "" {
		s.activeThemeID = theme.ID
	}

	return nil
}

func (s *MemoryStore) ActivateTheme(themeID string) error {
	themeID = strings.TrimSpace(themeID)
	if themeID == "" {
		return errors.New("theme id is required")
	}

	s.mu.Lock()
	defer s.mu.Unlock()

	if _, ok := s.themes[themeID]; !ok {
		return errors.New("theme was not found")
	}

	s.activeThemeID = themeID
	return nil
}

func (s *MemoryStore) RegisterModule(module ModuleRegistration) error {
	if strings.TrimSpace(module.ID) == "" {
		return errors.New("module id is required")
	}

	if strings.TrimSpace(module.Name) == "" {
		return errors.New("module name is required")
	}

	if strings.TrimSpace(module.Version) == "" {
		return errors.New("module version is required")
	}

	if strings.TrimSpace(module.Kind) == "" {
		return errors.New("module kind is required")
	}

	module.OwnedCapabilities = normalizeStringSlice(module.OwnedCapabilities)
	module.Tags = normalizeStringSlice(module.Tags)
	module.OpenAPIURL = strings.TrimSpace(module.OpenAPIURL)
	module.SwaggerUIURL = strings.TrimSpace(module.SwaggerUIURL)
	module.ConfigRequestSubject = strings.TrimSpace(module.ConfigRequestSubject)
	module.ConfigDesiredSubject = strings.TrimSpace(module.ConfigDesiredSubject)
	module.ConfigReportedSubject = strings.TrimSpace(module.ConfigReportedSubject)
	module.Topology = normalizeModuleTopology(module.Topology)
	module.Documentation = normalizeDocumentationLinks(module.Documentation)
	if module.RegisteredAt.IsZero() {
		module.RegisteredAt = time.Now().UTC()
	}

	s.mu.Lock()
	defer s.mu.Unlock()

	s.modules[module.ID] = module
	return nil
}

func (s *MemoryStore) RegisterPermission(permission PermissionDefinition) error {
	if strings.TrimSpace(permission.Key) == "" {
		return errors.New("permission key is required")
	}

	s.mu.Lock()
	defer s.mu.Unlock()
	s.permissions[permission.Key] = normalizePermission(permission)
	return nil
}

func (s *MemoryStore) ActiveThemeID() string {
	s.mu.RLock()
	defer s.mu.RUnlock()

	return s.activeThemeID
}

func (s *MemoryStore) Snapshot() Snapshot {
	return Snapshot{
		GeneratedAt:   time.Now().UTC(),
		ActiveThemeID: s.ActiveThemeID(),
		Plugins:       s.ListPlugins(),
		Themes:        s.ListThemes(),
		Modules:       s.ListModules(),
		Permissions:   s.ListPermissions(),
	}
}

func (s *MemoryStore) ListPlugins() []PluginManifest {
	s.mu.RLock()
	defer s.mu.RUnlock()

	plugins := make([]PluginManifest, 0, len(s.plugins))
	for _, item := range s.plugins {
		plugins = append(plugins, item)
	}

	sort.Slice(plugins, func(i, j int) bool {
		return plugins[i].ID < plugins[j].ID
	})

	return plugins
}

func (s *MemoryStore) ListThemes() []ThemeManifest {
	s.mu.RLock()
	defer s.mu.RUnlock()

	themes := make([]ThemeManifest, 0, len(s.themes))
	for _, item := range s.themes {
		themes = append(themes, item)
	}

	sort.Slice(themes, func(i, j int) bool {
		return themes[i].ID < themes[j].ID
	})

	return themes
}

func (s *MemoryStore) ListModules() []ModuleRegistration {
	s.mu.RLock()
	defer s.mu.RUnlock()

	modules := make([]ModuleRegistration, 0, len(s.modules))
	for _, item := range s.modules {
		modules = append(modules, item)
	}

	sort.Slice(modules, func(i, j int) bool {
		return modules[i].ID < modules[j].ID
	})

	return modules
}

func (s *MemoryStore) ListPermissions() []PermissionDefinition {
	s.mu.RLock()
	defer s.mu.RUnlock()

	permissions := make([]PermissionDefinition, 0, len(s.permissions))
	for _, item := range s.permissions {
		permissions = append(permissions, item)
	}

	sort.Slice(permissions, func(i, j int) bool {
		return permissions[i].Key < permissions[j].Key
	})

	return permissions
}

func normalizeCompatibility(compatibility Compatibility) Compatibility {
	if strings.TrimSpace(compatibility.PlatformAPIVersion) == "" {
		compatibility.PlatformAPIVersion = PlatformAPIVersion
	}

	return compatibility
}

func normalizePermission(permission PermissionDefinition) PermissionDefinition {
	permission.Key = strings.TrimSpace(permission.Key)
	permission.DisplayName = strings.TrimSpace(permission.DisplayName)
	permission.Scope = strings.TrimSpace(permission.Scope)
	permission.Description = strings.TrimSpace(permission.Description)
	return permission
}

func normalizeModuleTopology(topology *ModuleTopology) *ModuleTopology {
	if topology == nil {
		return nil
	}

	normalized := &ModuleTopology{
		DeploymentMode: strings.TrimSpace(topology.DeploymentMode),
		DataSources:    normalizeStringSlice(topology.DataSources),
		Dependencies:   normalizeStringSlice(topology.Dependencies),
	}

	if normalized.DeploymentMode == "" && len(normalized.DataSources) == 0 && len(normalized.Dependencies) == 0 {
		return nil
	}

	return normalized
}

func normalizeDocumentationLinks(values []DocumentationLink) []DocumentationLink {
	if len(values) == 0 {
		return nil
	}

	normalized := make([]DocumentationLink, 0, len(values))
	seen := make(map[string]struct{}, len(values))
	for _, value := range values {
		value.Key = strings.TrimSpace(value.Key)
		value.Title = strings.TrimSpace(value.Title)
		value.Href = strings.TrimSpace(value.Href)
		value.Description = strings.TrimSpace(value.Description)

		if value.Key == "" || value.Href == "" {
			continue
		}

		if value.Title == "" {
			value.Title = value.Key
		}

		identity := strings.ToLower(value.Key) + "|" + value.Href
		if _, exists := seen[identity]; exists {
			continue
		}

		seen[identity] = struct{}{}
		normalized = append(normalized, value)
	}

	if len(normalized) == 0 {
		return nil
	}

	sort.Slice(normalized, func(i, j int) bool {
		if normalized[i].Key == normalized[j].Key {
			return normalized[i].Href < normalized[j].Href
		}

		return normalized[i].Key < normalized[j].Key
	})

	return normalized
}

func normalizeStringSlice(values []string) []string {
	if len(values) == 0 {
		return nil
	}

	normalized := make([]string, 0, len(values))
	for _, value := range values {
		value = strings.TrimSpace(value)
		if value == "" {
			continue
		}

		if !slices.Contains(normalized, value) {
			normalized = append(normalized, value)
		}
	}

	sort.Strings(normalized)
	return normalized
}
