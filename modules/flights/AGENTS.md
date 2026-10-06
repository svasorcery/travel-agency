# Flights module

Flights M1 backend and B1–B5 UI are implemented; booking acceptance uses an isolated fictional demo. M2.1 provides deterministic explainable ranking; M2.2 protects new booking passenger/inbox data; M2.3b supports 1–9 adults with quote-bound local IDs and one protected party. M2.4 implements private encrypted saved-traveler profiles. M2.5 supports one complete offer/order with 1–4 ordered flight legs, including open-jaw; explicit-airport search/v2 and airport-local IANA timestamps coexist with compatible historical readers. The UI reviews every leg, cabin, connection and independent ground gap. See [ADR 0025](../../docs/adr/0025-flights-ordered-journeys-and-airport-time.md).

## Implemented scope

- Mixed search: bookable Duffel offers plus Travelpayouts deeplinks; NL-search; quote, hold, confirm, and cancel; get/list; Duffel webhook; SSE/email notifications; idempotency; cache and FX normalization; health checks; and telemetry.
- `BookingAggregate` is Marten event-sourced. Its states are `None`, `OfferQuoted`, `Held`, `Confirmed`, `Ticketed`, `Cancelled`, and `Refunded`.
- Current stream events are `OfferQuoted`, `OfferReQuoted`, `OfferHeld` (legacy), `OfferHeldV2` (legacy protected, singular), `OfferHeldV3` (protected party), `PaymentAuthorized`, `OrderConfirmed`, `OrderTicketed`, `OrderCancelled`, and `OrderRefunded`.
- Core ports are `IFlightSearchProvider`, `IFlightBookingProvider`, and `IPaymentGateway`. Provider clients, wire DTOs, and mappers stay in Infrastructure.
- Marten owns the booking stream. EF Core owns the read model, idempotency, webhook inbox, deeplink cache, and saved-traveler profiles.

## Conventions and boundaries

Ordinary Wolverine handlers use Host transaction defaults. The five booking writers use the explicit `NonTransactional` Marten commit path so they can return a 409 for an optimistic conflict; `SaveBookingWithReconcileAsync` appends domain events, a durable reconcile message, and sibling notifications through one enrolled Marten outbox. EF read-model updates are eventual. Reserve explicit `IDbContextOutbox<FlightsDbContext>` for a boundary that must translate a meaningful EF race, such as duplicate webhook delivery, to HTTP behavior.

Cohesive DTO and query records may share a file. The one-handler/endpoint-per-file convention still applies.

`Travel.Host` owns global Marten/Wolverine builders, transport policy, middleware order and one Wolverine endpoint mapping. `Flights.Api.Composition` contributes module services, handlers, routes, authorization, telemetry, persistence configuration and module Wolverine service-location policies. Non-Composition Api types must not depend on Infrastructure.

Tests: [unit](../../tests/flights/Travel.Modules.Flights.Tests.Unit), [integration](../../tests/flights/Travel.Modules.Flights.Tests.Integration), and [contract](../../tests/Travel.Tests.Contract/Flights).

Current limits: 1–9 adults and 1–4 flight legs in one complete offer/order, including open-jaw; no identity documents and a non-Production test wallet; no real supplier/payment acceptance. Historical orders retain their compatible readers; new creation rules do not rewrite their route or time data.

New hold commands contain a protected snapshot before bus dispatch; handlers decrypt only after guards. Projection copies ciphertext and supports V1/V2/V3 held events. New party purpose binds aggregate, owner, quote revision and count. Explicit PassengerCount metadata defaults to one for old orders; never infer it from tickets. Legacy plaintext is not rewritten. A dedicated certificate-protected key ring gates PII writes; no empty-ring bootstrap or plaintext fallback. The key-only CLI exits before Host/store registration. See [ADR 0024](../../docs/adr/0024-flights-pii-protection.md) and [key recovery](../../docs/operations/flights-pii-key-recovery.md). Never execute provisioning, schema changes or deployment without the applicable authorization.

## Flights M3 OpenSpec pilot

When starting M3 user-initiated cancellation and refunds, first read the [selected OpenSpec pilot brief](../../docs/superpowers/specs/2026-10-02-flights-cancellation-openspec-pilot-brief.md). The user selected cancellation with consent to current terms and recovery of the operation outcome after failures as the OpenSpec pilot and the example for the SDD article s03a02. Preserve this choice and restore the documented scope before planning. M3 T1–T2 tooling and authenticated harness verification are complete; canonical requirements/design/tasks are in `openspec/changes/flights-m3-cancellation`. Use the pinned repository launcher and current authorized scope. M3 source and existing-CI acceptance are complete in PR36 with consent-bound whole-order cancellation, durable recovery and explicit operator review in the same booking stream. Legacy `DuffelFlightBookingProvider.CancelOrderAsync` stays unsupported before HTTP. Actual CI37450413774 accepted head575baaf998679817af4b59150dc1afb2cf424ef6 with all13normal checks including durable recovery and E2E. Source/local proofs and CI acceptance remain distinct; merge/postmerge/archive and real supplier/payment rollout require their applicable authority.
