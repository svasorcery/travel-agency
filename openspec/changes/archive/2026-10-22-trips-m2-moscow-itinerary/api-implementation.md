# Tasks 3.3/3.4 API boundary implementation report

Uncommitted source/local result after fix round1: current boundary85/85 and retained M1 HTTP30/30, ready for scoped independent recheck; not task acceptance or whole M2 completion. Fixed HEAD `e654c76915103a82083ef2abc01c0579117c9749`, branch `codex/trips-m2-moscow-itinerary`; no branch/HEAD/Git mutation. Scope is the single coupled API boundary. No Core/Application/loader/data/TypeScript/architecture/Host/global guard/root configuration edits. Parent owns canonical docs, root configuration and task state.

## Result and public contracts

The early `.superpowers/sdd/tasks/task-3.3-3.4-interface.md` now describes the exact implemented v1 wire schema. It retains minimal source-free unsupported, and full computed candidate/empty basis. All positive Int64 revisions/path lengths/anchors/Wikidata revisions are strings; original titles, null times/estimates, explicit unknowns and directional path/evidence binding survive projection. The encoder projects only referenced facts; it never serializes `ValidatedItineraryFacts`. It validates projection bounds/references/UTF8/text/source metadata, caps the COMPLETE normally escaped UTF8 at128KiB/depth16 and fails the whole result503 without truncation. Application's already reviewed independent validator and ordinary production M1 codec preflight/frozen bytes/final owner GET remain unchanged. Endpoint writes the prepared bytes directly without a second serialization.

Model metadata retains actual publication/download/software build and retrieval/processing times; no human review date is invented. Null curator/reviewer, not-evaluated human/calibration status, fixed explicit75/45m/min/crossing assumptions and ODbL attribution are preserved. The fixed derived-data offer is `/trips-data/moscow-outdoor/1/moscow-v1.manifest.json`. Notes remain<=500 Unicode scalars, matching M1.

One exact method/path classifier is shared by both middleware layers: ordinal-ignore-case, one optional trailing slash, syntactically valid router-compatible Guid (including Guid.Empty). Unknown method/path/extra-slash/invalid-Guid do not gain availability bypass. ResponsePolicy remains before authentication; RequestGuard remains after authorization; one Wolverine mapping remains. M2off404 happens before auth/source/M1 dependencies, and all Trips replies retain no-store. Candidate reuses M1 strong quoted positive Int64 If-Match, requires strict bounded actual UTF8 stream of empty object with no properties/query (<=1024bytes even unknown length), and retains M1 readiness. Availability permits no body/query and bypasses only M1 readiness, with no readiness/store/protector/codec resolution. Ready has EXACT TEN fields and<=2KiB/depth4; source failure/withdrawal/expired source never emits stale identity. There are two clock checks, including immediately before success metadata. Observation remains best effort after the last check.

Typed RFC7807 status/code mappings cover401/403/404/428/400/412/409/429/503/504. Busy alone adds Retry-After:1; canonical deadline code is `Trips.ItineraryDeadline`. Framework's existing optional `traceId` is preserved, with fixed problem title and no private body/query in errors/logs.

Composition always registers TWO simple flags before the M1 disabled return. Effective M2 is TripsEnabled && ItineraryRequested && !Production. Therefore Production requestedtrue is effectivelyfalse: source/gate/encoder/service registrations are ALL omitted; no TestOnly-factory escape, replacement unavailable provider, or global guard change. Enabled nonproduction uses one singleton source pinned explicitly to `CuratedMoscowRelease.ReviewedV1`, singleton encoder/two-slot admission gate, scoped service. Fixed asset directory only; no arbitrary URL/path/source registry. Flags and withdrawal configuration are startup-bound and need process restart to change. Manual M1 stays independently available when optional facts fail. Wolverine service-location declarations preserve lazy route dependencies; default mapping was tested too.

## Verification (initial pre-review snapshot)

Required meaningful initial RED is `red-raw.txt` + `red.trx`:2failed/0passed/0skipped, both M2off anonymous requests unexpectedly returned401 before implementation. `green-initial` then2/2. Intermediate56-case run12failed44passed was diagnosed as an invalid fictional identity URL: the copied synthetic fixture URL did not satisfy Core's required exact Wikidata ID/revision URL. No Core contract was weakened. The synthetic fixture was corrected and asserts valid construction. A high revision test initially changed canonical M1 JSON through JsonNode's default serializer and was correctly rejected; it now changes only the canonical revision bytes. Compile/setup-only default-parameter/nullability/UTF8 Python-script errors are not counted as RED. Raw diagnostic/intermediate files remain; no receipt was replaced with fabricated success.

