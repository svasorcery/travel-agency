# Flights M2.5 Multi-leg Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans or superpowers:subagent-driven-development to implement this plan task-by-task. Use the user's preserved execution preference; independent ownership and review remain mandatory. Steps use checkboxes for recovery.

**Goal:** search, quote and book one complete 1–4-leg offer for 1–9 fictional adults, including open-jaw, with strict legacy compatibility and a usable browser demonstration.

**Architecture:** Core owns ordered requested legs, geometry and chronology; Infrastructure resolves supplier airport-local time and preserves all wire facts. Application validates provider capability, route and cache/ranking evidence; Api publishes additive itinerary facts and a new search/v2 request. The existing whole-party booking, protection, saved profiles and unknown-write barriers remain.

**Tech stack:** existing .NET10/ErrorOr/Wolverine/Marten/EF/Redis, Angular/TypeScript/Vitest, Node/Playwright. Built-in TimeZoneInfo only; no new package, LLM or paid API.

**Spec:** [2026-10-03-flights-m25-multi-leg-design.md](../specs/2026-10-03-flights-m25-multi-leg-design.md).

**Status/base:** specification and plan jointly approved on 2026-10-03, with an additional self-review before implementation. Freshly fetched origin/dev is `df7708716161750f053711fc3b663267833f0351`; ancestry to that M2 checkpoint and M1 `d75186054a33de0e1cb688da7e3e7cc42927dadf` was verified. A new managed implementation worktree starts at that exact SHA. The earlier documentation base `810c40dbfc8cc017e96bef28be8dfef74ad312ca` is historical. If origin/dev moves before execution, fetch and verify ancestry; do not substitute HEAD or rewrite/force-move the branch.

## Global constraints

- One complete offer/order/party; 1–4 requested legs, 1–9 adults. UI economy; API keeps existing cabin parsing. Explicit local dates nondecreasing, equal allowed. No split tickets or inferred ground feasibility.
- Slice elapsed duration positive and <48h; summed journey duration positive and <192h, excluding between-leg stays/ground travel.
- New request uses exact airport codes. Old search/NL retain provider-location/city semantics. No catalogue or implicit city-to-airport substitution.
- Tokens, PII and client operations in memory only; no URLs/history/browser storage/logging. Demo fake auth only in existing fileReplacement; backend JWT/issuer/audience/scope/GUIDsub unchanged.
- Fictional data and stub network only. No supplier/payment/Anthropic/paid API or paid AI-eval; run_paid_ai_evals remains off.
- No local Host/AppHost/schema apply/key provisioning/deploy. Reinspect initializer/fixture before each new test selection. Existing mandatory CI owns schema-creating suites; no CI/CD/dependency/hook configuration changes.
- Event identities/private JSON constructors/V1–V3 replay/protection purposes unchanged. No EF migration or historical timestamp rewrite.
- B5 remains accepted, Duffel cancellation remains unsupported before HTTP. M3/OpenSpec/refunds/ancillaries/new SSE/Support excluded.
- Root owns shared contract, git index/commits and final integration; workers own explicitly disjoint files and must accommodate others' changes. Serialize all .NET builds/tests on Windows.

## Review focus

1. A later local date can disagree with UTC order; a DST overlap without offset cannot be guessed. Pin the timezone/date-line cases in Task2 and offered chronology in Task1.
2. A provider response/cached result differing only in leg four must fail or remain distinct; no outbound-only key, dedup, route check or acceptance. Tasks2/3/5.
3. A legacy city code cannot be rejected by a new exact-airport equality rule; cache mode/capability and API versions stay distinct. Tasks1–4.
4. A route change, owner transition or saved-profile callback cannot release an unknown booking barrier or restore stale PII. Task5 and Task6 delayed-response proof.
5. New kind/offset/total behavior must not reinterpret old event JSON, break old held confirm/cancel, or let quote/list/detail share inconsistent readers. Tasks1/4/5 and CI regression.

## Task0: establish approved execution and shared contracts

