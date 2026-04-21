package httpapi

import (
	"bytes"
	"encoding/json"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"

	"github.com/exiledcms/platform-core/internal/config"
	"github.com/exiledcms/platform-core/internal/logging"
	"github.com/exiledcms/platform-core/internal/moduleconfig"
	"github.com/exiledcms/platform-core/internal/registry"
)

func TestListModuleDocumentationFiltersByKind(t *testing.T) {
	handler, store, _, _ := newTestHandler(t, config.Config{ServiceName: "platform-core", Environment: "test", HTTPPort: 8080})

	err := store.RegisterModule(registry.ModuleRegistration{
		ID:      "tickets-service",
		Name:    "Tickets Service",
		Version: "1.0.0",
		Kind:    "service",
		Documentation: []registry.DocumentationLink{
			{Key: "development", Title: "Dev", Href: "contracts/modules/development.md"},
			{Key: "sentry", Title: "Sentry", Href: "contracts/modules/observability.md#recommended-sentry-topology"},
		},
	})
	if err != nil {
		t.Fatalf("expected module registration to succeed, got %v", err)
	}

	req := httptest.NewRequest(http.MethodGet, "/api/v1/platform/modules/docs?kind=sentry", nil)
	res := httptest.NewRecorder()
	handler.ServeHTTP(res, req)

	if res.Code != http.StatusOK {
		t.Fatalf("expected status 200, got %d", res.Code)
	}

	var payload struct {
		Kind  string                    `json:"kind"`
		Items []moduleDocumentationItem `json:"items"`
	}
	decodeResponse(t, res, &payload)

	if payload.Kind != "sentry" {
		t.Fatalf("expected filtered kind to be echoed, got %q", payload.Kind)
	}

	if len(payload.Items) != 1 {
		t.Fatalf("expected one module with sentry docs, got %d", len(payload.Items))
	}

	if len(payload.Items[0].Documentation) != 1 || payload.Items[0].Documentation[0].Key != "sentry" {
		t.Fatalf("expected sentry docs to be filtered, got %#v", payload.Items[0].Documentation)
	}
}

func TestIngestLogsAndListLogsRoundTrip(t *testing.T) {
	handler, _, logs, _ := newTestHandler(t, config.Config{ServiceName: "platform-core", Environment: "test", HTTPPort: 8080})

	body := bytes.NewBufferString(`{"entries":[{"level":"warning","message":" queue retry "}]}`)
	req := httptest.NewRequest(http.MethodPost, "/api/v1/platform/logs", body)
	req.Header.Set("Content-Type", "application/json")
	req.Header.Set("X-Module-Id", "tickets-service")
	req.Header.Set("X-Module-Service", "Tickets Service")
	res := httptest.NewRecorder()
	handler.ServeHTTP(res, req)

	if res.Code != http.StatusAccepted {
		t.Fatalf("expected status 202, got %d", res.Code)
	}

	var ingest struct {
		Accepted int             `json:"accepted"`
		Items    []logging.Entry `json:"items"`
	}
	decodeResponse(t, res, &ingest)

	if ingest.Accepted != 1 || len(ingest.Items) != 1 {
		t.Fatalf("expected one accepted log entry, got accepted=%d items=%d", ingest.Accepted, len(ingest.Items))
	}

	if logs.Store().Count() != 1 {
		t.Fatalf("expected router store to retain one log entry, got %d", logs.Store().Count())
	}

	listReq := httptest.NewRequest(http.MethodGet, "/api/v1/platform/logs?moduleId=tickets-service&minimumLevel=warn", nil)
	listRes := httptest.NewRecorder()
	handler.ServeHTTP(listRes, listReq)

	if listRes.Code != http.StatusOK {
		t.Fatalf("expected status 200, got %d", listRes.Code)
	}

	var listed struct {
		Items    []logging.Entry `json:"items"`
		Retained int             `json:"retained"`
		Capacity int             `json:"capacity"`
		Sentry   struct {
			Enabled      bool   `json:"enabled"`
			MinimumLevel string `json:"minimumLevel"`
		} `json:"sentry"`
	}
	decodeResponse(t, listRes, &listed)

	if len(listed.Items) != 1 {
		t.Fatalf("expected one listed log entry, got %d", len(listed.Items))
	}

	item := listed.Items[0]
	if item.ModuleID != "tickets-service" || item.Service != "Tickets Service" || item.Level != "warn" {
		t.Fatalf("expected normalized module log metadata, got %#v", item)
	}

	if item.Message != "queue retry" {
		t.Fatalf("expected message to be trimmed, got %q", item.Message)
	}

	if listed.Retained != 1 || listed.Capacity != logs.Store().Capacity() {
		t.Fatalf("expected retention metadata to match store, got retained=%d capacity=%d", listed.Retained, listed.Capacity)
	}

	if listed.Sentry.Enabled {
		t.Fatalf("expected sentry to be disabled in test router")
	}
}

