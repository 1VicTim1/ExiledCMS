package httpapi

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net/http"
	"strings"
	"sync"
	"time"

	"github.com/exiledcms/platform-core/internal/registry"
)

// OpenAPIFetcher fetches an OpenAPI document from an upstream URL.
// Kept as an interface so tests can inject a stub and avoid real HTTP calls.
type OpenAPIFetcher interface {
	Fetch(ctx context.Context, url string) ([]byte, string, error)
}

// httpOpenAPIFetcher is the production fetcher used by platform-core.
// Timeout is intentionally short: modules live on the same control-plane network
// and an unresponsive module should fail fast so the hub stays usable.
type httpOpenAPIFetcher struct {
	client *http.Client
}

func newHTTPOpenAPIFetcher(timeout time.Duration) *httpOpenAPIFetcher {
	return &httpOpenAPIFetcher{
		client: &http.Client{Timeout: timeout},
	}
}

func (f *httpOpenAPIFetcher) Fetch(ctx context.Context, url string) ([]byte, string, error) {
	req, err := http.NewRequestWithContext(ctx, http.MethodGet, url, nil)
	if err != nil {
		return nil, "", err
	}
	req.Header.Set("Accept", "application/json")

	res, err := f.client.Do(req)
	if err != nil {
		return nil, "", err
	}
	defer res.Body.Close()

	if res.StatusCode < 200 || res.StatusCode >= 300 {
		return nil, "", fmt.Errorf("upstream returned status %d", res.StatusCode)
	}

	body, err := io.ReadAll(io.LimitReader(res.Body, 8*1024*1024))
	if err != nil {
		return nil, "", err
	}

	contentType := res.Header.Get("Content-Type")
	if contentType == "" {
		contentType = "application/json"
	}
	return body, contentType, nil
}

// openAPICacheEntry retains a fetched document alongside its freshness deadline.
// A single entry weighs at most a few MB so a trivial map is enough.
type openAPICacheEntry struct {
	body        []byte
	contentType string
	expiresAt   time.Time
}

// openAPIProxy aggregates module OpenAPI documents through platform-core.
// Modules often live on private control-plane networks the browser cannot reach;
// proxying fixes that and gives the hub a single same-origin entry point.
type openAPIProxy struct {
	store   *registry.MemoryStore
	fetcher OpenAPIFetcher
	ttl     time.Duration

	mu    sync.Mutex
	cache map[string]openAPICacheEntry
	clock func() time.Time
}

func newOpenAPIProxy(store *registry.MemoryStore, fetcher OpenAPIFetcher, ttl time.Duration) *openAPIProxy {
	if fetcher == nil {
		fetcher = newHTTPOpenAPIFetcher(5 * time.Second)
	}
	if ttl <= 0 {
		ttl = 30 * time.Second
	}
	return &openAPIProxy{
		store:   store,
		fetcher: fetcher,
		ttl:     ttl,
		cache:   make(map[string]openAPICacheEntry),
		clock:   time.Now,
	}
}

// handleProxyModuleDocument serves /api/v1/platform/openapi/modules/{id}.json.
// It returns:
//   - 404 when the module is unknown or exposes no OpenAPI URL
//   - 502 when the upstream is unreachable or responds with a non-2xx status
//   - 200 with the upstream JSON body otherwise (short-TTL cached)
func (p *openAPIProxy) handleProxyModuleDocument(w http.ResponseWriter, r *http.Request) {
	rawID := strings.TrimSpace(r.PathValue("id"))
	// The mux pattern captures `.json`, but operators may also request the bare id.
	moduleID := strings.TrimSuffix(rawID, ".json")
	if moduleID == "" {
		writeError(w, http.StatusBadRequest, errors.New("module id is required"))
		return
	}

	module, ok := p.lookupModule(moduleID)
	if !ok {
		writeError(w, http.StatusNotFound, fmt.Errorf("module %q is not registered", moduleID))
		return
	}

	if strings.TrimSpace(module.OpenAPIURL) == "" {
		writeError(w, http.StatusNotFound, fmt.Errorf("module %q did not register an OpenAPI URL", moduleID))
		return
	}

	body, contentType, err := p.documentFor(r.Context(), moduleID, module.OpenAPIURL)
	if err != nil {
		// 502 — upstream failure is an infrastructure problem, not a client error.
		writeError(w, http.StatusBadGateway, fmt.Errorf("failed to fetch OpenAPI document for %q: %w", moduleID, err))
		return
	}

	w.Header().Set("Content-Type", contentType)
	w.Header().Set("X-Proxy-Cache-TTL-Seconds", fmt.Sprintf("%d", int(p.ttl.Seconds())))
	w.WriteHeader(http.StatusOK)
	_, _ = w.Write(body)
}

func (p *openAPIProxy) lookupModule(id string) (registry.ModuleRegistration, bool) {
	for _, module := range p.store.ListModules() {
		if module.ID == id {
			return module, true
		}
	}
	return registry.ModuleRegistration{}, false
}

// documentFor returns a cached response when fresh, otherwise fetches and stores it.
// A mutex guards the whole fetch so concurrent requests for the same module do not
// stampede the upstream after expiry.
func (p *openAPIProxy) documentFor(ctx context.Context, moduleID, url string) ([]byte, string, error) {
	p.mu.Lock()
	defer p.mu.Unlock()

	now := p.clock()
	if entry, ok := p.cache[moduleID]; ok && now.Before(entry.expiresAt) {
		return entry.body, entry.contentType, nil
	}

	body, contentType, err := p.fetcher.Fetch(ctx, url)
	if err != nil {
		return nil, "", err
	}

	// Reject non-JSON replies early — the UI expects parseable OpenAPI JSON.
	if !looksLikeJSON(body, contentType) {
		return nil, "", errors.New("upstream response is not JSON")
	}

	p.cache[moduleID] = openAPICacheEntry{
		body:        body,
		contentType: contentType,
		expiresAt:   now.Add(p.ttl),
	}
	return body, contentType, nil
}

// looksLikeJSON is a cheap heuristic — the Content-Type header is preferred, but
// some upstreams still omit it so we fall back to a structural sniff.
func looksLikeJSON(body []byte, contentType string) bool {
	if strings.Contains(strings.ToLower(contentType), "json") {
		return true
	}
	trimmed := strings.TrimSpace(string(body))
	if trimmed == "" {
		return false
	}
	first := trimmed[0]
	if first != '{' && first != '[' {
		return false
	}
	return json.Valid(body)
}
