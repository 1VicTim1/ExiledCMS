package moduleconfig

import (
	"context"
	"encoding/json"
	"fmt"
	"log/slog"
	"strings"
	"time"

	"github.com/nats-io/nats.go"
)

// NATSSyncService connects platform-core to the module configuration transport.
// It serves authoritative config snapshots over request/reply, receives effective
// config reports from modules, and periodically republishes desired config state.
type NATSSyncService struct {
	logger  *slog.Logger
	natsURL string
	store   *Store
}

// NewNATSSyncService creates a new NATS-backed config sync service.
func NewNATSSyncService(logger *slog.Logger, natsURL string, store *Store) *NATSSyncService {
	return &NATSSyncService{
		logger:  logger,
		natsURL: strings.TrimSpace(natsURL),
		store:   store,
	}
}

// Start connects to NATS and runs the config sync loops until the context is cancelled.
func (s *NATSSyncService) Start(ctx context.Context) error {
	if s == nil || s.store == nil {
		return nil
	}

	url := s.natsURL
	if url == "" {
		url = nats.DefaultURL
	}

	nc, err := nats.Connect(url, nats.Name("platform-core module config sync"))
	if err != nil {
		return fmt.Errorf("connect to nats: %w", err)
	}
	defer nc.Drain()

	if _, err = nc.Subscribe(ConfigRequestSubjectPrefix+">", s.handleRequest); err != nil {
		return fmt.Errorf("subscribe to config requests: %w", err)
	}

	if _, err = nc.Subscribe(ConfigReportedSubjectPrefix+">", s.handleReported); err != nil {
		return fmt.Errorf("subscribe to reported config: %w", err)
	}

	if err = nc.Flush(); err != nil {
		return fmt.Errorf("flush nats subscriptions: %w", err)
	}

	s.publishAllDesired(nc)
	publishTicker := time.NewTicker(15 * time.Second)
	defer publishTicker.Stop()

	for {
		select {
		case <-ctx.Done():
			return nil
		case <-publishTicker.C:
			s.publishAllDesired(nc)
		}
	}
}

func (s *NATSSyncService) handleRequest(message *nats.Msg) {
	moduleID := strings.TrimSpace(strings.TrimPrefix(message.Subject, ConfigRequestSubjectPrefix))
	if moduleID == "" {
		return
	}

	desired, ok := s.store.DesiredFor(moduleID)
	if !ok {
		return
	}

	payload, err := json.Marshal(desired)
	if err != nil {
		s.logger.Warn("failed to serialize desired module config", "moduleId", moduleID, "error", err)
		return
	}

	if err := message.Respond(payload); err != nil {
		s.logger.Warn("failed to respond to module config request", "moduleId", moduleID, "error", err)
	}
}

func (s *NATSSyncService) handleReported(message *nats.Msg) {
	var reported ReportedConfig
	if err := json.Unmarshal(message.Data, &reported); err != nil {
		s.logger.Warn("failed to decode reported module config", "subject", message.Subject, "error", err)
		return
	}

	if strings.TrimSpace(reported.ModuleID) == "" {
		reported.ModuleID = strings.TrimSpace(strings.TrimPrefix(message.Subject, ConfigReportedSubjectPrefix))
	}

	if err := s.store.SetReported(reported); err != nil {
		s.logger.Warn("failed to store reported module config", "moduleId", reported.ModuleID, "error", err)
	}
}

func (s *NATSSyncService) publishAllDesired(nc *nats.Conn) {
	for _, desired := range s.store.ListDesired() {
		payload, err := json.Marshal(desired)
		if err != nil {
			s.logger.Warn("failed to serialize desired module config for publish", "moduleId", desired.ModuleID, "error", err)
			continue
		}

		if err := nc.Publish(DesiredSubject(desired.ModuleID), payload); err != nil {
			s.logger.Warn("failed to publish desired module config", "moduleId", desired.ModuleID, "error", err)
		}
	}

	if err := nc.Flush(); err != nil {
		s.logger.Warn("failed to flush desired module config publish batch", "error", err)
	}
}
