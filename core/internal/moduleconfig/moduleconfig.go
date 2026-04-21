package moduleconfig

import (
	"encoding/json"
	"errors"
	"sort"
	"strings"
	"sync"
	"time"
)

const (
	// ConfigRequestSubjectPrefix is the NATS subject prefix used by modules to request
	// their latest authoritative configuration from platform-core.
	ConfigRequestSubjectPrefix = "platform.config.request."
	// ConfigDesiredSubjectPrefix is the NATS subject prefix used by platform-core to
	// publish desired runtime configuration snapshots for modules.
	ConfigDesiredSubjectPrefix = "platform.config.desired."
	// ConfigReportedSubjectPrefix is the NATS subject prefix used by modules to report
	// their effective runtime configuration back to platform-core.
	ConfigReportedSubjectPrefix = "platform.config.reported."
)

// DesiredConfig describes the authoritative configuration platform-core distributes
// to a module. Database connection strings are intentionally centralized here so the
// module does not need to own or persist its own database configuration.
type DesiredConfig struct {
	ModuleID                 string            `json:"moduleId"`
	Revision                 string            `json:"revision,omitempty"`
	PublishedAt              time.Time         `json:"publishedAt"`
	DatabaseConnectionString string            `json:"databaseConnectionString,omitempty"`
	OpenAPIURL               string            `json:"openApiUrl,omitempty"`
	SwaggerUIURL             string            `json:"swaggerUiUrl,omitempty"`
	Settings                 map[string]string `json:"settings,omitempty"`
}

// ReportedConfig describes the effective configuration snapshot a module reports back
// to platform-core after it applies the desired configuration it received.
type ReportedConfig struct {
	ModuleID            string            `json:"moduleId"`
	ReportedAt          time.Time         `json:"reportedAt"`
	DatabaseConfigured  bool              `json:"databaseConfigured"`
	OpenAPIURL          string            `json:"openApiUrl,omitempty"`
	SwaggerUIURL        string            `json:"swaggerUiUrl,omitempty"`
	ConfigurationSource string            `json:"configurationSource,omitempty"`
	Settings            map[string]string `json:"settings,omitempty"`
}

// ModuleConfigView exposes the desired and reported configuration for a single module.
type ModuleConfigView struct {
	ModuleID string          `json:"moduleId"`
	Desired  *DesiredConfig  `json:"desired,omitempty"`
	Reported *ReportedConfig `json:"reported,omitempty"`
}

// Snapshot is a serializable view of every known desired and reported module config.
type Snapshot struct {
	GeneratedAt time.Time          `json:"generatedAt"`
	Items       []ModuleConfigView `json:"items"`
}

type desiredConfigInput struct {
	Revision                 string            `json:"revision"`
	DatabaseConnectionString string            `json:"databaseConnectionString"`
	OpenAPIURL               string            `json:"openApiUrl"`
	SwaggerUIURL             string            `json:"swaggerUiUrl"`
	Settings                 map[string]string `json:"settings"`
}

// Store keeps authoritative and reported module configuration snapshots in memory.
// It is intentionally small and deterministic so the HTTP API and NATS sync layer can
// both use the same state without additional persistence.
type Store struct {
	mu       sync.RWMutex
	desired  map[string]DesiredConfig
	reported map[string]ReportedConfig
}

// NewStore creates a store and optionally seeds it from the MODULE_CONFIG_JSON value.
func NewStore(rawJSON string) (*Store, error) {
	store := &Store{
		desired:  make(map[string]DesiredConfig),
		reported: make(map[string]ReportedConfig),
	}

	if strings.TrimSpace(rawJSON) == "" {
		return store, nil
	}

	var input map[string]desiredConfigInput
	if err := json.Unmarshal([]byte(rawJSON), &input); err != nil {
		return nil, err
	}

	for rawModuleID, item := range input {
		moduleID := strings.TrimSpace(rawModuleID)
		if moduleID == "" {
			continue
		}

		store.desired[moduleID] = normalizeDesired(DesiredConfig{
			ModuleID:                 moduleID,
			Revision:                 item.Revision,
			DatabaseConnectionString: item.DatabaseConnectionString,
			OpenAPIURL:               item.OpenAPIURL,
			SwaggerUIURL:             item.SwaggerUIURL,
			Settings:                 item.Settings,
		})
	}

	return store, nil
}

// RequestSubject returns the subject a module should use to request its latest config.
func RequestSubject(moduleID string) string {
	return ConfigRequestSubjectPrefix + strings.TrimSpace(moduleID)
}

// DesiredSubject returns the subject platform-core uses to fan out desired config.
func DesiredSubject(moduleID string) string {
	return ConfigDesiredSubjectPrefix + strings.TrimSpace(moduleID)
}

// ReportedSubject returns the subject a module uses to report applied config.
func ReportedSubject(moduleID string) string {
	return ConfigReportedSubjectPrefix + strings.TrimSpace(moduleID)
}

