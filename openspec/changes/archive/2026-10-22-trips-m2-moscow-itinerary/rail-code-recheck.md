# Scoped independent recheck — Trips M2 Rail 4.5/4.6, R1/R2

Date: 2026-10-21. Same independent reviewer as `task-4.5-4.6-review.md`. This report checks the exact five-file R1/R2 correction and new breakage attributable to that correction; it does not repeat the whole coupled review or final M2 audit.

## Verdicts

- **SPEC: FAIL — one new preservation regression in the R2 correction.**
- **TASK QUALITY: CHANGES REQUIRED.**
- **R1: ADDRESSED. R2: ADDRESSED.** Both original findings are closed on this source.
- **New R3: P2, open.** Explicit walking adoption error focus is lost because the page's separate continuation ticket is invalidated by the newly introduced owner-read generation advance.
- Current open findings: **P1: 0; P2: 1; P3: 0.** No additional issue was confirmed in the scoped delta.

## R1 — ADDRESSED

`apps/web/src/app/rail/rail-state.ts:417` now clears `admitted` on the actual scheduled result-expiry path, alongside `result`. This removes the RailState-owned complete result, membership Set and serialized payload references while retaining the still-fresh reference and selected stations. It does not renew a TTL or request data.

At `rail-state.ts:31`, the retained selection's currentness callback now captures the primitive existing sequence rather than the entire admission receipt. It still invokes the full `eligible()` action guard; later explicit search increments sequence, so old callbacks cannot regain authority after another result is admitted. Strict current membership/fingerprints/decoders and PR1's bounded render path are otherwise unchanged.

`apps/web/src/app/rail/rail-state.spec.ts:334` adds scheduled five-minute-result/thirty-minute-reference cases for both fictional and synthetic real profiles. They assert admission and visible result become null, the valid reference/stations remain, no automatic calls occur, the former selection is non-current and a later explicit search succeeds. This is correct ownership eviction, not a claim about force-GC, byte zeroing or an external caller retaining its own selected row.

## R2 — ADDRESSED

`apps/web/src/app/trips/trips-state.service.ts:242` now advances the existing `ticket(true)` readEpoch/foundationVersion authority before token/GET dispatch, updates only the owning walking foundation's WeakMap record, and sets `detailPending`. Public saved/Rail capture/current/append paths have no pending-read exception. Earlier Rail foundations therefore become stale synchronously and remain stale after success, failure or cancellation.

The narrow ownership exception at `:195`/`:232` keeps only the original walking foundation current during its own owner read. Its plan, revision, session and edit authority are preserved. It does not promote the response into the saved baseline or bypass the unchanged walking owner comparison/availability/freshness/combined stage checks. Duplicate simultaneous owner reads on one ticket are refused.

The matching read release at `:249` clears pending/ownership synchronously on cancellation, with idempotent finally cleanup. The read number guards stop an older callback from clearing a newer read. Existing edit/touch/save methods are not gated by `detailPending`; manual editing remains available and invalidates the adoption context. The prior failed dismiss-then-immediate-generate regression is retained in receipts and corrected without changing its existing test.

Evidence:

- `apps/web/src/app/trips/trips-state.service.spec.ts:645`: actual pending owner observable; saved open/capture/current/append refused; owning walking ticket current; success/failure/cancel; no old Rail revival; explicit normal walking stage; zero hidden PUT.
- Same file `:712`: old owner-read cleanup cannot clear a newer detail read.
- `apps/web/src/app/trips/trips-page-rail.component.spec.ts:504`: actual walking state, strict itinerary client/fixtures and shared Rail consumer; pre-opened Rail invalidates before render/child teardown, retained output cannot append, opening after read start is refused, success and failure/cancellation paths are tested.
- Unchanged walking state/page tests remain included in the final focused/full receipts.

## R3 — P2: own owner-read invalidates the page's error-focus continuation

