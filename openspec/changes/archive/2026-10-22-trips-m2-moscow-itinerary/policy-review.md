# Task 2.2 independent policy review

Date: 2026-10-21. Scope: task 2.2 only; source remains frozen for the correction round.

## Verdicts

- Specification review: **CHANGES REQUIRED**. One P2 finding below.
- Code-quality review: **CHANGES REQUIRED** for the same produced-note omission. No separate algorithmic, immutability or maintainability defect was found in this bounded review. Do not count the same finding twice.
- No P1 or additional P2/P3 findings. This is not whole-M2, real-data, Q_demo/Q_live, runtime or enablement acceptance.

## P2: Preserve required OSM/ODbL credit in the initial historical note

Location: `modules/trips/Travel.Modules.Trips.Core/Itineraries/ItineraryPolicy.cs:169` (note construction, lines 169-172); test coverage location `tests/trips/Travel.Modules.Trips.Tests.Unit/ItineraryPolicyTests.cs:39` (note assertions, lines 39-45).

The produced block note is only `Historical proposal: <dataset>/<version>; asOf=<instant>; POI=<id>.` It contains no OpenStreetMap credit or ODbL licence reference. Canonical design section 2.4, `design.md:85`, explicitly requires credit/licence reference in both preview/footer and historical note. Section 4, `design.md:130`, makes this the proposed ordinary block note. The policy owns the initial ready-to-transfer note; the parent confirmed that no later note augmentation is allocated. A separate UI footer therefore cannot satisfy the retained-note requirement when this text is adopted and saved through M1.

Required correction: include the bounded original OSM attribution and ODbL licence reference in the policy-produced historical note, retain dataset/version/asOf/POI identity, and add a semantic assertion that the generated note retains that credit/reference while staying within M1's 500-scalar bound. Use a fixture with maximum valid identifier lengths to substantiate the resulting bound. This finding is about the accepted product contract; this review makes no independent legal-compliance determination.

## Verified implementation properties

The complete new-file diff package was read, with chunked continuation after initial output truncation. Actual delivered files were checked against that package without rereading changed source for review. No Git command or source mutation was used.

- The score counts the distinct requested supported-interest union across already selected days and the current path. Food stays unmet; empty interests create no preference. Stable ordinal POI iteration occurs before beam sorting and truncation; the exact tie order is interest count, filled slots, lower upper-walk sum, ordinal POI sequence. Beam width is 16. Best already discovered paths survive pruning/work exhaustion.
- Pace targets 2/3/4, walking caps 60/90/120 and dwell-plus-walking caps 240/360/480 are applied directly. Dates ascend; selected POIs cannot repeat within a path or across days. Every supported requested date is emitted, including an empty date. Edges remain directed and require the scoped validated evidence; missing/reverse/expired connections are never repaired.
- The request-global counter counts each attempted start/next-POI extension, including rejected attempts. With 48 POIs, three extension levels, beam 16 and three days, the independently checked ceiling is `3 * (48 + 3 * 16 * 48) = 7056`, below the defensive 20000 limit. The current accepted design/task expressly uses that proof plus maximal-input/global-counter/cancellation checks rather than an unreachable exhaustion fixture. No expansion-unit change or global-optimum claim was accepted. The defensive search-limited branch is source-inspected, not empirically exhausted.
- Package/build/current-model validity is checked before constructing any Core outcome. Normalized destination ID, IANA zone and catalogue version must match. Horizon uses the supplied saved zone and explicit UTC asOf; no Moscow branch or clock read is in Core. Fact knownness requires current applicability and whole requested-local-day applicability. Access unknown and known closed remain distinct; expired access becomes explicit unknown, while stale identity/walking cannot supply a stop/connection.
- Candidate, Empty, Unsupported and unavailable ErrorOr results remain distinct. Coverage is Filled only with every target and requested interest covered and no search limit; access unknowns remain visible independently. Origin/budget exclusions are explicit. Generated exact times and money are null; original titles and selected fact/model/path/right metadata are retained. No feasible, calibrated, human-reviewed or within-budget assertion is added.
- Output classes are get-only/sealed; every list is defensively copied into a read-only collection. Their public constructors deliberately permit cross-field-invalid test objects for the independent task 2.3 validator; that permitted construction is not a defect here. Nested reused facts/model/source/preparation are validated immutable task 2.1 values. UUID generation is excluded from route ranking and deterministic-route comparisons.
- Cancellation is checked at entry, day/eligibility loops, every counted attempt and before a result is returned. The fixture proves pre-cancellation only; no measured in-flight deadline acceptance is claimed. Application still owns admission/deadline orchestration.

## Evidence and limits

Binding inputs: task-2.2-brief.md, task-2.2-interface.md, task-2.2-report.md, task-2.2-review-package.txt, current canonical tasks 2.2, design sections 3-4 and the related note requirement in section 2.4, scope/freshness/ranking/outcomes/validation/no-effect specifications, root/module AGENTS.md. Targeted outside-source checks were limited to task 2.1 fact applicability/tag helpers and normalized TripBrief to assess named cross-risks. No broad crawl, real package, frozen quality case or real-quality output was opened.

The report identifies base/HEAD `e654c76915103a82083ef2abc01c0579117c9749` and branch `codex/trips-m2-moscow-itinerary`; these are supplied review basis, not independently re-verified through Git in this review.

Delivered SHA-256 hashes matched the worker report, and all three source/test files matched the complete supplied diff package:

| File | SHA-256 |
|---|---|
| ItineraryCandidate.cs | 57b52398c7dbcfaef2e58af9c87ce9c8c49429e9482ce46a69d488983a4310b2 |
| ItineraryPolicy.cs | 95cb0a607df76affcd21eb02a0a95aae0f1ffa058dcf6351321669ac5f139f3f |
| ItineraryPolicyTests.cs | 8c0440c62b422a7102743a8935beb52222162b55c0c84a2bcfce0389599146b2 |
| task-2.2-interface.md | afa6f1f71a676fccc398d702166ecac6543a3527171beba08a84699bede4d572 |
| task-2.2-report.md | 1307ffda11a0087fee7c658ba88ec2da784c713ca80c20ef83d9d12da7a38849 |

Worker-reported verification: full Trips Unit 135 passed / 0 failed / 0 skipped (99 baseline + 36 policy cases), plus CSharpier check of three files. Test structure and stated semantic coverage were inspected; delivered hashes were verified. Original raw final test/format output is not persisted in TestResults/trips-m2, as confirmed by the parent. Therefore this review does not elevate the report/tool-output claims into independently inspected raw receipts, and did not rerun suites while source is frozen. The parent plans a fresh full unit run with TRX in the correction round; that will be new evidence and must not be called original RED proof. The report itself distinguishes the initial semantic RED, one later expiry RED, and additive tests without prior RED.

Not verifiable in task 2.2 alone: independent output rejection (task 2.3), Application Moscow scope-before-loader/owner/revision/admission/clock handling, post-computation expiry recheck, complete wire size/M1 payload bound, browser attribution/footer/adoption/private-state behavior, real-source D_demo construction, Q_demo binding/outputs, human quality, ordinary CI or target rollout. These remain later scoped acceptance items, not presumed passes or extra task 2.2 defects.

## Review effects

Read-only source review. Only this report and its canonical copy were written via same-directory temporary files and atomic File.Move after checking C: free space (3031273472 bytes before report writes). No suite, Host, database, network, provider, model, Q output, Git mutation or feature enablement was run. Default-off boundaries remain unchanged.
