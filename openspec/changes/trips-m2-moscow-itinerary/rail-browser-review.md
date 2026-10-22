# Independent task review — Trips M2 Rail browser task 4.7

Date: 2026-10-21. Fresh read-only review of the complete task diff and retained evidence. No supplied suite was rerun, no browser/server/provider/Host/DB/model/Q execution, Git operation, install, cleanup or canonical change was performed. This review file is the sole owned output.

## Verdicts

- **SPEC: PASS** for task 4.7 under the final CSP correction ruling in its brief.
- **TASK QUALITY: PASS.**
- **Findings: P1: 0, P2: 0, P3: 0.** No concrete defect or material evidence gap requiring a task correction was found. No minimal fix is requested.

These verdicts accept the bounded fictional browser task only. Source D_demo, current structural/build task 4.8, frozen walking Q/human quality, final fresh whole-M2 review 6.1, CI and target/real-source/banner acceptance remain separate.

## Exact reviewed inputs and scope

Read task-4.7-review-brief.md first, including its LAST actual-CSP correction, then task-4.7-brief.md, current Rail refinement, trip-rail-intentions spec and canonical task 4.7. Read the root instructions, test-authoring workflow/ADR0006, existing Trips/standalone browser contracts and accepted R3 source-review interface. No whole implementation plan was read.

| Input | Verified SHA256 |
|---|---|
| `.superpowers/sdd/tasks/task-4.7-report.md` | `5bb666b54ee050df9395d7defb178fd1f0ad8f81d05f06087c8a8046ad8b776b` |
| `.superpowers/sdd/tasks/task-4.7-hashes.json` | `cf4735f45eaf53d1fce87be7881a1dea83ad935a8095dd5d42b4e95654e6790d` |
| `D:/_Projects/_github/travel-agency/TestResults/trips-m2-rail-implementation/task-4.7-source.patch` | `389961456965b1d5855c246ea156c8bcb2628af6cc7c073193f12d0b220f84b6` |

Read the ENTIRE two-file patch against the accepted pre-4.7 fixture, together with both current files. HEAD was not used as its replacement baseline. The accepted before-file is `prior-browser-evidence/tree/tests/travel-e2e/trips-demo/trips-fixture.ts`, SHA `dc211befeac4d4a63890d012e72f229c8ddf2d554a53949aa2ae0dcc270d3b75`; the worker explicitly distinguishes its later initial snapshot. Final fixture SHA is `8fe8fbd5457f93ba225a0fa6f8000db103754efc8f23f4300738d20cd724bf70`; new Rail spec SHA is `2fbd1692212dd335b7db1c08c8a4405b1314c5e220612a4757177ce9b22ddf23`.

## Source and scenario assessment

`tests/travel-e2e/trips-demo/trips-fixture.ts:56` preserves default CI output routing and confines opt-in resolved output to this worktree's TestResults, rejecting cross-drive/parent/link escape. JSON receipts refuse overwrites. The recorded final preflight establishes the v7 root was absent before execution. The additions at `:201` admit only opt-in same-origin Rail station/search routes; the existing external/unknown fence, M1 authorization/CAS/lost-write behavior and afterEach assertions are retained. Authored reference/results are deep-cloned before test mutation, preventing the shared-object contamination observed in v3. No product or shared helper was patched.

`trips-rail.spec.ts:76` exercises actual walking preview/stage/Save followed by explicit Rail stations/date/mode, independent empty target-day selection, one ordinary stage, owner edit/Save and reload at 1280/390px. It checks actual form ancestry, keyboard Enter with no Trip submission, unchanged prior IDs/order/notes/brief/money, exact full PUT, null new times/estimate, exact fare and offset/unknown facts, source attribution, local banner pixels, one-stage double activation and editor focus. Reload retains ordinary text and the fictional historical notice without a trusted Rail panel.

The remaining 43 cases are substantive independent contours: nonempty other-city fourteen-day support and walking restriction (`:191`); exact unchanged buffer on 8/day, 500-scalar essential-note and prospective full UTF8 refusal (`:224`); intact versus damaged/missing/unsafe/safe-limited data and profile/generation authority (`:293`); distinct empty/limited/failed/skipped and mixed independent modes (`:403`, `:420`, `:582`, `:608`); handler versus real enforcing CSP/report-only distinctions (`:446`); delayed timer click expiry (`:634`); A-B-A/latest failed reference (`:644`); late edit/read/save/logout/owner/plan/delete/close effects and focus (`:661`); explicit CAS/lost PUT/pending-save edit (`:716`); station keyboard/late reply/close/reopen (`:518`) and null/changed reference authority (`:562`). Near-limit seeds use the real Trips codecs, avoiding a duplicated guessed validator. No fixture exercises a real provider or itinerary generator.

Clean Save is accurately described as a no-op. The save crossing uses a real explicit local edit and subsequent Save while the canceled Rail callback remains pending; it does not pretend clean Save invalidates Rail. Earlier source/component tests retain the separate saved-selection member/current/foundation checks; browser cases do not claim exhaustive replacement of those tests.

