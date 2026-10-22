# Scoped assessment: offline OSM input staging limits

Date: 2026-10-21. Scope: read-only judgment of one measured input/plan issue. This is not a whole-source audit, build acceptance, data release or permission to change limits. Root owns the plan decision and implementation.

## Judgment

**The proposed change is a justified, bounded refinement within the selected Moscow demo scope, provided the original 100,000-node limit continues to apply to the actual eligible pedestrian graph. It is not evidence that a larger graph should be accepted.**

Separating a temporary raw-coordinate index (at most 750,000 records) from the retained graph (at most 100,000 nodes / 50,000 ways), and adding a 1 GiB process-memory budget, makes an implicit import stage explicit. It adds a small amount of accounting/pruning to the same offline helper. It does not add a router service, map database, package dependency, runtime cache or new product capability. This remains proportionate for a demo; a general routing platform is unnecessary.

This judgment is conditional on an explicit plan revision and verification of the new stage limits. The current failed build stays failed. Actual graph size, memory peak, elapsed time, 18/60 coverage and package correctness remain unproven.

## Observed evidence

- Current `design.md:44` caps compressed input at 200 MiB, expanded input at 2 GiB, retained graph at 100,000 nodes / 50,000 ways and build time at five minutes. It contains no separate temporary raw-coordinate-index limit. Design SHA-256: `431b113575ec9350e98614dda3c9f42561d557d05561f1cba309129c2120038d`.
- Current `tools/trips/build-moscow.cs:207`–`210` reads tags, inserts every node inside the bbox, and immediately applies the 100,000-node check, before reading/filtering ways. `:221` then filters segments by referenced nodes/profile; `:244` returns the full node dictionary without pruning. Thus the current failure threshold measures raw bbox nodes, not the final eligible graph. This is directly visible in source.
- `bounds-diagnostic.log` records 5,134,381 raw nodes across the input, 597,838 inside the fixed bbox and 236,384 in the all-20-POI bbox plus margin, with diagnostic elapsed time 6,006 ms. Its own meaning explicitly excludes graph/anchor/coverage acceptance. The 750,000 proposal is above the measured fixed-bbox staging demand, with about 25% headroom; it is not a measured memory or final-graph result.
- The separate diagnostic run with an increased raw-node bound stops at `Build rejected: Too many OSM tags`. Current `build-moscow.cs:247`–`260` retains every tag and caps that dictionary at 256, including tags unrelated to the selected profile. Reading only the needed profile/geometry tags is a valid implementation correction. This log does not identify which element triggered the limit and does not prove the next build will pass.
- The download receipt and input manifest agree on SHA-256 `f03e23186dbcfb6e1add78e75425cd8654dc36a79fa08c546da7abf448e00c97`, 162,060,454 bytes, publication `2026-10-18T03:55:13Z` and download `2026-10-21T08:11:49.1731881Z`. I read those receipts; I did not rehash or reread the large map and did not repeat the network request. Publication remains an extract timestamp, not a survey.
- The root reports 10 CLI tests passing. I inspected the helper/test files for this scoped issue but did not execute tests or treat that reported result as real-input acceptance.

## Conditions for the proposed refinement

1. Name the two stages explicitly in design/spec/tasks and output receipts: `rawCoordinateIndexCount` versus `retainedGraphNodeCount`/retained ways. Preserve 100,000/50,000 for the graph and all existing compressed/expanded/wall/output caps. Do not silently relabel a 750,000-node graph as retained data.
2. Enforce the raw cap during indexing and the graph cap while admitting eligible segment endpoints, before routing. Prune unreferenced raw nodes after way processing; account for temporary copies and collection capacity in the process memory budget. Retain only profile-relevant bounded metadata; exclude contributor metadata from outputs.
3. Tag filtering must preserve every admitted/denied/conditional key used by the fixed profile, including node access/barrier/crossing and direction semantics. A late deny/conditional tag must not disappear because an earlier irrelevant tag exhausted a counter. Keep duplicate relevant-tag rejection and XML/attribute/way-reference bounds. Irrelevant tags may be streamed/skipped; they must not become new trusted fields.
4. Define the 1 GiB limit as a process budget including raw index, retained graph, parser buffers, routing and temporary allocations. Check during bounded parser/graph/routing work and reject before package release if violated; retain measured peak/elapsed/count receipts. Periodic WorkingSet checks are a fail-closed measured budget, not a promise that OS-resident memory can never transiently exceed the number. Do not claim an OS-hard memory ceiling unless an actual enforcement mechanism is used and verified.
5. Add focused fixtures for many irrelevant/raw nodes with a small valid graph, raw-index overflow, actual graph overflow, many irrelevant tags before a relevant deny/conditional tag, relevant duplicates and memory/time failure without release. Then run the actual pinned input and report the result unchanged, including any further blocker. No automatic cap escalation follows a new failure.

## Bounded alternatives

| Option | Assessment |
|---|---|
| Separate 750,000 raw-index cap, preserve graph cap, filter tags, add measured memory budget | Preferred minimal refinement for the current single-pass helper. Actual memory and graph size still need proof. |
| Multi-pass processing in the same BCL helper: collect eligible way/reference data, then retain only needed node coordinates | Avoids storing all raw bbox coordinates, but adds parser passes and bookkeeping. A reasonable fallback if the proposed staging representation fails the memory budget; it must still satisfy five minutes and expanded-input policy. No second service or parser package is needed. |
| Shrink bbox to the measured all-20-POI margin | Does not by itself solve the raw 100,000 threshold: the diagnostic still shows 236,384 nodes. Also changes coverage/path eligibility and can remove useful paths; requires explicit geography review. Not a silent workaround. |
| Raise the actual retained graph cap to 750,000 or drop memory/time caps | Not supported by these diagnostics. Raw counts do not establish a need for a larger pedestrian graph or justify an unbounded importer. |

No waiver, source acceptance, D_demo/Q_demo pass, map usability, licence-offer readiness or human/live quality is granted by this report. Root may adopt the explicit narrow revision or choose one of the bounded alternatives; the next real build supplies evidence.

## Exact read snapshot

```text
83aff6d28f225d64b65b183fa929893ec60e89231f5f0e18ff4112cde2462d18  tools/trips/build-moscow.cs
06dd8f7b2fee9a870215dbebdaf5efe90042269d7d48b0c2e4934578ba448ffa  tools/trips/build-moscow.test.mjs
431b113575ec9350e98614dda3c9f42561d557d05561f1cba309129c2120038d  openspec/changes/trips-m2-moscow-itinerary/design.md
fc34c1af7b41eabf62f10764f52e5dd0c20576bf1d428f16798069164182a813  D:/_Projects/_github/travel-agency/TestResults/trips-m2-source-cache/bounds-diagnostic.log
e5167ae7e569c4149bc6fc99472505e028e0df632351dbd7cd8744a51990927c  D:/_Projects/_github/travel-agency/TestResults/trips-m2-source-cache/diagnostic-cap.log
e60d08f93899bdafd1af50e5b9fe705f701521e8d2617906222879d51ce62bba  D:/_Projects/_github/travel-agency/TestResults/trips-m2-source-cache/input-download.json
9d2a09a05120b3f964c45c5647a464caa5140c0e5d27f359c56c32c91af5aae7  D:/_Projects/_github/travel-agency/TestResults/trips-m2-source-cache/input-manifest.json
```

Only this report was written. No product/plan/Git/Host/network changes or map processing were performed. This assessment relies on the cited current source and local diagnostic receipts, not historical memory.
