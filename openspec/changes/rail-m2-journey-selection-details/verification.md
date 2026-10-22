# Rail M2 implementation receipt

Implementation authorized 2026-10-21 after proposal review. Separately delegated gpt-6.1-sol implemented the source slice; parent personal review7.4 is complete. Current disposition: [personal review](personal-review.md); historical intermediate receipts below remain preserved.

- Root: C:\Users\Vladimir_sva\.codex\worktrees\rail-m2-implementation\travel-agency.
- Branch: codex/rail-m2-journey-selection-details.
- Fresh fetched origin/dev, initial HEAD and required base: a8721effd9056459d217966083816455fbea599d. Parent verified ancestry before/after switch; no replacement by another HEAD.
- Rail ownership only; preserve others' edits, Trips untouched.
- Allowed: offline unit/terminal adapter/lean TestServer, TS/Angular, architecture, formatting/harness/OpenSpec. Synthetic local browser only with owned ports/outbound denied. NuGet sources cleared, audit disabled; local caches only.
- At source acceptance on 2026-10-21, Host/DB/Docker/load were not authorized or executed. Separate authorization on 2026-10-22 completed actual Host 3/3 and fictional mixed load/memory 6/6; see [runtime verification](runtime-verification.md). Provider calls (including sandbox/unauthenticated), credentials, paid/model calls, AppHost, stage/commit/push/PR/deploy/sync/archive remain unexecuted.
- Historical planning-only text is superseded by current explicit implementation approval; historical audit preserved.

## Checks

Offline restore Rail unit/integration passed (TestResults/rail-m2/NuGet.offline.config with clear packageSources; NuGetAudit=false).
Baseline unit: 35 passed, 0 failed/skipped (baseline-unit.trx). New Core capability initially absent: red-core.log compilation failure, not claimed as behavioral RED. First Core green: 11 passed, 0 failed/skipped (core.trx). Unchanged Verify solution autodiscovery warning emitted; subsequent runs supply SolutionDir/Name.

## Initial pending state (historical)

At work start: remaining implementation/traceability, parent review, actual Host/OpenAPI runtime, ordinary CI, measured load/memory, real source/target. Host test/snapshot source edits do not prove actual Host acceptance. M1 receipts remain historical. The source review and separately authorized runtime receipt below record the later dispositions.

Settled groups: baseline adapter/lean HTTP75/75. Core platform/source behavioral RED1/1 then full unit55/55green (unit-m2.trx). Route parser initial behavioral RED11failed/5passed, green16/16; thread allowlist RED4failed then green4/4; locator14/14green. Coordinator explicit stub RED8/8, green8/8 included in55unit. Reflection bindings in old deadline/profile source now inspect RailReadAdmission.permits, assertions unchanged. Route string from/to echo regression RED1failed/3passed before correction (red-thread-echo.log); no domain constructors opened for JSON fixture. Node local baseline12/12; view8RED then8GREEN; detail decoder20vectors RED3failed/17passed then20GREEN. All commands use --no-restore or local node_modules and physical worktree. No Host/DB/load/provider activity.

## Final offline checks and limits

All .NET execution used --no-restore and -p:SolutionDir=<assigned-root>/ -p:SolutionName=Travel; restores used only cleared packageSources with NuGetAudit=false. Node used copied local node_modules, no package/browser downloads. Logs/TRX are retained in assigned-root TestResults/rail-m2/.

