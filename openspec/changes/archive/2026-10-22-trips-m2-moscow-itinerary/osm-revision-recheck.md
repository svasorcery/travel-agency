# Independent full-corpus recheck: Trips M2 offline OSM revision

Date: 2026-10-21. Reviewer: independent Codex reviewer from the original v2 planning review. This recheck evaluates the complete recovered planning corpus, not just the R1–R3 patch and not an implementation.

## Verdict

**Planning recheck passed. R1 closed; R2 closed; R3 closed. New confirmed findings: P1=0, P2=0, P3=0.**

The recovered v2 design is coherent with the current proposal/spec/tasks and retains the relevant M1 owner, revision, privacy, API, adoption and quality boundaries. It is sufficiently specified to begin the already authorized bounded source work without guessing the three material data-build decisions identified by the earlier review. This conclusion grants no new execution, publication or target authority.

The scope remains suitable for a demo project. It adds one nontrivial but bounded offline data-preparation helper; the application still reads a small immutable package. No runtime routing platform, map UI, additional database, provider API or LLM is required. The result can demonstrate real geography, documented mathematical walking estimates and explicit private-plan adoption. Human travel suitability/calibration is deliberately a later claim, not a condition invented by this review for technical demo completion.

## Reviewed recovery and scope

Reread every line of current `proposal.md`, `design.md`, `specs/trip-itinerary-candidates/spec.md`, `tasks.md`, `data-strategy-revision.md` and `quality-protocol.md`. The source research and frozen cases were fully read during the preceding review and their hashes were verified unchanged here. Root/Trips instructions and actual v1 Core/Composition/request/response middleware/TestOnlyGuard were previously inspected; their hashes also remain unchanged. The three current Core/test hashes match the retained `core-facts-review.md` receipt.

Current HEAD remains `e654c76915103a82083ef2abc01c0579117c9749`; working-tree plan bytes, rather than HEAD alone, are the reviewed artifact. Current recovered design is 56,133 bytes, SHA-256 `431b113575ec9350e98614dda3c9f42561d557d05561f1cba309129c2120038d`.

The storage incident is explicitly recorded at `data-strategy-revision.md:46` and the resulting artifact at line 48. The current design is a reconstructed full-text version, not a byte-identical restoration of the prior `b983...` v2 design. This recheck accepts the new inspected text; it does not use the old design hash as evidence for it. Cleanup/recovery execution itself was not independently replayed. The unchanged source hashes support source preservation; they do not establish that any test was rerun after recovery.

No source/product edit, package generation, map download, test/runtime execution, Host/database/model/provider call or Git mutation was performed for this recheck. Only this report was written, using a free-space check and a same-directory temporary file followed by a move. The historical 72/72 v1 result remains historical; required v2 adaptation is still pending.

## Resolution of the original findings

| Finding | Current evidence | Result |
|---|---|---|
| R1: profile and crossing counter | `design.md:48`–`59` fixes bbox clipping, highway allowlist, access precedence, unsupported/conditional tags, node barriers, foot direction and distinct eligible crossing-node counts including endpoints. `design.md:63` and `69` fix geometry rounding and integer minute arithmetic. `tasks.md:19` and spec scenarios at lines 57–59 retain the corresponding fixtures. | Closed at plan level. Unsupported data fail/exclude; road/foot direction is not guessed, and node-plus-way crossing representation cannot double the allowance. |
| R2: capped directed-pair selection | `design.md:65` fixes source ordering, eligible target filtering, distance/ID ordering and at most five outgoing targets per source. Upper-bound proof is 48 × 5 = 240, within 256. Eighteen fully eligible connected anchors produce 90 selected pairs. Reverse directions are computed independently and shortages are not repaired. `tasks.md:19` and spec lines 53–55 cover pair-count/order fixtures. | Closed at plan level. The 90-pair fixture must of course use targets satisfying the declared profile/anchor/120-minute eligibility, as required by the selector. Actual coverage is still measured separately. |
| R3: validity clock and reproducibility | `design.md:40`, `44`, `79`–`83` binds publication metadata from the same successful download to exact input SHA, fails absent/invalid/future publication, requires ordered frozen UTC metadata, fixes the model lease to publication + 30 days and checks the complete requested Moscow local day. Rebuild/read cannot renew it. Digest-affecting metadata are frozen inputs; execution measurements are a separate receipt. `tasks.md:19`, spec lines 61–63 and applicability tests in task 2.1 cover this. | Closed at plan level. Publication is explicitly an extract-publication date, not a field observation. No retrieval/build-time fallback silently renews an old map. |

