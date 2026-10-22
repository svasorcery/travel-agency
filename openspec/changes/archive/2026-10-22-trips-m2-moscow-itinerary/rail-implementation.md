# Tasks 4.5 + 4.6 implementation report

Status: **DONE_WITH_CONCERNS — source ready for parent personal review and fresh independent task review; no task acceptance claimed.** Date: 2026-10-21.

The reusable Rail search and fictional Rail-to-Trips intention consumer are implemented. Final runtime checks: focused **118/118, 6 files**, full web **815/815, 57 files**, existing isolated Rail decoder **16/16, 1 file**; all0failed/0skipped. Final scoped Biome exits0 with96 non-null assertion warnings and13 template-literal infos; detailed provenance is below. One unused test import was subsequently removed at the parent's explicit request; final no-emit TypeScript compile and scoped Biome passed. This sole post-suite edit changes no executable statement/assertion;815 runtime tests were intentionally not rerun for it.

## Basis, ownership and unchanged work

Worktree: `C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency`. Branch remains `codex/trips-m2-moscow-itinerary`; HEAD remains `a8721effd9056459d217966083816455fbea599d`. `git merge-base --is-ancestor e654c76915103a82083ef2abc01c0579117c9749 a8721effd9056459d217966083816455fbea599d` returned0. No checkout/branch/git mutation occurred.

Read the exact worker brief, final independent Rail plan PASS, `rail-refinement.md`, the full `trip-rail-intentions` delta spec, current task4.5/4.6 text at42/43 and refreshed `rail-apply-after-1.5.json`18/27 instructions. Existing task4.2 public interface was used, rather than reimplementing walking. Applied TDD, frontend-design, test-authoring and verification guidance. Canonical task4.5/4.6 checkboxes remain unchecked; controller acceptance, personal review and fresh review are separate.

The20 product/test/docs paths below are the entire worker source delta against the supplied pre-Rail baseline (12existing,8new). All12 before hashes also matched `worker-before-hashes.json` captured before edits. The13 other baseline files remain byte-identical, including the existing walking state, its tests, itinerary API tests and existing Trips page tests. Existing WIP elsewhere was preserved. No walking client/backend/Core/Data/Host/root/package/config/browser/canonical planning/quality input files were changed. The reports/manifest and owned D evidence are additional outputs.

Exact before/after hashes and all raw log hashes are in `task-4.5-4.6-hashes.json`. Full2087-line review patch: `D:/_Projects/_github/travel-agency/TestResults/trips-m2-rail-implementation/worker-source-patch.diff`; its normalized final-newline diff is for reading, while the manifest hashes identify exact source bytes. Prior pre-self-review patch/manifest are retained separately. No prior735-test receipt was relabelled.

| Changed path | Before SHA256 | Final SHA256 |
|---|---|---|
| `apps/web/src/app/rail/rail-page.component.ts` | `ffa5ccaab1c25b2e66d4c6e9cf24a1e701b03119f9d595af92339542327115cf` | `9a3c4958a56d10eca8435f85ace5a71acf0bc8243c5a841e7612f76bd0f53cb4` |
| `apps/web/src/app/rail/rail-page.component.html` | `f08df6e6339e4adbec2586210488077b1101a2ccb839110ff1524079125e80a4` | `32c0eebacfddf3243f563dea45210bd08687ce88cf86956d2fb7f2d7ccf7997c` |
| `apps/web/src/app/rail/rail-page.component.scss` | `4d55b63cdb4609581d2c50ab700d80405d17a53f81c5ed49073e21007126f378` | `1de7354a8bd9ff431786c0aa3f723f4a20d476c6fc7e74529c5583b43d7b7bdf` |
| `apps/web/src/app/rail/rail-page.component.spec.ts` | `e81fdde11391ca99dd80df9cc1fbc93e1590f96689d8824fc4ecde98f08d5387` | `4b2cf92466a49107ac7565077acb549ec684804bf90a4b9e771cc08e3b365c58` |
| `apps/web/src/app/rail/rail-search.component.ts` | `NEW` | `66572ba8b196cdf5b283d3428d975532db819bc0fa0608fa9bbb54786ce2fc53` |
| `apps/web/src/app/rail/rail-search.component.html` | `NEW` | `d2b79d94b3204b83f9db140b2a886214187e8d17b9e1746425693c2cfdac5e6c` |
| `apps/web/src/app/rail/rail-search.component.scss` | `NEW` | `f625892968b5d9ab0c8058015fef1d141f23cdc3fc62d22ae0b5e75cbbfb4d20` |
| `apps/web/src/app/rail/rail-search.component.spec.ts` | `NEW` | `5fa5049486d387bc7915cb99027358c5c1a532ce03db819ae59534996470f12c` |
| `apps/web/src/app/rail/rail-state.ts` | `795c37201bd86ba4464d20d3ae379575f7e139774993d8b3225717cb1a2bb033` | `36293639fb3c861c9044d887ffeb54bba0a3e8701ac7d41e1328006600234110` |
| `apps/web/src/app/rail/rail-state.spec.ts` | `13842861841f9c6b8fe6430214165c6dc9918e825ebc54c26e4e7476428803cd` | `764ef2499d525819fc7a1d1392fff73f1ba3f4356e8519cb30fd4e76e5c88493` |
| `apps/web/src/app/rail/README.md` | `NEW` | `9c1bd191c5cd937e3ab5f547b1bd68680608750011a248f1457a7eccad15efe5` |
| `apps/web/src/app/trips/trips-state.service.ts` | `d296cbc5a580866eb69eff5a119b8d37b25841f8a5b4e6f5d0cf3f5f99387e4f` | `1bfb20b948d498aafbfc196748cf0db39789902b0a56387f92a4cf6a2dc30dbf` |
| `apps/web/src/app/trips/trips-state.service.spec.ts` | `27375a92ead232d79e7c4fc312844681a37784cd19c1353081840612bd21b195` | `e288f64c70800a2e8cb49635b8c98549ecd7d81413153f07cbc6cdfea276959c` |
| `apps/web/src/app/trips/trips-page.component.ts` | `2d2609549fb9c3d2a8a6d43079d1c397b82196f8ac3dcb5b0fcb7917a9162d2d` | `71f50541a0916b36c6f161b3e3cd7e8e2d24340feaf36b7d4446b9a22d66490e` |
| `apps/web/src/app/trips/trips-page.component.html` | `0ef73014f38330aef93596423af1c75287fe5c1af0e0947c6ea8bb0407b2d27f` | `66c0ef375b2279268e9386fcfd022201f5b8475bdc0a34f40a381e4fb05b2a07` |
| `apps/web/src/app/trips/trips-page.component.scss` | `b19bd38046fffbf3311cf2ff29eba4aff6933a083e089fcba4de24fe8338213c` | `ee0810cde112c757643709725e811cb92e45e26db7a9de7ea9a7753bd7418d51` |
| `apps/web/src/app/trips/trips-page-rail.component.spec.ts` | `NEW` | `0319885cc7a936230e171d834f853d30779bbed86f34b9940411dc7d0b69975e` |
| `apps/web/src/app/trips/trips-rail-projection.ts` | `NEW` | `ae95eab97256a7da4b6e0553365a4011216e9066926201371d9377f94a3c2b25` |
| `apps/web/src/app/trips/trips-rail-projection.spec.ts` | `NEW` | `549cde29d8300b06919e8392faf868fa7d220dc59d7935768892ec1fad014b71` |
| `apps/web/src/app/trips/README.md` | `6b531a2e533ff693c4f85b13e3648a6db69e623a3db7efb2ec0556ca96d8d607` | `19b439428e7c0ac8a4b04ccabc95c84f9c9418f13605293560b8277efc78a0a2` |

