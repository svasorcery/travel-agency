# Flights M2.3a local verification

Date: 2026-10-03. Base: fresh fetched `origin/dev` at `02356dfc5010172548bb45938bc1bf29deae6dcd`, descendant of the supplied M1 checkpoint `d75186054a33de0e1cb688da7e3e7cc42927dadf`. Branch: `codex/flights-m23a-booking-correctness`. Scope: [approved reduced specification](../specs/2026-10-03-flights-m23-design.md) and [A plan](../plans/2026-10-03-flights-m23a-booking-safety.md).

## Result and boundaries

This slice corrects the existing single-passenger flow before M2.3b. Booking input is capped at 16KiB without disk spooling. Confirmation checks accepted money before the fake wallet and supplier payment; only a matching succeeded receipt establishes success. Duffel mutation requests never automatically retry. Possible hold dispatch failure or post-capture uncertainty cannot fabricate cancellation/refund/confirmation.

Current-session memory owns frozen hold attempts and blocks another hold/confirm or quote allocation after an unknown outcome, including normal SPA navigation and transient auth loss. Identity epoch reset erases old PII/attempts and invalidates old completions. Owner GET is observational and does not prove the original write failed. Quote acceptance and expiry are checked in methods. B5 cancellation retries retain their existing behavior.

No new events, schema changes, recovery endpoint or durable confirmation workflow were introduced. External effects remain outside the Marten commit. Memory does not coordinate clients/tabs or survive identity reset/reload/server restart. The test wallet is an in-memory fake, not real financial acceptance. M2.3b multi-passenger, M2.4 saved travelers and M2.5 multi-leg remain open.

## Executed local checks

| Check | Evidence |
|---|---|
| Flights unit, Release | 560/560; new receipt/price/unknown/cancellation tests observed RED before implementation |
| Selected noDB HTTP/direct middleware | 72/72; known/chunked exact limit and +1, route aliases, auth/key precedence, zero store/bus/PII-protector side effects on413, raw hash/lifetime |
| Fake-HTTP production client composition | 12/12; service graph only, no Host/database start |
| Architecture, Debug | 167/167; prepares the existing pre-push no-build hook |
| Production Angular | Build succeeded with the ordinary auth configuration; no fake-auth production change |
| Full solution, Release | Build succeeded, zero errors; applications were not started |
| Web | 273 tests across 21 files after review fixes; five new auth/navigation regressions observed RED then GREEN |
| API-client | 63 tests across 5 files and library build |
| Fictional Node demo API | 25/25; no Authorization accepted or real suppliers called |
| Chromium demo | 16/16; happy path, B5 cancellation replay, ranking/feed, no PII storage/logs, unknown hold/confirm and no second write after navigation |
| CSharpier | 518 files checked |
| Biome and Nx web/API-client/E2E lint | Pass, zero errors; existing warnings/informational findings retained |
| AI harness | 90/90 and source validation |
| README examples | 11/11 and catalog match |

Representative commands: `dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit/Travel.Modules.Flights.Tests.Unit.csproj --configuration Release --maxcpucount:1`; selected HTTP filter covers `BookingRequestBodyLimit`, `FlightsEndpointsHttpTests`, `FlightsPiiHttpTests`, `ReadmeRequestExamplesTests`; `dotnet test tests/Travel.Tests.Architecture/Travel.Tests.Architecture.csproj --configuration Debug --maxcpucount:1`; `dotnet build Travel.slnx --configuration Release --maxcpucount:1`; `npx nx test web --watch=false`; `npx nx test api-client --watch=false`; `npm run test:flights-demo`; `dotnet csharpier check .`; `npx biome ci .`; `npm run check:ai-harness`; `npm run check:readme-examples`.

Fixtures were inspected before execution. Local tests used fake HTTP, lean TestServer and disposable test crypto material. Actual Host/AppHost, database initialization, migrations, key provisioning, real providers/payments and paid AI APIs/evals were not run. The database/Marten/outbox/concurrency and Aspire suites remain mandatory CI gates; local compilation does not establish their execution.

