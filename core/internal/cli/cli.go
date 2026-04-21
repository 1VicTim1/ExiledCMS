// Package cli implements the platform-core command-line interface.
//
// The binary historically only ran an HTTP server. Operators wanted one place
// from which they could also inspect configuration, list seeded modules, check
// infrastructure readiness and (in the future) orchestrate migrations — so we
// split the entry point into subcommands.
//
// Design choices:
//
//   - Zero external dependencies: dispatch is a plain switch over os.Args. The
//     CLI surface is small and stable, a cobra-style framework is overkill.
//   - Side effects (serving HTTP, loading env) live in the runtime callbacks,
//     not in this package. The dispatcher is pure and easy to test.
//   - The Runtime interface decouples the dispatcher from concrete subsystems
//     so tests can inject fakes and assert on behaviour.
package cli

import (
	"context"
	"fmt"
	"io"
	"sort"
	"strings"

	"github.com/exiledcms/platform-core/internal/config"
	"github.com/exiledcms/platform-core/internal/registry"
)

// Runtime captures everything the CLI needs from the running application.
// Using an interface keeps the dispatcher testable without spinning up a full
// Application, a live HTTP server or real infrastructure probes.
type Runtime interface {
	// Config returns the loaded configuration snapshot.
	Config() config.Config
	// Modules returns modules currently in the in-memory registry.
	// At boot only `platform-core` is present — runtime modules register via HTTP.
	Modules() []registry.ModuleRegistration
	// Permissions returns permissions seeded by the core or registered at runtime.
	Permissions() []registry.PermissionDefinition
	// Serve starts the HTTP server and blocks until ctx is cancelled.
	Serve(ctx context.Context) error
}

// Version identifies the binary to `platform-core version`. Injected by main
// so the CLI package stays decoupled from the version subsystem.
type Version struct {
	Version string
	Commit  string
	Time    string
}

// Dispatch parses args (excluding the program name) and executes the matching
// subcommand. It returns the exit code the caller should propagate.
//
// `out` and `errOut` let tests capture output and production code send to
// os.Stdout / os.Stderr.
func Dispatch(ctx context.Context, args []string, runtime Runtime, version Version, out, errOut io.Writer) int {
	if len(args) == 0 || args[0] == "serve" {
		if err := runtime.Serve(ctx); err != nil {
			fmt.Fprintln(errOut, "serve:", err)
			return 1
		}
		return 0
	}

	switch args[0] {
	case "version", "--version", "-v":
		return commandVersion(out, version)
	case "info":
		return commandInfo(out, runtime)
	case "modules":
		return commandModules(out, runtime)
	case "permissions":
		return commandPermissions(out, runtime)
	case "doctor":
		return commandDoctor(out, errOut, runtime)
	case "migrate":
		return commandMigrate(out, runtime, args[1:])
	case "help", "--help", "-h":
		writeUsage(out)
		return 0
	default:
		fmt.Fprintf(errOut, "unknown command %q\n\n", args[0])
		writeUsage(errOut)
		return 2
	}
}

func writeUsage(w io.Writer) {
	fmt.Fprintln(w, "platform-core — ExiledCMS control-plane")
	fmt.Fprintln(w)
	fmt.Fprintln(w, "usage:")
	fmt.Fprintln(w, "  platform-core [serve]          start the HTTP control-plane (default)")
	fmt.Fprintln(w, "  platform-core version          print build version")
	fmt.Fprintln(w, "  platform-core info             print loaded configuration and infra status")
	fmt.Fprintln(w, "  platform-core modules          list seeded and registered modules")
	fmt.Fprintln(w, "  platform-core permissions      list known permissions in the registry")
	fmt.Fprintln(w, "  platform-core doctor           validate configuration, exit non-zero on errors")
	fmt.Fprintln(w, "  platform-core migrate plan     list modules that own migrations")
	fmt.Fprintln(w, "  platform-core help             show this help")
}

func commandVersion(w io.Writer, v Version) int {
	version := v.Version
	if version == "" {
		version = "dev"
	}
	fmt.Fprintf(w, "platform-core %s (commit %s, built %s)\n", version, defaultIfEmpty(v.Commit, "unknown"), defaultIfEmpty(v.Time, "unknown"))
	return 0
}

