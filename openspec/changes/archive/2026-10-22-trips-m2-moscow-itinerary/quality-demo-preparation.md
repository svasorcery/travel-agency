# Task 5.2 Phase A — P2 method correction, fix round 1

**Recorder-only correction prepared and frozen for the same reviewer’s scoped P2/new-breakage recheck.** Current4.8 is already accepted22/27 per parent; its broad suites were not repeated. No Q/S generation, actual-Q Node execution, PhaseB, run-binding or any of the three future fixtures. Independent pre-output PASS and explicit parent PhaseB message remain required.

## Exact review / preserved before state

- Review: `.superpowers/sdd/tasks/task-5.2-pre-output-review.md`, SHA256 `5a9a0458f82268242064e4cfd5be608bf8d26ae4ddd9b58f3677c9e478e6ad61`: SPEC FAIL / needs-fix, one P2 inherited unbound-code-loading environment finding. Review source was not edited.
- Complete previous freeze/source/manifests/schema/90,662-byte full patch/report remain in `TestResults/trips-m2/phasea-source-freeze-a872-04/`. Prior report SHA `275dc82f9f9422b787eb97e7242df90b948bcd24ad47f674f79da3252123e1d0`; prior full patch SHA `803c57b8667ac98ba9593ba1601ab9123ad79dc4dae4c975d21577b23a39d35b`.
- Before editing, separately copied recorder/report/review with exact hashes into `TestResults/trips-m2/phasea-p2-fix1-a872/`. `before-link.json` and `after-link.json` identify both freezes, reviewed input, exact source and diff hashes.
- Original requested base remains `e654c76915103a82083ef2abc01c0579117c9749`; integrated HEAD remains `a8721effd9056459d217966083816455fbea599d`. Recorder verified ancestry/current branch without Git mutation. Base/HEAD are context, not a claim that feature bytes are committed.
- Clock remains EXACT `2026-10-21T14:16:33.0000000Z`; original cases remain SHA `f50b7511fc12642b20df7ca207c780740382eeb4a482258929551ce29aa5756c`. C#/TS/S recipes/Flights mock/cases/protocol/Data/model/oracles remain unchanged.

## Only method correction

Added one finite named execution-envelope guard to the recorder. It runs before the first Node/dotnet invocation, including the metadata formatter, and repeats immediately before every recorded native call, direct formatter invocation, SDK resolution, binding construction and final verification. It replaces the prior late PhaseB-only three-variable check.

| Checked names | Allowed state |
|---|---|
| NODE_OPTIONS, NODE_PATH | absent/empty only |
| DOTNET_STARTUP_HOOKS, DOTNET_ADDITIONAL_DEPS, DOTNET_SHARED_STORE | absent/empty only |
| CORECLR_ENABLE_PROFILING, COR_ENABLE_PROFILING, DOTNET_ENABLE_PROFILING | absent/empty, or literal0 represented as disabled |
| ASPNETCORE_HOSTINGSTARTUPASSEMBLIES | absent/empty only |
| DOTNET_ROLL_FORWARD, DOTNET_ROOT, MSBuildSDKsPath | absent/empty only |

Only these12 names are queried. The collector checks emptiness or literal profiler zero, discards the local value and returns only names plus absent/disabled states. Unsupported state rejects with the variable name only. No inherited-environment enumeration/dump, option/path parsing, hook-file inspection, credential/config content reading, value hashing/copy/export, silent clearing or global environment mutation. Synthetic tests use state maps, not real environment mutations.
Initial safe states are compared before later native calls; even an absent→disabled drift is rejected. The binding records `executionHookEnvelope`; the final receipt records the same safe state map and `finalExecutionHookCheckPassed`. Final receipt serialization is native-free PowerShell JSON so reporting a newly unsafe hook cannot itself launch the Node formatter. Binding and eventual corpus metadata continue to use the guarded Biome formatting before hashes; raw HTTP bytes/base64 are untouched.

## Fix-only and full review packets

- Only changed source entry: `.superpowers/sdd/tasks/task-5.2-run.ps1`, SHA256 `35c528c17257a77b6c046e14316a93aba8e4b976fd7a2531ecca49cbf1dbb005` (before `340b883b137e0199a893d2e3346edb0437dd59cf616ea4776f88fddb369cd3e1`).
- Fix-only diff: `TestResults/trips-m2/phasea-p2-fix1-a872/fix-only.patch`, SHA256 `d753a7394aaba5db648966610008451165bca1c652bff6a97b337c6573bd604a`.
- Before/after link: `TestResults/trips-m2/phasea-p2-fix1-a872/after-link.json`, SHA256 `14f9e6d4252daa5d3ac312b01759be8167bca7981969cd51250346963ac8acdd`.
- Updated full five-file method patch: `TestResults/trips-m2/phasea-source-freeze-a872-p2fix1/source-review.patch`, SHA256 `43923d5d4b042bb11f8378afb6b3c052ef43ecefcfecac13e8cf891f6d9b1de6`. All five final source copies and an owned-source manifest are retained there. The other four files match prior hashes exactly.
- Existing case-record schema is unchanged, SHA256 `a7dc4190a1cd8ba3feadf1d196e2dd2addf8d9abd2c5fdb146f6062f1999c272`. New envelope metadata schema: `execution-envelope-schema.json`, SHA256 `6fb0e7fb922ea6100fd93462916aec0af2c352d8469d32d2f5aea5f4675e9395`. These are documentation artifacts, not Q outputs.

