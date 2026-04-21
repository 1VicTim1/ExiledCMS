package logging

import (
	"io"
	"log/slog"
	"testing"
	"time"

	"github.com/getsentry/sentry-go"
)

func TestStoreAppendAndListApplyRetentionAndFilters(t *testing.T) {
	store := NewStore(2)
	firstTime := time.Date(2026, time.April, 20, 0, 0, 0, 0, time.UTC)
	secondTime := firstTime.Add(time.Minute)
	thirdTime := secondTime.Add(time.Minute)

	store.Append(Entry{Timestamp: firstTime, Source: SourceModule, ModuleID: "tickets-service", Level: "info", Message: "first"})
	store.Append(Entry{Timestamp: secondTime, Source: SourceCore, Service: "platform-core", Level: "error", Message: "second"})
	store.Append(Entry{Timestamp: thirdTime, Source: SourceModule, ModuleID: "tickets-service", Level: "warn", Message: "third"})

	items := store.List(ListOptions{Limit: 10})
	if len(items) != 2 {
		t.Fatalf("expected retained entries to match capacity, got %d", len(items))
	}

	if items[0].Message != "third" || items[1].Message != "second" {
		t.Fatalf("expected entries to be returned newest-first, got %#v", items)
	}

	filtered := store.List(ListOptions{Limit: 10, Source: SourceModule, ModuleID: "tickets-service", MinimumLevel: "warn"})
	if len(filtered) != 1 || filtered[0].Message != "third" {
		t.Fatalf("expected source/module/level filters to match only the warning module entry, got %#v", filtered)
	}
}

func TestRecordModuleLogValidatesAndNormalizesEntries(t *testing.T) {
	router, err := NewRouter(RouterOptions{
		ServiceName:      "platform-core",
		SentryMinLevel:   "warning",
		BufferMaxEntries: 10,
	})
	if err != nil {
		t.Fatalf("expected router initialization without sentry dsn to succeed, got %v", err)
	}

	entry, err := router.RecordModuleLog(IngestEntry{
		ModuleID: "tickets-service",
		Level:    "warning",
		Message:  " queued retry ",
		Attributes: map[string]any{
			"ticketId": "42",
		},
	})
	if err != nil {
		t.Fatalf("expected module log to be accepted, got %v", err)
	}

	if entry.Source != SourceModule {
		t.Fatalf("expected module source, got %q", entry.Source)
	}

	if entry.Level != "warn" {
		t.Fatalf("expected warning level to normalize to warn, got %q", entry.Level)
	}

	if entry.Service != "tickets-service" {
		t.Fatalf("expected empty service to fall back to module id, got %q", entry.Service)
	}

	if entry.Message != "queued retry" {
		t.Fatalf("expected message to be trimmed, got %q", entry.Message)
	}

	if router.SentryEnabled() {
		t.Fatalf("expected sentry to remain disabled without a dsn")
	}

	if router.SentryMinLevel() != "warn" {
		t.Fatalf("expected configured sentry level to normalize to warn, got %q", router.SentryMinLevel())
	}
}

func TestRecordModuleLogRejectsUnsupportedLevelAndEmptyMessage(t *testing.T) {
	router, err := NewRouter(RouterOptions{ServiceName: "platform-core"})
	if err != nil {
		t.Fatalf("expected router initialization to succeed, got %v", err)
	}

	if _, err := router.RecordModuleLog(IngestEntry{Level: "verbose", Message: "bad"}); err == nil {
		t.Fatalf("expected unsupported level to be rejected")
	}

	if _, err := router.RecordModuleLog(IngestEntry{Level: "info", Message: "   "}); err == nil {
		t.Fatalf("expected empty message to be rejected")
	}
}

func TestHandlerWritesStructuredCoreLogsIntoRouter(t *testing.T) {
	router, err := NewRouter(RouterOptions{ServiceName: "platform-core", BufferMaxEntries: 5})
	if err != nil {
		t.Fatalf("expected router initialization to succeed, got %v", err)
	}

	logger := slog.New(NewHandler(slog.NewJSONHandler(io.Discard, nil), router))
	logger.Info("ticket created", "ticketId", "42")

	items := router.Store().List(ListOptions{Limit: 10})
	if len(items) != 1 {
		t.Fatalf("expected one buffered entry, got %d", len(items))
	}

	item := items[0]
	if item.Source != SourceCore {
		t.Fatalf("expected core source, got %q", item.Source)
	}

	if item.Service != "platform-core" {
		t.Fatalf("expected service to default to platform-core, got %q", item.Service)
	}

	if item.Level != "info" {
		t.Fatalf("expected info level, got %q", item.Level)
	}

	if item.Attributes["ticketId"] != "42" {
		t.Fatalf("expected structured attribute to be preserved, got %#v", item.Attributes)
	}
}

func TestToSentryLevelMapsKnownLevels(t *testing.T) {
	cases := map[string]sentry.Level{
		"debug": sentry.LevelDebug,
		"info":  sentry.LevelInfo,
		"warn":  sentry.LevelWarning,
		"error": sentry.LevelError,
		"fatal": sentry.LevelFatal,
	}

	for level, expected := range cases {
		if actual := toSentryLevel(level); actual != expected {
			t.Fatalf("expected level %q to map to %v, got %v", level, expected, actual)
		}
	}
}

func TestNewSentryEventPromotesModuleExceptionContext(t *testing.T) {
	entry := Entry{
		Timestamp: time.Date(2026, time.April, 21, 10, 30, 0, 0, time.UTC),
		Source:    SourceModule,
		ModuleID:  "tickets-service",
		Level:     "error",
		Message:   "ticket creation failed",
		Attributes: map[string]any{
			"exceptionType":    "MySqlException",
			"exceptionMessage": "duplicate key",
			"stackTrace":       "at Tickets.Create()",
			"correlationId":    "req-42",
			"fingerprint":      []any{"tickets-service", "duplicate-key"},
		},
	}

	event := newSentryEvent(entry, "platform-core")

	if event.Level != sentry.LevelError {
		t.Fatalf("expected error level, got %v", event.Level)
	}

	if event.Tags["service"] != "tickets-service" {
		t.Fatalf("expected service tag to fall back to module id, got %#v", event.Tags)
	}

	if event.Tags["moduleId"] != "tickets-service" {
		t.Fatalf("expected module id tag to be present, got %#v", event.Tags)
	}

	if event.Tags["correlationId"] != "req-42" {
		t.Fatalf("expected correlation id tag to be promoted, got %#v", event.Tags)
	}

	if len(event.Exception) != 1 {
		t.Fatalf("expected a single exception payload, got %#v", event.Exception)
	}

	if event.Exception[0].Type != "MySqlException" || event.Exception[0].Value != "duplicate key" {
		t.Fatalf("expected exception metadata to be preserved, got %#v", event.Exception[0])
	}

	if extra, ok := event.Extra["moduleStackTrace"]; !ok || extra != "at Tickets.Create()" {
		t.Fatalf("expected module stack trace in extras, got %#v", event.Extra)
	}

	if len(event.Fingerprint) != 2 || event.Fingerprint[0] != "tickets-service" || event.Fingerprint[1] != "duplicate-key" {
		t.Fatalf("expected fingerprint to be normalized, got %#v", event.Fingerprint)
	}
}
