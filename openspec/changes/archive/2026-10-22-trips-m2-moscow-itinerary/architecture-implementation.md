# Task 3.5: retained architecture and ordinary CI source proof

Source/local implementation complete; ready for parent fresh independent review. This does not accept all Trips M2, remote CI, Host/Keycloak, package quality, target provisioning or rollout. Active worktree: `C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency`. Parent-provided fixed HEAD `e654c76915103a82083ef2abc01c0579117c9749`, branch `codex/trips-m2-moscow-itinerary`; no Git command, branch switch, stage, commit, push, merge or ancestry rewrite performed by this worker.

## Scope and retained consumers

Only the ten Architecture C# paths below and one step in `.github/workflows/ci.yml` were edited. This report and isolated receipts are worker-owned outputs. Production Core/Application/Infrastructure/Api, Host, global guard, project references, .NET inventory sources, root Nx/package/formatter configuration, other modules, TS/UI and canonical documents were not edited by this worker. No subagents were spawned.

`ModuleArchitectureInventory` adds exactly the own-assembly nonempty `Trips.Core.Itineraries`, `Trips.Application.Itineraries`, `Trips.Infrastructure.Itineraries` required rows. `LayerArchitecturePolicy` adds `Trips.Api.Contracts` while preserving Endpoints/Middleware/Composition and all prior rows. The broad six-module/four-layer/24-assembly inventory and production type+IL and evaluated Debug/Release reference matrices remain intact.

Controlled selector negatives remove each exact new namespace subtree from the real production collection; each removal now yields its one exact missing-selector error. Each test also asserts the unmodified collection passes and the removed selection was nonempty. The real model explicitly contains ItineraryPolicy, ItineraryCandidateService, CuratedMoscowFacts, ItineraryCandidateResponseEncoder and both actual itinerary endpoints in their own production assemblies.

Two small fixture groups reuse existing policies. Exact origin/target expectations prove Trips Core/Application.Itineraries -> own CuratedMoscowFacts forbidden; Trips Api Endpoints/Contracts/CompositionExtra -> same Infrastructure type forbidden; exact Api.Composition -> same Infrastructure type allowed with a nonempty dependency. Trips Application has separately asserted Flights IataCode, Hotels HotelPropertyReference and Rail StationRef internal violations. Trips Application and Api reject concrete AI NlSearchRequested type dependencies; one signature-hidden Api AI method-body edge and one Application Flights edge are scanned and rejected by the existing IL policies. Existing Hotels -> Flights fixtures remain. The imported foreign-reference test keeps the Identity/Flights proof and additionally evaluates Trips Application in both Debug (empty/allowed) and Release (imported Flights reference/rejected). Additional direct AI consumer cases retain Hotels and add Trips Application/Api, requiring the actual unexpected project in the error; the existing evaluated property-import test remains unchanged. Trips is not added to any contract allowlist.

Host controlled proof rejects the real ItineraryCandidateEndpoint and positively sees/allows the real TripsModule Composition facade. Existing six-facade exact project set, paired Rail/Trips eight-layer closure and own-Infrastructure positives remain unchanged and passed in the full run. Trips Core/Application/API source fixtures also exercise existing global AddMarten/UseWolverine/MapWolverineEndpoints guards. No Host builder, endpoint map, project closure or production callsite changed.

These are concrete module/Infrastructure ownership and named AI-contract rules. No universal external DTO or package prohibition is claimed. The actual four Trips project references are retained and fingerprinted in `source-sha256.json`.

## TestOnly boundary and API receipt