| Command / selected scope | Result | Evidence |
|---|---|---|
|dotnet test tests/rail/Travel.Modules.Rail.Tests.Unit/...csproj|72 passed;0 failed/skipped|unit-final.log / unit-final.trx|
|dotnet test tests/rail/Travel.Modules.Rail.Tests.Integration/...csproj (terminal/TestServer/Wolverine/loopback telemetry only)|134 passed;0 failed/skipped|integration-final.log / integration-final.trx|
|dotnet test tests/Travel.Host.Tests.Integration/...csproj --filter FullyQualifiedName~RailMeasuredProfileSourceTests / FullyQualifiedName~RailM2MeasuredSourceTests|5 pure source tests passed; no Host fixture/DB/load|measured-source-final.log / measured-source-final.trx|
|dotnet test tests/Travel.Tests.Architecture/...csproj|183 passed;0 failed/skipped|architecture.log / architecture.trx|
|node node_modules/vitest/vitest.mjs run --config shared/ts/api-client/vite.config.mts <rail.decoder.spec.ts> <rail-details.decoder.spec.ts>|39 passed|decoder-final.log|
|node node_modules/vitest/vitest.mjs run --config tools/rail/vitest.config.mts|35 passed (state/view/fictional/auth/provenance)|rail-ts-final.log|
|node node_modules/nx/dist/bin/nx.js test web --watch=false --include=apps/web/src/app/rail/**/*.spec.ts (NX_DAEMON=false;NX_NO_CLOUD=true)|22 passed|angular-final.log|
|node node_modules/@angular/compiler-cli/bundles/src/bin/ngc.js -p apps/web/tsconfig.app.json --noEmit|passed|angular-compile-final.log|
|node node_modules/@playwright/test/cli.js test --config tests/travel-e2e/playwright.flights-demo.config.ts rail-search.spec.ts --retries=0|15 passed; outbound denied; loopback ports owned|browser.log|
|same Playwright command --grep local view controls (after focus/viewport change)|1 passed; route heading focused/in viewport; mobile visual inspected|browser-focus.log; test-results/rail-search-local-view-con-38b3e-erves-exact-fares-on-mobile-chromium/rail-route-mobile.png|
|dotnet csharpier check <changed Rail .cs files>|37 checked; passed|csharpier-check.log|
|node node_modules/@biomejs/biome/bin/biome ci <changed Rail TS/MJS/JSON>|passed with20warnings/154infos; no suppressions|biome-ci-final.log|
|npm.cmd run check:ai-harness|134 passed;0 failed/skipped|harness.log|
|node tools/openspec/run.mjs validate rail-m2-journey-selection-details --strict --json|valid;issues=[]|openspec.json|
|node tools/rail/run-measured.mjs --m2 --describe|executed=false; profile SHA256 f7f81e4c8d9f9c63b241b6f0e6ae98da2d5fcbdb5fbf3f7d853202c5d0159206|measured-m2-description.json|
|lean Rail-only Wolverine OpenAPI + structural snapshot preservation|new Rail path +9schemas; changedExisting=[]|lean-openapi.json; snapshot-preservation.mjs|

The Host-test SOURCE build emitted29 AD0001 warnings: Microsoft.AspNetCore.Analyzers.RouteHandlers.RouteHandlerAnalyzer threw System.IndexOutOfRangeException ("Index was outside the bounds of the array."). They remain recorded, not suppressed; the build is not claimed warning-free. Biome advisory diagnostics remain in its log. Initial Verify solution-discovery warning was resolved for later commands with explicit SolutionDir/SolutionName, without changing source tooling.

## Review regressions

Parent/reviewer findings were fixed within scope. Behavioral RED retained: core platform/source1; thread string echo1failed/3passed; requested epoch decoder1; expired-reference exception2; caller-cancel/deadline exception3; token/depth/header3; production HttpRequestError.ConfigurationLimitExceeded1; expired live-region component1failed/3passed. All have targeted GREEN and relevant final suites. Thread-only ConfigurationLimitExceeded normalizes output-limit; M1 search retains transport. No source deadlines/thresholds/assertions were weakened.

A final full adapter run exposed a test-only process-global OTel race (133passed/1failed, empty captured span on detail success). Its original output is red-telemetry-race.log. Isolated3cases had passed; inbound telemetry tests were moved into the already existing serial RailTelemetry collection used by outbound instrumentation, with bounded5s actual-span completion before inspecting nonempty/no-secret output. Full suite then134/134green. This5s test synchronization is separate from unchanged product5s child/8s operation budgets.

## Scenario traceability (source/offline)

The53 normative scenarios below map to executed test groups/source review; this is not actual Host/provider/load acceptance. The full 12-requirement corpus is unchanged.

