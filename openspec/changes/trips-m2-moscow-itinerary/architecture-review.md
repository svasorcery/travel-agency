# Spec compliance: PASS

Task quality: PASS. No P1/P2/P3 findings in task 3.5.

Scope: retained architecture proof and exactly one ordinary CI lint step; not whole Trips M2 acceptance. Controller-supplied fixed base/HEAD is `e654c76915103a82083ef2abc01c0579117c9749`, branch `codex/trips-m2-moscow-itinerary`, with no new commits. No Git command or branch mutation was performed by this reviewer.

## Spec compliance and strengths

- `tests/Travel.Tests.Architecture/Support/ModuleArchitectureInventory.cs:200` adds the three own-assembly Itineraries required selectors. `ModuleMatrixTests.cs:111` first proves the real unmodified production collection passes, asserts each removed subtree is nonempty, then requires exactly its missing-selector error. The real six-module/four-layer/24-assembly type, IL and evaluated Debug/Release project-reference matrix is retained (`ModuleMatrixTests.cs:184`).
- `tests/Travel.Tests.Architecture/Support/LayerArchitecturePolicy.cs:219` adds Trips.Api.Contracts without weakening Endpoints/Middleware/Composition or another module's rows. `LayerMatrixTests.cs:123` supplies the corresponding nonempty real-collection removal negative. No permissive selector escape or new allowlist entry was added.
- `tests/Travel.Tests.Architecture/LayerMatrixTests.cs:153` tests each Trips Core/Application -> concrete own CuratedMoscowFacts forbidden edge. The exact origin/target expectations at `LayerMatrixTests.cs:174` reject Endpoints, Contracts and CompositionExtra while positively requiring the same dependency in the allowed Composition case. The fixtures remain one small group (`Fixtures/LayerMatrix/TripsItineraryBypassFixtures.cs:1`), with no production policy framework or source changes.
- `tests/Travel.Tests.Architecture/ModuleMatrixTests.cs:139` separately rejects Flights IataCode, Hotels HotelPropertyReference and Rail StationRef internals from Trips Application; each assertion binds the intended origin, concrete target and module pair. `Fixtures/ModuleMatrix/TripsForeignInternalsFixture.cs:3` supplies those dependencies and a signature-hidden Flights method-body edge. Existing Hotels/Flights negatives remain.
- `tests/Travel.Tests.Architecture/LayerMatrixTests.cs:196` rejects concrete AI contract type dependencies from Trips Application and Api. `IlTypeReferenceScannerTests.cs:113` positively observes and then rejects the specific Api AI and Application Flights method-body edges. `ModuleMatrixTests.cs:65` retains Identity and adds Trips Application imported-reference cases for Debug/Release. `IntegrationContractArchitectureTests.cs:225` retains Hotels and adds both Trips consumers, requiring the actual unexpected project path; the existing property-imported consumer proof remains intact.
- `tests/Travel.Tests.Architecture/HostModuleTypeDependencyTests.cs:58` rejects the actual itinerary endpoint dependency and positively observes/allows the actual TripsModule Composition facade. `CompositionCallSiteTests.cs:65` adds Trips Core/Application/Api controlled bypass cases to the existing global-call guard. No Host callsite, endpoint map, builder ownership or project closure was changed.
- `tests/Travel.Tests.Architecture/LayerMatrixTests.cs:207` explicitly observes six actual M2 types in their production assemblies and checks CuratedMoscowFacts' direct TestOnlyAttribute with `inherit:false`. Its comment correctly limits this to structural marker proof. It does not pretend to close TestOnlyGuard's ImplementationType-only factory gap or establish Production registration behavior.
- `.github/workflows/ci.yml:44` adds exactly one unconditional `node --test tools/trips/build-moscow.test.mjs` step after the existing Node and .NET setup in lint. All existing jobs, timeouts, filters, coverage, frontend/browser commands and aggregate prerequisites are retained. No package script or helper source change was added. This supplies the requested ordinary CI consumer without claiming an actual CI run.

## Focused checks outside the diff

