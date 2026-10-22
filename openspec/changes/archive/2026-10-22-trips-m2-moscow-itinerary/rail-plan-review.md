# Independent Rail planning review

Date: 2026-10-21. Reviewer: fresh independent read-only reviewer.
Worktree: C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency.
Scope: additive planning refinement only, not implementation acceptance or final task6.1 source audit.

## Verdicts

**SPEC CONSISTENCY: PASS.**

**PLAN QUALITY / IMPLEMENTABILITY: PASS.**

Findings: **P1=0, P2=0, P3=0**. No concrete correction required before the planned implementation. These verdicts do not close tasks1.5/4.5-4.8, authorize publication/enablement, or assert that future tests pass.

## Reviewed inputs and integrity

Read the exact author brief and review brief, author report, all eight diffs in the supplied package (including both new planning documents and the controller integration receipt), and the input hash inventory. The package was read in sequential chunks; one tool-truncated task subsection was subsequently read in isolation. No planning/product/config/dependency files were changed.

Verified current SHA256:

| Input | SHA256 |
|---|---|
| task-rail-plan-brief.md | 89cfbe28cb69920d120b5d3c5945c9bd5790b7fd32ffbded606a86a3b7842818 |
| task-rail-plan-review-brief.md | 46964d0503bec908d519d393e1b8fe2d218e4d7a8ecb1821d399e445a9085fd2 |
| task-rail-plan-report.md | e97a2821e9ab420e5be1c0dbb502596c04afeb5f86ca92ac72bbe5064667bca7 |
| task-rail-plan-review-package.txt, 120475 bytes | a1be1df22815447688adf2090371b6f3e4fa42f0e564578c727975703a8eb7a4 |
| task-rail-plan-input-hashes.json | dbb6cad906b44134a19daaeaa178fe3c53cd88084e59b70b5d685cc932a444ea |

All8/8 inventory after hashes match the physical planning files. All8/8 before hashes match the preserved pre-sync tree or the declared empty base for new files. Thus the reviewed package/report is bound to the supplied input identities.

Independently recounted12 Rail requirements,35 scenarios,27 task checkboxes and17 checked. Every new exact requirement header appears in the task traceability table; no new task is checked. Reviewed task text and interface ownership, not just header matching.

Protected SHA256 equality verified against the recorded pre-refinement values:

| Protected file | SHA256 |
|---|---|
| specs/trip-itinerary-candidates/spec.md | 94f0b1e4ac3730a1a37d30c12a6978757ac856cad1461e5ec1730e902af2c5b0 |
| quality-cases.json | f50b7511fc12642b20df7ca207c780740382eeb4a482258929551ce29aa5756c |
| quality-protocol.md | bcb608c08f4761c973498186942194ab7d8913ea27f5ccac00ed757bd4489a1f |

Also checked6/6 canonical Moscow Data files and tools/trips/build-moscow.cs plus build-moscow.test.mjs against the pre-sync backup manifest: all unchanged. The manifest includes fixedQAsOfUtc=2026-10-21T14:16:33.0000000Z; the refinement explicitly preserves it and12Q+12S. No Q generator or output production was run.

## Spec consistency evidence

Paths below are relative to the reviewed worktree; refinement/spec/task paths are within openspec/changes/trips-m2-moscow-itinerary unless stated otherwise.

- Retention: rail-refinement.md:13-17 and new spec:58-71 preserve openspec/specs/rail-schedule-search/spec.md:382-395. A real train-derived note would still be supplier data; real results therefore remain transient and automatic stage is fictional-only. The plan neither treats source enablement as persistence permission nor silently substitutes fictional data. No source/demo prerequisite is imposed on real operator activation.
- Scope and authority: spec:9-22 separates departure date from selected existing plan day and keeps Rail's published geography distinct from Moscow walking. tasks.md:20,42-56 and rail-refinement.md:5-11 distinguish accepted historical work, current integration proof, new source work and later publication/real target gates.
- M1 semantics: spec:73-120 requires a clean saved foundation, nonempty append, one atomic local edit, unchanged old data, explicit Save/CAS and exact bounded historical text. Walking remains empty-only. No trusted DTO field, automatic PUT/retry, source-freshness claim after reload or reconstructed Rail authority is introduced.
- Source selection: spec:39-56 and refinement:45-56 define current member/request/profile/epoch/generation, successful reference, absolute non-null click-time freshness and exact admissible limited reasons. The failed-reference/A-B-A cases are explicit.
- Privacy, form and attribution: spec:122-168 and refinement:21-33,77-81 cover teardown, no storage/SSR/telemetry, sibling forms, Enter0PUT, focus, exact money, unknowns and approved iframe/CSP behavior.
- Traceability and sequencing: tasks.md:42-45,86-103 and refinement:83-93 assign coupled4.5+4.6 to one fresh implementer, primary personal review then fresh task review;4.7 has a fresh browser implementer/reviewer;4.8 precedes pre-output5.2;6.1 remains the fresh most-capable full-source audit with unified fixes/recheck. Existing accepted tasks are not silently renumbered or reopened.

