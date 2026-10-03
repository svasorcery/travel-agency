# Flights M2.3b Multi-Passenger Booking Implementation Plan

> **For agentic workers:** after approved spec/plan and completed M2.3a, use `superpowers:subagent-driven-development` for independent owned tasks or `superpowers:executing-plans` inline. This file does not itself authorize implementation.

**Goal:** deliver search → quote → protected hold → confirm/read for 1–9 fictional adults with exact supplier passenger bindings and group totals.

**Architecture:** extend normalized Core offer facts; persist quote revision/local slot to supplier-ref mapping; encrypt the whole party before bus dispatch; emit OfferHeldV3 and copy its protected snapshot/count through projection. Existing auth/idempotency and A's current-tab safety boundaries remain.

**Tech stack:** existing .NET/Marten/Wolverine/EF, Angular, xUnit, fake provider HTTP and isolated demo. No paid SDK/service.

**Spec:** [M2.3 design](../specs/2026-10-03-flights-m23-design.md), sections 6–11, P1–P7; [A plan](2026-10-03-flights-m23a-booking-safety.md) is a prerequisite.

**Status:** approved conditional on the non-overengineered scope; A has been reduced accordingly. Fresh research base `02356dfc5010172548bb45938bc1bf29deae6dcd`; implementation base must be freshly fetched dev **after A merge**, not this old SHA or current HEAD.

## Execution checkpoint

Source tasks 0–5 and the fictional vertical are implemented and locally verified; the detailed checklist below retains the original planned proof obligations, including CI-only ones. [Result report](../results/2026-10-03-flights-m23b-local.md) records actual evidence and fixes. Database/Host/outbox/concurrency tests remain mandatory CI gates; delivery is not complete until merge and cleanup. New B base is `f2162b323f33b7ed9c9fe86d7c95a5a1f324e596` after PR30/31; the original design base is historical.

## Global constraints

- Fictional data only; no real provider/payment/Anthropic/paid API or paid evals. No local Host/AppHost/schema apply/key provisioning/deployment. Inspect fixtures; DB/durable/Aspire acceptance runs in CI.
- Backend JWT/owner/flights:book and PII protections remain; all client drafts/operations/tokens in memory. No fake backend auth outside test/demo.
- Preserve V1/V2 event identities and singular purpose. New legacy-client hold requests without explicit quote/slot binding are deliberately rejected; no array-index/profile-ID inference or silent downgrade.
- PassengerCount source migration/snapshot/offline SQL review must be explicitly approved with the design. No database update or historical event/ciphertext rewrite.
- No children/infants/passports/loyalty, saved-profile implementation, multi-leg implementation, ancillary/refund/SSE/Support/M3/OpenSpec or CI/CD expansion here. M2.4/M2.5 follow, so completing B does not complete the chat objective.

## Review focus

1. Reordered/replaced provider passenger IDs with unchanged count/price must not bind the wrong person — Tasks 1–3.
2. Old warm cache or legacy quote JSON must not fabricate party facts/IDs or downgrade mandatory hold revision — Tasks 2–3.
3. Protected command and event can look safe while one passenger/title/contact escapes in errors, traces, read DTO or helper ToString — Tasks 1/3/4.
4. Advanced V3 checkpoints, source-derived count  must converge without PII reads/decryption — Task 4.
5. Nine-person form edit/auth/navigation after dispatch must not mutate the frozen body or clear A's unresolved hold barrier — Tasks 5–6.

## Task 0: new base and interface checkpoint

- [ ] Confirm A PR merged/closed, mandatory CI green, primary dev clean. Fetch origin/dev; verify A checkpoint ancestry and record exact SHA. Create a new managed worktree/branch from it; do not reuse or force-move an old branch.
- [ ] Read actual A implementation, this spec/plan, root/module instructions and ADRs 0006/0007/0015–0019/0024 including A's amendments to ADR0016/0019. Resolve plan-vs-code signature changes explicitly in the ledger, not silently.
- [ ] Record ownership: coordinator owns shared Core/HTTP/TS contract signatures, composition and migration snapshot. Backend and frontend implementation can run in parallel only after that contract task is fixed; no overlapping writers.

## Task 1: party identities, protected value and versioned quote/hold facts

**Files:** Core `ValueObjects/SearchCriteria.cs`, `ValueObjects/Offer/BookableOffer.cs`, new quote/party/title/binding value objects (including persisted FirstDepartureLocalDate) and `ProtectedPassengerPartySnapshot`; `DomainEvents/OfferQuoted.cs`, `OfferReQuoted.cs`, new `OfferHeldV3.cs`, `Aggregates/BookingAggregate.cs`; freeze the target provider hold signature here and replace it atomically with handlers/Duffel/fakes in Task 3. Application new `Privacy/IBookingPassengerPartyProtector.cs`; Infrastructure party protector using existing private crypto provider. Keep old PassengerInfo/protector/V2 event unchanged.