// SetDesired saves the authoritative desired configuration for a module.
func (s *Store) SetDesired(config DesiredConfig) error {
	config = normalizeDesired(config)
	if config.ModuleID == "" {
		return errors.New("module config module id is required")
	}

	s.mu.Lock()
	defer s.mu.Unlock()
	if config.PublishedAt.IsZero() {
		config.PublishedAt = time.Now().UTC()
	}
	if config.Revision == "" {
		config.Revision = config.PublishedAt.Format(time.RFC3339Nano)
	}
	
	s.desired[config.ModuleID] = config
	return nil
}

// SetReported saves the last effective configuration snapshot reported by a module.
func (s *Store) SetReported(config ReportedConfig) error {
	config = normalizeReported(config)
	if config.ModuleID == "" {
		return errors.New("reported module id is required")
	}

	s.mu.Lock()
	defer s.mu.Unlock()
	if config.ReportedAt.IsZero() {
		config.ReportedAt = time.Now().UTC()
	}
	s.reported[config.ModuleID] = config
	return nil
}

// DesiredFor returns the desired configuration for a module, if known.
func (s *Store) DesiredFor(moduleID string) (DesiredConfig, bool) {
	s.mu.RLock()
	defer s.mu.RUnlock()
	config, ok := s.desired[strings.TrimSpace(moduleID)]
	return config, ok
}

// ReportedFor returns the last reported configuration for a module, if known.
func (s *Store) ReportedFor(moduleID string) (ReportedConfig, bool) {
	s.mu.RLock()
	defer s.mu.RUnlock()
	config, ok := s.reported[strings.TrimSpace(moduleID)]
	return config, ok
}

// ListDesired returns all desired configurations in a deterministic order.
func (s *Store) ListDesired() []DesiredConfig {
	s.mu.RLock()
	defer s.mu.RUnlock()
	items := make([]DesiredConfig, 0, len(s.desired))
	for _, item := range s.desired {
		items = append(items, item)
	}
	sort.Slice(items, func(i, j int) bool {
		return items[i].ModuleID < items[j].ModuleID
	})
	return items
}

// Snapshot builds a combined view of desired and reported configurations.
func (s *Store) Snapshot() Snapshot {
	s.mu.RLock()
	defer s.mu.RUnlock()
	moduleIDs := make(map[string]struct{}, len(s.desired)+len(s.reported))
	for moduleID := range s.desired {
		moduleIDs[moduleID] = struct{}{}
	}
	for moduleID := range s.reported {
		moduleIDs[moduleID] = struct{}{}
	}

	ordered := make([]string, 0, len(moduleIDs))
	for moduleID := range moduleIDs {
		ordered = append(ordered, moduleID)
	}
	sort.Strings(ordered)

	items := make([]ModuleConfigView, 0, len(ordered))
	for _, moduleID := range ordered {
		view := ModuleConfigView{ModuleID: moduleID}
		if desired, ok := s.desired[moduleID]; ok {
			copy := desired
			view.Desired = &copy
		}
		if reported, ok := s.reported[moduleID]; ok {
			copy := reported
			view.Reported = &copy
		}
		items = append(items, view)
	}

	return Snapshot{
		GeneratedAt: time.Now().UTC(),
		Items:       items,
	}
}

func normalizeDesired(config DesiredConfig) DesiredConfig {
	config.ModuleID = strings.TrimSpace(config.ModuleID)
	config.Revision = strings.TrimSpace(config.Revision)
	config.DatabaseConnectionString = strings.TrimSpace(config.DatabaseConnectionString)
	config.OpenAPIURL = strings.TrimSpace(config.OpenAPIURL)
	config.SwaggerUIURL = strings.TrimSpace(config.SwaggerUIURL)
	config.Settings = normalizeSettings(config.Settings)
	if config.PublishedAt.IsZero() {
		config.PublishedAt = time.Now().UTC()
	}
	return config
}

func normalizeReported(config ReportedConfig) ReportedConfig {
	config.ModuleID = strings.TrimSpace(config.ModuleID)
	config.OpenAPIURL = strings.TrimSpace(config.OpenAPIURL)
	config.SwaggerUIURL = strings.TrimSpace(config.SwaggerUIURL)
	config.ConfigurationSource = strings.TrimSpace(config.ConfigurationSource)
	config.Settings = normalizeSettings(config.Settings)
	if config.ReportedAt.IsZero() {
		config.ReportedAt = time.Now().UTC()
	}
	return config
}

func normalizeSettings(settings map[string]string) map[string]string {
	if len(settings) == 0 {
		return nil
	}

	normalized := make(map[string]string, len(settings))
	for rawKey, rawValue := range settings {
		key := strings.TrimSpace(rawKey)
		value := strings.TrimSpace(rawValue)
		if key == "" || value == "" {
			continue
		}
		normalized[key] = value
	}
	if len(normalized) == 0 {
		return nil
	}
	return normalized
}
