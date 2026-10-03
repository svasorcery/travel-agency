# Flights M2.4: private saved travelers — proposed design

Status: approved by the user on 2026-10-03, including the implementation plan and source-only migration. The existing full delivery cycle is authorized: implementation, suitable tests, independent review, commit, push, PR into dev, green mandatory CI, merge, local dev synchronization and own cleanup. Local schema apply, real integrations and deployment remain excluded. Fresh fetched origin/dev on 2026-10-03: f143e360241311b521e70153ba33998b4bff6109 (PR32 merge), ancestry verified to M1 d75186054a33de0e1cb688da7e3e7cc42927dadf and PR32 head fe864dc65a30e621297c582a512f96ab73e0a31f. PR32 required CI37117974580 succeeded; post-merge CI37118585079 also succeeded (12 normal jobs; E2E is PR-only and passed before merge; paid evals skipped). A new managed flights-m24-design worktree starts at that exact fetched SHA; no archive was restored.

## Intended result and bounded choices

An authenticated owner can explicitly save, list, edit and delete fictional traveler details and copy one saved profile into one current passenger slot. Manual booking remains fully usable. One profile is reusable personal data, not a Keycloak account, booking passenger ID, supplier passenger reference, or event-sourced aggregate. Profile edits/deletion never modify a copied form or booked snapshot.

Recommended: one Flights EF table, existing PII key ring with a separate purpose, a small synchronous Application service and conditional CRUD. No profile event stream, background job, idempotency response cache, search index, duplicate detection, shared profiles, imports, passports or loyalty fields. Alternative POST/server-generated IDs is familiar but makes lost-create responses harder to reconcile. Reusing booking idempotency/durable messages creates unnecessary PII response-cache and outcome machinery. Conditional PUT with a client-created opaque ID provides a known read target and EF concurrency without a new operation store.

Save seven current booking detail fields: title, givenName, familyName, dateOfBirth, gender, email, phone. Names/contact bounds and explicit enums remain M2.3b. A profile validates a real DOB not after today; it does not assert eligibility for an unknown travel date. Copying always applies the existing18-on-first-origin-local-departure rule. This preserves the accepted M2.3 rule for someone who turns18 before departure; it does not add child/infant booking support. No new adult-today restriction.

## Source-backed seams

FlightsDbContext and its entity configurations own non-event data. BookingPassengerDetails.CreateRaw contains field rules plus slot-ID error metadata: extract a profile-independent validation entry point and keep the booking wrapper adding safe slot metadata, without inventing a booking ID. Existing FlightsPiiProtectionProvider owns the dedicated certificate-protected Data Protection ring; new profile purpose/envelope do not alter key lifecycle. Existing IdempotencyKeyMiddleware limits only order POST bodies and persists successful response bodies: reuse its bounded in-memory reader for traveler mutations, not its response cache. The group passenger FormArray and auth identityEpoch supply copy/clear/late-response boundaries.

## Domain, storage and atomicity

SavedTravelerId is a nonempty client-generated UUID distinct from BookingPassengerId. Profile row fields: Id (PK), OwnerUserId, Revision (nonempty server-generated UUID), ProtectedDetails (versioned envelope), CreatedAt and UpdatedAt from TimeProvider. Owner+CreatedAt+Id index supports a bounded stable list. No plaintext name/contact/nickname or duplicated searchable birth date. One explicit source migration/designer/snapshot and offline SQL review is requested with plan approval; it is never applied locally.

Protect the complete validated details with purpose components saved-traveler.v1 / owner / profileId / revision under the existing Travel.Flights.Pii.v1 application. Swapping owner/profile/revision or ciphertext fails closed. Validate decrypted format/shape through current base field rules, clear temporary plaintext bytes, and suppress sensitive ToString/exception/log output. Keep legacy booking and inbox purposes unchanged. Deletion removes this profile row only; it is not cryptographic erasure of backups or booking history.

