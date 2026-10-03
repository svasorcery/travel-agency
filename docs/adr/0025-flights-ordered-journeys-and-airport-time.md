# 0025. Ordered Flights journeys and airport-local time

**Date:** 2026-10-03
**Status:** Accepted
**Deciders:** user approval of the M2.5 specification and implementation plan, with an additional pre-implementation self-review

## Context

M1 represented one-way or mirrored return journeys. Increasing the slice limit alone would leave request construction, provider matching, cache identity, historical readers and route review inconsistent. Duffel timestamps may omit offsets and describe an airport-local clock; assuming UTC changes the actual instant and can also change the date used for adulthood.

Historical itinerary constructors allowed a mirrored return before the outbound finished. Existing events and order projections must remain readable without being judged by new creation rules. A computed HTTP journey kind cannot identify the age or version of stored history.

## Decision

Represent a request as 1–4 immutable ordered flight legs. New explicit-airport criteria use a separate route mode and `/api/flights/search/v2`; existing structured/NL requests retain their location/city semantics. Explicit local dates are nondecreasing, with equal dates permitted. This fictional product does not support a later requested local date preceding an earlier one across the date line.

One complete offer produces one quote, one protected party of 1–9 adults and one booking aggregate/order. Connections are segments inside a slice. Different airports between slices represent independent ground travel outside the ticket, with no inferred feasibility or price.

Fresh itinerary creation validates every segment/slice and UTC chronology between slices. Slice duration remains positive and less than 48 hours; summed journey duration is positive and less than 192 hours. The sum includes connections within slices and excludes stays and ground travel between slices. Fresh factories defensively copy nested collections.

Resolve each supplier departure using its origin airport's IANA zone and each arrival using its destination's zone inside Infrastructure. Raw wire strings and zones do not cross that boundary. Use built-in `TimeZoneInfo`, without a host-zone or UTC fallback. Offsetless DST gaps/overlaps fail; an explicit offset must be valid for the local wall time and zone, including either valid offset during an overlap. Missing, unknown or unsupported zones fail safely. Adult age uses the resolved first origin-local calendar date.

Validate explicit results against every requested airport, local date, cabin and passenger count. An invalid nonempty supplier response is provider-unavailable, rather than successful empty inventory. Quote validates the complete refreshed offer but cannot prove correspondence to an earlier search because it has no stored search criteria; the user must review every route/cabin/time change before hold.

Normal-search cache v4 includes ordered criteria, route mode and the sorted current provider capability inventory. Current support is evaluated before cache lookup. Cold/warm validation checks the complete fresh graph, totals, first-local-date party binding and current provider/skips/failure identities. Old v3 entries miss and expire naturally; no flush or migration. Travelpayouts is skipped before HTTP for explicit journeys with `journey-unsupported`; its existing startup enable/registration contract remains.

Derive `one-way`, `round-trip` or `multi-leg` from geometry. Core kind is computed and `JsonIgnore`; HTTP publishes additive `journeyKind`. Keep existing private JSON constructors, event identities and Apply methods unconditional. Never rewrite historical timestamps/durations or invoke fresh creation factories while reading old orders, including the historical flat-DTO fallback. Shared TS readers validate shape/geometry consistently across search, quote and owned orders; fresh v2 response checks remain separate.

UI shows every flight leg, connection, cabin, recorded airport offset and independent ground gap. Any changed route fact invalidates quote acceptance even at the same price. Ranking remains free/deterministic, saved-profile fill retains slot identity/guards, and a new search cannot release an unknown-write barrier.

## Alternatives

- Increasing only the array limit leaves provider/cache/decoder contracts incomplete.
- Independent searches/orders per leg require split-ticket, payment and recovery semantics outside M2.
- Assuming UTC or choosing a DST overlap offset invents supplier facts.
- A persisted required journey-kind/version field or historical backfill is unnecessary and risks replay compatibility.
- A route catalogue, new timezone service, LLM planner or additional dependency is outside this bounded increment.

## Consequences and validation boundary

The complete backend/transport/UI increment must ship together. Compatible readers must precede new 3–4-leg writes; rolling back to old readers after such writes is unsupported. This decision does not authorize or prove deployment, real supplier/payment acceptance, schema application or key provisioning.

Pure domain, provider and cache tests; audited fake HTTP/metadata fixtures; shared TS/Angular tests; and fictional Node/Playwright acceptance establish separate source/local boundaries. Actual database, full Host, Wolverine/Marten and Aspire behavior belongs to existing mandatory CI. Delivery still requires independent review, mandatory PR E2E, merge, postmerge checks and cleanup; source readiness alone does not close M2.

## Evidence

- [Approved specification](../superpowers/specs/2026-10-03-flights-m25-multi-leg-design.md) and [implementation plan](../superpowers/plans/2026-10-03-flights-m25-multi-leg.md).
- Core [criteria](../../modules/flights/Travel.Modules.Flights.Core/ValueObjects/SearchCriteria.cs), [itinerary](../../modules/flights/Travel.Modules.Flights.Core/ValueObjects/Itinerary.cs) and [slice](../../modules/flights/Travel.Modules.Flights.Core/ValueObjects/Slice.cs).
- Infrastructure [airport time resolver](../../modules/flights/Travel.Modules.Flights.Infrastructure/Providers/Duffel/DuffelAirportTimeResolver.cs) and [Duffel mapper](../../modules/flights/Travel.Modules.Flights.Infrastructure/Providers/Duffel/DuffelOfferMapper.cs).
- Application [journey validation](../../modules/flights/Travel.Modules.Flights.Application/Search/SearchJourneyValidation.cs) and [cache identity](../../modules/flights/Travel.Modules.Flights.Application/Search/SearchCacheKey.cs).
- [ADR 0015](0015-booking-aggregate-event-model.md), [ADR 0017](0017-flights-idempotency-strategy.md), [ADR 0023](0023-module-api-facades-and-cross-cutting-ownership.md) and [ADR 0024](0024-flights-pii-protection.md) retain their existing ownership, replay, operation and protection contracts.
