# 0015. BookingAggregate Event Model and State Machine

**Date:** 2026-05-13
**Status:** Accepted
**Deciders:** M1 design author

> **Amended 2026-05-16** — Ratified D8 (WS9 remediation): `OfferHeld` carries a *singular*
> `PassengerInfo Passenger`, not an array. Multi-passenger support in M2 requires an event-schema
> evolution (`OfferHeld_V2` or a migration), which is accepted as the M2 design challenge.

> **Amended 2026-09-16 — WS4 booking consistency.** Transition rules now live in pure typed
> `BookingAggregate` decisions (`Allowed`, `IdempotentNoOp`, `Rejected`) consumed by both HTTP
> commands and durable webhook handlers. `OfferHeld` records the authenticated owner as an optional
> trailing field, and `OfferReQuoted` may carry the refreshed Core offer snapshot. Both changes keep
> the existing event identities and preserve replay of legacy payloads.

## Context

The booking lifecycle for a single flight order involves multiple external calls (Duffel offer refresh, hold, payment, confirmation, ticketing webhook) and can be interrupted at any step. The system must be able to answer, at any point: what is the current state of this booking, what happened to it, and why? This is a debugging and operational requirement as much as a domain one.

The Travel Platform concept (§4.5) explicitly designates event sourcing (ES) for booking aggregates. The Foundation stack (ADR 0007) establishes Marten as the event store backed by PostgreSQL. The question for M1 is: which events, which state machine, and what deferred concerns are explicitly out of scope.

A booking in M1 involves a single passenger. Future milestones (M2) add multi-passenger, multi-leg, and saved traveler profiles, which will require revisiting how passenger identity is stored and protected. M1 must not pre-build M2 scope — but it must also not make decisions that M2 cannot work around.

## Decision

`BookingAggregate` is an event-sourced Marten aggregate stored in a Guid-identity stream (`StreamIdentity.AsGuid`); the stream id is the aggregate's `Guid` id. We define eight past-tense domain events and a seven-state machine.

**Events** (all implement `IDomainEvent` from `Travel.Shared.Abstractions`):

| Event | Key fields | Trigger |
|---|---|---|
| `OfferQuoted` | `OfferId, Itinerary, TotalAmount, ExpiresAt, ProviderRef, QuotedAt` | `QuoteOfferCommand` — re-fetches from Duffel |
| `OfferReQuoted` | `OfferId, OldAmount, NewAmount, ReQuotedAt, RefreshedOffer?` | Repeated quote after expiry or price change |
| `OfferHeld` | `OrderId, Passenger (singular PassengerInfo), HeldUntil, HeldAt, OwnerUserId?` | Authenticated `HoldOfferCommand` after Duffel hold succeeds |
| `PaymentAuthorized` | `PaymentRef, Amount, AuthorizedAt` | `IPaymentGateway.AuthorizeAsync` returns `Ok` |
| `OrderConfirmed` | `OrderId, ConfirmedAt, PaymentRef` | `ConfirmOrderCommand` after capture succeeds |
| `OrderTicketed` | `TicketNumbers[], TicketedAt` | Duffel webhook `order.created.documents_issued` |
| `OrderCancelled` | `CancelledAt, Reason (User/Airline/System)` | `CancelOrderCommand` or Duffel cancellation webhook |
| `OrderRefunded` | `RefundRef, RefundedAmount, RefundedAt, InitiatedBy` | Duffel webhook `order.airline_initiated_change.cancelled` |

**State machine** (seven states):

```
[None] ──quote──► [OfferQuoted] ──re-quote──► [OfferQuoted]
                       │
                   hold│
                       ▼
                   [Held] ──cancel──► [Cancelled]
                       │
                  confirm│
                       ▼
                 [Confirmed]
                       │
                  webhook│
                       ▼
                  [Ticketed]
                       │
           airline cancel webhook│
                       ▼
                  [Refunded]
```

`Apply` methods remain unconditional pure replay functions: historical streams are never judged by
today's command rules. New transitions are decided by `BookingAggregate` and return a typed value;
Application maps that value to HTTP errors or durable-message failure semantics. Handlers do not
duplicate status guards.

The accepted WS4 command matrix is:

- re-quote is allowed only from `OfferQuoted` and for the same provider offer reference;
- hold is allowed only from an unexpired `OfferQuoted` state; a successful hold records the owner;
- confirm is allowed only from an unexpired `Held` state and requires the recorded matching owner;
- cancel is allowed from `Held` or `Confirmed`, is an owned idempotent no-op from `Cancelled` or
  `Refunded`, and is rejected from `Ticketed` or other states;
- ticket is allowed from `Confirmed`; terminal duplicate callbacks are no-ops and a callback from
  `Held` waits for the confirmation prerequisite;
- refund is allowed from `Confirmed` or `Ticketed`; `Cancelled`/`Refunded` callbacks are no-ops and
  an early `Held` callback waits for its prerequisite.

`Cancelled` and `Refunded` are terminal. `Ticketed` rejects user cancellation but may still progress
to `Refunded` from an airline cancellation callback.

**PII storage in M1:** `PassengerInfo` (given name, family name, date of birth, gender, email, phone) is stored plaintext inside the `OfferHeld` event payload and in the `flights.order_read_model.passenger_info_json` column. Field-level encryption is explicitly deferred to M2, when saved traveler profiles and the encryption key management strategy will be designed together. The M1 sandbox README carries a disclosure that passenger data is unencrypted.

