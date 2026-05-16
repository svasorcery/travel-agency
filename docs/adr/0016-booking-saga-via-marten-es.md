# 0016. Booking Saga via Marten Event Stream

**Date:** 2026-05-13
**Status:** Accepted
**Deciders:** M1 design author

## Context

The flight booking workflow spans multiple steps — quote, hold, payment, confirmation, and optional cancellation — each of which calls an external provider and mutates the aggregate's state. This is a classic saga pattern: a sequence of local transactions coordinated without a distributed lock, with compensation logic if any step fails.

Two concerns arise. First, where does the saga state live? Second, who orchestrates the transitions?

A naïve approach would add a separate orchestrator object or a dedicated Wolverine `Saga` document that tracks which step the workflow has reached. But `BookingAggregate` already captures every state transition as a domain event appended to a Marten event stream. Introducing a second state carrier creates two sources of truth that must be kept in sync.

## Decision

The booking saga is not a separate orchestrator object. It **is** the `BookingAggregate` Marten event stream.

Each mutating command is handled by a small Wolverine handler (`QuoteOfferHandler`, `HoldOfferHandler`, `ConfirmOrderHandler`, `CancelOrderHandler`). The handler:

1. Opens a Marten `IDocumentSession` and replays the aggregate's event stream to obtain the current state.
2. Calls `GuardXxx()` on the aggregate to enforce valid state transitions (e.g., `GuardMustBeHeld()` before confirming). An illegal transition returns a typed `ErrorOr` error immediately — no side effects.
3. Calls the relevant external provider or payment gateway.
4. Appends the resulting domain event(s) to the stream and commits the session.

Compensation is expressed as additional events. For example, if payment capture succeeds but provider confirmation fails, the handler appends `OrderCancelled(reason: System)` and calls the payment gateway's refund path — both outcomes are visible in the event log.

The EF `order_read_model` table is updated after each commit by `IOrderReadModelProjector`, providing a fast, queryable snapshot for the read side.

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
- **Small handlers** — each handler does one thing; unit-testing a single transition requires only a fake session and fake provider.
- **Compensation is just events** — refunds, system cancellations, and retries all produce events that the same projection and query path handles uniformly.

### Negative / Trade-offs
- **"The saga" is spread across four handler files** rather than a single orchestrator class. Developers must read across multiple files to understand the full workflow. Mitigated by consistent naming conventions (`*Handler.cs`) and by `BookingAggregate.Guard*` methods acting as the single authoritative source of legal transition rules.
- **No built-in retry / step re-entry** — Wolverine message retries re-run the entire handler, not just the failed step. Handlers must be designed with this in mind (idempotent provider calls, idempotency keys passed to gateways).

### Neutral
- Marten's `AggregateStreamAsync` replays all events on each handler invocation. For a typical booking with fewer than 10 events this cost is negligible; if stream length ever grows materially, a Marten inline projection or snapshot can be added without changing the handler contracts.

## References

- ADR 0015: `docs/adr/0015-booking-aggregate-event-model.md` — BookingAggregate event stream design
- ADR 0003: `docs/adr/0003-wolverine-marten-stack.md` — Wolverine and Marten stack selection
