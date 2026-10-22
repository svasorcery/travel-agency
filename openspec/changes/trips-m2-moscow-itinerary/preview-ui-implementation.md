# Task 4.3 — frozen after review fix round 1

Current source is frozen for the parent's scoped reviewer recheck. The canonical task has not been marked complete. Fixed HEAD remains e654c76915103a82083ef2abc01c0579117c9749; branch codex/trips-m2-moscow-itinerary. The initial implementation receipt below is historical; current acceptance evidence is this fix-round section.

## Review finding and bounded fix

Read the complete independent task-4.3-review.md (review SHA256 cd4984d66a88b94bab8aa46fc86c3eb9f2c9b1f319f419a2d3543a177bb2f33e) and receiving-code-review skill, then verified the actual component/state/client cancellation path. Sole P2-1 is reproduced: explicit Close cancels adoption while leaving the clean M1 foundation current, so the old awaited false result used to schedule status focus after the newer dismissal requested generation-button focus. No data/CAS defect was found or patched.

Only two source/test files changed against the parent-created task-4.3-fix1-old snapshot:

- apps/web/src/app/trips/trips-page.component.ts
- apps/web/src/app/trips/trips-page-itinerary.component.spec.ts

The five-file owned set is retained in the manifest. HTML, SCSS and the original M1 component spec are byte-identical to that snapshot. State/client/backend/root configuration/README/canonical documents remain untouched. Close stays enabled during pending adoption; manual edits and existing Save remain usable.

A small component focus-intent counter captures current UI intent before awaited M2 actions. Explicit dismissal, route entry and subsequent M1 focus actions supersede an older intent. M2 continuations check the captured intent after await; the existing afterNextRender focus helper checks it again immediately before DOM focus. Actual canGenerate/canAdopt queries avoid superseding a valid focus intent for a no-op double submission. State remains the only cancellation/adoption/ticket/CAS owner; no second adoption machine, read retry, hidden write or baseline advance was introduced. Existing M1 validation/delete focus uses the same helper and stays covered by retained M1 tests.

## Current verification and receipts

All commands used the previously approved process-only D temp/cache/short Nx socket settings; no install, browser/Host/DB/network/model/key/Git mutation, feature enablement, cleanup, configuration suppression or subagent occurred. C headroom was checked before heavy runs and final recorded free space is 2580803584 bytes (about 2.40 GiB), above the 1.5 GiB stop threshold.

- task-4.3-fix1-red-final.log: meaningful RED, 4 failed / 42 passed in 46 tests. Three actual component/state/both-client cases hold both mandatory reads, only owner GET, or only availability GET pending; focus and click enabled Close; then cancellation/render settles. All three reached the final focus assertion with no preview, unchanged editor, no PUT and canceled pending reads, but received itinerary-status instead of generate-itinerary. The fourth reproduces the same late-continuation focus cause through direct generation cancellation.
- task-4.3-fix1-red.log retains the first exploration: the same four genuine focus failures plus an invalid test expectation for direct cancellation of an availability refresh, where the generation button does not exist because availability is checking. That unreachable recovery expectation was removed without changing product/state behavior. The corrected RED above is the behavioral evidence.
- task-4.3-fix1-green.log: final formatted-source focused GREEN 46/46 tests, 2 files, 0 failed/0 skipped, exit 0; started 2026-10-21 16:11:40 Europe/Moscow, Nx 12.7 s. Four new regression cases plus the existing 42 cases. Each pending adoption-read permutation now preserves explicit dismissal focus, cancels reads and performs no staging/PUT. The direct generation cancellation case also preserves the newer focus.
- task-4.3-fix1-web.log: ONE full web run after final formatting, 735/735 tests, 54 files, 0 failed/0 skipped, exit 0; started 2026-10-21 16:13:00 Europe/Moscow, Nx 16.0 s. Initial baseline 698 + 33 initial component cases + 4 focused regression cases. No repeated wide run was performed in this fix round.
- task-4.3-fix1-biome.json: final check exit 0, 0 errors, 3 warnings and 10 infos; no new diagnostics. task-4.3-fix1-biome-provenance.json binds all 13 to exact unchanged M1 HEAD lines and baseline SHA256. No unrelated refactor or suppression. Biome supports four TS/HTML inputs; SCSS was explicitly passed but remains unsupported. Owned git diff --check passes. Existing NO_COLOR/FORCE_COLOR warnings remain visible.

Commands: the original focused/full/check commands in the historical receipt remain the same, with the new logs named task-4.3-fix1-*. The original task-4.3-green-final.log (42/42), task-4.3-web-frozen.log (731/731), initial report and initial manifest are preserved; parent created task-4.3-fix1-old before changes. Current manifest now binds all five exact current sources, the two-file delta, old snapshots/review, exact current receipts and this updated report.

Browser 4.4, production build/budget 5.1, source/route quality and target/publication gates remain separate. No browser or live correctness is claimed. Parent should run the scoped reviewer recheck on this frozen fix; no further source changes or wide reruns are planned without a concrete new defect.

