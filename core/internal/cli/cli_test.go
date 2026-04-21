package cli

import (
	"bytes"
	"context"
	"strings"
	"testing"

	"github.com/exiledcms/platform-core/internal/config"
	"github.com/exiledcms/platform-core/internal/registry"
)

// fakeRuntime is a deterministic Runtime stub for dispatcher tests. Tracking
// serveCalled proves that only the explicit `serve` path reaches the HTTP loop.
type fakeRuntime struct {
	cfg         config.Config
	modules     []registry.ModuleRegistration
	permissions []registry.PermissionDefinition
	serveCalled bool
	serveErr    error
}

func (f *fakeRuntime) Config() config.Config                       { return f.cfg }
func (f *fakeRuntime) Modules() []registry.ModuleRegistration      { return f.modules }
func (f *fakeRuntime) Permissions() []registry.PermissionDefinition { return f.permissions }
func (f *fakeRuntime) Serve(_ context.Context) error {
	f.serveCalled = true
	return f.serveErr
}

func newFakeRuntime() *fakeRuntime {
	return &fakeRuntime{
		cfg: config.Config{
			ServiceName: "platform-core",
			Environment: "test",
			HTTPPort:    8080,
			MySQLDSN:    "mysql-dsn",
			NATSURL:     "nats://localhost:4222",
		},
		modules: []registry.ModuleRegistration{
			{ID: "platform-core", Name: "Platform Core", Version: "1.0.0", Kind: "core", BaseURL: "http://platform-core:8080"},
			{ID: "tickets-service", Name: "Tickets", Version: "1.0.0", Kind: "service", BaseURL: "http://tickets-service:8080"},
		},
		permissions: []registry.PermissionDefinition{
			{Key: "platform.logs.view", DisplayName: "View logs", Scope: "platform"},
			{Key: "ticket.create", DisplayName: "Create ticket", Scope: "tickets"},
		},
	}
}

func run(t *testing.T, args []string, runtime Runtime) (code int, stdout, stderr string) {
	t.Helper()
	var out, errOut bytes.Buffer
	code = Dispatch(context.Background(), args, runtime, Version{Version: "1.2.3", Commit: "abc", Time: "today"}, &out, &errOut)
	return code, out.String(), errOut.String()
}

func TestDispatchNoArgsCallsServe(t *testing.T) {
	runtime := newFakeRuntime()
	code, _, _ := run(t, nil, runtime)
	if code != 0 || !runtime.serveCalled {
		t.Fatalf("expected default serve to run and exit 0, got code=%d served=%v", code, runtime.serveCalled)
	}
}

func TestDispatchExplicitServeCallsServe(t *testing.T) {
	runtime := newFakeRuntime()
	code, _, _ := run(t, []string{"serve"}, runtime)
	if code != 0 || !runtime.serveCalled {
		t.Fatalf("expected explicit serve to run and exit 0, got code=%d served=%v", code, runtime.serveCalled)
	}
}

func TestDispatchVersionPrintsBuildInfo(t *testing.T) {
	runtime := newFakeRuntime()
	code, stdout, _ := run(t, []string{"version"}, runtime)
	if code != 0 {
		t.Fatalf("expected version to exit 0, got %d", code)
	}
	if !strings.Contains(stdout, "1.2.3") || !strings.Contains(stdout, "abc") {
		t.Fatalf("expected version output to contain injected metadata, got %q", stdout)
	}
	if runtime.serveCalled {
		t.Fatalf("version must not start the HTTP server")
	}
}

func TestDispatchInfoListsInfrastructure(t *testing.T) {
	runtime := newFakeRuntime()
	code, stdout, _ := run(t, []string{"info"}, runtime)
	if code != 0 {
		t.Fatalf("expected info to exit 0, got %d", code)
	}
	if !strings.Contains(stdout, "mysql") || !strings.Contains(stdout, "nats") {
		t.Fatalf("expected info to list infrastructure entries, got %q", stdout)
	}
}