## Retained interfaces and consumer behavior

`RailPageComponent` retains the standalone main/H1/header and embeds `RailSearchComponent`. The extracted standalone component has one existing RailState and existing RailApiService/strict Rail DTO/decoder. It receives no Trips fields. Its semantic section owns browser transport, timers, abort/dispose and the existing exact-allowlisted240x130 iframe/CSP listener. Local ElementRef focus replaces the old document-global station query. Standalone has no Trips handler, because `selectable` defaults false.

```ts
// RailSearchComponent
readonly selectable = input(false);
readonly journeySelected = output<RailSelection>();
choose(journey: RailJourney): void;
// RailState
captureSelection(journey: RailJourney): RailSelection | null;
interface RailSelection {
  readonly journey: RailJourney;
  readonly expiresAt: string;
  readonly isCurrent: () => boolean;
}
```

A capture is a frozen short-lived context with the exact current row identity. `isCurrent()` is bound to the admitted result receipt and rechecks current result identity/member, sequence/authority, unchanged result/reference snapshots, current source/from/to/date/mode/catalogue, fictional profile/profileEpoch, intact row and attribution, non-null original finite absolute freshness and the decoder's1s future tolerance. The existing strict decoders are reused at click; no duplicate parser/algorithm/client exists. Selected mode reception is also checked explicitly against its row and original expiry. Result expiry may not exceed current reference expiry. A selected mode may be complete, or limited only by nonempty page-limit/interval-service-unsupported reasons. Unknown reasons, damaged selected tickets, failed/skipped selected mode, missing/failed latest reference/banner, cloned/missing row and A-B-A replay are refused. An intact row can remain eligible when another mode fails or another row is damaged, preserving the envelope's shorter original expiry and visible warning. Real and unknown profiles never obtain fictional persistence privileges.

Failed/skipped current station replies now clear retained same-profile source authority. Current station read exceptions also clear it, including a concurrent second-field exception after a first-field success; known old other-profile replies still follow the existing epoch refusal. Criteria/profile/attribution/dispose invalidation drops the admitted result receipt. Timers clean up, while click-time checks remain decisive.

```ts
// One existing WeakMap/ticket and monotonic foundationVersion, generalized
interface TripsSavedFoundation { readonly plan: TripPlan; /* existing opaque brand */ }
type TripsEmptyFoundation = TripsSavedFoundation;
canCaptureSavedFoundation(): boolean;
captureSavedFoundation(): TripsSavedFoundation | null;
isSavedFoundationCurrent(foundation: TripsSavedFoundation): boolean;
appendSavedFoundation(
  foundation: TripsSavedFoundation, date: string, block: TripBlock,
  sourceCurrent: () => boolean = () => true,
): boolean;
// Existing walking signatures retained
captureEmptyFoundation(): TripsEmptyFoundation | null;
isEmptyFoundationCurrent(foundation: TripsEmptyFoundation): boolean;
readEmptyFoundation(foundation: TripsEmptyFoundation, cancelled: Observable<void>): Promise<TripPlan | null>;
stageEmptyFoundation(foundation: TripsEmptyFoundation, days: TripDay[]): boolean;
```

The common clean-saved predicate requires the current owner/session/saved plan, clean buffer/form, no pending mutation, detail/list read, missing plan or conflict/uncertainty barrier. New pending-read signals contain the existing detail/list epoch values; older finally callbacks cannot clear newer reads. Reads invalidate existing foundationVersion, and navigation detaches obsolete pending values. Nonempty Rail capture shares the same WeakMap/version/session/plan/read/edit authority. Walking remains empty-only; its guarded owner GET + availability GET contract and implementation/tests are unchanged.

`appendSavedFoundation` locates the explicit saved target day, refuses8existing blocks, builds the entire prospective ordinary ReplaceTrip preserving all other saved values, calls actual `tripWrite`/`decodeTripWrite` for scalar/duplicate-ID/money/time/full128KiB UTF8 invariants, then invokes `sourceCurrent()` and rechecks the same foundation immediately before one synchronous existing edit. There is no await, placeholder block, automatic PUT, new owner GET, CAS machine, retry or rebase. Success invalidates the foundation. Existing explicit Save/CAS/lost-PUT behavior remains authoritative.

Trips renders the picker after the existing M1 form. Target day is explicitly selected from the captured saved dates, independently of Rail departure date; changing it changes no M1 field or dirty flag. The single rendered picker is keyed by that same opaque foundation identity. Its output calls `chooseRail(selection, originalFoundation)`, which refuses a context from any earlier opening even before old-child teardown. Open focuses Rail origin, close returns to the open control, successful stage focuses the new editable block. Context invalidation/late replies do not steal focus; existing walking focus-intent guards are preserved.

