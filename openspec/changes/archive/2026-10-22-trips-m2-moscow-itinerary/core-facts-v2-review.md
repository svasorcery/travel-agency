# TASK2.1 v2 independent spec and quality review

Date: 2026-10-21. Base HEAD: e654c76915103a82083ef2abc01c0579117c9749; reviewed task source is uncommitted. Verdict: PASS for TASK2.1 v2 Core facts and focused test source. P1: 0; P2: 0; P3: 0. This is a new v2 verdict; the v1 review remains historical.

## Binding and reviewed source

Read task-2.1-v2-brief.md, task-2.1-v2-report.md, task-2.1-v2-review-package.txt and the current task-2.1-interface.md, then independently read all three current owned C# files. Binding: current exact task2.1, design sections2/3, and delta requirements for permitted facts, explicit modelled demo semantics, immutable release and freshness. Root/module AGENTS.md and the existing destination/text primitives apply. No source/product edit was made.

SHA256 of reviewed current bytes matches the worker report:

- ItineraryFacts.cs: c757f61f6b6f429401130a6d7c2125227239d6761e22f90c6f27a47100cad255
- ItineraryFactsTests.cs: 55268181f988c6996285663425ad81e7500cc1df2d6b82411397d4aa0fd2d518
- ItineraryTestFacts.cs: 7435a01ae6a746298da9c4afe8bdbbf817f2500c0928cb77c9c00549b97dd3d1

Paths below are repository-relative; line references are to these bytes.

## Confirmed findings

No confirmed correctness, contract or maintainability defect requiring a P1/P2/P3 correction was established. No speculative framework or unrelated work is requested.

## Spec and source assessment

- Truthful methods and actors: preparation requires osm-modelled-demo, a software token, exactly null human actors and both not-evaluated statuses (ItineraryFacts.cs:249-272). Evidence enforces exact kind/method/rights/field combinations: source-record+CC0 identity, separately permitted source-record or actually observed access, modelled+ODbL walking with ObservedAt=null (322-436). Invalid enums and nullable validation inputs return existing Trips.InvalidInput; no v1 walking/human factory overload remains.
- Publication-origin freshness: model source retains separate input/publication-input hash, publication/download/frozen-build timestamps and exact profile-v1/pair-selector-v1/model-v1 identities. It rejects default/non-UTC/unordered/unbound times, a build at or after publication+30days, and dates that could overflow expiry calculation (76-115). ValidUntil is derived from publication alone (73); rebuilding cannot extend it. Core can validate normalized consistency, while the later helper/loader must establish that this metadata came from the actual successful response. No physical survey date is inferred.
- Identity/access leases: source-record lease origin is retrieval; actual-observation access uses ObservedAt and cannot restart at retrieval/processing. Caps are90/7days; processing must precede expiry (338-421). Package release is capped by model expiry and every identity lease, with all evidence valid at frozen build (774-878). Access may expire earlier and thereafter be unknown. Actual execution-time readiness remains a later TimeProvider/asOfUtc check; a frozen build is not current readiness.
- Formula/path consistency: integer ceiling avoids multiplication/add-before-division overflow; the largest possible long quotient plus int crossings remains within long. Negative inputs, invalid anchors/self references and inconsistent zero/shared-anchor paths fail; derived lower/upper are1..120 (173-219). Package checks exact source input/version/timestamps, directed POI references, formula minutes, distinct path IDs, consistent per-POI anchor IDs and consistent anchor/length/crossing inputs for a reused digest (838-872). Path digest/geometry/anchor-distance reproduction belongs to the later helper/loader; this task does not pretend to parse or certify OSM geometry.
- Date-knownness: supplied saved destination IANA zone determines the date; processing local date is inclusive per the published task interface, and the requested whole local day must end by expiry. Invalid/null zone, DateOnly.MaxValue and invalid/ambiguous end boundary fail closed (448-464). IsKnownAt is half-open. No Moscow default or current clock/network call exists in Core.
- Retained package integrity:18..48POI,60..256directed edges, unique IDs/pairs, valid endpoints, at least3POI per supported tag, exact Wikidata entity/revision URL, scoped access/walk evidence and no orphan evidence remain enforced (774-878). No graph-connectedness rule is invented: disconnected-route outcomes are later consumer cases. Unknown/closed/full-day access remains distinct (511-565).
- Deep immutability: sealed validated classes have private constructors/get-only values; every collection boundary copies into a read-only wrapper, including intervals, closed dates, tags, permitted fields and package arrays. Shared nested fact objects and model provenance are themselves immutable. External OSM DTO/parser/router code is absent from this Core unit.

## Test quality and verification evidence

Worker receipts inspected, not newly executed by reviewer:

- TestResults/trips-m2/task-2.1-v2-green.log records the full --no-restore Trips Unit run:99passed,0failed,0skipped. The split53M1+46itinerary and RED sequence are worker receipts. The existing Verify SolutionDir warning is present.
- TestResults/trips-m2/task-2.1-v2-format.log records CSharpier checking3files.

Reviewer did not rerun tests or formatting because source inspection produced no concrete concern warranting duplicate execution. Reviewer checks were full source/spec/test inspection, current source hashes, HEAD and receipt-content reads. No independent runtime result is claimed.

Focused tests cover truthful demo metadata, method/rights/nullable observation constraints, exact source binding, publication+30day tick boundaries and rebuilds,90/7day origins, formula thresholds/extreme values, directed references/anchor/path digest consistency, unknown access/local dates, scope/duplicates/orphans, defensive copies and retained release/tag bounds. The upper-bound fixtures positively accept48/256 and separately reject49/257 using unique data, so they do not depend on duplicate-input rejection. Expected formula examples are fixed business boundary values, not a duplicated implementation algorithm. Fictional sources/path digests are explicitly labelled and do not become accepted real records.

Not every scalar/null/offset or cross-reference branch has a separate dynamic test. Source guards were traced, but this review does not claim exhaustive property testing or fresh compilation. No missing test was elevated into a defect without a demonstrated incorrect behavior.

## Verdict limits and remaining gates

This PASS covers the three normalized Core/test files and v2 contract only. Parent owns README/canonical progress integration. Offline helper input caps/profile behavior, actual response/input binding, real identities/paths/coverage, exact payload/evidence/manifest/licence bytes and D_demo reproduction remain separate. Root is researching the real input and may report a measured source/cap blocker; this review grants no D_demo acceptance.

Generator/independent candidate validation, loader, Application/HTTP/UI/runtime integration, Q_demo and final whole-M2 independent audit remain later work. Field calibration and two-human D_live/Q_live remain not-evaluated/pending. No publication/Production/Host/database/network/model/provider authority is inferred or exercised.