# Fresh whole-M2 independent source audit — task 6.1

Date: 2026-10-21. Reviewer: fresh independent whole-source reviewer, separate from the task and Q methodology reviewers. Read task-6.1-brief.md first (SHA256 597baf408a7f6287ae74829871ae08eff621336b4d7fc1372e923d15acf6f4ed).

## Verdict

**SOURCE / LOCAL DEMO READINESS: CHANGES REQUIRED.**
**SPEC: CHANGES REQUIRED. QUALITY: CHANGES REQUIRED.**
Findings: **P1 0; P2 1; P3 3.**

The architecture and demonstrated mechanical scenario are sound for this bounded source/local demo. The confirmed test defect prevents a clean source-readiness acceptance; the small UI defects should be corrected in the same requested unified fix wave. No algorithm, data, mathematical model, scope or new infrastructure change is requested. No private-write, owner/CAS, production-enable, real-Rail-retention or invented-route defect was found in the inspected effective implementation.

Existing D_demo and first bound mechanical Q_demo evidence remain accepted for their exact original inputs. Human route suitability/calibration, real access/hours, real Rail/banner/rights/quota, ordinary publication CI, Host/Keycloak target and rollout are separate pending gates, not prerequisites invented to block this source/local demo. Task6.1 is not closed by this report.

## Confirmed findings

### F1 — P2: helper rejection test becomes valid on 22 October and breaks ordinary CI

Location: tools/trips/build-moscow.test.mjs:269 (fixture clock at :12–15; CI consumer .github/workflows/ci.yml:44).

The test named "unsafe nodes, missing anchors, sparse coverage and stale/unbound metadata fail without release" expects rejection after setting publication to the literal 2026-10-22T00:00:00Z. Its downloadedAt and asOf are today's UTC midnight, and identity retrieval is the same dynamically chosen asOf. On 2026-10-22 UTC the supposedly future publication equals download/build; on subsequent days it is earlier. Through the ensuing nonexpired interval the otherwise-valid fictional graph, inventory and SHA binding pass the helper's publication/download/build ordering and current-expiry checks. Therefore ready output is published and rejected() fails its status assertion. The new unconditional helper CI step makes this a reproducibility/CI defect, not a hypothetical product data refresh concern. The current 21 October green receipt remains truthful; it does not prove the test is clock-stable.

Smallest fix: derive this deliberately future publication from fixedDay plus one day (or another explicit positive offset), rather than a calendar literal. Keep its expected rejection/assertions and the helper's production checks. A focused test should show the invalid case stays future when the fixture day advances. Do not alter the immutable real package, Q cases/clock/protocol or source freshness policy. The separate frozen-clock unit/API tests are unaffected.

Evidence: complete helper/test source read and direct control-flow/date comparison. No helper or test was executed during this audit.

### F2 — P3: fictional fares are explicitly attributed to real Yandex data

Location: apps/web/src/app/rail/rail-search.component.html:24.

Every reported fare renders "Цена по данным Яндекс.Расписаний", including profile=fictional. The same actual screen says the stations, journeys and prices are not Yandex data. This contradiction is visible in rail-picker-detail-1280.png and rail-picker-detail-390.png next to the exact fictional price 9007199254740993.05 RUB. The phrase was inherited from the upstream standalone template and has been carried into the newly extracted shared component; this is not a claim that M2 originally introduced the upstream wording. It is nevertheless a concrete error in the full supported M2 fictional demo.

Smallest fix: branch only the provenance wording by the strict result profile: fictional price / учебная цена for fictional; the existing Yandex attribution for real. Preserve every exact amount digit, currency, unknown scope, seats/realtime warnings and the required source/banner notices. Add an actual shared-component assertion for both profiles so the reusable /rail and Trips views agree. No source-result relabelling, fare copying into M1 or retention permission change.

### F3 — P3: two asynchronous focus continuations omit the final render-time context guard

Location: apps/web/src/app/trips/trips-page.component.ts:347 and :355–356; shared focus callback :493–497.

refreshItineraryAvailability() checks context/draft/brief after its await, then schedules focus without a validity closure. generateItinerary() likewise checks its captured empty foundation before scheduling, but the eventual afterNextRender callback defaults valid() to true. An editor change between scheduling and render does not increment focusIntent (title()/patchBrief() update editor authority). The stale generation-error/availability-error continuation can consequently focus the still-present itinerary-status after the user has resumed editing. Generation success has the same missing final guard, although its disappearing preview heading often makes that branch harmless. The existing R3 adoption-error fix correctly supplies its owning continuation guard; it does not cover these other paths.

