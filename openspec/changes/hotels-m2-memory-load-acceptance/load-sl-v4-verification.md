# S/L v4 — source verification of acceptance revision2

2026-11-01. Owner approved the audit recommendation and requested continuation. The future acceptance contract, executor and tests are updated; a new S/L packet is frozen. **No new Host/DB/load run occurred.** Overall plan remains6/8; runtime tasks3.2/3.3 are open.

## Behavior

The [approved amendment](acceptance-v2.md) changes only the role of finalSameRetainedRowsMemoryStable for explicit version2 packets. Its unchanged strict boolean, first/second windows, counts, mean RSS, delta and row/cache observations remain in receipt.memoryDiagnostic with blocking=false. A false diagnostic stays false.

Mandatory gates:23 total (16 original +7M2). RSS<=1GiB, final<=pre30s baseline+128MiB and mean[330,last]<=mean[300,330), cache/source/queue/business limits, complete3152jobs/361samples and cleanup remain blocking. No tolerance, altered window, forcedGC or idle phase was added.

Both S/L profiles must declare the same supported version. Existing version1 retains24 mandatory gates. Unknown/mixed versions fail before runtime. Shared M1 Gates, Schedule, Phase, RecoveryAt and four supplemental predicates are unchanged. Historical M1failed16/17, S-v2failed22/24, S-v3failed23/24 and Lnot-run are not reclassified.

The classification copies the original gate dictionary. Missing original boolean, insufficient final windows, duplicate timestamps or negative window RSS fail. Equal row/cache observations explicitly report full retained state comparison as notEstablished; detected row/cache changes report changed. Complete source/private/GC observations remain in existing raw artifacts; no leak-free inference is made.

## Changes and verification

Source slice: five tool files (new HotelsM2MemoryAssessment plus RunSpec/Campaign/LoadRunner/EvaluationProof), one new focused test class, a recorded final-window fixture with original hashes and one test-project compile link. No product application code changed in this slice.

| Check | Result |
|---|---|
| Actual old evaluator RED | diagnostic-rss-growth fails specifically finalSameRetainedRowsMemoryStable |
| Actual evaluator GREEN |48/48 acrossS/L, versions1/2 and12 cases each |
| Focused classification tests |20/20, including saved real S-v3 windows |
| Full permitted Hotels standalone |371/371,0skips |
| Wrapper build |0errors,1known Verify solution discovery warning |
| Historical M1 offline replay |16/17, workingSetRecovery=false;5legacy source sections unchanged |
| Formatting |7files passed |
| Independent fresh-context review |0Critical/0Important/0Minor; nofixpass/rereview |
| OpenSpec strict validation |passed |

Source-proof verifies the meaningful difference: one-page final-window growth fails in version1 and remains nonblocking diagnostic in version2. Peak ceiling, baseline tolerance, longer recovery mean growth, provider/business faults and other failures remain negative. These are pure source tests, not a replayed acceptance of historical S-v3.

All evidence is under TestResults/hotels-m2-memory-load-acceptance/source-sl-v4/. The recorded fixture pins resources SHA2225CD94724B3BCE8B802CBBBB9EE69176D83C6C7C7D9BCB8D00D1FE49FF860A and receipt SHA BD8DF24C7213F7CAE41462A5FA4535452CA67A039CD7536B8E60FBC35570C899. It contains only window metrics and gate booleans.

[Independent review](acceptance-v2-review.md) examined actual runtime wiring and retained-version boundaries. Final archive integrity remained main's separate check and was completed. Runtime and unrelated inherited diagnostics were not re-audited.

## Frozen package

Packet: TestResults/hotels-m2-memory-load-acceptance/load-sl-v4/. [Protocol](load-sl-v4-protocol.md).

| Item | Value |
|---|---|
| Profiles |S/L, JSON version2; data/catalog revision1 unchanged |
| Dates / seed |2026-11-06..08 /20261029 |
| Workload |3152jobs/360s each;6304planned |
| Source/input / build inventory |2018 /999files |
| Runtime / dotnet host |335files /1host |
| Explicit inputs |28 |
| Canonical dataset preservation |16files byte-identical to v3 |
| Historical raw preserved |373files, all hashes verified |
| Archive |3020entries,56156176B, each entry verified |
| Verify-only |prepared=true,acceptanceVersion2,executed=false |
| Execution authorization |false; no run directory or attempt marker |

| Artifact | SHA256 |
|---|---|
| source-manifest.json |9AAC4C79C7076DC7F094E0779E93ADDD21426CA976E56B40BDC97F35D58DB03C |
| freeze.json |B89FF0FD3836A4B689D1A2C99AE1B9B1216E383AE77281665B26B2397CB39F48 |
| frozen-source-build.zip |F3E35C33120E420B334049B3136E98CA17963244A0717D8B3132B67F92EE0F70 |

The package preserves numeric safety limits: RAM4GiB, disk5GiB, evidence512MiB/profile, setup120s/timed360s/cleanup60s, campaign1200s. Read-only preparation observed about12.2GiB free disk. Existing local PostgreSQL image/runtime identity checked without starting containers. No WPR/VMMap/ETW/UI or new tools installed.

## Decisions and remaining work

The frozen load-plan.md remains unchanged as the version1 reference; acceptance-v2.md explicitly supersedes only the supplemental gate classification for future packets. This preserves evidence at the cost of maintaining version1/2 paths.

Work stays in the existing isolated branch with local uncommitted edits; no stage/commit/push/archive occurred. Final review has no deferred findings. Packet hashes and archive checks are complete; independent runtime S/L acceptance remains required.

Next step is a separately authorized single S/L v4 campaign, with S first, L only after S success, stop-on-failure and no retries. Recheck date/resources/identity immediately before execution. Source readiness does not accept M2 or authorize suppliers, target or rollout.