The HEAD metadata described at `design.md:83` is explicitly feasibility evidence only. This reviewer did not repeat that HEAD request. It cannot become the future actual-download binding; the design correctly requires metadata from the download that supplies the hashed bytes.

## Full recovered-boundary checks

| Boundary | Evidence in the current recovered corpus | Assessment |
|---|---|---|
| Scope and architecture | Design lines 13, 19, 34, 42, 184–198, 251–257; proposal and tasks 1–4 | Moscow/1–3 days/1–9 adults, offline helper and immutable runtime package remain bounded. No new persistence, host builder or cross-module internal dependency appears. Manual M1 remains 1–14 days. |
| Sources, rights and uncertainty | Design lines 38–44, 69–89; spec lines 20–83; tasks 1.2, 1.4, 2.1, 3.1 | Exact Wikidata revisions, permitted structured fields, OSM input/path identities and licences remain required. Modelled estimates keep nullable observations/human actors and not-evaluated calibration. No borrowed provider minutes or invented empirical interval appears. |
| Reproducible release and offline bounds | Design lines 44–81; tasks 1.4 and 3.1 | Input/expanded/graph/wall limits, XML entity prohibition, fixed names, package/evidence caps, byte hashes and explicit failure survive. No full graph enters runtime. The final inventory and graph/caps still require a real build; the plan does not claim success in advance. |
| Public licence offer versus private plans | Design line 85; spec lines 68–70; tasks 3.1 and 4.3 | A versioned static offer comes from canonical public source artifacts and is checked independently of editable notes. No private fields or new dynamic API are introduced. Public distribution remains separately authorized. |
| Owner/revision/CAS | Design lines 93, 117–119, 168–176; spec owner/adoption/session requirements; tasks 3.2 and 4.2–4.4 | Owner-first authoritative reads, exact If-Match, empty saved plan, final revision reread, guarded local epochs and ordinary explicit M1 PUT remain. No hidden save, rebase, retry or receipt workflow is added. |
| HTTP and TS integration | Design lines 126–164; tasks 3.3–3.5 and 4.1 | Candidate POST has its own raw 1 KiB empty-body policy; availability bypasses only plan readiness after authentication. Disabled 404 is pre-auth. Real middleware-order, typed status, chunked-body, strict decoder and JSON-vector tests remain. Host order stays unchanged. |
| Adoption and privacy | Design lines 145–182; tasks 4.2–4.4 | Owner GET and availability GET are both required, with one adoption ticket and a final expiry/clean-state check. Package switches/failures and late edits/logout cannot overwrite the buffer. No browser storage/SSR/private cache or diagnostics leak is permitted. The later rollback race is explicitly best effort. |
| M1 compatibility | Design lines 174–180, 253–257; spec historical intentions and isolated generation | Saved output remains ordinary editable blocks, null initial money/times and a bounded historical note. No hidden trusted fields enter v1 snapshots; no key/schema migration or forced plan rewrite appears. Candidate failure leaves M1 manual readiness intact. |
| Demo versus human/live acceptance | Design lines 200–222; quality protocol; tasks 5.1–6.3 | D_demo/S/Q_demo plus independent source audit are separate from CI/target/rollout and D_live/Q_live. Frozen 12 Q requests and 12 S cases, pre-output binding, full denominators, 11/12 stop target and confirmatory-set rules remain. Human scores are neither generated nor waived into a pass. |
| Geography expansion | Design lines 224–239 | A second city remains a separate change, including source evidence and explicit candidate/availability compatibility. Catalogue version/snapshot/TS compatibility cannot be bypassed by simply adding city labels. |

Production exclusion remains a required implementation/test boundary, not a newly proven runtime fact. The real TestOnlyGuard inspects registered implementation types and fails Production violations; the future Composition tests must also preserve the stated manual-M1 behavior. No v2 Composition code currently claims to implement that behavior.

## Remaining evidence, not planning defects

Real input download and its matching metadata, 18–48 accepted POIs, at least 60 eligible selected paths, coverage after nearest-five selection, five-minute/resource caps, package hashes and licence-offer delivery are not yet measured. No modelled-demo source or end-to-end run has passed. The planned fail-and-revise behavior is appropriate if these constraints are not met; enlarging scope or fabricating edges is not an accepted workaround.