The only retained projection helper is `projectRailIntention(selection): TripBlock`, consumed by TripsPageComponent. It checks current source authority, creates one UUID, exact six-line fictional/historical note from original source/profile/transport/thread/station/offset/reception/expiry strings, and null start/end/estimate. A title contains noun+exact optional number+demo only if the complete title passes actual M1 rules; missing/overlong/control/unpaired-surrogate optional numbers use the fixed noun demo title with no number normalization/truncation. Essential note overflow/control incompatibility rejects the whole operation. Fare strings never enter budget/estimate. After explicit Save/reload there is only ordinary editable M1 text, no source-note parser, restored trusted badge/panel, pending choice or browser retention.

## Commands and receipts

All commands ran from the exact worktree. Heavy web commands were preceded by C free-space checks; no run started below1.5GiB. The transient earlier1.69GiB observation was reported to the parent; later checks were about2.74GiB without cleanup. Final Node is `v26.11.1`, not CI22. Root dependencies are the parent's physical current a872 copy; this worker did not reinstall or infer CI/security acceptance.

Per-process web environment:

```powershell
$env:NX_DAEMON='false'
$env:NX_NO_CLOUD='true'
$env:NX_CACHE_DIRECTORY='D:/_Projects/_github/travel-agency/TestResults/trips-m2-dependencies-a8721eff/nx-cache'
$env:TEMP='D:/_Projects/_github/travel-agency/TestResults/trips-m2-dependencies-a8721eff/tmp'
$env:TMP=$env:TEMP
$env:NX_SOCKET_DIR='D:/_Projects/_github/travel-agency/TestResults/m2-ipc-01a15a'
```

Actual test command families (repeated --include is supported):

```text
A: node node_modules/nx/dist/bin/nx.js test web --include=**/rail-state.spec.ts --include=**/trips-state.service.spec.ts --watch=false --skipNxCache
B: node node_modules/nx/dist/bin/nx.js test web --include=**/rail*.spec.ts --include=**/trips-state.service.spec.ts --include=**/trips-page-rail.component.spec.ts --watch=false --skipNxCache
C: node node_modules/nx/dist/bin/nx.js test web --include=**/rail*.spec.ts --include=**/trips-state.service.spec.ts --include=**/trips-page-rail.component.spec.ts --include=**/trips-rail-projection.spec.ts --watch=false --skipNxCache
D: node node_modules/nx/dist/bin/nx.js test web --watch=false --skipNxCache
E: node node_modules/vitest/vitest.mjs run --config shared/ts/api-client/vite.config.mts src/rail.decoder.spec.ts
F: node node_modules/nx/dist/bin/nx.js test web --include=**/trips-page-rail.component.spec.ts --watch=false --skipNxCache
```

Each log below is under `D:/_Projects/_github/travel-agency/TestResults/trips-m2-rail-implementation/`; manifest contains its full SHA256. Runtime test counts are total counters, not estimates. No final skips.

| Log | Command | Actual outcome |
|---|---|---|
| focused-red.log | A | compile RED: new APIs absent |
| focused-red-runtime.log | A | compile RED retained; initial Python source-read encoding error prevented stub write |
| focused-red-assertions.log | A | compile RED retained; initial byte-marker lookup also prevented stub write |
| focused-red-stubs.log | A | meaningful assertion RED:13failed/42passed55tests |
| extraction-red-state-green.log | B |3failed/58passed61tests;55state tests passed, missing reusable child/Trips opening failed |
| focused-green-first.log | B |1failed/60passed61tests; direct child-state test needed child ChangeDetectorRef mark after extraction |
| projection-final-guard-red.log | C |2failed/86passed88tests: final post-codec source callback RED and an inconsistent damaged-ticket fixture rejected by actual strict decoder |
| component-integration-first.log | C |105/105,5files |
| concurrent-reference-red.log | C |1failed/116passed117tests; actual concurrent unreadable-reference revocation gap |
| focused-final.log | C |1failed/116passed117tests; empty fixture Date.now drift could produce30,001ms instead of strict30s |
| focused-final-pass.log | C |117/117,6files after fixture bound to original receivedAt |
| web-full-final.log | D |814/814,57files, prior self-review snapshot |
| rail-decoder-final.log | E |16/16,1file; only existing Rail decoder file selected |
| reopen-foundation-red.log | F |1failed/22passed23tests; actual stale output across close/reopen before teardown |
| focused-reopen-final.log | C |118/118,6files,0failed/0skipped |
| web-full-after-self-review.log | D |815/815,57files,0failed/0skipped |

The two early Python issues were local encoding/newline handling, fixed using UTF8 and original bytes; no HEAD reset or source restoration occurred. Original before hashes and the complete final UTF8 patch were checked. The extracted-child test adaptation preserved every exact fare/sandbox/CSP assertion. The damaged mixed-row fixture was made structurally coherent (unknown electronic indicator with invalid-indicator reason); production decoder was not weakened. Empty outcome fixture expiry now derives from the original receivedAt rather than a later wall-clock call. Original failures remain in the log inventory.

The worker read the complete coupled2064-line first frozen patch, then the exact updated three-file ticket-binding fix and final patch source. Self-review found/reproduced the close/reopen edge with a real old output event: one extra block was appended before the fix. Keying the sole picker/output by the original common ticket now refuses that old event and remounts empty source state. That genuine new source change justified the second full web run; no gratuitous repeat followed the unused import cleanup.

The full web suite includes the existing walking adoption/two-GET/race/CAS/lost-answer regressions. New focused cases cover current strict Rail client/source membership; complete-empty/limited-empty/skipped/failed distinctions; safe/unknown limiting reasons; selected damaged vs intact mixed rows; absolute/null/future/faulted reference; exact large money; all ordinary old values; 7→8/ninth-block;100/500Unicode scalar limits and title fallback; a true near128KiB multibyte full-write boundary using actual M1 codec; zero half-edits/hidden writes; sibling DOM forms; search submit and station Enter effects; target-day cleanliness; real view-only; iframe/CSP failure; read/list/owner/logout/edit invalidation; close/reopen/stale callback, abort/dispose and focus. Browser-native implicit Enter behavior and browser lifecycle/layout remain4.7's separate proof.

## Final formatting/compile and warnings

Only owned files were passed to installed Biome. `node node_modules/@biomejs/biome/bin/biome format --write <owned paths>` and safe `check --write --linter-enabled=false <owned paths>` applied formatting/import ordering. No unsafe fixes, suppression, policy/root config changes or broad format occurred. Initial scoped errors were import ordering/three newly written iterable callbacks; callbacks were fixed and the multibyte fixture search bounded. Final `node node_modules/@biomejs/biome/bin/biome check --max-diagnostics=200 <owned paths>` exits0, with96`lint/style/noNonNullAssertion` warnings and13`lint/style/useTemplate` infos. Unsupported/ignored SCSS remains existing style; the formatter processed12TS files, final check15supported owned files. Raw logs retain every diagnostic.

