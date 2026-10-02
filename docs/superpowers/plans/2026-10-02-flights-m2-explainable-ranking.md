# Flights M2.1 Explainable Ranking Implementation Plan

> **For agentic workers:** after user approval, use `superpowers:executing-plans` to implement task-by-task in this session, then obtain independent whole-change review. Steps use checkbox syntax. User approval and the second self-review gate were satisfied on 2026-10-02.

**Goal:** show truthful, reproducible reasons for price-first ordering in anonymous Flights search, including incomplete data and failed currency conversion.

**Architecture:** pure Application ranking and conservative dedup; complete versioned search cache; additive HTTP explanation metadata; typed Angular presentation without client-side reranking. Core booking events, EF storage and provider wire contracts stay unchanged in M2.1.

**Tech stack:** existing .NET 10/Shouldly/xUnit, Redis adapter, ASP.NET/Wolverine endpoints, Angular/Nx, TypeScript runtime decoders, Node fictional API and Playwright. No new package or paid service.

**Spec:** [Flights M2 specification](../specs/2026-10-02-flights-m2-design.md), especially §§5–7 and R1–R7.

**Status/base:** approved 2026-10-02 after the requested second self-review. Research base `d75186054a33de0e1cb688da7e3e7cc42927dadf`; managed design worktree `C:\Users\Vladimir_sva\.codex\worktrees\flights-m2-design\travel-agency`, branch `codex/flights-m2-explainable-ranking`, created directly from the freshly verified base. Verification evidence is tracked in the local result report.

## Global constraints

- Only fictional data. No real supplier, payment, Anthropic or other paid API calls; skip paid AI-evals and never enable `run_paid_ai_evals`.
- Tokens and client operation state stay in memory. No PII in URLs, browser storage, history state or logs. Fake auth exists only in the isolated demo build/test fixtures; backend JWT/owner checks remain intact.
- No local schema creation/application, migrations or deployment. Inspect fixture/startup behavior before any Host/AppHost or test execution. Existing mandatory CI performs its disposable database checks; do not change CI/CD or bypass checks.
- B5 remains accepted. Duffel cancellation returns unsupported before HTTP; only the fictional demo demonstrates cancellation. Refunds, ancillaries, SSE, Support, durable cancellation recovery and the OpenSpec pilot are outside M2.1.
- No new provider HTTP calls, fields in booking events, EF migrations, auth changes, traveler forms or route-model expansion. No custom ranking weights, LLM, personalization, new endpoint or generic ranking engine.

## Review focus

1. FX failure before dedup must not remove a bookable offer or rank unrelated currencies numerically (Tasks 1–2, R2/R3).
2. Pairwise unknown-factor handling must still form a transitive total ordering (Task 1 permutation/tie tests, R1/R3).
3. Cached failures and source price provenance must not disappear while factors remain visible (Task 2, R4).
4. Legacy/invalid explanation metadata and late search responses must not attach reasons to a different offer or quote (Tasks 3–4, R5/R6).
5. New public ranking must not expose provider credentials, raw payloads or passenger data, or alter booking acceptance/unknown barriers (Tasks 3–6, R7).

## Task 0: re-establish the execution base after approval

- [x] Fetch `origin dev`, record its exact SHA and verify it descends from the research checkpoint. Check main checkout and all active attached worktrees for other work.
- [x] If fetched SHA is unchanged, use this managed worktree and create a new task branch from that exact SHA, after ancestry verification. If dev advanced, create a new managed worktree from the new fetched SHA and copy only these two reviewed documents after checking hashes/diff; inspect intervening changes and amend the plan if they invalidate it. Never reset/force-move an existing branch or restore an archived worktree.
- [x] Read root/nested instructions and applicable implementation/test/UI skills. Inspect test fixture startup and dependency scripts. Record which checks are safe locally. Do not run blanket `dotnet test Travel.slnx` under the no-schema instruction.
- [x] Establish baseline with the selected pure/no-DB suites below. If a baseline fails, explain it and its options; do not bypass a gate or fix unrelated tasks silently.

## Task 1: pure ranking facts, ordering and conservative dedup

**Files:** create `modules/flights/Travel.Modules.Flights.Application/Search/OfferRanking.cs`; modify neighboring `OfferRanker.cs` and `OfferDeduplicator.cs`; add `tests/flights/Travel.Modules.Flights.Tests.Unit/Search/OfferRankingTests.cs` and adapt `OfferPipelineTests.cs`.

**Interfaces:** these Application-owned records keep external DTOs out of Core. Import Core `Money`, `Offer`, `CurrencyCode`; `SearchRanking` stores the wire-independent factors defined in the spec.