Final post-format boundary command:

```text
dotnet test tests/trips/Travel.Modules.Trips.Tests.Integration/Travel.Modules.Trips.Tests.Integration.csproj --no-restore --filter "FullyQualifiedName~TripsItinerary" --logger "trx;LogFileName=boundary-final.trx" --results-directory .superpowers/sdd/tasks/task-3.3-3.4-results -p:SolutionDir=C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency/ -p:SolutionName=Travel
```

`boundary-final-raw.txt`/`boundary-final.trx`:83total,83passed,0failed,0skipped; every individual TRX result Passed. Class counts from actual TRX definitions: TripsItineraryCompositionTests=6, TripsItineraryHttpTests=61, TripsItineraryResponseCodecTests=16. Coverage includes real middleware order/Wolverine routes/production encoder+M1 codec/in-memory store+counted protector, off auth/source/store/codec/protector resolution sentinels, readiness bypass sentinel factories, owner401/403, foreign-before-decrypt/source404, canonical large revision>2^53, missing/malformed/stale revisions, duplicate/extra/missing/deep/invalid UTF8 bodies, exact1024/1025 bounded known/unknown length streams, wrong media charset, case/trailing/method/path routing, M1 readiness retained, nonempty409, optional503/manual M1, A->B/withdrawal/expiry source observations with unchanged plan revision, and actual App sharedgate429/deadline504 using only the existing async encoder seam. Cooperative caller cancellation/lease behavior is already covered by reviewed Application tests and was not duplicated.

Codec corrupt-output cases reject invented titles/evidence/unknowns, duplicate blocks, reversed edges, mismatched ranges, missing adjacency, time/money additions,501-scalar notes and unpaired UTF16; complete500-emoji rights metadata over12stops/3days is rejected at the actual escaped UTF8 cap. Valid metadata/unknowns/path strings and original titles remain. Core independent validation owns full semantics; the API encoder is additional projection defense, not a second route policy engine.

Full retained M1 lean HTTP regression command:

```text
dotnet test tests/trips/Travel.Modules.Trips.Tests.Integration/Travel.Modules.Trips.Tests.Integration.csproj --no-restore --filter "FullyQualifiedName~TripsHttpTests|FullyQualifiedName~TripsSizeHttpTests|FullyQualifiedName~TripsOwnerClaimsTests|FullyQualifiedName~TripsDateRangeConflictHttpTests|FullyQualifiedName~TripsPrivacyDiagnosticsTests|FullyQualifiedName~TripsCompositionTests" --logger "trx;LogFileName=m1-http-regression.trx" --results-directory .superpowers/sdd/tasks/task-3.3-3.4-results -p:SolutionDir=C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency/ -p:SolutionName=Travel
```

`m1-http-regression-raw.txt`/TRX:30total,30passed,0failed,0skipped, every individual result Passed. Includes all existing TripsHttp/SizeHttp/OwnerClaims/DateRangeConflictHttp/PrivacyDiagnostics and both existing composition tests; M1 byte/reserve/privacy behavior unchanged. The disabled composition expected descriptor count intentionally changes1->2. PostgreSQL `TripsPersistenceTests`, key-ring/protection `TripsCodecProtectionTests`/`TripsProtectionReadinessTests`, and already accepted98-case real loader suite are excluded, not reported as passed/skipped. No Docker/PostgreSQL/Host/Keycloak/key provisioning/real Q outputs involved. No broad solution/architecture/web/CI run.

Default `MapWolverineEndpoints()` (same no-options call as Host, no Host launch) is separately proven with M1on/M2off and Production requestedtrue: absent M2 source, M1 destinations200, both M2 anonymous404. Other fixture tests retain existing RouteWarmup.Lazy. Middleware order is ResponsePolicy -> Authentication -> Authorization -> RequestGuard -> Wolverine mapping in both variants.

Changed-file `dotnet csharpier format` and `check` ran over exactly15owned C# paths listed below; both exit0, check reports15files. Raw logs are `format-raw.txt` and `format-check-raw.txt`. No global formatter. C: free headroom checked repeatedly, about5.33GiB.

## Actual fictional vector evidence and limitation

