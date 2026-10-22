# Task 5.2 pre-output method review

SPEC: FAIL. QUALITY: needs-fix. One P2 method finding; no P1 or additional P3 findings. Phase B remains pending. This review does not report a Q/S result or close task 5.2.

## Reviewed identity and scope

Read the full current pre-output brief, including its final actual 22/27 handoff, task-5.2 brief/seam, frozen quality protocol/cases/recipes, implementation report, all four complete new method files, and the one-line auxiliary Flights mock typing delta. Reviewed all 90,662 bytes of the five-file patch by reading the full added source and verifying that each of its four addition hunks matches the corresponding current file line-for-line (914 C#, 169 TS, 375 recipe, 402 PowerShell lines); the fifth hunk only adds `<typeof fetch>` to the first mock. No source changes were made.

- Original requested base context: e654c76915103a82083ef2abc01c0579117c9749.
- Integrated HEAD context supplied by dispatch/retained receipt: a8721effd9056459d217966083816455fbea599d. No Git commands were run by this reviewer.
- Pre-output brief SHA256: 0e7256d4c01ae86e54b63ff0e1e49409cb3b2e4e4dda468a4c51b3f06be9e6b5.
- Implementation report SHA256: 275dc82f9f9422b787eb97e7242df90b948bcd24ad47f674f79da3252123e1d0.
- Full method patch SHA256: 803c57b8667ac98ba9593ba1601ab9123ad79dc4dae4c975d21577b23a39d35b.
- Owned-source manifest SHA256: 5eaac160d2d146e827bfd976623fb212716e38001a7b52144143d5ee57fb6e83.
- Record schema SHA256: a7dc4190a1cd8ba3feadf1d196e2dd2addf8d9abd2c5fdb146f6062f1999c272.

All 1,657 current source entries were independently checked for existence, length and SHA256 against the supplied source manifest: zero differences. The three predeclared future files were independently checked absent. No generation, harness Phase B, actual-Q Node execution, test rerun, build, install, network audit, browser, Host/DB/provider/model, credential/config content inspection, cleanup or Git mutation occurred. Only this review report is written.

## P2: inherited execution hooks are outside the supposedly closed input binding

Location: `.superpowers/sdd/tasks/task-5.2-run.ps1:340` (also native invocation at line 67, installed-byte inventory at lines 187-238, binding environment at line 352).

The recorder launches native commands with the process environment unchanged except for its few owned settings. Its only rejection checks DOTNET_ROLL_FORWARD, DOTNET_ROOT and MSBuildSDKsPath, and only late in Phase B. It does not reject or bind NODE_OPTIONS, NODE_PATH, DOTNET_STARTUP_HOOKS, DOTNET_ADDITIONAL_DEPS, DOTNET_SHARED_STORE, profiler activation, or ASPNETCORE_HOSTINGSTARTUPASSEMBLIES. The latter matters because the actual retained seam creates a WebApplication.

For example, a nonempty NODE_OPTIONS containing an external --require/--import path loads code in the metadata formatter, discovery and actual checker that is neither in source-manifest nor environment-manifest. A DOTNET_STARTUP_HOOKS path similarly adds an external assembly to .NET execution. Changing only that external file leaves all four current pre/post inventories equal. Runtime directories selected from runtimeconfig plus installed binaries do not identify those extra loaded inputs. Thus the current method can certify a closed binding while executing unbound code, directly contradicting the task's all-opened-input/runtime-closure requirement.

This is a method gap, not evidence that the supplied receipts were contaminated: a read-only, explicit-name presence check in this review process found all eleven named variables empty. No values, external hook files, profiles or credentials were inspected or emitted. The prospective recorder must enforce the condition itself in its actual execution process; a separate reviewer-process observation is not its bound execution envelope.

Smallest correction: add a bounded named guard before the first Node/dotnet invocation, reject unsupported execution-hook/module-path/runtime overrides without reading or printing their values, and retain only the checked names plus absent/disabled states in the binding. Cover Node startup/module resolution, .NET startup/additional-dependency/shared-store overrides, enabled profiling and ASP.NET hosting startup. Keep the existing version/runtime inventory and safe owned environment fields. Recheck the guard before execution (and at final inventory verification); do not dump the inherited environment, inspect hook content, silently clear user configuration, add a framework, or broaden into a machine audit. A pure synthetic presence-map guard proving rejection/acceptance is sufficient before re-freezing; no Q/S or actual-Q Node execution is needed. The original worker should make this recorder-only method correction and supply a fresh freeze for scoped recheck.

## Method portions that pass inspection

1. Q01-Q12 and S01-S12 are literal, with separate Q12/S-edge6/S-negative6 denominators. Original case SHA f50b7511fc12642b20df7ca207c780740382eeb4a482258929551ce29aa5756c and fixed clock 2026-10-21T14:16:33.0000000Z remain intact. S recipes are explicit fictional inputs. Checked their actual fixture: S04 uses later trip date beyond walk validity, S05 leaves only nonadjacent p0/p9, S06 leaves p0; closure evidence is added without altering real ReviewedV1 bytes. No hidden real-Q call occurs during declaration/discovery.
2. Every Q uses strict ReviewedV1, actual lean TestServer/Application/Core validation/API encoding and ordinary M1 codec/store path. CountedStore plus snapshot/revision/time checks enforce zero generation writes. Q01 explicitly edits a note, PUTs with revision, GETs and verifies preservation/null defaults/stable IDs. It remains complementary to the separately accepted fictional browser evidence.
3. Case-boundary catch retains failures and continues all 24; cancellation creates explicit unrun rows when the process remains able to record. Exact candidate/exchange bytes are captured as base64/length/SHA/status before semantic checks. The ordinary Fact asserts only after the full denominator is emitted. Process death cannot promise rows; the extractor appropriately hard-fails incomplete raw data rather than inventing them.
4. Target fill uses actual per-day counts, total and distinct POIs. All-case assertions and >=11/12 target fill are conjunctive. References, evidence scope/source/freshness, directed consecutive edges, path bytes/hash/node-way sequence/anchors, independent integer 75/45 formula, model/profile labels, attribution and unknown access/hours/prices are inspected. Prior D_demo carries offline graph eligibility; this harness does not claim an independent OSM rebuild. Human actors/scores/calibration stay null/not-evaluated.
5. The retained Node spec has twelve literal raw-body production-decoder checks plus one complete inventory/linkage check. Files are read within tests, so discovery neither consumes missing fixtures nor generates outputs. Missing files fail; no skip/dynamic empty tests. It hashes exact decoded body bytes and cross-checks candidate exchanges. Default future CI does not require historical Windows paths, DLLs or installed historical SDKs, and is labelled historical regression.
6. Recorder rejects existing run/future outputs, checks both context SHAs and ancestry, covers untracked source and deletions, and re-enumerates actual directory inventories to detect additions. Future exclusions are exactly three files. Executing binary/copy payload, generated build inputs, actual installed package/SDK/runtime/interpreter files and lock/config/decoder/recorder source are included. Restore-time NuGet paths are context only; no config contents are opened.
7. Binding is create-only, closed, hashed and reread before first execution. No rebuild occurs after binding. Source/binary/build-input/environment -> binding -> raw TRX/body records -> results manifest -> later receipt is acyclic. Canonical fixture bytes are created once. Metadata formatting does not reserialize candidate bodies.
8. Nonzero .NET exit does not short-circuit ReadRecords/extraction/Node checks when a complete 24-row TRX exists; final receipt remains failed. Wrong/missing/duplicate/reordered records, wrong Fact count/skips and bad body hashes hard-fail. Raw results remain retained. Later source correction would require a new binding and retained failure history, never automatic rerun/tuning.
9. The auxiliary Flights delta is test typing only. Existing assertions remain intact; supplied full tsc and focused 19-test green receipts support that bounded change.

## Evidence read and independently hashed

Directory prefix below: `TestResults/trips-m2/phasea-source-freeze-a872-04/`.

| Artifact | Count / outcome | SHA256 |
|---|---|---|
| source-manifest.json | 1657; all current entries match | e3bdc08aeb9795d2f576a3645ca08111276ee1b39d85c91ae5f302ed733cd9f7 |
| binary-manifest.json | 718 | 47008604aae1bb16b223c137282adcb572034785130ab302935f33959e5cb846 |
| build-inputs-manifest.json | 91 | 39878d20f8d0c231d7ab25f628586474fd5f2543989a8711c26ff4aee07ca7a6 |
| environment-manifest.json | 68095 | 38cebdd339fda82d52900a9977d4efa078d7822cde50a5e92ed256983586682a |
| build.txt | 0 errors, existing Verify warning | 0aa68123573b51fcf3b0c167cd4423f73d6f42b3632ee1eab10973f8a0aa9332 |
| discovery.txt | exactly 1 ordinary Fact | a4368eb09250bab3899ec1b5ad969d57a1fada85cd547682fe9d221962cbbd9e |
| node-discovery.txt | exactly 13 literal tests | 8f59cf1ea71f40b542490b0637244080d5193c03d8e25519b2007288ad913e2d |
| node-typecheck.txt | empty stdout; retained command exit 0 | e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855 |

Read retained SDK/runtime/Node outputs: SDK10.0.301, active net10.0.9, Node26.11.1. The retained sdk.txt environment section contains only the recorder's two explicit DOTNET values; this review makes no allegation of secret export there. Installed version identification is from actual package JSON, not declared ranges.

Read the synthetic recorder guard report/receipt, count 11 and realQExecuted=false. Receipt SHA256 93072486b5d00b074f21856a7d068e2749e5a6bdc52d235c365e1801f674c075; guard-report SHA256 3a6636c7539298aeccfcc5ca1fc3ffb2da1aa06760e2fbe4ab8579fca4742177. Read focused Flights final receipt (19 passed, none failed/pending/todo) SHA256 e0ecb2523f70ee96e76186b2256de972760567c3761af667e26e4d002e054549. These are supplied execution evidence, not suites rerun by this reviewer.

## Remaining gates and limits

The final task4.8 acceptance (22/27; review93e0d777... PASS) is supplied by the parent and was not rerun/re-audited here. This is the pre-output method review only. No real Q/S outcomes have been observed. After the one method correction, fresh freeze and scoped PASS, the parent must explicitly authorize the original worker's Phase B; this reviewer then checks actual raw 24+13 chain, exits, failures and unchanged-source receipts. Broad fresh task6.1 audit remains separate. Human field quality, model calibration, network audit, ordinary CI, Host/Keycloak/target and rollout are not closed by this review.
