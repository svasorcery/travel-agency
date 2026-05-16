# 0013. Capability-Segregated Provider Interfaces for Flights

**Date:** 2026-05-13
**Status:** Accepted
**Deciders:** M1 design author

## Context

The Flights module aggregates results from two external suppliers: Duffel (a full booking API) and Travelpayouts (an Aviasales affiliate API that returns deeplink URLs, not bookable offers). At the implementation level, both providers share the ability to return a list of flight offers in response to search criteria, but only Duffel supports the downstream operations required to create, hold, and confirm an airline order.

A naive design treats all providers as interchangeable behind a single interface, relying on runtime flags or `NotSupportedException` to distinguish capabilities. This pattern is common but violates the Interface Segregation Principle: it forces every consumer to know which methods are actually available for a given provider, and it turns a structural business constraint — "Travelpayouts cannot book" — into a runtime exception that cannot be detected until the code executes.

The search pipeline in `SearchFlightsHandler` fans out across all registered providers in parallel via `IEnumerable<IFlightSearchProvider>`. The booking saga in `HoldOfferHandler` and `ConfirmOrderHandler` must work with a single bookable provider. These two access patterns require different injection semantics: multi-registration fan-out for search, singleton selection for booking.

Additionally, the M1 design introduces `IPaymentGateway` as a third capability boundary — separating payment concerns from flight provider concerns entirely. This interface has its own single M1 implementation, `DuffelTestWalletPaymentGateway`, marked `[TestOnly]`.

## Decision

We define three separate interfaces in `Travel.Modules.Flights.Core/Providers/`, each covering exactly the capabilities a caller is permitted to expect:

- `IFlightSearchProvider` — implemented by both `DuffelFlightSearchProvider` and `TravelpayoutsSearchProvider`. Exposes only `SearchAsync`. Registered as multi-instance in DI so `SearchFlightsHandler` receives `IEnumerable<IFlightSearchProvider>` for parallel fan-out.
- `IFlightBookingProvider` — implemented by `DuffelFlightBookingProvider` only. Exposes `RefreshOfferAsync`, `HoldOfferAsync`, `ConfirmOrderAsync`, `CancelOrderAsync`, and `GetOrderStatusAsync`. Registered as a single scoped instance.
- `IPaymentGateway` — exposes `AuthorizeAsync`, `CaptureAsync`, and `RefundAsync`. Sole M1 implementation is `DuffelTestWalletPaymentGateway` (`[TestOnly]`). See ADR 0019.

Travelpayouts implements only `IFlightSearchProvider` and nothing else. Its inability to book is expressed structurally — it does not implement `IFlightBookingProvider` — rather than through a throw at runtime.

Provider DTO types produced by each implementation's HTTP client never cross the `Infrastructure` boundary; each provider adapter contains its own mapper that converts external shapes to domain types (`BookableOffer` or `DeeplinkOffer`).

## Alternatives Considered

### Option A: Single `IFlightProvider` with null-method dance

One interface combining all operations. Travelpayouts implements search methods and throws `NotSupportedException` (or returns `Error.Failure`) for booking methods.

Rejected because it violates ISP by exposing a contract that callers cannot safely depend on. `NotSupportedException` in normal booking flow is an unhandled partial-implementation smell. It also prevents clean multi-registration fan-out: a handler injecting `IEnumerable<IFlightProvider>` for search would have to guard each element before invoking booking operations, spreading provider-capability knowledge through the Application layer.

### Option B: Per-provider concrete types with no interface

Inject `DuffelFlightProvider` and `TravelpayoutsSearchProvider` directly into handlers. No interface abstraction.

Rejected because the search pipeline requires fan-out across all registered providers without knowing their concrete types. Without a shared interface, `SearchFlightsHandler` cannot iterate providers polymorphically. Introducing a new provider would require modifying the handler, violating the open/closed principle. Unit testing would require replacing concrete types rather than substituting interfaces.

## Consequences

### Positive
- Travelpayouts is honestly represented as search-only; its inability to book is enforced at compile time, not discovered at runtime.
- `SearchFlightsHandler` receives `IEnumerable<IFlightSearchProvider>` and fans out uniformly across all registered providers without provider-specific branching.
- `HoldOfferHandler` and `ConfirmOrderHandler` accept only `IFlightBookingProvider`; adding a second bookable provider requires implementing the interface and updating DI registration, with no changes to saga logic.
- New search-only or bookable providers slot into the correct interface lane without touching existing handlers.

### Negative / Trade-offs
- Duffel's functionality is split across two interfaces (`IFlightSearchProvider` and `IFlightBookingProvider`). The concrete `DuffelFlightSearchProvider` and `DuffelFlightBookingProvider` share a `DuffelClient` singleton but are separate types. Maintaining two implementations for one external supplier adds surface area and requires keeping them consistent when the Duffel API version is pinned or changed.

### Neutral
- The pinned Duffel API version (`Duffel-Version: v2`) is configured in `appsettings.json` and referenced in both Duffel adapter classes; a version upgrade requires touching two files.

## Out of Scope

- How providers are selected when multiple `IFlightBookingProvider` implementations exist in a future milestone — DI strategy for multi-bookable-provider scenarios is deferred to the milestone that introduces a second bookable supplier.
- Rate-limiting and retry policies per provider — governed by the Polly configuration in each adapter, not by this interface boundary decision.
- Currency conversion from provider-native currencies to the user's requested currency — handled in the search pipeline after fan-out, not within the provider interface contract.

## References

- Flights M1 spec: `docs/superpowers/specs/2026-05-13-flights-m1-design.md` §3 (decision 1), §5
- ADR 0019: `docs/adr/0019-payment-gateway-abstraction.md`
- ADR 0014: `docs/adr/0014-mixed-aggregation-bookable-deeplink.md`
- Robert C. Martin, *Agile Software Development* — Interface Segregation Principle
