package httpapi

import (
	"encoding/json"
	"errors"
	"fmt"
	"log/slog"
	"net/http"
	"strconv"
	"strings"
	"time"

	"github.com/exiledcms/platform-core/internal/config"
	"github.com/exiledcms/platform-core/internal/logging"
	"github.com/exiledcms/platform-core/internal/moduleconfig"
	"github.com/exiledcms/platform-core/internal/registry"
	"github.com/exiledcms/platform-core/internal/version"
)

type Server struct {
	logger        *slog.Logger
	config        config.Config
	store         *registry.MemoryStore
	logs          *logging.Router
	moduleConfigs *moduleconfig.Store
	openAPIProxy  *openAPIProxy
}

type themeActivationRequest struct {
	ID string `json:"id"`
}

type moduleDocumentationItem struct {
	ID            string                       `json:"id"`
	Name          string                       `json:"name"`
	Version       string                       `json:"version"`
	Kind          string                       `json:"kind"`
	BaseURL       string                       `json:"baseUrl,omitempty"`
	HealthURL     string                       `json:"healthUrl,omitempty"`
	Topology      *registry.ModuleTopology     `json:"topology,omitempty"`
	Documentation []registry.DocumentationLink `json:"documentation,omitempty"`
}

func NewServer(logger *slog.Logger, cfg config.Config, store *registry.MemoryStore, logs *logging.Router, moduleConfigs *moduleconfig.Store) http.Handler {
	return NewServerWithFetcher(logger, cfg, store, logs, moduleConfigs, nil)
}

// NewServerWithFetcher is the test-friendly constructor: it accepts a custom
// OpenAPI fetcher so suites can drive the aggregation proxy without hitting real
// upstream modules. Production callers should use NewServer.
func NewServerWithFetcher(logger *slog.Logger, cfg config.Config, store *registry.MemoryStore, logs *logging.Router, moduleConfigs *moduleconfig.Store, fetcher OpenAPIFetcher) http.Handler {
	server := &Server{
		logger:        logger,
		config:        cfg,
		store:         store,
		logs:          logs,
		moduleConfigs: moduleConfigs,
		openAPIProxy:  newOpenAPIProxy(store, fetcher, 30*time.Second),
	}

	mux := http.NewServeMux()
	mux.HandleFunc("GET /", server.handleRoot)
	mux.HandleFunc("GET /healthz", server.handleHealth)
	mux.HandleFunc("GET /readyz", server.handleReady)
	mux.HandleFunc("GET /api/v1/platform/info", server.handleInfo)
	mux.HandleFunc("GET /api/v1/platform/capabilities", server.handleCapabilities)
	mux.HandleFunc("GET /api/v1/platform/registry", server.handleRegistry)
	mux.HandleFunc("GET /api/v1/platform/plugins", server.handleListPlugins)
	mux.HandleFunc("POST /api/v1/platform/plugins", server.handleRegisterPlugin)
	mux.HandleFunc("GET /api/v1/platform/themes", server.handleListThemes)
	mux.HandleFunc("POST /api/v1/platform/themes", server.handleRegisterTheme)
	mux.HandleFunc("POST /api/v1/platform/themes/activate", server.handleActivateTheme)
	mux.HandleFunc("GET /api/v1/platform/modules", server.handleListModules)
	mux.HandleFunc("GET /api/v1/platform/modules/docs", server.handleListModuleDocumentation)
	mux.HandleFunc("POST /api/v1/platform/modules", server.handleRegisterModule)
	mux.HandleFunc("GET /api/v1/platform/module-config", server.handleListModuleConfigs)
	mux.HandleFunc("GET /api/v1/platform/module-config/{id}", server.handleGetModuleConfig)
	mux.HandleFunc("GET /api/v1/platform/logs", server.handleListLogs)
	mux.HandleFunc("POST /api/v1/platform/logs", server.handleIngestLogs)
	mux.HandleFunc("GET /api/v1/platform/permissions", server.handleListPermissions)
	mux.HandleFunc("POST /api/v1/platform/permissions", server.handleRegisterPermission)
	mux.HandleFunc("GET /api/v1/platform/openapi/core.json", server.handleCoreOpenAPIDocument)
	mux.HandleFunc("GET /api/v1/platform/openapi/documents", server.handleListOpenAPIDocuments)
	// The module proxy mounts under the same prefix so clients only need to know
	// one base URL to reach any document in the platform.
	mux.HandleFunc("GET /api/v1/platform/openapi/modules/{id}", server.openAPIProxy.handleProxyModuleDocument)
	mux.HandleFunc("GET /swagger", server.handleSwaggerHub)

	return server.withRequestLogging(mux)
}

func (s *Server) handleRoot(w http.ResponseWriter, r *http.Request) {
	writeJSON(w, http.StatusOK, map[string]any{
		"service":     s.config.ServiceName,
		"environment": s.config.Environment,
		"status":      "running",
	})
}

func (s *Server) handleHealth(w http.ResponseWriter, r *http.Request) {
	writeJSON(w, http.StatusOK, map[string]any{
		"status": "ok",
		"time":   time.Now().UTC(),
	})
}

