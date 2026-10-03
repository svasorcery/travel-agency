# 0016. Booking Saga via Marten Event Stream

**Date:** 2026-05-13
**Status:** Accepted
**Deciders:** M1 design author

> **Amended 2026-09-16 — WS4 booking consistency, transition/write boundary.** Existing-stream
> writers load with `FetchForWriting`, consume the aggregate's typed transition and ownership
> decisions, and commit against the loaded stream version. HTTP commands map a stale write to 409;
> durable webhook handlers throw a classified conflict so a fresh delivery scope reloads and
> re-decides. The later Task 9 amendment below completes the production write-path cutover.

## Context

The flight booking workflow spans multiple steps — quote, hold, payment, confirmation, and optional cancellation — each of which calls an external provider and mutates the aggregate's state. This is a classic saga pattern: a sequence of local transactions coordinated without a distributed lock, with compensation logic if any step fails.

Two concerns arise. First, where does the saga state live? Second, who orchestrates the transitions?

A naïve approach would add a separate orchestrator object or a dedicated Wolverine `Saga` document that tracks which step the workflow has reached. But `BookingAggregate` already captures every state transition as a domain event appended to a Marten event stream. Introducing a second state carrier creates two sources of truth that must be kept in sync.

## Decision

The booking saga is not a separate orchestrator object. It **is** the `BookingAggregate` Marten event stream.

Each mutating command is handled by a small Wolverine handler (`QuoteOfferHandler`, `HoldOfferHandler`, `ConfirmOrderHandler`, `CancelOrderHandler`). The handler:

1. Opens a Marten `IDocumentSession` with `FetchForWriting` and replays the aggregate's event stream
   while capturing the expected stream version.
2. Calls the aggregate's typed ownership and transition decisions before returning state or invoking
   a provider. Application maps `Rejected` to `ErrorOr` for HTTP commands or to a classified durable
   exception for background processing; an approved `IdempotentNoOp` appends nothing.
3. Calls the relevant external provider or payment gateway.
4. Appends the resulting domain event(s) and any existing sibling notification to the enrolled
   Marten outbox, then commits against the loaded version.

M2.3a removes automatic compensation on capture/provider uncertainty. Such a failure is not proof of cancellation or refund: the handler returns a safe unknown outcome, appends no terminal events and leaves the last committed aggregate Held. A future recovery design must establish the external outcome before recording compensation.

Every successful booking commit now uses `SaveBookingWithReconcileAsync`: quote, re-quote,
hold, successful confirmation, cancellation, ticketing and airline-initiated refund. One enrolled
Marten outbox commits the events, `ReconcileOrderReadModel`, and existing sibling notifications
atomically. These five explicit-commit handlers opt out of Wolverine's automatic transaction
middleware with `NonTransactional`: otherwise a caught write conflict is followed by a second
generated SaveChanges call instead of returning 409. This is a local boundary choice; ordinary
handlers and Host transaction defaults remain unchanged. HTTP commands still map optimistic conflicts to 409. Command responses use the
provider result or command-owned aggregate snapshot and never query EF after the write.

The synchronous EF projector has been removed. The durable reconciler applies ordered events
and saves derived fields with `ProjectedStreamVersion` in one EF transaction. Quote/re-quote
remain non-materialized; successful Hold makes an owned order visible through eventual GET/List.
There can be a period after a successful command when EF is missing or behind.

Notification envelopes carry an optional `RequiredStreamVersion` for compatibility with old
persisted JSON. New senders record the exact loaded version plus the number of appended events,
captured before appending. A missing/behind EF row raises `BookingReadModelNotReadyException`
before any email or SSE effect. A legacy null version captures the current Marten stream version;
explicit zero or negative versions are terminal-invalid. Readiness uses a fresh EF context and
does not project or repair data. Scoped bounded retry/DLQ ownership remains in ADR 0023.

Confirmation email is sent only for current Confirmed/Ticketed; cancellation email only for
current Cancelled. Obsolete messages are suppressed. A delayed confirmation SSE observing Ticketed
emits OrderTicketed; ticket SSE emits only for Ticketed and cancellation SSE only for Cancelled.
Cancelled/Refunded suppress confirmation/ticket SSE; no refund event is introduced.

