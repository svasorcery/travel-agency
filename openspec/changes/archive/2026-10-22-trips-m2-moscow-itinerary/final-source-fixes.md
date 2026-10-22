# Task 6.1 — unified F1–F4 fix wave

**DONE_WITH_CONCERNS.** All four accepted findings fixed in the authorized source/local demo scope. Final focused111/111, helper16/16, fullweb864/864, fresh Trips75/75 and standalone9/9 pass. ONE production build passes with retained style warnings. Controller personal review and the SAME final auditor's scoped F1–F4/new-breakage recheck remain pending; task6.1 is not closed here.

## Identity and exact review delta

Worktree: `C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency`. Branch `codex/trips-m2-moscow-itinerary`; HEAD `a8721effd9056459d217966083816455fbea599d` remains context only. Original requested fresh base `e654c76915103a82083ef2abc01c0579117c9749` remains ancestor (exit0). No switch/reset/rewrite, index mutation, commit or push. Final empty-index and whitespace checks exit0.

Read accepted review SHA `cfe7488c72b3e4ce42b1d6949387303561dfef6ec18b148a29653e1802c368a7` and fix brief SHA `1438a0f89aae0919ddfbde44834ff25f9f3d0f9c33e8b169e6b2355d11102495`; independently rehashed both. Applied receiving-code-review and Travel test-authoring, read root AGENTS/ADR0006/nearest tests; no lower AGENTS under owned paths.

Before any edit, independently matched and saved all9owned source bytes against frozen184path manifest SHA `6f26722b196ac150d8fbd1e57583bee947db3ebe0b4555e0bf2572105dd53ef3` / tree `5185181c407d535a63cb4e64874e4c78c02dfe8af431038c8ee65e16767b535e`. Exact originals remain in `D:/_Projects/_github/travel-agency/TestResults/trips-m2-final-fix-a872-v1/before/`; before inventory `before-manifest.json`. Every saved original rehashed at final seal.

Complete FIX-ONLY unified patch versus those original WIP bytes: [source-final-sealed.patch](D:/_Projects/_github/travel-agency/TestResults/trips-m2-final-fix-a872-v1/source-final-sealed.patch), **392contentlines**, SHA `9f9c19cf7bf3de50d1b63b4ac44d2fe6a4108e1c12996be80ed7891f553813f6`. No HEAD-relative feature diff substitutes for this patch. [source-final-sealed.json](D:/_Projects/_github/travel-agency/TestResults/trips-m2-final-fix-a872-v1/source-final-sealed.json), SHA `b63aa5fb8af26bd8a519cd3d23a6e23736efd0919018e7fb84f3b0f5dc0c2bee`, contains every before/after size/hash, current184/1657drift and98unchanged producer bindings. All9source paths modified; no new production/test source path. Three runtime files and six test files only. All9before/current files retain LF; no whole-file EOL churn.

| Owned path | Before SHA256 | After SHA256 |
|---|---|---|
| `tools/trips/build-moscow.test.mjs` | `b2ac3ab99396b381140a933792a5881fc1226ce7c1d174de00f07a2673ebccb4` | `bc760533d60a09ea56a3536e538e843c849ae7bed59e394659ca90fb0436be5c` |
| `apps/web/src/app/rail/rail-search.component.html` | `d2b79d94b3204b83f9db140b2a886214187e8d17b9e1746425693c2cfdac5e6c` | `746471f6ff1e27e28f38d9f727971df7609a94e0047efd4bc92dde2045f74222` |
| `apps/web/src/app/rail/rail-search.component.spec.ts` | `e7fdaf567775491ab5cdb80c6ed2d26fb59fb13d0b6d7da3da7fbc7674d3de28` | `41f0b29458f3094cb5f8d1e15e7263d9ac203d599eab81aa8763d677fa3af411` |
| `apps/web/src/app/trips/trips-page.component.ts` | `8206a2e9cb41167d3fd3bb45cf4f7d00e596d2ef63e59cefeb6643ca23960eb1` | `5ed7c6103b94fd0adb9cf235823e38a984f26319959a54602204a05bc81d0c80` |
| `apps/web/src/app/trips/trips-page.component.html` | `66c0ef375b2279268e9386fcfd022201f5b8475bdc0a34f40a381e4fb05b2a07` | `9647ff55798edcfeadc84252bca63e18e69bf32a0e1e78b010fec70bc2b09640` |
| `apps/web/src/app/trips/trips-page-rail.component.spec.ts` | `19dccd9988b25b415872ee4c844674a2f930780258af5b11d15d8ccb96d5c374` | `896a7791283ed8ee80492805e86a6ccec0d251c02ada2eb24ed3a9d00fc76430` |
| `apps/web/src/app/trips/trips-page-itinerary.component.spec.ts` | `a344d251541f3acccb33a10a882c7124d7baa8f65ef6553e3457bb637ff8260b` | `20b42ca07f137bd6ef3cd528595a2802e8e738f6ed0592c2a6d83f1e98f06687` |
| `tests/travel-e2e/trips-demo/trips-itinerary.spec.ts` | `7c2eb9b55540d4db36dcb052797eeb693fa69892ea546bc215796822d4779734` | `371215b735efaa0e0bbd99465870130ab53f0009af665e75dc199f56d56530ff` |
| `tests/travel-e2e/trips-demo/trips-rail.spec.ts` | `2fbd1692212dd335b7db1c08c8a4405b1314c5e220612a4757177ce9b22ddf23` | `3b22507d0b719b98f6ca917ddfb23c11c6e1a4ba9264e7eda2978017740ffdf3` |