## Actual verification in this round

- PowerShell parser PASS.
- `& ./.superpowers/sdd/tasks/task-5.2-run.ps1 -Phase RecorderGuards -RunId phasea-recorder-guards-a872-p2fix1`: **32/32 PASS**, realQExecuted=false. Original11guards retained;21new checks cover all-absent acceptance, each12unsupported state rejection, each3profiling disabled acceptance, each3state-drift rejection, incomplete-map rejection and rejecting a false disabled claim for NODE_OPTIONS.
- Guard report SHA256 `6c8fa6508e6b2d10758dfa2dfa12b6dbad4dfd4a291b4a139ec7982edef11f8f`; receipt SHA256 `ce52a14d0bf252e7aba1bb5e5d7898b20f262221544fda29e69a1ac0a21f880f`. The actual process envelope showed all12checked names absent; no values are retained.
- `& ./.superpowers/sdd/tasks/task-5.2-run.ps1 -Phase PhaseA -RunId phasea-source-freeze-a872-p2fix1`: successful=true,bound=false,finalExecutionHookCheckPassed=true. All recorded command exits0.
- Integration Debug build `--no-restore`:0errors, unchanged existing Verify SolutionDir/SolutionName warning. `dotnet test --no-build --no-restore --list-tests --filter FullyQualifiedName~TripsItineraryDemoIntegrityTests`: exactly1ordinary Fact discovered, never executed.
- Direct Vitest `list ... src/trips-itinerary.real-demo.spec.ts`: exactly13literal checks discovered, never executed. Full api-client `tsc --noEmit -p shared/ts/api-client/tsconfig.spec.json`: exit0.
- No broad architecture/client/browser reruns, no focused Flights rerun, no package acquisition/restore, network audit, Host/AppHost/DB/provider/model/key action or cleanup.

## Fresh freeze / unchanged closure

All new artifacts: `TestResults/trips-m2/phasea-source-freeze-a872-p2fix1/`. Source path sets are identical to04:1,657entries, exactly1changed recorder and1,656unchanged entries. Binary/build-input/environment manifests are byte-identical to04, confirming this correction did not change compiled/runtime/package inputs.

| Manifest | SHA256 | Relation to04 |
|---|---|---|
| `source-manifest.json` | `ed2ada080fbf05d84daaecdc1f464f6afc5a02c024a23f2ff781024108ae4493` | new metadata/source identity |
| `binary-manifest.json` | `47008604aae1bb16b223c137282adcb572034785130ab302935f33959e5cb846` | identical |
| `build-inputs-manifest.json` | `39878d20f8d0c231d7ab25f628586474fd5f2543989a8711c26ff4aee07ca7a6` | identical |
| `environment-manifest.json` | `38cebdd339fda82d52900a9977d4efa078d7822cde50a5e92ed256983586682a` | identical |
| `phase-a-source-freeze.json` | `3482dd4b3cc9696870ed5a68e1bf4e93c8d4ef9754f9a3463fcbfb57c9dd8cdb` | new metadata/source identity |
| `receipt.json` | `53a66d8142079e2c1e265fb486e65be65d70e384e0d6806aaf24065c241f2708` | new metadata/source identity |

PhaseA metadata is not an evaluation run-binding. Three future fixtures still absent: `shared/ts/api-client/tests/fixtures/trips-m2-q-demo-v1/{responses.json,run-binding.json,results-manifest.json}`. Per-process a872 D tmp/Nx cache and short socket directory remain; last Cfree2.46GiB, above1.5GiB floor.

## Remaining limits / next gate

The original review did not allege contamination of earlier receipts; this correction makes the checked execution envelope enforceable in the recorder process. The named policy is deliberately bounded to the reviewed runtime hooks/overrides (plus the DOTNET profiling alias), not a general machine/config audit.
The actual PhaseB branch and Q/S corpus remain unexecuted. The same reviewer must now recheck this P2 and new breakage only; task6.1’s broad fresh audit remains separate. After independent PASS, parent must explicitly dispatch PhaseB. No source edits/rebuild may occur after its future binding; prior failures and create-only first-run rules remain intact. Human Q_live/field calibration, CI, target and rollout remain pending.
