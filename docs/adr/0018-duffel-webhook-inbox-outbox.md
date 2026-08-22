# 0018. Duffel Webhook Processing via Inbox/Outbox

**Date:** 2026-05-13
**Status:** Accepted
**Deciders:** M1 design author

> **Amended 2026-05-16** — Confirmed correct after WS1+WS3 implementation. The inbox/outbox
> description matches the real Wolverine transactional outbox. `OrderTicketed` is the correct
> domain event name for `order.created.documents_issued` (not `BookingConfirmed`).

> **Amended 2026-08-22** — ADR 0023 places raw-body/header ingestion behind an Application port.
> Duffel verification, supplier DTOs, EF inbox persistence, duplicate handling, and Wolverine
> outbox publication remain in Infrastructure and retain this ADR's atomic transaction behavior.

> **Clarified 2026-08-22** — An invalid Duffel signature returns 401 Unauthorized.
> This corrects the historical 400 response stated in Decision step 1; malformed payloads
> continue to return 400 Bad Request.

## Context

Duffel delivers order lifecycle events (e.g. `order.created`, `order.updated`, `order.cancelled`) as HTTP webhooks to `POST /webhooks/duffel`. The endpoint must return a 2xx response within Duffel's timeout window (typically a few seconds), regardless of how long downstream aggregate work takes. At the same time, each webhook must be processed exactly once even if Duffel retries after a transient failure on our side.

Without a durable handoff pattern, the endpoint would either perform all aggregate mutations synchronously (risking timeouts and duplicate processing on retry) or fire-and-forget into an in-memory queue (losing events on process restart). Neither option provides the audit trail necessary to replay or debug missed events.

## Decision

Duffel webhooks are processed through a two-phase inbox/outbox pipeline:

1. **HMAC verification** — `DuffelWebhookVerifier` validates the `X-Duffel-Signature` header using HMAC-SHA256 before touching the payload. The header format is `t=<unix-seconds>,v1=<hex>`; the signed payload is `<timestamp>.<body>`. Invalid signatures return 401 Unauthorized immediately (clarified 2026-08-22).
2. **Inbox persist** — The raw JSON body is written to `flights.webhook_inbox` with columns `(id, source, event_id, event_type, payload, received_at, processed_at)`. A unique constraint on `(source, event_id)` rejects duplicate deliveries at the database level, returning 200 to Duffel so it stops retrying.
3. **Outbox dispatch** — Within the same database transaction, a `ProcessDuffelWebhookCommand` (carrying the inbox row id) is written to Wolverine's outbox table. The endpoint commits and returns 2xx.
4. **Handler** — Wolverine delivers `ProcessDuffelWebhookCommand` asynchronously. The handler loads the inbox row, maps `event_type` to a domain event (e.g. `OrderTicketed` on `order.created.documents_issued`, `OrderCancelled` on `order.airline_initiated_change.cancelled`), appends it to the `BookingAggregate` Marten event stream via the real Wolverine outbox, and sets `processed_at` on the inbox row.

The `source` column is populated with `"duffel"` today; the schema is ready to accommodate future webhook providers (e.g. Travelpayouts) without migration.

## Alternatives Considered

### Option A: Synchronous processing inside the HTTP endpoint

The endpoint verifies, maps, and applies the domain event to the aggregate before returning 2xx — no inbox or outbox tables.

Rejected because: Duffel expects a fast 2xx; aggregate work (Marten event append, optimistic concurrency retry) can take tens to hundreds of milliseconds and may spike under load. A slow or failed response causes Duffel to retry, and without deduplication each retry produces a duplicate domain event. There is also no audit trail of raw payloads for debugging or replay.

### Option B: Inbox only, no outbox

Persist the raw payload to `webhook_inbox`, commit, then publish `ProcessDuffelWebhookCommand` in a separate step after the HTTP response is sent.

Rejected because: a process crash between the commit and the publish silently drops the event — the inbox row exists but no command is ever dispatched, and there is nothing to trigger a retry. The Wolverine outbox eliminates this gap by making persistence and dispatch atomic within the same transaction; the relay guarantees eventual delivery even after a restart.

## Consequences

### Positive
- **At-least-once from Duffel becomes exactly-once in the domain** — the unique constraint absorbs duplicate deliveries; the idempotent handler guards against duplicate processing of the same inbox row.
- **Fast endpoint** — the HTTP handler does HMAC verification + one INSERT + outbox write, well within Duffel's timeout.
- **Audit and replay** — `webhook_inbox` retains the full raw payload indefinitely; a failed or missed processing run can be replayed by clearing `processed_at` and re-enqueuing.
- **Pattern reuse** — any future webhook source needs only a new `source` value and a corresponding handler; the inbox table and outbox infrastructure are already in place.

### Negative / Trade-offs
- Additional schema surface (`webhook_inbox` + Wolverine outbox tables) that must be migrated and monitored.
- The handler is eventually consistent; domain events are not appended synchronously with the HTTP response. Callers querying aggregate state immediately after a webhook may observe stale data until the handler runs.

### Neutral
- Travelpayouts currently has no webhook API; the `source` column is forward-compatible but adds no immediate operational cost.

## References

- ADR 0015: `docs/adr/0015-booking-aggregate-event-model.md` — BookingAggregate event stream design
- ADR 0003: `docs/adr/0003-wolverine-marten-stack.md` — Wolverine outbox and Marten event store selection
- Duffel Webhooks documentation: https://duffel.com/docs/api/webhooks