At the parent's final request, removed only unused `Subject` from the new Rail page test's existing rxjs import. `node node_modules/typescript/bin/tsc --noEmit -p apps/web/tsconfig.spec.json` exits0 with empty diagnostics in `typescript-freeze.log`; final Biome is `biome-freeze.log`. No runtime code, template, assertion or test behavior changed after815passed. The manifest stores both the runtime-tested and final source hash for this sole file, so exact evidence is not misrepresented.

`D:/_Projects/_github/travel-agency/TestResults/trips-m2-rail-implementation/biome-diagnostics.json` gives every final path/line/column/rule and baseline-line origin. Among the96non-null warnings:45are on exact unchanged lines from the supplied baseline,1is on the unchanged RailPage transport line moved to RailSearch,50are on new/changed lines. This is a line-origin classification, **not** an executed comparable baseline lint delta; no zero-new or baseline-clean assertion is made. New test paths naturally contain new diagnostics. Final non-null warning distribution:

| Path | Count |
|---|---:|
| `apps/web/src/app/rail/rail-search.component.spec.ts` | 6 |
| `apps/web/src/app/rail/rail-search.component.ts` | 1 |
| `apps/web/src/app/rail/rail-state.spec.ts` | 12 |
| `apps/web/src/app/rail/rail-state.ts` | 9 |
| `apps/web/src/app/trips/trips-page-rail.component.spec.ts` | 7 |
| `apps/web/src/app/trips/trips-page.component.ts` | 4 |
| `apps/web/src/app/trips/trips-rail-projection.spec.ts` | 1 |
| `apps/web/src/app/trips/trips-state.service.spec.ts` | 45 |
| `apps/web/src/app/trips/trips-state.service.ts` | 11 |

## Limits and next gate

No browser/devserver, production build, .NET/Integration suite, Q/S generator, frozen quality test, Host/AppHost/DB/key/schema/migration, model/provider/paid API/enablement/package installation/network audit, cleanup, stage/commit/push/merge or external mutation was performed. The existing strict Rail decoder filter was the only api-client test run; no unfiltered api-client run was used. Source/API/frontend tests run on fictional fixtures do not prove real-source/banner/rights/quota, target, human route quality, CI or rollout acceptance.

Nonblocking source concerns: retained non-null style warnings described above, and deliberately bounded projection (some valid long source identities cannot fit500scalars and are refused). Parent must personally read the entire final coupled patch and dispatch a fresh independent reviewer/recheck before4.7. Browser4.7 and production/source/dependency cross-check4.8 remain later tasks. No final source/business gate has been marked complete by this worker.


## Parent-confirmed P2 correction — current authoritative source receipt

Date: 2026-10-21. This appended receipt supersedes the initial source/verification totals above. Initial118/815/16 receipts, complete source/manifest/report and2087-line patch are retained unchanged under `D:/_Projects/_github/travel-agency/TestResults/trips-m2-rail-implementation/p2-render-cost/` (before-P2 snapshots), with their hashes in the updated manifest. No historical result is relabelled.

Parent personal full-patch review confirmed one P2: `selectionReason()` called `captureSelection()` twice per row, which serialized result/reference and ran whole strict decoders on each display access. At the published100-row limit this created quadratic rendering work. No other parent P1/P2 was reported.

The correction changes exactly three owned Rail files, listed below. No Trips/backend/client/canonical/browser/Q/config/dependency files changed in this follow-up. The20-file coupled source manifest is updated and all17other implementation files and13outside-owner baseline files were verified unchanged. The complete current coupled review patch is `D:/_Projects/_github/travel-agency/TestResults/trips-m2-rail-implementation/worker-source-patch.diff`; the isolated parent-fix patch is `D:/_Projects/_github/travel-agency/TestResults/trips-m2-rail-implementation/p2-render-cost/fix.diff`,283lines, SHA256 `731c545790648f1037b050b9da4336808ab06689f763b12ff97ef77d5d8e62ff`.

| P2 changed path | Before P2 SHA256 | Final SHA256 |
|---|---|---|
| `apps/web/src/app/rail/rail-search.component.spec.ts` | `5fa5049486d387bc7915cb99027358c5c1a532ce03db819ae59534996470f12c` | `e7fdaf567775491ab5cdb80c6ed2d26fb59fb13d0b6d7da3da7fbc7674d3de28` |
| `apps/web/src/app/rail/rail-search.component.ts` | `66572ba8b196cdf5b283d3428d975532db819bc0fa0608fa9bbb54786ce2fc53` | `c693d193eb2094cf68df0064a93772aa34d9b797541829d431d686df87d93490` |
| `apps/web/src/app/rail/rail-state.ts` | `36293639fb3c861c9044d887ffeb54bba0a3e8701ac7d41e1328006600234110` | `3a08270fbec4bc6e3e52e67474b7b37bcffb3215b7388f12c289371bd8192693` |

`RailState.selectionAvailable(journey): boolean` is now the retained **display hint** consumer for `RailSearchComponent.selectionReason()`. The admitted result records the exact current reference object and a one-time Set of row identities. Display uses constant-time Set membership and bounded profile/epoch/authority/request/source/ref/absolute-freshness/attribution/selected-mode/ticket fields. It does not scan all rows, serialize result/reference or invoke a strict whole-envelope decoder. Modes/reasons retain the existing strict limits (two modes, at most64reasons), so per-row display work does not grow with the number of rows. Admission builds the Set once in O(N); rendering is O(N), without another client/parser/CAS/epoch framework.

`captureSelection()` and `RailSelection.isCurrent()` still use the full private `eligible()` action path. That path first checks current bounded conditions, then exact current result membership, original whole result/reference fingerprints and existing strict `decodeRailSuggestions`/`decodeRailSearch`. The parent final synchronous append callback therefore retains strict source authority. Display availability is deliberately not an adoption ticket: in-memory snapshot corruption may still require the full click check to detect it. Real retention refusal, safe limited reasons, null/expired/future TTL, changed profile/epoch/catalogue/request, damaged tickets and missing/unsafe attribution retain their display refusal; CSP/frame failure still clears state. Existing final append and clean-saved foundation code was untouched.

