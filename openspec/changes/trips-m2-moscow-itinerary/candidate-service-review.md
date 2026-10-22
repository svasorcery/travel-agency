# Task 3.2 independent review

Spec compliance: PASS within the assigned task 3.2 Application service scope.
Code quality: PASS. No P1, P2 or P3 findings.

## Scope and binding

Reviewed 2026-10-21 against the task-3.2 brief/interface, actual tasks.md task 3.2 and design section 4, including the approved source-free result union, actual API encoder consumer and freeze-before-final-read refinements. This is a task gate, not a whole-M2 or merge review. README/application-flow documentation remains parent-owned, outside the five implementation files assigned here.

Fixed base/HEAD supplied by controller: e654c76915103a82083ef2abc01c0579117c9749. No Git commands were run; ancestry/branch identity is not independently re-proven by this review.

Reviewed package: `.superpowers/sdd/tasks/task-3.2-review-package.txt`, 51173 bytes, SHA256 `e2c4d71d2f29b88d1f99ce501cdbcd630b740e4812d5723f204b9af05863dc91`. It contains all five new owned files. The diff was read once in bounded chunks; a truncated generator hunk was retrieved from that same package. Changed source files were not separately re-read. Hash-only verification confirmed current files and every source/interface/receipt hash listed by the implementer match the report.

Implementer report independently hashed: `33abeb4a4c03a9157600db13239ba924cb48b1a144ac635fb43e84965bc15317`.

## Verified behavior and strengths

- `modules/trips/Travel.Modules.Trips.Application/Itineraries/ItineraryCandidateService.cs:26`: null owner/revision guards precede reading; original owner load, exact revision and empty-calendar checks precede the single captured asOfUtc. `:47`, `:210` preserve saved-zone scope; unsupported results bypass source/admission while retaining encoding and final owner validation. `:63`, `:224` reject bad source binding/currentness before admission without fabricated empty outcomes or retries.
- `ItineraryCandidateService.cs:71`: the injected TimeProvider controls the linked two-second deadline. Actual static policy and independent validator execute before encoding. `:117`, `:131`, `:138` retain caller cancellation precedence, typed Trips.ItineraryDeadline and exactly one finally release after awaited work. `ItineraryAdmissionGate.cs:6` is a focused two-slot immediate SemaphoreSlim admission guard; no queue, background continuation or extra generator port.
- `ItineraryCandidateService.cs:152`: encoded output is nonempty, strict UTF8 and bounded by bytes, independent of the ordinary M1 reserve preflight at `:174`/`:187`. Restore preserves original identity, revision, timestamps and brief while mapping proposed blocks to null time/money intentions. No persistence/protection-write path is called.
- `ItineraryCandidateService.cs:195`: owned response storage is frozen before final owner GET, revision and expiry checks. `ItineraryCandidateContracts.cs:30` copies producer memory. Final deletion/foreign ownership/revision races reject the response. No error body or private text is logged.
- `tests/trips/Travel.Modules.Trips.Tests.Unit/ItineraryCandidateServiceTests.cs:17`, `:84`, `:172`, `:224`, `:263`, `:280` exercise authorization, ordinary projection, encoding boundaries and final-read races using real TripPlanService plus sentinel/counter doubles. `:293`, `:325`, `:367`, `:399`, `:436` exercise shared admission, cooperative deadline, uncooperative work retaining its lease, late-error cancellation precedence and repeated cleanup. Tests assert outcomes and effects rather than merely mock invocation order.

## Named-risk checks outside the diff

- Owner-before-decrypt and final-read race risk: `modules/trips/Travel.Modules.Trips.Application/TripPlanService.cs:187`/`:206` checks loaded row owner and ID before Unprotect, then validates decoded identity/revision/timestamps and complete detail size. The new service reuses this exact read boundary. Fake row changes therefore pass through genuine M1 authorization/binding checks.
- Ordinary M1 reserve and accidental mutation risk: `modules/trips/Travel.Modules.Trips.Core/TripPlan.cs:7` supplies null default times/money; `:173` Restore validates all requested dates, unique bounded blocks and original metadata, creating an event-free object without Replace. `modules/trips/Travel.Modules.Trips.Infrastructure/Serialization/TripPlanJsonCodec.cs:77` and `:123` measure actual full detail with maximum revision/timestamps and UTF8 byte count. The Application call correctly forwards reserveMaximumServerFields=true. The unit codec is a semantic double, not exact production serialization proof.
- Static policy/validator seam and circular-test risk: `modules/trips/Travel.Modules.Trips.Core/Itineraries/ItineraryPolicy.cs:16` accepts saved destination, frozen asOfUtc and cancellation; guards are city-independent. `ItineraryCandidateValidator.cs:12` independently checks result metadata, source currentness, counts and dates. `tests/trips/Travel.Modules.Trips.Tests.Unit/ItineraryCandidateValidatorTests.cs:66`, `:191`, `:338`, `:493`, `:886` mutate global claims, stops, directed paths/provenance and expiry, then assert whole-result typed rejection. The service invokes that real validator; no production extension point was introduced solely to inject malformed generator output.

## Evidence checked

Read existing raw output and parsed original TRX; no tests were rerun.

- `task-3.2-results/task-3.2-full-unit.trx`: total/executed/passed 294, failed/error/timeout/notExecuted/warning 0. Exactly 48 ItineraryCandidateServiceTests and 110 ItineraryCandidateValidatorTests results. Raw full-unit output agrees: 294 passed, 0 failed, 0 skipped. SHA256 `21520d5bea420e53c59ecf0443dba35991076ab83f5c55cdec382ec24bf288f1`.
- `task-3.2-results/task-3.2-red.trx`: 8 executed/8 failed/0 skipped, with behavior assertions for missing Forbidden and unsupported/candidate success, not compiler failures.
- `task-3.2-results/task-3.2-red-cancel.trx`: 2 executed/2 failed/0 skipped. Deadline was incorrectly unavailable and caller cancellation failed to propagate; these same cases pass in final TRX.
- `task-3.2-results/format-raw.txt` and `format-check-raw.txt`: exactly 5 files formatted/checked. No warning text in retained raw outputs. Intermediate failed receipts remain retained and are not counted as acceptance.

## Unverifiable here / downstream gates

- Tasks 3.3/3.4 must prove real strict API DTO JSON, referenced-only fact projection, HTTP codes including Trips.ItineraryDeadline=504, Retry-After:1, no-store, raw application/json writing without reserialization, feature-off/Production guards and one process-wide singleton admission registration. This review proves the Application seam, not those later consumers.
- Actual candidate bytes combined with the production M1 codec and enabled composition require downstream tests. The unit fake deliberately returns small arbitrary UTF8 JSON and demonstrates byte/order/cancellation behavior; it does not validate the final wire schema. There is no claim of real-source/Q, Host/Keycloak, database, model/provider/network, target, CI or rollout acceptance.
- Deadline is cooperative: uncooperative encoding may remain active beyond two seconds, retaining its slot until actual completion, then rejecting late success. No hard execution-time SLA is claimed. The final owner read is best effort; later adoption availability and ordinary M1 PUT CAS must handle races after response/source replacement.
- Arbitrary malformed Core outcomes are tested at the independent validator boundary; there is no injectable malformed-generator branch through Application. This is explicit coverage scope, not missing justification for another production port.

Task quality approved for the reviewed source and evidence. This report does not authorize CI, push, commit, enablement or publication.
