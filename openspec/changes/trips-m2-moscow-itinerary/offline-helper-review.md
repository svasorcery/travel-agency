# Task 1.4 independent review

Reviewed 2026-10-21. Scope: tools/trips/build-moscow.cs and tools/trips/build-moscow.test.mjs. Source-only helper review; this is not final M2 acceptance or permission to publish, enable, stage, commit, push, run Host/database/model/provider calls, or modify product code.

## Verdicts

- Spec compliance: PASS for task 1.4, against the current task, design section 2 and the Explicit modelled demo semantics / Bounded offline preparation requirements. No P1/P2 defect found in this bounded implementation.
- Code/test quality: APPROVE WITH ONE NONBLOCKING P3 INTEGRATION FOLLOW-UP. The BCL-only file app and dependency-free CLI tests stay within the chosen build-time boundary. Runtime loader acceptance remains unverified until task 3.1.

## Exact snapshot and method

HEAD: e654c76915103a82083ef2abc01c0579117c9749.

- tools/trips/build-moscow.cs SHA256: b9f5ad331d54772c2eacc4f21311bdd1de0f117434368e59c15ebf1f76702ba8.
- tools/trips/build-moscow.test.mjs SHA256: b2ac3ab99396b381140a933792a5881fc1226ce7c1d174de00f07a2673ebccb4.

Verified both source hashes and HEAD live. Read task-1.4-brief.md first, then task-1.4-report.md and the full new-source diff in task-1.4-review-package.txt once. The initial tool output cut off the helper middle; only that cut-off range was fetched again. Read the canonical task 1.4, applicable spec requirements and design section 2. The only outside-source inspection was the named Core 2.1 normalization/method/path-schema risk in ItineraryFacts.cs. No repository crawl, suite rerun, graph parse, download, network call, product edit or Git mutation was performed.

## Findings

No P1 or P2 findings.

### P3 - Keep helper-to-Core boundary validation explicit in task 3.1

Locations: tools/trips/build-moscow.cs:241, :303, :429-433, :765-769; modules/trips/Travel.Modules.Trips.Core/Itineraries/ItineraryFacts.cs:190, :620, :905-915.

The helper accepts digit-leading POI/dataset tokens, up to 48 characters per POI ID, and titles with leading/trailing whitespace. It constructs path/edge IDs by concatenating both POI IDs. Current Core IDs must start with a letter and be at most 64 characters; Core text must be trimmed. Consequently two allowed 48-character helper POI IDs can produce a 102-character path ID, while a whitespace title passes helper checks and fails the Core factory. This is a concrete boundary mismatch in the accepted input spaces, not evidence that the current real output is invalid.

Task 3.1 is still pending and owns strict schema/domain mapping. Its absent mapping prevents a verified conclusion about final package acceptance or normalization policy. Before closing that task, add a real emitted-byte-to-Core acceptance test and explicitly reject or deliberately map incompatible identifiers/text without silently changing source evidence/hash semantics. Also verify the source-record/CC0 identity mapping, modelled/ODbL walking mapping, fixed manifest paths, path digest encoding and release expiry through the actual loader. This note does not claim that the loader already works or is defective, and does not block the bounded helper task.

## Compliance and quality evidence

Static inspection confirms the exact conservative highway/access/foot/conditional/barrier/direction table; unsupported or denied tags cannot be rescued by another allow. Only adjacent mapped endpoints inside the bbox produce segments, with no gap repair. Anchors are eligible mapped nodes within 50 m. Haversine radius, midpoint-away integer millimetres, min-1-mm segment lengths, checked sums, sorted adjacency, priority-queue numeric-node ties and nonreplacement of equal distances match the declared deterministic algorithm. The distinct crossing-node count includes endpoints and does not use crossing-way tags as extra events. Independent directed searches and length/target-ID selection cap each source at five outgoing paths; 18..48 inventory and at least three per tag plus at least 60 paths are enforced.

The integer model formula, upper limit, explicit modelled/uncalibrated semantics, nullable human actors/observedAt, unknown access and approximate anchors are preserved. No runtime router, network client, provider DTO integration, database or service registration was introduced.

The code streams gzip/XML with DTD prohibited and resolver null, drops irrelevant tags while retaining relevant deny/conditional fields, and enforces the separate compressed/expanded/raw-index/graph/ways bounds. Time and memory operator flags only tighten hard ceilings; the force check immediately before atomic directory publication includes sampled working set and recorded peak. This is a sampled process guard, not OS containment. An I/O or final guard failure may leave an owned .partial-UUID directory, but cannot publish the final release path. Existing output is rejected.

Publication/download/frozen-build ordering and actual-execution source expiry are checked. Hash-bearing output uses frozen inputs, exact UTF-8 LF bytes, no BOM or self digest, fixed manifest names and explicit payload/evidence/manifest byte limits. OSM attribution, ODbL derived-data offer and reproduction inputs are emitted locally. This is not evidence of an already published offer. Identity validity is 90 days from its recorded retrieval; release expiry is the minimum of identity and model leases. Runtime clock/expiry acceptance is still a loader/service concern.

## Receipts actually inspected

Read these retained logs in full; did not execute their commands again:

- TestResults/trips-m2/offline-helper-final.log: 15 passed, 0 failed, 0 skipped, duration 42488.2544 ms. Source test inspection confirms fictional fixture labeling and assertions for determinism, direction, conservative profile exclusions, crossing counts, malformed/oversized input, source clock/hash binding and tighter operator budgets. These CLI assertions do not establish human route quality.
- TestResults/trips-m2/offline-real-final-1.log: 20 POIs, 90 paths, raw index 597838, retained graph 71507 nodes / 23566 ways; 15381 ms, peak 205950976 bytes.
- TestResults/trips-m2/offline-real-final-2.log: same counts; 15879 ms, peak 223760384 bytes. All five artifact digests in the two receipts match.

Matching retained receipt digests:

- moscow-v1.json: 631fd5cd9650e3228af87ef8a31219d7ea59a2e4af42bc4328453f32268ffb08
- walking-evidence.json: 196c5cd39481bdb55b6c123a4555571a0aa6f2eabc720e24ef3d70f63e3b397b
- ATTRIBUTION.txt: 0c8bca1abb555d22fc9a30133287abb9a11cc8554169ede7c379b9473cb01172
- inventory.json: c04bc32ca50be5fa5caa6057bf89948fee874c01ecdc418d4bd92059695f01ae
- input-manifest.json: d5a3025a6670d7aaccfaaff8c15a085197c2c9734fec50339a6ecf2322870ac8

These are checked log contents, not an independent rehash of the cached real input or every generated real artifact. The final formatter results, earlier RED/GREEN receipts, download response metadata/checksum, real source identities/rights and the stated isolated POIs were not independently reverified in this review. No formatter rerun was needed for a concrete doubt. D_demo/Q_demo binding, task 3.1 real package/static offer integration, whole-M2 audit, human quality, rollout and CI/target acceptance remain separate pending work.
