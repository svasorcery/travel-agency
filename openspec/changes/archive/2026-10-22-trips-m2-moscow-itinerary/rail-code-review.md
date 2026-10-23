# Fresh independent review — Trips M2 Rail tasks 4.5/4.6

Date: 2026-10-21. Scope: final coupled source task review after the primary's PR1 recheck. This is not the later whole-M2 audit 6.1.

## Verdicts

- **SPEC: CHANGES REQUIRED.** Two P2 gaps remain: expired result retention in the new admission object, and Rail admission while the existing walking owner GET is pending.
- **TASK QUALITY: CHANGES REQUIRED.** The source is otherwise bounded, the shared component/action guards are coherent, and PR1's rendering issue is addressed. Fix the two concrete lifecycle gaps and obtain scoped recheck before accepting 4.5/4.6.
- Findings: **P1: 0; P2: 2; P3: 0.** Style warning totals alone are not a finding.

## R1 — P2: the new admission object retains expired rows and fares until catalogue expiry

Location: `apps/web/src/app/rail/rail-state.ts:326` (new admission retaining `result`, `members`, `resultJson`), with the existing result-expiry cleanup at `:415`.

After a successful search, `admitted` owns the complete result object, every journey via its Set, and a serialized complete result. It is populated for real as well as fictional profiles. When the result's original expiry arrives before the reference expiry, `scheduleExpiry()` sets only `this.result = null` and reschedules for the reference deadline. It does not clear `admitted`. The expired rows, fares and serialized source payload therefore remain in component memory until another criteria change, source clear, teardown or catalogue expiry. An ordinary five-minute search against a thirty-minute reference retains these values for another twenty-five minutes.

`selectionAvailable()` correctly returns false once `this.result` is null, so this finding does **not** claim an expired append or visible expired row. It is the explicit source-retention regression: `openspec/specs/rail-schedule-search/spec.md:341`, `:349`, `:359` and `:389` require removal of expired snapshots/source rows/RAM entries, and the task requires preserving this published Rail contract. Hiding the display leaves the new owning references alive.

Read-only probe used the actual final RailState implementation transpiled in memory, the existing fictional `apps/web/test-fixtures/rail-fixture.ts`, a controlled clock/timer scheduler and a fixture transport. Reference expiry was original reception + 30 minutes; the fixture result expired after 5 minutes. Invoking the actual scheduled result-expiry callback returned:

```json
{"probe":"result-expiry-memory","visibleResult":null,"retainedAdmission":true,"retainedRows":2,"retainedMembers":2,"retainedJsonContainsFare":true,"referenceStillFresh":true,"nextCleanupMilliseconds":1500000}
```

Minimal fix: clear the admission receipt, including its Set and serialized payload references, whenever result expiry clears the current result. Preserve the still-valid reference/selected stations and existing explicit-refresh behavior. Add a focused test with `result.expiresAt < reference.expiresAt` that advances the real scheduled expiry path and proves both the visible result and its internal owning admission references are gone; keep the earlier emitted context non-current. No TTL renewal or automatic request is needed.

## R2 — P2: pending walking owner read is absent from Rail's clean-saved exclusion

Location: `apps/web/src/app/trips/trips-state.service.ts:225` (new shared clean-saved pending-read guard). Connected paths: existing `readEmptyFoundation()` at `:229`, new `TripsPageComponent.openRail()` at `apps/web/src/app/trips/trips-page.component.ts:146`, `railOpen` at `:72`, and `chooseRail()` at `:161`.

The new guard observes only `detailPending` and `listPending`, set by `refreshDetail()`/`loadList()`. The existing walking adoption performs a real owner GET through `readEmptyFoundation()` (`trips-itinerary-state.service.ts:185–187`), but that method does not set either pending signal or advance the common read/foundation generation. The page's Rail open/current/choose guards do not inspect the already available `itinerary.pending()` checking-adoption state either.

Reachable sequence: on a clean saved empty plan, obtain a walking preview and open Rail; start “Перенести в редактор” while its owner GET remains pending; the existing Rail foundation remains current and a valid Rail choice can still stage. Opening Rail after that owner GET starts is also admitted. This violates the accepted no-pending-read opening/staging requirement and the requirement to invalidate existing Rail choices on reads (`rail-refinement.md`, “One opaque Trips foundation”; delta spec, “Clean saved foundation for Rail staging” and “Rail picker lifetime and privacy”). The supplied component integration setup returns disabled itinerary availability, so its read/list cases exercise refreshDetail/loadList, not this coupled path.