SSE JSON carries `streamVersion`. Each active connection serializes version comparison, nonblocking
channel enqueue and last-enqueued-version advancement under its own lock. Older/equal events are
coalesced. A full channel or byte budget completes the slow connection; the endpoint drains and
exits on reader completion and releases registration in finally. It retains at most one pending
heartbeat wait and one reader wait. Version state ends with the connection: reconnect uses GET,
with no replay, Last-Event-ID, cross-connection monotonicity or cross-node delivery guarantee.
SMTP remains at-least-once and may duplicate after a crash; SSE is best effort for an active connection.

Do not run old EF projectors alongside versioned writers. An old SQL update can overwrite derived
fields while retaining a current checkpoint; incremental delivery cannot infer that corruption.
The mixed-writer test demonstrates it and read-only validation detects the mismatch. Deployment
must stop old writers and separately validate schema and historical ownership/checkpoints before
cutover. The Task 10–11 amendment and recovery evidence below now describe exclusive maintenance tooling and convergence proof.
No live rollout or shared-data repair is implied by these source changes.

For webhook processing, EF `processed_at` is a later idempotent acknowledgement, not part of a
cross-ORM transaction. Marten event, reconciliation request and sibling notification commit first. If processing fails after
that commit, the inbox remains unprocessed; a fresh retry sees the advanced stream, takes the approved
terminal no-op, and then marks the inbox processed without duplicating the event or notification.

## Alternatives Considered

### Option A: Wolverine stateful `Saga` type

Wolverine supports a first-class `Saga` abstraction that persists its own state document (in Marten or a relational table) and routes messages by correlation id.

Rejected because: the saga document would duplicate state already captured in the `BookingAggregate` event stream. Keeping two representations consistent under failure introduces exactly the dual-write problem the event-sourced aggregate is designed to eliminate. The Wolverine saga abstraction also adds a code ceremony overhead (saga class, `Start`/`Handle` method conventions) that provides no advantage here since all routing is straightforward sequential flow.

### Option B: Dedicated orchestrator service object

A single `BookingOrchestrator` class holds all saga logic, calling handlers in sequence and managing compensation itself.

Rejected because: the handlers and the aggregate's typed transition decisions already express the workflow. A separate orchestrator layer adds indirection without isolating anything meaningful — it would essentially re-implement what the event stream and the handler chain already do, while hiding the individual steps behind a monolithic class that is harder to test in isolation.

## Consequences

### Positive
- **Complete audit trail** — every state transition is a persisted, immutable event; time-travel debugging is available out of the box via Marten's event store.
- **Rebuildable read model** — the shared event-applier pipeline supports validation and exclusive reset from the Marten stream; operational maintenance tooling is a separate WS4 task.
- **Small handlers** — each handler orchestrates one aggregate decision and side-effect boundary;
  transition rules are not reimplemented in handler-local status checks.
- **Truthful transitions** — uncertainty does not fabricate cancellation, refund or confirmation. Only established outcomes produce their existing events.

### Negative / Trade-offs
- **"The saga" is spread across command and webhook handler files** rather than a single
  orchestrator class. The aggregate's typed decision matrix is the authoritative transition source;
  handlers still own ordering of external side effects and persistence.
- **No built-in retry / step re-entry** — Wolverine message retries re-run the entire handler, not just the failed step. HTTP booking idempotency stores completed responses, but does not make supplier effects atomic with the stream commit. Duffel mutation requests are not automatically retried; no supplier key guarantee is assumed.
- The EF inbox acknowledgement is intentionally after the Marten commit. Monitoring/retry policy
  must keep a failed acknowledgement visible until a later no-op delivery completes it.

### Neutral
- Existing-stream handlers replay through Marten's `FetchForWriting` while capturing the expected version. For a typical booking with fewer than 10 events this cost is negligible; if stream length ever grows materially, a Marten inline projection or snapshot can be added without changing the handler contracts.

## Task 9 evidence (2026-09-22)

Disposable tests cover each production commit shape and rollback, exact sibling versions, missing
correlation followed by reconciliation/retry, legacy envelope readiness and mixed-writer corruption.
Unit and HTTP tests cover notification coalescing, connection monotonicity, bounded buffers, reader
completion, heartbeat and command-owned responses. These are local source/integration evidence,
not live validation or the remaining WS4 recovery/maintenance acceptance.

