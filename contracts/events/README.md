# Event Contracts

Initial platform event envelope for the MVP:

- `eventId`
- `eventType`
- `version`
- `occurredAt`
- `correlationId`
- `causationId`
- `payload`

Concrete event schemas will be added here before wiring `NATS JetStream` producers and consumers.