The v1 Core requires the explicit v2 method/rights/timestamp adaptation in task 2.1. Actual source implementation, all new tests and the requested independent whole-source review remain necessary. This report closes only the planning findings.

## Exact memory provenance

The earlier review did read the actual registry `C:/Users/Vladimir_sva/.codex/memories/MEMORY.md`, not just the supplied summary. Relevant relative ranges were `MEMORY.md:28-30` (Moscow choice, independent review/recheck and authority boundaries) and `MEMORY.md:34-38` (historical M1 HTTP, immutable identity, human-quality and geography guidance). The corresponding rollout ID is `01a15aa3-f306-7b21-a017-e16f34c0ec03`, identified by `MEMORY.md:10`. No rollout-summary file was opened. These historical notes were orientation only; current repository text and previously inspected primary documents ground the conclusions. Historical v1 geography/human gate wording was not treated as overriding the user's newer demo revision.

## Exact reviewed snapshot

SHA-256 values below cover exact bytes; paths are repository-relative.

```text
8354f626b7ef338ad62bf9d356261057dae5ff5c937d5a11125a8aae917d19b4  openspec/changes/trips-m2-moscow-itinerary/proposal.md
431b113575ec9350e98614dda3c9f42561d557d05561f1cba309129c2120038d  openspec/changes/trips-m2-moscow-itinerary/design.md
69ac179f6f987d91fa16844ccd66f6c1819e9586b66da0a18a8863b7ab2f4eaa  openspec/changes/trips-m2-moscow-itinerary/specs/trip-itinerary-candidates/spec.md
ffc20a0001a0c8c2cbaab0eeb5d22c92755e3d9501dc6fa2f37a5370a6d8c9f7  openspec/changes/trips-m2-moscow-itinerary/tasks.md
01e263b4e3b9b79fba36886e4705300d54a66d228576582bc7fa6e0f12022f3b  openspec/changes/trips-m2-moscow-itinerary/data-strategy-revision.md
f0c0f4528d841fc61c78b9e2b2bda4c04543d477da5b6a285eee327f17fafc29  openspec/changes/trips-m2-moscow-itinerary/data-source-research.md
384a6458b15c22d4ebe2aa3885842749344f0e02a46a15fb90429a55b0f21aca  openspec/changes/trips-m2-moscow-itinerary/quality-protocol.md
f50b7511fc12642b20df7ca207c780740382eeb4a482258929551ce29aa5756c  openspec/changes/trips-m2-moscow-itinerary/quality-cases.json
a44913b75730caf9158c489b44acec98b08d5375fc305fce1029c2c2b7542044  openspec/changes/trips-m2-moscow-itinerary/osm-revision-review.md
05b4548169f05ab545c8bcb499966c0575dfea87ce2e67f47692b94494c18073  AGENTS.md
e5492e295c8c16d9710bf5fff8d835eae6f2f0897e8746f130252b1b284de71b  modules/trips/AGENTS.md
da933d5b833763ea2f64888d550e0acb408b47dda038e42ac0d94e2a4cdabb54  modules/trips/Travel.Modules.Trips.Core/Itineraries/ItineraryFacts.cs
2de734de520f3c4ae0156a026b0abf2188d16aa95236ee9fef7aec473dc6372e  tests/trips/Travel.Modules.Trips.Tests.Unit/ItineraryFactsTests.cs
d91e39af78213149dedcffcce865bd7d9b37530de52e1d07b5c6b039127c1f4f  tests/trips/Travel.Modules.Trips.Tests.Unit/ItineraryTestFacts.cs
6d4127431db48452f4494e9fd8859cd4a824a21c8fdbdb9e3e8238beb42b774c  modules/trips/Travel.Modules.Trips.Api/Composition/TripsModule.cs
83a7189f961c7bb0c446b211a39c2c0a87e6a7f018a2900c1ae38fa2aec01971  modules/trips/Travel.Modules.Trips.Api/Middleware/TripsRequestGuardMiddleware.cs
7ad711982e2fc081aba8aac1fecc84c3a0995009342d4004346e0aebd429e2a4  modules/trips/Travel.Modules.Trips.Api/Middleware/TripsResponsePolicyMiddleware.cs
f55709c816405fd4265686b369ab2bbe21ff2f1bb889035f2dc87803512eb457  apps/Travel.ServiceDefaults/Hosting/TestOnlyGuard.cs
```