Smallest fix: pass the existing captured-context/foundation and current-outcome checks through to focus(), so they are rerun at afterNextRender. Reuse the current tickets/context and focusIntent; do not add another revision machine or change staging authority. Extend the existing deterministic render-boundary component test pattern from trips-page-rail.component.spec.ts:658 to generation error and explicit availability error, including a positive unchanged-context focus case and a late edit/direct dismissal or newer action. No browser timing threshold is necessary.

Evidence: complete page/state source and focused/component test bodies read; control-flow proof. This is a keyboard/focus defect, not a content overwrite or unauthorized persistence finding. No new test was executed.

### F4 — P3: repeated walking disclaimers and software metadata obscure the route

Location: apps/web/src/app/trips/trips-page.component.html:372–380; existing source disclosure :424–429.

Before the first stop, the preview shows a coverage warning, the full exclusions/uncertainties list, then repeats modelled/uncalibrated/no-human-review statements in another paragraph and exposes the raw builder identifier and seven-digit software timestamp. The current desktop/mobile PNGs show the resulting long block of repeated explanation before the route. The mandatory uncertainty is appropriate; repetition and internal preparation metadata in the main decision flow make the actual proposed stops harder to inspect.

Smallest fix: consolidate repeated statements into one short visible demo/uncalibrated/no-human-review summary; retain distinct unmet interests and relevant exclusions/unknowns. Move preparedBy/buildAsOf and other reproduction metadata into the existing source/data details. Keep the per-edge "Оценка по карте … Без полевой проверки", explicit unknown access/costs, model assumptions, exact source metadata and attribution reachable. This requests a local copy/hierarchy cleanup, not a redesign, disappearance of uncertainty or a change to raw API facts. Update relevant rendered-text assertions and inspect the affected desktop/mobile view.

## Fresh inspected scope

Worktree: C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency.
Requested fresh base: e654c76915103a82083ef2abc01c0579117c9749.
Authorized integrated upstream context: a8721effd9056459d217966083816455fbea599d.
Branch context: codex/trips-m2-moscow-itinerary.
Ancestry is retained from the accepted integration/structural receipts; this reviewer did not run Git or substitute HEAD for the requested base.

The reviewed feature is the actual uncommitted-byte package, not HEAD. Independently rehashed all184 final manifest entries: zero missing/drift. Source tree identity: 5185181c407d535a63cb4e64874e4c78c02dfe8af431038c8ee65e16767b535e.

Reviewed all changed executable/config/test code in the exact 92-path review-reading-index: complete new executable and test bodies, effective changed production files, and changed hunks plus relevant surrounding seams for retained architecture/old-test files. The six raw JSON wire vectors in that list are data, parsed/shape/hash-inspected rather than treated as executable code. Large/truncated command displays were followed by bounded reads of the missing relevant portions; no missing-file defect was inferred from a truncated display.

Logical passes:
- All new Trips Core itinerary values/fact factories, policy and independent validator; Application fact/encoder ports, result contracts, two-slot admission and candidate service.
- Full pinned Infrastructure loader/release descriptor and copy configuration; the complete offline helper and CLI tests; all new C# fact/policy/validator/service/loader/codec/HTTP/composition/Q tests and their fictional factories/HTTP fixtures.
- Full API DTO encoder, endpoints, route classifier/request/response guard behavior and typed errors; focused existing TripPlanService ownership, decryption, full-detail reserve and CAS seam.
- Full strict raw TypeScript parser/decoder/types/transport; new decoder, transport and actual-body Q tests.
- Full walking state, effective Trips state/page/template/styles, Rail state/shared component/template/styles/projection; new state/component tests and modified M1/standalone regression hunks. Read existing public Rail decoder/client as the reused authority.
- All new/changed browser fixture and walking/Rail cases, the extracted old-workspace fixture change, exact source privacy and form/keyboard/lifecycle checks.
- All changed architecture guards/selectors/positive and negative fixtures; .gitattributes, Biome, Nx inputs/assets/API-client export, CI helper insertion and the unrelated one-line Flights test typing adjustment.
- Entire ignored task-5.2-run.ps1 recorder explicitly included by the brief; safe task4.8 runner and executed-code reconstruction/correction reviewed as evidence, never executed.
- Root/Trips/Shared AGENTS; proposal, design including geography §8, both normative delta specs (62 walking and35 Rail scenarios), Rail refinement, API contract, task requirements/current verification/current scenario overlay, module/client/UI operations/readmes, data dossier, frozen quality protocol, no-tuning record and current structural/Q reviews. The M1 contract is still in openspec/changes/trips-m1-structured-planning/specs/trip-plan-workspace/spec.md, not a published openspec/specs path. Published Rail retention contract was read at openspec/specs/rail-schedule-search/spec.md.
- Upstream25-path integration context was kept separate. Relevant root package/lock overrides, existing public Rail contract and integration/dependency receipts were checked; upstream documentation archive churn is not attributed to this feature.

