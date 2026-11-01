# S/L v4 — future acceptance version2

2026-11-01. Source preparation only; runtime not authorized by this packet.

This is the first prepared S/L packet using the owner-approved [acceptance-v2](acceptance-v2.md). Both profile JSON files have version2. It keeps23 mandatory gates (16 original +7M2); the finalSameRetainedRowsMemoryStable original result is explicitly diagnostic, blocking=false, and never rewritten to true. Version1/M1 behavior remains available and unchanged.

The [load-plan](load-plan.md) remains the frozen version1 workload reference; only its supplemental five-second blocking classification is superseded by acceptance-v2. All numeric mandatory memory thresholds, source/cache/business/evidence/resource limits, recovery windows and stop-on-failure rules stay unchanged. The [v2 timer/error protocol](load-sl-v2-protocol.md) still applies.

S/L data/catalog revision1, 16 specimens copied byte-for-byte from v3; dates2026-11-06..08, seed20261029, areaKey=fictional; S9 properties/source and L100;3152jobs/profile,360s/profile,6304planned total. Run before2026-11-05T00:00Z (check-in must remain more than one UTC day ahead); no automatic date shifts.

Safety unchanged: availableRAM>=4GiB, freeDisk>=5GiB, ownprofileevidence<=512MiB, setup120s, timed360s, cleanup60s, campaign1200s. No new idle/control phase, epsilon, forcedGC, cacheclear, WPR/ETW/VMMap/dumps or UI automation.

Packet: TestResults/hotels-m2-memory-load-acceptance/load-sl-v4/.
Verify-only command from the physical repository root:

    dotnet --fx-version 10.0.9 tools/demo/Travel.Hotels.Fixture/bin/Debug/net10.0/Travel.Hotels.Fixture.dll m2-load TestResults/hotels-m2-memory-load-acceptance/load-sl-v4/profile-S.json TestResults/hotels-m2-memory-load-acceptance/load-sl-v4/profile-L.json

A future separately authorized invocation adds --execute exactly once. S runs first; any S failure cancels the campaign before L. No retry. Source/build/data/date/resources must be rechecked immediately before execution.

373 historical raw files, including unchanged S-v3 failed23/24 and Lnot-run, are pinned. The old packets never inherit version2 retroactively. Preparation/source tests/review and replay do not establish runtime acceptance. A new full S/L result, truthful diagnostic evidence and verified own cleanup are still required.
