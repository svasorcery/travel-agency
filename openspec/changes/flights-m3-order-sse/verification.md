# Verification and acceptance matrix

## Current source receipt — 2026-10-11

Source implementation was authorized by the user's «го» after the canonical plan, independent plan review and requested additional self-review. The managed worktree was created from freshly fetched `7b7fb0f2831cb5067d4dea025e9e7d9d3f16e347` with ancestry checked. After «чё дальше? мерж? го», branch/commit/push/PR and normal merge into dev are authorized. A fresh publication fetch still resolves origin/dev to that same SHA; new `codex/flights-m3-order-sse` was explicitly created from it and ancestry checked. Mandatory CI, PR E2E, latest source review and postmerge verification govern delivery. No policy bypass, sync/archive, supplier/payment/live or deployment action is authorized by that merge step.

The page subscribes with header-authenticated fetch to the existing owner SSE endpoint and reads order/creation/cancellation/refund facts independently. Generic committed invalidations reuse the existing outbox/readiness/registry. Additive checkpoints and owner/epoch/read guards prevent stale replacement; terminal pages retain fallback observation. Supplier entitlement, supplier settlement, fictional customer receipt and bank credit remain separate.

Prior independent source review `/root/sse_source_review`: **source-review-ready** after its corrections, including resume and backend test changes; the reviewer did not execute tests. The later requested solution self-review found and corrected two additional UI defects plus one stale fictional test guard. Those latest changes have self-review/regression evidence and are not described as freshly independently reviewed. Full findings and exact review scopes are in [review.md](review.md).

| Executed local check | Result and scope |
|---|---|
| `npx.cmd nx test api-client` | PASS:170/170; decoders plus19 transport/parser cases. Client source unchanged since that successful run. |
| `npx.cmd nx test web --skip-nx-cache` | PASS:438/438 in35files after first CI repairs; includes pending/unknown confirmation, first-Held applicability and positive/legacy retained evidence on all404, as well as original expiry, retained facts, identities, coalescing and pause recovery. |
| `node --test tools/demo/flights-search-api.test.mjs tools/demo/flights-customer-refunds.test.mjs` | PASS:69/69. The existing search test entry imports the new SSE tests, so unchanged CI command includes them; do not list that imported file twice. |
| Full fictional Playwright command below | PASS:56/56,2.6min after requested solution self-review and saved-profile guard correction; existing booking/ancillary/refund/party/journey/profile baseline plus4 SSE cases. An earlier self-review run had55/56 due the stale test guard; that failure was reproduced, corrected and rechecked. |
| Same Playwright config selecting `flights-saved-travelers.spec.ts` and `flights-order-live-updates.spec.ts` | PASS:17/17 after guard correction; profile privacy/identity baseline plus real loopback SSE→GET→ticket/report/refund, report beyond195s page time, reload and offline missed-signal recovery; zero observation-induced POSTs in the SSE cases. |
| `npx.cmd nx build web --configuration production` | PASS; existing Flights search SCSS warning:4.50KiB exceeds4.00KiB warning budget by502bytes. No budget change. |
| `npm.cmd run check:ai-harness` | PASS:133/133 and exact harness/pinned inventory validation. |
| `dotnet csharpier check .` | PASS:805files. |
| `npx.cmd biome ci . --diagnostic-level=error` | PASS:243files; warning-level style diagnostics are not claimed absent. No config/rule changes. |
| `node tools/openspec/run.mjs validate flights-m3-order-sse --strict` | PASS on final canonical artifacts;20checked/2pending tasks. |
| Static boundary/whitespace/link checks | PASS: `git diff --check`, canonical local links and whitespace; no staged changes; no Core/migrations/auth/CI/tooling/package-manifest changes; original dev clean. |