**M1 passenger cardinality (D8):** `OfferHeld` carries a *singular* `PassengerInfo Passenger` field (not an array). This was ratified in remediation decision D8: the forward-compat array was pre-YAGNI and complicated the `PassengerInfo.Create` signature. Multi-passenger support in M2 will require an event-schema evolution (a new `OfferHeld_V2` event type or a migration), which is accepted as the M2 design challenge.

**Ownership and legacy events (WS4):** new `OfferHeld` events record `OwnerUserId`. An ownerless
quoted stream may acquire its owner at Hold. Legacy ownerless held/order streams remain replayable,
but Confirm/Cancel and non-no-op ticket/refund processing fail closed; ownership is not inferred from
the EF read model or passenger data. No historical event backfill is part of this decision.

**Re-quote replay (WS4):** new `OfferReQuoted` events carry the complete refreshed `BookableOffer`
used by the response and the next Hold. Legacy events without that optional snapshot retain their
historical amount-only behavior; no expiry or provider reference is synthesized during replay.

## Alternatives Considered

### Option A: EF Core entity with a status column and an audit log table

A `FlightOrder` entity with a `Status` enum column, updated in place. Each state transition writes a row to an `order_audit_log` table containing the previous state, new state, timestamp, and actor.

Rejected because it loses the event-sourcing property that makes the booking saga debuggable and replayable. An audit log captures what changed but not the full context (offer price at time of quoting, exact passenger payload, payment reference) needed to reconstruct the saga's reasoning. The concept (§4.5) mandates ES for bookings; the audit-log pattern is a weaker approximation that creates two sources of truth (entity state and log). Marten's stream replay gives us a genuine time machine without additional effort.

### Option B: Marten ES with more granular events

Keep Marten ES but subdivide the eight events further — for example, separate `PassengerInfoAttached`, `OfferReserved` (before Duffel hold call), `PaymentInitiated` (before `PaymentAuthorized`), and so on, resulting in twelve or more events.

Rejected because M1 involves a single-passenger flow with straightforward compensation paths. Granularity beyond the eight chosen events adds events that convey internal implementation steps rather than meaningful domain state changes. The eight events map directly onto the business concepts that domain experts reason about ("the offer was held", "the payment was authorised", "the order was ticketed"). The finer granularity would need revisiting when M2 introduces multi-passenger and multi-leg flows anyway; we choose the right semantic level for M1 and let M2 extend it.

## Consequences

### Positive
- The full booking history is queryable by replaying the Marten stream for any `BookingAggregate` instance; no separate audit table is required.
- The durable Flights reconciler rebuilds the EF `order_read_model` from Marten events during exclusive maintenance; validation and reset do not change the event log.
- The eight events and their field sets map cleanly to M1's single-passenger scope. `OfferHeld.Passenger` is a singular `PassengerInfo`; M2 multi-passenger support will require an event-schema evolution (new event version), which is accepted and planned.
- A single typed decision matrix is reused by command and webhook orchestration, preventing handlers
  from silently diverging on terminal, expiry, and prerequisite behavior.

### Negative / Trade-offs
- PII (passenger name, email, phone, date of birth) is stored plaintext in the `OfferHeld` event payload and in the read model for the M1 milestone. This is a deliberate and time-boxed risk: M1 is a sandbox showcase with no real passengers, and M2 will introduce field-level encryption. The risk is acknowledged in the README and this ADR. Any attempt to use M1 with real production traffic before the M2 encryption work is complete would be a policy violation.
- `Apply` methods deliberately accept historical sequences without consulting current transition
  policy. Every new write path must call the aggregate decision before side effects and append; this
  separation is enforced by focused decision and orchestration tests.
- Additive optional event fields make old payloads readable by the new model, but legacy ownerless
  order streams cannot safely accept new owned operations without an explicit data decision.

### Neutral
- The `OrderRefunded` event in M1 is triggered only by an airline-initiated Duffel webhook. User-initiated refund flows (M3) will append the same event type; `InitiatedBy` field distinguishes the trigger. No schema change will be required when M3 refund flows are implemented.

## Out of Scope

- Field-level encryption for `PassengerInfo` — deferred to M2 alongside the saved traveler feature design.
- User-initiated refund command and refund policy engine — M3 scope; only the state machine node `Refunded` and the `OrderRefunded` event are implemented in M1.
- Multi-passenger event payloads — M2 will introduce an event-schema evolution for `OfferHeld`; this ADR does not constrain the exact mechanism.
- Marten stream archival or event log pruning policy — operational concern, not an M1 decision.

## References

- Travel Platform concept: `docs/superpowers/specs/2026-05-03-travel-platform-concept.md` §4.5
- Flights M1 spec: `docs/superpowers/specs/2026-05-13-flights-m1-design.md` §3 (decision 4), §4.1, §4.2, §7, §10.3
- ADR 0007: `docs/adr/0007-storage-strategy-marten-ef-coexistence.md`
- ADR 0014: `docs/adr/0014-mixed-aggregation-bookable-deeplink.md`
- ADR 0019: `docs/adr/0019-payment-gateway-abstraction.md`
- Oskar Dudycz, *Event Sourcing for .NET Developers* — aggregate design guidelines
