# Independent review: Trips M2 offline OSM plan revision

Date: 2026-10-21. Reviewer: independent Codex agent, fresh task context. Review target: v2 planning revision, not implementation acceptance.

## Verdict

**Revise three bounded data-build decisions before dependent implementation. Confirmed findings: P1=0, P2=3, P3=0.**

The selected direction is suitable for this demo. It demonstrates real place identities, source-derived graph paths, deterministic itinerary selection, private preview and explicit M1 adoption without adding a runtime router, database, map frontend, paid provider or LLM. A small offline helper is a meaningful additional component, not a trivial data conversion, but the stated scope does not inherently grow into a routing platform. Keep the explicit stop-and-revise rule if real input cannot satisfy the caps or coverage. There is no evidence yet that it will satisfy them.

The three findings below require explicit local rules and fixtures, not GraphHopper/OSRM, production calibration, additional runtime infrastructure or invented human acceptance. Once corrected and rechecked, this is an appropriately bounded demo plan.

## Scope and evidence

Read the complete canonical proposal, design, delta spec, tasks, data-strategy revision, source research and quality protocol; also read the frozen quality cases. Consulted root and Trips AGENTS. Checked actual v1 Core evidence factories and the Trips Composition/request/response middleware, global SDK pin and shared TestOnlyGuard. HEAD at review: `e654c76915103a82083ef2abc01c0579117c9749` in `C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency`. Working-tree planning/source additions are not represented by that HEAD alone; exact bytes are bound below.

Historical v1 plan reviews and the recorded 72/72 Core tests are not v2 approval. No map/input download, helper execution, tests, Host, database, model/provider runtime, Git mutation or product edit occurred in this review. The only write is this report. Previous memory was used only to orient the historical M1 boundary; current files were reread for findings.

Read-only primary checks during review:

- [BBBike Moscow extract listing](https://download.bbbike.org/osm/bbbike/Moscow/) currently lists OSM XML gzip as 155 MB. This supports availability/format only, not parse time, graph eligibility or coverage.
- [Microsoft file-based apps](https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps) supports the proposed single-file .NET helper form. The repository pins .NET 10.0.203 with latestFeature roll-forward.
- [OSM foot](https://wiki.openstreetmap.org/wiki/Key:foot) and [OSM crossing](https://wiki.openstreetmap.org/wiki/Key:crossing) establish that access and crossing tags have distinct semantics; they do not define this project's chosen subset. The separate sidewalk page timed out and was not used as evidence.
- [ODbL 1.0](https://opendatacommons.org/licenses/odbl/1-0/) supports retaining notices, attribution and the planned derivative-data/reproduction obligation. This review does not decide that a resulting database is exempt from share-alike. The plan correctly makes no such exemption claim.

## Confirmed findings

### R1 — P2: Pin the pedestrian profile and crossing counter before deriving trusted model inputs

Location: `design.md:48`, `design.md:52`, `design.md:54`; owning task `tasks.md:19`.

The selected profile admits other highway ways with “explicit mapped foot permission/sidewalk”, says to account for node barriers/access and excludes unsupported restrictions, but never defines accepted tag values or the barrier/default precedence. The formula then consumes `crossingCount` without defining which nodes/ways constitute one crossing, how duplicate representations are counted, or whether path endpoints count. These are material data semantics: the same map can produce different eligible paths and different upper minutes depending on the implementer's interpretation. Those minutes directly decide per-day eligibility and candidate ordering. A version string alone does not settle these decisions.

A concrete counterexample is a route with one `highway=crossing` node on a `footway=crossing` way: counting both tagged elements and counting only the node produce different upper minutes. OSM documents crossing tags on both kinds of elements; `crossing=no` explicitly does not denote a usable crossing. “Mapped crossing count” does not specify this distinction. Similarly, a barrier node without an explicit foot tag has no chosen default in this plan.

Minimal correction: add one small versioned table defining accepted way/access/direction values, node-barrier defaults/precedence, relevant conditional keys and reject/unknown handling. Define one deterministic crossing counting rule with deduplication and endpoint handling. Add tiny synthetic fixtures for these exact choices. It is acceptable to choose a deliberately narrower subset and fail coverage; do not add a general OSM profile engine.

### R2 — P2: Specify which directed anchor pairs enter the capped package

Location: `design.md:34`, `design.md:50`, `design.md:52`; related bounds `specs/trip-itinerary-candidates/spec.md:29` and build task `tasks.md:19`.

The package is capped at 256 directed edges, but the helper input contains a POI inventory and the design computes shortest paths “between valid anchors” without a pair selection rule. Eighteen fully connected anchors already have 306 directed pairs; 48 have 2,256. Many valid compact graphs therefore exceed the cap without violating any eligibility rule. The builder must either fail an otherwise useful graph or invent pruning/truncation that changes which routes and interests can be served. Input ordering must not silently choose the retained geography.

Minimal correction: choose either a pinned, hashed directed-pair allowlist in the build inventory (at most 256 unique pairs), or one exact deterministic bounded neighbour selection policy with stable ties. Specify exclusions and how the existing 60-edge/coverage proof applies to the selected result. Do not raise the cap or introduce a generic routing service to resolve this omission. Include a fixture with more than 256 reachable pairs and shuffled input that proves the declared selection.

### R3 — P2: Define the v2 validity clock and reproducible timestamp inputs

Location: `design.md:40`, `design.md:62`; `specs/trip-itinerary-candidates/spec.md:49` and `specs/trip-itinerary-candidates/spec.md:72`; adaptation task `tasks.md:22`.

V2 removes observed/human review timestamps for modelled edges and records publication (if available), download, retrieval and processing separately, but it never selects the timestamp from which the 30-day snapshot allowance is calculated or a fallback when publication is absent. “Snapshot/build age” permits materially different implementations. Rebuilding a months-old pinned extract today could receive a new 30-day validity if the implementation chooses processing time; another implementation would reject it as expired. The current v1 factory uses ReviewedAt for freshness, so it does not supply the missing v2 rule.

The same omission affects the exact reproducibility promise: the helper records processing metadata while identical input bytes/inventory/profile/model are required to reproduce output hashes. If current wall time is written into the hashed package on each build, that promise fails even when all graph outputs agree.

Minimal correction: define method-specific validity origins and ordering, missing-publication policy, UTC bounds and whole Moscow-local-day eligibility. Tie the source-age basis to the pinned input identity so rerunning an unchanged old input cannot silently renew it. For an explicitly retrieval-age-only fallback, label that narrower meaning and pin the original retrieval metadata. Supply all digest-affecting release/build timestamps as frozen build metadata; place per-execution elapsed time outside the immutable package. Add fixtures for old-input rebuild, absent publication, boundary day and identical-input repeat builds. Human observation remains null throughout.

## Areas without confirmed new findings

- M1 privacy and revision safety remain coherent: owner-first read, exact revision, final reread, clean-buffer/epoch guards, two guarded adoption reads, ordinary explicit PUT/CAS and existing uncertain-write behavior.
- The proposed HTTP changes address the actual M1 middleware seams. Candidate POST cannot reuse create-body validation; availability must bypass only private-plan readiness; pre-auth response policy retains disabled 404. Distinct typed errors and strict TS JSON vectors are planned.
- The payload/evidence split keeps the full OSM graph out of runtime. Fixed names, hash binding, XML DTD/entity prohibition, input/output/graph/wall caps and explicit failure preserve a bounded helper. Their real performance and coverage remain unmeasured, not assumed passed.
- Licences/source credits are planned outside editable owner notes, with public distribution as a separate gate. Private plans are not automatically published. There is no false claim that CC0 extends to Wikipedia prose or that OSM supplies observed walking minutes.
- Uncalibrated mathematical estimates are consistently distinct from empirical intervals, open-at-arrival facts and accessibility guarantees. No human signatures/scores are invented.
- D_demo/S/Q_demo, historical human Q_live, CI, target and rollout are separate. Q_demo may close an honest technical demo without human walking scores; the frozen human protocol remains pending. Actual source graph, 18/60 coverage, reproducibility, source tests and stage/edit/save/reload still need evidence.
- Production exclusion is implementable with the existing Composition/guard seam. TestOnlyGuard checks implementation types and throws if they are registered in Production; implementation must choose registration/route behavior that also preserves the stated M1 boundary. This is a verification point, not a claim that v2 code exists.

## Reviewed snapshot SHA-256

Paths below are repository-relative. Hashes cover exact file bytes.

```text
05b4548169f05ab545c8bcb499966c0575dfea87ce2e67f47692b94494c18073  AGENTS.md
e5492e295c8c16d9710bf5fff8d835eae6f2f0897e8746f130252b1b284de71b  modules/trips/AGENTS.md
8354f626b7ef338ad62bf9d356261057dae5ff5c937d5a11125a8aae917d19b4  openspec/changes/trips-m2-moscow-itinerary/proposal.md
b9834aaa07de222463cd13350da4c68732e4ebf5646c0643fde8dacab958cf55  openspec/changes/trips-m2-moscow-itinerary/design.md
f6555fb00e17d8e2686b28784435a6e8d27a2d50c273749f70647dfbd46ba1ac  openspec/changes/trips-m2-moscow-itinerary/specs/trip-itinerary-candidates/spec.md
b9dd0797d629f27235a93a957ed49b045d941724c6d96c6eee39692c74e5fa96  openspec/changes/trips-m2-moscow-itinerary/tasks.md
a24fccf4abb363dc652bc80511080b517886d9ea882f0d9f62612e8cc6c36732  openspec/changes/trips-m2-moscow-itinerary/data-strategy-revision.md
f0c0f4528d841fc61c78b9e2b2bda4c04543d477da5b6a285eee327f17fafc29  openspec/changes/trips-m2-moscow-itinerary/data-source-research.md
384a6458b15c22d4ebe2aa3885842749344f0e02a46a15fb90429a55b0f21aca  openspec/changes/trips-m2-moscow-itinerary/quality-protocol.md
f50b7511fc12642b20df7ca207c780740382eeb4a482258929551ce29aa5756c  openspec/changes/trips-m2-moscow-itinerary/quality-cases.json
da933d5b833763ea2f64888d550e0acb408b47dda038e42ac0d94e2a4cdabb54  modules/trips/Travel.Modules.Trips.Core/Itineraries/ItineraryFacts.cs
6d4127431db48452f4494e9fd8859cd4a824a21c8fdbdb9e3e8238beb42b774c  modules/trips/Travel.Modules.Trips.Api/Composition/TripsModule.cs
83a7189f961c7bb0c446b211a39c2c0a87e6a7f018a2900c1ae38fa2aec01971  modules/trips/Travel.Modules.Trips.Api/Middleware/TripsRequestGuardMiddleware.cs
7ad711982e2fc081aba8aac1fecc84c3a0995009342d4004346e0aebd429e2a4  modules/trips/Travel.Modules.Trips.Api/Middleware/TripsResponsePolicyMiddleware.cs
f55709c816405fd4265686b369ab2bbe21ff2f1bb889035f2dc87803512eb457  apps/Travel.ServiceDefaults/Hosting/TestOnlyGuard.cs
2453a99b53402c938db96e6f982e20525938469feb66329d2f52315949c3a496  global.json
```

This report accepts neither implementation nor data/runtime/quality/publication gates. A scoped recheck must bind the corrected canonical file hashes and explicitly resolve R1–R3.
