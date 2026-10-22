# Task 3.2: owner/revision-bound read-only candidate service

Status: implementation and isolated fictional Unit self-verification complete; independent task review remains pending. This is not whole-M2, real-source/Q, API/Host, CI, target or rollout acceptance.

## Identity and owned scope

Worktree: C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency.
Verified unchanged HEAD e654c76915103a82083ef2abc01c0579117c9749; branch codex/trips-m2-moscow-itinerary.
Ownership: only the four new Application files, ItineraryCandidateServiceTests.cs, early interface note and this report/owned evidence directory. No changes to Core, loader, canonical facts, shared test facts, root config, README or task checkboxes. Parent owns README/application-flow documentation and canonical task state. Existing other-worker files remain untouched.

## Public consumer contract

Exact public signatures are in task-3.2-interface.md, published to parent before implementation and retained unchanged:
- `ItineraryCandidateService(TripPlanService, ITripPlanCodec, IItineraryFactSource, IItineraryCandidateResponseEncoder, ItineraryAdmissionGate, TimeProvider)`.
- `GenerateAsync(OwnerIdentity?, Guid tripId, long expectedRevision, CancellationToken = default) -> Task<ErrorOr<PreparedCandidateResponse>>`.
- `ItineraryCandidateResult.BaseRevision`, with `ScopeUnsupportedCandidateResult(Destination, Reason, PolicyVersion, AsOfUtc)` or `ComputedCandidateResult(Outcome, Facts)`.
- `IItineraryCandidateResponseEncoder.EncodeAsync(ItineraryCandidateResult, CancellationToken = default) -> Task<ErrorOr<ReadOnlyMemory<byte>>>`.
- `PreparedCandidateResponse.Utf8Json`: exact complete response, owned copied storage before final read. The internal constructor copies producer memory; later producer-array mutations cannot change returned bytes.
- Gate: public no-argument constructor; register ONE singleton. Service may be scoped. Exactly two active supported computations with immediate busy rejection, no queue/retry.

Parent's contract ruling retained: canonical deadline code is `Trips.ItineraryDeadline`, not brief typo `Trips.ItineraryDeadlineExceeded`. Other M2 codes: `Trips.ItineraryRequiresEmptyPlan`, `Trips.ItineraryUnavailable`, `Trips.ItineraryBusy`. M1 read errors and owner/revision codes propagate. Api must map the codes and write Utf8Json directly as application/json, without reserialization.

## Behavior and invariants

Null owner and invalid revision are rejected before reads; M1 Get enforces owner before decrypt. Exact saved revision and all-day empty calendar precede one captured TimeProvider asOfUtc. Unsupported destination/range/historical/horizon results require no facts and no admission, carry no fake source identity, but still require encoding and the final same-owner Get/revision check. Manual 4-14 day M1 plans stay valid.

Supported source is loaded once and its binding/build/release/model freshness checked before admission. Actual static Core policy and independent validator receive the same saved destination and captured instant. Generation/validation failures are typed unavailable, without repair/fallback or user-input 400. Core city-independent policy remains unchanged.

Admission's two-second TimeProvider deadline is linked with caller cancellation and remains active through policy, validation, encoding, ordinary M1 reserve preflight and final Get. The service awaits actual encoder completion; no Task.Run, WaitAsync/WhenAny early-return race, background work, provider or model calls exist. Caller cancellation propagates even when a late encoder throws a different exception; internal deadline has canonical deadline status. Exactly one finally releases each admitted operation.

Encoder output must be nonempty valid UTF8 and no larger than 131072 bytes. Independently, computed outcomes become a temporary TripPlan.Restore with every requested day, stable proposed UUIDs, historical bounded notes, null times/money, and the original ID/owner/title/brief/destination/revision/timestamps. The complete ordinary M1 detail is measured through ITripPlanCodec with reserveMaximumServerFields=true; invalid/oversize fails whole preview, no truncation. This temporary object is never replaced, protected or persisted.

Owned bytes are copied before final Get; final deletion/foreign owner returns404, changed revision412. Computed expiry is checked after final read; expired preview is unavailable. No store writes, protector writes, revision/timestamp mutation, hidden retries or private logging were added.

## Test boundary and evidence

Only xUnit v3/Shouldly semantic Unit tests and fictional ItineraryTestFacts-based private builders. No real Moscow package, frozen Q cases or Q outputs read or generated. No integration/solution/Docker/Host/database/model/network/provider/enablement/Git mutation or package installation/restore was performed.

