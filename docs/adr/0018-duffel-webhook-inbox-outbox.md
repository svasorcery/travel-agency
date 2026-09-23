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

> **Amended 2026-09-16 — WS4 booking write consistency.** Webhook transition decisions and writes
> now share one expected-version Marten boundary. The EF inbox `processed_at` flag is explicitly a
> later acknowledgement; a failure after the Marten commit is recovered by reloading, taking the
> aggregate's terminal no-op, and marking the inbox without a duplicate domain event or notification.

## Context

Duffel delivers order lifecycle events (e.g. `order.created`, `order.updated`, `order.cancelled`) as HTTP webhooks to `POST /webhooks/duffel`. The endpoint must return a 2xx response within Duffel's timeout window (typically a few seconds), regardless of how long downstream aggregate work takes. At the same time, each webhook must be processed exactly once even if Duffel retries after a transient failure on our side.

Without a durable handoff pattern, the endpoint would either perform all aggregate mutations synchronously (risking timeouts and duplicate processing on retry) or fire-and-forget into an in-memory queue (losing events on process restart). Neither option provides the audit trail necessary to replay or debug missed events.

## Decision

Duffel webhooks are processed through a two-phase inbox/outbox pipeline:

1. **HMAC verification** — `DuffelWebhookVerifier` validates the `X-Duffel-Signature` header using HMAC-SHA256 before touching the payload. The header format is `t=<unix-seconds>,v1=<hex>`; the signed payload is `<timestamp>.<body>`. Invalid signatures return 401 Unauthorized immediately (clarified 2026-08-22).
2. **Inbox persist** — The raw JSON body is written to `flights.webhook_inbox` with columns `(id, source, event_id, event_type, payload, received_at, processed_at)`. A unique constraint on `(source, event_id)` rejects duplicate deliveries at the database level, returning 200 to Duffel so it stops retrying.
3. **Outbox dispatch** — Within the same database transaction, a `ProcessDuffelWebhookCommand` (carrying the inbox row id) is written to Wolverine's outbox table. The endpoint commits and returns 2xx.
4. **Handler** — Wolverine delivers `ProcessDuffelWebhookCommand` asynchronously. The handler loads
   the inbox row, resolves its provider-order correlation, then loads the stream with
   `FetchForWriting`. The aggregate decides ticket/refund against that loaded state. An allowed
   `order.created` appends `OrderTicketed` and its existing notification through the enrolled Marten
   outbox; an allowed `order.airline_initiated_change.cancelled` appends `OrderRefunded`. Both
   paths use `SaveBookingWithReconcileAsync` to atomically include `ReconcileOrderReadModel`. Only after
   the Marten commit or an approved terminal no-op does the handler set EF `processed_at`.

The source stream, not the EF read model, supplies the owner for a new ticket/refund transition.
Ownerless legacy order streams fail closed without an event, message, or processed acknowledgement.
Missing provider-order correlation and transition prerequisites remain unacknowledged and surface as
classified failures for the module's durable retry policy. An optimistic write conflict also escapes
as a classified failure so the retry receives a fresh scope/session and re-evaluates the decision.

The `source` column is populated with `"duffel"` today; the schema is ready to accommodate future webhook providers (e.g. Travelpayouts) without migration.

## Amendment (2026-09-22): versioned read-model cutover

The webhook no longer invokes a synchronous EF projector. The durable reconciler is the sole
production projection writer. Ticket notifications carry the exact loaded stream version plus
one, captured before append. Event, notification and reconcile message roll back together on a
losing expected-version write. Refund appends reconcile without introducing a new notification.

Missing provider-order correlation still raises a bounded-retry dependency failure and leaves
ProcessedAt empty. Disposable tests prove both a ticket callback and a direct refund callback
against a Confirmed stream recover after the missing EF row is reconstructed. Refund does not
require a ticket callback. A failure during the later inbox acknowledgement retries against the
advanced stream and reaches the approved terminal no-op without duplicate events/messages.

Notification consumers wait for EF to reach the required version before effects; legacy null
versions capture current Marten version. Current-state suppression and per-connection SSE
monotonicity are specified in ADR 0016. SSE remains best effort, not durable replay. The old/new
projection writers cannot coexist during rollout; the version column alone does not protect
against old writers that ignore it. The Task 11 operator recovery procedure is documented in
`docs/operations/booking-read-model-recovery.md`; live rollout remains a separate gate.

## Alternatives Considered

### Option A: Synchronous processing inside the HTTP endpoint

The endpoint verifies, maps, and applies the domain event to the aggregate before returning 2xx — no inbox or outbox tables.

Rejected because: Duffel expects a fast 2xx; aggregate work (Marten event append, optimistic concurrency retry) can take tens to hundreds of milliseconds and may spike under load. A slow or failed response causes Duffel to retry, and without deduplication each retry produces a duplicate domain event. There is also no audit trail of raw payloads for debugging or replay.

### Option B: Inbox only, no outbox

Persist the raw payload to `webhook_inbox`, commit, then publish `ProcessDuffelWebhookCommand` in a separate step after the HTTP response is sent.

Rejected because: a process crash between the commit and the publish silently drops the event — the inbox row exists but no command is ever dispatched, and there is nothing to trigger a retry. The Wolverine outbox eliminates this gap by making persistence and dispatch atomic within the same transaction; the relay guarantees eventual delivery even after a restart.

## Consequences

### Positive
- **At-least-once delivery produces one accepted domain transition** — the unique constraint absorbs
  duplicate provider event ids, while expected-version writes plus terminal no-op decisions protect
  distinct/replayed inbox deliveries for the same transition.
- **Fast endpoint** — the HTTP handler does HMAC verification + one INSERT + outbox write, well within Duffel's timeout.
- **Audit and replay** — `webhook_inbox` retains the full raw payload indefinitely; a failed or missed processing run can be replayed by clearing `processed_at` and re-enqueuing.
- **Pattern reuse** — any future webhook source needs only a new `source` value and a corresponding handler; the inbox table and outbox infrastructure are already in place.

### Negative / Trade-offs
- Additional schema surface (`webhook_inbox` + Wolverine outbox tables) that must be migrated and monitored.
- The handler is eventually consistent; domain events are not appended synchronously with the HTTP response. Callers querying aggregate state immediately after a webhook may observe stale data until the handler runs.
- Inbox acknowledgement is not atomic with the Marten write. This deliberate ordering can repeat
  handler execution after a crash, but the fresh aggregate decision prevents a second event or
  notification and allows the acknowledgement to converge.

### Neutral
- Travelpayouts currently has no webhook API; the `source` column is forward-compatible but adds no immediate operational cost.

## References

- ADR 0015: `docs/adr/0015-booking-aggregate-event-model.md` — BookingAggregate event stream design
- ADR 0003: `docs/adr/0003-wolverine-marten-stack.md` — Wolverine outbox and Marten event store selection
- Duffel Webhooks documentation: https://duffel.com/docs/api/webhooks