## Independent evidence checks

Performed read-only built-in Node filesystem/hash and JSON checks, not acceptance reruns:

- Recursively verified all **486 unique path/SHA/length entries** in the task manifest, including all 440 receipt entries. Zero mismatches or missing files.
- Independently verified **84/84 historical preserved files** against their preserved-path hashes/lengths. Preservation manifest SHA is `4ea63d693f29b3041c448f689ba26df33b2d26088b0d08cb0719ea1514a30d38`.
- Compared both entire old test modules byte-for-byte with preserved accepted files: workspace 11795 bytes/SHA `85824169e248ca70685c8112956676594d619670cab287d11887b39575c19ac5`; walking 22042 bytes/SHA `7c2eb9b55540d4db36dcb052797eeb693fa69892ea546bc215796822d4779734`. Thus all five original M1 bodies, thirty walking cases, receipt hooks and assertions remain intact.
- Recomputed ID equality from raw final discovery, raw Playwright result JSON and all final individual ledgers: **80 discovered = 80 unique results = 80 unique ledgers**, matching titles; exactly one passed result each, retry 0 and repeatEachIndex 0. No missing/duplicate/unexpected/skip/retry. File counts remain **5 M1 + 30 walking + 45 Rail**, separate from walking 12Q+12S.
- Recomputed final raw ledger totals: **4006 page requests, 1079 API calls, 137 Rail calls, 25 explicit Trip PUTs, 0 unknown failures, 0 external attempts**. The six APIRequestContext assets and two document GETs are explicitly recorded outside the page-event count.
- Independently compared all six actual-public-assets receipt hashes/lengths with current canonical `modules/trips/Travel.Modules.Trips.Infrastructure/Itineraries/Data/moscow-outdoor/1` bytes: **6/6 match**, status 200. Existing test code reads actual Angular responses with redirects disabled and never fixture-fulfills these assets.
- All **34 final preflight source/dependency inputs** still match, including the 21 frozen accepted source files and exact browser source/config/package/installed-lock identities. The raw result config confirms full Trips discovery/execution, one worker, retries 0 and reuseExistingServer false.
- Standalone raw discovery/result IDs independently match **9/9**, unique single passed results/retry 0, using the unchanged existing rail-search.spec.ts and 5100/4201 fictional topology. Read the terminal authored Rail server, imports and routing; the standalone file keeps its original strict page route/authorization assertions. It has retained existing attachments, not newly claimed per-case terminal ledgers.
- Read final raw log/check tails: 80 passed, Biome 0 errors with the three retained existing non-null warnings/11 infos, TypeScript diagnostic count 0. Retained closure receipt records ECONNREFUSED for 4201/4203/5100. No fresh listener or process probe was needed.

Preliminary roots and failures remain represented honestly: v1 partial/timedOut; v2 34 passed/4 failed with ONE attempted official-frame GET; v3 42/2; v4 43/1; v5 44/1; v6 corrected focused 1/1; final v7 80/80. Independently found that exact v2 external URL in its raw ledger; the unchanged strict fence aborts before network. No preliminary attempt was relabelled as zero-attempt. Exact preliminary source snapshots were not all taken; the report explicitly limits final exact-source binding to v7 rather than inventing retrospective hashes. This is a disclosed historical limitation, not a gap in the final v7 proof.

## CSP and visual proof boundaries

`trips-rail.spec.ts:488` correctly adds a stricter TEST-ONLY enforcing self CSP BEFORE the synthetic real reference reply. Actual saved document metadata has no CSP response header and client `frame-src 'self' https://yandex.st/rasp/media/apicc/copyright_vert_mono.html`, matching current travel-frame-policy.ts. No source CSP is relaxed, no real iframe is fulfilled locally, and no real request is allowed in the final run. The actual browser-induced refusal clears facts/choice and leaves no stage, PUT or fallback.

This is composed with `apps/web/src/app/trips/trips-page-rail.component.spec.ts:270`: its helper builds matching real reference and result authority; actual UI and captureSelection leave an otherwise-valid real row view-only and preserve the buffer/no replace call. The component test remains in the frozen source set and accepted exact-source web receipt. Neither proof establishes a stable real official banner or complete route-abort failure visibility; the observed aborted v2 iframe did not emit the clearing error. The report states both limitations correctly.

Independently viewed ALL SIX actual final detail PNGs with view_image at original detail: desktop/mobile picker, staged editor and reloaded editor. The distinct dates, fictional notice/local attribution, exact large fare, focus outlines, meaningful train title, empty time/estimate fields and retained ordinary notice are readable. No material clipped control or horizontal overflow was observed; textarea internal scrolling is ordinary and full persisted note equality is asserted separately. Dense explanatory copy may be considered in final whole-M2 UX triage; it is not a task-4.7 correction finding.

## Remaining gates

The parent retains personal review and canonical acceptance bookkeeping. This independent task review grants no publication/CI/provider/model/Q/Host/DB/enablement authority and makes no broader M2 completion claim. Original fresh-base ancestry is retained as the supplied controller identity; no Git audit or branch mutation was performed in this review.