Six JSON files were emitted ONCE from actual HTTP production serializer responses, then frozen. Normal tests contain no write/update path and never rewrite goldens. Files are raw unformatted UTF8 with NO terminal newline. Ready/empty/unsupported are compared byte-for-byte without substitution. Core generates new UUIDs, so candidate/partial comparisons validate every generated proposedBlockId UUID and substitute ONLY those UUIDs from the frozen file before every-byte comparison; error similarly substitutes ONLY a validated existing ASP.NET traceId. Full JsonNode equality also checks the complete shape/values. This is not unmodified raw byte equality for candidate/partial/error, and it is not evidence that UUIDs/traceIds are deterministic. Vectors contain fictional synthetic POIs/sources/model paths, never real-package route-quality outputs. No runtime generator port or test behavior flag was added. Root formatter override is parent-owned.

## Exact final source snapshot
- `modules/trips/Travel.Modules.Trips.Api/Contracts/ItineraryCandidateDtos.cs`: 23142 bytes; SHA256 `c4869f3e8ef06fa67364d7880d4e77a87ade4abffde67b1e4316ed65d4e49a63`.
- `modules/trips/Travel.Modules.Trips.Api/Contracts/TripsProblems.cs`: 1667 bytes; SHA256 `724684b75fe61e6e077e9e563960ec3ceca44757fb96bf7ba7061c8006dc14fd`.
- `modules/trips/Travel.Modules.Trips.Api/Endpoints/ItineraryCandidateEndpoint.cs`: 1605 bytes; SHA256 `9e731eb6a3ea587af9521f857a9af467e521f14c308a6848e0ee31a789f8c242`.
- `modules/trips/Travel.Modules.Trips.Api/Endpoints/ItineraryAvailabilityEndpoint.cs`: 2698 bytes; SHA256 `7ebf0769df0769f3a3e6c2615f255aacc9e289352c5937ea07efb72c512644f3`.
- `modules/trips/Travel.Modules.Trips.Api/Middleware/TripsItineraryRoutes.cs`: 1213 bytes; SHA256 `33914793a7a710441cfc2cc6e3614b87a894bba229b0b6b8eb89d86b3e3ad5f6`.
- `modules/trips/Travel.Modules.Trips.Api/Middleware/TripsResponsePolicyMiddleware.cs`: 1681 bytes; SHA256 `2e28cef425604444129405e1f0ab97d76f384dccb9ae236e902c0a2bfd8cb717`.
- `modules/trips/Travel.Modules.Trips.Api/Middleware/TripsRequestGuardMiddleware.cs`: 13928 bytes; SHA256 `ac8adae775d3b1462ba270e5c8cfbde18d6c6ddfc338df4eb0f7adaf7438857f`.
- `modules/trips/Travel.Modules.Trips.Api/Composition/TripsModule.cs`: 4648 bytes; SHA256 `bffd3eca7e94b2e41760190e576ffa66a515e57d3a50ed2ef55e79ac5ce20670`.
- `tests/trips/Travel.Modules.Trips.Tests.Integration/TripsItineraryHttpTests.cs`: 45217 bytes; SHA256 `f5cda6407b21456253eee8405c9b95a53b4324deeaf9172be32f80b75bb3ed21` (fix round1).
- `tests/trips/Travel.Modules.Trips.Tests.Integration/TripsItineraryCompositionTests.cs`: 2686 bytes; SHA256 `ddc73c7c875048545ed54f2519f3e38217bc456830d85352b267e2141eb8fbf9`.
- `tests/trips/Travel.Modules.Trips.Tests.Integration/TripsItineraryResponseCodecTests.cs`: 8903 bytes; SHA256 `05e98129847c9c87cd88ad9e073f5d7cd8356fa191161a5eea417aba4a90eec8`.
- `tests/trips/Travel.Modules.Trips.Tests.Integration/FictionalItineraryFacts.cs`: 9773 bytes; SHA256 `407723e5c0ca8e78b3cdd3e87fcb5df09d85533481bd4de50f6fd5f64200ee18`.
- `tests/trips/Travel.Modules.Trips.Tests.Integration/FictionalItinerarySource.cs`: 1943 bytes; SHA256 `62d237e31b2c36382f7e7ccb019b5874030c10589842eb1ca9ed918ab88ce654`.
- `tests/trips/Travel.Modules.Trips.Tests.Integration/TripsHttpFixture.cs`: 11698 bytes; SHA256 `3c7fb620ed8ac7327ebd7cbd788c64f7fcdd1ccfdb10a30da9fb6f75434f69b1`.
- `tests/trips/Travel.Modules.Trips.Tests.Integration/TripsCompositionTests.cs`: 2105 bytes; SHA256 `15ff84781101e0f0e36cb47d8579e80ea56859fe17e7275e44ec08d2a3839056`.
- `.superpowers/sdd/tasks/task-3.3-3.4-interface.md`: 6944 bytes; SHA256 `3dc95dece1ddbe2a1cfc63e60c37dedfc63f5741c800e7fe16b13bfdaedf4e7d`.