func (s *Server) handleReady(w http.ResponseWriter, r *http.Request) {
	writeJSON(w, http.StatusOK, map[string]any{
		"status": "ready",
		"infra":  s.config.InfraStatus(),
	})
}

func (s *Server) handleInfo(w http.ResponseWriter, r *http.Request) {
	writeJSON(w, http.StatusOK, map[string]any{
		"service": map[string]any{
			"name":        s.config.ServiceName,
			"environment": s.config.Environment,
			"version":     version.BuildVersion,
			"commit":      version.BuildCommit,
			"buildTime":   version.BuildTime,
		},
	})
}

func (s *Server) handleCapabilities(w http.ResponseWriter, r *http.Request) {
	writeJSON(w, http.StatusOK, map[string]any{
		"items": registry.KnownCapabilities(),
	})
}

func (s *Server) handleRegistry(w http.ResponseWriter, r *http.Request) {
	writeJSON(w, http.StatusOK, s.store.Snapshot())
}

func (s *Server) handleListPlugins(w http.ResponseWriter, r *http.Request) {
	writeJSON(w, http.StatusOK, map[string]any{
		"items": s.store.ListPlugins(),
	})
}

func (s *Server) handleRegisterPlugin(w http.ResponseWriter, r *http.Request) {
	var manifest registry.PluginManifest
	if err := decodeJSON(r, &manifest); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}

	if err := s.store.RegisterPlugin(manifest); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}

	writeJSON(w, http.StatusCreated, map[string]any{
		"item": manifest,
	})
}

func (s *Server) handleListThemes(w http.ResponseWriter, r *http.Request) {
	writeJSON(w, http.StatusOK, map[string]any{
		"activeThemeId": s.store.ActiveThemeID(),
		"items":         s.store.ListThemes(),
	})
}

func (s *Server) handleRegisterTheme(w http.ResponseWriter, r *http.Request) {
	var manifest registry.ThemeManifest
	if err := decodeJSON(r, &manifest); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}

	if err := s.store.RegisterTheme(manifest); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}

	writeJSON(w, http.StatusCreated, map[string]any{
		"item": manifest,
	})
}

func (s *Server) handleActivateTheme(w http.ResponseWriter, r *http.Request) {
	var request themeActivationRequest
	if err := decodeJSON(r, &request); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}

	if err := s.store.ActivateTheme(request.ID); err != nil {
		writeError(w, http.StatusNotFound, err)
		return
	}

	writeJSON(w, http.StatusOK, map[string]any{
		"activeThemeId": request.ID,
	})
}

func (s *Server) handleListModules(w http.ResponseWriter, r *http.Request) {
	writeJSON(w, http.StatusOK, map[string]any{
		"items": s.store.ListModules(),
	})
}

func (s *Server) handleListModuleDocumentation(w http.ResponseWriter, r *http.Request) {
	kind := strings.TrimSpace(r.URL.Query().Get("kind"))
	modules := s.store.ListModules()
	items := make([]moduleDocumentationItem, 0, len(modules))
	for _, module := range modules {
		documentation := module.Documentation
		if kind != "" {
			documentation = filterDocumentationLinks(documentation, kind)
			if len(documentation) == 0 {
				continue
			}
		}

		items = append(items, moduleDocumentationItem{
			ID:            module.ID,
			Name:          module.Name,
			Version:       module.Version,
			Kind:          module.Kind,
			BaseURL:       module.BaseURL,
			HealthURL:     module.HealthURL,
			Topology:      module.Topology,
			Documentation: documentation,
		})
	}

	writeJSON(w, http.StatusOK, map[string]any{
		"kind":  kind,
		"items": items,
	})
}

func (s *Server) handleRegisterModule(w http.ResponseWriter, r *http.Request) {
	var module registry.ModuleRegistration
	if err := decodeJSON(r, &module); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}

	if err := s.store.RegisterModule(module); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}

	writeJSON(w, http.StatusCreated, map[string]any{
		"item": module,
	})
}

func (s *Server) handleListModuleConfigs(w http.ResponseWriter, r *http.Request) {
	if s.moduleConfigs == nil {
		writeJSON(w, http.StatusOK, moduleconfig.Snapshot{GeneratedAt: time.Now().UTC(), Items: nil})
		return
	}

	writeJSON(w, http.StatusOK, s.moduleConfigs.Snapshot())
}

func (s *Server) handleGetModuleConfig(w http.ResponseWriter, r *http.Request) {
	moduleID := strings.TrimSpace(r.PathValue("id"))
	if moduleID == "" {
		writeError(w, http.StatusBadRequest, errors.New("module id is required"))
		return
	}

	if s.moduleConfigs == nil {
		writeError(w, http.StatusNotFound, errors.New("module configuration was not found"))
		return
	}

	view := moduleconfig.ModuleConfigView{ModuleID: moduleID}
	if desired, ok := s.moduleConfigs.DesiredFor(moduleID); ok {
		copy := desired
		view.Desired = &copy
	}
	if reported, ok := s.moduleConfigs.ReportedFor(moduleID); ok {
		copy := reported
		view.Reported = &copy
	}

	if view.Desired == nil && view.Reported == nil {
		writeError(w, http.StatusNotFound, errors.New("module configuration was not found"))
		return
	}

	writeJSON(w, http.StatusOK, view)
}