- [x] Obtain explicit joint approval of this written specification and plan; preserve the authorized full delivery cycle and parallel/sequential preference. No product code or dependency setup before that gate.
- [x] Fetch origin/dev, record exact SHA, verify M1 and latest accepted merge ancestry and worktree ancestry. If the branch does not descend from the new requested base, create a fresh managed worktree/branch instead of rewriting it. Never restore an archive.
- [x] Read root/modules/flights/shared AGENTS and startup/fixture code again. Audit unit/TestServer/demo selections. Do not run Host/AppHost or database fixture locally.
- [x] Freeze Core/DTO/TS signatures below in the task ledger. Root owns Core contract edits; no overlapping worker ownership. Create reviewable commits per completed task; never commit scratch/browser caches.

## Task1: ordered domain criteria and compatible itinerary construction

**Own:** C `ValueObjects/SearchCriteria.cs`, `Itinerary.cs`, `Slice.cs`, `Duration.cs`; new `RequestedFlightLeg.cs`, `JourneyKind.cs`, `SearchRouteMode.cs`. Tests: existing Unit `ValueObjects/SearchCriteriaTests.cs`, `ItineraryTests.cs`, `SliceTests.cs`, `DurationTests.cs`, `Aggregates/PassengerPartyReplayTests.cs`, `ReadModels/PassengerPartyProjectionTests.cs`; new `ValueObjects/MultiLegCriteriaTests.cs`, `Aggregates/MultiLegReplayTests.cs`.

**Interface:** keep the old SearchCriteria.Create signature. New factory consumes an ordered immutable copy and returns ErrorOr. IFlightSearchProvider remains unchanged.

```csharp
public enum JourneyKind { OneWay, RoundTrip, MultiLeg }
public enum SearchRouteMode { LegacyLocations, ExplicitAirportLegs }
// New value object: Origin, Destination, DepartureDate; private constructor.
public static ErrorOr<RequestedFlightLeg> Create(
    IataCode origin, IataCode destination, DateOnly departureDate);
// On SearchCriteria:
public static ErrorOr<SearchCriteria> CreateMultiLeg(
    IReadOnlyList<RequestedFlightLeg> legs, int passengerCount,
    CabinClass cabinClass, CurrencyCode currency, string locale = "ru");
// On Duration, preserve old Create(TimeSpan):
public static ErrorOr<Duration> CreateJourneyTotal(TimeSpan value);
```

- [x] Write RED cases: 1/2 mirrored, 2 open-jaw, 3/4 legs; 0/5/null; same endpoint/minimum date; equal versus decreasing local dates; four 15h slices =60h; >=48h individual slice; next departure before previous arrival with differing offsets. Assert caller list mutation cannot change criteria/itinerary.
- [x] Pin nested immutability: mutating the caller's original segment list after Slice/Itinerary creation cannot change route, chronology or cached totals. New factories take read-only defensive copies; keep historical JSON constructor signatures and replay unchanged. Fresh validation rejects null legs/slices/segments and malformed deserialized slice endpoints/durations before indexing; it does not become a replay validator.
- [x] Capture serialized pre-M2.5 quote/held V1/V2/V3 fixtures with absent new kind; replay/project without protection keys, preserving original timestamp strings/instants and ciphertext. Add four-leg quote/re-quote/held projection and old-held confirm/cancel regression.
- [x] Run focused RED: `dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit --filter "FullyQualifiedName~MultiLegCriteriaTests|FullyQualifiedName~MultiLegReplayTests|FullyQualifiedName~ItineraryTests|FullyQualifiedName~DurationTests"` after the safe-unit audit; verify actual failing assertions.
- [x] Implement canonical Legs/mode, old factory adapter and aliases; geometry-derived JourneyKind/IsRoundTrip. New creation validates slices/UTC leg order; JSON constructors/Apply remain unconditional. Mark the computed Core JourneyKind JsonIgnore; HTTP owns its string publication. Use CreateJourneyTotal for the summed total, keep Slice's existing Create duration bound.
- [x] GREEN focused tests and existing replay/projection subset. Independent review checks historical JSON identity and creation/replay separation; fix findings before committing `feat(flights): model ordered multi-leg journeys`.


Concrete new Core test (existing Shouldly/xUnit pattern):

