package httpapi

import (
	"html/template"
	"net/http"
	"sort"
	"strings"
)

// openAPIDocumentItem describes one discoverable OpenAPI document in the platform.
// AggregatedURL is the proxied, same-origin URL served by platform-core and should
// always be preferred by the hub UI because module URLs may live on a private
// network the browser cannot reach. OpenAPIURL is kept for tooling that already
// resolves upstreams directly (CI, curl, module-to-module calls, etc.).
type openAPIDocumentItem struct {
	ID            string `json:"id"`
	Name          string `json:"name"`
	Kind          string `json:"kind"`
	OpenAPIURL    string `json:"openApiUrl,omitempty"`
	AggregatedURL string `json:"aggregatedUrl,omitempty"`
	SwaggerUIURL  string `json:"swaggerUiUrl,omitempty"`
}

// handleCoreOpenAPIDocument returns the platform-core OpenAPI document. It
// enumerates the routes this file's neighbour (server.go) actually mounts, so
// the hub remains a truthful reference and not a separate document that can
// drift from reality.
func (s *Server) handleCoreOpenAPIDocument(w http.ResponseWriter, r *http.Request) {
	writeJSON(w, http.StatusOK, map[string]any{
		"openapi": "3.0.3",
		"info": map[string]any{
			"title":       "ExiledCMS Platform Core API",
			"version":     "1.0.0",
			"description": "Control-plane API: module registry, permission registry, log ingestion, module-config sync, and centralized OpenAPI aggregation.",
		},
		"servers": []map[string]string{
			{"url": "/"},
		},
		"paths": map[string]any{
			"/healthz": pathItem("get", "Health probe"),
			"/readyz":  pathItem("get", "Readiness probe with infrastructure status"),

			"/api/v1/platform/info":         pathItem("get", "Service build and environment metadata"),
			"/api/v1/platform/capabilities": pathItem("get", "List known platform capabilities"),
			"/api/v1/platform/registry":     pathItem("get", "Registry snapshot (plugins, themes, modules, permissions)"),

			"/api/v1/platform/plugins":          pathItemMulti("get", "List registered plugins", "post", "Register a plugin manifest"),
			"/api/v1/platform/themes":           pathItemMulti("get", "List registered themes", "post", "Register a theme manifest"),
			"/api/v1/platform/themes/activate":  pathItem("post", "Activate a registered theme"),
			"/api/v1/platform/modules":          pathItemMulti("get", "List registered modules", "post", "Register or refresh a module"),
			"/api/v1/platform/modules/docs":     pathItem("get", "Documentation links grouped by module (filterable with ?kind=)"),
			"/api/v1/platform/module-config":    pathItem("get", "List desired and reported module configuration snapshots"),
			"/api/v1/platform/module-config/{id}": pathItem("get", "Get desired and reported configuration for one module"),

			"/api/v1/platform/logs":        pathItemMulti("get", "List centralized logs", "post", "Ingest structured logs from modules"),
			"/api/v1/platform/permissions": pathItemMulti("get", "List known platform permissions", "post", "Register a permission"),

			"/api/v1/platform/openapi/core.json":          pathItem("get", "This document"),
			"/api/v1/platform/openapi/documents":          pathItem("get", "List central and module OpenAPI documents with proxied URLs"),
			"/api/v1/platform/openapi/modules/{id}.json":  pathItem("get", "Server-side proxy for a module's OpenAPI JSON document"),
			"/swagger":                                    pathItem("get", "Central Swagger UI hub (HTML)"),
		},
	})
}

func pathItem(method, summary string) map[string]any {
	return map[string]any{method: map[string]any{"summary": summary}}
}

func pathItemMulti(a, aSummary, b, bSummary string) map[string]any {
	return map[string]any{
		a: map[string]any{"summary": aSummary},
		b: map[string]any{"summary": bSummary},
	}
}

// handleListOpenAPIDocuments returns the list consumed by the swagger hub and any
// external tooling that wants to auto-discover platform and module API documents.
func (s *Server) handleListOpenAPIDocuments(w http.ResponseWriter, r *http.Request) {
	writeJSON(w, http.StatusOK, map[string]any{
		"items": s.listOpenAPIDocuments(),
	})
}

