package main

import (
	"context"
	"errors"
	"net/http"
	"os"
	"os/signal"
	"syscall"
	"time"

	"github.com/exiledcms/platform-core/internal/app"
	"github.com/exiledcms/platform-core/internal/cli"
	"github.com/exiledcms/platform-core/internal/config"
	"github.com/exiledcms/platform-core/internal/moduleconfig"
	"github.com/exiledcms/platform-core/internal/registry"
	"github.com/exiledcms/platform-core/internal/version"
)

// main is deliberately thin: it constructs the runtime and hands the CLI
// dispatcher the os.Args tail. All subcommand logic lives in internal/cli so
// it can be exercised without a real HTTP server.
func main() {
	cfg := config.Load()
	application := app.New(cfg)

	runtime := &coreRuntime{cfg: cfg, app: application}

	shutdownContext, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()

	code := cli.Dispatch(
		shutdownContext,
		os.Args[1:],
		runtime,
		cli.Version{Version: version.BuildVersion, Commit: version.BuildCommit, Time: version.BuildTime},
		os.Stdout,
		os.Stderr,
	)

	// Sentry has to be flushed regardless of whether the HTTP server ran.
	application.Logs.Flush(2 * time.Second)
	if code != 0 {
		os.Exit(code)
	}
}

// coreRuntime adapts the live Application to the CLI's Runtime interface. Only
// the HTTP-server subcommand actually starts servers and background workers;
// everything else is a pure read from the seeded in-memory state.
type coreRuntime struct {
	cfg config.Config
	app *app.Application
}

func (c *coreRuntime) Config() config.Config                        { return c.cfg }
func (c *coreRuntime) Modules() []registry.ModuleRegistration       { return c.app.Registry.ListModules() }
func (c *coreRuntime) Permissions() []registry.PermissionDefinition { return c.app.Registry.ListPermissions() }

// Serve runs the HTTP control-plane alongside the NATS config-sync worker and
// blocks until ctx is cancelled (signal received). Errors during shutdown are
// logged but never surfaced as a non-zero exit unless the server itself failed
// to bind.
func (c *coreRuntime) Serve(ctx context.Context) error {
	configSync := moduleconfig.NewNATSSyncService(c.app.Logger, c.cfg.NATSURL, c.app.Configs)

	server := &http.Server{
		Addr:              c.cfg.HTTPAddr(),
		Handler:           c.app.Handler(),
		ReadHeaderTimeout: 5 * time.Second,
		ReadTimeout:       15 * time.Second,
		WriteTimeout:      15 * time.Second,
		IdleTimeout:       60 * time.Second,
	}

	go func() {
		if err := configSync.Start(ctx); err != nil {
			c.app.Logger.Error("platform core module config sync exited with failure", "error", err)
		}
	}()

	go func() {
		<-ctx.Done()

		shutdownCtx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()

		c.app.Logger.Info("shutting down platform core")
		if err := server.Shutdown(shutdownCtx); err != nil {
			c.app.Logger.Error("platform core shutdown failed", "error", err)
		}
		if !c.app.Logs.Flush(2 * time.Second) {
			c.app.Logger.Warn("timed out while flushing sentry events during shutdown")
		}
	}()

	c.app.Logger.Info("starting platform core", "addr", c.cfg.HTTPAddr(), "environment", c.cfg.Environment)

	if err := server.ListenAndServe(); err != nil && !errors.Is(err, http.ErrServerClosed) {
		return err
	}
	return nil
}