The new100-row regression goes through real RailApiService and strict decoder admission:50train+50suburban,100unique identities, valid mode counts. It instruments JSON.stringify only after source admission and counts exact result/reference inputs, including decoder bound checks. Initial render plus three unrelated change-detection passes produced **6400** whole-envelope serializations in RED. After the fix, identical rendering produces **0**, all100choices render enabled, and an actual choose produces a positive strict serialization count and one event. A mutated admitted snapshot and click after original absolute expiry emit no further event. There is no timing threshold. Ten additional negative cases verify bounded display plus actual-click refusal for expired/nullTTL/profile/epoch/generation/damaged/attribution/unsafe-frame/unknown-limit/criteria changes.

Commands use the previously documented process-local NX/TEMP/TMP environment and C free-space stop checks. C free remained approximately2.73GiB; no cleanup occurred.

```text
RED: node node_modules/nx/dist/bin/nx.js test web --include=**/rail-search.component.spec.ts --watch=false --skipNxCache
FOCUSED: node node_modules/nx/dist/bin/nx.js test web --include=**/rail*.spec.ts --include=**/trips-state.service.spec.ts --include=**/trips-page-rail.component.spec.ts --include=**/trips-rail-projection.spec.ts --watch=false --skipNxCache
FULL: node node_modules/nx/dist/bin/nx.js test web --watch=false --skipNxCache
```

Actual receipts under `D:/_Projects/_github/travel-agency/TestResults/trips-m2-rail-implementation/p2-render-cost/`:

- `red.log`:1failed/8passed9tests; expected0 versus actual6400 whole-envelope serializations.
- `focused-first.log`:119/119,6files after first source correction.
- `focused-final.log`:**129/129**,6files,0failed/0skipped after the10negative display cases.
- `web-full-final.log`:**826/826**,57files,0failed/0skipped. One full web run in this P2 follow-up, after final product/test edits; no product source changes followed.
- `format.log`/`format-final.log`: safe format/import-only writes scoped to the three correction files.
- `biome-final.log`: scoped supported owned-file check exits0,0errors,**121non-null warnings,14template infos**. Initial comparable15-file scope had96non-null warnings/13infos. The P2 delta adds25warnings (19in the new regression assertions and6in the factored display/action guards) and1info; no suppression or existing-warning cleanup was used. Exact current path/line/column and before-P2 line-origin inventory is `D:/_Projects/_github/travel-agency/TestResults/trips-m2-rail-implementation/p2-render-cost/biome-diagnostics.json`. These are nonblocking style diagnostics, not an invented zero-new assertion.

| P2 raw receipt | SHA256 |
|---|---|
| `biome-final.log` | `15b76fb8d041a751a21c4816ff65b3fd8733e2ab0e522748327417f8dbda8a39` |
| `focused-final.log` | `878ee54e0ec9516fcecd45daf0fa717aa07eb5c2c6aa41ee50b04038f69c5bc7` |
| `focused-first.log` | `e42a2d89a89c1539dc8b28e4e3872efd26d9cd9ba1ed21901cc7163b6599f2f6` |
| `format-final.log` | `2f82ffe6f6df19a77eb34cbb6165758b394bfb1c7875adc66bf6d9525b2b510a` |
| `format.log` | `302c3d97c627b7bdc2b7b8aa30c5c711419b27d3af68cdd290a54367fa6c9e16` |
| `red.log` | `1a73ded8db0d6b041f05efa300f62bab60d8ee80e7ef7cc49460e2bc3b69b09c` |
| `web-full-final.log` | `87c02efae296af85505ab1ce6f852d935b6043861ae2a009f42a821c7bbd8351` |

Worker read the entire283-line correction diff. Full-envelope integrity remains an action-only operation; source expiration, stale selection and final Trips append tests pass in the focused/full suites. Parent must personally read this exact correction diff, then dispatch the prepared fresh independent full coupled review. Canonical tasks remain unchecked. No browser/devserver/Q/Host/DB/provider/model/audit/commit/push or other authority was used.


## Independent review R1/R2 corrections — current authoritative receipt

Date: 2026-10-21. This appended receipt supersedes the latest source/verification counters above. Exact independent review `task-4.5-4.6-review.md`, SHA256 `03dfa0c11486dd3809713621e7e430b75ed4c85a52e270cb9383af7ad6b8d4b0`, was read in full; its verbatim R1 and R2 findings are retained there unchanged. Review verdict was CHANGES REQUIRED with0P1/2P2/0P3. No source acceptance or reviewer recheck is claimed here.

Before edits, the current20-file source manifest, report,2232-line full patch and all five touched source/test files were copied under `D:/_Projects/_github/travel-agency/TestResults/trips-m2-rail-implementation/r1-r2-review-fixes/`. Original118/815/16 and later129/826 receipts remain intact. The correction touches exactly the five paths below; all15other implementation paths and13out-of-worker baseline paths were verified unchanged. No existing walking service/client/component test was changed or weakened. Current exact20-file hashes and all raw receipt hashes are in the updated manifest.

The isolated correction is `D:/_Projects/_github/travel-agency/TestResults/trips-m2-rail-implementation/r1-r2-review-fixes/fix.diff`: **481lines**, SHA256 `830ab732c2732334bef829d2dc4dc6a28b07fc6be76ac527423a0f4cd3538b1b`. Worker read all481lines before the final full suite. The complete coupled source patch is updated at `D:/_Projects/_github/travel-agency/TestResults/trips-m2-rail-implementation/worker-source-patch.diff`.