Nine specified current images inspected: both Rail picker detail images, both staged detail images, both reloaded detail images, and all three walking/full-plan images under TestResults/trips-m2/rail-browser-a8721eff-v7. No overflow/overlap or broken actual M1 editor control was found in those retained pixels. They are fictional browser evidence, not new browser execution or human route-quality evaluation.

## Design and boundary assessment

For the stated demo, offline OSM plus modelled time is a suitable bounded implementation. One BCL preparation helper emits a small pinned immutable package; runtime performs deterministic selection over that package above the existing M1 editor. No runtime routing service, map DB, provider/model call, persistence schema, orchestration framework or speculative geography registry is required.

Verified code boundaries:
- Independent trusted manifest pin, six exact filenames and per-file caps; strict schema/duplicates/UTF8/LF, source/release/rights/actor/date checks; no cache refresh or validity extension.
- Directed foot/access/barrier/profile rules, bounded approximate anchors, stable integer-mm paths/nearest-five selector, exact uncalibrated75/45 model formula; disconnected POIs are retained as such, never repaired. Source18-connected/2-isolated and20POI/90paths are honest.
- Immutable facts; stable ties/distinct-interest preference; pace2/3/4 and daily caps; unique POIs; date-local whole-day freshness and independent validator. The20,000 defensive cap is not claimed reached under the7,056 valid-input bound.
- Owner-before-private-payload processing; exact expected revision/empty basis; source-free unsupported distinct from empty/unavailable. No generation store/protection write, supplier/model call or retry. Actual bounded encoded bytes and M1 maximum-server-field reserve precede final owner/revision/expiry observation; those same owned bytes are written.
- Off404 before auth/dependencies; authenticated ten-field availability avoids plan readiness/store/codec/protector; Production omits demo DI; existing Host composition/security order stays intact.
- Actual raw decoder checks and saved-foundation binding; owner+availability adoption reads; read ownership prevents Rail/other tickets following the walking read; ordinary M1 stage/save/CAS/lost-write contract remains authoritative.
- Rail strict click membership/request/profile/epoch/generation/absolute expiry/attribution checks; final append rechecks source after full write validation; safe selected mode remains independent of another mode failure. Real profile remains transient/view-only, no stripping/relabel bypass. Exactly one fictional block appends atomically, null times/estimate, exact identity note and bounded title, with explicit Save only.
- Public six-file assets and browser-only Trips/Rail route policy; external Data/Nx affected inputs, formatter-only immutable byte exceptions with lint retained, one helper step in existing CI topology.

## Evidence independently checked and evidence reused

Independent read-only checks in this audit:
- Six audit package hashes verified. Manifest SHA256 6f26722b196ac150d8fbd1e57583bee947db3ebe0b4555e0bf2572105dd53ef3; complete feature diff edb05c5cab719bda94152fd74c46e5876bf8550e2c395642edce131692593050; code/config/test diff 3a64ba8c33ab93d2b1e200f318ae2e3078a663164362057d29794cff29e74055; reading index 28caee9e52373aa93e76e863d5e8c0b4f6131bd3a48170224f1b412bc405f0dc; integration context 31200d433106a4df070be395a549e14e54aaaa21a90140a7c825a1ad741677ca; accepted receipt index e66effafe893f0ad46fec8083ef96eb25e248339f4a5dd03db1a35613b7385ae.
- All19 accepted receipt-index references and all19 first-Q receipt artifacts rehashed successfully.
- Actual Q source manifest1657 entries: exactly the declared modules/trips/README.md prose drift, no other drift. All718 retained Q binary files rehashed successfully. This is not a relabelling of the old global source hash as current.
- All24 raw corpus record bodies and all50 HTTP exchange lengths/SHA256 verified; all24 completed, failures empty. Actual TRX is one ordinary Fact: total1/executed1/passed1/notExecuted0. Actual Node JSON13/13, pending0. Q targets2,3,4,4,6,8,6,9,12,3,6,9 allfilled; S edge6 and negative6 remain separate.
- Canonical package five child hashes,20POI/90edge structure, all90 exact raw path-object hashes and allsix actual built public asset bytes checked.
- Actual current Architecture TRX208/208 and ordinary Node JSON356/356 counters inspected, separate from13 Q checks. Actual build result exit0 and actual style warning receipt read; effective Nx input/affected proof read.