**Build boundary:** no temporary singleton/default-interface fallback. Task 1 stays compilable through trailing optional event/offer fields, but new hold guards fail closed until Tasks 2–4 are integrated. No partial B release.

**Produces:** validated `QuoteBinding(revision, ordered slots)`; local BookingPassengerId distinct from opaque supplier ref; `BookingPassengerDetails(PassengerInfo, title)`; whole-party protected snapshot; new V3 hold. Optional trailing quote binding preserves historical JSON, but new writes require it.

- [ ] RED factories: count 1,2,9 vs 0/10/fractional wire input; empty/duplicate IDs; title/gender explicit choices; per-slot safe validation; adult cutoff from preserved origin-local calendar date, including offset-less wire JSON/offsets/leap date; explicit name/email/phone/ref size bounds; no identifier reuse as profile ID.
- [ ] RED actual crypto: purpose bound to aggregate/owner/revision/count, full party roundtrip, wrong context/tamper/shape rejected, ToString/messages/serialized command lack PII. Disposable owned keys only.
- [ ] Implement opaque/value types and protector. Validate decrypted shape; clear temporary byte arrays; do not infer identities from order/name/email. Preserve all legacy purpose strings and replay behavior.
- [ ] RED/GREEN pure aggregate V1/V2/V3 and optional quote binding replay; old quoted stream cannot hold without fresh binding; a later legacy re-quote without binding invalidates an earlier one rather than retaining stale IDs. Add all event registration/projection selectors needed for compile; final projection behavior belongs to Task 4.

## Task 2: exact search party and provider ACL

**Files:** Infrastructure Duffel `DuffelFlightSearchProvider.cs`, `DuffelOfferMapper.cs`, offer/root/segment passenger DTOs, booking provider request mapping; Travelpayouts provider; Core search support result/port; Application search handler/result/cache/ranking/dedup; Redis cache envelope and tests; search HTTP/TS contracts.

**Consumes:** Task 1 normalized passenger facts. **Produces:** each bookable offer has validated count and supplier refs/capability facts; skippedProviders distinct from failures, preserved in cache.

- [ ] RED fixture mappings for 1/2/9 adult search descriptors and valid returned root/segment IDs; missing/duplicate/foreign IDs, mismatched count or cabin, malformed capabilities and unsupported adult type fail honestly.
- [ ] Map IDs/types/hold/document requirements and normalized origin-local calendar date behind Infrastructure before UTC conversion; do not use host timezone. Require explicit hold support and no required identity documents before booking. Handle unsupported offers with safe capability reason; no instant/passport fallback.
- [ ] Add small support decision before provider fan-out; Travelpayouts count>1 is skipped with zero HTTP calls, never multiplied prices/singleton deeplink. Disabled/unsupported is not provider outage.
- [ ] Bump cache key namespace/envelope and validate party/capability count against request. Extend dedup facts without using random local quote IDs. Ranking remains price-first-v1 over group totals, correct currencies and unknown partner factors.
- [ ] RED/GREEN cold/warm parity, old-cache miss, mismatched cache count, provider failure vs skip (all eligible fail with a skipped provider still returns unavailable; all ineligible is explicit unsupported/empty), NL v1 count bounds with fake AI only. No external supplier requests.

## Task 3: quote revision, encrypted party hold and HTTP transition

**Files:** Application quote/hold commands/results/handlers; Api Contracts/endpoints; Core booking port; Duffel passenger wire mapping; HTTP fixtures/README catalog; TS search/quote/booking DTO/decoders/services. Existing `/quote` and `/hold` routes remain; no receipt route is introduced.

**Contract:** quote accepts expected count (legacy omission=1) and returns revision + local slot IDs/count. Hold requires exact current revision, unique exact slot set and explicit title/details. Server owns supplier-ref lookup. New requests without binding receive safe upgrade/re-quote error before provider effects. Old held orders still confirm/read under A.

- [ ] RED quote/re-quote: mismatched searched/provider count, same IDs reordered, replaced refs at same price, stale revision, changed route/price; preserve IDs by ref lookup only. Every successful re-quote changes revision and invalidates acceptance; party count change needs new search/aggregate.
- [ ] API validates shape/current personal factories, protects entire body before bus. Handler owner/state/expiry/revision guards precede decrypt; validate every adult/member before one supplier call. There is no partial booking success.
- [ ] Wire maps exact persisted supplier id and explicit title/m/f/date/contact, omitting guessed adult type and never converting unspecified gender to male. Names/field rules are validated pre-HTTP with safe slot/field codes, not echoed values.
- [ ] On success append V3 with original protected ciphertext and count using current atomic reconcile boundary. Confirm still pays one accepted group total through A, not per-person multiplication/looped payments.
- [ ] RED/GREEN no-DB HTTP captured-command privacy/auth/request compatibility; CI handler success/guard/outbox/concurrency tests. A's memory attempt freezes the exact new raw body and never repeats a potentially dispatched unknown hold.

## Task 4: count projection, schema source and historical proof