## Frozen actual HTTP vector bytes
- `tests/fixtures/trips-itinerary-v1/candidate.json`: 5545 bytes; SHA256 `60a7fec4d3948271044def8afeb9529ab518b66fb5374835e6c8c4db689f18c8`; terminalLF=false.
- `tests/fixtures/trips-itinerary-v1/empty.json`: 2134 bytes; SHA256 `82b83c74a5e0d5a4f72c1bd07900557295b90c5d4a0d76c531e98273a6f74911`; terminalLF=false.
- `tests/fixtures/trips-itinerary-v1/error.json`: 183 bytes; SHA256 `5c9899bc4648d9e9daa2fa495e3b9c6c90e3f6ec2c20fe56e50523bb73828595`; terminalLF=false.
- `tests/fixtures/trips-itinerary-v1/partial.json`: 5568 bytes; SHA256 `eae996710c633d724f669b7816d22e4dfdc282da9d325a46ef5309d7451e6c6d`; terminalLF=false.
- `tests/fixtures/trips-itinerary-v1/ready.json`: 355 bytes; SHA256 `cf80aa3adf531547337ff83549d147516483360ea2d11a8f896dad66fee3a0bc`; terminalLF=false.
- `tests/fixtures/trips-itinerary-v1/unsupported.json`: 263 bytes; SHA256 `5899ad2ffb02a9a93c105a5e88af64b08aa2e572af0dc098fb76e07c324cfddc`; terminalLF=false.

## Reviewed canonical data unchanged

All six original reviewed hashes from task3.1 were independently recomputed and matched; no package rewrite/reformat occurred. Machine-readable source/vector/data receipts: `task-3.3-3.4-results/final-hashes.json`.
- `ATTRIBUTION.txt`: 869 bytes; SHA256 `0c8bca1abb555d22fc9a30133287abb9a11cc8554169ede7c379b9473cb01172`.
- `input-manifest.json`: 295 bytes; SHA256 `d5a3025a6670d7aaccfaaff8c15a085197c2c9734fec50339a6ecf2322870ac8`.
- `inventory.json`: 7711 bytes; SHA256 `c04bc32ca50be5fa5caa6057bf89948fee874c01ecdc418d4bd92059695f01ae`.
- `moscow-v1.json`: 101163 bytes; SHA256 `631fd5cd9650e3228af87ef8a31219d7ea59a2e4af42bc4328453f32268ffb08`.
- `moscow-v1.manifest.json`: 488 bytes; SHA256 `8f364b38540a3d8dfd21272021c50f3f18ca6c01d2863617368c12d2e6f60e43`.
- `walking-evidence.json`: 137293 bytes; SHA256 `196c5cd39481bdb55b6c123a4555571a0aa6f2eabc720e24ef3d70f63e3b397b`.

## Remaining gates

Parent independent combined API review/recheck is required before accepting tasks3.3/3.4. TypeScript consumer/UI/source documentation/whole-M2 tests and final source review remain downstream; no whole-M2 completion is claimed. Publication, CI/postmerge, real source/model/human quality, real Host/Keycloak/target/schema/keys/Production/rollout remain separate gates. No package installation, source network/provider/model call, database/container/key provisioning, Git mutation, enablement, publication or subagent delegation occurred. Only authorized local TestServer HTTP was started; it is not installed/live target proof.

## Fix round1 - sole P2 real HTTP response-size evidence gap

Canonical `openspec/changes/trips-m2-moscow-itinerary/api-review.md` / task review SHA256 `f76d405ceed84aa1f7ab90ff85cfb0f4f971daf09a6dd7ad8faabb6739170221` found production code clean and one P2: the escaped multibyte overflow case was only a direct codec call. The original83/83 and M130/30 did not close the mandatory real HTTP near-limit/no-truncate criterion. This finding is retained here; independent acceptance awaits the scoped recheck. Original report SHA256 `1c8781c6b706360c7a776d43c38c143a90fc97d416e186d5225aa66ebe15c84a`, original HTTP test/report/hash snapshots and every old83-case receipt are preserved in `task-3.3-3.4-results/fix-round-1` or their original receipt paths.