Controller-owned docs are outside this fix delta. Current frozen184drift is exactly9owned paths plus4declared docs: operations.md and change design/tasks/verification. The new byte-exact final-source-audit.md belongs to controller. No other frozen184path drift.

## Findings and behavioral proof

- **F1/P2:** invalid publication derives from fixture download/build UTCday+1day. A bounded fixtureDay argument keeps identity/download/as-of bound to that same fixture clock. Regression checks21,22,23Oct2026,1Jan2027 and29Feb2028; the literal failed on22Oct with0rather than86400000ms. Existing rejection/no-output checks remain; full16helper tests execute actual CLI on fictional fixtures. Production helper, actual immutable Data, fixed Q clock and all production freshness checks unchanged. Existing confined owned-temp cleanup retains resolved absolute dirname/prefix checks.
- **F2/P3:** shared Rail template chooses only fare provenance wording by strict result.profile: fictional “Учебная цена”, real original Yandex attribution. Both actual shared-component profiles pass strict decoder admission and DOM assertions. Exact9007199254740993.05RUB, unknown scope/seats/realtime/IANA/source notices remain. Both Trips picker widths have browser assertions and actual viewed pixels. No DTO relabel, fare persistence, real retention or flags changed.
- **F3/P3:** refreshAvailability final focus closure rechecks captured context/draft/brief plus the exact current availability/status and no pending outcome. Generation success/error final closure rechecks captured empty foundation, owning preview/status and no pending outcome. Existing focusIntent/afterNextRender remains authority. Fifteen deterministic render-boundary cases cover3outcomes × unchanged/title edit/brief edit/direct dismissal/newer intent, with no sleeps. Positive unchanged focus and newer-intent focus stay intact. Existing R3adoption-error method is byte-identical; pending-owner-read exclusion/state source unchanged and included in fullweb/browser coverage. No staging/write/revision machine changes.
- **F4/P3:** one visible demo/uncalibrated/no-human-quality summary replaces repeated warning paragraphs/list entries. Explicit unmet interests, all relevant exclusions and unknown access/costs remain. preparedBy/buildAsOf and exact75/45/crossing model assumptions move to existing source/data details; original source/download/attribution/per-edge “Без полевой проверки” remain reachable. DOM tests distinguish visible copy from closed details; browser opens/closes the existing details after its original keyboard sequence. No API facts or CSS changed.

Self-review read all393patch lines including the final browser-only order/screenshot delta. Rechecked closure ownership, dismissal/newer action behavior, unchanged adoption-error guard, unknowns, privacy, explicit Save and real source view-only boundaries.

## Actual checks and raw attempts

All paths in this table are under `D:/_Projects/_github/travel-agency/TestResults/trips-m2-final-fix-a872-v1`. Every result JSON retains exact argv, start/end, raw exit, Cfree before/after and ONLY sixexplicit assigned environment keys. Full stdout/stderr remain separate. Earlier failed/preliminary attempts are retained and are not relabelled green.