```csharp
[Fact]
public void Two_independent_legs_preserve_exact_order_and_mode()
{
    var first = RequestedFlightLeg.Create(IataCode.Create("LED").Value,
        IataCode.Create("DME").Value, new DateOnly(2030, 6, 1)).Value;
    var second = RequestedFlightLeg.Create(IataCode.Create("VKO").Value,
        IataCode.Create("LED").Value, new DateOnly(2030, 6, 8)).Value;
    var result = SearchCriteria.CreateMultiLeg([first, second], 2,
        CabinClass.Economy, CurrencyCode.Create("RUB").Value);
    result.IsError.ShouldBeFalse();
    result.Value.Legs.ShouldBe([first, second]);
    result.Value.RouteMode.ShouldBe(SearchRouteMode.ExplicitAirportLegs);
    result.Value.IsRoundTrip.ShouldBeFalse();
}
```
## Task2: Duffel whole-route mapping and timezone truth

**Own:** I `Providers/Duffel/DuffelFlightSearchProvider.cs`, `DuffelOfferMapper.cs`, existing `Dto/DuffelSegmentDto.cs`, `DuffelPlaceDto.cs`, `DuffelLocalTimestampConverter.cs`; new `Providers/Duffel/DuffelAirportTimeResolver.cs`. Tests: Unit `Providers/Duffel/DuffelPartySearchTests.cs`, `DuffelPartyMappingTests.cs`, `DuffelOfferMapperTests.cs`; new `DuffelAirportTimeResolverTests.cs`, `DuffelMultiLegSearchTests.cs`. Actual provider Integration tests are CI-only.

**Interface:** raw timestamps remain string-valued wire DTO fields; place adds nullable time_zone. No provider DTO enters another layer. Resolver is pure and shared by search/quote mapping:

```csharp
public static ErrorOr<DateTimeOffset> Resolve(string raw, string? ianaTimeZone);
// Mapping still produces ErrorOr<BookableOffer> via DuffelOfferMapper.Map(dto, time).
// BuildRequest emits c.Legs.Select(l => new {
//   origin = l.Origin.Value, destination = l.Destination.Value,
//   departure_date = l.DepartureDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
// }).ToArray(), with the existing exact adult count.
```

- [x] RED resolver fixtures: London/New York different offsets; unknown/missing zone; impossible date/whitespace/suffix; spring gap; autumn overlap without offset; both valid explicit overlap offsets; conflicting offset. Use dates in the fictional2030 fixtures and expected offsets from TimeZoneInfo. Test a flight whose arrival local clock is earlier but absolute instant later, and first departure whose local date differs from UTC.
- [x] RED stub requests: four outgoing slices exactly ordered; connection segments do not expand requests; wrong fourth airport/date/count/member/cabin/time; genuine zero inventory versus nonempty invalid inventory. Legacy city request LON→JFK must retain provider-delegated airport semantics.
- [x] Implement strict ISO text parse, IANA lookup and invalid/ambiguous checks. For explicit offsets verify membership in valid offsets at the wall time. No host timezone, UTC guess or machine-setting change. Remove/rework the old AssumeUniversal converter; update only wire fixtures/constructors requiring actual time_zone.
- [x] Map resolved offsets before segment/slice construction; derive party adult date from the resolved first origin-local departure. Map all segments, then validate the requested ordered v2 airports/local dates/count. Legacy response still must have 1/2 requested slices and one-way/mirrored-return geometry; no exact city-versus-airport rule. Invalid nonempty bookable inventory fails the provider safely; do not log raw payload/exception messages.
- [x] GREEN focused resolver/mapper/stub tests plus booking safety fixtures. Independent review of DST, age date and full-route mapping, then commit `feat(flights): map complete Duffel journeys with airport time`.


Concrete resolver RED/GREEN assertion; raw/local time never enters the error description:

```csharp
[Fact]
public void Dst_overlap_requires_an_explicit_valid_airport_offset()
{
    DuffelAirportTimeResolver.Resolve("2030-10-27T01:30:00", "Europe/London")
        .IsError.ShouldBeTrue();
    var resolved = DuffelAirportTimeResolver.Resolve(
        "2030-10-27T01:30:00+01:00", "Europe/London");
    resolved.IsError.ShouldBeFalse();
    resolved.Value.Offset.ShouldBe(TimeSpan.FromHours(1));
    resolved.Value.UtcDateTime.ShouldBe(new DateTime(2030, 10, 27, 0, 30, 0, DateTimeKind.Utc));
}
```
## Task3: capability, route validation and v4 cache/ranking parity

