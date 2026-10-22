# Spec compliance: NEEDS FIXES (one P2 evidence gap)

Task quality: NEEDS FIXES for the same required acceptance gap; no separate production-code defect found in this scoped review.

Scope: combined Tasks 3.3/3.4 only, not whole M2. Fixed base/HEAD supplied by controller: e654c76915103a82083ef2abc01c0579117c9749; uncommitted source, no new commits. Reviewed all 21 files in the 153354-byte full-context package once, in sequential chunks. No changed-source reread was needed for a cut-off hunk; subsequent symbol lookups obtained citation line numbers only.

Review identity:
- Implementer report SHA256: 1c8781c6b706360c7a776d43c38c143a90fc97d416e186d5225aa66ebe15c84a (independently recomputed).
- Review package SHA256: 82cd09b49519f7d7cc3d24a2fc7aa1b7c2b250199b38c09bc2b712118ce59d59 (independently recomputed).
- Source/vector/canonical-data identities are the frozen snapshot in task-3.3-3.4-results/final-hashes.json, checked by the controller before dispatch. This reviewer did not repeat those source/data hash checks or run Git commands.

## Strengths and verified requirements

- Exact shared M2 route classifier, ordinal-ignore-case and one trailing slash, router-compatible Guid including Guid.Empty; unknown paths/methods do not gain the availability bypass: modules/trips/Travel.Modules.Trips.Api/Middleware/TripsItineraryRoutes.cs:13. Off routes return 404 before authentication through TripsResponsePolicyMiddleware.cs:29; resolution/authentication sentinels are asserted in tests/trips/Travel.Modules.Trips.Tests.Integration/TripsItineraryHttpTests.cs:650.
- Availability bypasses readiness only after trusted owner validation, rejects query/body and does not resolve ITripsReadiness/store/protector/codec: modules/trips/Travel.Modules.Trips.Api/Middleware/TripsRequestGuardMiddleware.cs:37, tests/trips/Travel.Modules.Trips.Tests.Integration/TripsItineraryHttpTests.cs:57. Candidate retains exact M1 If-Match parsing and normal readiness, with a separate strict UTF8 empty-object parser reading at most 1025 actual bytes: TripsRequestGuardMiddleware.cs:46 and :171. Known/unknown length 1024/1025-byte requests are covered at TripsItineraryHttpTests.cs:283.
- Typed statuses 409/503/429/504 are mapped explicitly in modules/trips/Travel.Modules.Trips.Api/Contracts/TripsProblems.cs:31. Busy alone adds Retry-After:1 at Endpoints/ItineraryCandidateEndpoint.cs:27. Real service/shared-gate/deadline paths are exercised at TripsItineraryHttpTests.cs:439 and :462.
- Encoder projects the referenced subset, checks whole normally escaped UTF8 and depth, preserves positive revision/path/anchor strings, original titles, null money/times, scope-bound facts and directed adjacency: modules/trips/Travel.Modules.Trips.Api/Contracts/ItineraryCandidateDtos.cs:21 and :90. Source model identity, explicit uncalibrated assumptions, null human actors, fixed attribution/derived-data offer and 500-scalar notes are retained. The 500-line codec has one bounded schema responsibility; no generic framework or runtime generator port was introduced.
- Endpoint writes prepared bytes directly: modules/trips/Travel.Modules.Trips.Api/Endpoints/ItineraryCandidateEndpoint.cs:34. Focused external check confirmed the actual Application consumer awaits encoding, performs ordinary M1 restoration/reserved-size preflight, copies bytes, then final owner/revision/expiry checks: Application/Itineraries/ItineraryCandidateService.cs:146; ItineraryCandidateContracts.cs:24. No post-final-read reserialization was found.
- Availability exposes exactly ten bounded fields and rechecks expiry immediately before success: modules/trips/Travel.Modules.Trips.Api/Endpoints/ItineraryAvailabilityEndpoint.cs:27. Package changes, withdrawal and first/second clock expiry are tested, with unchanged saved revision.
- Production explicitly makes the effective itinerary flag false before registering M2 dependencies, including the factory source; both simple flags precede the M1 disabled return. Source is fixed-path and pinned to ReviewedV1; singleton admission/encoder/source and scoped service are appropriate: modules/trips/Travel.Modules.Trips.Api/Composition/TripsModule.cs:32, :58 and :77. Six real descriptor cases cover this fence; default no-options MapWolverineEndpoints() is separately exercised with absent M2 services and working manual destinations: TripsItineraryHttpTests.cs:559. This is TestServer proof, not an actual Host launch.
- Six production HTTP vectors are checked by whole JSON equality plus byte comparison, with no golden-write path: tests/trips/Travel.Modules.Trips.Tests.Integration/TripsItineraryHttpTests.cs:483. Candidate/partial substitute only validated generated UUIDs; error substitutes the validated framework traceId; ready/empty/unsupported compare unmodified bytes. The report correctly states this limitation. Fixtures remain fictional and are not Q outputs.