| Attempt | Raw exit | Outcome | Raw files |
|---|---:|---|---|
| `helper-red` | 1 | 0/1; expected dynamic-day RED | `helper-red.result.json`, `helper-red.stdout.log`, `helper-red.stderr.log` |
| `web-red` | 1 | 99/111;12fail incl1preliminary real-fixture URL error | `web-red.result.json`, `web-red.stdout.log`, `web-red.stderr.log` |
| `helper-green-focused` | 0 | 1/1; five explicit UTCfixture dates inside one test | `helper-green-focused.result.json`, `helper-green-focused.stdout.log`, `helper-green-focused.stderr.log` |
| `web-green-focused` | 1 | 110/111; test-only closed-details ancestor assertion | `web-green-focused.result.json`, `web-green-focused.stdout.log`, `web-green-focused.stderr.log` |
| `web-green-focused-final` | 1 | 110/111; same assertion after failed LF/CRLF edit | `web-green-focused-final.result.json`, `web-green-focused-final.stdout.log`, `web-green-focused-final.stderr.log` |
| `web-green-focused-v2` | 0 | 111/111;3files,0skip | `web-green-focused-v2.result.json`, `web-green-focused-v2.stdout.log`, `web-green-focused-v2.stderr.log` |
| `helper-full` | 0 | 16/16; before unused declaration removal | `helper-full.result.json`, `helper-full.stdout.log`, `helper-full.stderr.log` |
| `helper-full-final` | 0 | 16/16; final helper bytes,0skip | `helper-full-final.result.json`, `helper-full-final.stdout.log`, `helper-full-final.stderr.log` |
| `web-full` | 0 | 864/864;57files,0skip; ONE fullweb run | `web-full.result.json`, `web-full.stdout.log`, `web-full.stderr.log` |
| `trips-browser` | 1 | 74/75; new details clicks disturbed existing keyboard sequence | `trips-browser.result.json`, `trips-browser.stdout.log`, `trips-browser.stderr.log` |
| `trips-browser-final` | 0 | 75/75;30walking+45Rail;0skip/retry/flaky | `trips-browser-final.result.json`, `trips-browser-final.stdout.log`, `trips-browser-final.stderr.log` |
| `standalone-browser` | 0 | 9/9;0skip/retry/flaky; fresh own output | `standalone-browser.result.json`, `standalone-browser.stdout.log`, `standalone-browser.stderr.log` |
| `biome-final-after-test-order` | 0 | 7unchanged,0errors,38warnings,18infos,0omitted | `biome-final-after-test-order.result.json`, `biome-final-after-test-order.stdout.log`, `biome-final-after-test-order.stderr.log` |
| `production-build` | 0 | exit0; ONE build,75files,6exact assets,3publicroutes | `production-build.result.json`, `production-build.stdout.log`, `production-build.stderr.log` |

Essential command families from worktree root:

`node --test [--test-name-pattern=...] tools/trips/build-moscow.test.mjs`.

`node node_modules/nx/dist/bin/nx.js test web --include=**/rail-search.component.spec.ts --include=**/trips-page-rail.component.spec.ts --include=**/trips-page-itinerary.component.spec.ts --watch=false --skipNxCache`; ONE fullweb omits the3include arguments.

`node node_modules/@playwright/test/cli.js test trips-itinerary.spec.ts trips-rail.spec.ts --config=tests/travel-e2e/playwright.trips-demo.config.ts --output=<owned-new-output> --reporter=list,json`; standalone uses `rail-search.spec.ts` and existing `playwright.flights-demo.config.ts`. Final TRIPS_DEMO_ARTIFACTS_DIR explicitly names CWT `TestResults/trips-m2/final-fix-browser-a872-v2`; JSON explicitly names owned D output. Preliminaryv1 remains untouched. No transformed/copied specs.

`node node_modules/nx/dist/bin/nx.js build web --configuration=production --skip-nx-cache --outputPath=D:/_Projects/_github/travel-agency/TestResults/trips-m2-final-fix-a872-v1/production-dist`.

The final helper16repeat was necessary after removing a newly unused clock declaration identified by Biome; it is not32tests. Fullweb ran exactly once after final UI edits. Later e2e-only assertion ordering and test PNG additions did not alter current UI/component tests. Final browser75is a fresh separate run after fixing the test insertion; no automatic retries. Historical5M1 browser cases and prior80 remain historical accepted evidence with unchanged test bodies, **not current exact whole-UI closure or fresh80**. Current864web and75walking/Rail cover current edit/stage/Save/reload behavior.

Preliminary real-profile fixture used the wrong official banner URL and was corrected to the exact existing allowlist. F4's first DOM assertion mistakenly included closed details through ancestor li; corrected to remove details from a visible clone. One edit helper first expected CRLF where files were LF; its no-write failure caused one unchanged110/111repeat before correction. The first75browser attempt failed because added details clicks preceded the existing keyboard story; moving only those new assertions after the original story preserved meaningful native keyboard proof. Failed trace/context/ledgers/logs remain. `operational-attempts.json` additionally records read-only path/parser/ANSI/patch-child/label probe failures; no product workaround or hidden failure. The normal sandbox native-git child attempt failed; the authorized scoped elevated read-only diff succeeded. No automatic approval-review rejection.

## Browser, pixels and production evidence