Historical research/self-review receipts are explicitly labelled historical. The revised active design/proposal no longer claim that all work is unimplemented or that the entire27-task scope is Moscow-only.

## Narrow implementability checks

| Named risk | Existing source evidence | Assessment |
|---|---|---|
| Source selection and failed reference | apps/web/src/app/rail/rail-state.ts:65-103 retains same-profile reference on some failed/skipped/error paths; criteriaChanged:123-130 invalidates search sequence; dispose:267-278 aborts and clears | Planned narrow eligibility/failure invalidation is necessary and explicitly assigned; retaining the existing class is feasible |
| Safe limited selection | modules/rail/Travel.Modules.Rail.Infrastructure/YandexRailScheduleParser.cs:38-49 produces page-limit/interval-service-unsupported; mode result:127-134 uses those reasons; ticket damage is separate | Allowing an intact selected row for these reasons or its complete mode is grounded in actual contracts; unrelated damaged rows retain the original shorter envelope TTL |
| Strict authority and original TTL | shared/ts/api-client/src/rail.decoder.ts:314-327 validates reception/expiry;400-468 binds mode counts, row reception, result kind and reference expiry, with30s/300s bounds | Plan adds click-time checks and membership instead of replacing the strict decoder or renewing expiry |
| Opaque foundation | apps/web/src/app/trips/trips-state.service.ts:40-55,146-206 stores branded tickets in one WeakMap, monotonic foundationVersion and shared session/read/edit state;227-245 preflights then edits once | Generalizing the clean-saved predicate and retaining the empty wrapper supports nonempty Rail append without a second CAS machine. Pending-read rejection remains an explicit implementation requirement; current code is not asserted to satisfy new Rail guards already |
| Actual full-write limits | shared/ts/api-client/src/trips.decoder.ts:47-59,139-179,188-193,351-354 provides scalar/control validation,8/day, duplicate IDs, exact money/null time rules and131072-byte JSON bound | One prospective full write through decodeTripWrite can enforce the plan. Optional title-number fallback and essential-note rejection are precise; no impossible promise that maximal Rail refs always fit500scalars |
| Extraction, forms and attribution | rail-page.component.ts:20-76 has browser transport, allowlisted frame and CSP/dispose logic;93 has global focus query. rail-page.component.html:1-26 has main/H1/form/banner. trips-page.component.html:166-704 contains the M1 form | Planned standalone wrapper, local focus query and sibling component directly address observed boundaries. Standalone regression and actual browser Enter0PUT proof are assigned, still pending |

These are plan feasibility checks, not a second whole-source review. Future implementation must demonstrate the listed guards and tests; this review did not infer their success.

## Receipts verified without reruns

- Live HEAD is a8721effd9056459d217966083816455fbea599d. git merge-base --is-ancestor e654c76915103a82083ef2abc01c0579117c9749 a8721effd9056459d217966083816455fbea599d returned0. No branch operation performed.
- backup-manifest.json SHA256 edec135a552a79c24eef5e6760540680ca818684dfe10bd333062d9f930f54b5 matches the integration receipt. The historical695-file/139-WIP preservation operation was not repeated.
- Current package.json and package-lock.json hashes match rail-dev-integration.md. Dependency consumer log b069ccb197912d63e658dec66ce6cd110c1f7cd7f9421750bd0d547683aa30d4 shows7/7, validator log189d3142765d13de1ea7c9d341dc3be1f4fc5449cce0587e11efca855888259d shows15/15, local security log cf67f0852c9b3902027277213d39ada598bb4352de1324a5539dadfc8f5ce3c1 shows60/60; each has0fail/0skip.
- Physical copy manifest hash37535d5d35fb050a2984ade4a2ae295aac4bedaaeb6a0fe7064632f79f2c263c matches the controller receipt. The59,458-file installed tree was not rehashed by this reviewer.
- Earlier artifact command log hash0b2fba78c4274f4354aaa53318546c08a8ef54e3e579812155b7ca61f30702d3 matches; it explicitly says LOCK/ARTIFACT ONLY and records18root/4OpenSpec audit carrier entries. This is not installed-runtime or full current network/CI acceptance.
- Read retained pinned validation JSON: change1/1, issues=[]; corpus15/15. Hashes: change-strict-final.log=d7469423f2b29c547fdef782a825f46dd929fa80f3e0948776a271d0d7a0babc; corpus-strict-final.log=de5df2247ffc710df25ac7f2c81c856cc6828e10bda6d90d80a5357120de8cdb. Validation was not rerun.

## Limits and remaining work

Only this report was written. No subagents, product/planning edits, tests/builds, network audit, provider/model calls, Host/DB work, Q outputs, git mutation or external-system mutation occurred.

The later full npm audit was previously rejected by automatic approval review for dependency-metadata egress; human permission remains pending according to the review brief. Nothing in this review retries it or converts the earlier artifact invocation/local suites into full audit acceptance.

Source work may proceed in the already authorized planned order after the controller's own task1.5 acceptance. New Rail implementation, component/standalone/browser regressions, current build/structural checks, Q5.2 and final6.1 review remain outstanding. No real-source, human-route, publication/CI, target or rollout success is claimed.
