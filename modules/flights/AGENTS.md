# Flights module

Flights M1 backend is implemented. A complete booking frontend remains a separate milestone.

## Implemented scope

- Mixed search: bookable Duffel offers plus Travelpayouts deeplinks; NL-search; quote, hold, confirm, and cancel; get/list; Duffel webhook; SSE/email notifications; idempotency; cache and FX normalization; health checks; and telemetry.
- `BookingAggregate` is Marten event-sourced. Its states are `None`, `OfferQuoted`, `Held`, `Confirmed`, `Ticketed`, `Cancelled`, and `Refunded`.
- Current stream events are `OfferQuoted`, `OfferReQuoted`, `OfferHeld`, `PaymentAuthorized`, `OrderConfirmed`, `OrderTicketed`, `OrderCancelled`, and `OrderRefunded`.
- Core ports are `IFlightSearchProvider`, `IFlightBookingProvider`, and `IPaymentGateway`. Provider clients, wire DTOs, and mappers stay in Infrastructure.
- Marten owns the booking stream. EF Core owns the read model, idempotency, webhook inbox, and deeplink cache.

## Conventions and boundaries

Ordinary Wolverine handlers use Host transaction defaults. The five booking writers use the explicit `NonTransactional` Marten commit path so they can return a 409 for an optimistic conflict; `SaveBookingWithReconcileAsync` appends domain events, a durable reconcile message, and sibling notifications through one enrolled Marten outbox. EF read-model updates are eventual. Reserve explicit `IDbContextOutbox<FlightsDbContext>` for a boundary that must translate a meaningful EF race, such as duplicate webhook delivery, to HTTP behavior.

Cohesive DTO and query records may share a file. The one-handler/endpoint-per-file convention still applies.

`Travel.Host` owns global Marten/Wolverine builders, transport policy, middleware order and one Wolverine endpoint mapping. `Flights.Api.Composition` contributes module services, handlers, routes, authorization, telemetry, persistence configuration and the `IFxRates` Wolverine service-location policy. Non-Composition Api types must not depend on Infrastructure.

Tests: [unit](../../tests/flights/Travel.Modules.Flights.Tests.Unit), [integration](../../tests/flights/Travel.Modules.Flights.Tests.Integration), and [contract](../../tests/Travel.Tests.Contract/Flights).

M1 limits: one passenger, a non-Production test wallet, and no real booking UI.
