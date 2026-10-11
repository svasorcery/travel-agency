# Verification and acceptance matrix

## Final implementation delivery and authorized sync/archive — 2026-10-11

[PR42](https://github.com/svasorcery/travel-agency/pull/42) merged normally into dev at **135fc134cfb386870851ef3dfec3c2fa0a78eae4** on2026-10-11T19:32:36Z. Final publication head938896dde25318199fa35acb8e4b4a6ecc274f81 passed [CI38167545464](https://github.com/svasorcery/travel-agency/actions/runs/38167545464): all13normal jobs including mandatory PR E2E. [Postmerge CI38168466905](https://github.com/svasorcery/travel-agency/actions/runs/38168466905) passed all12normal jobs on the exact merge SHA; paid evals and PR-only E2E skipped under unchanged dev-push policy. Main checkout fast-forwarded safely and strict OpenSpec validation passed. Merge and accepted publication trees are identical:234d1d8b450b8d0ec8874a4bb0711c7c492b9729.

All22source/local/CI tasks are complete; independent source/repair review has no open P1/P2. The user subsequently authorized the proposed sync/archive step with «Го». A fresh fetch still resolves dev to135fc134cfb386870851ef3dfec3c2fa0a78eae4; clean codex/flights-m3-order-sse-archive was created explicitly from that SHA in the existing managed worktree, with ancestry checked. The delta introduces one new [main capability contract](../../../specs/flights-order-live-updates/spec.md): Purpose and all12requirements/35scenarios are preserved verbatim beneath the main-spec headers. No requirement, financial rule or product code changes during closure.

Earlier source/planning/publication checkpoints below retain their dated authority, pending gates and base-bound observations as historical evidence. This final receipt governs implementation delivery. Raw failed/passing CI logs and the local delivery receipt remain separately retained under artifacts/local-evidence/flights-m3-order-sse-2026-10-11 in the main checkout. Closure publication is gated by its own normal PR/CI receipt; live deployment-proxy streaming, real supplier/payment acceptance, bank credit and rollout remain unverified and separately authorized.

## Historical source/local and implementation acceptance receipts — 2026-10-11

Source implementation was authorized by the user's «го» after the canonical plan, independent plan review and requested additional self-review. The managed worktree was created from freshly fetched `7b7fb0f2831cb5067d4dea025e9e7d9d3f16e347` with ancestry checked. After «чё дальше? мерж? го», branch/commit/push/PR and normal merge into dev are authorized. A fresh publication fetch still resolves origin/dev to that same SHA; new `codex/flights-m3-order-sse` was explicitly created from it and ancestry checked. Mandatory CI, PR E2E, latest source review and postmerge verification govern delivery. No policy bypass, sync/archive, supplier/payment/live or deployment action is authorized by that merge step.

The page subscribes with header-authenticated fetch to the existing owner SSE endpoint and reads order/creation/cancellation/refund facts independently. Generic committed invalidations reuse the existing outbox/readiness/registry. Additive checkpoints and owner/epoch/read guards prevent stale replacement; terminal pages retain fallback observation. Supplier entitlement, supplier settlement, fictional customer receipt and bank credit remain separate.

Independent source review `/root/sse_source_review` accepted published18c7a3, subsequent retained-evidence/CI corrections in cd392d3 and the final test-only projection/retry correction in c2fadc7; no open P1/P2. The reviewer inspected source and did not execute tests. Requested self-review findings and exact independent review scopes are retained in [review.md](review.md).

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
| `node tools/openspec/run.mjs validate flights-m3-order-sse --strict` | PASS on acceptance corpus;22/22tasks closed after actual full CI/PR E2E pass. |
| Static boundary/whitespace/link checks | PASS: `git diff --check`, canonical local links and whitespace; no staged changes; no Core/migrations/auth/CI/tooling/package-manifest changes; original dev clean. |

Initial publication CI receipt: [PR42](https://github.com/svasorcery/travel-agency/pull/42), [CI38162914202](https://github.com/svasorcery/travel-agency/actions/runs/38162914202), source head18c7a384e45930214deef2c45e6701c96b3a42c5 (synthetic PR merge6e70de229623ea02f39102d397ab00ca9031d6bd).8normal jobs passed,4failed, mandatory E2E was skipped due those failures; paid evals skipped normally. Source repairs and exact failed-job diagnostics are described in process-log.md; the raw logs are retained in ignored main-checkout local evidence. No merge or CI acceptance is claimed from this first run.

Second CI receipt: [CI38165251104](https://github.com/svasorcery/travel-agency/actions/runs/38165251104), source head cd392d3da9b887fa18c6b1faecf9abc4300d83b1. All11 normal backend/build/lint jobs passed, including Flights Unit, Flights Integration, full Host HTTP397/397 with the generated OpenAPI comparison, Host Integration, architecture, contract and Aspire. The whole normalized Host OpenAPI snapshot passed unchanged; task1.4 has actual CI evidence. Frontend ESLint/unit/build passed, but fictional browser55/56 failed; mandatory PR E2E was consequently skipped. Paid evals skipped normally. This run is not whole-PR acceptance.

The failing search scenario assumed one404 would keep projection lag visible; the new post-connect GET could converge before its assertion. Its retry then reused the same deterministic order already Held in the shared demo server. The fixture now holds actual upstream order reads behind404 until the lag assertion completes, then releases the real200 response; a fresh legitimate form date per repeat/retry creates a fresh offer/order identity. No runtime reset API or product behavior change. The corrected scenario passed2/2 consecutive repeats in43.7s; unchanged travel-e2e ESLint and Biome passed. Full normal CI and mandatory PR E2E remain pending on the corrected head.

Additional publication-local checks: unchanged pre-push Architecture170/170 passed after startup audit and build; web/api-client/travel-e2e ESLint passed after fixing actual no-empty-function diagnostics. No generated snapshot is fabricated.

Corrected implementation acceptance: [CI38166506507](https://github.com/svasorcery/travel-agency/actions/runs/38166506507), exact source c2fadc7e1e4666d61f5286c3c42d57af8878efa7, completed **success**. All13 normal jobs passed: build-dotnet, lint, frontend-affected, test-flights-unit, test-identity-unit, test-ai, test-host-http, test-architecture, test-contract, test-flights-integration, test-host-integration, test-aspire-smoke and mandatory test-e2e. Paid test-ai-evals skipped under unchanged policy. Results include uncached Angular438/438, client170/170, clean fictional browser56/56 in2.8min, Flights Unit1389/1389, Flights Integration400/400 and Host HTTP397/397. Actual Host startup, README smoke(status/openapi/validation), Angular startup and real Host browser2/2 passed. Raw frontend/backend/E2E logs are retained in ignored local evidence. Tasks1.4/6.3 are closed from actual execution;22/22source/local/CI tasks complete.

This closing receipt changes only canonical documentation. Its publication head must pass the same normal PR CI before the authorized merge; exact final-head and postmerge receipts will be retained in PR42 and local delivery evidence. CI-ready/source-accepted is the completed boundary here. OpenSpec sync/archive, deployed streaming, provider/payment acceptance and deployment remain separate, unperformed gates.

Backend **local compile-only**: sequential `dotnet build <project> --no-restore --nologo -v:q` passed for Flights Unit, Flights Integration and Travel.Host Integration projects. Unit/Integration emit the existing Verify solution-discovery warning. Host build passed; a prior cold compilation emitted existing Wolverine analyzer AD0001 warnings. Those backend test runners and application runtime were not invoked locally; existing CI subsequently executed and passed them on cd392.

OpenAPI HTTP tests cover additive int64 fields, owner concealment and no-store; the actual full Host generated snapshot passed in the second CI run. `HostWebContractTests.OpenApi.verified.txt` was not invented or edited. Backend runtime has CI evidence on cd392; full PR acceptance and live delivery are separate pending gates.

## Requirement → concrete source proof → execution lane

| Requirement | Relevant test sources | Evidence lane |
|---|---|---|
| Read after committed change | `OrderLiveUpdatePipelineTests.cs`; `flights-order-live-updates.spec.ts` | Persisted/signed/owner chain passed existing CI; fictional browser executed |
| Notification follows committed evidence | `OrderChangedCommitTests.cs`, `CustomerRefundOrchestrationTests.cs`, `BookingCommitOutboxTests.cs`, `CustomerRefundPersistenceTests.cs`, `CustomerRefundRestartTests.cs`, `NotificationVersionGateTests.cs` | Unit and persisted commit/rollback/loser delivery/post-replacement consumer passed existing CI |
| Report and money truth | Pipeline report asserts no amount/credit; Node live-report/live-refund; browser retained Sent on503 and unproven bank credit | Backend passed existing CI; Node/browser executed |
| Private subscription | `FlightsOrderReadVersionHttpTests.cs`, existing SSE ownership/lifecycle tests; `flights-order-events.spec.ts`; fictional route guard | Owner/auth HTTP passed existing CI; header/redirect/parser/guard local tests passed |
| Identity and page lifetime | Existing page logout/route/epoch tests and coordinator token-stop/15s timeout; leaf epoch tests | Angular executed |
| Lost signals and terminal lifetime | Coordinator healthy fallback/stop; browser offline reconnect, report after195s and reload; hidden200s regression | Angular/browser executed |
| Bounded coalescing |100hints during slow refund GET; satisfied/exhausted duplicates; stable/backoff/Retry-After0; parser64KiB | Angular/client executed |
| Monotonic snapshots | Order mapper sentinel tests; versioned decoder cases; creation same-version180s diagnosis; persisted snapshot/fresh-read test; pageV12→V11/503 | Backend passed existing CI, TS/Angular executed |
| Read failure versus business | Page/leaf retained ticket/Sent, independent refund despite failed creation/order; equal-version acknowledgment and auth revocation | Angular/browser executed |
| Observation grants no mutation | Actual leaf freshness admission, admitted consent through pause, deadline regression, Node/browser GET/POST counters | Angular/Node/browser executed; gateway/commit guards passed existing CI |
| Additive compatibility | Historical unknown checkpoint decoder; get/list/cancel mappings; existing9-adult/4-leg and historical-refund suites; OpenAPI source tests | Local fictional/client baseline passed; actual Host snapshot and contracts passed existing CI |
| Separate evidence lanes | This receipt, canonical review and process ledger | Source/local/full implementation CI done; final publication-head CI/merge and live separate |

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

Publication authorization is recorded above. Full Host HTTP/OpenAPI and contract acceptance closed task1.4 in the second CI run; full implementation CI38166506507 and mandatory PR E2E closed task6.3. `.github/workflows/ci.yml`, auth policies and normal review gates remain unchanged. Aspire passed on this slice; no historical exit134 is attributed to it. Normal final-head CI, merge and postmerge checks still govern delivery. A prior refund CI run covers its own SHA, not this slice.

Live acceptance separately requires deployed artifact identity and actual deployment proxy flush/buffering/heartbeat/timeouts, auth ownership and reconnect proof. It has not been run. Local Node/browser proof does not establish JWT/HMAC ingress integration, Marten/EF runtime, bank credit, real supplier/PSP acceptance or rollout.

## Historical planning receipt

Before «го», all4 planning artifacts,12requirements/35scenarios and22unchecked tasks existed. Pinned OpenSpec1.14.0 was restored from its unchanged lockfile with explicit user permission and strict validation passed. Initial independent plan review corrected EF sentinel-1 compatibility, source-only applicability and first-Hold notification eligibility. The additional user-requested self-review refined identity idempotence, read pause epochs, duplicate budgets and equal-version acknowledgment. Those receipts describe planning only; current source results are recorded above.