## Historical initial implementation receipt


Source is frozen for the parent's independent review. This report does not mark the canonical OpenSpec task complete. Fixed HEAD remains `e654c76915103a82083ef2abc01c0579117c9749`, branch `codex/trips-m2-moscow-itinerary`, worktree `C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency`.

## Owned files

- `apps/web/src/app/trips/trips-page.component.ts`
- `apps/web/src/app/trips/trips-page.component.html`
- `apps/web/src/app/trips/trips-page.component.scss`
- `apps/web/src/app/trips/trips-page.component.spec.ts`
- New focused integration suite: `apps/web/src/app/trips/trips-page-itinerary.component.spec.ts`

No state/client/backend/data/browser/root configuration, README or canonical planning document was edited. No Git mutation, browser/devserver/Host/DB/model/provider/network/key operation, package installation, cleanup, feature enablement or subagent was performed. Task-local logs, this report, diagnostic provenance and hash manifest are retained under `.superpowers/sdd/tasks`.

## Result and integration

The existing quiet green workbench and native controls remain the visual direction. The existing form contains an independent optional itinerary section: explicit Save of empty basis → explicit proposal request → ephemeral preview → explicit transfer → ordinary editable M1 blocks → explicit Save. Disabled/checking/unavailable M2 does not disable manual form controls or Save. Existing saved blocks and dirty/unsaved foundations cannot generate; the UI explains the empty foundation requirement without clearing data.

The component calls the reviewed `TripsItineraryStateService` directly. `adoptItinerary()` calls its actual `adopt()`, which owns both guarded owner detail and availability GETs, their combined ticket and final staging. No second candidate/adoption/CAS machine or baseline advancement was added. M2 pending disables its own actions, while existing M1 fields and Save remain usable. Title/brief, proposed UUIDs, null times/estimates and ordinary historical note survive staging; no structured facts enter the M1 PUT. The preview fact panel disappears after transfer, Save and reload. User-edited note text is never parsed as trusted provenance.

Each route entry captures a component route epoch and identity epoch before awaiting M1 `enter()`. A late old entry cannot activate M2 over a newer route or owner context. The actual A→B→A late-read component test preserves the newer preview and confirms no duplicate availability request. Activation otherwise uses the reviewed idempotent state method. Destroy leaves both state services; login/retry/new-plan paths also activate the current context explicitly.

Preview distinguishes filled/partial/empty/unsupported, generation/checking-adoption/busy/deadline/stale/expired/conflict/uncertain, and independent availability states. It renders original titles, translated interest tags, dwell recommendations, date-only day order, uncovered interests, exclusions, unknown hours/cost/arrival/transfer facts, approximately rounded map distance and exact map-duration interval. Walking is labelled modelled and without field verification. The 75/45 m/min plus mapped-crossing scenario, original assumptions, not-evaluated human quality/calibration and null human curator/reviewer are disclosed without calibrated-confidence or feasibility claims.

Source details expose exact retrieval/software-processing meaning and source URL, CC0 licence, OSM/ODbL credit and original source/rights text as plaintext. External links are HTTPS with `rel="noopener noreferrer"`; there are no embeds, images, innerHTML, prefetches or automatic external requests. Only the reviewed fixed manifest and five existing child paths are offered for download. The general OSM/ODbL notice and manifest link live outside the ephemeral fact panel, persist after transfer/reload and describe the common proposal source rather than assigning provenance to edited blocks; credit therefore does not depend on the editable historical note.

## Tests and raw evidence

The suite uses the actual component, M1/M2 state, M2 HTTP client and strict decoder. M1 calls use a bounded API double; the owner-timeout case instantiates the actual M1 HTTP client. Actual canonical ready/candidate/partial/empty/unsupported fixture JSON is flushed as realm-correct ArrayBuffer bytes. Hostile title and rights-description text is mutated only inside a fictional fixture; unsafe source URL is rejected by the actual decoder. No fixture performs network requests.

Meaningful RED precedes production changes:

- `task-4.3-red.log`: 22/22 failed because the component did not activate M2; no availability request existed. An earlier test fixture typing error was corrected before this semantic RED and is not claimed as behavioral evidence.
- `task-4.3-green-initial.log`: initial integration run exposed a test-only cross-realm ArrayBuffer construction issue plus the obsolete M1 heading assertion. Both were corrected without relaxing the decoder or default-off behavior.
- `task-4.3-green-2.log` and `task-4.3-green-3.log`: the one remaining failure in each was respectively an incomplete fake M1 Save response and an incorrect textContent assertion for an input value. Corrected fixtures/assertion, no unrelated product workaround.
- `task-4.3-green-4.log`: 37/37 passed before additional independent availability and disclosure/focus checks.
- `task-4.3-red-disclosure-focus.log`: 40/42 passed; the two actual failures were absent original assumptions disclosure and absent focus recovery after an explicit availability recheck error. Component fixes added disclosure and guarded focus recovery without changing state.
- `task-4.3-red-license.log`: 41/42 passed; the missing direct CC0 licence link failed. The same integration scenario also requires independent post-transfer/reload OSM/ODbL credit.
- `task-4.3-green-final.log`: FINAL exact-source focused GREEN, 42/42 tests, 2 files, 0 failed/0 skipped, exit 0, started 2026-10-21 15:56:36 Europe/Moscow. Includes 33 new component integration cases and 9 retained M1 cases.
- `task-4.3-web-frozen.log`: FINAL exact-source complete web GREEN, 731/731 tests, 54 files, 0 failed/0 skipped, exit 0, started 2026-10-21 15:56:51 Europe/Moscow, Nx 16.3 s. Baseline 698 + 33 new component integration cases. `task-4.3-web-full.log` retains an earlier successful complete run.

