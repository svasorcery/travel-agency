# Flights M2: scope, sequence and first increment

**Status:** M2.1 specification and plan approved by the user on 2026-10-02, conditional on a second self-review. That review passed; implementation, local verification and independent review are complete; remote CI/merge evidence belongs to the delivery record. The later M2 increments remain design directions.

**Recommendation:** begin with M2.1, deterministic explainable search ordering. Then introduce protected booking passenger snapshots, multi-passenger booking, saved travelers and multi-leg search as separately accepted increments. M2.1 is not completion of M2.

**Evidence base:** `git fetch origin dev` on 2026-10-02 resolved to `d75186054a33de0e1cb688da7e3e7cc42927dadf`. `git merge-base --is-ancestor d75186054a33de0e1cb688da7e3e7cc42927dadf origin/dev` returned 0. The equality was observed after fetching. The main dev checkout was clean. A new managed worktree at `C:\Users\Vladimir_sva\.codex\worktrees\flights-m2-design\travel-agency` has that exact detached HEAD; no archive was restored. [CI run 37020342377](https://github.com/svasorcery/travel-agency/actions/runs/37020342377) was independently read: completed/success, push, same SHA. This is baseline evidence, not validation of M2.

## 1. Purpose and fixed boundaries

The demonstration should explain how a user selects an offer, supplies several fictional passengers, optionally reuses private traveler profiles, and searches independently chosen journey legs. Every slice must have a usable browser demonstration and evidence for its real backend contracts. A fixture demonstration does not establish live supplier behavior.

- Only fictional data. No real supplier, payment, Anthropic or other paid API calls; skip paid AI-evals and never enable `run_paid_ai_evals`.
- Tokens and client operation state stay in memory. No PII in URLs, browser storage, history state or logs. Fake auth exists only in the isolated demo build/test fixtures; backend JWT/owner checks remain intact.
- No local schema creation/application, migrations or deployment. Inspect fixture/startup behavior before any Host/AppHost or test execution. Existing mandatory CI performs its disposable database checks; do not change CI/CD or bypass checks.
- B5 remains accepted. Duffel cancellation returns unsupported before HTTP; only the fictional demo demonstrates cancellation. Refunds, ancillaries, SSE, Support, durable cancellation recovery and the OpenSpec pilot are outside M2.1. The [M3 brief](2026-10-02-flights-cancellation-openspec-pilot-brief.md) remains deferred.
- The joint design/plan approval now authorizes implementation, tests, independent review, commit/push, PR into dev, mandatory green CI, merge, dev synchronization and cleanup. No extra publication approval is needed after that gate.

## 2. Verified M1 constraints and contract gaps

Paths below are relative to the repository. F = `modules/flights`; C/A/I/API = `Travel.Modules.Flights.Core/Application/Infrastructure/Api` within F.

| Area | Current source evidence | M2 consequence |
| --- | --- | --- |
| Search party | C `ValueObjects/SearchCriteria.cs`: count must be 1. API `Contracts/Contracts.cs` accepts an integer, but TS `flights-search.types.ts` uses literal `1`; `flight-search-form.ts` emits 1. | A wider HTTP integer alone does not enable multi-passenger. Search must price the exact passenger composition. |
| Passenger identity | C `PassengerInfo` contains names, birth date, gender, email and phone, without a local or provider passenger ID. I `DuffelOfferDto` omits offer passengers; `BookableOffer` has no passenger slots. | Add explicit quote-scoped slots and provider references; never infer identity from name, email or array index. |
| Hold | API `HoldOfferEndpoint.cs` requires `Passengers.Length == 1` and uses `[0]`; command, handler and `IFlightBookingProvider.HoldOfferAsync` take one passenger. TS uses tuple `[FlightPassengerInfo]`. | Array syntax in the old M1 spec is not end-to-end compatibility. Count, uniqueness and quote-to-passenger binding need validation before side effects. |
| Provider mapping | I `DuffelFlightSearchProvider.BuildRequest` always sends one adult. Booking mapper sends one adult, no passenger `id`, and maps unspecified gender to male. | Real mapping is not proved by the demo. Preserve supplier passenger IDs; reject unsupported identity fields rather than inventing them. [Duffel's guide](https://duffel.com/docs/guides/getting-started-with-flights) explicitly binds booking passengers to offer-request IDs. No provider API was called. |
| Routes | C `Itinerary.Create` permits 1–2 slices; two must have mirrored endpoints. `IsRoundTrip` and API `ItineraryDto.From` equate two slices with return travel. Search stores origin/destination/departure/optional return. | Open-jaw already fails with two slices; multi-leg needs explicit ordered leg criteria and journey kind, not a larger array limit alone. Segments within one slice are connecting flights, not independent requested legs. |
| Offers and quote | `OfferQuoted` saves route, price, expiry, provider ref and fare conditions; no party. `OfferReQuoted` optionally carries refreshed `BookableOffer`. `QuoteOfferHandler` refreshes from supplier; hold reconstructs the offer from the aggregate. | Persist quote party/binding through first quote, re-quote, replay and hold. Invalidate acceptance when party or route changes, even without a price change. Quote has no HTTP idempotency key. |
| Events and projection | C `OfferHeld` and `BookingAggregate.Passenger` are singular. I `OrderReadModelEventApplier` serializes plaintext passenger JSON and rejects unsupported event types; `BookingAggregateConfig` explicitly registers events. | New events require aggregate replay, registration, catch-up, rebuild, validation and notification-readiness changes together. Old event types cannot simply be renamed or reinterpreted. |
| Read/HTTP model | EF `PassengerInfoJson` is jsonb; `OrderView` contains it but public `OrderResponse` omits PII, passenger count and HeldUntil. Tickets are a flat string array. | Add only necessary public count/summary fields; do not return raw profiles. Flat ticket numbers cannot prove passenger/segment ticket assignment. |
| Auth | Identity validates issuer/audience; normalized GUID sub supplies owner. Writes require `flights:book`; GET/list require authenticated owner. Frontend additionally requires book scope at sign-in. | A traveler is not a Keycloak user. CRUD must derive owner server-side, use no-store and hide foreign IDs. Do not expand Identity internals or weaken current auth. Prior local claims evidence is not current live issuance proof. |
| Idempotency/unknown | Middleware scopes key by owner/route and hashes method/path/raw bytes; successful response retention is 24h. Hold has no provider idempotency parameter; external effects precede optimistic append. Confirm has stable aggregate-key propagation, not an end-to-end exactly-once guarantee. | Freeze party order and serialized body during uncertain writes. Do not promise recovery across reload/server crash. B5 confirm/cancel memory and retry cutoff do not automatically cover hold. Audit hold separately in multi-passenger work. |
| UI/demo | Angular implements B1–B5, one passenger, owner list/details and in-memory confirm/cancel operations. Search decoder rejects >2 slices. `tools/demo/flights-search-api.mjs` enforces count/array length 1 and assigns dates as outbound/return. | Extend types, decoders, forms, quote comparison, fixture validation and E2E together. Node demo is not a provider implementation. |
| Ranking | A `OfferRanker` sorts amount/duration/random offer GUID, top 200. FX failure retains original currency. Dedup uses only the first segment of every slice, without currency. Travelpayouts maps one synthetic segment and may use 60 minutes as fallback. | Numeric cross-currency comparison, false duration/transfer factors and lossy dedup are direct blockers to truthful explanations. GUID tie-break is stable for one snapshot, not repeated equivalent supplier responses. |
| Cache | `ISearchCache`/`SearchCacheRedis` store only offers; hit returns empty partial failures. Key omits locale. | Cache must preserve ranking evidence and incomplete-search status; change key namespace so old payloads cannot imply a new ranking policy. |

**Documentation drift:** root/module AGENTS still call the frontend absent/foundation-only; B1–B5 source and local reports supersede that historical description. The M1 spec's single PII store, generated TS/discriminator assumptions and simple array-extension claim are not the current contract. ADR 0015 explicitly records the singular event; ADR 0017 now describes raw-byte hashing. `current-state.md` includes B4 but omits B5. Do not reopen completed frontend work to reconcile every historical sentence.

Read together: [concept §7](2026-05-03-travel-platform-concept.md), [M1 design](2026-05-13-flights-m1-design.md), [current architecture](../../architecture/current-state.md), [search frontend](2026-09-28-flights-m1-search-frontend-design.md), [B1–B3 design](2026-09-29-flights-m1-booking-frontend-design.md), [B4](2026-09-30-flights-m1-b4-own-orders-design.md), [B5](2026-10-01-flights-m1-b5-own-order-cancellation-design.md), their dated reports under `docs/superpowers/results`, and ADRs 0006/0007/0010/0013–0017/0020/0023.

## 3. Options and recommended increments

| Order | Benefit | Cost / limitation |
| --- | --- | --- |
| **A: ranking → protected booking snapshots → multi-passenger → saved travelers → multi-leg** | First result has no event/EF migration or PII changes. Later booking expansion builds on a protected representation. | Several releases; family booking arrives after its data foundation. Recommended. |
| B: protected multi-passenger → saved travelers → multi-leg → ranking | Main M2 booking headline arrives first. | First slice combines cryptography/key lifecycle, event evolution, provider identity and UI mutation risks. Larger review and CI surface. |
| C: multi-leg search → ranking → protected booking → multi-passenger → saved travelers | Demonstrates itinerary modeling early. | Quote/order views share the itinerary model; incomplete Travelpayouts and legacy DTOs make this substantially more than a search form. |

Recommended delivery boundaries (each receives its own detailed approved plan):

1. **M2.1 — explainable price-first ranking**, specified below.
2. **M2.2 — protected booking passenger snapshots for the existing one-passenger flow.** Versioned event/persistence representation, key lifecycle and all new PII sinks covered; replay/rebuild retains legacy semantics. Source migrations only if separately approved for that slice; never local apply.
3. **M2.3 — 1–9 adults, one-way/return, manual entry.** A proposed product limit, not a claim every supplier accepts every party. All passengers must be adults on the first departure under the selected provider rule, validated from birth dates. Quote has exactly the slots being booked, whole-order hold/confirm, group total, per-person fields and no partial success. Children/infants require age-at-travel and adult/infant association design in a follow-on before claiming family support.
4. **M2.4 — owner-only saved travelers.** CRUD, explicit opt-in save and prefill into M2.3; edits/deletion of a profile cannot change an existing booking snapshot. No passport/loyalty fields until an actual supported booking contract needs them.
5. **M2.5 — ordered 1–4 search legs, including two-leg open-jaw.** Proposed demo bound, not an assertion of provider maximum. Retain M1 request shape as an adapter; introduce explicit multi-leg request/version and `journeyKind`. Legacy round-trip remains mirrored. Validate each leg and date order; surface ground gaps rather than pretending a connection. Quote/UI/read DTO consumers must safely represent the selected full itinerary; one order still selects one complete offer, never one independent order per leg. Unsupported providers are explicitly skipped, not fed a truncated route.

Dependency rules: saved travelers require protected storage and owner contracts; multi-passenger requires stable quote bindings and protected snapshots but not saved profiles. Multi-leg can follow ranking independently of saved travelers, but must reuse party contracts once available. Ranking consumes group totals and sums slice durations, so its policy survives both extensions. The v1 NL message remains one-way/return-shaped; no silent multi-leg downgrade, no new LLM work or calls in these increments.

## 4. Data, compatibility and PII decisions for subsequent slices

These are design directions for M2.2–M2.5, not authorization to implement them with M2.1.

- Separate `TravelerProfileId` (owner's reusable profile), `BookingPassengerId` (immutable booking slot), and opaque provider passenger reference (bound to the accepted offer). Reordering UI rows never changes bindings. Re-quote that changes the set requires new explicit review; missing/duplicate/foreign IDs fail before HTTP.
- Preserve legacy `OfferHeld` and its exact serializer identity/Apply behavior. M2.2 refines the next version to a singular `OfferHeldV2` with owner and a protected snapshot, as approved in [the PII design](2026-10-02-flights-m22-pii-protection-design.md). M2.3 must introduce a further event version for ordered booking passenger IDs and count/type; it must not reinterpret the singular V2. New quote party fields must be versioned/additive and survive `OfferReQuoted`; legacy quotes missing bindings require refresh, not fabricated provider IDs. Legacy ownerless orders remain readable for replay and fail closed for owned operations, as ADR 0015 requires.
- Keep transition policy separate from replay. New reader/projection supports old and new events before emitting V2; old writers/readers cannot run against new events during rollback. Deployment/backfill/rebuild execution is a later explicit operational gate. Do not rewrite historical streams or run a migration as part of design.
- Projection copies protected envelopes without decrypting them; status/list reading and stream replay need no PII key. New public DTOs expose passenger count and safe slot IDs only when useful. Ticket-to-passenger associations stay unknown unless supplied and mapped explicitly. Never zip ticket arrays to passenger arrays.
- Recommended free demo implementation: an Application PII protection port with an Infrastructure ASP.NET Core Data Protection implementation, versioned purpose strings bound to owner/entity/payload version, and a durable key ring outside Git, protected at rest (certificate for portable hosting). No custom cipher implementation or paid KMS requirement. Missing keys/tampered ciphertext/wrong purpose fail closed; no plaintext fallback. Old keys remain available while ciphertext depends on them, with backup/restore and rotation tests. Microsoft notes that [indefinite confidential storage is not the primary Data Protection use case](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/introduction?view=aspnetcore-10.0); [explicit key persistence also needs explicit at-rest protection](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0). Production retention/key custody is not proved by a demo.
- New protected booking snapshots and saved profiles have different purposes. Prefill decrypts only after authorization and copies fields into a new booking snapshot; later profile deletion removes the profile, not booking evidence. No names/email in lookup keys or unencrypted labels. EF stores profile ciphertext and minimal owner/version metadata; no PII search index.
- Scope the PII inventory beyond profiles: existing `OfferHeld` and `PassengerInfoJson` are plaintext; `DuffelWebhookIngestionPort` stores raw payload; Duffel confirm logs `RawBody`. M2.2 must protect/minimize new raw inbox payloads after signature verification and remove unsafe body logging; inspect durable Wolverine envelopes, diagnostics, notification rendering and cached responses too. Idempotency currently stores body hashes and successful non-PII response bodies, not raw request bodies. Do not broaden new DTOs to leak PII there.
- Historical fictional plaintext remains a documented exception; encryption of new writes does not retrospectively protect old events/backups. Any rewrite, purge or cryptographic erasure needs a separate retention/data decision. No real data may be introduced to make the demonstration realistic.
- The B2 baseline used a short-lived `sessionStorage` quote intent across OIDC, removed in M2.2. This is a legacy exception, not a pattern for M2 operation state. M2.1 does not invoke or extend that storage. The next booking slice removes this dependency: sign in before entering the passenger flow; redirect/reload can require a fresh search/quote, never persist a party/key/body. Preserve opaque existing order links only.

## 5. M2.1 contract: explainable ordering without an LLM

**User result:** anonymous search shows why results are ordered, the actual factors used, and where comparison is unavailable. No claim of a universally best flight, cheapest market price, carbon benefit, baggage allowance or refund quality.

Choose a lexicographic policy `price-first-v1`, not subjective weights: lower displayed total first; equal price → known full itinerary duration before unknown, then shorter duration; remaining ties → known transfer count before unknown, fewer transfers, then a canonical ordinal key. Duration sums slice durations (including intra-slice layovers), not time spent at the destination. Whole seconds are used consistently for comparison and explanation; sub-second differences are ties. Transfers sum `segments.Count - 1` per slice. Prices use decimal on the server.

Currency defines comparison groups. Requested currency comes first; other currencies follow in ordinal ISO-code order with separate headings and ranks restarting at 1. There is no cross-currency winner. Successful FX conversion uses the requested currency and records source price/currency; failed FX keeps its actual currency and `fx-unavailable`. A group order is navigation, not a price preference. Bookable and partner offers remain interleaved within each currency; amend ADR 0014 only for honest currency comparison, not separate purchase-type sections.

In M2.1, bookable itineraries provide duration/transfers. Partner summaries always have those factors `null` with `partial-itinerary`, even if their synthetic itinerary contains a positive duration. Known-before-unknown on equal price is explicitly a data-completeness tie-break, not proof the unknown route is slower. All prices remain preliminary; partner price is labeled as reported by partner, requiring verification there. Quote stays authoritative for booking.

Dedup must not discard distinct connections/return legs, fare terms, currencies or purchase paths. Use conservative exact-equivalence grouping across provider, purchase kind, provider reference/link, source and displayed amounts/currencies, every ordered slice/segment (airports, instants, carrier/flight, cabin), and bookable fare conditions/expiry. Ignore generated offer ID/fetched time for equivalence. Never merge bookable with partner or distinct provider refs. Choose newest fetched duplicate, then GUID for indistinguishable duplicates. Canonical stable ordering uses those normalized fields with ordinal/invariant formatting, not random GUID alone. Limit to 200 only after full ordering; ranks explain the returned candidate set, not all provider inventory.

Add optional top-level `ranking` to existing `SearchResponse`; keep `offers` and `partialFailures` and all booking DTOs unchanged:

```typescript
interface FlightRanking {
  policy: 'price-first-v1';
  requestedCurrency: string;
  entries: {
    offerId: string;                 // exactly one entry per returned offer, same order
    currency: string;                // equals offer.currency, defines group
    rank: number;                    // contiguous within currency, starting at 1
    sourceAmount: number;
    sourceCurrency: string;
    priceState: 'native' | 'converted' | 'fx-unavailable';
    durationSeconds: number | null;
    transfers: number | null;
    limitations: ('partial-itinerary' | 'fx-unavailable')[];
  }[];
}
```

Server responses include `ranking`; optionality permits a new UI to read an older backend. Absence displays “Пояснение сортировки недоступно” and preserves backend order. A present but malformed/unsupported policy, duplicate/missing offer ID, inconsistent currency/rank, invalid number or contradictory factor is a contract error; do not synthesize an explanation. The UI never re-sorts or calculates its own winner. Short text states the rule and numbers; an accessible disclosure can explain unknown factors. The same endpoint mapping is used by structured and NL search, but NL is tested with a fake bus only.

Change the Redis search payload to a versioned complete `SearchResult` (offers, failures, ranking). Use `flights:search:v2:price-first-v1:{hash}` and include locale and all current criteria. Old namespace is a miss and expires naturally; no flush/migration. Cold/warm hits preserve ranking evidence and provider-failure codes; the banner describes incompleteness when this snapshot was fetched, not current provider health. Keep the current five-minute cache behavior; neither ranking nor cache implies a quote is still bookable. This targeted cache change is needed for truthful explanations, not a general caching project.

## 6. UI states, errors and unknown outcomes

| State | Required behavior |
| --- | --- |
| Initial/loading/new criteria | No ranking before a successful response. Clear prior explanations with old offers; discard stale responses through existing request cancellation. |
| Ready | Rule visible, per-offer factors available, backend order unchanged, currency group headers only when needed. No “best” badge. |
| Partial provider/FX | Keep usable offers, show partial snapshot and per-offer limitations. No invented time, zero-transfer claim or global cheapest claim. |
| Empty/all providers failed | Existing distinct empty/error states; no ranked placeholders or fallback fixture data in API mode. |
| Legacy response | Offers still usable, explanation unavailable. A malformed present ranking fails decoding. |
| Timeout/retry | Search retry is explicit; no automatic booking action. Quote response lost remains unknown; ranking does not recreate or accept a quote. |
| Quote/hold/confirm/cancel | Preserve B1–B5 behavior and owner/unknown barriers. Selecting a rank never authorizes a booking. Confirmed is not Ticketed. |

For later booking slices: freeze the ordered party/body/key after dispatch; validation errors identify a safe slot/field without echoing PII; changed quote/expiry needs renewed review. Timeout, malformed success, conflict or auth loss after dispatch never imply rejection. Read-model lag is distinct from unknown external outcome. Logout clears visible PII and invalidates late responses; reload loses attempts, shows a read/review path and never automatically retries a write. Durable provider recovery remains a separately designed capability, not a browser-storage workaround.

## 7. Acceptance and proof

| ID | Observable requirement | Test level |
| --- | --- | --- |
| R1 | Same candidate facts, different arrival order/generated IDs → same semantic order and factors; price/duration/transfers/ties and top-200 boundary are explicit. | .NET unit, permutation cases |
| R2 | RUB 5,000 and unconverted EUR 100 never compete numerically; native/converted/failed FX retain correct source facts, groups and ranks. | Unit + handler with fake FX |
| R3 | Partner synthetic duration/segments never become known factors. Different connections, return legs, currencies, refs or purchase kinds survive dedup. | Unit + provider fixture regression |
| R4 | Warm cache equals cold ranking/partial evidence; old/corrupt payload is a miss; locale/policy changes cannot reuse incompatible data. | Serializer/unit + disposable Redis integration |
| R5 | Real HTTP serialization matches TS decoding; legacy absence accepted, malformed present metadata rejected; `/search/nl` mapping parity without AI calls. | No-DB HTTP + TS contracts; actual Host OpenAPI snapshot in CI |
| R6 | Desktop/mobile/keyboard can read factors and limitations; empty/partial/error/retry/stale response states remain honest; quote and B2–B5 regression journeys still work. | Angular unit + fictional Playwright + visual inspection |
| R7 | No PII, token, operation state persistence or external API calls added; no booking event/EF schema changes, auth/CI changes or paid evals. | Diff/architecture review + demo network/storage assertions |

No tests or servers ran during this research. Source inspection confirmed `FlightsEfInitializer.MigrateAsync` and `FlightsMartenInitializer.ApplyAllConfiguredChangesToDatabaseAsync(AutoCreate.All)` in non-Production despite `AutoCreate.None` in Host registration. Full local Host/AppHost/database suites are therefore excluded. No-DB HTTP, pure unit and isolated Redis fixtures can be selected after rechecking their initialization; mandatory CI owns schema-creating suites. Historical passing reports are not current M2 test results.

Subsequent slices additionally need legacy serialized-event replay, owned/ownerless cases, mixed-version projection/rebuild, ciphertext-at-rest and key-rotation/tamper tests, owner-isolation HTTP tests, party binding/permutation/count tests, failed/unknown writes, multi-leg topology and DTO-version tests. These are prerequisites for those slices, not work hidden in R1–R7.

## 8. Self-review and approval boundary

Self-review checked four-feature coverage, event/PII compatibility, deterministic total ordering, mixed-currency and synthetic-factor behavior, cache parity, legacy clients, test startup side effects and the user's exclusions. Corrections incorporated: do not rank unknown duration as zero; do not lose failures on cache hit; do not claim new encryption protects history; do not treat B5 retry guards as hold guards; do not reuse sessionStorage for M2 operation state.

The first-increment [implementation plan](../plans/2026-10-02-flights-m2-explainable-ranking.md) passed a second self-review and was approved for its bounded delivery cycle. Later increment contracts, source migrations and implementation require their own scope approval. These documents travel with the implementation PR; delivery remains incomplete until merge, dev synchronization and task cleanup.

Second self-review clarified integer-second factors, malformed cache misses, and conservative purchase dedup. Source inspection during execution also established that the current search OpenAPI response describes `IResult`, not `SearchResponse`; this slice verifies additive response fields through real no-DB HTTP serialization and the TS decoder. The existing Host OpenAPI snapshot remains a CI regression gate; improving its response schema is not silently included.