**Files:** Infrastructure event registry, entity/config/read queries/event applier/reconciler, `Migrations/*AddOrderPassengerCount*` and snapshot; Application OrderView; public OrderResponse/TS order decoder; read/rebuild/convergence tests. Coordinator owns migration snapshot after A.

- [ ] Add PassengerCount NOT NULL default1 with bounds1..9 through explicitly approved source migration; inspect Up/Down and offline SQL. Existing V1/V2 rows/events remain singular; do not rewrite event log/ciphertext.
- [ ] V1/V2 Apply writes1; V3 writes validated count and exact party envelope. Include V3 in prefix owner checks, FieldsMatch, materialization and validation/reset. Preserve existing booking status/version semantics; do not add confirmation workflow state.
- [ ] RED/GREEN pure projector; CI real Marten event identity/replay, advanced checkpoint payment/confirmation suffix, equal-version redelivery, reset, corrupted count detection and restoration. Compare deserialized JSON envelopes/ciphertext, not jsonb whitespace; timestamps use DB-representable precision.
- [ ] Keep normal Get/List SELECT free of PassengerInfoJson and keys. Count is explicit metadata, never ticket-array length or itinerary JSON. Foreign/ownerless access still fails closed; old rows expose truthful1 from source/schema, not a guessed unknown response.

## Task 5: group UI and immutable attempt

**Files:** Angular search form/page/results/quote comparator/panel, passenger form/panel, A operation service, order/detail/feed view models/templates; TS fixtures/decoders/specs.

- [ ] RED count controls 1/2/9 and invalid values; immutable submitted count travels with search result/quote intent, whole-group price labels and skipped-provider explanation.
- [ ] Quote exposes exact number of slots and requires latest review. Compare prior route/money/binding even after login/refresh; `beginBooking` and hold dispatch enforce acceptance and expiry, including clock change between render and click.
- [ ] FormArray keyed by BookingPassengerId; stable labels/DOM IDs, independent title/contact fields and per-slot error focus. Search is the only place to change count; never silently remove an entered person.
- [ ] Integrate with A's single active hold attempt: freeze serialized ordered body/key once, owner/session guards before/after awaits, preserve unknown on navigation/new criteria; owner GET for viewing only after uncertainty. Later rejection/expiry never frees an old unknown attempt.
- [ ] RED/GREEN logout/owner switch/same-owner new epoch, late quote/hold receipt, changed form vs frozen bytes, 16 KiB cap, no PII/token/key/body in storage/URL/history/logs. Existing confirmed/cancelled B5 paths remain functional.
- [ ] Show count and observed booking status in GET/list; no names in order metadata and no invented passenger↔ticket association. Update baggage copy to maximum per passenger/segment, not group allowance.

## Task 6: full fictional vertical and delivery

**Files:** `tools/demo/flights-search-api.mjs` and tests; shared search/booking fixtures; `tests/travel-e2e/demo/*`; docs/current-state/ADR/result report and required instruction adapters only if facts change.

- [ ] Demo refs include count; quote returns deterministic party slots/revision; fixture group totals are explicit; hold validates exact IDs, preserves raw key/body, delayed/lost responses remain unknown without a second write. No real auth/payment/provider.
- [ ] Full fictional journeys: 2 adults one-way and9 return; mismatch/underage/unsupported capability; changed binding/route; unknown hold status lookup; A current-tab unknown confirm; owner changes and successful B3–B5 regressions. Network guard rejects non-demo traffic.
- [ ] Run meaningful safe local unit/HTTP/serialization/client/web/architecture/build/format/harness checks, web/api-client/travel-e2e lint and demo Playwright. Inspect keyboard/desktop/360px. Real DB/Host/Aspire acceptance stays on existing mandatory CI; no local schema side effects.
- [ ] Self-review P1–P7 and five Review Focus cases; independent whole-change review, important fixes with regressions, commit all docs/tests/source, push and attach PR/dev, green mandatory exact-head CI, normal merge, dev synchronization and only owned cleanup.
- [ ] Record M2.3 completion and continue the chat objective: design M2.4 and M2.5 with stable party interfaces, then their approved delivery cycles. Do not report all M2 complete here.

## Subsequent increments and safe parallelism

M2.4 belongs in Flights EF (the original concept already says so): owner-filtered encrypted profiles with concurrency version and no plaintext label/search index. Separate purpose bound to profile+owner; listing decrypts only bounded owned data; explicit opt-in save and copy-to-draft; profile edits/deletion never mutate booked snapshots. Its table/migration is a separately reviewed source change.

M2.5 uses explicit journey kind and ordered 1–4 legs, legacy one-way/return adapter, new cache compatibility version and provider skip reasons. Ranking keeps group totals and sums only flight-slice duration/transfers; ground gaps are visible. NL v1 remains unchanged. Neither capability requires implementing the other. Parallel research/backend work is possible; shared HTTP/TS/UI/composition/schema files have one integration owner and each PR receives all mandatory checks.
