# S/L v3 — acceptance after scoped metadata repair

2026-11-01. The owner requested continuation to independent S/L acceptance after the H6 v3 comparison. One campaign is authorized: S once, then L once only if S passes; stop on failure, no retries.

This packet uses the reviewed scoped AssemblyMetadata lifetime implementation described in [compiler verification](compiler-metadata-lifetime-verification.md). Product/compiler/runner source is unchanged from diagnostic-h6-native-heap-v3. Its partial H6 result is separate from S/L acceptance.

All workload, fictional dataset, dates (2026-11-06 through 2026-11-08), seed 20261029, source fault recipes, 3152 jobs and 360 seconds per profile are unchanged from [load-plan](load-plan.md). The 16 frozen data specimens are byte-identical to load-sl-v2. The [v2 timer/error protocol](load-sl-v2-protocol.md) remains applicable. Schema version and catalog revision remain 1; v3 identifies this execution packet.

Original 17 gate families and seven M2 additions are unchanged. Available RAM >=4 GiB, free disk >=5 GiB, own profile evidence <=512 MiB; setup <=120s, timed 360s, cleanup <=60s, campaign <=1200s. No forced GC, cache clearing, trimming, extended windows or threshold tolerance.

Execution uses the existing local Testing Host, owned disposable PostgreSQL and fictional handlers. No WPR, ETW, VMMap, dumps, UI automation, supplier sockets, paid APIs or shared Docker changes. S/L run sequentially in background with exact owned-resource cleanup.

Packet: TestResults/hotels-m2-memory-load-acceptance/load-sl-v3/. Its create-only profile/source/build/runtime inventories and archive are verified before execution. The current 345 prior raw artifacts, including failed M1/S-v2 and partial H6-v3, retain their original hashes.

Command from the physical repository root:

    dotnet --fx-version 10.0.9 tools/demo/Travel.Hotels.Fixture/bin/Debug/net10.0/Travel.Hotels.Fixture.dll m2-load TestResults/hotels-m2-memory-load-acceptance/load-sl-v3/profile-S.json TestResults/hotels-m2-memory-load-acceptance/load-sl-v3/profile-L.json --execute

Without --execute, the same invocation only verifies the packet. New output paths, exact process arguments and one attempt marker are recorded before dispatch. The existing campaign controls sequencing, cancellation, the full denominator and stop-on-failure.

M2 accepted only if both profiles pass with complete evidence and verified cleanup. Failure or omitted L remains explicit. This does not establish supplier/target readiness, a hard whole-system memory budget or absence of all leaks.
