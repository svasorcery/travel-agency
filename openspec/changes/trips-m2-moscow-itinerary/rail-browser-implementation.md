# Task 4.7 — fictional Rail + Trips browser acceptance

**DONE WITH CONCERNS: local browser evidence is complete; parent personal review and fresh independent task review remain required.** 2026-10-21. No canonical task marker was changed.

Final whole Trips run: **80/80 passed = 5 M1 + 30 walking + 45 new Rail; 0 failed/skipped/retries**. Existing standalone Rail file: **9/9 passed; 0 failed/skipped/retries**. Final Trips discovery, result IDs and individual ledgers agree exactly: **80 expected, 80 actual, 80 unique ledgers; no missing/duplicate/unexpected IDs; repeatEachIndex=0**. These counts do not include the walking 12Q/12S quality denominators.

## Source and authority

Worktree: `C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency`. HEAD remains `a8721effd9056459d217966083816455fbea599d`, branch `codex/trips-m2-moscow-itinerary`. Original requested base `e654c76915103a82083ef2abc01c0579117c9749` remains an ancestor (exit0). No branch/index/commit/push operation occurred.

Read the exact brief SHA256 `70d25159fad05dc69111bd0eb6855ead4e755fec21564481aa62a689e758faef`, root AGENTS, test-authoring skill, ADR0006, current refinement/spec, actual component/state/projection/templates, nearest browser and component tests, and accepted R3 review/interface. Accepted source manifest remains SHA256 `1af7f86f8be821636b8d36838cd540090d24c4bb3aac1a3b7782f78e251fefb7`. All21 frozen source files match. All34 pre-final source/dependency input hashes remain unchanged across the final run;13 additional read-only supporting input hashes are recorded separately.

Changed only:

| File | Before | Final SHA256 | Bytes |
|---|---|---|---:|
| `tests/travel-e2e/trips-demo/trips-fixture.ts` | `dc211befeac4d4a63890d012e72f229c8ddf2d554a53949aa2ae0dcc270d3b75` | `8fe8fbd5457f93ba225a0fa6f8000db103754efc8f23f4300738d20cd724bf70` |19391|
| `tests/travel-e2e/trips-demo/trips-rail.spec.ts` | new | `2fbd1692212dd335b7db1c08c8a4405b1314c5e220612a4757177ce9b22ddf23` |31836|

Both old test modules are byte-identical to the independently preserved accepted4.4 files, proving preservation of every old body/assertion, including all five M1 bodies and their existing explicit afterEach receipt hooks:

- `trips-workspace.spec.ts`:11795bytes, SHA256 `85824169e248ca70685c8112956676594d619670cab287d11887b39575c19ac5`.
- `trips-itinerary.spec.ts`:22042bytes, SHA256 `7c2eb9b55540d4db36dcb052797eeb693fa69892ea546bc215796822d4779734`.

Exact two-file review diff against the preserved pre4.7 fixture (not Git HEAD): `D:/_Projects/_github/travel-agency/TestResults/trips-m2-rail-implementation/task-4.7-source.patch`,40382bytes, SHA256 `389961456965b1d5855c246ea156c8bcb2628af6cc7c073193f12d0b220f84b6`. Original baseline and final source copies are retained. The initial `rail-browser-source-baseline/trips-fixture.ts` snapshot was made after the first fixture edit; it is explicitly **not** the accepted before-file. `pre-4.7-trips-fixture.ts` and the preserved84 tree supply the verified accepted before bytes.

Historical preservation manifest SHA256 `4ea63d693f29b3041c448f689ba26df33b2d26088b0d08cb0719ea1514a30d38` still verifies **84/84** preserved entries by SHA/length. No historical browser root, screenshot or report was overwritten.

## Test boundary and changes

Browser E2E uses the existing4203 Angular Trips configuration, one worker, retries0, reuseExistingServer=false. All APIs terminate in the browser-memory fixture. The new opt-in Rail routes accept only same-origin station GET with one explicit bounded `q` parameter and search POST with the five published request keys. Authorization on Rail, unknown paths/methods and external browser requests are recorded, aborted and fail the receipt assertion. No private Trips title/interests/adults/budget are sent as search metadata.

