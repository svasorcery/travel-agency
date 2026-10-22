# Task 5.2 Phase B — first bound real-demo execution

**Single authorized execution succeeded; independent same-reviewer raw-chain verification is pending.** Task5.2 is not marked complete by this worker. No tuning, retry, second submission, source edit or rebuild after binding occurred.

## Authority and immutable identity

- Exact command invoked once: `& ./.superpowers/sdd/tasks/task-5.2-run.ps1 -Phase PhaseB -RunId quality-demo-a872-v1`; worktree `C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency`. Ongoing execution was resumed/polled only as session61232 until final exit0.
- Parent dispatch was recorded before output: `.superpowers/sdd/tasks/task-5.2-phase-b-dispatch.json`, SHA256 `9bcc7c761802c1f2c1c8a3bdf3c65438ff32fe9e9ab914849f889004ee2657e3`. Gates: current4.8 review `93e0d77702e6b8e0f5da4fac788f469b014f6acb51e6fba5788f25ebf91eb5b1`, method recheck `3a5c0d92ae21f67991ceac6ae5ae7c19b33699e51358e884ff6a896a1c4cf736`, both supplied PASS.
- Original requested base `e654c76915103a82083ef2abc01c0579117c9749`; integrated HEAD `a8721effd9056459d217966083816455fbea599d`; actual source/binary/dependency bytes bound separately. Recorder SHA `35c528c17257a77b6c046e14316a93aba8e4b976fd7a2531ecca49cbf1dbb005` unchanged.
- Original cases SHA `f50b7511fc12642b20df7ca207c780740382eeb4a482258929551ce29aa5756c`; fixed fixture clock `2026-10-21T14:16:33.0000000Z`. Protocol/Data/model/profile/S recipes/oracles remained unchanged. No generated human observations or scores.
- Closed binding created at `2026-10-21T19:04:15.1070295+00:00`, SHA256 `49829aa1f7a083d1e1ac5d7f50880712c0bcb1af548ad2f6626621ea43308b8d`. Actual execution was later; fixture clock was not advanced.

## Outcomes, without denominator inflation

- xUnit: **1 ordinary Fact passed**, containing **24 complete case records**. Q=12,S-edge=6,S-negative=6; failed0,unrun0; all outcome/structural expectations passed. This is not24xUnit tests.
- Filled Q stop targets: **12/12**, above unchanged threshold11/12, calculated from distinct actual POIs plus per-day/total stop counts. Coverage enum and human suitability were not substituted for this count.
- Node: **13/13 passed,0failed/skip/todo** =12actual-Q raw production-parser/decoder checks plus1complete24-record inventory/hash/linkage check. This is not24decoder tests.
- Dotnet exit0, Node exit0, final recorder exit0. Final receipt successful=true,bound=true,fatal=null; all12named runtime-hook states absent and finalExecutionHookCheckPassed=true.

| ID | HTTP / outcome | Stops per day | Actual / target | Coverage / explicit negative result |
|---|---|---|---|---|
| Q01 | 200 / candidate | 2 | 2 / 2 | filled |
| Q02 | 200 / candidate | 3 | 3 / 3 | filled |
| Q03 | 200 / candidate | 4 | 4 / 4 | filled |
| Q04 | 200 / candidate | 2,2 | 4 / 4 | filled |
| Q05 | 200 / candidate | 3,3 | 6 / 6 | filled |
| Q06 | 200 / candidate | 4,4 | 8 / 8 | filled |
| Q07 | 200 / candidate | 2,2,2 | 6 / 6 | filled |
| Q08 | 200 / candidate | 3,3,3 | 9 / 9 | filled |
| Q09 | 200 / candidate | 4,4,4 | 12 / 12 | filled |
| Q10 | 200 / candidate | 3 | 3 / 3 | filled |
| Q11 | 200 / candidate | 3,3 | 6 / 6 | filled |
| Q12 | 200 / candidate | 3,3,3 | 9 / 9 | filled |
| S01 | 200 / candidate | 3 | 3 / - | partial; unmet food |
| S02 | 200 / candidate | 3 | 3 / - | partial; unmet food |
| S03 | 200 / candidate | 3 | 3 / - | filled |
| S04 | 200 / candidate | 1 | 1 / - | partial |
| S05 | 200 / candidate | 1 | 1 / - | partial; unmet history |
| S06 | 200 / candidate | 1 | 1 / - | partial; unmet history |
| S07 | 404 / problem | - | 0 / - | Trips.NotFound |
| S08 | 412 / problem | - | 0 / - | Trips.RevisionMismatch |
| S09 | 200 / unsupported | - | 0 / - | destinationUnsupported |
| S10 | 200 / unsupported | - | 0 / - | dateRangeUnsupported |
| S11 | 200 / unsupported | - | 0 / - | historicalDatesUnsupported |
| S12 | 200 / unsupported | - | 0 / - | horizonUnsupported |

- S01/S02 explicitly partial with unmet food; S03 retains unknown hours; S04 expired walks, S05 disconnected eligible POIs and S06 single eligible POI each produce1stop/no invented adjacency. S07 foreign owner404 and S08 stale revision412 retained exact problem bodies; S09–S12 return explicit unsupported reasons, not invented success.
- Q01 actual real-package candidate -> local note edit -> explicit ordinary M1 PUT -> GET passed with stable UUIDs/title/brief/historical note/null time/estimate. Generation invoked zero store writes and preserved snapshot/revision/time. This complements prior fictional browser evidence; it does not claim real browser/Host/DB acceptance.

## Visible unknowns and limits