| Capability / requirement | Scenario | Proof boundary |
|---|---|---|
| rail-journey-details / Explicit bounded detail selection | Open one current journey | JourneyHttpTests; JourneyDetailsServiceTests.Default_off_and_profile_notice |
| rail-journey-details / Explicit bounded detail selection | Invalid selection or request | JourneyHttpTests; JourneyDetailsServiceTests.Default_off_and_profile_notice |
| rail-journey-details / Explicit bounded detail selection | Disabled or changed profile | JourneyHttpTests; JourneyDetailsServiceTests.Default_off_and_profile_notice |
| rail-journey-details / Fresh confirmation before route lookup | Exact current selection | JourneyDetailsServiceTests (exact/limited/no-match/cache/failure); CacheTests |
| rail-journey-details / Fresh confirmation before route lookup | Selection changed or outside refreshed page | JourneyDetailsServiceTests (exact/limited/no-match/cache/failure); CacheTests |
| rail-journey-details / Fresh confirmation before route lookup | Conditional cache invalidation and concurrent refresh | JourneyDetailsServiceTests (exact/limited/no-match/cache/failure); CacheTests |
| rail-journey-details / Fresh confirmation before route lookup | Partial verification with a matching journey | JourneyDetailsServiceTests (exact/limited/no-match/cache/failure); CacheTests |
| rail-journey-details / Fresh confirmation before route lookup | Recheck fails or is not admitted | JourneyDetailsServiceTests (exact/limited/no-match/cache/failure); CacheTests |
| rail-journey-details / Source supplied route locator | Boarding occurs after the route lookup date | RouteLocatorTests; JourneyVerificationTests; JourneyProviderTests |
| rail-journey-details / Source supplied route locator | Missing or invalid locator | RouteLocatorTests; JourneyVerificationTests; JourneyProviderTests |
| rail-journey-details / Source supplied route locator | Safe locator normalization | RouteLocatorTests; JourneyVerificationTests; JourneyProviderTests |
| rail-journey-details / Route belongs to the selected occurrence | Whole supplied route and selected segment | RailRouteRulesTests; RouteParserTests; rail-details.decoder.spec.ts |
| rail-journey-details / Route belongs to the selected occurrence | Repeated station visits | RailRouteRulesTests; RouteParserTests; rail-details.decoder.spec.ts |
| rail-journey-details / Route belongs to the selected occurrence | Missing or contradictory route identity | RailRouteRulesTests; RouteParserTests; rail-details.decoder.spec.ts |
| rail-journey-details / Route belongs to the selected occurrence | Calendar echoes are not stop timestamps | RailRouteRulesTests; RouteParserTests; rail-details.decoder.spec.ts |
| rail-journey-details / Route belongs to the selected occurrence | Duplicate structural identity and optional facts | RailRouteRulesTests; RouteParserTests; rail-details.decoder.spec.ts |
| rail-journey-details / Route belongs to the selected occurrence | Unsupported interval detail | RailRouteRulesTests; RouteParserTests; rail-details.decoder.spec.ts |
| rail-journey-details / Faithful stop facts and missing times | Terminal nulls and explicit zero dwell | RailRouteRulesTests; RouteParserTests; decoder vectors; fictional partial browser |
| rail-journey-details / Faithful stop facts and missing times | Damaged optional intermediate time | RailRouteRulesTests; RouteParserTests; decoder vectors; fictional partial browser |
| rail-journey-details / Faithful stop facts and missing times | Required boarding and alighting times | RailRouteRulesTests; RouteParserTests; decoder vectors; fictional partial browser |
| rail-journey-details / Faithful stop facts and missing times | Inconsistent dwell or platform | RailRouteRulesTests; RouteParserTests; decoder vectors; fictional partial browser |
| rail-journey-details / Faithful stop facts and missing times | Empty optional platform | RailRouteRulesTests; RouteParserTests; decoder vectors; fictional partial browser |
| rail-journey-details / Faithful stop facts and missing times | Exact numeric and timestamp representation | RailRouteRulesTests; RouteParserTests; decoder vectors; fictional partial browser |
| rail-journey-details / Distinct detail outcomes and provenance | Complete and limited details | JourneyDetailsServiceTests; JourneyProviderTests; rail-details.decoder.spec.ts; state tests |
| rail-journey-details / Distinct detail outcomes and provenance | Failure after successful verification | JourneyDetailsServiceTests; JourneyProviderTests; rail-details.decoder.spec.ts; state tests |
| rail-journey-details / Distinct detail outcomes and provenance | Detail fetch does not refresh fares | JourneyDetailsServiceTests; JourneyProviderTests; rail-details.decoder.spec.ts; state tests |
| rail-journey-details / Distinct detail outcomes and provenance | Profile change while source work is running | JourneyDetailsServiceTests; JourneyProviderTests; rail-details.decoder.spec.ts; state tests |
| rail-journey-details / Distinct detail outcomes and provenance | Invalid detail envelope | JourneyDetailsServiceTests; JourneyProviderTests; rail-details.decoder.spec.ts; state tests |
| rail-journey-details / Bounded route execution and shared quota | Saturation and uncertain outcomes | JourneyDetailsServiceTests; JourneyBodyDeadlineTests; RouteParserTests bounds; JourneyProviderTests quota/header |
| rail-journey-details / Bounded route execution and shared quota | Cancellation between or during stages | JourneyDetailsServiceTests; JourneyBodyDeadlineTests; RouteParserTests bounds; JourneyProviderTests quota/header |
| rail-journey-details / Bounded route execution and shared quota | Bounded payload | JourneyDetailsServiceTests; JourneyBodyDeadlineTests; RouteParserTests bounds; JourneyProviderTests quota/header |
| rail-journey-details / Bounded route execution and shared quota | Body and parser remain inside the operation deadline | JourneyDetailsServiceTests; JourneyBodyDeadlineTests; RouteParserTests bounds; JourneyProviderTests quota/header |
| rail-journey-details / Temporary detail authority and private transport metadata | Earlier snapshot expiry wins | rail-details-state.spec.ts; RailPageComponent expiry live-region; RailTelemetryTests; RailOutboundTelemetryTests |
| rail-journey-details / Temporary detail authority and private transport metadata | Expiry during an unfinished request or suspended view | rail-details-state.spec.ts; RailPageComponent expiry live-region; RailTelemetryTests; RailOutboundTelemetryTests |
| rail-journey-details / Temporary detail authority and private transport metadata | Reopen and delayed response | rail-details-state.spec.ts; RailPageComponent expiry live-region; RailTelemetryTests; RailOutboundTelemetryTests |
| rail-journey-details / Temporary detail authority and private transport metadata | Attribution and secret redaction | rail-details-state.spec.ts; RailPageComponent expiry live-region; RailTelemetryTests; RailOutboundTelemetryTests |
| rail-journey-details / Temporary detail authority and private transport metadata | Route attribution becomes unavailable | rail-details-state.spec.ts; RailPageComponent expiry live-region; RailTelemetryTests; RailOutboundTelemetryTests |
| rail-journey-details / Accessible selection and isolated delivery scope | Filter hides the opened journey | rail-details-state.spec.ts; RailPageComponent; fictional Playwright15 + focus/viewport1; scope inventory |
| rail-journey-details / Accessible selection and isolated delivery scope | Keyboard and mobile route reading | rail-details-state.spec.ts; RailPageComponent; fictional Playwright15 + focus/viewport1; scope inventory |
| rail-journey-details / Accessible selection and isolated delivery scope | Fictional implementation proof | rail-details-state.spec.ts; RailPageComponent; fictional Playwright15 + focus/viewport1; scope inventory |
| rail-journey-details / Accessible selection and isolated delivery scope | No Trips or sales side effects | rail-details-state.spec.ts; RailPageComponent; fictional Playwright15 + focus/viewport1; scope inventory |
| rail-schedule-search / Local journey ordering | Absolute times and stable ties | rail-view.spec.ts; RailPageComponent; browser local view controls |
| rail-schedule-search / Local journey ordering | Limited results are not a global ranking | rail-view.spec.ts; RailPageComponent; browser local view controls |
| rail-schedule-search / Local time and carrier filters | Station local clock rather than browser time | rail-view.spec.ts; rail-details-state.spec.ts; browser local view controls |
| rail-schedule-search / Local time and carrier filters | Carrier selection without identity invention | rail-view.spec.ts; rail-details-state.spec.ts; browser local view controls |
| rail-schedule-search / Local time and carrier filters | Carrier options remain tied to the whole snapshot | rail-view.spec.ts; rail-details-state.spec.ts; browser local view controls |
| rail-schedule-search / Local time and carrier filters | Reset and boundary controls | rail-view.spec.ts; rail-details-state.spec.ts; browser local view controls |
| rail-schedule-search / Local time and carrier filters | Combined filters and empty visible list | rail-view.spec.ts; rail-details-state.spec.ts; browser local view controls |
| rail-schedule-search / Local time and carrier filters | Change view without extending data lifetime | rail-view.spec.ts; rail-details-state.spec.ts; browser local view controls |
| rail-schedule-search / Bounded execution and cancellation | Capacity exhausted | JourneyDetailsServiceTests.All_search_detail_mixes; RailDeadlineTests |
| rail-schedule-search / Bounded execution and cancellation | Cancellation with uncooperative child | JourneyDetailsServiceTests.All_search_detail_mixes; RailDeadlineTests |
| rail-schedule-search / Bounded execution and cancellation | Deadline and retry limit | JourneyDetailsServiceTests.All_search_detail_mixes; RailDeadlineTests |
| rail-schedule-search / Bounded execution and cancellation | Mixed search and detail workload | JourneyDetailsServiceTests.All_search_detail_mixes; RailDeadlineTests |