- Risk: synthetic test namespaces could satisfy production selectors. Checked `tests/Travel.Tests.Architecture/ArchitectureTestBase.cs:19`: the production model loads the exact module DLL inventory plus named shared/Host/AI/AppHost/contracts assemblies, excluding the Architecture test assembly. Controlled fixtures are loaded separately by the new tests. The selector absence tests therefore operate on real production types.
- Risk: additional Trips proof could conceal lost Rail/Host closure coverage. Checked `tests/Travel.Tests.Architecture/HostModuleProjectReferenceTests.cs:13`, `:44` and `:79`: the exact six Api facades, both Rail/Trips four-layer closures and each own-Infrastructure positive remain enforced in Debug and Release. The complete Architecture TRX includes their passing results.
- Risk: the marker assertion could be mistaken for runtime omission evidence. Read the accepted API recheck `.superpowers/sdd/tasks/task-3.3-3.4-recheck.md` and recomputed SHA256 `d11c920c083ab69b11b9c59718c77fd1805b917db657a6a55ba904cbbc4e854e`. Its accepted API assessment and retained boundary85/85 plus M1 HTTP30/30 evidence are cross-task evidence, not new task3.5 results. Runtime Production/default-off/fail-closed acceptance remains owned by that API boundary; no global guard fix is inferred here.

## Verification and snapshot binding

Read the complete 3916-line full-context review package once. One initial tool-output truncation was recovered by reading the missing diff span; no changed source file was separately reread. No tests, helper, Host, DB, Docker, model, provider, network or Q outputs were executed. Review commands only read source/receipts, parsed XML and recomputed hashes; only these two identical reports were written.

Recomputed input identities:

- Review package SHA256: `52c45587a2882fd36be067426b0360afbc8385dfd4858f789254bfc8886bf6f2`.
- Implementer report SHA256: `dae38a3a972f89f31bca67a425e411bd4f707de9e876fb60e7d4f5defcdb0919`.
- All entries in `TestResults/trips-m2/architecture/receipt-sha256.json` matched their actual receipt bytes. The controller independently verified the current source-manifest fingerprints; this review did not repeat that full source hashing.

Reparsed raw TRX counters and individual results, and checked raw logs:

| Receipt | Result |
|---|---|
| selector-red.trx | 33 executed, 29 Passed, 4 Failed, 0 notExecuted/warnings; four exact new-selector failures because the policy returned an empty error collection |
| focused-green.trx | 34 executed, all 34 Passed, 0 failures/notExecuted/warnings |
| full-architecture.trx | 208 executed, all 208 Passed, 0 failures/notExecuted/warnings; unfiltered Architecture project |
| format-check-raw.txt | Exactly 10 C# files checked successfully |
| inventory-raw.txt | Static inventory validator passed; Node63 total,62 passed,0 failed,1 skipped |

The existing Node skip is explicitly `inventory-owned project paths must not be symbolic links`, reason `creating symlinks requires Windows Developer Mode`. It is not represented as zero skips. The earlier compile-only CS0104 diagnostic is retained separately and is not counted as meaningful RED; final Architecture output has no build/test warning. The expected controlled process timeout remains a deliberate fixture behavior.

The helper's unchanged source/test hashes and prior accepted15/15 receipt are retained by the controller/implementer. The identical CI command was source-reviewed, not rerun, and those15 are not added to the new totals.

## Limits and downstream gates

This PASS accepts only task3.5 source/local architecture proof and static ordinary CI wiring. It does not certify a universal external-DTO/package policy; the proof is concrete Trips layer/Infrastructure ownership, cross-module internals and named AI-contract boundaries. Rail remains a peer in inventory, closure, facade and CI ownership; no shared business model was introduced.

The authorized offline cache-only restore used a command-scoped `NuGetAudit=false`; vulnerability auditing/security CI acceptance was not performed or waived. Cached SDK/dependencies, local SDK10.0.301 roll-forward and CI-pinned10.0.203 remain distinct. No dependency/source policy or version change is accepted from this setup receipt.

Root Nx configuration and controller-owned docs/task state are outside this verdict. TS/UI/adoption/browser integration, package/demo quality and Q_demo, whole-M2 audit, actual exact-head PR/postmerge CI, Host/Keycloak/target provisioning, human D_live/Q_live and rollout remain separate gates. No publication, provisioning or enablement authority follows from this review.
