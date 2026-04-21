# Tickets Service Events

All `tickets-service` events use the platform envelope declared in `contracts/events/README.md`:

- `eventId`
- `eventType`
- `version`
- `occurredAt`
- `correlationId`
- `causationId`
- `payload`

## Subjects

### `ticket.created`

Payload:

```json
{
  "ticketId": "guid",
  "subject": "Cannot open crate menu",
  "priority": "high",
  "status": "open",
  "category": {
    "id": "guid",
    "name": "Technical Support"
  },
  "createdBy": {
    "userId": "guid",
    "displayName": "Player123"
  },
  "createdAtUtc": "2026-04-19T21:55:00Z"
}
```

### `ticket.message.added`

Payload:

```json
{
  "ticketId": "guid",
  "messageId": "guid",
  "isStaffReply": true,
  "author": {
    "userId": "guid",
    "displayName": "ModeratorOne",
    "role": "moderator"
  },
  "createdAtUtc": "2026-04-19T22:01:00Z"
}
```

### `ticket.assigned`

Payload:

```json
{
  "ticketId": "guid",
  "assignmentId": "guid",
  "assignee": {
    "userId": "guid",
    "displayName": "ModeratorOne"
  },
  "assignedBy": {
    "userId": "guid",
    "displayName": "ModeratorOne",
    "role": "moderator"
  },
  "assignedAtUtc": "2026-04-19T22:03:00Z"
}
```

### `ticket.status.changed`

Payload:

```json
{
  "ticketId": "guid",
  "previousStatus": "open",
  "newStatus": "in_progress",
  "changedBy": {
    "userId": "guid",
    "displayName": "ModeratorOne",
    "role": "moderator"
  },
  "reason": "Investigating logs",
  "changedAtUtc": "2026-04-19T22:04:00Z"
}
```

### `ticket.closed`

Payload:

```json
{
  "ticketId": "guid",
  "closedBy": {
    "userId": "guid",
    "displayName": "ModeratorOne",
    "role": "moderator"
  },
  "closedAtUtc": "2026-04-19T22:10:00Z",
  "reason": "Issue solved"
}
```

## Delivery model

The service writes domain events into the MySQL outbox table `ticket_outbox_events` inside the same transaction as business changes. A background dispatcher publishes the envelope JSON to NATS, which keeps the service ready for future notification consumers without coupling business handlers to broker availability.
