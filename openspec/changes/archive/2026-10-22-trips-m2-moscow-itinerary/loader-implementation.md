# Task 3.1 implementation and local verification

Current revision: the Revision 1 section at the end supersedes the initial constructor, source hashes and 90-case snapshot below. Initial independent review requested changes for the P2 release-identity gap; the correction has new local evidence and awaits scoped re-review.

Implementation candidate is ready for independent review. This report records only task 3.1 source and targeted local verification; parent owns task acceptance, D_demo status, Q binding and later gates. Worktree is C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency, unchanged HEAD e654c76915103a82083ef2abc01c0579117c9749. No staging/commits/branch mutation. Canonical six data files were read only throughout.

## Public contract and implementation

- Application.Itineraries.IItineraryFactSource.ReadAsync(CancellationToken cancellationToken = default) returns Task<ErrorOr<ValidatedItineraryFacts>>.
- Infrastructure.Itineraries.CuratedMoscowFacts is public sealed and [TestOnly]; constructor CuratedMoscowFacts(string packageDirectory, TimeProvider timeProvider, Func<bool>? isWithdrawn = null). The directory is the exact version directory, normally Path.Combine(AppContext.BaseDirectory, "Trips", "Itineraries", "moscow-outdoor", "1"). Composition/production exclusion remains task 3.4.
- Only public files, injected clock and optional withdrawal predicate are dependencies. Every read reopens the package, rechecks withdrawal before/after, validates current clock against frozen build/release and identity leases, and returns no cached success. Expected file/JSON/domain failures return one Trips.ItineraryUnavailable without paths/exception text; cancellation propagates.
- Private Infrastructure JsonElement wire parsing explicitly whitelists every property, required field/type and duplicate check recursively, depth 16, strict UTF8 without BOM/CR. No source wire DTO crosses the boundary; every resulting source/preparation/evidence/access/POI/modelled-walk/edge/package goes through its actual Core factory. No normalization, truncation, hash repair, source parser, geometry reconstruction, router, source API, registry, background refresh or cache.
- Six actual directory entries must have exact ordinal filenames and be ordinary files; extra files/directories, reparse points and Windows case aliases reject. Manifest has the exact five child keys and lowercase SHA256; all five file digests are checked before source mapping. Manifest never controls a path or URL.
- Bounded asynchronous streams check pre-read length and actual accumulated bytes: manifest 2 KiB, payload 256 KiB, walking evidence 4 MiB, attribution 8 KiB, input-manifest 4 KiB, inventory 128 KiB. Cancellation is passed to stream reads and checked during mapping. Cap+1 files are tested for all six roles; concurrent stream-growth races are not separately injected.
- Input metadata and source/evidence metadata must agree on the exact input/publication hashes, source URL, UTC publication/download/frozen build and fixed model versions. Input compressed size is positive and at most 200 MiB; the raw OSM input is deliberately absent at runtime. Inventory must bind every payload POI identity/title/tags/dwell/revision/retrieval/coordinate and optional anchor. Approximate anchors must be positive, within 50 m and inside the fixed source bbox; demo access is strictly unknown. No physical observation, calibration or human actor is invented.
- Every selected path binds its exact UTF8 raw JSON object plus LF digest, ordered path schema, positive node/way arrays, endpoints, path/edge references, length/crossings/anchors, ODbL/modelled/null observation and Core integer envelope. Paths have no orphan/duplicate refs and at most five outgoing selected edges per source. Runtime verifies published evidence bytes/shape/reference/formula; it does not repeat the offline map graph or geometry proof.
- Fixed v1 catalogue snapshot is new DestinationSelection("moscow", "Москва", "Europe/Moscow", "1"), without live CityCatalogue.Version alias. Actual reviewed bytes normalize to 20 real POIs / 90 directed edges / 110 scoped evidence records. Explicitly isolated real POIs remain isolated. Release is 2026-11-17T03:55:13Z; reading/copying does not renew it.

## Canonical static offer

