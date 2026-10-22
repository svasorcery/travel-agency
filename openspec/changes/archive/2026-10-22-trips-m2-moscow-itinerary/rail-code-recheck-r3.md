# Same-reviewer scoped recheck — R3, Trips M2 Rail 4.5/4.6

Date: 2026-10-21. Scope: exact162-line R3 correction and breakage introduced by that delta only. Earlier full source review and R1/R2 recheck remain the surrounding evidence.

## Verdicts

- **R3: ADDRESSED.**
- **SPEC: PASS** for the reviewed tasks4.5/4.6 source scope.
- **TASK QUALITY: PASS** for the reviewed tasks4.5/4.6 source scope.
- **New findings in this delta: P1:0, P2:0, P3:0.** No open independent task-review finding remains. R1/R2 stay ADDRESSED; PR1's earlier closure is preserved.

This closes the independent source task review/recheck, not browser4.7, current build4.8, frozenQ5.2 or the later fresh whole-M2 audit6.1. Canonical task markers and acceptance bookkeeping belong to the parent.

## R3 implementation assessment

`apps/web/src/app/trips/trips-itinerary-state.service.ts:124` adds `captureAdoptionErrorFocusGuard()`. It captures the existing preview's owning foundation and current scope, both read-only. The returned closure requires the same live scope, the same foundation still current, no active operation, cleared preview and non-idle terminal state. It creates no ticket/counter, changes no generation, grants no write authority and performs no read/write. The existing walking foundation can follow its own validation read under R2, so an owner503 with otherwise unchanged context no longer invalidates the page's error-focus continuation.

`apps/web/src/app/trips/trips-page.component.ts:361` now obtains that guard instead of a second independent empty-foundation ticket. At `:367`, failed adoption checks it before scheduling status focus and supplies the same closure to the existing scheduled `focus()` callback. At `:493`, the existing helper rechecks focusIntent, destruction and the supplied validity callback when the render completes. Thus context changes between the failure and actual focus cannot use an earlier successful guard result. The successful-stage branch and its focus behavior are unchanged.

The separate safeguards have actual consumers:

- Owning foundation/session/scope prevents focus after actual editor/read/route/owner changes.
- Active operation or a new preview blocks an obsolete continuation.
- Idle phase rejects direct dismissal, even without a newer page focus intent.
- Existing focusIntent rejects newer explicit UI focus actions; destruction rejects callbacks after teardown.
- The scheduled validity callback prevents edit/direct dismissal from stealing focus at the final render boundary.

No source capability is widened: all Rail saved/current/append paths still use the R2 shared foundation checks; R1 result-admission eviction and PR1 display/action separation are unchanged. The diff adds no altered adoption/GET/CAS/stage pipeline, no auto retry or save, and no duplicate state authority.

## Focus regression evidence

Read all new tests in `apps/web/src/app/trips/trips-page-rail.component.spec.ts`:

- `:594`: actual page adoption with the visible adoption button initially focused, dispatched owner observable and independent availability response, owner503, cleared preview, unchanged draft, no PUT, and actual `document.activeElement.id === 'itinerary-status'`.
- `:618`: six late-context cases: edit, page dismissal, direct service dismissal, newer focus intent, authoritative plan navigation and logout. These check that the status does not steal focus, with positive expected destinations for retained input/dismissal/newer intent.
- `:658`: three final scheduled-boundary cases inject edit/direct dismissal/newer intent while retaining the production focus helper and its actual Angular callback. They require the boundary to be reached and assert real component DOM focus, so omitting the final supplied guard would be detected.

The navigation case intentionally changes the authoritative editor context without advancing page focusIntent, independently testing the foundation/scope guard. The final scheduled cases exercise the real callback with deterministic interception, not a timing threshold. The positive case uses page.adoptItinerary rather than bypassing its continuation through service.adopt.

Raw `r3-focus-round2/red.log` was inspected: meaningful RED is1failed/27passed, specifically expected itinerary-status but actual empty activeElement.id. The final exact-source raw counters were inspected:

- `focused-freeze.log`: **261/261,8files,0failed/0skipped**; SHA256 `0772a79bf0a6c8bf79ee654785e74188a5054aa6396f823c973496d9f4bec816`.
- `web-full-final.log`: **846/846,57files,0failed/0skipped**; SHA256 `5e10031494c89d4620ff06890881f378c5960ab4c14f31279105d7c24334fc3c`.