func TestReadyEndpointReportsInfraStatus(t *testing.T) {
	handler, _, _, _ := newTestHandler(t, config.Config{
		ServiceName:    "platform-core",
		Environment:    "test",
		HTTPPort:       8080,
		MySQLDSN:       "mysql-dsn",
		RedisAddr:      "redis:6379",
		NATSURL:        "nats://localhost:4222",
		SentryDSN:      "https://example@sentry.invalid/1",
		SentryMinLevel: "error",
	})

	req := httptest.NewRequest(http.MethodGet, "/readyz", nil)
	res := httptest.NewRecorder()
	handler.ServeHTTP(res, req)

	if res.Code != http.StatusOK {
		t.Fatalf("expected status 200, got %d", res.Code)
	}

	var payload struct {
		Status string            `json:"status"`
		Infra  map[string]string `json:"infra"`
	}
	decodeResponse(t, res, &payload)

	if payload.Status != "ready" {
		t.Fatalf("expected ready status, got %q", payload.Status)
	}

	if payload.Infra["sentry"] != "configured" {
		t.Fatalf("expected sentry infra to be configured, got %#v", payload.Infra)
	}
}

func TestModuleConfigEndpointReturnsDesiredAndReportedSnapshots(t *testing.T) {
	handler, _, _, configs := newTestHandler(t, config.Config{ServiceName: "platform-core", Environment: "test", HTTPPort: 8080})

	if err := configs.SetDesired(moduleconfig.DesiredConfig{
		ModuleID:                 "tickets-service",
		DatabaseConnectionString: "Server=mysql;Database=exiledcms_tickets;User Id=tickets;Password=super-secret;",
		OpenAPIURL:               "http://tickets-service:8080/swagger/v1/swagger.json",
		Settings: map[string]string{
			"auth.jwt.secret": "do-not-leak",
			"feature_flag":    "enabled",
		},
	}); err != nil {
		t.Fatalf("expected desired config to be stored, got %v", err)
	}

	if err := configs.SetReported(moduleconfig.ReportedConfig{
		ModuleID:            "tickets-service",
		DatabaseConfigured:  true,
		ConfigurationSource: "nats",
	}); err != nil {
		t.Fatalf("expected reported config to be stored, got %v", err)
	}

	req := httptest.NewRequest(http.MethodGet, "/api/v1/platform/module-config/tickets-service", nil)
	res := httptest.NewRecorder()
	handler.ServeHTTP(res, req)

	if res.Code != http.StatusOK {
		t.Fatalf("expected status 200, got %d", res.Code)
	}

	var payload moduleconfig.ModuleConfigView
	decodeResponse(t, res, &payload)
	if payload.Desired == nil || payload.Reported == nil {
		t.Fatalf("expected desired and reported config, got %#v", payload)
	}

	if payload.Desired.DatabaseConnectionString != "Server=mysql;Database=exiledcms_tickets;User Id=tickets;Password=[redacted];" {
		t.Fatalf("expected config payload to redact secrets in database data, got %#v", payload)
	}

	if payload.Desired.Settings["auth.jwt.secret"] != "[redacted]" || payload.Desired.Settings["feature_flag"] != "enabled" {
		t.Fatalf("expected settings to redact secrets while preserving safe values, got %#v", payload)
	}

	if !payload.Reported.DatabaseConfigured {
		t.Fatalf("expected config payload to expose database data and readiness, got %#v", payload)
	}
}