CuratedMoscowFacts retains a direct `TestOnlyAttribute` checked with `inherit:false`. This is structural marker proof only. `TestOnlyGuard` still checks `ServiceDescriptor.ImplementationType`; a factory can have a null ImplementationType, and this task does not claim to fix that gap. Actual default-off/Production omission, disabled-before-auth, manual M1 preservation and fail-closed composition/runtime behavior belong to accepted task3.3/3.4 tests and receipt `.superpowers/sdd/tasks/task-3.3-3.4-recheck.md`, verified current SHA256 `d11c920c083ab69b11b9c59718c77fd1805b917db657a6a55ba904cbbc4e854e`. Its retained boundary85/85 and M1 HTTP30/30 results are referenced, not rerun or included in this task's new totals. No global guard broadening or runtime enablement was done.

## Rail and CI touchpoints

Rail remains in shared module assembly/project inventory, Host's exact Api facade set, paired Rail/Trips closure, test-architecture and test-host-http ownership and ordinary aggregate prerequisites. All previous Rail rows/references/tests remain. New Trips -> Rail internal use is explicitly rejected, with no shared trip/rail business model introduced.

One unconditional ordinary `lint` step was inserted after existing setup-node and setup-dotnet:

```text
node --test tools/trips/build-moscow.test.mjs
```

No new job, lane, timeout, package script, dependency, project, coverage filter or aggregate change. The dependency-free inventory check validates the retained ordinary full Trips Unit/Integration/Architecture/Host/other module lanes and E2E prerequisites. This is source/static CI wiring; no remote workflow was executed. The helper/test source was left untouched: current hashes `b9f5ad331d54772c2eacc4f21311bdd1de0f117434368e59c15ebf1f76702ba8` and `b2ac3ab99396b381140a933792a5881fc1226ce7c1d174de00f07a2673ebccb4` exactly match accepted task1.4 source, whose existing `offline-helper-final.log` records15passed/0failed/0skipped in42488ms. The exact same accepted command is now retained in CI; it was not wastefully rerun in this task and those15 are not new results.

## Setup, offline restore and limits

Preflight found `tests/Travel.Tests.Architecture/obj/project.assets.json` absent and reported it before execution. Parent explicitly authorized cache-only restore; no remote fallback or credential/user NuGet configuration read/change. Installed SDK10.0.301 is selected by existing global.json10.0.203 with latestFeature rollForward. Existing cached Aspire.AppHost.Sdk13.4.6 Sdk.props/Sdk.targets were checked before restore; no SDK acquisition was needed.

Owned config `TestResults/trips-m2/architecture/NuGet.offline.config` clears packageSources and auditSources and names only `C:/Users/Vladimir_sva/.nuget/packages`. Exact successful setup command (exit0):

```text
dotnet restore tests/Travel.Tests.Architecture/Travel.Tests.Architecture.csproj --configfile C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency/TestResults/trips-m2/architecture/NuGet.offline.config --source C:/Users/Vladimir_sva/.nuget/packages --no-http-cache -p:NuGetAudit=false
```

NuGetAudit=false applied only to this offline setup command to prevent audit networking. Vulnerability auditing was NOT performed here and no security acceptance is inferred; ordinary CI audit steps remain unchanged. No dependency version or project-file change. C: free headroom was5.32GiB before setup and approximately4.62GiB after full verification; no cleanup performed.

Architecture builds Host/AppHost/AI project references and loads their metadata; it does not launch those applications. No Host/AppHost, DB, Docker, Keycloak, provider, network source, model, package installation/download, target/schema/key action, Q output or enablement occurred. The existing controlled child-process timeout fixture intentionally waits approximately2s and remains unchanged.

## Fresh TDD and verification

All dotnet test commands below used `--no-restore`, an implicit build (no --no-build), `--results-directory TestResults/trips-m2/architecture`, and these unchanged Verify discovery properties:

```text
-p:SolutionDir=C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency/ -p:SolutionName=Travel
```

Initial build-only diagnostic: ambiguous `Type` caused CS0104 in the new fixture; corrected to System.Type. `selector-red-raw.txt` preserves that compile diagnostic. It produced no test/TRX result and is NOT the meaningful RED.

Meaningful RED command:

```text
dotnet test tests/Travel.Tests.Architecture/Travel.Tests.Architecture.csproj --no-restore --filter 'FullyQualifiedName~Trips|FullyQualifiedName~Imported_foreign_reference|FullyQualifiedName~Additional_direct_consumer|FullyQualifiedName~Module_global_call_guard' --logger 'trx;LogFileName=selector-red.trx' --results-directory TestResults/trips-m2/architecture -p:SolutionDir=C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency/ -p:SolutionName=Travel
```

`selector-red-assertions-raw.txt` / `selector-red.trx`:33executed,29passed,4failed,0notExecuted/skipped. All four expected failures were missing new selector enforcement: removing Core, Application, Infrastructure Itineraries or Api.Contracts incorrectly returned `[]`. The new concrete dependency fixtures already passed the existing generic policies. Only then were the four selector rows added.

Focused GREEN used the same command/filter plus `|FullyQualifiedName~Curated_Moscow` and `LogFileName=focused-green.trx`:34executed/34passed/0failed/0skipped, all34 raw individual outcomes Passed. This includes the actual production model/marker check.

Final full unfiltered command:

```text
dotnet test tests/Travel.Tests.Architecture/Travel.Tests.Architecture.csproj --no-restore --logger 'trx;LogFileName=full-architecture.trx' --results-directory TestResults/trips-m2/architecture -p:SolutionDir=C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency/ -p:SolutionName=Travel
```

`full-architecture-raw.txt` / `full-architecture.trx`:208executed/208passed/0failed/0notExecuted/skipped, all208 raw individual results Passed; test duration59s. `trx-summary.json` retains parsed counters and independent raw-result counts. No final build/test warning was observed; explicit SolutionDir/SolutionName prevents the earlier Verify discovery warning. Expected controlled generic-type diagnostic output is retained by its existing test.

Changed-file `dotnet csharpier format` then `dotnet csharpier check` used exactly the ten owned C# paths below: both exit0,10files each (`format-raw.txt`, `format-check-raw.txt`). No global formatter.

`npm.cmd run check:dotnet-inventory` exit0; static validator passed. Its unchanged Node self-test suite has63total,62passed,0failed,1skipped. The existing Windows capability skip is `inventory-owned project paths must not be symbolic links` with reason `creating symlinks requires Windows Developer Mode`; this is explicitly not represented as0skips. Architecture itself has0skips. Inventory source and manifest were not edited.

## Exact owned source hashes

| File | SHA256 |
|---|---|
| `tests/Travel.Tests.Architecture/ModuleMatrixTests.cs` | `cfd44af5668ca9c30646a9c1afc4ea4e76b82ba4d20ebd234ba06e864ccdb3b5` |
| `tests/Travel.Tests.Architecture/LayerMatrixTests.cs` | `40f8d071b709b5a7204329cf430157813c7f3a7afe127d6fb6b7fcac6f64ad29` |
| `tests/Travel.Tests.Architecture/IlTypeReferenceScannerTests.cs` | `2443592d5af26dc9b70cfcf3749838f0071e34382f7db251aa259f52664084ee` |
| `tests/Travel.Tests.Architecture/HostModuleTypeDependencyTests.cs` | `397b7c1a609ab65be128e58d9aa9c91c3532d82ec5e4c477134c45953a904938` |
| `tests/Travel.Tests.Architecture/IntegrationContractArchitectureTests.cs` | `8664617348f5cde4e74fb0c18f6e66c53bfd7b1d2769d04493a16a2ec7731ad1` |
| `tests/Travel.Tests.Architecture/CompositionCallSiteTests.cs` | `3c5c1528e794efc32c8eb71f6324a70cf5943dc45ef825e15bc20291fa075c54` |
| `tests/Travel.Tests.Architecture/Fixtures/ModuleMatrix/TripsForeignInternalsFixture.cs` | `6006db80add4a23f6eacd6bbfc8cb1045be5a072afead1248fbe54824034c75c` |
| `tests/Travel.Tests.Architecture/Fixtures/LayerMatrix/TripsItineraryBypassFixtures.cs` | `2e7fd2b5e201717ac1dd1d6bb8f540d697e83722e7a24731bb553e0e0c3e6f6a` |
| `tests/Travel.Tests.Architecture/Support/ModuleArchitectureInventory.cs` | `179d670bc2ce87f62c4d1d82d5dfe3a512173a07800a2961510db1256d54a72d` |
| `tests/Travel.Tests.Architecture/Support/LayerArchitecturePolicy.cs` | `de346f38c064ca010b0a41a1f01e0c53537c01330fe051b7aa15d8bdc7e0910c` |
| `.github/workflows/ci.yml` | `5f8af47ca08ab4839d6f3e6a0be64cf5e4b1c26e94ff4db7d53454895ceb33d3` |

