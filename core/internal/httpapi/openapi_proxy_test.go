package httpapi

import (
	"context"
	"errors"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"strings"
	"sync/atomic"
	"testing"
	"time"

	"github.com/exiledcms/platform-core/internal/config"
	"github.com/exiledcms/platform-core/internal/logging"
	"github.com/exiledcms/platform-core/internal/moduleconfig"
	"github.com/exiledcms/platform-core/internal/registry"
)

// stubFetcher lets tests drive the proxy without hitting the network. The call
// counter proves the TTL cache actually skips repeated upstream calls.
type stubFetcher struct {
	calls atomic.Int32
	body  []byte
	ctype string
	err   error
}

func (f *stubFetcher) Fetch(_ context.Context, _ string) ([]byte, string, error) {
	f.calls.Add(1)
	if f.err != nil {
		return nil, "", f.err
	}
	return f.body, f.ctype, nil
}

func newProxyTestHandler(t *testing.T, fetcher OpenAPIFetcher) (http.Handler, *registry.MemoryStore) {
	t.Helper()

	cfg := config.Config{ServiceName: "platform-core", Environment: "test", HTTPPort: 8080}
	logger := slog.New(slog.NewTextHandler(io.Discard, nil))
	store := registry.NewMemoryStore()
	configs, err := moduleconfig.NewStore("")
	if err != nil {
		t.Fatalf("expected module config store initialization to succeed, got %v", err)
	}
	logs, err := logging.NewRouter(logging.RouterOptions{
		ServiceName:      cfg.ServiceName,
		Environment:      cfg.Environment,
		BufferMaxEntries: 16,
	})
	if err != nil {
		t.Fatalf("expected router initialization to succeed, got %v", err)
	}
	return NewServerWithFetcher(logger, cfg, store, logs, configs, fetcher), store
}

func TestOpenAPIProxyReturnsUpstreamDocument(t *testing.T) {
	fetcher := &stubFetcher{body: []byte(`{"openapi":"3.0.0","info":{"title":"Tickets"}}`), ctype: "application/json"}
	handler, store := newProxyTestHandler(t, fetcher)

	if err := store.RegisterModule(registry.ModuleRegistration{
		ID: "tickets-service", Name: "Tickets", Version: "1.0.0", Kind: "service",
		OpenAPIURL: "http://tickets-service:8080/swagger/v1/swagger.json",
	}); err != nil {
		t.Fatalf("expected module registration to succeed, got %v", err)
	}

	req := httptest.NewRequest(http.MethodGet, "/api/v1/platform/openapi/modules/tickets-service.json", nil)
	res := httptest.NewRecorder()
	handler.ServeHTTP(res, req)

	if res.Code != http.StatusOK {
		t.Fatalf("expected status 200, got %d (body=%s)", res.Code, res.Body.String())
	}
	if got := res.Header().Get("Content-Type"); !strings.Contains(got, "json") {
		t.Fatalf("expected JSON content type, got %q", got)
	}
	if !strings.Contains(res.Body.String(), "\"Tickets\"") {
		t.Fatalf("expected upstream body to be returned verbatim, got %s", res.Body.String())
	}
}

func TestOpenAPIProxyCachesResponses(t *testing.T) {
	fetcher := &stubFetcher{body: []byte(`{"openapi":"3.0.0"}`), ctype: "application/json"}
	handler, store := newProxyTestHandler(t, fetcher)

	if err := store.RegisterModule(registry.ModuleRegistration{
		ID: "tickets-service", Name: "Tickets", Version: "1.0.0", Kind: "service",
		OpenAPIURL: "http://tickets-service:8080/swagger/v1/swagger.json",
	}); err != nil {
		t.Fatalf("expected module registration to succeed, got %v", err)
	}

	for i := 0; i < 3; i++ {
		req := httptest.NewRequest(http.MethodGet, "/api/v1/platform/openapi/modules/tickets-service.json", nil)
		res := httptest.NewRecorder()
		handler.ServeHTTP(res, req)
		if res.Code != http.StatusOK {
			t.Fatalf("iteration %d: expected 200, got %d", i, res.Code)
		}
	}

	// Three HTTP calls should collapse into a single upstream fetch while the
	// cache entry is still fresh.
	if got := fetcher.calls.Load(); got != 1 {
		t.Fatalf("expected a single upstream fetch thanks to caching, got %d", got)
	}
}