func TestDispatchModulesListsRegistry(t *testing.T) {
	runtime := newFakeRuntime()
	code, stdout, _ := run(t, []string{"modules"}, runtime)
	if code != 0 {
		t.Fatalf("expected modules to exit 0, got %d", code)
	}
	if !strings.Contains(stdout, "platform-core") || !strings.Contains(stdout, "tickets-service") {
		t.Fatalf("expected modules output to contain both registered modules, got %q", stdout)
	}
}

func TestDispatchPermissionsListsRegistry(t *testing.T) {
	runtime := newFakeRuntime()
	code, stdout, _ := run(t, []string{"permissions"}, runtime)
	if code != 0 {
		t.Fatalf("expected permissions to exit 0, got %d", code)
	}
	if !strings.Contains(stdout, "ticket.create") {
		t.Fatalf("expected permissions output to contain registered key, got %q", stdout)
	}
}

func TestDispatchDoctorReportsHealthyConfig(t *testing.T) {
	runtime := newFakeRuntime()
	runtime.cfg.SentryDSN = "https://example@sentry.invalid/1"
	code, stdout, _ := run(t, []string{"doctor"}, runtime)
	if code != 0 {
		t.Fatalf("expected doctor to exit 0 on healthy config, got %d", code)
	}
	if !strings.Contains(stdout, "healthy") {
		t.Fatalf("expected doctor to confirm health, got %q", stdout)
	}
}

func TestDispatchDoctorWarnsOnMissingDsn(t *testing.T) {
	runtime := newFakeRuntime()
	runtime.cfg.SentryDSN = ""
	code, stdout, _ := run(t, []string{"doctor"}, runtime)
	if code != 0 {
		t.Fatalf("missing Sentry DSN is a warning, not a hard error; expected 0 got %d", code)
	}
	if !strings.Contains(stdout, "SENTRY_DSN") {
		t.Fatalf("expected doctor to warn about Sentry, got %q", stdout)
	}
}

func TestDispatchDoctorFailsInProductionWithoutRequiredInfra(t *testing.T) {
	runtime := newFakeRuntime()
	runtime.cfg.Environment = "production"
	runtime.cfg.NATSURL = ""
	runtime.cfg.MySQLDSN = ""
	code, _, stderr := run(t, []string{"doctor"}, runtime)
	if code != 1 {
		t.Fatalf("expected production-with-missing-infra doctor to exit 1, got %d", code)
	}
	if !strings.Contains(stderr, "NATS_URL") || !strings.Contains(stderr, "MYSQL_DSN") {
		t.Fatalf("expected doctor to report both missing required vars, got %q", stderr)
	}
}

func TestDispatchMigratePlanListsModules(t *testing.T) {
	runtime := newFakeRuntime()
	code, stdout, _ := run(t, []string{"migrate", "plan"}, runtime)
	if code != 0 {
		t.Fatalf("expected migrate plan to exit 0, got %d", code)
	}
	if !strings.Contains(stdout, "tickets-service") {
		t.Fatalf("expected migrate plan to reference known modules, got %q", stdout)
	}
}

func TestDispatchUnknownCommandReturnsUsageError(t *testing.T) {
	runtime := newFakeRuntime()
	code, _, stderr := run(t, []string{"teleport"}, runtime)
	if code != 2 {
		t.Fatalf("expected unknown command to exit 2, got %d", code)
	}
	if !strings.Contains(stderr, "unknown command") {
		t.Fatalf("expected usage error to mention unknown command, got %q", stderr)
	}
	if runtime.serveCalled {
		t.Fatalf("unknown command must not invoke Serve")
	}
}

func TestDispatchHelpPrintsUsage(t *testing.T) {
	runtime := newFakeRuntime()
	code, stdout, _ := run(t, []string{"help"}, runtime)
	if code != 0 {
		t.Fatalf("expected help to exit 0, got %d", code)
	}
	if !strings.Contains(stdout, "platform-core") || !strings.Contains(stdout, "doctor") {
		t.Fatalf("expected usage output to list subcommands, got %q", stdout)
	}
}