Use a scoped Application service and persistence/protection ports. API never imports Infrastructure. The service authorizes the owner and handles validation/crypto; Infrastructure repository performs one EF atomic insert/update/delete and maps expected unique/concurrency outcomes. No plaintext profile command/query result enters Wolverine durability. Avoid ambient booking transactions/outbox: profiles have no supplier effect or notification.

Update and delete predicate on Id+OwnerUserId+expectedRevision atomically. Updates assign a new revision and ciphertext bound to it. A stale revision cannot overwrite another edit; a deleted row cannot be updated/upserted. Create inserts only, never updates an existing ID. Random IDs make collisions exceptional: create with an existing owned ID is412, and a foreign ID is404. Check ownership/existence before encryption on update so missing keys cannot replace a foreign/missing404 with503. A fresh explicit create uses a fresh client ID; no automatic old-ID recreate after uncertainty/deletion and no tombstone engine.

## HTTP contract

Routes under /api/flights/travelers, same JWT issuer/audience/GUID sub; no body owner/userId. All routes require the existing flights:book policy because they handle reusable PII and the frontend already requires that scope. This adds no realm change or weaker validation.

- GET collection: offset multiples20, bounded20 displayed +one metadata lookahead; return items with id/revision/details and hasMore/offset. Stable CreatedAt DESC, Id DESC. Fetch no more21 owned rows, decrypt only returned20. No total count or snapshot claim; concurrent changes may shift pages. No query/name search.
- GET /{id}: owner lookup first, then decrypt. Return profile and strong ETag equal to quoted revision UUID.
- PUT /{id} create: If-None-Match:*; full seven-field details body, one insertion,201 and metadata-only id/revision receipt plus ETag. Client knows ID before dispatch.
- PUT /{id} update: exact single strong If-Match revision; full details replacement, atomic compare/update,200 metadata receipt+ETag. Missing/malformed/both preconditions are safe428/400; stale owned revision412; foreign/missing404. No wildcard update.
- DELETE /{id}: exact If-Match; atomic compare/delete,204. Requires no decryption/key availability. Foreign/missing404, stale412; absent/malformed precondition428/400.

No Idempotency-Key middleware for profile requests. The profile UUID in URL is non-PII; names/DOB/contact remain JSON body only. The shared bounded reader rejects known/chunked bodies over16KiB before binding/protection/store, never spills plaintext to disk. Add no-store early for every traveler response including401/403/400/404/412/413/428/503. Return fixed field/code validation details only, no raw values/exception text. Missing keys/ciphertext corruption is safe 503, not an empty list or partially omitted profiles. List authorizes/filters before any decrypt. Request DTO should reject ambiguous unknown owner/slot fields rather than accidentally persist them.

## UI and outcomes

Add owner-only Saved travelers screen linked from Flights with paged list, explicit create/edit/delete and a confirmation for deletion. Names can appear after authenticated decrypt in memory and DOM; do not persist names or other PII to storage, URL, navigation state or logs. Keep the existing visual system. Each booking row offers explicit fill-from-saved selection and explicit save-this-traveler; no automatic read/population or save-on-hold. Selection copies only details, preserves current bookingPassengerId, updates validation and never creates another row or changes count. Profile selection is disabled after a frozen/unknown hold; it cannot alter serialized operation bytes. A late prefill completion must still match owner/epoch, quote revision, slot ID and selection generation, and must not overwrite manual edits made since selection began. Saving a profile is independent of hold and cannot clear an unknown booking attempt.

List state: idle/loading/ready/empty/unavailable/key unavailable. Edit: pristine/dirty/validating/saving/saved/validation/conflict/missing/unknown. Disable duplicate mutation submissions. Every completion checks owner and identityEpoch; logout/new identity/navigation tears down and clears PII; stale completions are ignored. Transition to anonymous/auth error, even without changed owner/epoch, hides and erases decrypted profiles/forms/frozen plaintext and invalidates request generations. Retain only non-PII pending/unknown mutation metadata for an unchanged session if needed; re-auth requires a fresh read, and no late completion from the prior auth generation can restore PII. Keep the required profile ID/version and frozen mutation only in component/session memory while active.

