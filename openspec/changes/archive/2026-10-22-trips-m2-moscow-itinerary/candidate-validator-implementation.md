# Task 2.3 independent Core validator report

Status: implemented and locally verified; independent parent review pending. Worktree `C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency`, branch `codex/trips-m2-moscow-itinerary`, HEAD remained `e654c76915103a82083ef2abc01c0579117c9749`.

## Ownership and interface

Only new validator, its own unit test file, task-2.3-interface.md and this report were written, plus task-owned raw verification receipts under TestResults/trips-m2/validator. No Policy/Candidate/Facts/shared-fixture/README/canonical-task/Application/API/UI edits. No Git mutations, subagents, installs, restore, solution/integration/Docker/Host/database/network/model/provider/real-data/Q execution.

Public signature was published before implementation for Application 3.2: `ItineraryCandidateValidator.Validate(TripBrief, DestinationSelection, ValidatedItineraryFacts, ItineraryOutcome, DateTimeOffset) -> ErrorOr<ItineraryOutcome>`. Valid returns the same whole original object. Invalid produced output, null output and stale/not-yet-built supplied facts return stable Failure `Trips.ItineraryUnavailable` / `Itinerary generation is unavailable.` Normalized input/null/binding/non-UTC argument errors return `Trips.InvalidInput`. No exception text or repair.

## Independent checks

- No call to Generate or policy private helpers. Pure explicit UTC instant, no clock reads/effects. Destination IDs/IANA/catalogue bind to saved selection; output snapshot includes exact saved label while package label need not match. Dataset/version/hash/policy, generation instant and expiry match. Preparation/model source equality checks values, including publication/download/build/input hashes, versions and validity; equivalent distinct objects are accepted.
- Current build/release/model leases precede every branch. Local scope uses saved IANA zone: requested dates exactly once ascending; range/historical/horizon unsupported has no fake route, zero expansions and exact reasons.
- Bounded roots/lists/strings are checked before member enumeration. Stops require original POI/title/date, nonempty globally unique UUID and POI, null initial times/money, exactly identity and access refs. Identity evidence must be current and applicable for the whole local day with original scope/fields/source-record/CC0. Access intervals and exceptions are recomputed with current-instant and whole-day applicability; closed is forbidden, missing/expired remains unknown with precise reason and no intervals.
- Historical note must exactly match the complete initial dataset/version/asOf/POI reference and OSM/ODbL credit/URLs, <=500 Unicode scalars; appended feasibility/observed/budget claims fail, no truncation. Validator uses C# `\u00A9` for the exact copyright character.
- Every edge must match exact adjacency direction, scoped current walking evidence, original restrictions and modelled/ODbL/no-observation semantics. All source/path/hash/length/crossing/anchor/profile/selector/model values bind to source facts. Integer scenario lower/upper are recomputed independently using quotient/remainder; no geometry/router runtime.
- Per-day target/walking/dwell+walking limits are recomputed from source dwell and recomputed edge upper bounds. Global requested-interest coverage is a distinct union across all selected days; food remains unmet. Filled requires all target slots and interests, no search limit. Candidate iff any selected stops, Empty iff none. All reasons/exclusions/uncertainties are exact ordinal-sorted lists; hidden access/origin/budget/model uncertainty and fabricated/duplicate codes fail.
- Counter is 0..20000 and at least selected stops; Unsupported is zero. Supported counter also fits independent current bound `dayCount * POIcount * (1 + (target-1)*16)`, at most7056. SearchLimited requires cap count and therefore cannot fit current valid shape. Parent confirmed this necessary bound validation; it neither replays the trace nor claims ranking optimality/actual cap exhaustion. Increasing shape or changing algorithm/version requires revisiting this proof.

## Fresh receipts

All logs/TRX below were captured from actual new runs. No reconstruction of prior baseline or Integration receipts.

| Receipt stem in TestResults/trips-m2/validator | Executed | Passed | Failed | NotExecuted | Meaning |
|---|---:|---:|---:|---:|---|
| red |88|3|85|0|Clean semantic initial RED: rejection assertions fail against deliberate passthrough stub; valid same-object positives pass.|
| red-extended |104|7|97|0|96 semantic failures plus one invalid fixture error (dwell150 exceeds Core120); retained as failed diagnostic, not clean RED proof.|
| green |104|103|1|0|Initial implementation; same invalid fixture failed.|
| red-additional |6|0|6|0|Clean semantic RED with corrected dwell120 and additional sum/repeated-tag/cross-day tests, deliberate passthrough stub and finally-restored implementation.|
| green-final |110|102|8|0|Formatter subprocess input encoding transliterated copyright to c; existing candidate-positive tests detected exact-note mismatch. Retained failed diagnostic.|
| green-verified |110|110|0|0|Corrected ASCII Unicode escape, fresh focused final GREEN.|
| full-unit |246|246|0|0|Fresh full Trips Unit:136 prior +110 validator cases.|

