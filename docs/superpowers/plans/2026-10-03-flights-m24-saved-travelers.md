# Flights M2.4 saved travelers — approved implementation plan

> For agentic workers: execute task-by-task with explicit ownership and independent review; use superpowers:subagent-driven-development or executing-plans. The user approved this specification and plan on 2026-10-03 with full delivery through merge and cleanup.

Goal: owner-private encrypted fictional traveler CRUD and explicit copy/save from one existing booking slot, preserving booking identity, snapshots and outcome guards.
Architecture: Flights Core validation/value types, Application synchronous service and storage/crypto ports, Infrastructure EF/Data Protection, Api endpoints and strict DTOs; Angular memory-only profiles. No provider, booking event or durable profile command.
Spec: [approved saved-travelers design](../specs/2026-10-03-flights-m24-saved-travelers-design.md). Fresh fetched base f143e360241311b521e70153ba33998b4bff6109 after PR32 merge, with original checkpoint ancestry verified. Re-fetch before execution and retain this recorded base as history, not a replacement for a newer requested base.

## Global constraints

Fictional data; no real provider/payment/Anthropic/paid APIs/evals; no local Host/AppHost/schema apply/key provisioning/deploy; source-only profile migration/designer/snapshot/offline SQL approved explicitly with this plan. Tokens/PII/client operations memory only, no URL/storage/log PII; backend auth unchanged. No M3/OpenSpec/refunds/ancillaries/new SSE/Support. No CI/CD changes. Existing B5 behavior and M2.3 unknown barriers retain their regression tests.

## Review focus

- A delayed profile selection must not overwrite a manually edited row, another quote revision or changed slot.
- Auth error without owner/epoch change erases visible/plaintext data and invalidates late results; metadata-only unknown state cannot unlock booking writes.
- Malformed2xx/receipt failure is unknown; no automatic mutation repeat or old-ID recreation after delete.
- Foreign/missing update404 is resolved before encryption/key checks; owned create collision412 and optimistic conflicts preserve winner.
- A profile DOB does not establish adult eligibility; re-copy/requote checks first departure while later profile edits cannot alter frozen/held snapshots.

## Task 0: exact fresh base and contract ownership

- Verify PR32 merged/closed and all normal CI/post-merge gates. Fetch origin/dev, record exact SHA, prove ancestry to PR32 merge and original M1 checkpoint. Use a new managed worktree/branch from that SHA; never restore archived worktrees or substitute HEAD.
- Re-read AGENTS, current M2.3b code, spec and ADR0024; record any drift before implementation. Root owns shared DTO/EF snapshot/middleware/UI boundaries. Separate crypto+Core from profile UI only after profile/error interfaces are frozen; no M2.5 implementation touching shared files concurrently.
- Capture baseline tests/side effects. DB/Host/Aspire fixtures are CI-only. Preserve main checkout and all other tasks.

## Task 1: profile-independent validation and protected profile

Files: Core/ValueObjects/{SavedTravelerId, SavedTravelerDetails, ProtectedSavedTravelerSnapshot}.cs (new; identifier can follow current Identifiers directory), existing BookingPassengerDetails.cs; Application/Privacy/ISavedTravelerProtector.cs; Infrastructure/Privacy/DataProtectionSavedTravelerProtector.cs; corresponding Unit/ValueObjects and Privacy tests.
Interfaces: SavedTravelerId and opaque revision nonempty UUID; SavedTravelerDetails seven fields with no slot/profile identity inside them; protector context(owner,id,revision) returns ErrorOr opaque envelope or validated details. Existing booking CreateRaw wrapper retains its signature and safe slot metadata.

- RED field boundaries/enums/realDOB/notfuture and no slot ID requirement; booking age at first local date unchanged, future-adult profile permitted without booking guarantee.
- Extract shared base validation, keep booking-specific metadata wrapper; avoid relaxing existing historical replay constructors or inventing slot IDs.
- RED/GREEN actualcrypto with ownedtemporarykeys: wrong owner/id/revision/purpose/tamper/unsupportedformat, restart and key retention, safe error/ToString, bytebufferclear; no key provisioning outside fixtures. Keep all existing booking/inbox purposes unchanged.

## Task 2: EF profile persistence and synchronous application service

Files: Application/SavedTravelers/{ISavedTravelerStore,SavedTravelerService, profile read/write result records}; Infrastructure/Persistence/Entities/SavedTravelerEntity.cs, Configurations/SavedTravelerConfig.cs, SavedTravelerStore.cs, FlightsDbContext.cs and new source migration/designer/snapshot; module composition registration. Tests Unit/SavedTravelers and Integration/SavedTravelers plus migration/initialization inventory expectations.
Interfaces: service methods List/Get/Create/Update/Delete return ErrorOr, authenticated owner explicitly supplied by Api; store returns encrypted row views and atomic conditional outcomes, never raw details. Protection binds new server revision. Service invokes no bus/provider.