## Worker revision1 handoff status (historical)

Tasks1.1 through7.3 are source/offline complete (20/21);7.4 is reserved for the parent personal final review. Task7.1 is SOURCE ONLY: actual Host/OpenAPI execution pending. Task7.2 is SOURCE/describe/pure predicates ONLY: actual mixed load/memory pending. Ordinary CI remains unrun because no push/PR was authorized. Real provider compatibility/rights/keys/quota/banner/target rollout, paid/model execution and publication/deployment remain unrun and unauthorized. No staging/commit/push/PR/sync/archive or Trips edits.

Exact final changed-file SHA256 inventory is TestResults/rail-m2/changed-files-final.json. Final source manifest is TestResults/rail-m2/source-manifest-final.json, explicitly invoked with requested fresh M2 base a8721effd9056459d217966083816455fbea599d (not the historical M1 default); it includes the retained shared Rail detail fixture. No source mutations after that manifest.

## Authorized final-review correction, worker revision2

The parent explicitly reopened frozen worker snapshot021e8e4167ace08e10d9f552a9ce1f0ed33d49fe4fad14bf86780f95091f0350 for one scoped HTTP/platform exception-boundary correction. That original snapshot and its72unit/134integration evidence are preserved; this section records the later result and does not replace history.

Two actual lean HTTP regressions reproduced the defect: a throwing IRailRuntimeProfile.Current returned400 instead of500, and a throwing search-cache Get returned200/provider-failed instead of500. The executable RED2/2 is retained in TestResults/rail-m2/red-platform-boundary.log and red-platform-boundary.trx. The fixture uses the ordinary PlatformExceptionHandler/ProblemDetails middleware, authored input and only fictional reference loading, without Travel.Host/DB/network startup. It also checks malformed400 and oversized413 with no-store.