Reused, with source/hash binding and independently accepted receipts, not rerun here:294Unit,98loader,85API+30M1HTTP,15helper;846web/57files;80Trips browser (35existing+45Rail) and9standalone;45CSharpier,48Biome0errors,15OpenSpec,134harness,62inventory plus the existing Windows capability skip; one75-artifact production build with sixassets/threepublicprerenders. Local7/15/60 dependency checks and the independent59,458-file installed corpus verification are reused; this audit did not redundantly rehash the whole installed environment or perform npm audit.

Q binding49829aa1f7a083d1e1ac5d7f50880712c0bcb1af548ad2f6626621ea43308b8d preceded the single execution (19:04:15Z before19:05:15Z). Cases f50b7511fc12642b20df7ca207c780740382eeb4a482258929551ce29aa5756c, protocol bcb608c08f4761c973498186942194ab7d8913ea27f5ccac00ed757bd4489a1f and frozen fixture clock2026-10-21T14:16:33.0000000Z remain unchanged. Accepted mechanical Q is observed regression evidence, not an unseen holdout, calibrated time or human score.

## Explicit set-asides and limits

- Raw160MB OSM is not imported/rebuilt. Original map eligibility/reproduction, lawful source acquisition and measured resource receipts are reused from accepted D_demo; current helper implementation, pinned inputs/bundle/path hashes were checked fresh. Repeated path numeric arrays, base64 bodies and historical duplicate prose were structurally/hash checked rather than manually reading every repeated byte or every old review paragraph.
- The exact first-Q recorder is ignored/local and machine-specific by design; it is explicitly retained in this audit package and bound evidence. Portable ordinary .NET/Node regression source and the three raw-response fixtures are present. The approved scope does not require publishing that one-off orchestration script or invoking it on CI. No source-retention blocker is inferred.
- Trips CSS is5.01kB, above the4kB warning and below the8kB error; prior walking was4.83kB, original M1 was already near4kB. Read the actual stylesheet: the added rules serve the new retained views. A small shared-border cleanup would not by itself remove the1.01kB warning, and no concrete loading/layout failure follows from this warning. **Set aside as a disclosed performance warning, not a confirmed required defect.** No budget increase, style relocation to evade budgets or claim of warning-free build. F4 requests useful copy cleanup independently of that warning.
- Existing standalone fictional-fare wording is inherited (F2), but still wrong in the delivered shared component; other untouched Flights/Support style warnings are not new M2 defects.
- RAIL08 is induced stricter TESTONLY self-CSP plus a separate actual otherwise-valid real-profile component refusal. It is not official banner visibility proof. The preliminary aborted external attempt and lack of iframe-error response to route abort remain retained; no false zero-historical-attempt claim.
- The task4.8 environment serialization incident was a real corrected local receipt defect. Current safe writer emits only six explicit assigned values; the reconstructed executed source clearly identifies the one earlier full-env sink. Historical no-readback/no-transmission claims remain process attestations, not independently replayed history. I inspected no inherited environment values/credentials/unsafe original payload. This does not establish a current product privacy defect or justify rerunning accepted tests.
- Review-only probe corrections: the M1 spec was located in its still-active change after checking the nonexistent published path; a path-hash probe initially collided with PowerShell's read-only PID name and was rerun with pathIdentity; an XML namespace query was corrected; evidence filenames were located after guessed-name misses. These were read-only reviewer inspection errors, not product/test failures. No product/evidence mutation resulted.

## Unified correction and recheck gate

Parent should send F1–F4 together to the single authorized fix worker, then return the exact changed-source manifest, focused tests and affected UI/build receipts to this same reviewer for scoped recheck. Preserve original audit/Q/preliminary failure artifacts. Do not close6.1 until confirmed fixes and recheck are accepted.

F1 is a helper-test fixture correction; F2–F4 are UI wording/focus/presentation corrections. They do not require changing the generator, model/profile, real package, frozen cases, thresholds or original run-binding, so do not rerun or relabel Q acceptance merely to regenerate global hashes. Record the new final-source identity and unchanged evaluated producer/data closure explicitly. If the fix wave instead changes selection, model/profile/data or other Q-producing behavior, stop that expansion and apply the pre-output new12-case confirmatory binding plus S regression requirement before outputs.

Geography remains: automatic outdoor Moscow1–3days now; ordinary M1 other supported cities/1–14days unchanged. A second city is a separately authorized change after Moscow D_demo/S/Q_demo and ordinary CI plus measured maintenance evidence; it needs its own permitted package, compatible contracts and quality evidence. M3/AI is not a prerequisite. No publication6.2, target6.3, archive, cleanup, enablement or further agent delegation was performed or authorized by this review.
