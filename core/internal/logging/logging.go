package logging

import (
	"context"
	"fmt"
	"log/slog"
	"strings"
	"sync"
	"time"

	"github.com/getsentry/sentry-go"
)

const (
	SourceCore   = "core"
	SourceModule = "module"
)

type Entry struct {
	Timestamp  time.Time      `json:"timestamp"`
	Source     string         `json:"source"`
	Service    string         `json:"service,omitempty"`
	ModuleID   string         `json:"moduleId,omitempty"`
	Level      string         `json:"level"`
	Message    string         `json:"message"`
	Attributes map[string]any `json:"attributes,omitempty"`
}

type IngestRequest struct {
	Entries []IngestEntry `json:"entries"`
}

type IngestEntry struct {
	Timestamp  time.Time      `json:"timestamp,omitempty"`
	Service    string         `json:"service,omitempty"`
	ModuleID   string         `json:"moduleId,omitempty"`
	Level      string         `json:"level"`
	Message    string         `json:"message"`
	Attributes map[string]any `json:"attributes,omitempty"`
}

type ListOptions struct {
	Limit        int
	Source       string
	ModuleID     string
	MinimumLevel string
}

type RouterOptions struct {
	ServiceName      string
	Environment      string
	Release          string
	SentryDSN        string
	SentryMinLevel   string
	BufferMaxEntries int
}

type Store struct {
	mu         sync.RWMutex
	maxEntries int
	entries    []Entry
}

func NewStore(maxEntries int) *Store {
	if maxEntries <= 0 {
		maxEntries = 1000
	}

	return &Store{
		maxEntries: maxEntries,
		entries:    make([]Entry, 0, maxEntries),
	}
}

func (s *Store) Append(entry Entry) {
	s.mu.Lock()
	defer s.mu.Unlock()

	if len(s.entries) == s.maxEntries {
		copy(s.entries, s.entries[1:])
		s.entries[len(s.entries)-1] = cloneEntry(entry)
		return
	}

	s.entries = append(s.entries, cloneEntry(entry))
}

func (s *Store) List(options ListOptions) []Entry {
	limit := options.Limit
	if limit <= 0 {
		limit = 100
	}

	minimumLevel := strings.TrimSpace(options.MinimumLevel)
	source := strings.TrimSpace(options.Source)
	moduleID := strings.TrimSpace(options.ModuleID)

	s.mu.RLock()
	defer s.mu.RUnlock()

	items := make([]Entry, 0, min(limit, len(s.entries)))
	for index := len(s.entries) - 1; index >= 0; index-- {
		entry := s.entries[index]
		if source != "" && !strings.EqualFold(entry.Source, source) {
			continue
		}

		if moduleID != "" && !strings.EqualFold(entry.ModuleID, moduleID) {
			continue
		}

		if minimumLevel != "" && !levelAtLeast(entry.Level, minimumLevel) {
			continue
		}

		items = append(items, cloneEntry(entry))
		if len(items) >= limit {
			break
		}
	}

	return items
}

func (s *Store) Count() int {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return len(s.entries)
}

func (s *Store) Capacity() int {
	return s.maxEntries
}

type Router struct {
	store          *Store
	serviceName    string
	sentryEnabled  bool
	sentryMinLevel string
}

func NewRouter(options RouterOptions) (*Router, error) {
	router := &Router{
		store:          NewStore(options.BufferMaxEntries),
		serviceName:    fallbackString(options.ServiceName, "platform-core"),
		sentryMinLevel: normalizeConfiguredLevel(options.SentryMinLevel, "error"),
	}

	dsn := strings.TrimSpace(options.SentryDSN)
	if dsn == "" {
		return router, nil
	}

	err := sentry.Init(sentry.ClientOptions{
		Dsn:         dsn,
		Environment: fallbackString(options.Environment, "development"),
		Release:     fallbackString(options.Release, "dev"),
	})
	if err != nil {
		return router, err
	}

	router.sentryEnabled = true
	return router, nil
}

func (r *Router) Store() *Store {
	return r.store
}

func (r *Router) SentryEnabled() bool {
	return r.sentryEnabled
}

func (r *Router) SentryMinLevel() string {
	return r.sentryMinLevel
}

func (r *Router) Process(entry Entry) Entry {
	normalized := normalizeEntry(entry, r.serviceName)
	r.store.Append(normalized)
	if r.sentryEnabled && levelAtLeast(normalized.Level, r.sentryMinLevel) {
		r.sendToSentry(normalized)
	}

	return normalized
}

func (r *Router) RecordModuleLog(entry IngestEntry) (Entry, error) {
	level, err := ParseLevel(entry.Level)
	if err != nil {
		return Entry{}, err
	}

	message := strings.TrimSpace(entry.Message)
	if message == "" {
		return Entry{}, fmt.Errorf("log message is required")
	}

	return r.Process(Entry{
		Timestamp:  entry.Timestamp,
		Source:     SourceModule,
		Service:    strings.TrimSpace(entry.Service),
		ModuleID:   strings.TrimSpace(entry.ModuleID),
		Level:      level,
		Message:    message,
		Attributes: cloneAttributes(entry.Attributes),
	}), nil
}