The common edit/foundation guard still prevents the eventual walking completion from overwriting a Rail edit; ordinary explicit Save/CAS remains authoritative. **No data loss or CAS bypass is asserted.** The defect is allowing a competing automatic transfer while the owner validation read is unresolved, despite the explicit exclusion contract.

A focused read-only method probe used the actual final TripsStateService prototype (in-memory TypeScript transpilation), a current-owner clean empty baseline, dependency/signal stubs, real RxJS Subject/firstValueFrom/takeUntil, and the actual capture/current/read methods. It waited until the owner API observable was subscribed, leaving it unresolved:

```json
{"probe":"walking-owner-read-admission","ownerGetDispatched":1,"readStillPending":true,"canOpenRail":true,"canCaptureRail":true,"oldRailStillCurrent":true,"walkingStillCurrent":true,"readEpoch":0}
```

Minimal fix: include this existing owner-validation read in Rail's exclusion and invalidate an already captured Rail choice when that read begins. Reuse the existing walking pending/current authority or track that read with ownership in the shared seam; do not add another CAS/epoch framework. Preserve walking's own exact foundation and two-GET check: naively making its own `isEmptyFoundationCurrent()` false during its read would break the accepted path. Add a coupled test with a real pending walking owner read, asserting both refusal to open and refusal/invalidation of a pre-opened Rail choice, plus successful walking completion when no other edit occurs.

## Independent review assessment

I read the complete final coupled patch once in sequential nontruncated ranges (package lines 1–350, 351–700, 701–1050, 1051–1400, 1401–1750, 1751–2100, 2101–2234). The two package preface lines precede all **2232 source diff lines**, covering **12 changed existing + 8 new files**. This includes every changed implementation, template, stylesheet, test and both UI READMEs. The parent review was context, not a substitute for this read or judgment.

PR1 is addressed: one Set is constructed on result admission; `selectionReason` uses bounded membership/current/profile/request/freshness/attribution/mode/ticket predicates. No whole-envelope serialization or strict decoder runs from display. Existing strict decode and original result/reference fingerprints remain on actual capture and `isCurrent`, including the final append callback. The supplied valid 100-row regression checks four rendering passes, enabled choices, positive strict-action work, tamper and expiry refusal. The two modes and bounded reason lists keep display work O(N) in row count. Synthetic mutation of an already admitted snapshot may leave the display hint enabled until strict click rejection; this is explicitly an action-guard boundary, not acceptance of corrupted data.

Other reviewed contract paths are supported by source and relevant tests:

- One existing RailApiService/RailState/strict wire decoder; semantic reusable section and standalone main/H1 wrapper; no duplicate transport, backend, flag, persistence or algorithm. Construction/open sends no Rail request; actual request tests contain only explicit Rail criteria and no private plan values.
- Fictional-only action admission, clear real retention refusal, intact selected row, independent other-mode failure, explicit safe limited reasons, source/profile/epoch/catalogue/request/member and absolute original freshness checks. Null/expired/future/changed/damaged/attribution failures refuse action. Full decoder is still authoritative for payload shape, TTL and consistency.
- Common WeakMap foundation and same edit/read/session/revision authority; existing empty-only walking wrappers retained. Nonempty local append uses the actual full `decodeTripWrite`, preserves prior content, checks 8/day and full scalar/UTF-8 bounds, invokes source recheck and same-foundation guard immediately before one synchronous edit. The new Rail-read exclusion gap is separately R2 above.
- Exact historical projection, exact eligible optional number or fixed noun fallback, refusal of essential note overflow/incompatibility, null block times and estimate; no fare/adult/budget conversion. Ordinary explicit Save/reload remains separate from source state. Original-ticket output binding prevents close/reopen stale-child replay.
- Sibling form DOM, one page main/H1, explicit independent day/date, type=button choice/open/close and local station keyboard focus. Existing iframe allowlist, sandbox, no-referrer, text/link order, enforced CSP failure and cancellation/dispose are retained. Source/component checks do not establish native browser form/layout or official-banner visibility.

