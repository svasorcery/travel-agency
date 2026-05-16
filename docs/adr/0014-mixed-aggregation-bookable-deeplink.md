# 0014. Mixed Bookable and Deeplink Offer Aggregation

**Date:** 2026-05-13
**Status:** Accepted
**Deciders:** M1 design author

## Context

The Flights module aggregates results from Duffel (bookable offers with a full transactional lifecycle) and Travelpayouts (Aviasales affiliate deeplinks with no booking capability in our system). Both sources return flight options for the same routes and dates. The central product question is: how should these two structurally different result types coexist in the domain model and in the user-facing search result list?

The Travel Platform concept (§3.2) explicitly describes the mixed aggregation pattern — showing bookable and deeplink results together in a single ranked list — as a distinguishing product feature. It positions this pattern as uncommon among OTAs and uses it to demonstrate that the platform can be useful to users even where full transactional coverage is absent. Reflecting this product intent in the domain model is therefore an architectural requirement, not merely an implementation choice.

At the booking layer, `HoldOfferHandler` and `ConfirmOrderHandler` must be protected against receiving a deeplink offer that has no booking reference. This protection should be enforced structurally, not via runtime validation that can be omitted under refactoring pressure.

## Decision

We model the offer hierarchy as a discriminated record type in `Travel.Modules.Flights.Core/`:

- `Offer` is an `abstract record` carrying the fields shared by both types: `OfferId`, `Itinerary`, `TotalAmount`, `ProviderId`, and `FetchedAt`.
- `BookableOffer : Offer` is a `sealed record` adding `ExpiresAt`, `FareConditions`, and `ProviderOfferRef` (the Duffel `offer.id` required to hold and confirm).
- `DeeplinkOffer : Offer` is a `sealed record` adding `DeeplinkUrl` (the affiliate URL with partner marker) and `PartnerName` (e.g. `"Aviasales"`).

The search pipeline returns `IReadOnlyList<Offer>` — a mixed list of both subtypes — ranked together by the M1 default sort (price ascending, duration as tie-break). The dedup key is `(primary_carrier_code, primary_flight_number, departure_date_utc)`; when a bookable and deeplink offer collide, the bookable offer wins.

Booking handlers declare their parameter type as `BookableOffer`, not `Offer`. An attempt to pass a `DeeplinkOffer` to `HoldOfferHandler` is rejected at compile time by the C# type system.

The frontend receives the mixed list as a single `offers` array. Each element carries a discriminator field (`offer_type: "bookable" | "deeplink"`). Deeplink rows are rendered with a partner badge and a "Buy at partner →" CTA that opens the partner URL in a new tab. Bookable rows show the standard "Book" CTA.

## Alternatives Considered

### Option A: Single flat `Offer` record with an `IsBookable` flag and nullable booking fields

One record type for all offers. Fields like `ProviderOfferRef` and `ExpiresAt` are nullable. A boolean `IsBookable` (or `OfferType` enum) controls which fields are populated.

Rejected because nullability spreads the structural distinction across every consumer. Any handler or mapper that reads `ProviderOfferRef` must guard against `null`. The booking saga cannot rely on the compiler to prevent a deeplink from entering `HoldOfferHandler`; it must add a runtime guard that is easy to forget and invisible to callers. The discriminated hierarchy makes the booking saga compile-time safe without any runtime guards.

### Option B: Separate UI sections — "Book here" and "More options at partners"

Keep the same domain model split but surface the two offer types in distinct sections of the search results page rather than interleaving them in a single ranked list.

Rejected because the Travel Platform concept (§3.2) specifically describes the interleaved mixed list as the product differentiator. Separating sections fragments the user's mental model of what a "good price" looks like for a route. The ranking algorithm is provider-neutral and produces a coherent price-ordered view regardless of offer type; splitting by type defeats this and hides cheaper deeplink options below the fold.

## Consequences

### Positive
- Booking handlers are compile-time safe: `HoldOfferHandler(BookableOffer offer)` cannot receive a `DeeplinkOffer`. No runtime guard needed, no risk of omission under refactoring.
- The mixed list ranking is provider-neutral; the algorithm does not need to know offer type to compute a rank.
- Future providers slot into either lane (`BookableOffer` or `DeeplinkOffer`) by returning the appropriate subtype from their `IFlightSearchProvider.SearchAsync` implementation.
- The UX delivers the concept's "mixed list" showcase pattern: users see all competitive prices in one view, with clear provenance badges distinguishing the two purchase paths.

### Negative / Trade-offs
- The C# inheritance hierarchy for records is unconventional; `abstract record` with `sealed` subtypes is less common than interface-based polymorphism. Developers unfamiliar with record inheritance may introduce a base `Offer` parameter type accidentally, bypassing the type-safety guarantee. Code review and ArchUnitNET rules should guard against this.
- JSON serialisation of the discriminated hierarchy requires a `[JsonDerivedType]` attribute or a custom converter to preserve the concrete type through the Redis cache and API response. This is a small but non-zero maintenance surface.

### Neutral
- The `offer_type` discriminator in the API response is forward-compatible: if a third offer category is introduced (e.g., a hotel-bundle offer in a future milestone), a new `offer_type` value extends the contract without breaking existing consumers that ignore unknown values.

## Out of Scope

- Ranking weights beyond price and duration — explainable multi-factor ranking is explicitly deferred to M2. This ADR does not constrain the ranking algorithm's future evolution.
- Ancillary offers (seat upgrades, bag add-ons) — these are M3 scope and may introduce additional `Offer` subtypes. Whether they extend this hierarchy or introduce a separate abstraction is a decision for the M3 spec.
- UI visual design details for the partner badge and CTA — defined in the frontend component spec, not in this ADR.

## References

- Travel Platform concept: `docs/superpowers/specs/2026-05-03-travel-platform-concept.md` §3.2
- Flights M1 spec: `docs/superpowers/specs/2026-05-13-flights-m1-design.md` §3 (decision 2), §4.4, §6.3
- ADR 0013: `docs/adr/0013-flights-provider-abstraction.md`
- ADR 0015: `docs/adr/0015-booking-aggregate-event-model.md`
