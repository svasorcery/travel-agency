# Same-auditor scoped recheck — Trips M2 task 6.1

Date: 2026-10-21. This resumes my original whole-M2 audit. Scope is F1–F4, breakage introduced by their exact nine-path fix, and the six parent-owned documentation changes. The broader original audit is retained, not repeated.

## Verdict

**F1 ADDRESSED. F2 ADDRESSED. F3 ADDRESSED. F4 ADDRESSED.**

**SOURCE / LOCAL DEMO READINESS: PASS. SPEC: PASS. QUALITY: PASS** for the approved source/local demo scope, combining the original whole-source inspection with this scoped correction recheck.

**Remaining findings: P1 0; P2 0; P3 0.** No new confirmed defect from the fix or documentation delta requires another correction. The existing disclosed CSS warning remains the original set-aside; it was neither fixed nor waived by changing a budget.

The parent may accept task6.1 and perform the described nonexecuting acceptance bookkeeping. I have not changed its marker. Publication/ordinary CI6.2 and real target6.3 remain unchecked and unauthorized. This PASS does not assert real walking suitability, calibrated times, real Rail/banner/rights/quota, enabled Host/Keycloak, target or rollout acceptance.

## Finding-by-finding recheck

### F1/P2 — ADDRESSED

tools/trips/build-moscow.test.mjs:270 now obtains invalid publication from futurePublication(manifest.downloadedAtUtc); :285 adds exactly86,400,000ms. The fixture's optional fixtureDay binds downloadedAt, identity retrieval and the helper's asOf input to the same UTC instant. Thus the formerly fixed22October value remains one day later than the fixture clock instead of becoming valid as time advances.

The regression at :289–302 checks21/22/23October2026,1January2027 and29February2028. It asserts the actual fixture timestamps and positive one-day delta. The existing actual CLI rejection/no-published-output test still uses this function. This is sufficient combined boundary evidence; the five-date test does not falsely claim five CLI executions. Production helper and immutable Data/freshness rules are unchanged.

Read original RED (reported date-delta failure), focused correction and final helper source/receipts. Actual final helper stdout records16tests/16pass,0fail/cancel/skip/todo; final result exit0. No helper was executed by this reviewer.

### F2/P3 — ADDRESSED

apps/web/src/app/rail/rail-search.component.html:24 now changes only the provenance label: strict fictional profile renders "Учебная цена", real renders the existing "Цена по данным Яндекс.Расписаний". Exact amount/currency, unknown scope and independent seat/realtime/IANA notices remain. Source profiles/DTOs, persistence and real-view-only guards are unchanged.

The actual shared-component test at rail-search.component.spec.ts:290 covers both strict decoded profiles, including the approved official frame URL for real, exact9007199254740993.05RUB and the absence of a real-data claim for fictional. Both desktop/mobile browser assertions verify the fictional label; I independently viewed both new Rail detail PNGs at original resolution. The visible price and demo notice agree.

### F3/P3 — ADDRESSED

apps/web/src/app/trips/trips-page.component.ts:341–354 carries an availability focusCurrent closure into the existing focus helper; it rechecks captured context, draft, brief, exact availability object, terminal status and no pending operation at actual render time. :364–370 does the equivalent for generation using the existing captured empty foundation, exact preview/status and no pending operation. Existing focusIntent and destroyed guards remain in the actual afterNextRender callback.

These closures remove the gap between the earlier successful check and eventual focus. They introduce no ticket/counter, write authority, API call or new revision machine. The prior adoption-error continuation at :375–381 and its owning-state guard are unchanged.

Read all15 added deterministic cases at trips-page-rail.component.spec.ts:708 onward: generation error, availability error and generation success crossed with unchanged/title edit/brief edit/direct dismissal/newer focus intent. Tests retain the production scheduling callback and evaluate the supplied validity closure at the render boundary; they require boundaryReached and assert actual DOM focus, including positive unchanged-context focus and newer delete-confirmation intent. They do not simply test a copied predicate or rely on sleeps. Fresh focused111/111 and fullweb864/864 receipts support these source checks.

### F4/P3 — ADDRESSED

apps/web/src/app/trips/trips-page.component.html:372 now has one concise visible demo/uncalibrated/no-human-review/feasibility warning. The immediately following covered/unmet interests remain. Only the duplicate unmet-interest reason and three uncertainties already expressed by the summary are omitted from the repeated list; relevant exclusions and unknown hours remain. :427–429 retains software preparation/build timestamp, null-human disclosure and exact mathematical assumptions inside the existing data details.

Per-stop unknown access, per-edge "Оценка по карте … Без полевой проверки", source/licence links, exact reproduction metadata and explicit transfer/save explanation remain. No API facts or CSS changed.

Read the added DOM test at trips-page-itinerary.component.spec.ts:549 and the exact browser change. The DOM test removes details from a clone before checking visible-copy absence, then asserts the metadata still exists within the closed details. The browser opens/closes that same details control after the original keyboard source-link sequence; the original native keyboard assertions remain. I viewed both new walking preview PNGs at original resolution: the route is easier to scan, the builder identifier is absent from the main flow, mandatory uncertainties remain and no new clipping/overlap is visible.

## Exact scope and identity

Read the recheck brief first and verified its corrected SHA256:
31b8f8d9a30ddeaddb46dfdcb4011f93d38eebe5e66b6367b58bed1227b266aa.