Narrow outside-diff reads answered named questions only: complete RailState search/admission/timer cleanup; M1 current/clean/read methods; walking adopt owner-read call and current/final guards; page adoption handler and relevant template gating; strict Rail decoder attribution, freshness, mode/item/TTL bounds; actual tripText/decodeTripWrite semantics; published Rail expiry/retention clauses. Root AGENTS, exact task brief/review brief, refinement, delta spec, tasks4.5/4.6 and the frozen task4.2 interface were read. No broad tree/source audit was performed.

## Exact identity and evidence checks

All of these were independently verified against current bytes:

| Input | SHA256 |
|---|---|
| final worker report | `248b0b24176fa40b28af040a0457a3621157fe534715cce2a8085e4a7ecdfd7f` |
| final manifest | `eb05d93d32681b9be8f7d68458783cf37c9194794db9a6d1a2cfa5f63987aa15` |
| supplied full review package, 117942 bytes | `11b6ce9ca8311a7caf4b03acd6bb92c6bd343d8e950f2b7afac7b8956acb55fa` |
| primary personal review and PR1 recheck | `34c7ffea18887b22ab236c27fc0114e505e464a98270d4eb174bec0111a4227e` |
| full 2232-line source patch | `689fb9e83b801c3cbb4d86566296bc7e89d696ad62458a824a78f7a366251e01` |

Verified **20/20 current source file hashes and lengths**, **34/34 raw receipt hashes and lengths** (27 retained + 7 PR1 follow-up), all **25 baseline source hashes**, linkage of all **12 existing before hashes**, all **13 out-of-worker baseline files unchanged**, and all **17 other implementation files unchanged versus pre-PR1 manifest**. No mismatches. Baseline manifest lives beside `baseline/`, not inside it; one initial read used the wrong location, then the correct manifest was read and verified.

Current Git HEAD is `a8721effd9056459d217966083816455fbea599d`; `git merge-base --is-ancestor e654c76915103a82083ef2abc01c0579117c9749 HEAD` exited 0. Staged diff is empty. The task's feature identity remains the uncommitted source hashes against the exact pre-Rail WIP baseline, not HEAD or HEAD~1. No branch or Git mutation occurred.

Current raw counters were inspected directly:

- `D:/_Projects/_github/travel-agency/TestResults/trips-m2-rail-implementation/p2-render-cost/focused-final.log`: **129/129, 6 files**, 0 failed/0 skipped.
- Same directory `web-full-final.log`: **826/826, 57 files**, 0 failed/0 skipped.
- `D:/_Projects/_github/travel-agency/TestResults/trips-m2-rail-implementation/rail-decoder-final.log`: retained **16/16, 1 file**, unchanged decoder closure. This is not a new post-PR1 decoder run.
- Current scoped Biome receipt is 0 errors, 121 non-null warnings, 14 infos; warning provenance and explicit PR1 delta remain recorded, not waived or recast as zero-new. Reviewed production assertions are under current decoded/synchronous guards; no independent null-dereference defect is asserted from the diagnostic count.

I did not rerun supplied suites. The two named-risk read-only probes ran with installed Node/TypeScript/RxJS via an inline PowerShell here-string piped to `node`, from the reviewed C worktree; they transpiled actual source in memory and wrote no source/test/cache file. They are method-level diagnostics with controlled dependencies, not Angular/browser/full-suite acceptance. The first combined invocation established R1, but the first R2 harness completed its Subject before the async cross-context token continuation dispatched and ended in `EmptyError: no elements in sequence`. A separate corrected R2 invocation waited 12 microtasks, explicitly required `dispatched === 1 && !settled`, then produced the successful diagnostic above and exited 0. The initial setup failure is preserved here and is not evidence of a product failure.

No source edits were made. Only this review report is owned/written. Browser/native/layout4.7, current production/source/dependency acceptance4.8, frozen Q phase5.2 and final fresh whole-M2 audit6.1 remain separate. No Q/S output, browser/devserver, Host/AppHost/DB/provider/model/network audit/install, CI, real-banner/rights/target or rollout claim is made. The wider frozen walking corpus/clock identity is outside this review's narrow verification set; the checked baseline walking files were unchanged, and parent/controller evidence remains the authority for the wider freeze.