[verification-proof.json](D:/_Projects/_github/travel-agency/TestResults/trips-m2-final-fix-a872-v1/verification-proof.json) independently checks75unique passing IDs/results/ledgers, zero retry/repeat/skip/flaky/failures/external attempts;30walking+45Rail. Final ledgers total3668requests/989API/137Rail/15explicitPUT. Standalone9unique results pass with zero retries/skips; no fictional terminal ledgers are invented for standalone.

Four NEW actual PNGs were viewed with original/full-size detail in CWT `TestResults/trips-m2/final-fix-browser-a872-v2`: walking-preview-desktop.png, walking-preview-mobile.png, rail-picker-detail-1280.png, rail-picker-detail-390.png. `reuse-and-visual-proof.json` records dimensions/hashes. At desktop/mobile the summary/constraints precede legible stops without builder metadata; source details remain usable. Exact fictional price and notice are legible, with no overflow/overlap observed. Full-page walking, staged/reloaded Rail and standalone PNGs also remain as raw artifacts; no extra visual review is claimed for unviewed images.

Fresh production output has75actualfiles and6publicdata assets equal current canonical bytes, including separate manifest and every child. Exact asset hashes/all75dist hashes are in verification-proof. Actual prerendered-routes.json has **only /, /hotels, /hotels/reservations**. Source Trips/Trips/**/Rail remain client-only. Existing olddist was preserved by the explicit D output override. Cache skipped; no build-budget change.

Retained build warnings: Trips5.01kB against4kBwarning/8kBerror; existing Support4.24kB and Flights4.50kB. All3raw warnings retained. Trips CSS hash is unchanged. Final Biome38warnings/18infos are retained; one new known-fixture non-null assertion follows neighboring test style, remaining warnings are on existing lines; no suppression. NO_COLOR/FORCE_COLOR and NodeDEP0205 messages retained. These are disclosed warnings, not warning-free acceptance or a new budget waiver.

## Frozen quality and reused unaffected proof

No Q/S/actual-Q Node run, producer rebuild, tuning, provider/model call or generated-fixture change. Explicit98path closure rehashed exact against historical Qsource/final frozen record: actual QgeneratorFact and its tests/helpers, Core/Application/API/Infrastructure, sixData files, production helper, TSdecoder/types/transport/actual-Qchecker, raw wire vectors, frozen quality-cases/protocol/instantiations recipe and recorder. Runtime/staging/decoder/Data/model production authority unchanged.

Historical Q remains exactly the accepted first `quality-demo-a872-v1`:1Fact/24records(Q12+6edge+6negative),12/12targets and13Nodechecks. This wave does not rerun or create current Qresults. The original global1657source hash is **historical superset only**; current9UI/test edits and declared docs change it. source-final-sealed lists every drift rather than falsely claiming global identity. Existing modules/trips/README.md drift preceded this wave.

All19accepted receipt-index references rehashed exact, including the accepted Qbinding/receipt/TRX/post-report references. 110priorv7/standalone artifact hashes rechecked exact; all23original Qrun artifact files inventoried read-only. Unaffected294Unit/98loader/85API+30M1HTTP/208Architecture/356ordinaryNode and local dependency/structural checks are reused through unchanged corresponding source/config/receipt hashes, not rerun. No installation, npm audit, dependency network check or full installed-corpus rehash.

## Environment and artifact seal

Per-process NX_DAEMON=false, NX_NO_CLOUD=true; existing D dependency nx-cache/tmp and short m2-ipc path. Nodev26.11.1/.NETSDK10.0.301 local differsCI. Own installed Playwright/Chromium only; no downloads. C remained above1.5GiB; final recorded2039033856bytes. Ports4201/4203/5100 are allECONNREFUSED after normal owned webServer teardown; no user server reused/killed. No inherited environment/credential/config enumeration or serialization. No real Host/DB/key/schema/provider/model/feature-enable/rollout/cleanup/archive action; only existing safely confined fixture cleanup.

Complete artifact hash inventory: [artifacts-manifest.json](D:\_Projects\_github\travel-agency\TestResults\trips-m2-final-fix-a872-v1\artifacts-manifest.json), **359files**, SHA `e4a026ce1e84e831956610af1ee5279d2b887acce0d4fb7cf625a72a35f574dd`. It binds preserved before bytes, every attempt log/result, recorder/edit/proof scripts, exact fix patch/source manifests, both fresh browser evidence roots, standalone outputs and productiondist. Report itself and that manifest are bound by `seal.json` to avoid self-hash cycles. No existing report/audit/evidence overwritten.

No source mutation remains. Controller must read the complete fix-only delta and send the SAME final auditor its scoped recheck; source/local acceptance, CI, human quality/calibration, real banner/retention/rights/quota, target and rollout remain separate.