// swaggerHubTemplate embeds the real Swagger UI from a public CDN and wires it
// to the centralized `/api/v1/platform/openapi/documents` index. It picks the
// first document on load; users can switch modules from Swagger UI's own
// top-bar selector, so we do not need to write a custom switcher.
//
// Keeping the page self-contained (no build step, no server-side bundle) means
// the hub works in any deployment, including air-gapped ones where the CDN is
// replaced by an internal mirror — only the two URLs in the template change.
const swaggerHubTemplate = `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>ExiledCMS API Hub</title>
  <link rel="stylesheet" href="https://unpkg.com/swagger-ui-dist@5/swagger-ui.css" />
  <style>
    body { margin: 0; background: #0b0b14; color: #f6f3ff; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; }
    header { padding: 20px 32px; border-bottom: 1px solid rgba(162,89,255,0.25); background: rgba(23,18,38,0.92); }
    header h1 { margin: 0 0 4px; font-size: 20px; }
    header p { margin: 0; color: #b7b0c7; font-size: 13px; }
    #swagger-ui { background: #fff; min-height: calc(100vh - 72px); }
    noscript { display: block; padding: 24px; }
  </style>
</head>
<body>
  <header>
    <h1>ExiledCMS API Hub</h1>
    <p>Central OpenAPI explorer. Module documents are proxied through platform-core so they are always same-origin.</p>
  </header>
  <noscript>This hub requires JavaScript. The raw OpenAPI JSON is available at <a href="/api/v1/platform/openapi/documents">/api/v1/platform/openapi/documents</a>.</noscript>
  <div id="swagger-ui"></div>
  <script src="https://unpkg.com/swagger-ui-dist@5/swagger-ui-bundle.js" crossorigin></script>
  <script src="https://unpkg.com/swagger-ui-dist@5/swagger-ui-standalone-preset.js" crossorigin></script>
  <script>
    (async function init() {
      let urls = [{ name: "Platform Core", url: "/api/v1/platform/openapi/core.json" }];
      try {
        const res = await fetch("/api/v1/platform/openapi/documents", { cache: "no-store" });
        if (res.ok) {
          const payload = await res.json();
          const items = Array.isArray(payload.items) ? payload.items : [];
          // Prefer the proxied aggregatedUrl so the browser hits the control-plane
          // origin it was already served from. Fall back to the raw module URL.
          urls = items
            .filter((item) => item.aggregatedUrl || item.openApiUrl)
            .map((item) => ({
              name: item.name + " (" + item.kind + ")",
              url: item.aggregatedUrl || item.openApiUrl,
            }));
          if (urls.length === 0) {
            urls = [{ name: "Platform Core", url: "/api/v1/platform/openapi/core.json" }];
          }
        }
      } catch (error) {
        console.error("swagger hub: failed to load document index", error);
      }

      window.ui = SwaggerUIBundle({
        urls,
        "urls.primaryName": urls[0].name,
        dom_id: "#swagger-ui",
        deepLinking: true,
        presets: [SwaggerUIBundle.presets.apis, SwaggerUIStandalonePreset],
        layout: "StandaloneLayout",
      });
    })();
  </script>
</body>
</html>`

// handleSwaggerHub renders the centralized Swagger UI HTML entry point. The
// template itself is static; dynamic document discovery happens client-side.
func (s *Server) handleSwaggerHub(w http.ResponseWriter, r *http.Request) {
	compiled, err := template.New("swagger-hub").Parse(swaggerHubTemplate)
	if err != nil {
		writeError(w, http.StatusInternalServerError, err)
		return
	}

	w.Header().Set("Content-Type", "text/html; charset=utf-8")
	w.WriteHeader(http.StatusOK)
	_ = compiled.Execute(w, nil)
}

func (s *Server) listOpenAPIDocuments() []openAPIDocumentItem {
	items := []openAPIDocumentItem{
		{
			ID:           "platform-core",
			Name:         "Platform Core",
			Kind:         "core",
			OpenAPIURL:   "/api/v1/platform/openapi/core.json",
			// platform-core is already same-origin, so no proxy indirection.
			AggregatedURL: "/api/v1/platform/openapi/core.json",
			SwaggerUIURL:  "/swagger",
		},
	}
	seen := map[string]struct{}{"platform-core": {}}

	for _, module := range s.store.ListModules() {
		if _, exists := seen[module.ID]; exists {
			continue
		}

		if strings.TrimSpace(module.OpenAPIURL) == "" && strings.TrimSpace(module.SwaggerUIURL) == "" {
			continue
		}
		seen[module.ID] = struct{}{}

		aggregated := ""
		if strings.TrimSpace(module.OpenAPIURL) != "" {
			// Proxy every module with an OpenAPI URL so the UI stays same-origin and
			// hides private control-plane addresses from browsers.
			aggregated = "/api/v1/platform/openapi/modules/" + module.ID + ".json"
		}

		items = append(items, openAPIDocumentItem{
			ID:            module.ID,
			Name:          module.Name,
			Kind:          module.Kind,
			OpenAPIURL:    module.OpenAPIURL,
			AggregatedURL: aggregated,
			SwaggerUIURL:  module.SwaggerUIURL,
		})
	}

	sort.Slice(items, func(i, j int) bool {
		if items[i].Kind == items[j].Kind {
			return items[i].ID < items[j].ID
		}
		return items[i].Kind < items[j].Kind
	})
	return items
}