Commands:

```text
node node_modules/nx/dist/bin/nx.js test web --include=**/trips-page*.component.spec.ts --watch=false --skipNxCache
node node_modules/nx/dist/bin/nx.js test web --watch=false --skipNxCache
node node_modules/@biomejs/biome/bin/biome check apps/web/src/app/trips/trips-page.component.ts apps/web/src/app/trips/trips-page.component.html apps/web/src/app/trips/trips-page.component.scss apps/web/src/app/trips/trips-page.component.spec.ts apps/web/src/app/trips/trips-page-itinerary.component.spec.ts --reporter=json --max-diagnostics=200
```

Positive integration covers explicit generation/source/coverage rendering, both real adoption reads and double-click blocking, unchanged title/brief, stable UUID, note scalar limit, null time/money, no PUT before Save, ordinary edit/Save and reload. Negative coverage includes nonempty/unsaved/form-only dirty/revert, manual editing during generation, late response with no focus theft, package switch, unknown policy, flag withdrawal, read failure, local edit/logout/plan switch between replies, changed revision/release expiry, client timeouts of either GET, actual unsafe URL decoder failure, busy/deadline/unavailable distinctions, default-off and independent 401/403/404/503, ordinary M1 412 and lost-PUT barriers, later local form changes during staged Save, native dismissal focus and late route completion.

## Lint, resources and limitations

`task-4.3-biome-current.json`: exit 0, 0 errors, 3 warnings and 10 infos, 0 new diagnostics. `task-4.3-biome-provenance.json` binds all 13 retained diagnostics to exact unchanged HEAD M1 source lines and baseline SHA256. These are 3 pre-existing non-null-assertion warnings and 10 pre-existing string-template infos. Introduced anchor/ARIA/template diagnostics were fixed; no suppressions, unsafe fixes, rule or configuration changes. Biome supports the four TS/HTML inputs; SCSS was explicitly passed but is unsupported by Biome and was compiled by the Angular test builds. Owned `git diff --check` passes.

Per-process environment: `TEMP`/`TMP=D:/_Projects/_github/travel-agency/TestResults/trips-m2-dependencies-e654c769/tmp`; `NX_SOCKET_DIR=D:/_Projects/_github/travel-agency/TestResults/m2-ipc-01a15a`; `NX_CACHE_DIRECTORY=D:/_Projects/_github/travel-agency/TestResults/trips-m2-dependencies-e654c769/nx-cache`; `NX_DAEMON=false`; `NX_NO_CLOUD=true`. Existing owned paths were reused. C headroom was checked before heavy runs; final recorded free space is 2596159488 bytes (about 2.42 GiB), above the 1.5 GiB stop threshold. No global environment changes, cache cleaning, plugin disabling, timeout/coverage/budget changes. Existing NO_COLOR/FORCE_COLOR warnings remain visible in logs.

These are source/unit integration results, not browser/Host/target acceptance. Actual keyboard traversal, responsive appearance and visual focus contrast remain the parent-owned 4.4 browser gate. The production Angular build/budget gate remains parent 5.1 as instructed. No real-source quality, human route quality, calibration, target enablement or publication claim is made.

## Proposed README / focus and status guidance for parent

Add a concise M2 paragraph describing the explicit empty-basis Save → proposal → preview → transfer → optional edits → explicit Save flow; source/quality unknowns; M2 default-off independence; and ephemeral facts versus editable historical intentions. Document both actual adoption reads as owned by the state service, not by the component. The existing README remains parent-owned and was not edited.

Focus: accepted new preview → `itinerary-preview-title`; successful local transfer → first ordinary block title; explicit request/recheck error → `itinerary-status` only while the captured form/context is still current; dismiss → generation button. Existing M1 validation focus and reconciliation remain unchanged. Native buttons, headings, ordered day/stop lists, details/summary and safe anchors preserve keyboard semantics. Status uses a polite live region and is not hidden behind icons; M2 pending never globally disables the editor or traps focus. Actual browser proof belongs to 4.4.

Parent should independently review the frozen five-file source set and this receipt before marking task 4.3 complete. `.superpowers/sdd/tasks/task-4.3-hashes.json` records exact owned sources, reviewed inputs, raw receipts and this report. No further product edits are planned before that review.