Initial browser execution could not find the Playwright-pinned Chromium; that exact free headless browser was downloaded into task-owned temporary storage. The first executed browser run passed 15/16; a new test matched two notices before navigation completed. URL and exact order-page text assertions corrected that test, and the full rerun passed 16/16. No application behavior was weakened to make it pass.

Existing ASP.NET RouteHandlerAnalyzer AD0001 and Verify solution-discovery warnings appeared in .NET logs; explicit SolutionDir removed the latter in later builds. Biome reports8 existing warnings and152 informational diagnostics, scoped Nx lint7 warnings. These are disclosed, not treated as failed checks or suppressed by configuration changes.

## Review

Independent task review approved backend correctness. Middleware review requested actual noDB HTTP pipeline coverage plus exact+1/auth cases; these were added and scoped re-review approved. Frontend review found missed epoch-reset and transient-auth quote guards; fixes have RED/GREEN coverage. Root also corrected an ordinary order-page link that reloaded the document and lost memory state.

Final whole-change review found two further UI classification defects: typed413 rejection was treated as unknown, and transient auth loss overwrote established hold/confirm outcomes. Six failures reproduced those defects; the narrow fixes passed focused 56 tests and full web 273, followed by another successful Node 25/Chromium 16 run and production build. A malformed test XML paragraph was also corrected. Final scoped re-review approved all three fixes with no new issues. Mandatory exact-head PR CI remains the delivery gate. This report establishes local evidence and does not itself declare merge or completion of M2.


## First PR CI follow-up

PR #30 run `37105025567` on `dc4f7e489ef6147b453158bb60a230e756580e5f` failed one Host integration assertion while the other completed test jobs passed; the dependent E2E job was skipped because its prerequisite failed. The unchanged composition test still expected four Duffel payment POST attempts when an arbitrary key was present. The approved invariant requires one attempt; the observed result was one. The test now uses `/air/payments` and its nested payment payload, asserts one attempt even with a key, and retains the status assertion. GET and unrelated retry tests remain unchanged.

The complete fake-HTTP composition class passed 12/12 locally after fixture inspection. It constructs a service provider with fake primary handlers and does not start Host or schema initialization. This correction changes no production or CI/CD code. A fresh exact-head CI run is required before merge; the earlier green jobs are not a substitute.


## Post-merge concurrency test correction

PR #30 merged as `8ae2d5847d9c69d22f3c3f1b835c66da3066fdc3` after exact-head CI `37105545044` passed all thirteen normal checks, including the dependent Host/Angular E2E; paid AI-evals were skipped. Post-merge run `37106209834` then failed one of 305 Flights integration tests: `Concurrent_ticket_callbacks_append_once_and_retry_the_loser_as_noop` observed zero classified write conflicts instead of one, after 31 seconds.

Source inspection found an unsafe test synchronization mechanism. `FirstEventBarrierTimeProvider` blocked the first two global clock calls with 15-second waits and ignored their timeout results. A single handler calls the clock for event creation and later inbox acknowledgement, so it can consume both waits before the second writer reaches the contested read. The duration supports this explanation, but the original log does not prove the exact scheduling sequence or expose all captured exception types. Production webhook/save code and dependency versions were unchanged by M2.3a; only an unused provider fake signature changed in that test file.

The test-only correction synchronizes both contender sessions asynchronously in the existing `BeforeWebhookCommit` listener. Both source reads/decisions must finish before either save proceeds. The 15-second deadline remains bounded and a missing participant fails explicitly. Strict success/conflict/event/inbox/retry-noop assertions remain; unexpected errors are reported by type without raw exception payloads. No production code, workflow, retries or schema behavior is changed. Local verification is limited to compilation and pure gate tests; full database proof remains a fresh CI gate.

Four pure gate tests passed locally (4/4), covering asynchronous rendezvous, a missing participant and cancellation; the affected integration project compiled in Release with zero warnings/errors. Container-backed race tests were not executed locally. Their strict assertions must pass in CI before the follow-up can merge.

Independent review approved the test-only correction without findings; database runtime acceptance remains the exact-head CI gate.
