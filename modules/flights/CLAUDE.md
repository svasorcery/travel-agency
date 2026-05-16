# Flights module

**Status:** каркас (production-grade реализация в подпроекте 1)

## Bounded context
Поиск и бронирование авиабилетов. Mixed bookable + deeplink aggregation (Duffel + Travelpayouts).

## Aggregates
_TBD в подпроекте 1: BookingAggregate с состояниями OfferQuoted → Held → Confirmed → Ticketed → Refunded → Cancelled_

## Domain Events
_TBD в подпроекте 1_

## External integrations
_TBD в подпроекте 1: Duffel (bookable), Travelpayouts (deeplink)_

## Transaction patterns

The default pattern for Wolverine handlers is `[Transactional]` (or `Policies.AutoApplyTransactions()` in `Program.cs`): Wolverine wraps the handler in a transaction, and any `IMessageBus.PublishAsync` calls inside the handler are buffered on the outbox and flushed atomically with the DB commit — no boilerplate required.

Use `IDbContextOutbox<FlightsDbContext>` explicitly **only** when the endpoint or handler must catch a domain-meaningful DB exception (e.g. a `23505` unique-violation race) and return a 2xx response rather than letting the failure propagate through Wolverine middleware. The Duffel webhook endpoint (`DuffelWebhookEndpoint`) is the canonical example: it calls `outbox.PublishAsync` + `outbox.SaveChangesAndFlushMessagesAsync` so it can catch the `23505` duplicate-delivery case and return 200 instead of faulting the Wolverine pipeline.

Do not reach for `IDbContextOutbox<T>` reflexively — `[Transactional]` is less code, less drift surface, and is already wired by policy.

## DTO file organisation

`Contracts.cs` and `NlSearchContracts.cs` in `Travel.Modules.Flights.Api/Contracts/` intentionally bundle cohesive groups of request/response records in a single file. The one-class-per-file convention applies to Wolverine handlers (e.g. `HoldOfferEndpoint.cs`), not to DTO bundles where co-location of tightly related types improves discoverability. Similarly, `OrderReadModelQueries.cs` in `Application/Queries/` groups the query, result, and view record together for the same reason.

## Tests
- Unit:        `tests/flights/Travel.Modules.Flights.Tests.Unit/`
- Integration: `tests/flights/Travel.Modules.Flights.Tests.Integration/`