**Own:** C `Providers/FlightSearchSupport.cs`; I `Providers/Travelpayouts/TravelpayoutsSearchProvider.cs`, `Cache/SearchCacheRedis.cs`; A `Search/SearchCacheKey.cs`, `SearchPartyValidation.cs`, new `SearchJourneyValidation.cs`, `SearchProviderCapability.cs`, `Handlers/Search/SearchFlightsHandler.cs`. Tests: Unit `Search/SearchPartyTests.cs`, `SearchCachePartyEnvelopeTests.cs`, `OfferRankingTests.cs`, new `MultiLegSearchHandlerTests.cs`, `SearchJourneyValidationTests.cs`; disposable Redis/provider Integration cases CI-only.

**Interface:** keep party validation and introduce a separate full-journey validator; support reasons are fixed strings.

```csharp
public static bool Matches(Offer offer, SearchCriteria criteria);
public static bool IsValid(SearchResult result, SearchCriteria criteria,
    IReadOnlyList<SkippedProvider> expectedSkips, IReadOnlySet<string> eligibleProviderIds);
public sealed record SearchProviderCapability(string Provider, bool Supported, string? ReasonCode);
// SearchCacheKey adds Build(criteria, IReadOnlyList<SearchProviderCapability> capabilities).
// Existing Build(criteria) remains criteria-only solely for provider deeplink identity.
// FlightSearchSupport.JourneyUnsupported: new(false, "journey-unsupported").
// Travelpayouts explicit-leg mode gate precedes count and precedes any HTTP.
// Normal v4 key hashes ordered legs/mode/count/cabin/currency/locale/policy
// plus sorted current provider ID/supported/reason inventory.
```

- [x] RED support tests: direct TP.SearchAsync(v2) performs zero HTTP even with count2; v1 count1 remains eligible; v1 count2 uses passenger-count-unsupported. All-ineligible returns an empty response with skips; all eligible providers failing returns unavailable.
- [x] RED pure handler/cache serializer tests: ordered leg permutations, leg-four-only change, exact/legacy mode, currency/locale/count; v3 miss; corrupt/unsupported offer `$type` discriminator or skip reason; capability configuration changes; mismatched count/airports/date. Core journey kind stays computed/JsonIgnore; HTTP kind contradictions are covered in Tasks4/5. Pin provider enabled→disabled AND newly added misses, especially successful old responses with skips=[]; removed-provider offers/failures cannot survive and new eligible providers must be searched. Exercise both cold and warm, not only cache key text.
- [x] Evaluate GetSupport once before cache lookup. Hash its sorted ID/supported/reason inventory into the normal-search key. Expected skips are compared with cached skips, and offer/failure IDs must be currently eligible; validate all returned live offers before ranking/cache write. v2 bookable routes match exact ordered airports/local dates; legacy bookable count/geometry/dates are checked without pretending city provenance. A provider returning invalid inventory becomes its safe failure. Legacy partner summary retains limited facts and unknown factors.
- [x] Pin the party departure-date binding: cold and warm bookable offers require Party.FirstDepartureLocalDate to equal the first segment's recorded origin-local calendar date. A cached valid-count party with a stale date is a miss; a live mismatch fails that provider, preserving adulthood checks downstream. Cache validation must reject malformed route internals safely and compare stored slice/total metadata with freshly derived values without changing stored history.
- [x] Version complete Redis envelope/key to4, preserving failures/skips/ranking. New full itinerary validation uses the domain factory; corrupt data is a miss, never a false empty hit. No flush or migration.
- [x] Pin deterministic ranking/dedup differences confined to leg four, total60h, ground gap excluded from transfer count/duration and group-total price. Keep price-first-v1 and currency grouping unchanged.
- [x] GREEN pure tests, independent review and commit `feat(flights): validate multi-leg search and version cached results`.


