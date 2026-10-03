# Flights M2.5: ordered legs and open-jaw journeys

**Status:** proposed design, awaiting the user's joint specification/plan approval. No M2.5 implementation or runtime setup has started.

**Evidence base:** freshly fetched `origin/dev` = `810c40dbfc8cc017e96bef28be8dfef74ad312ca` on 2026-10-03, the PR33 M2.4 merge. Ancestry to M1 checkpoint `d75186054a33de0e1cb688da7e3e7cc42927dadf` and M2.3b `f143e360241311b521e70153ba33998b4bff6109` was checked. Primary dev was clean, equal to origin/dev, with no unpublished commits. A new managed worktree starts at that exact SHA; no archived worktree was restored. PR33 CI [37131352525](https://github.com/svasorcery/travel-agency/actions/runs/37131352525) passed all 13 normal mandatory jobs, including dependent E2E; paid evals were skipped. Postmerge CI [37132065320](https://github.com/svasorcery/travel-agency/actions/runs/37132065320) also passed all 12 normal jobs; E2E was skipped under the unchanged PR-only policy and paid evals stayed skipped. M2.4 source and docs are merged; its own branch/worktree have been cleaned up.

## Purpose, options and recommended scope

Close the remaining M2 capability: a user searches one complete offer for 1–9 fictional adults across 1–4 ordered flight legs, including a two-leg open-jaw, reviews the entire refreshed route, and creates one whole-party order. Existing one-way/return, ranking and saved-traveler flows keep working.

| Approach | Consequence |
| --- | --- |
| **One complete vertical increment with internal review gates** | Recommended. Domain/provider contracts, HTTP/TS readers and UI arrive together; old readers never receive newly enabled journeys during an incomplete release. |
| Backend release followed by UI release | Smaller PRs, but enabling 3–4-leg quotes/orders before updating shared decoders can break existing screens. Requires an additional activation mechanism. |
| Separate searches/bookings per leg or a journey planner | Adds split-ticket/payment/recovery semantics and cannot promise one complete offer. Outside this milestone. |

Use the first approach, with sequential shared-contract work and parallel independent UI/demo work only after the contracts are frozen. No new planner, route catalogue, search session store, LLM call, dependency or feature-flag platform.

## Current contract gaps checked against source

Paths are repository-relative. C/A/I/API denote Flights Core/Application/Infrastructure/Api under `modules/flights`.

| Evidence | Required change |
| --- | --- |
| C `SearchCriteria`: origin/destination/departure/optional return; `IsRoundTrip` means return date present. | Canonical ordered leg criteria; keep the old factory/request as an adapter. |
| C `Itinerary.Create`: max two, mirrored endpoints; `IsRoundTrip` means count two. | Permit 1–4 independent legs, derive journey kind from geometry; do not require adjacent legs to share airports. |
| C `Duration.Create`: less than 48h also used for itinerary totals. `Slice` validates segment continuity and time. | Keep the existing per-slice duration factory; add a bounded journey-total factory so four valid 15h legs have a valid 60h total. |
| I Duffel request builder generates only outbound/return; mapper loops every slice but validates count only. Timestamp converter uses `AssumeUniversal`; place DTO loses `time_zone`. | Send every ordered leg, resolve airport-local timestamps, validate requested route/date/count and all passenger references. |
| A/Redis cache v3 keys old route fields; hit validation checks party and skip reason only. | v4 ordered criteria and route-mode key; strict cold/warm journey validation. |
| API `ItineraryDto.From` and shared TS decoder equate two slices with return; the decoder rejects more than two. Quote/order consumers reuse it. | Add `journeyKind` and update all consumers together, retaining a strict legacy branch. |
| UI quote comparison already includes every ordered segment. Results use `formatOffsetTime`; order list/detail use browser-zone `formatTime` for flight times too. | Preserve full-route acceptance, add kind/ground gaps, and render flight times with their recorded airport offset in every screen. Booking creation timestamps may remain browser-local. |
| Quote request contains supplier reference/count, not authoritative earlier search criteria. | Validate the actual refreshed full offer and require explicit review. Do not claim server proof of correspondence to a previous search. |

Read with the M2 milestone design, M2.3 specification, M2.4 specification, current-state, ADR0015/0017/0023/0024 and the deferred M3 cancellation brief. No B5 state/retry/provider cancellation changes are needed.

## Domain and request contract

- `RequestedFlightLeg` contains origin, destination and local departure `DateOnly`. Factory rejects equal endpoints and absent/minimum dates. Codes use existing IATA syntax validation; no claim of catalogue-backed airport existence.
- `SearchCriteria.Legs` is an immutable ordered copy, 1–4 entries. `CreateMultiLeg` selects `ExplicitAirportLegs`; the existing `Create(origin, destination, departure, return, ...)` constructs 1/2 mirrored legs with `LegacyLocations`. Old public aliases remain for legacy consumers; providers must gate capability before accessing them. First-leg departure stays explicit. ReturnDate exists only for the mirrored two-leg case.
- New explicit leg dates are nondecreasing; equal dates are allowed. This is a documented demo product limit: journeys whose later local departure date is earlier because of the date line are outside this increment. Actual offered instants still must be ordered. No arbitrary maximum ground gap, minimum transfer time or promise of a feasible ground journey.
- `JourneyKind`: `one-way` = one leg; `round-trip` = exactly two mirrored airport legs; `multi-leg` = every other supported 2–4-leg geometry. An open-jaw is `multi-leg`; it does not need another persistence/event type. Selecting the multi-leg form does not change geometry-derived classification.
- `Itinerary.Create` validates every slice, count 1–4, and later slice departure >= previous arrival in UTC. Within a slice the existing continuous-airport/segment-time rules remain. Between slices a different airport is permitted and means independent ground travel.
- Keep `Duration.Create` and the less-than-48h slice limit. Add `Duration.CreateJourneyTotal` for positive totals less than 192h. Sum slice elapsed durations, including connections within each slice, excluding days between slices and ground travel. The existing `Duration { Value }` JSON shape remains unchanged.

```json
POST /api/flights/search/v2?currency=RUB
{
  "legs": [
    { "origin": "LED", "destination": "DME", "departureDate": "2030-06-01" },
    { "origin": "VKO", "destination": "LED", "departureDate": "2030-06-08" }
  ],
  "passengerCount": 2,
  "cabinClass": "economy"
}
```

The anonymous v2 endpoint uses existing currency query and Accept-Language resolution. A guard uses the existing 16KiB in-memory body reader before binding, rejects unknown/duplicate/malformed fields without logging raw values, and performs no dispatch on 400/413. The endpoint validates 1–4 ordered entries and existing party/cabin/currency rules; malformed/count/date/leg input returns safe 400 with fixed error codes and a zero-based `legIndex` where relevant. No request-body logging. The old `/search` and `/search/nl` request contracts remain unchanged; no new NL prompt/parser, AI call or paid eval. UI one-way/return can continue using v1; the new form uses v2. Currency defaults and existing error policy remain.

## Provider anti-corruption and time semantics

New v2 criteria mean exact airport codes, not cities. The UI says this explicitly. A supplier responding to LON with LHR does not satisfy an exact-airport request; the API cannot identify a city code from syntax alone, so it does not invent a local catalogue. Legacy provider-location/city semantics remain on v1 and have a distinct cache mode. Legacy bookable results still have the requested 1/2 slices, local dates and one-way/mirrored-return geometry. No naive exact-airport rejection is added to legacy city requests; authoritative city membership remains delegated to that legacy supplier contract.

Duffel's public [offer requests](https://duffel.com/docs/api/v2/offer-requests) describe ordered slices. The [offers contract](https://duffel.com/docs/api/v2/offers) distinguishes slice/segment, includes airport time zones, and shows offsetless local times. Only documentation was read; no supplier API was called.

- Keep wire values inside Infrastructure. Preserve raw departing/arriving timestamp text in Duffel DTOs and add origin/destination `time_zone`; a mapper-local resolver returns `ErrorOr<DateTimeOffset>`. No timezone/city wire DTO crosses Core/Application.
- Accept ISO local date/time with optional fractional seconds and optional `Z`/numeric offset; reject whitespace, impossible calendars and malformed suffixes. Require a resolvable IANA zone for the airport. No host-zone or assumed-UTC fallback.
- Offsetless times: reject invalid DST gaps and ambiguous times; otherwise use the airport's actual offset. Explicit offsets: require that the offset matches the airport zone at that local time; either valid offset resolves an ambiguous overlap. Unknown/missing zone or a conflicting offset is an invalid supplier offer.
- Use built-in `TimeZoneInfo`; [Microsoft documents IANA resolution](https://learn.microsoft.com/en-us/dotnet/api/system.timezoneinfo.findsystemtimezonebyid?view=net-10.0), including the Windows ICU requirement. Test on the supported .NET10 environments. Missing runtime timezone support fails safely; do not enable invariant globalization, add a paid service, or change machine settings as a workaround.
- Map each segment with its origin-local departure and destination-local arrival offset. Chronology/duration compare instants. Adult age uses the first departure's origin-local calendar date, including when its UTC date differs.
- Duffel serializes every requested leg in order and the existing exact adult count. For v2 validate returned slice count, ordered first/last airports, local departure dates, chronology and exact passenger references/cabins on every segment. A connection within one slice is never another requested leg.
- A nonempty response containing an invalid bookable offer fails that provider response with the existing safe provider-unavailable outcome; never relabel invalid supplier data as a genuine empty result. Genuine empty inventory is still empty. No partial success within an offer.
- Quote maps and validates the complete fresh offer using the same resolver/domain factories. It has no saved search criteria, so route-change acceptance remains an explicit client responsibility. Hold/confirm keep one accepted offer, one party and one aggregate.
- Travelpayouts remains eligible only for legacy v1 one-way/return with one passenger. It cannot prove the complete explicit ordered-leg contract and is skipped for v2 before HTTP with `journey-unsupported`. When both journey and party are unsupported, journey takes precedence. Existing legacy partner summaries remain visibly incomplete; do not invent extra slices or ranking factors.

## Cache, ranking and compatibility

- v4 normal-search key/envelope: ordered origin/destination/local date for every leg, `LegacyLocations`/`ExplicitAirportLegs`, party count, cabin, currency, locale and existing `price-first-v1`; the key also includes the sorted current provider capability inventory (ID/supported/reason). Disabling/removing or adding a provider must cause a miss even when all prior providers succeeded and skips was empty. Preserve a separate criteria-only key overload solely for the existing provider deeplink identity. Old v3 keys are misses and expire naturally; no flush/migration.
- Evaluate current provider support once before cache lookup. Validate bookable party, every new route fact and all supported skip reasons both before ranking/cache write and at cache hit; cached skips must agree with current configured support, and offers/partial failures must belong to current eligible provider IDs. A sorted inventory in the key also prevents a cached result omitting a newly eligible provider. Legacy partner summaries use their existing limited validation; v2 has no partner summary. A corrupt/mismatched v4 envelope is a miss. Cache failures/skips/ranking retain cold/warm parity.
- Ranking remains free, deterministic and price-first within actual currency. Group total covers all passengers and legs. Sum slice durations; transfers = sum(segmentCount − 1). Ground gaps are not transfers or invented duration. Canonical dedup/ties include all ordered slices/segments. Test a difference confined to leg four.
- Add `journeyKind` to HTTP itinerary DTO, retaining `slices`, `totalDuration`, `isRoundTrip`. Server emits the kind for valid search, quote and owned list/detail/cancel itineraries, including the existing historical flat-DTO fallback in OrderResponse.From. Normalize that legacy geometry without invoking new creation rules; malformed stored data retains its explicit contract-error path. `isRoundTrip` agrees with geometry, not count alone.
- New TS decoder accepts kindless historical 1-leg or 2-leg mirrored data with consistent legacy isRoundTrip. Three/four legs require an explicit supported kind. Present kind/count/geometry contradictions are errors. All shared offer/quote/order decoders use this same transport shape/kind rule. They do not apply new inter-slice chronology/duration creation rules to owned historical reads: old constructors permitted a mirrored return before the outbound finished, and current HTTP mapping adds kind to such old data too. A present kind therefore cannot distinguish new history. Strict chronology belongs to fresh provider/v4 search validation. Pin old serialized events → current DTO with kind → TS decode using such a fixture.
- Preserve existing private JSON constructors, event identities and V1/V2/V3 Apply behavior. Derive kind from slices, mark the computed Core kind JsonIgnore and publish it only through HTTP DTO mapping, rather than adding a required persisted field. No schema migration or event rewrite. Historical timestamps/durations are read as stored, never retrospectively shifted or revalidated against new creation rules. Source fixtures capture serialized old events as well as new four-leg replay/projection.
- Existing held orders retain confirm/cancel and PII purposes. New readers must precede new 3–4-leg writers; rolling back to old readers after new writes is unsupported. No rollback, backfill, database command or deployment is authorized by this design.

## UI, errors and uncertainty

- Add a third search mode with 2–4 numbered rows, add/remove buttons, airport/date labels and errors beside the exact leg. No drag/drop or map. Global 1–9-adult count and economy selection remain.
- Show every leg and its connections in results, quote review, owned list and detail, including each segment cabin. Update the actual inline renderer in flight-offer.component.ts as well as its view model so a cabin-only change requiring acceptance is visible. Between unequal adjacent airports show “Самостоятельное перемещение DME → VKO не входит в билет”. Do not estimate its cost or feasibility. Label duration as time in flight legs including their connections; group price includes the whole selected offer.
- Loading, no offers, all providers unavailable, partial results, unsupported-provider skips and malformed-contract states remain distinct. Unsupported v2 Travelpayouts is a capability note, not a supplier failure.
- Any changed leg, connection, airport time/offset, cabin, duration, kind, party or total invalidates accepted quote state, even when price is unchanged. The user reviews the complete refreshed route before hold. Changing requested criteria clears selection/acceptance; it never clears the M2.3 unknown-write barrier.
- Saved-traveler fill still preserves quote-bound slot IDs and all M2.4 generation/edit guards. Recheck adulthood at the actual first departure date. Profiles remain independent of booking snapshot data.
- Hold/confirm unknown outcomes, frozen serialized attempt and safe current-state read paths remain unchanged. No automatic retry, cross-reload recovery, new recovery endpoint or browser persistence. Logout/owner/session change invalidates late completions and clears PII.
- Flight times use recorded local offsets on every screen; booking-created timestamps remain ordinary metadata. No new tokens, PII or operation state in URLs, history, browser storage or logs. Demo auth stays in its isolated fileReplacement; backend auth stays unchanged.

## Acceptance and verification

| ID | Requirement and proof |
| --- | --- |
| J1 | Core accepts one-way, mirrored return, two-leg open-jaw, three/four independent legs; rejects 0/5, empty/same endpoints, invalid local date order and offered UTC time inversion. Equal dates are accepted. Four 15h slices sum to 60h; each slice >=48h remains invalid. Pure unit. |
| J2 | Ordered outgoing Duffel slices and exact count match; connecting segments stay in one slice. Airport/date/leg-four/passenger mismatch fails the provider; unknown zone, DST gap/ambiguous-no-offset and conflicting explicit offset fail safely. Explicit overlap offset, date-line flight and first-origin age date pass. Stub HTTP/provider + unit, no supplier calls. |
| J3 | v1/NL legacy shape and city behavior remain; v2 is exact-airport, Travelpayouts skips before HTTP, including both unsupported reasons. Invalid inventory differs from genuine empty. Cache v3 miss/v4 cold-warm parity and leg order/mode collisions are tested. Pure handler/serializer tests; isolated Redis integration in CI. |
| J4 | Search/quote/orders serialize consistent journeyKind; TS strict decoder covers old absence, 2-leg open-jaw and 4-leg data plus contradictory/malformed payloads. Legacy serialized quote/held V1/V2/V3 replay/projection remain intact without keys; new four-leg events project all slices. Unit + no-DB HTTP/TS; full Host/Marten/Postgres/OpenAPI in CI. |
| J5 | Late-leg-only route change requires fresh acceptance; first-local-date birthday/profile fill keeps slot ID and guards. Unknown hold survives a new multi-leg search and prevents another write. Order list/detail show all legs, ground gaps and airport-local times. Angular tests + fictional Playwright. |
| J6 | Desktop/mobile/keyboard complete two-adult open-jaw and four-leg flows to one order, then reopen list/detail. Existing one-way/return/ranking/profiles/B5 journeys pass. Demo network/storage guards retain no external/paid calls and memory-only PII. Node + Playwright + visual inspection. |
| J7 | Independent review, no CI/CD/dependency/schema/auth changes, all normal mandatory PR CI including E2E green, merge into dev, successful normal postmerge CI, clean dev/no unpublished task commits and own cleanup. Paid evals skipped. |

Before selecting tests, inspect startup again: non-Production Flights EF initializer calls MigrateAsync; Marten and Wolverine initializers apply schema despite registration AutoCreate.None. No local Host/AppHost/database fixtures, migrations, key provisioning or deployment. Pure tests, audited TestServer/fakes and fictional browser builds are allowed after approval; real schema-creating fixtures run only in existing mandatory CI. No M2.5 tests ran during design.

## Boundaries and self-review

Outside: children/infants, documents/loyalty, ancillaries/refunds, split tickets/payments, live supplier/payment acceptance, new SSE/Support, NL multi-leg AI, paid evals, maps/catalogue/timezone service, durable unknown-operation recovery, local migrations/deploy and M3 cancellation/OpenSpec pilot. B5 remains accepted and Duffel cancellation remains unsupported before HTTP.

Self-review corrections: retain the old duration factory and add a journey-total factory; distinguish exact airport mode from legacy city searches in both support and cache; require timezone truth before UTC chronology; keep ground travel separate from connections/ranking; explicitly state quote's missing earlier-search provenance; derive kind without changing persisted event constructors; apply one decoder to search/quote/orders; preserve unknown-write barriers when criteria/profile data change. See the [implementation plan](../plans/2026-10-03-flights-m25-multi-leg.md). The proposed specification and plan need explicit joint approval before code.

## Publication and next-chat handoff

The user authorized publishing the remaining documents and cleaning up this chat on 2026-10-03. This publication preserves the proposed design and plan; it does **not** approve M2.5 product implementation. Self-review and independent review passed after the provider-inventory cache key, historical DTO chronology compatibility and actual cabin renderer were clarified. No M2.5 product code, migration, dependency setup or runtime was added.

The next chat must read both documents, freshly fetch origin/dev and verify ancestry, then obtain joint design/plan approval before code. Create a new managed worktree from the exact fetched SHA; do not restore this chat's archived worktrees. After approval the retained delivery scope is implementation, suitable tests, independent review, commit/push, PR into dev, all mandatory normal CI including PR E2E, merge, postmerge verification, clean dev and own cleanup. M2 is still incomplete until M2.5 is delivered; M3 remains deferred.