func TestOpenAPIDocumentsIncludeRegisteredModuleMetadata(t *testing.T) {
	handler, store, _, _ := newTestHandler(t, config.Config{ServiceName: "platform-core", Environment: "test", HTTPPort: 8080})

	err := store.RegisterModule(registry.ModuleRegistration{
		ID:                   "tickets-service",
		Name:                 "Tickets Service",
		Version:              "1.0.0",
		Kind:                 "service",
		OpenAPIURL:           "http://tickets-service:8080/swagger/v1/swagger.json",
		SwaggerUIURL:         "http://tickets-service:8080/swagger",
		ConfigDesiredSubject: "platform.config.desired.tickets-service",
	})
	if err != nil {
		t.Fatalf("expected module registration to succeed, got %v", err)
	}

	req := httptest.NewRequest(http.MethodGet, "/api/v1/platform/openapi/documents", nil)
	res := httptest.NewRecorder()
	handler.ServeHTTP(res, req)

	if res.Code != http.StatusOK {
		t.Fatalf("expected status 200, got %d", res.Code)
	}

	var payload struct {
		Items []openAPIDocumentItem `json:"items"`
	}
	decodeResponse(t, res, &payload)

	if len(payload.Items) < 2 {
		t.Fatalf("expected centralized openapi index to include core and module docs, got %#v", payload.Items)
	}

	foundTickets := false
	for _, item := range payload.Items {
		if item.ID == "tickets-service" && item.OpenAPIURL == "http://tickets-service:8080/swagger/v1/swagger.json" {
			foundTickets = true
			break
		}
	}

	if !foundTickets {
		t.Fatalf("expected tickets-service openapi metadata in central index, got %#v", payload.Items)
	}

	hubReq := httptest.NewRequest(http.MethodGet, "/swagger", nil)
	hubRes := httptest.NewRecorder()
	handler.ServeHTTP(hubRes, hubReq)

	if hubRes.Code != http.StatusOK {
		t.Fatalf("expected swagger hub status 200, got %d", hubRes.Code)
	}

	// The hub is now a real Swagger UI page — module discovery happens client-side
	// through /api/v1/platform/openapi/documents, so we just assert the UI loads.
	body := hubRes.Body.String()
	if !strings.Contains(body, "swagger-ui") || !strings.Contains(body, "/api/v1/platform/openapi/documents") {
		t.Fatalf("expected swagger hub html to embed swagger-ui and link the document index, got %s", body)
	}

	// The JSON index must also expose the same-origin aggregated URL so the hub
	// can render module docs without exposing private module addresses to browsers.
	foundAggregated := false
	for _, item := range payload.Items {
		if item.ID == "tickets-service" && item.AggregatedURL == "/api/v1/platform/openapi/modules/tickets-service.json" {
			foundAggregated = true
			break
		}
	}
	if !foundAggregated {
		t.Fatalf("expected tickets-service to expose an aggregated proxy URL, got %#v", payload.Items)
	}
}

func newTestHandler(t *testing.T, cfg config.Config) (http.Handler, *registry.MemoryStore, *logging.Router, *moduleconfig.Store) {
	t.Helper()

	logger := slog.New(slog.NewTextHandler(io.Discard, nil))
	store := registry.NewMemoryStore()
	configs, err := moduleconfig.NewStore(cfg.ModuleConfigJSON)
	if err != nil {
		t.Fatalf("expected module config store initialization to succeed, got %v", err)
	}
	logs, err := logging.NewRouter(logging.RouterOptions{
		ServiceName:      cfg.ServiceName,
		Environment:      cfg.Environment,
		SentryMinLevel:   cfg.SentryMinLevel,
		BufferMaxEntries: 32,
	})
	if err != nil {
		t.Fatalf("expected router initialization to succeed, got %v", err)
	}

	return NewServer(logger, cfg, store, logs, configs), store, logs, configs
}

func decodeResponse(t *testing.T, recorder *httptest.ResponseRecorder, destination any) {
	t.Helper()

	if err := json.Unmarshal(recorder.Body.Bytes(), destination); err != nil {
		t.Fatalf("expected valid json response, got %v with body %s", err, recorder.Body.String())
	}
}