Concrete cache compatibility test in existing SearchCachePartyEnvelopeTests, reusing its audited DispatchProxy Redis fake:

```csharp
[Fact]
public async Task Old_version_three_is_a_miss_even_with_current_offer_fields()
{
    var node = JsonNode.Parse(Envelope())!;
    node["schemaVersion"] = 3;
    (await Read(node.ToJsonString())).ShouldBeNull();
}
```

The configured-inventory regression must compare Build(criteria, capabilities) for the same criteria with Duffel-only versus Duffel+Travelpayouts and assert different keys in both directions; additionally seed the pure handler's fake cache with an old all-success response and assert every newly eligible provider is invoked. This pins behavior beyond the key implementation.
## Task4: HTTP v2 and unified itinerary transport compatibility

**Own:** API `Contracts/Contracts.cs`, new `Contracts/MultiLegSearchContracts.cs`, `Endpoints/MultiLegSearchEndpoint.cs`, `Middleware/MultiLegSearchBodyGuardMiddleware.cs`; narrow Api.Composition registration. Tests: new safe Host `Flights/MultiLegSearchHttpTests.cs`, existing `Flights/FlightsApiFixture.cs`, `FlightsSearchContractHttpTests.cs`; existing documentation snapshot/endpoint inventory after actual observation. No Host Program/auth edits.

```csharp
public sealed record RequestedFlightLegDto(string Origin, string Destination, DateOnly DepartureDate);
public sealed record SearchRequestV2(RequestedFlightLegDto[] Legs,
    int PassengerCount = 1, string CabinClass = "economy");
// ItineraryDto keeps its old fields, adds optional trailing string JourneyKind.
// MultiLegSearchEndpoint uses existing locale/currency parsing and the same SearchFlightsQuery.
// Explicit response type: Results<Ok<SearchResponse>, ProblemHttpResult>.
```

- [x] RED no-DB TestServer cases: v1 unchanged; v2 1/2-open/4 legs, currency/locale/count; null/0/5/duplicate/unknown/malformed fields, impossible calendars and decreasing dates; safe fixed400 +legIndex, oversized413, no dispatch. Assert no raw body appears in recording logs.
- [x] Reuse BoundedRequestBody for a 16KiB RAM guard scoped only to POST search/v2 before binding. Enforce exact fields/types and bounded legs without echoing submitted names/values. No booking fingerprint/idempotency/body behavior changes.
- [x] Map domain kind to one-way/round-trip/multi-leg for every itinerary DTO, including quote and owned list/detail/cancel. Derive legacy kind from geometry; do not require it in persisted events. Normalize the existing OrderResponse.From flat-ItineraryDto fallback too, without calling fresh Itinerary.Create or shifting/revalidating stored instants.
- [x] GREEN safe HTTP/serialized legacy/new quote-order payloads. Validate actual Wolverine endpoint discovery/OpenAPI with an audited isolated mediator-only endpoint fixture; full Host snapshot runs in CI. Capture paths, schemas AND global tags, rather than constructing an assumed snapshot. No local Host startup.
- [x] Independent HTTP/compatibility review and commit `feat(flights): expose explicit multi-leg search contract`.


Concrete safe TestServer negative case; FakeMessageBus has no registered response, so unintended dispatch cannot produce this required400:

```csharp
[Fact]
public async Task Empty_explicit_legs_are_rejected_before_dispatch()
{
    _fixture.Bus.Reset();
    using var response = await _fixture.Client.PostAsJsonAsync(
        "/api/flights/search/v2?currency=RUB",
        new { legs = Array.Empty<object>(), passengerCount = 2, cabinClass = "economy" },
        TestContext.Current.CancellationToken);
    response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
}
```
## Task5: shared TS readers and complete UI flow

**Own:** shared TS `flights-search.types.ts`, `flights-search.decoder.ts`, `flights-search-api.service.ts`, `index.ts` and their specs; shared quote/booking decoder tests. UI `flight-search-form.ts`, `flights-page.component.ts/html`, `flight-results.ts`, `flight-offer.component.ts`, `flight-quote.ts`, quote panel HTML, order list/detail templates/components; focused existing/new Angular specs. Keep saved-profile service/auth/operation barriers unchanged unless a demonstrated route integration blocker requires a scoped fix.