| Review-fix path | Before R1/R2 SHA256 | Final SHA256 |
|---|---|---|
| `apps/web/src/app/rail/rail-state.spec.ts` | `764ef2499d525819fc7a1d1392fff73f1ba3f4356e8519cb30fd4e76e5c88493` | `873ac91f22bd23455478fc0d459fae479cd7bf3bd9f5ea05af94c567ac870e06` |
| `apps/web/src/app/rail/rail-state.ts` | `3a08270fbec4bc6e3e52e67474b7b37bcffb3215b7388f12c289371bd8192693` | `3ee2f2b2e161a365dc75a14ad479a2c0400d8c66c3a80c39e6bdcc8ec28c3603` |
| `apps/web/src/app/trips/trips-page-rail.component.spec.ts` | `0319885cc7a936230e171d834f853d30779bbed86f34b9940411dc7d0b69975e` | `c827d6b5f3d5f0ed1309d4fe3667d5608074f764555e48c11e9e90ef7c476283` |
| `apps/web/src/app/trips/trips-state.service.spec.ts` | `e288f64c70800a2e8cb49635b8c98549ecd7d81413153f07cbc6cdfea276959c` | `dbcc00f623b40e5a4c9ab413548e193c2a420bbaef63f2e9f7a389373867c01c` |
| `apps/web/src/app/trips/trips-state.service.ts` | `1bfb20b948d498aafbfc196748cf0db39789902b0a56387f92a4cf6a2dc30dbf` | `953763028da6566654e881a92d7de91de17ced422a53684a9ef406d2aa4d3b14` |

### R1 — expired state ownership removed

The actual scheduled result-expiry branch now clears both `result` and `admitted`. This releases all state-owned receipt references: complete result, Set members, result JSON and reference JSON. The still-fresh reference, station selections and manual-refresh behavior remain. No TTL is renewed and no request starts automatically.

`captureSelection()` now closes over the existing primitive sequence rather than the whole admission object. A retained old currentness callback therefore cannot itself retain that complete receipt graph; source authority still goes through current `eligible()`/strict decoders and original sequence. The externally passed short-lived selected-row event remains the same public contract; this is logical ownership cleanup, not a claim about GC scheduling or erasing an external caller's retained object.

Regression tests drive the real scheduled timer with5-minute result expiry and30-minute reference expiry, for fictional and synthetic real-profile fixtures. Before expiry they verify the admitted JSON contains the exact large fare. At the result deadline they require visible result=null and internal state admission=null, the original fresh reference/stations unchanged, original selection non-current and no automatic station/search call. An explicit later fresh search works with those stations; the old selection remains non-current. No real supplier was called.

### R2 — same owner-read authority, narrow walking ownership exception

`readEmptyFoundation()` now begins through the **existing** `ticket(true)` readEpoch/foundationVersion machinery and `detailPending`. This invalidates every competing captured foundation before awaiting token/dispatch. Only this read's original walking WeakMap entry is advanced to that existing read/version; its plan, revision, session, edit identity and opaque object remain unchanged. The entry privately records `ownerRead` (the existing read number). No second epoch/CAS store, controller or public ticket field was introduced.

The shared private current/clean predicate has a narrow optional owned-read argument. `isEmptyFoundationCurrent()` obtains it only from that exact WeakMap record, so the walking operation/preview stays current during its **own** owner GET and can complete the unchanged mandatory owner GET + availability GET check. Public `canCaptureSavedFoundation`, `captureSavedFoundation`, `isSavedFoundationCurrent` and `appendSavedFoundation` never supply that exception: they reject every pending owner read, and pre-opened Rail tickets remain stale permanently because their old generation is never advanced. New empty/walking captures are also refused during the read. Duplicate simultaneous reads on the same owning ticket are refused.

Cancellation releases only the matching pending read/owner marker synchronously; `finally` repeats the same idempotent release. It never rolls back readEpoch/foundationVersion or revives old Rail. This synchronous release is required for the accepted dismiss→immediate-generate path; waiting only for Promise finally initially broke its existing regression. Guards on the captured read number prevent an old cancellation/finally from clearing a newer detail read. None of this promotes a returned owner plan into saved/comparison state or adds PUT/GET/retry/CAS behavior. Walking successful completion still requires the original unchanged owner plan and separate package availability, then stages only through its existing path.

New state tests hold a real owner observable pending and verify: dispatched initial+guarded GET count2; Rail open/capture/current/append exclusion; owning walking ticket current; clean editor unchanged; no old Rail revival after success/error/cancel; immediate fresh capture after cancellation; successful ordinary walking stage; and old-owner cleanup cannot clear a newer pending detail refresh.

The new coupled component cases use the **actual** TripsItineraryStateService, TripsStateService, strict itinerary API/fixtures and shared Rail client. The previous isolated Rail setup remains disabled only for its existing tests; the new opted-in test setup uses existing fictional walking ready/candidate bytes and the unchanged test clock seam. With a real walking owner GET unresolved, they assert the common capture/current/append guards and pre-opened `railOpen` become false **before any render or child teardown**, and an emitted prior Rail choice leaves the editor untouched. They also test opening Rail after owner-read start, explicit failure/cancellation without revival, and successful walking completion against the unchanged plan with exactly its original two owner reads (initial load + validation), its independent availability read, correct walking UUIDs and zero PUT. No new production flag or fake walking-state service was added.

### RED/GREEN and current source verification

Commands use the previously recorded worktree and per-process NX/TEMP/TMP settings. C free checks remained approximately2.70GiB; no cleanup or test/runtime authority expansion occurred.

```text
STATE RED: node node_modules/nx/dist/bin/nx.js test web --include=**/rail-state.spec.ts --include=**/trips-state.service.spec.ts --watch=false --skipNxCache
COUPLED RED: node node_modules/nx/dist/bin/nx.js test web --include=**/trips-page-rail.component.spec.ts --watch=false --skipNxCache
FOCUSED: node node_modules/nx/dist/bin/nx.js test web --include=**/rail*.spec.ts --include=**/trips-state.service.spec.ts --include=**/trips-page-rail.component.spec.ts --include=**/trips-rail-projection.spec.ts --include=**/trips-itinerary-state.service.spec.ts --include=**/trips-page-itinerary.component.spec.ts --watch=false --skipNxCache
FULL: node node_modules/nx/dist/bin/nx.js test web --watch=false --skipNxCache
```

