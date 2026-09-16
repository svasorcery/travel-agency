# 0016. Booking Saga via Marten Event Stream

**Date:** 2026-05-13
**Status:** Accepted
**Deciders:** M1 design author

> **Amended 2026-09-16 — WS4 booking consistency, transition/write boundary.** Existing-stream
> writers load with `FetchForWriting`, consume the aggregate's typed transition and ownership
> decisions, and commit against the loaded stream version. HTTP commands map a stale write to 409;
> durable webhook handlers throw a classified conflict so a fresh delivery scope reloads and
> re-decides. This amendment does not yet cut production handlers over to the WS4 reconcile pipeline.

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

Compensation is expressed as additional events. For example, if payment capture succeeds but provider confirmation fails, the handler appends `OrderCancelled(reason: System)` and calls the payment gateway's refund path — both outcomes are visible in the event log.

At this amendment checkpoint, the legacy `IOrderReadModelProjector` still updates EF after the
Marten commit. WS4 Task 5 introduces and tests the atomic event-plus-reconcile primitive separately;
the production cutover is a later semantic amendment. The command response is already constructed
from command-owned state rather than a post-command EF query.

For webhook processing, EF `processed_at` is a later idempotent acknowledgement, not part of a
cross-ORM transaction. Marten event and sibling notification commit first. If processing fails after
that commit, the inbox remains unprocessed; a fresh retry sees the advanced stream, takes the approved
terminal no-op, and then marks the inbox processed without duplicating the event or notification.

## Alternatives Considered

### Option A: Wolverine stateful `Saga` type

Wolverine supports a first-class `Saga` abstraction that persists its own state document (in Marten or a relational table) and routes messages by correlation id.

Rejected because: the saga document would duplicate state already captured in the `BookingAggregate` event stream. Keeping two representations consistent under failure introduces exactly the dual-write problem the event-sourced aggregate is designed to eliminate. The Wolverine saga abstraction also adds a code ceremony overhead (saga class, `Start`/`Handle` method conventions) that provides no advantage here since all routing is straightforward sequential flow.

### Option B: Dedicated orchestrator service object

A single `BookingOrchestrator` class holds all saga logic, calling handlers in sequence and managing compensation itself.

Rejected because: the handlers and the aggregate's `Guard*` methods already express the workflow. A separate orchestrator layer adds indirection without isolating anything meaningful — it would essentially re-implement what the event stream and the handler chain already do, while hiding the individual steps behind a monolithic class that is harder to test in isolation.

## Consequences

### Positive
- **Complete audit trail** — every state transition is a persisted, immutable event; time-travel debugging is available out of the box via Marten's event store.
- **Rebuildable read model** — the EF `order_read_model` table can be dropped and rebuilt at any time by replaying the event stream through `IOrderReadModelProjector`.
- **Small handlers** — each handler orchestrates one aggregate decision and side-effect boundary;
  transition rules are not reimplemented in handler-local status checks.
- **Compensation is just events** — refunds, system cancellations, and retries all produce events that the same projection and query path handles uniformly.

### Negative / Trade-offs
- **"The saga" is spread across command and webhook handler files** rather than a single
  orchestrator class. The aggregate's typed decision matrix is the authoritative transition source;
  handlers still own ordering of external side effects and persistence.
- **No built-in retry / step re-entry** — Wolverine message retries re-run the entire handler, not just the failed step. Handlers must be designed with this in mind (idempotent provider calls, idempotency keys passed to gateways).
- The EF inbox acknowledgement is intentionally after the Marten commit. Monitoring/retry policy
  must keep a failed acknowledgement visible until a later no-op delivery completes it.

### Neutral
- Marten's `AggregateStreamAsync` replays all events on each handler invocation. For a typical booking with fewer than 10 events this cost is negligible; if stream length ever grows materially, a Marten inline projection or snapshot can be added without changing the handler contracts.

## References

- ADR 0015: `docs/adr/0015-booking-aggregate-event-model.md` — BookingAggregate event stream design
- ADR 0003: `docs/adr/0003-wolverine-marten-stack.md` — Wolverine and Marten stack selection