```typescript
export type FlightJourneyKind = 'one-way' | 'round-trip' | 'multi-leg';
export interface FlightSearchV2Request {
  legs: { origin: string; destination: string; departureDate: string }[];
  passengerCount: FlightPassengerCount;
  cabinClass: 'economy';
}
// FlightItinerary.journeyKind?: FlightJourneyKind;
// FlightsSearchApiService.searchMultiLeg(body: FlightSearchV2Request): Observable<FlightSearchResponse>;
// New form TripType adds 'multiLeg'; FormArray has2–4 numbered leg groups.
// Search intent is a discriminated legacy/v2 union; never silently flatten v2 into v1.
```

- [x] RED shared decoder cases: kindless old1/2-mirror, explicit2-open/3/4, missingkind3/4, unsupported/presentcontradictory kind/isRoundTrip/geometry, invalid route and count. Test shared quote/order decoding and v2 response consistency with submitted ordered criteria. Do not add a separate lenient decoder.
- [x] Implement one transport shape/kind rule reused by all decoders, without fresh creation chronology on historical order reads. Pin an old mirrored return before outbound completion through replay→current DTO with present kind→TS decode; kind presence is not a history version. Fresh search service checks/provider/cache still enforce current route chronology. Normalize only effective legacy kind for presentation/quote comparison; a present contradiction is a contract error. Existing search(body) stays on v1; searchMultiLeg sends v2 and verifies all returned bookable route facts/count. No client re-ranking.
- [x] RED UI: add/remove bounds, stable row errors/date order, mode switching and stale search response; four-leg card/ground gap; leg-four-only quote change with unchanged price requires acceptance; birthday on first origin-local date. Search criteria change clears selected quote, never operation barriers.
- [x] Extend the existing form/selection flow, group price/duration labels, numbered legs and ground gaps. Add cabinClass to each segment view and render it in the actual flight-offer inline template, quote and owned route screens; a later-leg-only cabin change must be visible. Apply formatOffsetTime to segment flight times in order list/detail; keep bookedAt metadata formatting. Keyboard controls and360px overflow check are required.
- [x] RED integration behavior: profile fill preserves slotId; manual row edit and owner/epoch/quote generation beat delayed fill; unknown hold followed by multi-leg search still prevents another write. Existing M2.3/M2.4 guards remain authoritative; no storage/auth workaround.
- [x] Run `npx.cmd nx test api-client`, `npx.cmd nx test web`, `npx.cmd nx build web --configuration=production` and affected lint/Biome after setup under approved fictional boundaries. Verify actual test counts/output; retained warnings documented, no suppressions.
- [x] Independent UI/shared-reader review and commit `feat(web): search and review complete multi-leg journeys`.


Concrete shared-reader regression in the existing fixture-based Vitest file:

```typescript
it('rejects a present kind contradicting the one-way geometry', () => {
  const value = structuredClone(fixtures.oneWay.response);
  Object.assign(value.offers[0].itinerary, { journeyKind: 'round-trip' });
  expect(() => decodeFlightSearchResponse(value)).toThrow(FlightSearchContractError);
});
```

Keep the unmodified historical fixture as a positive assertion, and add a separate current DTO fixture carrying kind but the historically permitted inter-slice time inversion. Both must decode; the same route from a fresh v2 provider must be rejected by Task2/3.
## Task6: fictional acceptance, final review and complete delivery

**Own:** `tools/demo/flights-search-api.mjs`, `tools/demo/flights-search-api.test.mjs`; new `tests/travel-e2e/demo/flights-multi-leg.spec.ts`; existing `fictional-route-guard.ts`, `fictional-browser-proof.ts` reused unchanged where possible. README/current-state, dated result report and ADR amendment only where the confirmed compatibility/time decision needs it. No AGENTS rule/fact edits without applicable explicit authorization.