```csharp
public enum RankingPriceState { Native, Converted, FxUnavailable }
public sealed record RankingCandidate(Offer Offer, Money SourcePrice, RankingPriceState PriceState);
public sealed record OfferRankingEntry(
    Guid OfferId, string Currency, int Rank, Money SourcePrice,
    RankingPriceState PriceState, long? DurationSeconds, int? Transfers,
    IReadOnlyList<string> Limitations);
public sealed record SearchRanking(
    string Policy, string RequestedCurrency, IReadOnlyList<OfferRankingEntry> Entries);
public sealed record RankedOffers(IReadOnlyList<Offer> Offers, SearchRanking Ranking);
// OfferDeduplicator.Dedup(IEnumerable<RankingCandidate>) -> IReadOnlyList<RankingCandidate>
// OfferRanker.Rank(IEnumerable<RankingCandidate>, CurrencyCode requestedCurrency, int top = 200)
//     -> RankedOffers
```

- [x] Add failing table-driven tests with these exact expectations; build candidates using existing `OfferPipelineTests` fixture patterns, with fixed clocks/IDs rather than real data:

```text
same currency: B(4500, 5h, 2) < A(5000, 2h, 1)
equal price: A(5000, 2h, 1) < C(5000, 3h, 0)
equal price/time: D(5000, 2h, 0) < A(5000, 2h, 1)
equal price: complete(5000, 2h, 0) < partner(5000, unknown, unknown)
FX failure: RUB 5000 -> RUB rank 1; EUR 100 -> EUR rank 1, fx-unavailable
```

- [x] Add permutation tests for all tie cases including unknowns; change generated IDs while preserving canonical facts and assert identical semantic order. Test 0/1/200/201 candidates and group rank restarts. Test synthetic partner duration 60m remains unknown. Test multi-segment connections, different inbound routes, cabins, fare conditions, source/display currencies and distinct purchase paths/refs survive dedup; exact duplicates collapse deterministically.
- [x] Run focused unit tests and confirm the intended failures. Implement pure comparison keys per spec: currency-group order, decimal amount, known-duration discriminator/value, known-transfer discriminator/value, canonical invariant ordinal key. Do not skip a comparator condition based on only the other operand (that risks nontransitivity). Produce factors from the same values used by sorting; group rank after truncation, empty result has empty entries.
- [x] Implement exact-equivalence dedup as specified; never compare prices across currencies to select a survivor. Generated IDs are only the final representative tie-break for equivalent duplicates, not a customer preference.
- [x] Run focused tests, then Flights unit suite. Verify no Core/event serializer dependency or new I/O was introduced. Deliverable: R1 and pure R2/R3.

## Task 2: integrate normalization and preserve complete cache evidence

**Files:** modify Application `Handlers/Search/SearchFlightsHandler.cs`, `Queries/SearchFlightsQuery.cs`, `Search/ISearchCache.cs`, `Search/SearchCacheKey.cs`; Infrastructure `Cache/SearchCacheRedis.cs`; tests `Tests.Integration/Search/SearchFlightsHandlerTests.cs`, `Tests.Integration/Cache/SearchCacheRedisTests.cs`; update all in-repo `ISearchCache` fakes found by `rg -n 'ISearchCache|OfferRanker.Rank|OfferDeduplicator.Dedup'`.

**Interfaces:** extend `SearchResult(Offers, PartialFailures, SearchRanking? Ranking = null)` for compatibility with existing fake-bus constructors. Actual search handler always supplies ranking, including empty results. Cache becomes `Task<SearchResult?> TryGetAsync(...)` and `Task SetAsync(string key, SearchResult result, TimeSpan ttl, CancellationToken ct)` with internal `{ schemaVersion: 2, result }` JSON wrapper.

- [x] Add RED handler cases: no conversion/native, fake successful FX, fake failed FX, partial provider response then cache hit, all-provider error, empty success, and repeated semantically identical responses with fresh GUIDs. Assert source price is captured before `WithAmount` and never recomputed from the converted offer.
- [x] Add RED cache cases: complete result serialization round-trip, corrupt/null/missing ranking/unknown schema treated as miss, old namespace not read, different locale changes key, repeated read preserves failures and limitations. Test mixed bookable/partner polymorphism with the actual Redis serializer.
- [x] Implement `RankingCandidate` creation during normalization; `Dedup` then `Rank`; save the complete result. On hit return the complete validated snapshot. Use `flights:search:v2:price-first-v1:{hash}` with existing criteria plus locale. Do not flush Redis or change the separate EF deeplink cache schema.
- [x] Recheck Redis fixtures: current named classes use disposable Redis and fake suppliers/FX, without PostgreSQL/schema initialization. Run only these classes locally if that remains true; use fake cache unit coverage if unavailable and retain the actual Redis CI gate. Do not silently substitute mocks as integration proof.
- [x] Verify cold/warm results have identical offer IDs/order, ranking and partial failures. Existing five-minute TTL and quote expiry semantics remain. Deliverable: R2–R4.