Only `TripsItineraryHttpTests.cs` changed in source: two parameterized real HTTP POST cases plus a test-local counted store decorator. No shared fixture, production code, interface contract, Core/Application, loader/data, UI, root configuration or vector changed. No production cap was modified to manufacture RED. The first new execution was legitimately GREEN2/2 because this was a coverage gap in already implemented behavior; `fix-round-1/http-boundary-first-raw.txt` + TRX preserve that exact first run. No new failed behavior is claimed. Existing direct codec overflow coverage remains unchanged.

Both HTTP cases create an ordinary saved three-day Active plan and use the real middleware order/Wolverine route, production ItineraryCandidateService, production response encoder and actual `TripPlanJsonCodec`. Only fictional immutable facts carry large valid rights metadata. A small baseline through the actual serializer measures the precise escaped size, and explicit frozen assertions bind this fixture to422/423 Unicode scalars per evidence and exact byte sizes:

| Case | Emoji scalars in each referenced rights record | Referenced records | Complete serialized size | Actual HTTP |
|---|---:|---:|---:|---|
| Below |422|21|131017bytes (55below131072)|200; Content-Length131017; complete UTF8/depth16 parse;3days,12blocks,9edges,all21full rights values|
| One extra scalar per record |423|21|131269bytes (197above131072)|503 application/problem+json; code Trips.ItineraryUnavailable; actual fixed problem171bytes; no candidateVersion/outcome/days/evidence|

The production serializer uses12escaped UTF8 bytes per supplementary scalar, so the single per-record increment adds252bytes. All source record values stay within the500-Unicode-scalar factory bound. Below-cap proof compares the entire actual HTTP response to production encoder bytes after substituting only the12validated nondeterministic block UUIDs, and asserts complete byte length and every untruncated rights string. This comparison has the same explicit UUID limitation as the original vector tests; it does not claim random IDs are deterministic. Over-cap proof verifies the complete fixed RFC7807 error with no partial candidate. Both cases assert no-store/no Retry-After, exactly one source call/authenticated request, expected initial/final reads (success2, overflow1), no automatic resubmit, store writes1->1, same exact saved row object, revision, updated timestamp and protected payload. No plan data is lost or changed. Test diagnostics print only sizes/counts/status, no private plan fields.

First focused command (GREEN2/2,0skip, raw detailed output includes both measured boundaries):

```text
dotnet test tests/trips/Travel.Modules.Trips.Tests.Integration/Travel.Modules.Trips.Tests.Integration.csproj --no-restore --filter "FullyQualifiedName~Complete_multibyte_response_straddles_actual_HTTP_cap" --logger "trx;LogFileName=http-boundary-first.trx" --logger "console;verbosity=detailed" --results-directory .superpowers/sdd/tasks/task-3.3-3.4-results/fix-round-1 -p:SolutionDir=C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency/ -p:SolutionName=Travel
```

Final post-format command:

```text
dotnet test tests/trips/Travel.Modules.Trips.Tests.Integration/Travel.Modules.Trips.Tests.Integration.csproj --no-restore --filter "FullyQualifiedName~TripsItinerary" --logger "trx;LogFileName=boundary-fix1-final.trx" --results-directory .superpowers/sdd/tasks/task-3.3-3.4-results/fix-round-1 -p:SolutionDir=C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency/ -p:SolutionName=Travel
```

NEW `fix-round-1/boundary-fix1-final-raw.txt`/TRX:85total/executed/passed,0failed/skipped, all individual results Passed (63HTTP+6composition+16codec). Existing original83/83 and M130/30 raw/TRX were parsed again and remain all Passed; they were not rewritten. M1 regression was not rerun because neither production nor the shared HTTP fixture changed in this test-only correction. Scoped `dotnet csharpier format` and `check` touched/checked only `TripsItineraryHttpTests.cs`, both exit0, check1file; receipts in fix-round-1. C: free headroom remained about5.32GiB.

`final-hashes.json` is updated to current source with only the HTTP test hash changed. `fix-round-1/final-hashes-before.json` preserves the original identity set. Verification recomputed every remaining API source/interface hash, all six actual wire vector hashes, all six canonical package hashes and the approved task3.2 Core/Application/service-test source hashes: all unchanged. The scoped old/new unified diff and report diff are in `fix-round-1/review-package.md`; `fix-round-1/fix1-hashes.json` records exact before/after/report/package/receipt hashes. No broader tests, Host/database/model/provider/real Q execution, package installation, Git mutation, enablement, publication or subagents occurred. Parent scoped independent recheck remains the next gate.
