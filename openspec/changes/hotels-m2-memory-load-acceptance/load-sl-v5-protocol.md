# S/L v5 — compact load fixtures and acceptanceVersion3

2026-11-01. Owner requested autonomous completion of source/tests/review and the next frozen S/L execution. One new campaign is authorized after verification; no unchanged-package retry.

Protocol uses acceptance-v3.md for classification and inherits load-plan.md workload and load-sl-v2-protocol.md timing/error semantics. Data/catalogrevision1, dates2026-11-06..08, seed20261029, S9properties/source andL100,3152jobs/360s each,6304total. Canonical transport/data specimens retained fromv4; new retention/queue observations are runtime evidence.

All numeric memory/business/source/cache limits remain unchanged. Required queue health is now measured from exact due messages and authoritative owned-row terminal state; raw counts/lag and raw legacyboolean remain visible. Unknown or incomplete evidence is a failure. Product scheduler/read behavior is unchanged.

Preparation: TestResults/hotels-m2-memory-load-acceptance/load-sl-v5/. version3 explicitly in bothprofiles,source/build/runtime/data inventories and archive verified.425prior raw files preserved. Check-in must remain more than one UTC day ahead; no automatic date changes.

Safety: availableRAM4GiB,freeDisk5GiB,evidence512MiB/profile;setup120s,timed360s,cleanup60s,campaign1200s. S first,L only ifS passes; stop-on-failure,no retry. No new idle phase,forcedGC/cacheclear/trim,WPR/ETW/VMMap/dumps or UI.

Verify-only:
    dotnet --fx-version 10.0.9 tools/demo/Travel.Hotels.Fixture/bin/Debug/net10.0/Travel.Hotels.Fixture.dll m2-load TestResults/hotels-m2-memory-load-acceptance/load-sl-v5/profile-S.json TestResults/hotels-m2-memory-load-acceptance/load-sl-v5/profile-L.json

The one authorized campaign adds --execute after source review and fresh identity/date/resource checks. Preserve all results and clean only exact owned processes/container/temp. Historical failures and prior classifications do not change.