The wrapper reuses published authored `fictionalRailSuggestions`/`fictionalRailSearch`, deep-cloning their replies before case-specific mutation. It does not modify the shared helper. Existing M1 owner/CAS/lost-PUT and finite walking vectors remain intact. Actual Angular document, public Data assets and fictional banner are not mocked. A narrow `TRIPS_DEMO_ARTIFACTS_DIR` routes outputs to a fresh owned TestResults directory; default CI location is unchanged. Resolved containment, absolute cross-drive rejection and existing symlink/realpath checks protect this path; JSON receipts refuse overwrite.

Final root: `TestResults/trips-m2/rail-browser-a8721eff-v7`. Each preliminary attempt has a separate v1–v6 root. Final discovery/validation/raw command logs are under `D:/_Projects/_github/travel-agency/TestResults/trips-m2-rail-implementation/`.

| New scenario group | Cases | Actual browser coverage |
|---|---:|---|
|RAIL01|2|1280px/390px walking preview→stage→explicit Save→Rail search→one stage→editor edit→explicit Save→reload. Exact prior IDs/body/order/brief/money, independent dates, no hidden GET/PUT, null time/estimate, historical marker, meaningful train number, real markup/form ancestry and keyboard selection.|
|RAIL02|1|Nonempty Kaliningrad14-day M1 plan; preserves other days/estimates and walking empty-only restriction.|
|RAIL03|3|Full8/day, essential note500-scalar overflow, prospective128KiB full-write refusal. Actual Trips codecs construct the valid near-limit seed; exact editor values remain unchanged after refusal.|
|RAIL04|10|Null result TTL; damaged/missing tickets; missing mode; unsafe limit; safe page/interval limit; profile epoch/generation mismatch; overlong number fallback. Decoder rejection and displayed disabled-row refusal are distinguished by actual UI.|
|RAIL05|3|Complete-empty, limited-zero interval and failed responses retain distinct explanations.|
|RAIL06|1|Safe train with independently skipped suburban mode.|
|RAIL07|4|Actual iframe error handler; enforced matching CSP event; report-only event; browser-enforced frame-src none. Failure clears facts/choice; report-only retains eligible choice.|
|RAIL08|1|Synthetic real reference under induced stricter enforcing self CSP cannot stage/persist/fall back; zero external attempts in final run. Composed with accepted actual component real-view-only proof, as explained below.|
|RAIL09|1|System time passes expiry before timer delivery; actual click cannot stage.|
|RAIL10|1|Date A→B→A revokes old result; latest failed station reference cannot resurrect it; no retry.|
|RAIL11|9|Pending source reply crossed with title edit, dirty brief, close, refresh/read, actual Save, logout, owner switch, new plan and delete. No late reopen/append/overwrite/focus steal.|
|RAIL12|3|Staged intention retains ordinary412 CAS, lost-PUT uncertainty and newer edit during pending Save; one explicit PUT, no automatic retry/rebase.|
|RAIL13|1|Keyboard open/Escape/close/reopen, delayed station A→B→A reply, empty reopening and focus preservation.|
|RAIL14|3|Null reference TTL, changed reference generation and changed profile epoch require explicit reselection.|
|RAIL15|1|Independent failed mode warning does not invalidate safe selected train.|
|RAIL16|1|All-skipped remains distinct from failed/empty.|

RAIL01 asserts exact `9007199254740993.05 RUB`, original `+03:00` times, IANA/sourceUpdatedAt/seat/realtime unknowns, reception versus expiry, and no fare copied into budget or estimate. It checks actual sandbox/referrer attributes, rendered local banner text and viewport visibility; load alone is not used as visibility proof. Double click appends once. Reload shows only ordinary M1 text, with no trusted Rail source panel or note parsing.

Clean Save intentionally sends no mutation and does not close the picker (`TripsStateService.save()` returns when clean). The actual Save cross-boundary case therefore performs an explicit local edit, which invalidates Rail, then explicit Save while its canceled source callback is still pending. It asserts the resulting one PUT and no late Rail effects. This is not a claim that a clean no-op Save cancels Rail. The separate RAIL12 case crosses a real pending Save with a later form edit.

