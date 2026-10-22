# Task 5.2 actual first Phase B chain review

SPEC: PASS. QUALITY: PASS for the actual bound mechanical Q_demo/structural/integration chain. No P1/P2/P3 findings. This is not human route-quality, calibration, CI or target acceptance.

## Scope and identity

Same reviewer as the pre-output method review and P2 recheck. Read the current PhaseB review brief first, then the execution report, dispatch, actual binding, manifests, original TRX, extracted corpus, result manifest, actual Node JSON report, final receipt and post-run report links. Did not repeat the full method review. Used only read-only artifact/hash/JSON probes; no harness, generator, actual-Q tests, build, installed-tool execution, environment-value query, source edit, subagent, Git, network, Host/DB/provider/model, cleanup or retry. Only this review report is created.

Verified inputs:
- Brief SHA256 0e210652b04b7a48e0b00b7c41688b8c4bd0886523afd993d95edd412f162d09.
- Worker report SHA256 9c1c6e50f60a6fb73ddc6c7441039a8ad1b5c7b24b37059565dd49c7a5b67ad2.
- Parent dispatch SHA256 9bcc7c761802c1f2c1c8a3bdf3c65438ff32fe9e9ab914849f889004ee2657e3; recorded19:02:26.7224875Z before binding and outputs, linking accepted4.8 and method recheck.
- Reviewed method recheck remains 3a5c0d92ae21f67991ceac6ae5ae7c19b33699e51358e884ff6a896a1c4cf736; recorder remains35c528c17257a77b6c046e14316a93aba8e4b976fd7a2531ecca49cbf1dbb005.
- Original requested base e654c76915103a82083ef2abc01c0579117c9749 and integrated HEAD a8721effd9056459d217966083816455fbea599d remain distinct context fields. Actual uncommitted bytes are bound separately.

## Binding, execution order and unchanged inputs

Binding created2026-10-21T19:04:15.1070295+00:00, before the single recorded dotnet-execution started19:05:15.1727361+00:00. Build/discovery/typecheck are earlier. Actual .NET command retains --no-build and --no-restore, with the exact filtered ordinary Fact. There is one recorded .NET execution and one recorded Node execution; no post-binding build. The parent supplies the one authorized invocation/session61232 and final exit0; the retained command chain is consistent with it, with no hidden retry/tuning indication.

All four run manifests are byte-identical to the reviewed P2fix1 freeze. Independently rehashed all1657 current source entries: zero missing/changed files. Independently re-enumerated the source inventory using its exact directory/generated-path/future-output boundaries:1657paths, zero new/missing paths after excluding exactly the three declared outputs. Independently checked all718 actual binary payload entries and six package files against bound hashes: zero differences. Original cases, S recipes and protocol match their binding hashes. Fixture clock stays exactly2026-10-21T14:16:33.0000000Z; no date shift or package/model/profile/oracle change.

Installed environment/build-input manifests remain identical to their reviewed versions; the reviewed recorder's successful final verification supplies their actual pre/post equality. This review did not redundantly hash all68095 installed environment entries. The actual binding/receipt contain12named absent hook states; finalExecutionHookCheckPassed=true. No inherited environment values were inspected by this reviewer.

## Raw chain and denominator verification

Independently verified all19 final-receipt artifact references by length and SHA256. Verified result-manifest links to exact binding, TRX and corpus bytes, plus source/binary manifest linkage. Each of the three canonical fixture files is byte-identical to its original run-directory artifact. Post-run report-manifest links to report/case-summary/original receipt/binding also verify. The result manifest does not hash itself; later receipt supplies its hash.

Parsed the original TRX: exactly1ordinary UnitTestResult, Passed; counters total1/executed1/passed1/notExecuted0. Extracted its24literal prefixed records in the exact Q01..Q12,S01..S12 order and compared every parsed metadata record with responses.json: all equal. All24execution states are completed, failures empty; Q12/S-edge6/S-negative6 are retained separately. No failed/unrun/missing case is omitted.

For every record and all50HTTP exchanges, independently decoded the retained base64 and checked canonical base64 roundtrip, exact byte length and SHA256. Each record's candidate bytes/status equal its actual candidate exchange. These checks operate on retained bytes; no HTTP body was normalized or rewritten. The extra two exchanges are Q01 explicit-save and reload, above the24create+24candidate exchanges.

Actual Node report contains exactly13passed assertions:12literal real-Q production-decoder cases and1full-corpus integrity case;0failed/pending/todo. The bound checker source calls the actual strict production raw decoder, as already reviewed. No assertion was rerun here. Raw .NET summary independently reads filledQTargets12, required11, failed0,unrun0. Instrumented1794checks are not additional tests.

## Actual response checks

Read the decoded retained bodies, not only green console summaries. Independently recalculated every Q target from day count, each day's stop count, total stops and distinct POI IDs. Results are12/12filled with targets2,3,4,4,6,8,6,9,12,3,6,9 and the predeclared daily splits. All12are HTTP200/candidate, exact baseRevision1 and fixed generatedAt; expiry is15minutes later.