func commandInfo(w io.Writer, runtime Runtime) int {
	cfg := runtime.Config()
	fmt.Fprintln(w, "platform-core")
	fmt.Fprintf(w, "  service       %s\n", cfg.ServiceName)
	fmt.Fprintf(w, "  environment   %s\n", cfg.Environment)
	fmt.Fprintf(w, "  http          %s\n", cfg.HTTPAddr())
	fmt.Fprintln(w, "infrastructure:")
	statuses := cfg.InfraStatus()
	keys := make([]string, 0, len(statuses))
	for key := range statuses {
		keys = append(keys, key)
	}
	sort.Strings(keys)
	for _, key := range keys {
		fmt.Fprintf(w, "  %-12s %s\n", key, statuses[key])
	}
	return 0
}

func commandModules(w io.Writer, runtime Runtime) int {
	modules := runtime.Modules()
	if len(modules) == 0 {
		fmt.Fprintln(w, "no modules registered")
		return 0
	}
	fmt.Fprintln(w, "registered modules:")
	for _, module := range modules {
		fmt.Fprintf(w, "  %-24s %-10s %-10s %s\n", module.ID, module.Kind, module.Version, module.BaseURL)
	}
	return 0
}

func commandPermissions(w io.Writer, runtime Runtime) int {
	permissions := runtime.Permissions()
	if len(permissions) == 0 {
		fmt.Fprintln(w, "no permissions registered")
		return 0
	}
	fmt.Fprintln(w, "registered permissions:")
	for _, permission := range permissions {
		fmt.Fprintf(w, "  %-36s %-12s %s\n", permission.Key, permission.Scope, permission.DisplayName)
	}
	return 0
}

// commandDoctor returns a non-zero exit code if the configuration is obviously
// wrong (e.g. missing MySQL DSN in a non-development environment). Warnings do
// not fail the command — they are informational.
func commandDoctor(out, errOut io.Writer, runtime Runtime) int {
	cfg := runtime.Config()
	problems, warnings := validateConfig(cfg)

	fmt.Fprintln(out, "doctor:")
	if len(problems) == 0 && len(warnings) == 0 {
		fmt.Fprintln(out, "  configuration looks healthy")
		return 0
	}
	for _, warning := range warnings {
		fmt.Fprintln(out, "  warn  ", warning)
	}
	for _, problem := range problems {
		fmt.Fprintln(errOut, "  error ", problem)
	}
	if len(problems) > 0 {
		return 1
	}
	return 0
}

func validateConfig(cfg config.Config) (problems, warnings []string) {
	if strings.TrimSpace(cfg.MySQLDSN) == "" {
		warnings = append(warnings, "MYSQL_DSN is empty — persistent storage will be unavailable")
	}
	if strings.TrimSpace(cfg.NATSURL) == "" {
		warnings = append(warnings, "NATS_URL is empty — module-config sync bus is disabled")
	}
	if strings.TrimSpace(cfg.SentryDSN) == "" {
		warnings = append(warnings, "SENTRY_DSN is empty — errors will not be forwarded to Sentry")
	}
	if cfg.HTTPPort <= 0 {
		problems = append(problems, "PORT is not a positive integer — the HTTP server cannot bind")
	}
	if strings.EqualFold(cfg.Environment, "production") {
		// In production, a missing broker is an outage, not a friendly warning.
		if strings.TrimSpace(cfg.NATSURL) == "" {
			problems = append(problems, "NATS_URL is required in production")
		}
		if strings.TrimSpace(cfg.MySQLDSN) == "" {
			problems = append(problems, "MYSQL_DSN is required in production")
		}
	}
	return problems, warnings
}

// commandMigrate is the operator-facing entry point for migrations. Actual SQL
// migrations live inside individual modules (see src/Services/*/Migrations) —
// the core only discovers who owns what. The `plan` subcommand is the current
// MVP; `apply` is reserved for a future coordinated rollout.
func commandMigrate(w io.Writer, runtime Runtime, args []string) int {
	sub := "plan"
	if len(args) > 0 {
		sub = args[0]
	}
	switch sub {
	case "plan":
		modules := runtime.Modules()
		fmt.Fprintln(w, "migration plan:")
		fmt.Fprintln(w, "  (modules run their own migrations at startup; this list is informational)")
		for _, module := range modules {
			if strings.TrimSpace(module.BaseURL) == "" {
				continue
			}
			fmt.Fprintf(w, "  - %-24s %s\n", module.ID, module.BaseURL)
		}
		return 0
	default:
		fmt.Fprintf(w, "unknown migrate subcommand %q (supported: plan)\n", sub)
		return 2
	}
}

func defaultIfEmpty(value, fallback string) string {
	if strings.TrimSpace(value) == "" {
		return fallback
	}
	return value
}