Known validation/precondition errors permit correction;412 prompts an explicit fresh read and review, never automatic overwrite. Timeout, disconnect,5xx, malformed/unexpected2xx or mutation-receipt decoding failure after possible dispatch is unknown: do not retry the mutation automatically. Offer read current profile/list with the known ID. A read shows current state, not a proof of historical execution; after review a new conditional action uses the observed current revision. If absent, explain that no profile is currently visible; do not resurrect the previous ID or declare the lost mutation successful. This local behavior makes no cross-tab/reload exactly-once promise. Profile uncertainty must not stop unrelated manual booking or alter the separate booking barrier.

## Acceptance and tests

1. Valid fictional profile roundtrip; each seven-field bound/enum/DOB error safe and shared with manual booking. Profile identity never replaces slot ID. Future-adult profile can be saved, but selected booking eligibility uses first departure.
2. Actual Data Protection roundtrip across restart and retained keys, wrong owner/id/revision/purpose/tamper fails; ciphertext-only DB row and no payload log/ToString/durable cache. Reuse owned temporary fictional keys only.
3. Anonymous/invalidsub/wrongscope/foreignID protected; all errors no-store; unknown fields/bodycap known+chunked reject before crypto/store. No profile body in URL/browser state.
4. Real EF CI: create collision, concurrent update/update and update/delete yield one appropriate winner, stale revision cannot overwrite, foreignowner cannot mutate, delete without keys works, bounded list order/lookahead and unreadable row failure. Source Up/Down inspect, no local DB.
5. UI source tests: explicit copy/save, differentdetailsperrow/currentID preserved, copy/booking immutable after profile edit/delete, owner/epoch/auth-generation and quote/slot/selection-generation late completions ignored, intervening manual edits retained,412review/malformed2xx/unknownwrite no automatic resend, manual booking and Aunknown unchanged.
6. Isolated fictional demo: CRUD→copy into group→hold/confirm one snapshot; laterprofile edit/delete independent; fake owner isolation declared demo-only; mobile/keyboard/errors/response-loss/no-storage. Real JWT/EF/crypto are tested in their actual layers, not claimed by fake browser auth.
7. Mandatory existing CI, independent review, all docs/source committed, PR/dev green checks including dependentE2E, merge/post-merge/dev synchronization/own cleanup. No CI/CD changes, paid evals, real provider/payment/Anthropic, localHost/AppHost/schemaapply/deploy.

## Delivery sequence

1. Freeze profile ID/details/receipt/conditional API and sharedvalidation boundary; add pure validation+cryptoRED/GREEN with explicitpurpose and keyfailure.
2. Add EF repository/entity/source migration/offlineSQL and CI concurrency/owner/ciphertext tests; wire smallApplicationservice to ports, noevents or durablePII.
3. Add endpoints/auth/no-store/bodybound/typederrors/OpenAPI+no-database HTTP+strictTSdecoders; test source migration inventory updates (lessonfromM2.3b).
4. Add existing-style profileUI and explicitslotcopy/save with auth/epoch/unknown guards; unit+fictionalNode/E2E desktop360px, no storage/logPII.
5. Integrated meaningful safe checks, independentwhole-change review/fix, commit/push/PRgreenCI/merge/synccleanup; then M2.5. SharedContracts/EFsnapshot/UI files have one owner; no simultaneous edits from M2.5 implementation.

Outside: profile sharing/search/dedup/auto-save, useridentity domain, passports/loyalty, childrenbooking, background sync, audit-event store, irreversible booking edits, automatic retry/recovery engine, all M3/OpenSpec/refunds/ancillaries/SSE/Support.

Self-review and independent source review resolved four gaps: late-prefill quote/slot/edit generations, transient auth error PII clearing, malformed-success unknown outcome, and owned412/foreign404 plus owner lookup before encryption. No extra recovery mechanism was added.

Detailed [implementation plan](../plans/2026-10-03-flights-m24-saved-travelers.md). The separate M2.5 itinerary/time-zone research remains a later design; it is not bundled into this profile implementation.