Changed location introducing the regression: `apps/web/src/app/trips/trips-state.service.ts:242–244`. Affected retained consumer: `apps/web/src/app/trips/trips-page.component.ts:358–367` (`adoptItinerary()`).

The page captures its own `captureEmptyFoundation()` ticket at line 361 before calling `itinerary.adopt()`. That separate ticket is used after an unsuccessful explicit adoption to permit `focus('itinerary-status')` only if the page context is still current. The R2 correction advances readEpoch/foundationVersion and updates only the **service preview's** owning ticket. The page's independent continuation ticket is therefore always stale once the real owner validation read starts, even when the user makes no edit, route/session change, dismissal or other action.

For an owner GET error, the unchanged itinerary service clears the preview and returns false. The page then skips its existing error-focus branch because its ticket was invalidated solely by its own requested validation. This also affects non-successful adoption outcomes after that read, such as availability failure. When the action was activated from the preview's keyboard button, the preview/control disappears without the intended focus destination. The live status remains; this finding is specifically the lost explicit focus transition, not a claim that the error text is absent or that there is a data/CAS problem.

This is directly induced by the fix and falls within preserving the existing walking focus behavior in the task brief. The accepted task4.3 brief line11 requires focus after actual preview/stage/error without focus theft on late invalid replies; the retained page implementation explicitly enforces that behavior. It is not a deferred general styling/layout observation.

A narrowly targeted read-only comparative probe executed the actual page `adoptItinerary`, actual itinerary service `adopt`/owner-read/error cleanup, and actual before/after TripsStateService capture/current/read methods. It used in-memory TypeScript transpilation, simple controlled signal/dependency stubs, an actual pending RxJS owner Subject and the page's focus method replaced with a call recorder. Preconditions were a current owner, clean saved empty plan and valid admitted walking preview; it waited for exactly one owner subscription, then errored it with status503. No editor or focus-intent change occurred. Only the before/after TripsStateService source changed between runs:

```json
{"label":"before-R1R2","ownerGetDispatched":1,"status":"unavailable","previewCleared":true,"focusRequests":["itinerary-status"],"unchangedBaseline":true,"readEpoch":0}
{"label":"after-R1R2","ownerGetDispatched":1,"status":"unavailable","previewCleared":true,"focusRequests":[],"unchangedBaseline":true,"readEpoch":1}
```

The probe exited0. It demonstrates the lost focus request at the actual method boundary; it does not claim an actual browser rendering/focus test. The before source was the verified preserved `r1-r2-review-fixes/before/apps/web/src/app/trips/trips-state.service.ts` with SHA `1bfb20b948d498aafbfc196748cf0db39789902b0a56387f92a4cf6a2dc30dbf`. Page and itinerary service code were the same current unchanged files in both runs.

The new coupled R2 tests call `s.page.itinerary.adopt()` directly, so they bypass the page's affected continuation. Existing walking owner-failure tests at `trips-page-itinerary.component.spec.ts:416` assert preserved draft/brief/no PUT but do not assert this failure focus. Existing focus tests primarily exercise generation errors, successful stage and explicit dismissal. Passing those suites therefore does not resolve this observed regression.

Minimal remedy: preserve the page's explicit adoption error-focus continuation across its **own** validation read, using the existing UI/context/focus intent authority or the owning operation's current context. Continue to suppress late focus after actual edits, navigation/logout, dismissal or a newer focus intent. Do not revive old saved/Rail tickets, weaken R2 exclusion, or authorize staging using a focus-only context. Add a real component test that invokes `page.adoptItinerary()`/the visible adoption button, produces a dispatched owner-read failure with unchanged context and asserts `document.activeElement.id === 'itinerary-status'`; pair it with a late failure after edit/dismissal to retain no-focus-steal behavior.

## Exact scoped read and identity