Initial publication CI receipt: [PR42](https://github.com/svasorcery/travel-agency/pull/42), [CI38162914202](https://github.com/svasorcery/travel-agency/actions/runs/38162914202), source head18c7a384e45930214deef2c45e6701c96b3a42c5 (synthetic PR merge6e70de229623ea02f39102d397ab00ca9031d6bd).8normal jobs passed,4failed, mandatory E2E was skipped due those failures; paid evals skipped normally. Source repairs and exact failed-job diagnostics are described in process-log.md; the raw logs are retained in ignored main-checkout local evidence. No merge or CI acceptance is claimed from this first run.

Additional publication-local checks: unchanged pre-push Architecture170/170 passed after startup audit and build; web/api-client/travel-e2e ESLint passed after fixing actual no-empty-function diagnostics. The whole Host normalized OpenAPI snapshot did not fail on the first run; its final acceptance still waits for a fully passing HTTP/CI run, and no snapshot is fabricated.

Backend **compile-only**: sequential `dotnet build <project> --no-restore --nologo -v:q` passed for Flights Unit, Flights Integration and Travel.Host Integration projects. Unit/Integration emit the existing Verify solution-discovery warning. Host build passed; a prior cold compilation emitted existing Wolverine analyzer AD0001 warnings. No backend test runner or application runtime was invoked.

OpenAPI HTTP/source tests cover additive int64 fields, owner concealment and no-store; actual full Host generated snapshot remains pending existing CI. `HostWebContractTests.OpenApi.verified.txt` was not invented or edited. This is not a CI-ready or runtime-accepted claim.

## Requirement → concrete source proof → execution lane

| Requirement | Relevant test sources | Evidence lane |
|---|---|---|
| Read after committed change | `OrderLiveUpdatePipelineTests.cs`; `flights-order-live-updates.spec.ts` | Persisted/signed/owner chain CI-only compiled; fictional browser executed |
| Notification follows committed evidence | `OrderChangedCommitTests.cs`, `CustomerRefundOrchestrationTests.cs`, `BookingCommitOutboxTests.cs`, `CustomerRefundPersistenceTests.cs`, `CustomerRefundRestartTests.cs`, `NotificationVersionGateTests.cs` | Backend source compiled; real commit/rollback/loser delivery and post-replacement consumer await CI |
| Report and money truth | Pipeline report asserts no amount/credit; Node live-report/live-refund; browser retained Sent on503 and unproven bank credit | Backend compiled; Node/browser executed |
| Private subscription | `FlightsOrderReadVersionHttpTests.cs`, existing SSE ownership/lifecycle tests; `flights-order-events.spec.ts`; fictional route guard | Real owner/auth HTTP runtime CI-only; header/redirect/parser/guard local tests passed |
| Identity and page lifetime | Existing page logout/route/epoch tests and coordinator token-stop/15s timeout; leaf epoch tests | Angular executed |
| Lost signals and terminal lifetime | Coordinator healthy fallback/stop; browser offline reconnect, report after195s and reload; hidden200s regression | Angular/browser executed |
| Bounded coalescing |100hints during slow refund GET; satisfied/exhausted duplicates; stable/backoff/Retry-After0; parser64KiB | Angular/client executed |
| Monotonic snapshots | Order mapper sentinel tests; versioned decoder cases; creation same-version180s diagnosis; persisted snapshot/fresh-read test; pageV12→V11/503 | Backend compiled, TS/Angular executed |
| Read failure versus business | Page/leaf retained ticket/Sent, independent refund despite failed creation/order; equal-version acknowledgment and auth revocation | Angular/browser executed |
| Observation grants no mutation | Actual leaf freshness admission, admitted consent through pause, deadline regression, Node/browser GET/POST counters | Angular/Node/browser executed; gateway/commit guards CI-only |
| Additive compatibility | Historical unknown checkpoint decoder; get/list/cancel mappings; existing9-adult/4-leg and historical-refund suites; OpenAPI source tests | Local fictional/client baseline passed; actual Host snapshot and contracts CI-only |
| Separate evidence lanes | This receipt, canonical review and process ledger | Source/local done; CI/live pending |

The new creation persistence test appends after the first handler returns, proving the returned snapshot remains bound to its version and a fresh read advances. It does not simulate an append inside the handler. The handler's single `FetchForWriting` aggregate/current-version snapshot is verified by source review. The signed pipeline uses a recording outbox to inspect enrollment; it does not replace the separate real durable outbox tests.

Existing registry same-version coalescing, slow-client cleanup, readiness, duplicate inbox and concurrency tests are retained rather than duplicated. New source tests reuse existing fixtures. Browser tests establish fictional observation; detailed malformed/EOF/auth/status races are verified at the transport/coordinator/page layers.

## Local reproducible fictional proof

From the managed worktree with existing pinned dependencies:

```powershell
node --test tools/demo/flights-search-api.test.mjs tools/demo/flights-customer-refunds.test.mjs
npx.cmd playwright test -c tests/travel-e2e/playwright.flights-demo.config.ts
```

The full config owns one loopback fictional API and the existing demo Angular build. The four new cases additionally create isolated in-memory servers with startup-only presets. No Host/AppHost, DB, keys, paid API or real supplier/payment credentials are used.

For manual fictional observation use two terminals from the same worktree:

```powershell
$env:TRAVEL_FLIGHTS_ORDER_EVENTS_PRESET = 'order-live-ticket'
node tools/demo/flights-search-api.mjs
```

```powershell
npx.cmd nx serve web --configuration=flights-demo
```

Open `http://127.0.0.1:4201/flights/orders/00000000-0000-4000-8000-000000001501` and click «Демо вход». After the first stream registration the retained ticket appears through GET. Alternative startup values are `order-live-report`/ID ending1502 and `order-live-refund`/ID ending1503. Restart the demo server when selecting another preset; no runtime control/reset API exists. The timer starts once at first subscription and never restarts on reconnect or GET.

The report preserves an unverified supplier fact with manual review and no customer amount/credit. The refund preset shows a fictional sent receipt while bank credit remains unestablished. Reload requires the existing demo login and reads retained state; closing the server cleans its stream/timers.

## Required CI and live gates

Startup audit: Flights Unit includes assembly-level `FlightPiiTestRing`, temporary certificate/directory creation and `Crypto.Initialize()`. A test filter does not isolate that initialization. Integration fixtures start PostgreSQL and prepare schema; Host fixtures bootstrap the application and its dependencies. Therefore those runners were not executed locally. Builds do not run their assemblies.

Publication authorization is now recorded above. Run all existing normal CI checks and mandatory PR E2E, record exact implementation SHA/run/jobs and obtain actual full Host OpenAPI output. Preserve `.github/workflows/ci.yml`, auth policies and normal review gates. Tasks1.4 (generated snapshot/actual contract acceptance) and6.3 remain pending until those runs actually pass. A prior refund CI run covers its own SHA, not this slice. If Aspire exit134 recurs, retain its new log without attributing it to a historical incident.

Live acceptance separately requires deployed artifact identity and actual deployment proxy flush/buffering/heartbeat/timeouts, auth ownership and reconnect proof. It has not been run. Local Node/browser proof does not establish JWT/HMAC ingress integration, Marten/EF runtime, bank credit, real supplier/PSP acceptance or rollout.

## Historical planning receipt

Before «го», all4 planning artifacts,12requirements/35scenarios and22unchecked tasks existed. Pinned OpenSpec1.14.0 was restored from its unchanged lockfile with explicit user permission and strict validation passed. Initial independent plan review corrected EF sentinel-1 compatibility, source-only applicability and first-Hold notification eligibility. The additional user-requested self-review refined identity idempotence, read pause epochs, duplicate budgets and equal-version acknowledgment. Those receipts describe planning only; current source results are recorded above.