- `state-red.log`: **5failed/74passed79tests**. Both profile expiry cases retain admission in RED; all3pending-owner cases incorrectly admit Rail in RED.
- `coupled-red.log`: **4failed/23passed27tests**. Actual walking owner-read start leaves Rail admitted in RED, both pre-opened and absent-picker cases.
- `focused-first.log`: **1failed/249passed250tests**. All new R1/R2 cases pass after the initial implementation; the unchanged existing `prevents an old adoption from overwriting a dismissed and newly generated preview` test exposes Promise-finally-only cancellation release. That failure is preserved; its test was not altered.
- `focused-final.log`: **251/251**,8files,0failed/0skipped after synchronous cancellation release and the additional newer-read cleanup regression. This includes75existing walking-state tests,37existing walking-page tests and the PR1O(N)100-row rendering regression.
- `web-full-final.log`: **836/836**,57files,0failed/0skipped. Exactly one full web run in this correction, after final source/test edits. No product source changed afterward.
- `biome-final.log`:0errors, **132non-null warnings/14template infos**, exit0. Same15supported owned-file scope; +11non-null warnings versus the prior121 receipt, with unchanged infos. Exact current rule/path/line/column and before-review line origin are in `D:/_Projects/_github/travel-agency/TestResults/trips-m2-rail-implementation/r1-r2-review-fixes/biome-diagnostics.json`. No suppression, unsafe fix or broad policy/format change.
- `format-first.log` and `format-cancel-fix.log`: safe format/import-only writes limited to the correction's owned files.

| R1/R2 raw receipt | SHA256 |
|---|---|
| `biome-final.log` | `fcb315b6c1735211c2a4fbc8c0db4a132622513fce4f3aa5ed24df1d8cd8ff05` |
| `coupled-red.log` | `aa16bde3df89534f7f4f6cbcf7a22d63539114f31cd71a29359239f0d7e4c888` |
| `focused-final.log` | `5c45ef9f1402bd9c714ef83fba7bb4941c5a65d8ea7f2adc43eab45f5b15d3f7` |
| `focused-first.log` | `d36530b31dddbde4561f0de0beac9ee5b41e9f9792a9c2b8b728e71a9e9f0454` |
| `format-cancel-fix.log` | `ffd8b1bf4cf9c47e43bfc3f27fa073ee6005c89df70e12f619ec6177360d4440` |
| `format-first.log` | `10433ee7fd35dd4b179028847274e39e9689272bb717e77bb592a897e62925c4` |
| `state-red.log` | `0be71b387d74dc5490b2237003a25c1b1c2b61003c2ace622b997326527bbfab` |
| `web-full-final.log` | `36618bae597ecb1e7344216f1ad66f4f0384a6fd0129bb19334b688ba23d40e4` |

Strict Rail action guards, fictional-only projection, final M1 append, full-write bounds, PR1 linear rendering and ordinary M1/walking race/CAS/cancellation behavior remain verified in the focused/full suites. Existing strict Rail decoder source is unchanged; the earlier16/16 receipt was preserved and not gratuitously rerun or relabelled.

Current handoff remains **source frozen for parent personal correction read and scoped recheck by the same independent reviewer**. Canonical task markers remain18/27;4.5/4.6 are unchecked. No browser/devserver/Q/production build/Host/AppHost/DB/schema/provider/model/network audit/install/commit/push/merge/enablement action was performed. The prior CHANGES REQUIRED verdict remains until the reviewer actually rechecks these exact hashes.


## Same-reviewer R3 correction, round2/5 — current authoritative receipt

Date: 2026-10-21. Read exact `task-4.5-4.6-recheck.md`, SHA256 `50afd581637aa539d0cccc7f9e5c1d3424c70573f73b9fc21dd0ea1c8f699a33`. That independent verdict closed **R1 and R2 as ADDRESSED**, and identified **new R3/P2**: the page's separate focus foundation becomes stale during the preview's own owner-read validation, so an unchanged-context503 loses error focus. Its verdict remains CHANGES REQUIRED until actual R3 scoped recheck; this report does not accept its own fix.

Pre-edit836source/report/manifest/full2555-line patch and all three touched files are preserved under `D:/_Projects/_github/travel-agency/TestResults/trips-m2-rail-implementation/r3-focus-round2/`. The current correction is exactly **162lines**, SHA256 `818b3e76fab800868e2410b2b165fd675d804a8ba3275ad055b761defc130a50`, at `D:/_Projects/_github/travel-agency/TestResults/trips-m2-rail-implementation/r3-focus-round2/fix.diff`; worker read the entire diff. No product source changed after the final846-test full run.

Parent explicitly extended ownership to the **existing** `trips-itinerary-state.service.ts` for this small read-only focus seam. Its before bytes match the pre-Rail baseline. The total hashed source inventory therefore expands20→21 (13changed existing,8new), and unchanged outside-ownership baseline inventory13→12. All18other prior implementation files and all12remaining outside-ownership baseline files were verified unchanged, including old walking state/page tests. This is an authorized scope addition, not hidden modification of those previous baseline claims.

| R3 path | Before R3 SHA256 | Final SHA256 |
|---|---|---|
| `apps/web/src/app/trips/trips-itinerary-state.service.ts` | `0c27843b0485c0b75581e6293fc7156ac586cc45b9bd4bbfeef2c0b0c68e0c1c` | `c17b61eafb95ab2e2f6d69741a6dfde5a8070f63eb9ade2748d86ae6d3619c77` |
| `apps/web/src/app/trips/trips-page-rail.component.spec.ts` | `c827d6b5f3d5f0ed1309d4fe3667d5608074f764555e48c11e9e90ef7c476283` | `19dccd9988b25b415872ee4c844674a2f930780258af5b11d15d8ccb96d5c374` |
| `apps/web/src/app/trips/trips-page.component.ts` | `71f50541a0916b36c6f161b3e3cd7e8e2d24340feaf36b7d4446b9a22d66490e` | `8206a2e9cb41167d3fd3bb45cf4f7d00e596d2ef63e59cefeb6643ca23960eb1` |

### Minimal focus-only implementation

`TripsItineraryStateService.captureAdoptionErrorFocusGuard(): (() => boolean) | null` adds15read-only lines. It captures only the existing preview's owning opaque foundation and existing scope. It does not capture the complete preview, create/renew another ticket, increment an epoch/counter, change signals, issue a read/write or grant a staging capability. Capture requires the local preview context to remain current; it deliberately does not add another stage/freshness gate to focus eligibility.

The returned guard checks current scope/live context and the same owning foundation, plus no current operation, cleared preview and non-idle terminal state. The owning foundation follows its own read under R2, so an actual owner503 with unchanged plan can still focus the status after the preview clears. A manual edit/read/navigation/logout makes that foundation or scope stale; a new preview/operation or direct dismissal blocks the focus-only continuation as well.