## Exact results and network evidence

`rail-browser-v7-validation.json` records all80 discovered/result/ledger IDs, case titles, files, individual receipt paths, retry/iteration checks and source preservation checks. Final browser ledgers record **4006 same-origin page requests,1079 fixture API calls,137 Rail calls,25 explicit Trips PUTs,0 unknown failures,0 external attempts**. The PUT denominator includes old M1/walking and explicitly authorized Save cases; Rail preview/search/stage itself does not persist.

APIRequestContext calls are separately explicit: six same-origin actual public asset GETs in unchanged walking acceptance and two same-origin actual document GETs in RAIL01. Their own raw files record status/bytes/SHA/canonical comparisons or headers/client meta. They are not silently folded into the page-event request count. All six actual public asset bytes match canonical source hashes (`actual-public-assets.json`). No real itinerary generator or Q harness was invoked.

Existing standalone extraction regression used exactly `demo/rail-search.spec.ts` through unchanged `playwright.flights-demo.config.ts`, fictional Node API5100 plus Angular4201, one worker/local retries0/no reuse. Read the retained Rail helper, server imports/composition/Rail-first dispatch/loopback listen and proxy; no external transport is used by this source slice. All9 discovered IDs have one passed result. Existing strict page routing and request assertions remain unchanged. The readonly standalone file has its existing result/attachment evidence, not new per-case terminal network ledgers; the80-ledger claim belongs to Trips.

Scoped final Biome: **0 errors,3 retained non-null assertion warnings in the existing fixture,11 infos**; new Rail spec has no non-null assertion warning. No suppression/config change. TypeScript no-emit over the two owned files with existing base options: **0 diagnostics**. Raw preliminary/final diagnostics remain in the log inventory.

## Preserved preliminary failures

| Attempt | Outcome | Cause/boundary |
|---|---|---|
|v1|2 timed-out ledgers; remaining38-case attempt stopped early|Exact label query incorrectly included native select option text. Stopped only the owned Playwright/helper process tree; partial run is not a full38-case result.|
|v2|34passed/4failed of38;1 external attempt aborted|Seed totals/size, broad option selector, and incorrect prepared CSP assumption. External GET to official iframe was blocked by the terminal route before network.|
|v3|42passed/2failed of44;0 external attempts|Shared authored station/attribution objects were mutated by a test, contaminating later seeds. Wrapper deep clone fixed fixture isolation.|
|v4|43passed/1failed of44;0 external attempts|Valid UTF8 seed still had enough capacity for the small projection; product correctly appended. Seed now fills the actual detail codec's final residual bytes.|
|v5|44passed/1failed of45;0 external attempts|Incorrect clean-Save cancellation assumption, corrected to the actual explicit edit→Save contour above.|
|v6|1/1passed|Focused corrected actual Save case.|
|v7|80/80passed|Single final complete Trips suite; exact inventory validated.|

No failed raw log, ledger, trace, error context or preliminary screenshot was removed, renamed into a pass or overwritten. v2 JSON reporter output used a config-relative nested path; that original file is retained and inventoried. Preliminary source revisions were not all snapshotted before their runs; their raw failure contexts/traces are retained, while the exact final source/hash binding is the34-file v7 preflight and post-run comparison. No retrospective preliminary source hashes were invented.

## CSP / real-source limitation

The prepared brief's self-only default CSP assumption was incorrect. Actual document headers and client meta are in `rail-document-policy-1280.json` and `rail-document-policy-390.json`: the Angular response has no CSP response header, and the installed client meta is exactly `frame-src 'self' https://yandex.st/rasp/media/apicc/copyright_vert_mono.html`.

Consequently v2's synthetic real reference attempted that exact official iframe URL. The strict fence aborted it before network; this observed route abort did not produce the iframe error-clearing behavior. It is retained as an observed browser limitation, not claimed as complete network-failure visibility or official-banner acceptance.