The Q corpus has72stop instances and48directed modelled-edge instances across cases, sourced from the unchanged20POI/90edge package. These are instances, not72distinct source POIs. Independently cross-checked all used POI titles/coordinates/Wikidata identities/dwell durations against the payload; access facts remain unknown, opening intervals empty, all time/estimate defaults null and historical notes explicit. HumanQuality/calibration remain not-evaluated with null actors/scores. Required uncertainties, scope exclusions, OSM attribution/ODbL and reproduction offer are visible.

Cross-checked each used edge with its bound source edge, direction and consecutive output stop pair; path identity/hash/length/crossings/anchors; retained raw path-object hash and node/way length relationship; and independently recomputed the75/45integer-minute formula. Evidence scopes/source URLs/rights/freshness and null observedAt match the source. Each Q evidence set contains exactly the referenced evidence. Profile/model/selector/input/publication/timestamps and datasetHash agree with the bound package. Offline graph eligibility and reproduction remain the separately accepted D_demo proof, not a newly claimed OSM rebuild here.

S outcomes read from actual retained bodies:
- S01/S02: HTTP200candidate,3stops, partial and explicit unmet food.
- S03: HTTP200candidate,3stops with explicit unknown hours.
- S04/S05/S06: HTTP200candidate, partial, exactly1stop each and exact empty edges arrays; no expired/invented adjacency or known-closed POI. S04date is2026-11-18; S05/S06retainp0.
- S07:404/Trips.NotFound. S08:412/Trips.RevisionMismatch.
- S09/S10/S11/S12: HTTP200unsupported with destinationUnsupported/dateRangeUnsupported/historicalDatesUnsupported/horizonUnsupported, without candidate days.

Q01 raw exchanges are create201 -> candidate200 -> explicit-save200 -> reload200. Verified original revision1 becomes2 only at saved reload; title and brief preserved; both proposed UUIDs survive, each persisted historical note is the proposal note plus the explicit local edit, and times/estimate remain null without trusted preview facts/poiId in M1. The bound real test also asserts zero generation store writes and unchanged pre-generation snapshot/revision/timestamps, with no recorded failures. HTTP bodies alone do not expose internal write counts; that part is established by the unchanged reviewed instrumentation and its successful Fact. This is lean real-package backend save/reload evidence complementary to prior fictional browser acceptance, not real-browser/Host/database proof.

Reviewer probe notes: an initial dataset-shape lookup and a PowerShell hash-helper alias collision were corrected in read-only inspection; an initial S04-S06 nested-array count counted the empty array wrapper, then direct per-day inspection confirmed edges=[]/count0. These were reviewer inspection issues, not product/test failures, and required no source/output changes or test reruns.

## Exact evidence hashes

Run directory: `TestResults/trips-m2/quality-demo-a872-v1/`.

| Artifact | SHA256 |
|---|---|
| source-manifest.json |ed2ada080fbf05d84daaecdc1f464f6afc5a02c024a23f2ff781024108ae4493 |
| binary-manifest.json |47008604aae1bb16b223c137282adcb572034785130ab302935f33959e5cb846 |
| build-inputs-manifest.json |39878d20f8d0c231d7ab25f628586474fd5f2543989a8711c26ff4aee07ca7a6 |
| environment-manifest.json |38cebdd339fda82d52900a9977d4efa078d7822cde50a5e92ed256983586682a |
| run-binding.json |49829aa1f7a083d1e1ac5d7f50880712c0bcb1af548ad2f6626621ea43308b8d |
| results.trx |590fa1a0532ab83a8144d409e9d7321d438222594f818bd4104d8cc355cea180 |
| responses.json |987d77ee68aaa9d1253c97e026e8854545ffca11876232b33d9f326d784d5486 |
| results-manifest.json |2657295cd968094432ae33c5e9fbef3ccb35b2703a2d299a0da3c979dbde878d |
| ts-decoder-results.json |750b664a152e4254a14bfb3d9ff58e6970a5c5881694a59ca15a855abedebf13 |
| receipt.json |2d0f6ad3cbc24101c7849f73c180e108b865bb5590b989ad41024c5c4c59f176 |
| post-run-report-manifest.json |95a825aedc9247c1fbce43318d40d6f13a19fe9cef0ccf0120305ad84131b444 |

Final receipt: successful=true,bound=true,fatal=null,dotnetExitCode0,tsExitCode0; the authorized parent reports final process exit0. No quality-driven tuning or confirmatory rerun is triggered by this passing corpus. Parent may now close task5.2 within its source/local scope. Whole-M2 fresh6.1 audit, ordinary CI, human Q_live/field calibration, Host/Keycloak/target and rollout remain separate. Existing CSS/UX triage is outside this raw-chain review.