Initial compile-only missing-contract checks and test Memory->ReadOnlyMemory conversion fixes are not counted as RED. Compile scaffolding then returned typed unavailable; actual meaningful RED (8 failed/0 passed/0 skipped) observed missing owner guard, unsupported and successful candidate behavior. One receipt-directory setup error occurred before a test ran and was fixed. A later 34-case intermediate run had18 failures from test-fixture read-counter/equality and food-only-as-empty assumptions, not accepted business changes; counter evaluation/value comparison were corrected and Empty now uses independently valid fictional known-closed POIs. Raw intermediate output is retained.

A separate focused RED reproduced two real cancellation bugs: late non-OCE encoder exception masked deadline/caller cancellation. Both failed before the catch-policy fix, then passed. Subsequent 36/36 and45/45 bounded runs passed. Final source, including three extra final-read race cases, was verified by the full suite:

`dotnet test tests/trips/Travel.Modules.Trips.Tests.Unit/Travel.Modules.Trips.Tests.Unit.csproj --no-restore --logger "trx;LogFileName=task-3.2-full-unit.trx" --results-directory .superpowers/sdd/tasks/task-3.2-results -p:SolutionDir=C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency/ -p:SolutionName=Travel`

Exit0:294 executed/294 passed/0 failed/0 skipped; XML contains48 ItineraryCandidateServiceTests cases (existing baseline246 retained). Coverage includes owner-before-decrypt; stale/nonempty; source-free unsupported and saved-zone date; computed Candidate/Empty and complete ordinary reserve; wrong/future/expired source and zero retry; encoding error/exception/empty/invalidUTF8/multibyte boundary/oversize; final update/delete/foreign races during encode and immediately before final Get; byte freezing before final read; current expiry; shared two-slot/no-queue behavior; supported failure cleanup across3 repetitions per path; cooperative deadline/caller cancellation; uncooperative-fake lease retention and late-error cancellation priority; source/unsupported ordering while both slots occupied; internal Core invalid input mapping.

Changed-file `dotnet csharpier format` and `dotnet csharpier check` both exit0, exactly5 owned C# files; no global formatter. Free C: headroom checked before writes and final run, approximately5.3GiB.

Receipts in `.superpowers/sdd/tasks/task-3.2-results/`:
- `red-raw.txt`, `task-3.2-red.trx`: meaningful initial RED8.
- `green-raw.txt`, `task-3.2-green.trx`: retained failed intermediate fixture run18/34, not green acceptance.
- `green2-raw.txt`, `task-3.2-green2.trx`:34/34.
- `red-cancel-raw.txt`, `task-3.2-red-cancel.trx`: meaningful cancellation RED2.
- `green-cancel-raw.txt`, `task-3.2-green-cancel.trx`:36/36.
- `green-expanded-raw.txt`, `task-3.2-green-expanded.trx`:45/45.
- `format-raw.txt`, `format-check-raw.txt`:5 files formatted/checked.
- `full-unit-raw.txt`, `task-3.2-full-unit.trx`: final294/294 with48 owned cases.

## Limits and downstream work

The encoder is an actual-consumer Application port; its fake is only a semantic Unit double. Real strict JSON/DTO projection, referenced-only facts, HTTP status/no-store/raw application-json behavior, enabled/Production/default-off composition and singleton registration belong to tasks3.3/3.4. This task does not claim production JSON validation or actual HTTP payload-size proof; it checks the exact encoder bytes and forwards the ordinary codec reserve correctly.

No extra generator port was added. Actual Generate+Validate run in service tests; arbitrary corrupt Core-produced outcomes cannot be injected through an Application boundary. Existing independent validator malformed-result tests are included in the294-case full run; source binding/internal generation failures and encoder failures are tested at Application level. This limitation is reported rather than creating a test-only extension point.

The deadline is cooperative. A deliberately uncooperative encoder can keep the request and slot active beyond2seconds; the service does not pretend it finished or free its slot early. Tests prove that limitation and later deadline rejection after it actually finishes. No OS-hard cancellation/SLA claim.

Final-read protection is best effort: a race after response is resolved later by adoption availability/GET and ordinary M1 PUT CAS. Source withdrawal/package replacement after captured facts needs downstream adoption availability, not a fabricated current-source claim. Real source/demo quality, CI, target provisioning, live traffic and rollout remain separate gates.

## Exact SHA256 binding