## Amendment (2026-09-23): exclusive maintenance and operator recovery

Task 11 provides Application catalog, rebuild and diagnostic ports behind the Flights
Api facade and the existing Host CLI. Host owns process-store configuration in two
mutually exclusive entry paths. Maintenance is selected before normal web/consumer
startup; its container is built but never started. It registers persistence and
diagnostics only and checks schemas without applying changes in every environment.

Validate and Reset share the production reconciler/event applier. Reset requires
`--execute --exclusive-maintenance`, retains row identity and atomically saves the
replacement. The flag acknowledges the operator's obligation to drain/stop every
normal writer; it does not implement distributed fencing. Online Reset is unsupported:
a stream-version token cannot detect a same-version repair. A failed replacement
leaves the old row intact. Source events, inbox acknowledgements and unrelated tables
are never reset. Historical ownership gaps need a separately approved data decision.

Terminal/exhausted recovery is projection-first, then exact-ID Wolverine DLQ replay
of known reconcile/webhook/notification types. User booking commands are excluded.
Derived-field mismatch requires Reset even when the checkpoint is behind, because
an event suffix cannot repair arbitrary corrupt prefix fields. Per-stream progress
and final failure/partial counts remain visible; failures produce nonzero exit codes.

The supported default is forward repair or a tested WS4-compatible artifact. Additive
JSON compatibility and an additive EF column do not certify a pre-WS4 binary: it lacks
the reconcile consumer and current ownership protections; old writers can retain a
new checkpoint while corrupting derived fields. Keep the checkpoint column during
compatible rollback. Migration Down, owner backfill and emergency pre-WS4 downgrade
are separate reviewed operations, not features of this maintenance command.

Tests use disposable PostgreSQL, production registrations and test-only failure
injection. The legacy handler-surface fixture demonstrates the missing-consumer
boundary; it is not certification of a historical deployable artifact. The concrete
commands, issue codes and operational sequence are in
[the recovery runbook](../operations/booking-read-model-recovery.md). Verification
and remaining evidence boundaries are recorded in the
[Task 11 report](../operations/2026-09-23-ws4-task11-recovery-review.md).

## Amendment (2026-10-03): bounded booking correctness

Confirmation validates the supplier order identity, accepted total, currency, unpaid state and deadline before the test wallet, then checks the total again immediately before payment. Only a matching succeeded payment receipt establishes confirmation. `Flights.OrderPriceChanged` is a pre-wallet refusal; uncertain capture or any refusal/error after capture becomes `Flights.ConfirmationOutcomeUnknown`. Supplier hold errors after possible dispatch become `Flights.HoldOutcomeUnknown`.

The existing success events and atomic Marten outbox remain. No new workflow event, migration or recovery endpoint is added. External effects still precede commit, so a lost response, conflict or crash can leave an unresolved outcome. Current-session browser memory prevents a second hold/confirm write after uncertainty, including after SPA navigation; it cannot coordinate other tabs or survive identity reset/reload. Observing Held through GET is not proof that the original operation failed. M3 recovery and real payment acceptance require separate design.

See [approved M2.3 specification](../superpowers/specs/2026-10-03-flights-m23-design.md). Local evidence uses fakes/noDB HTTP and the fictional browser demo; database/outbox behavior requires mandatory CI.

## Amendment (2026-10-03): one booking for an adult party

M2.3b retains the same saga/commit boundary and uncertainty limitations. One current quote binding defines the ordered local-to-supplier passenger mapping for 1–9 adults. A successful whole-party hold emits V3 and one reconciliation message; confirmation still uses one accepted group total. No child saga, per-passenger payment, partial success, compensation engine or recovery endpoint is introduced.

The EF projection adds explicit PassengerCount, defaults historical orders to 1, and compares count during validation/reset. Source migration must be applied through an independently authorized rollout before enabling V3 writers. V3-aware event readers, projection and prefix-owner checks must roll forward together; old readers are not a safe rollback after new writes. Original ciphertext and event history remain untouched. M3 recovery remains deferred.

## References

- ADR 0015: `docs/adr/0015-booking-aggregate-event-model.md` — BookingAggregate event stream design
- ADR 0003: `docs/adr/0003-wolverine-marten-stack.md` — Wolverine and Marten stack selection