`TripsPageComponent.adoptItinerary()` uses this guard instead of its independent `captureEmptyFoundation()` focus ticket. The existing focusIntent/destroy checks remain. On false adoption it checks the guard, then passes the **same guard** into the existing `focus(..., valid)` scheduled callback, so data/context is checked again when focus executes. The successful-stage focus branch is unchanged. `adopt(): Promise<boolean>`, the entire existing generation/adoption/error/read/stage pipeline, public saved/Rail capture/current/append guards and every CAS/write authority are unchanged. The new boolean closure is consumed only by focus; no Rail/staging code can use it as a foundation.

### Actual component and scheduled focus evidence

The positive regression uses actual page `adoptItinerary()`, actual itinerary/editor services and the already visible adoption button as the initial DOM focus. It waits for a real subscribed owner observable and exactly two owner calls (initial load + validation), completes the independent availability read, then sends owner503. With no edit, the preview clears to unavailable, the unchanged editor remains intact, zero PUT occurs and `document.activeElement.id` must be `itinerary-status`.

Six paired late-context cases cover edit, visible/page dismissal, direct service dismissal, newer focus intent, authoritative plan navigation and logout. The navigation case intentionally uses awaited `editor.enter(other)` without advancing the page focus counter: this isolates the foundation/scope guard, so a counter-only workaround would fail. All assert no late itinerary-status focus or write; edits preserve the title input focus, dismissal focuses Generate, and newer intent focuses its confirmation control.

Three deterministic scheduled-boundary cases preserve the real page `focus` implementation and Angular afterNextRender callback. A test-only wrapper injects an edit/direct dismissal at the final valid-callback boundary, or a newer UI focus request immediately after scheduling. It then invokes the production-provided guard and asserts actual DOM focus. The wrapper does not replace adoption/read outcomes or assert only mock calls; the render boundary must be reached and the real status focus must be refused. This avoids sleeps/timing thresholds and catches omission of the final scheduled guard. These are component DOM tests, not native-browser/layout acceptance.

### Exact runtime receipts

All commands use the previously documented worktree/process-local NX/TEMP/TMP settings. Pre-heavy C free checks were approximately2.69GiB; no cleanup occurred. Existing8-file focused command includes Rail/Trips state/projection/component plus untouched walking-state and walking-page regressions:

```text
RED: node node_modules/nx/dist/bin/nx.js test web --include=**/trips-page-rail.component.spec.ts --watch=false --skipNxCache
FOCUSED: node node_modules/nx/dist/bin/nx.js test web --include=**/rail*.spec.ts --include=**/trips-state.service.spec.ts --include=**/trips-page-rail.component.spec.ts --include=**/trips-rail-projection.spec.ts --include=**/trips-itinerary-state.service.spec.ts --include=**/trips-page-itinerary.component.spec.ts --watch=false --skipNxCache
FULL: node node_modules/nx/dist/bin/nx.js test web --watch=false --skipNxCache
```

- `red.log`: **1failed/27passed28tests**. Actual owner503 left activeElement.id empty rather than itinerary-status. This is the meaningful R3 RED.
- `focused-first.log`: **1failed/260passed261tests**. R3 positive and scheduled/late guards passed; the navigation test incorrectly expected a new availability request after a fixed microtask drain. Its retained failure is a test setup/dispatch expectation, not a product regression. The corrected test awaits the authoritative editor context transition and avoids relying on a newer page focus intent or speculative availability request.
- `focused-final.log`: **261/261,8files**, before the final tiny capture-precondition refinement.
- `focused-freeze.log`: **261/261,8files,0failed/0skipped** on exact final source. Capture was refined to read live/own-foundation context directly rather than repeat canAdopt's clock/stage predicate; it remains focus-only.
- `web-full-final.log`: **846/846,57files,0failed/0skipped**. Exactly one full web run in R3 round2, after final source/test edits; no later source change.
- `biome-final.log`:0errors, **133warnings =132non-null +1optional-chain**,14template infos, exit0. Supported owned scope is now16files due the authorized itinerary-state file. The optional-chain diagnostic is the new readable focus guard condition; no unused import, policy suppression or broad rewrite. The retained132non-null warnings remain explicit. Current exact rule/path/line inventory and before-R3 origin are in `D:/_Projects/_github/travel-agency/TestResults/trips-m2-rail-implementation/r3-focus-round2/biome-diagnostics.json`.
- Safe formatting/import checks are retained in `format-first.log`, `format-navigation-test.log` and `format-readonly-context.log`, scoped only to this correction's files.

| R3 raw receipt | SHA256 |
|---|---|
| `biome-final.log` | `60f3a642ed3479bfb8dcc3743d6ec5ada0e5029e993c13bfd18912fbe9dc3156` |
| `focused-final.log` | `b6af9725b92466accece46dab0b840d5c7e2918ad9a1ca531419f4036128561a` |
| `focused-first.log` | `572a60facffb413d71e3e98343ae9b2ae42780fc33b588f46d84222c323cda85` |
| `focused-freeze.log` | `0772a79bf0a6c8bf79ee654785e74188a5054aa6396f823c973496d9f4bec816` |
| `format-first.log` | `0847c5e3c47ac2dfa6dd9e63919faa707b2f158bd40d3cbd848052030c14d0fb` |
| `format-navigation-test.log` | `ec845f019da08e0a5c055d7411581a605101f9ac938077f9bb5ecb14eff1ea0c` |
| `format-readonly-context.log` | `4134ce411dd1835914c035907fd798a05197b0fd6d8009f5f71eef7788246ced` |
| `red.log` | `d34246cc5ae7ba6309daef8f193f32706e0c0bdad1c460b2a6d575ce0b241d69` |
| `web-full-final.log` | `5e10031494c89d4620ff06890881f378c5960ab4c14f31279105d7c24334fc3c` |

Earlier251/836, R1/R2REDs, PR1O(N)regression and original16/16strict decoder receipt remain unchanged in their original locations. No decoder rerun was needed. The final focused/full suites include successful walking adoption, two-GET proof, R1expiry ownership, R2pending owner-read Rail exclusion, cancellation/restart/newer-read cleanup and the100-row render regression. No old walking test was weakened or edited.

Handoff: parent personal read of this exact162-line fix, then **same reviewer scoped R3 and newly induced breakage recheck only**. Canonical18/27/task checkboxes are unchanged; no browser/devserver/Q/production build/Host/DB/provider/model/audit/install/commit/push/enablement action occurred. Current21-file source and raw hashes are frozen in the manifest; independent acceptance remains pending.