JourneyDetails now catches only input reading/parsing errors; service.GetAsync executes outside that catch. The unchanged M1 endpoint retains its existing implementation. The coordinator marks exceptions from invoking/awaiting provider children and normalizes only those as typed source failures. Platform cache/serialization/finalization exceptions propagate to sanitized5xx middleware instead of becoming provider-failed. Existing provider cancellation/profile/reference/deadline priority remains, without a new framework or dependency. Added positive unit vectors prove provider InvalidOperationException remains a sanitized failed outcome in each source stage.

Target GREEN2/2: dotnet test tests/rail/Travel.Modules.Rail.Tests.Integration/Travel.Modules.Rail.Tests.Integration.csproj --no-restore --filter FullyQualifiedName~Unexpected_platform_dependency_fault -p:SolutionDir=C:/Users/Vladimir_sva/.codex/worktrees/rail-m2-implementation/travel-agency/ -p:SolutionName=Travel --logger 'trx;LogFileName=platform-boundary.trx' --results-directory TestResults/rail-m2.

Final full unit:74passed/0failed/0skipped; final full integration:136passed/0failed/0skipped. Exact commands are the existing full Rail commands above, with evidence files platform-boundary-unit-full.log/.trx and platform-boundary-integration-full.log/.trx. Scoped CSharpier format/check passed on RailEndpoints.cs, RailJourneyDetailsService.cs, JourneyHttpTests.cs and JourneyDetailsServiceTests.cs (platform-boundary-format-check.log). Source changes in this correction are limited to those four files and this appended receipt; no other source/runtime/publication authority was used.