func (s *Server) handleListLogs(w http.ResponseWriter, r *http.Request) {
	if s.logs == nil || s.logs.Store() == nil {
		writeError(w, http.StatusServiceUnavailable, errors.New("central log router is not available"))
		return
	}

	items := s.logs.Store().List(logging.ListOptions{
		Limit:        parsePositiveInt(r.URL.Query().Get("limit"), 100, 500),
		Source:       r.URL.Query().Get("source"),
		ModuleID:     r.URL.Query().Get("moduleId"),
		MinimumLevel: r.URL.Query().Get("minimumLevel"),
	})

	writeJSON(w, http.StatusOK, map[string]any{
		"items":    items,
		"retained": s.logs.Store().Count(),
		"capacity": s.logs.Store().Capacity(),
		"sentry": map[string]any{
			"enabled":      s.logs.SentryEnabled(),
			"minimumLevel": s.logs.SentryMinLevel(),
		},
	})
}

func (s *Server) handleIngestLogs(w http.ResponseWriter, r *http.Request) {
	if s.logs == nil {
		writeError(w, http.StatusServiceUnavailable, errors.New("central log router is not available"))
		return
	}

	var request logging.IngestRequest
	if err := decodeJSON(r, &request); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}

	if len(request.Entries) == 0 {
		writeError(w, http.StatusBadRequest, errors.New("at least one log entry is required"))
		return
	}

	defaultModuleID := strings.TrimSpace(r.Header.Get("X-Module-Id"))
	defaultService := strings.TrimSpace(r.Header.Get("X-Module-Service"))
	items := make([]logging.Entry, 0, len(request.Entries))

	for index, entry := range request.Entries {
		if strings.TrimSpace(entry.ModuleID) == "" {
			entry.ModuleID = defaultModuleID
		}

		if strings.TrimSpace(entry.Service) == "" {
			entry.Service = defaultService
		}

		normalized, err := s.logs.RecordModuleLog(entry)
		if err != nil {
			writeError(w, http.StatusBadRequest, fmt.Errorf("invalid log entry at index %d: %w", index, err))
			return
		}

		items = append(items, normalized)
	}

	writeJSON(w, http.StatusAccepted, map[string]any{
		"accepted": len(items),
		"items":    items,
	})
}

func (s *Server) handleListPermissions(w http.ResponseWriter, r *http.Request) {
	writeJSON(w, http.StatusOK, map[string]any{
		"items": s.store.ListPermissions(),
	})
}

func (s *Server) handleRegisterPermission(w http.ResponseWriter, r *http.Request) {
	var permission registry.PermissionDefinition
	if err := decodeJSON(r, &permission); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}

	if err := s.store.RegisterPermission(permission); err != nil {
		writeError(w, http.StatusBadRequest, err)
		return
	}

	writeJSON(w, http.StatusCreated, map[string]any{
		"item": permission,
	})
}

func (s *Server) withRequestLogging(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		startedAt := time.Now()
		next.ServeHTTP(w, r)
		s.logger.Info(
			"http request served",
			"method", r.Method,
			"path", r.URL.Path,
			"durationMs", time.Since(startedAt).Milliseconds(),
		)
	})
}

func decodeJSON(r *http.Request, destination any) error {
	if r.Body == nil {
		return errors.New("request body is required")
	}

	decoder := json.NewDecoder(r.Body)
	decoder.DisallowUnknownFields()

	if err := decoder.Decode(destination); err != nil {
		return err
	}

	return nil
}

func writeError(w http.ResponseWriter, statusCode int, err error) {
	writeJSON(w, statusCode, map[string]any{
		"error": err.Error(),
	})
}

func writeJSON(w http.ResponseWriter, statusCode int, payload any) {
	w.Header().Set("Content-Type", "application/json; charset=utf-8")
	w.WriteHeader(statusCode)

	_ = json.NewEncoder(w).Encode(payload)
}

func parsePositiveInt(raw string, fallback int, max int) int {
	trimmed := strings.TrimSpace(raw)
	if trimmed == "" {
		return fallback
	}

	parsed, err := strconv.Atoi(trimmed)
	if err != nil || parsed <= 0 {
		return fallback
	}

	if max > 0 && parsed > max {
		return max
	}

	return parsed
}

func filterDocumentationLinks(values []registry.DocumentationLink, kind string) []registry.DocumentationLink {
	if len(values) == 0 {
		return nil
	}

	kind = strings.TrimSpace(kind)
	if kind == "" {
		return values
	}

	filtered := make([]registry.DocumentationLink, 0, len(values))
	for _, value := range values {
		if strings.EqualFold(value.Key, kind) {
			filtered = append(filtered, value)
		}
	}

	if len(filtered) == 0 {
		return nil
	}

	return filtered
}