## Task 3: additive HTTP and TypeScript contracts

**Files:** modify API `Contracts/Contracts.cs`, `Endpoints/SearchEndpoint.cs`, `Endpoints/NlSearchEndpoint.cs`; TS `shared/ts/api-client/src/flights-search.types.ts`, `flights-search.decoder.ts`, `flights-search.decoder.spec.ts`; add/extend HTTP `tests/Travel.Host.Tests.Integration/Flights/FlightsSearchContractHttpTests.cs`; update affected fake responses in `FlightsEndpointsHttpTests.cs`. The current OpenAPI search response schema is `IResult`; no snapshot change is inferred from an additive DTO. Preserve the real Host snapshot regression gate in CI; verify new response fields with no-DB HTTP/TS tests.

**Interfaces:** spec §5 `FlightRanking`; nullable optional `SearchResponse.Ranking`. Explicit mapping serializes price states to `native`, `converted`, `fx-unavailable` and limitations to the fixed allowlist. Share the response mapping between both search endpoints; no AI request or v1 integration-contract change.

- [x] Add RED HTTP assertions for empty/populated/partial ranking, source/display currency distinctions, no changes to offer discriminants or booking DTOs, and structured/NL mapping parity through fake bus.
- [x] Add RED TS tests: omitted/null legacy metadata accepted without explanation; valid v1 accepted; unknown policy, mismatched offerId, duplicates, missing entries, noncontiguous group ranks, mismatched currency, NaN/negative factors, inconsistent native/converted/failed currency state and known partner factors rejected. Limitations are allowlisted and agree with factor/state values.
- [x] Implement mapping and strict optional decoder. `ranking === undefined || ranking === null` means legacy unavailable; any other malformed value is a contract error. Never accept partial metadata for a subset of offers. Preserve backend order.
- [x] Run TS and no-DB HTTP classes only after confirming `FlightsApiFixture` uses fake bus/store and does not boot Host Program. No TestServer fake-auth result is claimed as live JWT proof. Keep real Host/OpenAPI schema-creating verification in mandatory CI. If CI exposes a real snapshot change, inspect its received artifact and reconcile only the explained schema delta; never disable the snapshot test.
- [x] Deliverable: R5, with additive old-client/new-server and new-client/old-server examples recorded.

## Task 4: factors and limitations in the existing results UI

**Files:** modify `apps/web/src/app/flights/flight-results.ts`, `flight-results.spec.ts`, `flights-page.component.ts/.html/.scss`, `flights-page.component.spec.ts`, `flight-offer.component.ts/.scss`, `flight-offer.component.spec.ts`. Keep quote cards usable without search ranking inputs. Add a focused presentation component only if needed to keep current component/style budgets.

**Interfaces:** result views add nullable ranking facts keyed by offer ID; page summary adds policy availability and currency-group boundaries. The server response remains the sole source of ordering and rank.

- [x] Write RED tests for exact Russian copy and factors: “Сначала цена; при равной цене — длительность и пересадки”, “Длительность неизвестна”, “Курс недоступен: сравнение только внутри EUR”, and “Пояснение сортировки недоступно”. Known-before-unknown is described as completeness of data, not speed.
- [x] Cover native/converted source prices, mixed currency headings, partner preliminary price, empty/partial/error/loading, missing legacy metadata, malformed response, rapid resubmit and late old response. A quote retains its original selected offer; a new search clears prior quote/explanation via existing behavior.
- [x] Render concise visible rule/factors and keyboard-accessible details with `aria-expanded` if disclosure is needed. No weighted scores, “лучший” badge, silent client sorting, invented bag/carbon/refund factors or partner click feature. Use existing typography/layout patterns; no redesign.
- [x] Run API-client and web unit tests, builds and lint. Confirm booking components, owner-bound operation service and redirect draft logic were not modified. Deliverable: UI portion of R6/R7.

## Task 5: fictional demo and browser evidence

**Files:** modify `tests/fixtures/flights-search.json`, `tools/demo/flights-search-api.mjs`, `tools/demo/flights-search-api.test.mjs`, `tests/travel-e2e/demo/flights-search.spec.ts`; retain existing booking/orders/cancellation journeys. If adding fixtures, keep them in the same fixture directory and label them fictional.