Both are under `D:/_Projects/_github/travel-agency/TestResults/trips-m2-rail-implementation/r3-focus-round2/`. Supplied focused coverage includes earlier R1/R2, successful walking adoption/two-GET and100-row rendering regressions. Existing Rail decoder16/16 remains a retained unchanged-source receipt, not a new run. The earlier navigation-test setup/dispatch expectation failure is preserved separately; it is neither a product failure claim nor relabelled as a pass.

The provided evidence answers the exact R3 risk, so I ran no additional probe or suite. This is source/component DOM evidence; native-browser focus/layout is still the separate4.7 task.

## Read scope and exact identity

Read the R3 recheck brief first, the prior exact R3 finding, the R3 appended worker receipt, updated manifest and primary scoped note. Read **all162lines** of the exact three-file fix once; no whole2709-line coupled patch reread. The only narrow source read outside that delta was the existing final `focus()` implementation to verify it actually executes the supplied validity callback.

Verified input hashes:

| Input | SHA256 |
|---|---|
| R3 recheck brief | `a348da35f0c8f517e9b2b0c70ad26eb87909306b46853106d53f06f78e50641d` |
| previous independent recheck | `50afd581637aa539d0cccc7f9e5c1d3424c70573f73b9fc21dd0ea1c8f699a33` |
| current appended report | `5187ed0a7954bed973bf4e9199d1af0c7ee216721ecc2b7f5487eb0fdf79e1d5` |
| current manifest | `1af7f86f8be821636b8d36838cd540090d24c4bb3aac1a3b7782f78e251fefb7` |
| exact162-line fix | `818b3e76fab800868e2410b2b165fd675d804a8ba3275ad055b761defc130a50` |
| current primary personal note | `aedf35162daba99275ff6f9afd64fc2b1d51656b694234b9008325f3715fe251` |
| preserved prior manifest | `8d2ccd48d523b81c12ea4beedcfefe1bcbf311d21e93c57089b43b96af48856e` |

Independently checked **21/21 current source hashes and lengths** (13 existing +8 new), **3/3 before-file hash links and preserved before bytes**, **18/18 retained implementation hashes**, **12/12 outside-ownership baseline source hashes unchanged**, and **51/51 raw receipt hashes/lengths** (27+7+8+9). **No mismatches.** The additionally authorized existing itinerary-state file's pre-R3 hash matches its original pre-Rail baseline; the inventory expansion21/12 is explicit.

Current changed hashes:

| File | Final SHA256 |
|---|---|
| trips-itinerary-state.service.ts | `c17b61eafb95ab2e2f6d69741a6dfde5a8070f63eb9ade2748d86ae6d3619c77` |
| trips-page-rail.component.spec.ts | `19dccd9988b25b415872ee4c844674a2f930780258af5b11d15d8ccb96d5c374` |
| trips-page.component.ts | `8206a2e9cb41167d3fd3bb45cf4f7d00e596d2ef63e59cefeb6643ca23960eb1` |

Git HEAD remains `a8721effd9056459d217966083816455fbea599d`; ancestry from requested original base `e654c76915103a82083ef2abc01c0579117c9749` exits0; staged diff is empty. Uncommitted source hashes are the feature identity. No Git mutation occurred.

Scoped Biome receipt records0errors,133warnings (132non-null +1optional-chain),14infos across16supported files. The small optional-chain style diagnostic on the readable guard condition is not a correctness issue; no policy suppression or blanket waiver is inferred. Other recorded diagnostics retain their previous provenance.

## Remaining boundaries

Only this new review report is written; source/tests/config/canonical task files and previous reports are untouched. No tests were rerun, no subagents spawned, and no browser/devserver/build/Q/S/network audit/Host/DB/provider/model/install/cleanup/Git/push/enable action occurred.

No remaining blocker was found in R3 or its scoped delta. Source-task PASS does not establish native browser/layout, current production build, frozen quality output, final whole-M2 audit, CI, official banner/rights, real source, target or rollout acceptance. Those gates remain separate and retain their existing owners and authority.