| File | SHA256 |
| --- | --- |
| modules/trips/Travel.Modules.Trips.Application/Itineraries/ItineraryCandidateService.cs | f4fa3f5ff20d1f2b51d91ffae741a0d14811692d1285dcb7981dbe46874319ed |
| modules/trips/Travel.Modules.Trips.Application/Itineraries/ItineraryCandidateContracts.cs | 44b86014ac07e3e270fa4142a71fc41c90d2d1de370e7e440dfa4d7a96b31daa |
| modules/trips/Travel.Modules.Trips.Application/Itineraries/IItineraryCandidateResponseEncoder.cs | 1267577601ec06dc6895bf2a50600f20349e7bd8205ee0996a4712d2e181cff8 |
| modules/trips/Travel.Modules.Trips.Application/Itineraries/ItineraryAdmissionGate.cs | bd84bbebe83bcf6681ed3311f09340693644edc549f70c92756d2d383d01d510 |
| tests/trips/Travel.Modules.Trips.Tests.Unit/ItineraryCandidateServiceTests.cs | d681b4170697cbbb743441ea43def86281018a1563f39d6ebfbea5ea7637f703 |
| .superpowers/sdd/tasks/task-3.2-interface.md | 206ad3225e3928a255f0e37592f9ae3e65a7f40e54572ae35378a28df789007e |
| modules/trips/Travel.Modules.Trips.Core/Itineraries/ItineraryPolicy.cs | c5df39020d036bf5896b4054ee32f48d994c0fd0f949a46c1a801b8607f0d102 |
| modules/trips/Travel.Modules.Trips.Core/Itineraries/ItineraryCandidateValidator.cs | 5f8defec3c0b7aefabaad17f7f1159e53972a15b20ff15a1395c82d1cc90ca61 |
| modules/trips/Travel.Modules.Trips.Application/Itineraries/IItineraryFactSource.cs | b28e21ee86c424b5027d2d3babe6ee4f7a367e7a4521b601c0b95e427a660145 |
| .superpowers/sdd/tasks/task-3.2-results/format-check-raw.txt | f756bb744616d5f4f050df70cde80ef5bba7412e2f166de4a0fc44b7c4bc658e |
| .superpowers/sdd/tasks/task-3.2-results/format-raw.txt | 08f87c79cb2f7ce66ec79eb98d005cb18dae7a71a715163226c13a195f4ace8d |
| .superpowers/sdd/tasks/task-3.2-results/full-unit-raw.txt | 9baa5f110145f628c6a414bf08b8f8715cd278b656b82cc447a3dca84f64f374 |
| .superpowers/sdd/tasks/task-3.2-results/green-cancel-raw.txt | 3705d4654275616fa3d9ac69b0af6ba373d989ffe4c4c1d56c6a208978cb9983 |
| .superpowers/sdd/tasks/task-3.2-results/green-expanded-raw.txt | 8c3f9bd6abfba12e57961c0fb1840cde58280f4daea741aaae4a9de726716b58 |
| .superpowers/sdd/tasks/task-3.2-results/green-raw.txt | 646cbfeb8de0e4f89b38873de7bff6254cdc6628a6a041855a36d8809add46df |
| .superpowers/sdd/tasks/task-3.2-results/green2-raw.txt | a67ea3bbebcdbd84462cc0a81400b54d3af78f496d06970acb1395032009dca0 |
| .superpowers/sdd/tasks/task-3.2-results/red-cancel-raw.txt | acd78cf27504a08053b171cc71cd02967d3f91ad0c78daf3f1120add8afcbe57 |
| .superpowers/sdd/tasks/task-3.2-results/red-raw.txt | a878489786ee44a0a0ad0be8893335acab3ec16095fd4f9fb8d5c56ac9e084e9 |
| .superpowers/sdd/tasks/task-3.2-results/task-3.2-full-unit.trx | 21520d5bea420e53c59ecf0443dba35991076ab83f5c55cdec382ec24bf288f1 |
| .superpowers/sdd/tasks/task-3.2-results/task-3.2-green-cancel.trx | c47ea7067a1623e4bfa32080cb0a663079252b86623545b48ae17e9f516fa8cf |
| .superpowers/sdd/tasks/task-3.2-results/task-3.2-green-expanded.trx | 175dc91aad5121b9e0d675f580cdbb1fd1688d917ba03475e66d66deb2690c01 |
| .superpowers/sdd/tasks/task-3.2-results/task-3.2-green.trx | 95bc64acf1c198973f1770fddb4d1b3c21f4945ed5b8c56e0509e81a630631da |
| .superpowers/sdd/tasks/task-3.2-results/task-3.2-green2.trx | d3b953c87fe436e17a67fb4519ae90c1555947b699465d57ee4c5d8287d5753f |
| .superpowers/sdd/tasks/task-3.2-results/task-3.2-red-cancel.trx | 5baed659732f42a30ac69dcd9978002c073d311e6ca70568d94cb22595a61180 |
| .superpowers/sdd/tasks/task-3.2-results/task-3.2-red.trx | 86f07cbcdfccc3a328b074f74c599d642ce43893d3b7c60af913ef741533f3b7 |