- [x] Add RED Node/browser cases with explicit known expected response ordering and factors for equal prices, partial itinerary and FX failure. The demo supplies fixed contract fixtures; it does not implement a second backend ranking algorithm that could conceal disagreement.
- [x] Ensure date-shifted demo requests retain correct factors for the represented route. Stub rejects bearer credentials and performs no external HTTP. Existing single-passenger booking limits remain in M2.1.
- [x] Run `npm.cmd run test:flights-demo`; prove factors/currency boundaries, keyboard disclosure, error/retry and old-response rejection, then unchanged quote → fictional hold/confirm → own orders → B5 cancellation journeys. Assert URLs, storage and console contain no fictional passenger fields or tokens. Block unexpected external requests in the browser harness.
- [x] Inspect desktop and 360px screenshots, focus order and horizontal overflow. Record results as demo UI evidence, not live provider, payment or auth acceptance. Deliverable: R6/R7.

## Task 6: verify, independently review and deliver the bounded increment

**Docs:** update this spec/plan status, `docs/architecture/current-state.md`, ranking statement in `docs/adr/0014-mixed-aggregation-bookable-deeplink.md`, relevant README wording and a new `docs/superpowers/results/2026-10-02-flights-m2-ranking-local.md` (use actual execution date if later). Clarify the concept's historical “AI feature #2”: M2.1 is deterministic and has no AI runtime dependency. Do not rewrite B5 decisions or the M3 brief.

- [ ] Run applicable gates below once after final changes; repeat only for actual changes/failures. Record passed/local vs CI-only vs not attempted separately, with no invented counts.
- [ ] Self-review R1–R7 and the five review focus items. Obtain the user-authorized independent review of the complete diff, including new files/docs, and resolve meaningful findings with focused regressions. No review is claimed from this research self-check.
- [ ] Commit all selected-increment implementation/tests/docs on its verified branch; push; create and attach a PR targeting dev. Use the repository PR template if present. Link `/pull/<number>/changes` when requesting the user's review. These actions are authorized only after the current joint design/plan approval.
- [ ] Await all mandatory checks on the actual PR HEAD. Paid evals remain excluded. Do not bypass failures or change CI/CD; resolve in-scope failures, explain unrelated or unexpected blockers. Merge only after required gates and repository review rules pass.
- [ ] Verify PR merged/closed and resulting dev SHA. Fetch; update clean local dev by fast-forward only. Verify task changes are present and no unpublished commits/local docs remain. Archive only this task's managed worktree using the app tool; clean only owned task branches/temp artifacts after preserving needed evidence. Never touch another task's work.

### Verification commands and fixture gates

Commands run from the approved implementation worktree on Windows PowerShell. Restore tools/dependencies only as needed; never expose configured secrets. Use the repository-supported Node runtime.

```powershell
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit/Travel.Modules.Flights.Tests.Unit.csproj --configuration Release --maxcpucount:1
dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration/Travel.Modules.Flights.Tests.Integration.csproj --configuration Release --maxcpucount:1 --filter "FullyQualifiedName~SearchFlightsHandlerTests|FullyQualifiedName~SearchCacheRedisTests"
dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --configuration Release --maxcpucount:1 --filter "FullyQualifiedName~FlightsSearchContractHttpTests|FullyQualifiedName~FlightsEndpointsHttpTests|FullyQualifiedName~ReadmeRequestExamplesTests"
dotnet test tests/Travel.Tests.Architecture/Travel.Tests.Architecture.csproj --configuration Release --maxcpucount:1
dotnet test tests/Travel.Tests.Contract/Travel.Tests.Contract.csproj --configuration Release --maxcpucount:1
npx.cmd nx test api-client --skipNxCache
npx.cmd nx test web --skipNxCache --watch=false
npm.cmd run test:flights-demo
npx.cmd nx build web --configuration=production
npx.cmd nx build web --configuration=flights-demo
npx.cmd nx run-many -t lint -p web api-client
npm.cmd run check:ai-harness
npm.cmd run check:dotnet-inventory
npm.cmd run check:readme-examples
dotnet csharpier check .
npx.cmd biome ci .
git diff --check
```

Before the Redis/HTTP commands, inspect **all selected fixtures and collection fixtures**, not just test names, for schema application and external calls. Current named Redis suites use fake providers/FX; current lightweight HTTP fixtures use fake bus/store. Do not broaden the filter to database/Host/AppHost suites. Mandatory CI covers actual Host OpenAPI, DB/outbox/projection and Aspire lanes. No paid evaluation is needed for deterministic ranking.

## Self-review coverage

R1 → Task 1; R2/R3 → Tasks 1–2; R4 → Task 2; R5 → Task 3; R6 → Tasks 4–5; R7 → Tasks 3–6. There is no event/PII migration task hidden under ranking. Cache namespace and payload evolve together. UI never assumes partner duration is known. Source prices remain separate from converted display values. Every local database-starting path remains behind fixture inspection, and the approved lifecycle ends only after merge/dev synchronization/cleanup.