Read the recheck brief first, original exact R1/R2 findings, appended worker correction report, updated manifest and primary's scoped check. Read every line of the supplied **481-line five-file correction**, via package ranges1–240 and241–483 (two package preface lines). Did not reread the full2555-line coupled patch. The original full2232-line review remains applicable except for this exact correction.

Named outside-delta checks were limited to the R3 question and authority preservation: retained page adoption handler; existing walking focus/error tests; existing service adoption/current/error cleanup; M1 edit/touch gates; task4.3 focus requirement. One guessed specification path was absent; no content was inferred from it. The existing task4.3 brief supplied the exact focus requirement. No unrelated tree audit or source exploration was performed.

Verified current input hashes:

| Input | SHA256 |
|---|---|
| appended report | `20852625135a2edfb5cd3933652e6397a3a228d1e7548f39ff9a9011d25d453c` |
| final source/raw manifest | `8d2ccd48d523b81c12ea4beedcfefe1bcbf311d21e93c57089b43b96af48856e` |
| scoped recheck package | `fecec6fa8f8c84b85fab3bee31561ea3858da277181d999990542921ac59fffc` |
| primary personal review | `f4be86cafaee1e80c4e1f2e72caed4527a431d3cb0751c8cc3399dec9fa26854` |
| exact481-line fix diff | `830ab732c2732334bef829d2dc4dc6a28b07fc6be76ac527423a0f4cd3538b1b` |
| preserved previously reviewed manifest | `eb05d93d32681b9be8f7d68458783cf37c9194794db9a6d1a2cfa5f63987aa15` |
| current full coupled patch, hash only | `9eac3b2f790103bc12d8843bf54b6f230c26ce874d42155c73925927ac1d942d` |

Independently verified **20/20 current source hashes/lengths**, **5/5 changed before-hash links and preserved before bytes**, **15/15 retained implementation hashes**, **42/42 raw log hashes/lengths** (27+7+8), and **13/13 outside-worker baseline source hashes unchanged**. No mismatch. Current HEAD remains `a8721effd9056459d217966083816455fbea599d`; original requested base `e654c76915103a82083ef2abc01c0579117c9749` ancestry check exited0; staged diff is empty. Feature identity is current uncommitted bytes, not HEAD.

Inspected exact current raw counters under `D:/_Projects/_github/travel-agency/TestResults/trips-m2-rail-implementation/r1-r2-review-fixes/`:

- `focused-final.log`: **251/251, 8 files**,0failed/0skipped. SHA `5c45ef9f1402bd9c714ef83fba7bb4941c5a65d8ea7f2adc43eab45f5b15d3f7`.
- `web-full-final.log`: **836/836, 57 files**,0failed/0skipped. SHA `36618bae597ecb1e7344216f1ad66f4f0384a6fd0129bb19334b688ba23d40e4`.
- Meaningful prior RED state5failed/74passed and coupled4failed/23passed, plus first focused cancellation regression1failed/249passed, remain distinct preserved receipts. No historical826 result is relabelled.
- Existing Rail decoder16/16 closure is retained and unchanged, not rerun in this correction/recheck. PR1 component/source files remain among the15 unchanged implementation paths; its regression runs in the supplied focused/full suites.
- Current scoped Biome receipt:0errors,132non-null warnings/14infos. The explicit+11warning delta is not a zero-new or blanket waiver claim. No separate defect was inferred from warning counts.

No supplied suite was rerun. The sole named-risk probe above used installed local Node/TypeScript/RxJS and actual source transpiled entirely in memory, wrote no file and made no network call. No source/test/config edits, Git mutation, browser/devserver, production build, Q/S output, Host/DB/provider/model/audit/install or cleanup occurred. Only this scoped report is written.

R3 needs an original-implementer fix and scoped recheck. Canonical task acceptance belongs to the parent. Browser4.7/current build4.8/frozenQ5.2 and fresh whole-M2 audit6.1 remain separate; no CI, real-source/banner/rights/target or rollout acceptance is inferred.