- RED fake-store tests owner lookup beforecrypto, deletewithoutkeys, failclosedunreadablelist, bounded20+1lookahead/stableorder, ownedcollision412/foreign404, metadata-onlymutation receipts.
- Implement row columns Id/OwnerUserId/Revision/ProtectedDetails/CreatedAt/UpdatedAt and index for owner+createdAt+id. Explicit version concurrency rather than ambient provider state. Atomic owner+expectedrevision update/delete; insert-only create; map expected DB races to service outcomes inside commit boundary.
- Generate source migration only after approved model; inspect operations and offlineUp/DownSQL for ciphertext-only table/index and no bookinghistorychange. Never execute SQL. Update exactmigration inventories/snapshots where appropriate.
- Add CI-only realEF ownership, update/update, update/delete, create collision, boundedlist, nodecryptforeign, ciphertextstorage and rollbacksource tests. Compile locally, execute only permitted puretests; preserve old booking/projectiontests.

## Task 3: protected HTTP contracts and typed client

Files: Api/Endpoints/{ListSavedTravelers,GetSavedTraveler,PutSavedTraveler,DeleteSavedTraveler}Endpoint.cs, Api/Contracts/SavedTravelerContracts.cs (new cohesive DTO file), bounded body-reader extraction from existing IdempotencyKeyMiddleware into module-local helper/middleware, Composition/FlightsModule.cs. Shared TS separate saved-travelers types/decoder/service files and barrel; no existingbookingtransport rewrite. NoDB Host tests/Documentation OpenAPI fixture.

- RED actualHTTP owner/scope/subject, no-store on every status, strictbodyfields, If-None-Match* create and exactstrongIf-Match update/delete, no/bad/bothpreconditions, foreign404beforemissing keys, safe 503, boundedfield 400, known/chunked 413 before crypto/store.
- Extract/reuse only16KiB boundedmemoryread, keeping bookingrawbytehash/retry exact behavior; no profile responses enter idempotencycache. Route detection tested case/trailing slash, no diskbuffering or payloadlogging.
- Endpoints derive owner, call synchronousApplicationservice and return PII only on authorizedGET/list; mutation receipts ID/revision+ETag only. Use required Guid JSON constructor fields with no optional Guid=default schema trap; retain typedbusinesserrors for invalid/missinginputs.
- Strict TS decoders boundpagecount/IDs/revisions/fields; header/body ETag coherence for receipts, malformed2xx never treatedsuccess. NoDB OpenAPI generation+schema tests; actualHostsnapshot validationCI. Run former123 safeHTTP regressions to ensure bodyreaderextraction preserves booking barriers.

## Task 4: owner profile UI and explicit booking copy

Files: apps/web/src/app/flights saved-travelers page/editor/state and route/navigation additions, existing booking-panel and passengerform integration; client/UI specs. Root coordinates booking panel edits. Reuse existing style components and typography.

- RED pagedloading/empty/keyfailure/errors, explicitcreate/edit/deleteconfirmation,412freshreadreview; mutationdisabledwhilepending, unknown/malformed2xx no automatic resend, refreshcurrentstatewithoutclaiminghistory.
- Add explicit one-profile → one-slot copy and save-thisrow; keep slotID/count/revision, same form validation. Do not add implicitprefill/save-on-hold. Frozen/unknownbooking cannotbealtered; profileerrorcannotclearbookingbarrier.
- Owner+identityEpoch+authgeneration+quoteRevision+slotID+selectionGeneration guardasyncprefill; roweditgenerationprevents clobbering newmanual edits. ClearPII onlogout/identity/navigation/autherror; discardlatecompletions. RetainonlynecessarynonPIIunknownmetadata in currentsession; no browserstorage/historystate.
- GREEN existingmanual1–9flow, profile edit/deleteindependencefromcopieddraft/heldsnapshot, travel-age revalidation. Run meaningful web/API unit/lint/build, not unrelatedvisualredesign.

## Task 5: fictional vertical, review and delivery

Files: tools/demo/flights-search-api.mjs andtests; isolatedfakeauth/demo profilefixtures; tests/travel-e2e/demo saved-traveler cases; README/current-state/ADR0024 and resultreport/instructionfacts.

- Nodefake implements exactprofileCRUD/concurrency/errors in memory with explicitfakeidentityisolation only inside demo build; never reinterpretfakeauth as backendproof. Controlledlostresponse/keys-unavailablefixtures, no realcrypto/provider.
- BrowserfictionalCRUD→selecttwoprofiles→group booking→edit/deleteprofile leavesbookingunchanged; staleversion, missingkey, lost/malformedwrite, late prefill/manual edit/authclear, desktop360px/keyboard, noPIIURL/storage/logs. Existing M2.3/B3–B5 staygreen.
- Verify purecrypto/Core/Application, no-databaseHTTP/OpenAPI, TS/web, architecture/build/format/docs and appropriatefakeintegration; realEF/Host/Aspire/outbox only CI, paidlaneoff. Independentreview, repairimportantfindings andtargetedregressions.
- Commitall code/tests/docs, pushownbranch, PRdev andattach, allmandatoryexact-headCIincludingdependentE2E green, normalmerge, post-mergeCI verification, fresh dev fast-forward andonlyownbranch/worktree/temp cleanup. UpdateM2checkpoint withoutclaimingM2complete; M2.5 remainsnext.