Original audit remains cfe7488c72b3e4ce42b1d6949387303561dfef6ec18b148a29653e1802c368a7. Fix report15eb6d43aecfa996a64085f0f0d51231d97642b426dc9fe127ab5c203f6a08c7. Read the complete392-content-line nine-path patch9f9c19cf7bf3de50d1b63b4ac44d2fe6a4108e1c12996be80ed7891f553813f6, including every Rail/UI/test hunk. Bounded follow-up reads covered material omitted by an initially combined/truncated display.

Independently rehashed all9saved before files and all9current files against source-final-sealed.json b63aa5fb8af26bd8a519cd3d23a6e23736efd0919018e7fb84f3b0f5dc0c2bee. The base is the original uncommitted184-file audit corpus, not HEAD. Only three runtime files changed: Rail HTML and Trips TS/HTML. The other six are tests.

Read the entire parent documentation patch290752542c647f2877d32ddb9bc733b8ed4bdca8caca23760afcaacbb4ca2a26. It accurately separates the completed fix wave from this pending recheck, retains24/27before acceptance, distinguishes fresh75walking/Rail browser cases from the historical5M1 cases, and keeps Q/CI/live gates separate. New canonical audit/fix report copies are included in the current feature closure. Older ledger entries and earlier report inventories retain their historical timestamp/ownership scope; they are not substituted for the final closure.

Independently verified the parent closure receipt hashdd0c3fc0d9d3923b7d704e66655e70d8bfa5892ab2b03b90efdaf92516340a9b and all186current feature path hashes. Current pre-recheck/pre-bookkeeping tree:
0e30b597e13fb08f824780e2416a3ef9a671617e2868828ecad0a0455c628c24.
Original184 comprises169unchanged,9fix source/test paths and6declared prose changes;2new canonical report copies make186. This identity excludes this new report and future acceptance bookkeeping.

Requested basee654c76915103a82083ef2abc01c0579117c9749 and integrated upstreama8721effd9056459d217966083816455fbea599d remain distinct contexts. Branch/ancestry/index evidence is reused from the supplied unchanged receipts; I performed no Git operation.

## Actual retained proof checked

- Verified all four seal references and every one of359artifact entries by SHA256 and byte length, including raw attempts, originals, patches, browser evidence and all75new production artifacts. Artifact manifest SHA256e4a026ce1e84e831956610af1ee5279d2b887acce0d4fb7cf625a72a35f574dd.
- Read actual final raw stdout/result exits: focused111/111(3files), helper16/16, fullweb864/864(57files), exit0. Fullweb is one run; repeated preliminary focused failures are preserved, not added into green denominators.
- Independently parsed actual Playwright JSON:75unique Trips specs and9unique standalone specs, each one passed result/retry0. Checked all75individual Trips ledgers against raw result IDs: passed/retry0/repeatEachIndex0, no failure or external attempt;3668page requests and989API calls. The proof's137Rail/15explicitPUT counts remain separate from source-selection actions.
- Rehashed four reviewed PNGs through the artifact inventory and independently viewed walking-preview-desktop.png, walking-preview-mobile.png, rail-picker-detail-1280.png and rail-picker-detail-390.png under TestResults/trips-m2/final-fix-browser-a872-v2.
- Actual production command is one cache-skipped build with an explicit fresh D output; exit0. Counted75actual files, compared allsixnew public assets directly with canonical bytes, and read actual prerendered-routes.json: only /, /hotels and /hotels/reservations. No private prerender is inferred. Raw stderr retains Trips5.01kB/Support4.24kB/Flights4.50kB warnings; CSS/budgets unchanged.
- Independently rehashed all98explicit unchanged Qproducer paths and all718original Qbinary files. The original cases/protocol/recipe/recorder, generator/Core/Application/API/Infrastructure/Data, production helper, strict TS decoder/types/transport and actual-Q checker remain unchanged. Current186closure also binds the original three generated raw-Q fixtures.
- Read preliminary failure explanations and retained hashed artifacts: the wrong real-fixture URL, closed-details ancestor assertion, failed LF edit/unchanged repeat and new-click browser ordering failure were corrected in test setup/assertions, without a product workaround or lowered threshold. The final75run is freshv2, not a retry rewritten overv1.

No suite, helper, generator, Q/S, actual-Q Node, build, provider/model, DB/Host/key, network audit or install was run by this reviewer. All executable results above are retained outputs checked read-only.

## Reuse and remaining boundaries

Original full-source audit supplies the unchanged wider review. Accepted294Unit/98loader/85API+30M1HTTP,208Architecture,356ordinaryNode and local dependency proof are reused for their unchanged closures, not rerun or presented as new tests. Prior80browser and its5M1 subset remain historical; current evidence is75walking/Rail plus9standalone and864web.

The first bound Q_demo remains1Fact/24records,12/12filled targets and13Node checks for its original evaluated producer. The original1657source-manifest hash is a historical superset with declared later UI/test/prose changes, not a current global hash. These fixes do not change Q-producing behavior; no confirmatory Q rerun, new holdout or retrospective binding is needed or claimed. Human/calibration/live acceptance remains pending.

The original CSS warning and CSP/official-banner evidence limitations remain explicit. This recheck introduces no fresh broad audit, new permission gate or blanket demo waiver. Only this new assigned recheck report is written; original audit, product source, tests, task markers and historical artifacts are untouched.