- [ ] RED Node protocol tests for exact v2 body/leg order/count, open-jaw/4-leg quote/hold/order/list, whole-group price, capability skips, malformed/invalid dates and no silent partial itinerary. Old v1 fixtures and saved profiles stay compatible.
- [ ] Extend the in-memory fake to retain the complete offered itinerary in supplier reference/quote/order. Use only fictional timestamps/prices/identities; no real provider or auth token accepted. Order count equals one, not one per leg.
- [ ] Playwright: two profiles/adults on2-leg open-jaw and four legs; fresh route-only re-quote; unknown hold+new search; reopen owned list/detail; ground gap and local-offset display; mobile/keyboard. Keep prior one-way/return/ranking/profiles/B5 tests. Every route.fetch has the existing exact localhost/method/path/auth/redirect guard; synthetic external targets abort before network. Delayed assertions await actual handler/request/client completion, not fixed sleeps.
- [ ] Run Node tests and the isolated fictional Playwright configuration; inspect desktop/mobile screenshots. Stop owned servers/processes and verify ports4201/5100 free. No Host/AppHost.
- [ ] Run audited full unit, safe HTTP, architecture, Release compile and source/static checks. Actual Redis/Postgres/Marten/outbox/Host/Aspire tests remain CI-only. Do not run paid evals or the authenticated Codex harness verifier as an accidental AI workload.
- [ ] Write result/limits report; independent whole-branch review on the confirmed capable model, with no author overlap. Fix important findings with targeted tests, then rerun only checks affected by new changes. Check no schema/CI/CD/dependency/auth changes.
- [ ] Commit all task source/docs, push and attach PR into dev under preserved full-cycle authorization. Exact-head all mandatory normal CI including dependent E2E must pass. Do not merge, override or change checks on failure.
- [ ] Normal merge with head-SHA guard; verify closed PR and merged source ancestry. Check normal postmerge CI (E2E is PR-only under existing policy, paid lane skipped). Fresh-fetch/update clean local dev by fast-forward.
- [ ] Restore primary hooks, preserve necessary recovery notes, native-archive only this managed worktree and remove only this task's merged branches after identity/ancestry checks. Remove owned temporary files/processes. Verify clean dev, dev==origin/dev and no unpublished task commits. Only then report all M2 complete against the four milestone capabilities; M3 remains deferred.


Concrete Node v2 adapter regression; extend buildDemoSearchResponse to accept the discriminated v2 shape rather than flattening it:

```javascript
test('explicit open-jaw has two independent legs and one group price', () => {
  const response = buildDemoSearchResponse({
    legs: [
      { origin: 'LED', destination: 'DME', departureDate: '2030-06-01' },
      { origin: 'VKO', destination: 'LED', departureDate: '2030-06-08' },
    ],
    passengerCount: 2,
    cabinClass: 'economy',
  });
  const offer = response.offers.find((item) => item.providerOfferRef !== null);
  assert.equal(offer.passengerCount, 2);
  assert.equal(offer.itinerary.journeyKind, 'multi-leg');
  assert.equal(offer.itinerary.isRoundTrip, false);
  assert.deepEqual(offer.itinerary.slices.map((item) => [item.origin, item.destination]),
    [['LED', 'DME'], ['VKO', 'LED']]);
});
```
## Plan self-review and approval gate

Additional pre-implementation self-review on the fresh PR34 base found and corrected nested segment-list immutability, null/malformed fresh-route validation, first-local-date party/cache binding and the demo test filename. J1-J7 coverage and Core/API/TS signatures remain consistent. These corrections strengthen the approved contracts without changing product scope. The user's approval includes implementation and the full delivery cycle after this self-review.

J1→Task1; J2→Task2; J3→Task3/4; J4→Task1/4/5; J5→Task5/6; J6→Task6; J7→Task6. Each task has a real RED/GREEN deliverable and an independent review. Shared DTO/factory signatures are frozen before parallel ownership. No task adds historical data rewrite, required persisted kind, search provenance store, new AI, paid call, or local schema application.

The five review focus cases are pinned to their owning test steps. There are no undecided product limits or hidden activation/deployment work. Confirmed user method permits sequential/parallel work; root chooses sequential Core→provider→cache/API, then independent UI/demo responsibilities where contracts are stable. Joint approval and the requested additional self-review are complete; task completion and delivery evidence are recorded separately.