## Findings

### P2 - Required real HTTP multibyte output-boundary proof is missing

Location: tests/trips/Travel.Modules.Trips.Tests.Integration/TripsItineraryResponseCodecTests.cs:124; tests/trips/Travel.Modules.Trips.Tests.Integration/TripsItineraryHttpTests.cs:483.

The only escaped multibyte response overflow test calls ItineraryCandidateResponseEncoder.EncodeAsync directly. The HTTP suite covers small normal vectors and request-body limits, but never drives a near-limit/oversized complete candidate response through the production endpoint, service, encoder and ordinary M1 codec together. Task 3.3 and the coupled brief explicitly require real HTTP UTF8 near-limit/no-truncate proof. Thus the existing 83 passing cases do not close that required acceptance criterion; this is an evidence gap, not a demonstrated bypass in the production cap.

Fix: add focused fictional-source HTTP cases for a three-day active plan with large multibyte rights metadata, using the real production encoder and M1 codec. Verify a measured near-limit valid response is complete and parses without lost fields, and an over-128KiB response returns RFC7807 503/code Trips.ItineraryUnavailable with no-store, no truncated candidate, no plan/store mutation and no automatic resubmit. Retain the direct codec test. Use a focused run and publish its raw receipt; no broader suite rerun is requested by this review.

No P1 or separate P3 findings.

## Named-risk checks outside the diff

- Risk: frozen-byte response could be nominal only or bypass ordinary M1 reserve. Inspected Application/Itineraries/ItineraryCandidateService.cs:146 and ItineraryCandidateContracts.cs:24; Infrastructure/Serialization/TripPlanJsonCodec.cs:77 and :123 confirm actual reserved server fields and actual encoder measurement. Order and consumer binding are correct.
- Risk: availability might trust unconstrained metadata or emit fabricated human/model identity. Inspected Core/Itineraries/ItineraryFacts.cs:76, :249 and :760: private immutable factories enforce hashes/identifiers/canonical UTC, model versions, null human actors, and releaseValidUntil no later than model expiry. The endpoint need not introduce a second mutable metadata validator.
- Risk: validated source could mean only a payload digest, with stale/withdrawn package identity retained. Inspected Infrastructure/Itineraries/CuratedMoscowFacts.cs:49: pinned manifest and whole bundle hashes precede strict mapping, with fresh withdrawal/clock checks before returning. Composition explicitly passes ReviewedV1; no network or arbitrary source registry was added.

## Verified receipts

- Parsed raw boundary-final.trx: 83 total/executed/passed, zero failed/notExecuted/warning; all 83 individual results Passed. Definitions: 61 HTTP, 6 composition, 16 response codec.
- Parsed raw m1-http-regression.trx: 30 total/executed/passed, zero failed/notExecuted/warning; all 30 individual results Passed. Definitions: 24 TripsHttpTests, 2 composition, 1 each size, owner claims, date-range conflict and privacy diagnostics.
- Parsed red.trx and read red-raw.txt: 2 failed, 0 passed, 0 skipped; both initial off-route tests received 401 instead of required 404. This is meaningful RED.
- Read boundary-final-raw.txt, m1-http-regression-raw.txt, format-raw.txt and format-check-raw.txt: final test logs contain no build/test warnings; CSharpier reports 15 formatted and 15 checked files. No suites or formatters were rerun by this reviewer.

## Scope limits and next gate

Tasks 3.3/3.4 remain unaccepted until the P2 HTTP proof is supplied and rechecked. Parent-owned operations/README/task-state updates remain controller work. TypeScript decoder/UI/adoption, Q_demo, whole-M2 source/architecture checks, actual Host/Keycloak, CI/postmerge, real-source/human quality, target/provisioning and rollout are separate and receive no acceptance here. The reviewed source remains default-off; no Git mutation, Host/database/model/provider/network execution, Q output, source edits or subagents were used. Only these two report copies were written.