The Infrastructure project adds one Content glob from Itineraries/Data/**/* to output/publish Trips/Itineraries/%(RecursiveDir)%(Filename)%(Extension). apps/web/project.json adds one existing Angular asset entry with input modules/trips/Travel.Modules.Trips.Infrastructure/Itineraries/Data, glob **/* and output trips-data.

The configured public offer is /trips-data/moscow-outdoor/1/moscow-v1.manifest.json and the five hashed children from the same canonical folder. Targeted tests inspect that actual Angular configuration and compare all six actual built .NET content copies byte-for-byte, then load the built package through the real source. Both Infrastructure/bin and Integration/bin copies were independently rehashed against canonical bytes in curated-facts-byte-identity.json. No Angular application build/server or published/static live URL was run or claimed in this task.

## TDD and verification actually executed

Only FullyQualifiedName~CuratedMoscowFactsTests ran; this class has no collection, database/container, Host, web-server, model, provider or policy/Q fixture. Ordinary NuGet restore was allowed and needed after the first --no-restore invocation produced NETSDK1004 missing Integration obj/project.assets.json (initial setup failure remains in tool output). Initial compile-only xUnit cancellation analyzer failures were fixed by passing TestContext.Current.CancellationToken; a standard InvalidDataException replaced the temporary exception type after the repository exception-constructor analyzer rejected it. No analyzer suppression was added.

Meaningful retained REDs:

- curated-facts-red.log and loader/curated-facts-red.trx: 2 failures / 68 passed / 70 total against unavailable-only source stub. Actual reviewed package success and repeated readiness failed before loader implementation.
- curated-facts-assets-red.log and loader/curated-facts-assets-red.trx: 1 failure / 84 passed / 85 total before the static asset entry/content-copy seams existed.
- curated-facts-utc-red.log and loader/curated-facts-utc-red.trx: 2 failures / 85 passed / 87 total demonstrated equivalent +03:00 aliases accepted in reproduction input/inventory; explicit UTC parsing closed it without converting them.
- curated-facts-filenames-red.log and loader/curated-facts-filenames-red.trx: 3 failures / 87 passed / 90 total demonstrated Windows case aliases and unmanifested files/directories accepted; bounded exact-six-entry validation closed it.

Final command:

```text
dotnet test tests/trips/Travel.Modules.Trips.Tests.Integration/Travel.Modules.Trips.Tests.Integration.csproj --no-restore --filter FullyQualifiedName~CuratedMoscowFactsTests --logger "trx;LogFileName=curated-facts-final.trx" --results-directory TestResults/trips-m2/loader -p:SolutionDir=C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency/ -p:SolutionName=Travel
```

Final NEW receipt: TestResults/trips-m2/curated-facts-final.log and TestResults/trips-m2/loader/curated-facts-final.trx: 90 passed, 0 failed, 0 skipped, about 1 s test duration; build has no analyzer errors. All receipts are fresh local executions. Core worker's 136-case receipt was not rerun or reused as loader proof.

Coverage includes actual 20/90 success, truthful rights/method/unknown hours/isolated POI, ready then withdrawn/missing/corrupt/expired with no stale facts, actual before-build/release-boundary clocks, cancellation, all five offered digests, fixed manifest names/traversal/absolute/URL/case aliases, exact directory children, unknown/missing/duplicate/wrong-type JSON, BOM/CRLF/malformed UTF8/depth/size, wrong method/rights/observation/calibration/humans/destination/version/source clocks/hashes, formula/reference/anchor/path digest, rehashed inconsistent reproduction files and valid-digest bad node/way/endpoints/path ID.

P3 helper-to-Core mismatch closure is direct: actual reviewed helper bytes pass every Core factory. Matching inventory AND payload mutations of an isolated real POI reject digit-leading/65-character/whitespace IDs and leading/trailing/101-character titles. These mutations keep source inventory agreement, so contradiction detection cannot mask invalid Core input. Overlong edge IDs and matching evidence/edge path IDs are separately rejected. No rewritten/trimmed/truncated facts or evidence IDs are returned.

Changed-file formatting: dotnet csharpier check with the exact three owned C# files and Infrastructure.csproj checked 4 clean; TestResults/trips-m2/curated-facts-format-check.log. Existing primary Biome binary ci apps/web/project.json checked 1 clean; curated-facts-json-check.log. No npm installation/cache cleanup or full-repository formatting was performed.

## Exact final source snapshot
- modules/trips/Travel.Modules.Trips.Application/Itineraries/IItineraryFactSource.cs: 261 bytes; SHA256 b28e21ee86c424b5027d2d3babe6ee4f7a367e7a4521b601c0b95e427a660145.
- modules/trips/Travel.Modules.Trips.Infrastructure/Itineraries/CuratedMoscowFacts.cs: 27073 bytes; SHA256 15648e006273d9a8c65bc63441023f6a7a545524b7230614a96510f6b57b570e.
- modules/trips/Travel.Modules.Trips.Infrastructure/Travel.Modules.Trips.Infrastructure.csproj: 1003 bytes; SHA256 9c1b927530934118d14fdeac38bf2ec27a25d66d35f0adc9822d5f5f5a02449d.
- tests/trips/Travel.Modules.Trips.Tests.Integration/CuratedMoscowFactsTests.cs: 26976 bytes; SHA256 a698eabe601468c04b447358fcd85c358dd9a6228872da9de317ed201ed5c897.
- apps/web/project.json: 6141 bytes; SHA256 b4e151e7619c67caa0c2ed00d7d570da86aa523c841503f7c6fc0cdcc2c3ba40.
- .superpowers/sdd/tasks/task-3.1-interface.md: 1106 bytes; SHA256 48c418411fddbe3fa81ed0281a5b92ed1f84b0d33e0e33de3e7b9a4144c30479.

## Canonical bytes and built copies

- ATTRIBUTION.txt: 869 bytes; SHA256 0c8bca1abb555d22fc9a30133287abb9a11cc8554169ede7c379b9473cb01172; Infrastructure and Integration built copies equal.
- input-manifest.json: 295 bytes; SHA256 d5a3025a6670d7aaccfaaff8c15a085197c2c9734fec50339a6ecf2322870ac8; Infrastructure and Integration built copies equal.
- inventory.json: 7711 bytes; SHA256 c04bc32ca50be5fa5caa6057bf89948fee874c01ecdc418d4bd92059695f01ae; Infrastructure and Integration built copies equal.
- moscow-v1.json: 101163 bytes; SHA256 631fd5cd9650e3228af87ef8a31219d7ea59a2e4af42bc4328453f32268ffb08; Infrastructure and Integration built copies equal.
- moscow-v1.manifest.json: 488 bytes; SHA256 8f364b38540a3d8dfd21272021c50f3f18ca6c01d2863617368c12d2e6f60e43; Infrastructure and Integration built copies equal.
- walking-evidence.json: 137293 bytes; SHA256 196c5cd39481bdb55b6c123a4555571a0aa6f2eabc720e24ef3d70f63e3b397b; Infrastructure and Integration built copies equal.

## Remaining authority and limits

Independent review and whole-M2 integration remain parent-owned. No D_demo release verdict, Q_demo outputs/frozen Q inspection, human route-quality assessment, field calibration, full Host/Keycloak/Production/target acceptance or CI acceptance is asserted. No network/provider/model calls occurred except ordinary NuGet asset restore; no DB/container/Host/server, migrations, source download, enablement, staging, commit, push or publication. Static assets remain source/local preparation; no public URL is claimed live. Source freshness remains bounded by the frozen extract publication and identity leases.

## Revision 1 - trusted reviewed release identity (P2 correction)

Read .superpowers/sdd/tasks/task-3.1-review.md and the canonical loader-review.md before this correction. The P2 was reproduced as a NEW executed regression, not inferred from source alone: matching valid POI-title changes in inventory AND payload, a valid coverage-only change, and manifest whitespace changes were all accepted by the prior loader under the same moscow-outdoor/1. TestResults/trips-m2/curated-facts-release-red.log and loader/curated-facts-release-red.trx retain 3 failed / 90 passed / 93 total. Historical prior logs/TRX/byte-identity JSON remain unchanged.

Added one explicitly authorized focused immutable descriptor file, Infrastructure/Itineraries/CuratedMoscowRelease.cs. Its public get-only DatasetId/Version/ManifestSha256 are constructor-validated; scope is only moscow-outdoor, versions/hashes are canonical, and version 1 cannot be rebound to a digest other than the reviewed one. Static ReviewedV1 holds trusted manifest SHA256 8f364b38540a3d8dfd21272021c50f3f18ca6c01d2863617368c12d2e6f60e43 outside the replaceable package. There is no registry, state store, model, cross-city path or automatic release discovery.

CuratedMoscowFacts now accepts a FOURTH optional argument CuratedMoscowRelease? expectedRelease = null. Omitted/null means ReviewedV1. The exact actual manifest bytes are hashed and must equal the immutable descriptor BEFORE JSON/source admission; both manifest and payload dataset/version must match that descriptor. All existing child hashes, strict parsing, mapping, Core factories, clock and withdrawal guards remain. IItineraryFactSource is unchanged. A future separately reviewed release can explicitly provide its trusted distinct identity; expected digests are never obtained from the runtime directory by production reader code.

Semantic-negative coverage remains meaningful beyond the new pin: tests explicitly opt into a separate test-semantic-v1 release, change BOTH fixture version fields before mutations, then bind the known test manifest outside the source before the read. The default/v1 regression tests never opt in or rebind. A positive distinct-release fixture with matching valid changed title loads 20/90 through full mapping and is rejected by the default production constructor, proving the semantic fixture identity path is reachable. Existing invalid matching inventory/payload ID/title tests, valid-digest malformed path tests, source/rights/formula/UTC/parser/size tests use those explicitly bound distinct fixture releases. Four descriptor negative cases cover attempted v1 repinning, another city, whitespace version and uppercase digest. The first review's helper P3 closure is preserved without claiming pin-only rejection proves Core validation.

NEW first GREEN: curated-facts-release-green.log and loader/curated-facts-release-green.trx: 98/98. After formatting, NEW FINAL command uses the same targeted Integration project/filter as above, with --logger "trx;LogFileName=curated-facts-release-final.trx" and the same results directory/SolutionDir/SolutionName. Receipts TestResults/trips-m2/curated-facts-release-final.log and loader/curated-facts-release-final.trx show 98 total/passed, 0 failed/skipped, about 2 s test duration; all 98 individual TRX results are Passed. No full suite, container, Host or Q fixture ran.

NEW formatting receipt curated-facts-release-format-check.log: CSharpier checked 5 clean (port, loader, focused release descriptor, test, Infrastructure.csproj). Angular configuration did not change in this correction, so its prior clean Biome receipt remains applicable; no unrelated formatter rerun was needed.

NEW immutable-byte/source receipt TestResults/trips-m2/curated-facts-release-byte-identity.json verifies all six canonical artifacts still match the original independent-review hashes and both built .NET copies. Canonical manifest is still 488 bytes with the exact trusted SHA above; payload remains 101163 bytes / SHA256 631fd5cd9650e3228af87ef8a31219d7ea59a2e4af42bc4328453f32268ffb08. No canonical data, root config, helper, Core or other worker file was edited.

### Exact revision 1 source snapshot
- modules/trips/Travel.Modules.Trips.Application/Itineraries/IItineraryFactSource.cs: 261 bytes; SHA256 b28e21ee86c424b5027d2d3babe6ee4f7a367e7a4521b601c0b95e427a660145.
- modules/trips/Travel.Modules.Trips.Infrastructure/Itineraries/CuratedMoscowFacts.cs: 27436 bytes; SHA256 f0932e8ddd6ebfb37c92ebdcc292825847b57f88e45a0ada12988e97bb485cb6.
- modules/trips/Travel.Modules.Trips.Infrastructure/Travel.Modules.Trips.Infrastructure.csproj: 1003 bytes; SHA256 9c1b927530934118d14fdeac38bf2ec27a25d66d35f0adc9822d5f5f5a02449d.
- tests/trips/Travel.Modules.Trips.Tests.Integration/CuratedMoscowFactsTests.cs: 31778 bytes; SHA256 944e2e4d9f3f6861904a681078aa4e59bb6444fba7a91e4a521fb490070da7ee.
- apps/web/project.json: 6141 bytes; SHA256 b4e151e7619c67caa0c2ed00d7d570da86aa523c841503f7c6fc0cdcc2c3ba40.
- .superpowers/sdd/tasks/task-3.1-interface.md: 2846 bytes; SHA256 9cc0119f5a07d17bf02df70c156e3036933ea0fc73fc8331a5c59d7c110904b1.
- modules/trips/Travel.Modules.Trips.Infrastructure/Itineraries/CuratedMoscowRelease.cs: 1683 bytes; SHA256 9ae7d7d9eb618c380fa2f9eac21951ebd1fef0cb92325f76a1696fc96a780d32.

Independent scoped re-review remains the next gate. This correction does not assert task acceptance, D_demo/Q_demo/CI/target/publication/live or human/field quality acceptance, and does not change the original source-only authority limits. Interface revision 1 contains the exact constructor and descriptor signatures. No Git mutation, extra source network call, database/container/Host/model execution or enablement occurred.