func (r *Router) Flush(timeout time.Duration) bool {
	if !r.sentryEnabled {
		return true
	}

	return sentry.Flush(timeout)
}

func (r *Router) sendToSentry(entry Entry) {
	sentry.CaptureEvent(newSentryEvent(entry, r.serviceName))
}

type Handler struct {
	next   slog.Handler
	router *Router
}

func NewHandler(next slog.Handler, router *Router) *Handler {
	return &Handler{
		next:   next,
		router: router,
	}
}

func (h *Handler) Enabled(ctx context.Context, level slog.Level) bool {
	return h.next.Enabled(ctx, level)
}

func (h *Handler) Handle(ctx context.Context, record slog.Record) error {
	if h.router != nil {
		h.router.Process(Entry{
			Timestamp:  record.Time,
			Source:     SourceCore,
			Service:    h.router.serviceName,
			Level:      levelFromSlog(record.Level),
			Message:    record.Message,
			Attributes: attributesFromRecord(record),
		})
	}

	return h.next.Handle(ctx, record)
}

func (h *Handler) WithAttrs(attrs []slog.Attr) slog.Handler {
	return &Handler{
		next:   h.next.WithAttrs(attrs),
		router: h.router,
	}
}

func (h *Handler) WithGroup(name string) slog.Handler {
	return &Handler{
		next:   h.next.WithGroup(name),
		router: h.router,
	}
}

func ParseLevel(value string) (string, error) {
	switch strings.ToLower(strings.TrimSpace(value)) {
	case "debug":
		return "debug", nil
	case "info":
		return "info", nil
	case "warn", "warning":
		return "warn", nil
	case "error":
		return "error", nil
	case "fatal":
		return "fatal", nil
	default:
		return "", fmt.Errorf("unsupported log level %q", value)
	}
}

func normalizeConfiguredLevel(value string, fallback string) string {
	normalized, err := ParseLevel(value)
	if err != nil {
		return fallback
	}

	return normalized
}

func normalizeEntry(entry Entry, defaultService string) Entry {
	timestamp := entry.Timestamp
	if timestamp.IsZero() {
		timestamp = time.Now().UTC()
	} else {
		timestamp = timestamp.UTC()
	}

	source := fallbackString(entry.Source, SourceCore)
	service := strings.TrimSpace(entry.Service)
	moduleID := strings.TrimSpace(entry.ModuleID)
	if service == "" {
		if source == SourceCore {
			service = defaultService
		} else {
			service = moduleID
		}
	}

	return Entry{
		Timestamp:  timestamp,
		Source:     source,
		Service:    service,
		ModuleID:   moduleID,
		Level:      normalizeConfiguredLevel(entry.Level, "info"),
		Message:    strings.TrimSpace(entry.Message),
		Attributes: cloneAttributes(entry.Attributes),
	}
}

func levelAtLeast(level string, minimum string) bool {
	return levelRank(normalizeConfiguredLevel(level, "info")) >= levelRank(normalizeConfiguredLevel(minimum, "error"))
}

func levelRank(level string) int {
	switch level {
	case "debug":
		return 10
	case "info":
		return 20
	case "warn":
		return 30
	case "error":
		return 40
	case "fatal":
		return 50
	default:
		return 0
	}
}

func levelFromSlog(level slog.Level) string {
	switch {
	case level >= slog.LevelError:
		return "error"
	case level >= slog.LevelWarn:
		return "warn"
	case level >= slog.LevelInfo:
		return "info"
	default:
		return "debug"
	}
}

func toSentryLevel(level string) sentry.Level {
	switch normalizeConfiguredLevel(level, "info") {
	case "debug":
		return sentry.LevelDebug
	case "warn":
		return sentry.LevelWarning
	case "error":
		return sentry.LevelError
	case "fatal":
		return sentry.LevelFatal
	default:
		return sentry.LevelInfo
	}
}

func newSentryEvent(entry Entry, defaultService string) *sentry.Event {
	serviceName := strings.TrimSpace(entry.Service)
	if serviceName == "" {
		if strings.EqualFold(entry.Source, SourceModule) && strings.TrimSpace(entry.ModuleID) != "" {
			serviceName = strings.TrimSpace(entry.ModuleID)
		} else {
			serviceName = fallbackString(entry.Service, defaultService)
		}
	}

	event := sentry.NewEvent()
	event.Timestamp = entry.Timestamp.UTC()
	event.Message = entry.Message
	event.Level = toSentryLevel(entry.Level)
	event.Tags = map[string]string{
		"source":  entry.Source,
		"service": serviceName,
	}
	if entry.ModuleID != "" {
		event.Tags["moduleId"] = entry.ModuleID
	}

	if correlationID := extractStringAttribute(entry.Attributes, "correlationId", "correlationID", "requestId", "traceId"); correlationID != "" {
		event.Tags["correlationId"] = correlationID
	}

	event.Extra = map[string]any{
		"timestamp": entry.Timestamp.UTC().Format(time.RFC3339Nano),
	}
	for key, value := range entry.Attributes {
		event.Extra[key] = value
	}

	if stackTrace := extractStringAttribute(entry.Attributes, "stackTrace", "exceptionStackTrace"); stackTrace != "" {
		event.Extra["moduleStackTrace"] = stackTrace
	}

	if fingerprint := extractFingerprint(entry.Attributes); len(fingerprint) > 0 {
		event.Fingerprint = fingerprint
	}

	if exception := extractSentryException(entry); exception != nil {
		event.Exception = []sentry.Exception{*exception}
	}

	return event
}

