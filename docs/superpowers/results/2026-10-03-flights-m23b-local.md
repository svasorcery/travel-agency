# Flights M2.3b — adult-party verification

## Scope and base

The approved slice adds 1–9 adult passengers to the existing one-way/return search and booking flow: exact supplier/local bindings, fresh quote revision, bounded per-person fields, one encrypted party before bus dispatch, `OfferHeldV3`, explicit order count, repeated passenger forms and a fictional browser demonstration. M2.1 ranking and M2.2 protection remain; M2.3a uncertainty guards and B5 cancellation behavior are preserved. Only cancellation count metadata changes.

Fresh fetched `origin/dev`: `f2162b323f33b7ed9c9fe86d7c95a5a1f324e596`, verified descendant of M1 checkpoint `d75186054a33de0e1cb688da7e3e7cc42927dadf`. New managed worktree/branch `flights-m23b-multi-passenger` / `codex/flights-m23b-multi-passenger` starts at that exact SHA. A was closed through PR30 and test-only PR31; PR31 post-merge run37108452996 succeeded. Pre-publication fetch still returned the same base. No archived checkout was restored.

## Local evidence

| Check | Observed result | Boundary |
|---|---|---|
| Complete Flights unit suite, Release | 668 passed | Pure/fake HTTP/Redis/NL/crypto; no supplier or database |
| Eight exact noDB Host HTTP/middleware/catalog classes | 122 passed | Real endpoint/auth policy pipeline with controlled identity and downstream fakes; no actual Host startup |
| Serialization-only event compatibility | 5 passed | Old payloads and protected snapshot serialization without keys |
| Pure migration operation tests | 3 passed | EF model/Up/Down inspection; no connection |
| Architecture, Debug | 167 passed | Dependency/type/project checks; no services |
| Full solution Release build | 0 warnings, 0 errors | Compilation only, including CI-only test source |
| Angular web / typed API client | 308 / 77 passed | Forms, contract decoders, quote/owner/unknown guards |
| Production Angular build and web/API lint | Passed | Existing seven non-null assertion warnings; no new style-budget warning |
| Fictional Node API | 32 passed | Only in-memory fake state and local HTTP |
| Fictional Playwright | 23 passed, no retry | Two adults one-way, nine adults return, existing B3–B5 regressions |
| E2E lint / repository Biome / CSharpier | Passed | Informational existing style suggestions remain |
| README catalog and smoke helper unit tests | 12 passed | No real smoke against a running Host |
| AI-harness validation | 90 tests passed; inventory valid | Dependency-free check only; no authenticated model verifier or paid eval |

Principal commands:

```text
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit --configuration Release --no-restore --filter "Category!=AiEval&Category!=AiEvals" --maxcpucount:1
dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration --configuration Release --no-restore --filter FullyQualifiedName~OrderReadModelMigrationTests --maxcpucount:1
dotnet test tests/Travel.Tests.Architecture --configuration Debug --filter Category=Architecture --maxcpucount:1 --no-restore
dotnet build Travel.slnx --configuration Release --maxcpucount:1
node --test tools/demo/flights-search-api.test.mjs
node node_modules/@playwright/test/cli.js test --config tests/travel-e2e/playwright.flights-demo.config.ts
npm run check:readme-examples
npm run check:ai-harness
npx biome ci .
dotnet csharpier check .
```

The noDB HTTP filter selected only `FlightsEndpointsHttpTests`, `FlightsSearchContractHttpTests`, `FlightsPiiHttpTests`, `FlightsPassengerPartyHttpTests`, `FlightsOrderPassengerCountHttpTests`, `BookingRequestBodyLimitHttpTests`, `BookingRequestBodyLimitTests`, and `ReadmeRequestExamplesTests`. Actual Host, PostgreSQL, Marten/outbox, migrations and Aspire suites were never run locally.

Browser assertions include keyboard navigation, distinct local-ID labels, desktop and 360px no overflow, same-ID re-quote draft retention with new acceptance, changed membership/route clearing, safe per-person errors, zero second write after lost hold/confirm response even across GET/SPA navigation, and PII sentinels absent from storage/URL/history/console. Loss tests first observe a real fictional API200, then abort delivery. Different real owners are covered at HTTP/unit boundaries; the demo exposes only one fictional identity.

One-way group search totals20750/92900 and quote totals21420/95580 are explicit nonlinear fixtures. Return demo totals are independently declared (count1:20000/20500; count9:172900/178480); the canonical ranked return-search contract example has20500. They are separate fictional scenarios, not real fare evidence. Ranking source amounts always match each actual response.

## Review and repairs

Domain/privacy and backend/provider/projection/source-migration independent reviews found no blocking defects. Frontend review found empty phone accepted by an optional pattern validator, negative decoder tests failing on absent surrounding metadata, and real page refresh destroying entered passengers. Fixes add required phone, intended-field assertions and a mounted same-page quote snapshot; PII remains only in the child form. Same owner/IDs preserve drafts during quote loading; new acceptance is mandatory, and changed IDs/owner/navigation clear them. Scoped independent re-review approved these fixes.

Integrated tests caught a stale expected event inventory (667 passed/1 failed before adding V3; final668 passed). Browser runs exposed ranking evidence left at singleton price, ambiguous old selectors and shared fictional orders across test cases; regressions and distinct per-case dates fixed them. A raw changed-body replay test failed400 before parsing order was corrected to return409 conflict. All final checks above passed after these corrections. No assertion, required check or CI configuration was weakened.

## Schema and compatibility

Source migration `20261003094726_AddOrderPassengerCount` adds only integer NOT NULL `passenger_count` DEFAULT1 and check1–9 on `flights.order_read_model`. Generated offline Up/Down SQL was inspected; no SQL was executed. Old rows stay count1. V1/V2 event identities and ciphertext purpose stay intact; V3 protects the party with booking/owner/revision/count context. Projection and ordinary metadata reads need no decrypt and never infer passengers from tickets. Historical plaintext is not rewritten.

V3 writers require the compatible schema and readers. Old binaries are not a safe rollback once V3 exists; drain incompatible writers/messages and use a compatible forward repair. Old Held orders retain confirm/cancel. Old quoted streams need a fresh binding before new hold. No local key provisioning, database apply, Host/AppHost, deployment, real supplier, payment, Anthropic or paid API/eval was executed.

## Delivery gate and remaining scope

Final independent whole-branch review approved publication with no important findings, including the stable demo. Mandatory exact-head CI still precedes merge. CI must verify real PostgreSQL upgrade/default/check behavior, V3 Marten identity/replay, projection repair and owner prefixes, transactional outbox/concurrency, actual Host OpenAPI snapshot and dependent E2E. The OpenAPI expected snapshot was updated from changed request records; only actual Host CI establishes its runtime match. Local evidence is not deployed acceptance. Publication/merge evidence belongs on the PR and subsequent milestone checkpoint.

M2.3b is not all M2. Saved travelers (M2.4) and ordered multi-leg/open-jaw search (M2.5) need their concrete reviewed designs and delivery cycles. Children/infants, passports, partial-party booking, ancillaries, refunds, new SSE/Support, M3 cancellation/recovery and OpenSpec remain outside this slice. Existing current-session uncertainty protection still cannot coordinate other tabs or survive reload/restart.