Both changed C# files pass CSharpier (`format-final.log`: Checked2files,770ms). No warning/error was emitted by final test/format commands. Counters were separately read from saved TRX. C: free before first write5,837,594,624bytes; final5,749,108,736bytes. All existing source edits used same-directory temporary file and atomic File.Move replacement. No cleanup performed.

Commands (PowerShell, from named worktree; each run uses its own exact stem shown above):

```powershell
dotnet test tests/trips/Travel.Modules.Trips.Tests.Unit/Travel.Modules.Trips.Tests.Unit.csproj --no-restore --filter FullyQualifiedName~ItineraryCandidateValidatorTests --verbosity minimal -p:SolutionDir=C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency/ -p:SolutionName=Travel --logger 'trx;LogFileName=green-verified.trx' --results-directory TestResults/trips-m2/validator 2>&1 | Tee-Object -FilePath TestResults/trips-m2/validator/green-verified.log

dotnet test tests/trips/Travel.Modules.Trips.Tests.Unit/Travel.Modules.Trips.Tests.Unit.csproj --no-restore --verbosity minimal -p:SolutionDir=C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency/ -p:SolutionName=Travel --logger 'trx;LogFileName=full-unit.trx' --results-directory TestResults/trips-m2/validator 2>&1 | Tee-Object -FilePath TestResults/trips-m2/validator/full-unit.log

dotnet csharpier check modules/trips/Travel.Modules.Trips.Core/Itineraries/ItineraryCandidateValidator.cs tests/trips/Travel.Modules.Trips.Tests.Unit/ItineraryCandidateValidatorTests.cs 2>&1 | Tee-Object -FilePath TestResults/trips-m2/validator/format-final.log
```

Every invoked command preserved `$LASTEXITCODE` and exited with it; complete logs were persisted before output tailing. Initial RED commands are the focused command with red/red-extended stems. Additional RED filter was `FullyQualifiedName~Day_duration_uses_source_dwell|FullyQualifiedName~Walking_upper_sum|FullyQualifiedName~Repeated_tags_cannot|FullyQualifiedName~Uniqueness_and_date_order`. Formatter read owned files through stdin and wrote stdout to a same-directory temp then File.Move; no other source file was formatted.

## SHA-256 of reviewed sources and successful receipts

| Path | SHA-256 |
|---|---|
| modules/trips/Travel.Modules.Trips.Core/Itineraries/ItineraryCandidateValidator.cs |5f8defec3c0b7aefabaad17f7f1159e53972a15b20ff15a1395c82d1cc90ca61|
| tests/trips/Travel.Modules.Trips.Tests.Unit/ItineraryCandidateValidatorTests.cs |a9086d8443bddd8ac9fe2aeba2f5ecdbd8d589c09b367d84b6adb12e11997edc|
| .superpowers/sdd/tasks/task-2.3-interface.md |c69e7506ecf50c50a465cbb306fc9c189c671cd5de1feb88e75869c70d15847c|
| TestResults/trips-m2/validator/red.log |3c526950dbff686e727cd70d232f78ba18c43002e1526e9245df09138f196af4|
| TestResults/trips-m2/validator/red.trx |ce5224e376cd3647b0a51c13185f9d582404c5844def4d0e51d0323f20ce32cf|
| TestResults/trips-m2/validator/red-additional.log |2e39db007b1d3c8934248a87313987b0ae87b5d86222a78e22d21b9610a81eec|
| TestResults/trips-m2/validator/red-additional.trx |240a2c088871c7938959218cac4cdbc3368213a6e9320b7339e92be280dfcf30|
| TestResults/trips-m2/validator/green-verified.log |74705473cb9c1bf664134db035dacd0294834d6e71f227f4688393caddf8306b|
| TestResults/trips-m2/validator/green-verified.trx |927850ae15c0750439bca1319478e681da0bb33bd068adeae2de40dd35cab57e|
| TestResults/trips-m2/validator/full-unit.log |b0ead7cbf1a21fc5208b9247fd0832dda95d9cb8ce8cd7578b43003f1b31efa7|
| TestResults/trips-m2/validator/full-unit.trx |a718eb5c06d984ff3671dff4b41a4caa72b49fefeadb65c7002f176f7b68ffc1|
| TestResults/trips-m2/validator/format-final.log |2d2d7e47718e1193a5988405eab454953609dab3418c4e41fb81cb5cb5481e26|

## Remaining boundary

This establishes source/local fictional structural checks only. Parent independent review, Application/API/UI consumer work, real-data/Q_demo evidence, ordinary PR/postmerge CI, Host/Keycloak/target and rollout are separate. No real-source quality, legal/live compliance, ranking optimum, walking feasibility or empirical cap-exhaustion claim is made. Baseline136/136 and loader98Integration were dependency context, not evidence executed by this task. Parent owns canonical task checkboxes/README/final acceptance.