- Q bodies contain72stop instances with access/opening hours unknown; all72estimates and72start/end-time pairs remain null. No known-closed-as-open, invented observedAt or unsupported-as-success assertion failed. All computed records retain humanQuality/calibration not-evaluated and human actors/scores null.
- Q outputs contain48modelled directed walking-edge instances across the12cases. The unchanged tests checked used source/evidence identity, freshness, rights, retained path bytes/hash/node-way sequence/anchors and independent integer75/45formula; prior accepted D_demo remains the offline graph-eligibility/reproduction evidence.
- Every Q retains `walkingModelled`, `openingHoursUnknown`, `calibrationNotEvaluated`, `humanQualityNotEvaluated`; excludes arrival schedule, indoor reservations and interday transfers. No human pace burden/geographic suitability/interest-fit score, field calibration, live opening-hours assurance or guaranteed travel-time claim follows from this mechanical success.
- Human Q_live, field checks, provider/Host/Keycloak/database/target/rollout and ordinary CI remain separate. No external provider/model calls, network audit, install/restore, keys, Git mutation, cleanup or enablement occurred.

## Bound sequence and raw artifacts

Raw run directory: `TestResults/trips-m2/quality-demo-a872-v1/`. Build/discovery/full typecheck completed before binding; the one actual Fact ran already-built with --no-build --no-restore. No source changes/rebuild after binding. The recorder re-enumerated/hashed source,binary,build-input and installed environment inventories before execution and during final verification; final success/fatal=null confirms equality. All four manifest bytes also equal the reviewed P2fix1 freeze.

| Recorded command | Started UTC | Finished UTC | Exit |
|---|---|---|---|
| build | 2026-10-21T19:03:03.6586155+00:00 | 2026-10-21T19:03:06.7154761+00:00 | 0 |
| discovery | 2026-10-21T19:03:06.7227693+00:00 | 2026-10-21T19:03:08.9188536+00:00 | 0 |
| node-discovery | 2026-10-21T19:03:08.9318021+00:00 | 2026-10-21T19:03:09.9122999+00:00 | 0 |
| node-typecheck | 2026-10-21T19:03:11.6373953+00:00 | 2026-10-21T19:03:13.9438639+00:00 | 0 |
| sdk | 2026-10-21T19:04:14.6797401+00:00 | 2026-10-21T19:04:15.0402333+00:00 | 0 |
| runtimes | 2026-10-21T19:04:15.0438330+00:00 | 2026-10-21T19:04:15.0619793+00:00 | 0 |
| node-version | 2026-10-21T19:04:15.0660673+00:00 | 2026-10-21T19:04:15.0950730+00:00 | 0 |
| dotnet-execution | 2026-10-21T19:05:15.1727361+00:00 | 2026-10-21T19:05:31.2171431+00:00 | 0 |
| ts-execution | 2026-10-21T19:05:31.5336319+00:00 | 2026-10-21T19:05:33.3127844+00:00 | 0 |

| Artifact | SHA256 |
|---|---|
| `source-manifest.json` | `ed2ada080fbf05d84daaecdc1f464f6afc5a02c024a23f2ff781024108ae4493` |
| `binary-manifest.json` | `47008604aae1bb16b223c137282adcb572034785130ab302935f33959e5cb846` |
| `build-inputs-manifest.json` | `39878d20f8d0c231d7ab25f628586474fd5f2543989a8711c26ff4aee07ca7a6` |
| `environment-manifest.json` | `38cebdd339fda82d52900a9977d4efa078d7822cde50a5e92ed256983586682a` |
| `run-binding.json` | `49829aa1f7a083d1e1ac5d7f50880712c0bcb1af548ad2f6626621ea43308b8d` |
| `results.trx` | `590fa1a0532ab83a8144d409e9d7321d438222594f818bd4104d8cc355cea180` |
| `responses.json` | `987d77ee68aaa9d1253c97e026e8854545ffca11876232b33d9f326d784d5486` |
| `results-manifest.json` | `2657295cd968094432ae33c5e9fbef3ccb35b2703a2d299a0da3c979dbde878d` |
| `ts-decoder-results.json` | `750b664a152e4254a14bfb3d9ff58e6970a5c5881694a59ca15a855abedebf13` |
| `receipt.json` | `2d0f6ad3cbc24101c7849f73c180e108b865bb5590b989ad41024c5c4c59f176` |
| `case-summary.json` | `2b3362c490d278920fea8c6c52bbb89550126f22439474c21e9c23aa90818f91` |

- Acyclic chain: reviewed source/input/binary/environment manifests -> pre-output binding -> one raw TRX/all24exact bodies -> result manifest ->13Node raw report/final receipt. Receipt artifact hashes were reread/verified; original raw24TRX JSON records equal the extracted response records exactly; every body/exchange base64 length/SHA was rechecked read-only for this report.
- Exactly the three predeclared canonical outputs were created by the reviewed script: `shared/ts/api-client/tests/fixtures/trips-m2-q-demo-v1/responses.json`, `run-binding.json`, `results-manifest.json`. All three were verified byte-identical to their run-directory originals. Bodies were never normalized/reserialized; only their envelopes were formatted before hashes.
- Actual aggregate case checks: 1794. This is instrumentation, not extra test count or human quality evidence. No failures needed suppression; the earlier synthetic failure-path and all PhaseA failed receipts remain preserved.
- Existing Verify SolutionDir/SolutionName warning persists. No unexpected PhaseB failure occurred. Minimum required Cfree1.5GiB was enforced by the recorder; observed before submission2624241664bytes, with no cleanup.

## Next gate

The same independent reviewer must verify this actual raw chain,24/13counts,exact body/linkage hashes and unchanged-source evidence before parent closes task5.2. This worker changes no canonical task markers or acceptance docs. Source and generated fixtures remain frozen; no further run or tuning is authorized by this report.