The parent explicitly authorized an additional stricter test-only enforcing `frame-src 'self'` meta before the synthetic real reply. Its intersection with the unchanged served policy induces an actual CSP failure, clears facts/choice and produces no external attempt in final RAIL08. No source policy was relaxed, no official banner was fulfilled locally and no real URL was followed. The otherwise-valid real-row view-only guarantee is the accepted actual component test `trips-page-rail.component.spec.ts:270`, in the unchanged21-file source manifest and retained846-test web receipt. This composed evidence does not show a stable real banner or real supplier integration.

## Visual and environment receipts

Viewed actual v7 PNG pixels with `view_image`, original detail, for all six desktop/mobile picker/staged/reloaded detail images. Focus outlines, readable independent date labels, fictional warning/source attribution, exact fare, train-number title and null money/time controls are visible. Mobile390px has no horizontal overflow; full-page captures are retained too. Standard textarea scrolling does not discard the exact persisted note; DOM/body/reload assertions compare its full value. The final root contains15PNG files, including the three unchanged walking captures; dimensions and SHA are inventoried. No generated image or new asset was used.

Node `v26.11.1`, installed Playwright `1.64.1`, physical root node_modules (not a junction). Observed launched browser path is the existing `chromium_headless_shell-1248/.../chrome-headless-shell.exe`; binary reports `Google Chrome for Testing 156.0.8078.4`, SHA256 `f77d8e27c8db1e4ac0399459a00e0c97bb9417adc393474cabaffebe2314ecb0`. Package/lock/current installed lock hashes are in the preflight. This is local Node26 evidence, not CI Node22 acceptance.

Every run used per-process `NX_DAEMON=false`, `NX_NO_CLOUD=true`, cache `D:/_Projects/_github/travel-agency/TestResults/trips-m2-dependencies-a8721eff/nx-cache`, TEMP/TMP its `tmp`, and socket directory `D:/_Projects/_github/travel-agency/TestResults/m2-ipc-01a15a`. C free remained above1.5GiB (final2846625792bytes). No install/download/audit/cleanup was performed. Normal sandbox writes to the designated C worktree were denied; the authorized escalation for owned test/evidence work succeeded. No automatic approval rejection occurred.

Preflight verified4201/4203/5100 closed before final Trips and standalone. Existing Playwright webServer lifecycle closed its owned servers normally; final connect probes return **ECONNREFUSED on all4201/4203/5100**. No user server was reused/killed. The sole early termination was v1's explicitly owned helper tree.

Commands are retained in result JSON `config.argv` and config metadata, plus raw logs. Final essential commands, from worktree root with the environment above:

```text
node node_modules/@playwright/test/cli.js test --config=tests/travel-e2e/playwright.trips-demo.config.ts --list --reporter=json
node node_modules/@playwright/test/cli.js test --config=tests/travel-e2e/playwright.trips-demo.config.ts --output=TestResults/trips-m2/rail-browser-a8721eff-v7/test-output --reporter=list,json
node node_modules/@playwright/test/cli.js test rail-search.spec.ts --config=tests/travel-e2e/playwright.flights-demo.config.ts --output=TestResults/trips-m2/rail-standalone-a8721eff-v1/test-output --reporter=list,json
node node_modules/@biomejs/biome/bin/biome ci tests/travel-e2e/trips-demo/trips-fixture.ts tests/travel-e2e/trips-demo/trips-rail.spec.ts
```

The JSON output paths were absolute and create-only per attempt; discovery had a separate output file. TypeScript used `typescript.createProgram` on exactly the two owned files with parsed existing `tsconfig.base.json`, `noEmit:true`, `types:['node']`; final diagnostic count0.

`task-4.7-hashes.json` and `rail-browser-receipt-inventory.json` bind changed source, exact before-file, preserved old bodies, frozen prerequisite source, dependency/supporting inputs, all raw attempts/logs/ledgers/screenshots and final validation/closed-port receipts. No Host, DB, schema/key, provider/model, real-Q, quality/human review, CI, enablement, rollout or publication acceptance is claimed. No product defect was fixed or concealed in this browser task. Parent review and independent review are the next gates.