New immutable worker snapshot: TestResults/rail-m2/source-manifest-worker-v2-platform-boundary.json; changed-file inventory: TestResults/rail-m2/changed-files-worker-v2-platform-boundary.json. It is bound explicitly to a8721effd9056459d217966083816455fbea599d. All prior snapshots, source warnings and unrun gates remain as recorded. Task7.4 remains the parent's review gate. No source mutations after the revision2 snapshot.

## Final parent acceptance

Personal review7.4 is complete: [report](personal-review.md). Parent independently reran final Rail unit74/74 and adapter/lean HTTP136/136 (parent-unit-v2.trx, parent-integration-v2.trx), plus unchanged TS decoder39/39 and state/view/demo35/35; inspected the mobile screenshot, source invariants, all188worker-v2 hashes and preserved OpenAPI fields. No material findings remain open. Tasks21/21 are source/offline complete. Actual Host/DB/OpenAPI runtime, mixed load/memory, CI, real provider and publication gates remain unrun.

Parent finalization changes only review/task/documentation status after immutable worker-v2. Production/test source is unchanged. A separate parent-final source manifest and changed-file inventory are retained under TestResults/rail-m2; prior worker snapshots are not overwritten. There is no commit, push, PR or Trips change.

## Separately authorized runtime acceptance — 2026-10-22

Actual Host/OpenAPI passed 3/3 with disposable PostgreSQL. One fixed M2 run passed 6/6 cycles, 6000/6000 useful operations, 0 failures and all memory-recovery bounds. Executed source digest 41ef618fd29d2d5dce3272bf71f1cd9d8c67ad4adba170223bd78f0bb4d4a1bb and 10 Release DLLs remained unchanged. The earlier source-only pending statements above are historical. [Runtime verification](runtime-verification.md) records commands, full denominators, cleanup, warnings and the corrected textual profile hash; no provider calls or publication occurred.

## Pre-publication test boundaries — 2026-10-22

User authorized commit/push/PR to dev and waiting for ordinary CI, without merge, provider calls or Trips changes. Fresh origin/dev still equals a8721effd9056459d217966083816455fbea599d. A new full web ESLint check exposed four @nx/enforce-module-boundaries errors in three Rail test files; the earlier Biome/compiler checks did not cover this rule. Original JSON is retained in TestResults/rail-m2-publication-20261022/eslint-red.json.

Rail tests now use web-local fixtures following the existing rail-fixture pattern. The new web-local details fixture has identical authored data and differs from the decoder fixture only by its public type import; source-manifest includes it. Production code, measured profile, assertions and lint rules are unchanged. Fresh web lint passed with 0 errors/94 existing warnings; Rail TS 35/35 and Angular 22/22 passed. Scoped Biome passed with 3 warnings. Logs: lint-green.log, rail-ts-green.log, angular-green.log under the same receipt directory. This test-only correction follows the preserved runtime source 41ef618...; it does not relabel that measured run as executed on a later source digest.