func TestOpenAPIProxyFailsWhenUpstreamUnreachable(t *testing.T) {
	fetcher := &stubFetcher{err: errors.New("connection refused")}
	handler, store := newProxyTestHandler(t, fetcher)

	if err := store.RegisterModule(registry.ModuleRegistration{
		ID: "tickets-service", Name: "Tickets", Version: "1.0.0", Kind: "service",
		OpenAPIURL: "http://tickets-service:8080/swagger/v1/swagger.json",
	}); err != nil {
		t.Fatalf("expected module registration to succeed, got %v", err)
	}

	req := httptest.NewRequest(http.MethodGet, "/api/v1/platform/openapi/modules/tickets-service.json", nil)
	res := httptest.NewRecorder()
	handler.ServeHTTP(res, req)

	if res.Code != http.StatusBadGateway {
		t.Fatalf("expected status 502 for unreachable module, got %d", res.Code)
	}
}

func TestOpenAPIProxyReturns404ForUnknownModule(t *testing.T) {
	handler, _ := newProxyTestHandler(t, &stubFetcher{})

	req := httptest.NewRequest(http.MethodGet, "/api/v1/platform/openapi/modules/does-not-exist.json", nil)
	res := httptest.NewRecorder()
	handler.ServeHTTP(res, req)

	if res.Code != http.StatusNotFound {
		t.Fatalf("expected 404 for unknown module, got %d", res.Code)
	}
}

func TestOpenAPIProxyReturns404WhenModuleHasNoOpenAPIURL(t *testing.T) {
	handler, store := newProxyTestHandler(t, &stubFetcher{})
	if err := store.RegisterModule(registry.ModuleRegistration{
		ID: "themes-service", Name: "Themes", Version: "1.0.0", Kind: "service",
	}); err != nil {
		t.Fatalf("expected module registration to succeed, got %v", err)
	}

	req := httptest.NewRequest(http.MethodGet, "/api/v1/platform/openapi/modules/themes-service.json", nil)
	res := httptest.NewRecorder()
	handler.ServeHTTP(res, req)

	if res.Code != http.StatusNotFound {
		t.Fatalf("expected 404 when module has no OpenAPI URL, got %d", res.Code)
	}
}

func TestOpenAPIProxyRejectsNonJSONUpstream(t *testing.T) {
	fetcher := &stubFetcher{body: []byte("<html>nope</html>"), ctype: "text/html"}
	handler, store := newProxyTestHandler(t, fetcher)
	if err := store.RegisterModule(registry.ModuleRegistration{
		ID: "tickets-service", Name: "Tickets", Version: "1.0.0", Kind: "service",
		OpenAPIURL: "http://tickets-service:8080/",
	}); err != nil {
		t.Fatalf("expected module registration to succeed, got %v", err)
	}

	req := httptest.NewRequest(http.MethodGet, "/api/v1/platform/openapi/modules/tickets-service.json", nil)
	res := httptest.NewRecorder()
	handler.ServeHTTP(res, req)

	if res.Code != http.StatusBadGateway {
		t.Fatalf("expected 502 when upstream returns non-JSON, got %d", res.Code)
	}
}

// End-to-end smoke test that the HTTP fetcher actually performs a real request
// against a local server. This guards the production code path rather than the
// stub used elsewhere.
func TestHTTPOpenAPIFetcherFetchesFromHTTPServer(t *testing.T) {
	upstream := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		w.Header().Set("Content-Type", "application/json")
		_, _ = w.Write([]byte(`{"openapi":"3.0.0"}`))
	}))
	t.Cleanup(upstream.Close)

	fetcher := newHTTPOpenAPIFetcher(500 * time.Millisecond)
	body, ctype, err := fetcher.Fetch(context.Background(), upstream.URL)
	if err != nil {
		t.Fatalf("expected fetch to succeed, got %v", err)
	}
	if !strings.Contains(ctype, "json") {
		t.Fatalf("expected JSON content type, got %q", ctype)
	}
	if !strings.Contains(string(body), "openapi") {
		t.Fatalf("expected body to be forwarded, got %s", body)
	}
}