func extractSentryException(entry Entry) *sentry.Exception {
	exceptionValue := extractStringAttribute(entry.Attributes, "exceptionMessage", "errorMessage", "error", "exception")
	stackTrace := extractStringAttribute(entry.Attributes, "stackTrace", "exceptionStackTrace")
	if exceptionValue == "" && stackTrace == "" {
		return nil
	}

	exceptionType := extractStringAttribute(entry.Attributes, "exceptionType", "errorType")
	if exceptionType == "" {
		exceptionType = "ModuleError"
	}

	if exceptionValue == "" {
		exceptionValue = entry.Message
	}

	return &sentry.Exception{
		Type:  exceptionType,
		Value: exceptionValue,
	}
}

func extractFingerprint(attributes map[string]any) []string {
	if len(attributes) == 0 {
		return nil
	}

	value, ok := attributes["fingerprint"]
	if !ok {
		return nil
	}

	switch typed := value.(type) {
	case string:
		parts := strings.FieldsFunc(typed, func(r rune) bool {
			return r == ',' || r == ';' || r == '|'
		})
		return normalizeFingerprint(parts)
	case []string:
		return normalizeFingerprint(typed)
	case []any:
		parts := make([]string, 0, len(typed))
		for _, item := range typed {
			if stringValue := strings.TrimSpace(fmt.Sprint(item)); stringValue != "" {
				parts = append(parts, stringValue)
			}
		}
		return normalizeFingerprint(parts)
	default:
		stringValue := strings.TrimSpace(fmt.Sprint(typed))
		if stringValue == "" {
			return nil
		}
		return []string{stringValue}
	}
}

func normalizeFingerprint(values []string) []string {
	if len(values) == 0 {
		return nil
	}

	items := make([]string, 0, len(values))
	for _, value := range values {
		if trimmed := strings.TrimSpace(value); trimmed != "" {
			items = append(items, trimmed)
		}
	}

	if len(items) == 0 {
		return nil
	}

	return items
}

func extractStringAttribute(attributes map[string]any, keys ...string) string {
	if len(attributes) == 0 {
		return ""
	}

	for _, key := range keys {
		for attributeKey, attributeValue := range attributes {
			if !strings.EqualFold(attributeKey, key) {
				continue
			}

			if attributeValue == nil {
				return ""
			}

			return strings.TrimSpace(fmt.Sprint(attributeValue))
		}
	}

	return ""
}

func attributesFromRecord(record slog.Record) map[string]any {
	if record.NumAttrs() == 0 {
		return nil
	}

	attributes := make(map[string]any, record.NumAttrs())
	record.Attrs(func(attr slog.Attr) bool {
		attributes[attr.Key] = valueToAny(attr.Value)
		return true
	})

	return attributes
}

func valueToAny(value slog.Value) any {
	resolved := value.Resolve()
	switch resolved.Kind() {
	case slog.KindBool:
		return resolved.Bool()
	case slog.KindDuration:
		return resolved.Duration().String()
	case slog.KindFloat64:
		return resolved.Float64()
	case slog.KindInt64:
		return resolved.Int64()
	case slog.KindString:
		return resolved.String()
	case slog.KindTime:
		return resolved.Time().UTC().Format(time.RFC3339Nano)
	case slog.KindUint64:
		return resolved.Uint64()
	case slog.KindGroup:
		group := make(map[string]any, len(resolved.Group()))
		for _, attr := range resolved.Group() {
			group[attr.Key] = valueToAny(attr.Value)
		}
		return group
	default:
		value := resolved.Any()
		if err, ok := value.(error); ok {
			return err.Error()
		}

		if stringer, ok := value.(fmt.Stringer); ok {
			return stringer.String()
		}

		return value
	}
}

func cloneEntry(entry Entry) Entry {
	entry.Attributes = cloneAttributes(entry.Attributes)
	return entry
}

func cloneAttributes(attributes map[string]any) map[string]any {
	if len(attributes) == 0 {
		return nil
	}

	cloned := make(map[string]any, len(attributes))
	for key, value := range attributes {
		cloned[key] = value
	}

	return cloned
}

func fallbackString(value string, fallback string) string {
	trimmed := strings.TrimSpace(value)
	if trimmed == "" {
		return fallback
	}

	return trimmed
}