Additional retained production/API/project/Host/guard/helper/inventory fingerprints are in `source-sha256.json`; actual compiled Architecture/module/Host/AppHost/AI DLL hashes are in `compiled-sha256.json`. These are fresh implicit-build receipts against current source; no commit or remote exact-head CI attestation is claimed.

## Receipt hashes

All paths in this table are relative to `TestResults/trips-m2/architecture/`.

| Receipt | SHA256 |
|---|---|
| `NuGet.offline.config` | `4b71e9ca1fbdd6dd51c026d1f1be30545c7dda4c66daddac4d16e0eec1def3b3` |
| `offline-restore-raw.txt` | `348b8eda112e6a078b224d438db23b0f83f806dc1572f2418b3d76eade2b2506` |
| `selector-red-raw.txt` | `e80da17c5763a7704ac8ebe392cb8420c451b564769d6ecafc59da573103c526` |
| `selector-red-assertions-raw.txt` | `932abc04e9d7dd78e85d3a53aec7feb9827626625dc59b5f1ed4e9cd8bd97024` |
| `selector-red.trx` | `b87896df71b570d10e7145147e98e4b9d166f7eb64ffe2bc27d112302fa74684` |
| `focused-green-raw.txt` | `63ae48ff40603f99cc7549833e4d8a02e0257c16d5c179d70c9f6669c38461d9` |
| `focused-green.trx` | `179cbfa58ebef0d055adc6472e5d5ac5d64f94cf01097ff54ac0e8c79a2edf8e` |
| `full-architecture-raw.txt` | `42926ff889e65ab6533ea0af19c98f180759fcf6c8f872bf6da52956a02bd1ef` |
| `full-architecture.trx` | `52d2ae9f3db0e4e7c3f6c7bcb6be72050158e8c04fb4ad3b173228fd43c3a841` |
| `format-raw.txt` | `4e435bb0049b6e1bf199e3e049e5767a081fb4a168118cf19800bbadc0eeffa3` |
| `format-check-raw.txt` | `285d55fd0ab5a5aecde6dcdbdbd1b4f7ef9c0c23a87f9b02060485a0776b5d98` |
| `inventory-raw.txt` | `b1764d945fa1bb13ee4ed9e52ce3b23b3cc8cd099e692f4303588f705f249b03` |
| `source-sha256.json` | `a59f4d469959be614bea196493c2fc5cd11805cd0dc151e3a2276e3524b69e44` |
| `compiled-sha256.json` | `8333c0953df7bcb687528b22b5ef39ab2c47ba62cbf2060da396fac5152cb5dd` |
| `trx-summary.json` | `a80a211a99f1275946b19896fe188caef429cda9fac843924fb702e4ad12abca` |

`receipt-sha256.json` contains the same receipt fingerprints. Parent fresh independent review/recheck remains required before marking task3.5 accepted. Root Nx/data/wire input edits are parent-owned and outside this verdict. TS/UI/adoption/browser/Q_demo/whole-M2 review/publication/CI/target/human-quality/rollout gates remain separate.
