## Purpose

Provide a private, bounded workspace for structured trip wishes and an editable day plan, preserving a reliable foundation for later system-proposed itineraries without claiming generation or booking in M1.

## ADDED Requirements

### Requirement: Structured single destination brief
The system SHALL accept one selected destination, optional selected origin, inclusive dates spanning 1–14 days, 1–9 adults, one supported currency, optional budget, supported interests and pace. Selection SHALL use the published local city catalogue. It SHALL NOT interpret free text as a confirmed brief or promise supplier availability.

#### Scenario: Create from explicit parameters
- **WHEN** the owner submits a valid selected city, dates and planning preferences
- **THEN** the system creates a private revision-1 draft with one empty day per date and no generated activities or booked services.

#### Scenario: Reject invalid scope
- **WHEN** dates are reversed, the range exceeds14days, adults are out of bounds, an enum is unsupported or a selected city is unknown
- **THEN** the request is rejected without storing or partly correcting the brief.

#### Scenario: Historical plan
- **WHEN** the owner saves a valid past date range
- **THEN** date validation permits it without implying present availability of any service.

### Requirement: Honest planning status
M1 SHALL identify empty-day creation and user blocks as planning content. It SHALL NOT claim a generated itinerary, verified feasibility, booking, payment or complete trip price. M1 SHALL make no model, supplier, geocoding, weather, POI or purchase call when reading or editing a plan.

#### Scenario: Empty day template
- **WHEN** the user creates a plan with complete preferences
- **THEN** the UI identifies it as a calendar foundation requiring content rather than an automatically proposed route.

#### Scenario: User writes a booking claim
- **WHEN** the owner puts the words booked or paid into a block title
- **THEN** the text remains user content and does not establish a trusted booking/payment state or trigger an external operation.

### Requirement: Owner private workspace
Every read, list and mutation SHALL require authentication and derive ownership only from the trusted principal. Lists SHALL expose only that owner. Unknown and foreign identifiers SHALL return the same not-found response without private content. Client-supplied ownership SHALL NOT establish access.

#### Scenario: Foreign detail and mutation
- **WHEN** an authenticated user requests or changes another owner's TripId
- **THEN** detail, PUT and DELETE return404 without disclosing or decrypting that owner's plan.

#### Scenario: Missing authentication
- **WHEN** an anonymous client calls a Trips endpoint
- **THEN** it receives401 and no plan or catalogue payload.

#### Scenario: Injected owner field
- **WHEN** a request includes an ownerId field not declared in its contract
- **THEN** validation rejects it and never uses it to select a different owner.

#### Scenario: Ambiguous authenticated identity
- **WHEN** an authenticated principal lacks one valid normalized owner subject or has conflicting trusted ownership claims
- **THEN** the request receives403 before private payload access rather than creating an empty or alternate owner identity.

### Requirement: Bounded editable day blocks
The owner SHALL be able to add, edit, reorder, move and remove blocks with stable unique IDs, plain text, optional same-day local time pair and optional user estimate. Plans SHALL allow at most8blocks/day and112total. Invalid time pairs, duplicate IDs or blocks outside the date range SHALL reject the whole mutation.

#### Scenario: Move without losing identity
- **WHEN** the owner moves a block to another existing day below its block cap
- **THEN** its ID, text and estimate are preserved and it exists exactly once in the saved plan.

#### Scenario: Invalid block set
- **WHEN** a replacement includes a duplicate ID, a ninth block in a day, an out-of-range day or an end time not later than its start
- **THEN** no portion is stored and the previous revision remains authoritative.

#### Scenario: Overlapping intentions
- **WHEN** two valid timed blocks overlap
- **THEN** the plan can be saved with an explicit overlap warning and does not claim their compatibility or practical feasibility.

### Requirement: Non destructive brief changes
Date changes SHALL preserve blocks on retained dates and add empty new days. A change that drops a nonempty day SHALL fail atomically until the owner moves or removes its blocks. Destination or currency changes with any existing block SHALL fail rather than reinterpreting it; other valid preference changes SHALL preserve blocks.

#### Scenario: Shorten with populated removed date
- **WHEN** the owner shortens a range that would remove a populated day
- **THEN** the request returns a typed conflict and the original brief and every block remain unchanged.

#### Scenario: Extend and preserve
- **WHEN** the owner extends a valid range without dropping a populated day
- **THEN** retained dates keep their blocks and IDs and added dates are empty.

#### Scenario: Change city or currency
- **WHEN** the owner changes destination or currency on a plan with blocks
- **THEN** the change fails with a typed conflict instead of moving local times or converting amounts.

#### Scenario: Adjust interests
- **WHEN** the owner changes valid interests, pace, adults or budget
- **THEN** existing blocks remain intact and no hidden replanning occurs.

### Requirement: Exact optional planning money
Budget and block estimates SHALL use exact nonnegative decimal strings in RUB/EUR/USD, bounded by999999999.99per value. Currency SHALL match the brief. Known estimates SHALL be summed exactly with unknown count separate; absent amounts SHALL NOT become zero, supplier prices or proof that the whole trip fits the budget.

#### Scenario: Partial estimates
- **WHEN** one block has an estimate and another has none
- **THEN** the UI shows the known user-estimate subtotal and an unknown count without calling the subtotal the whole trip price.

#### Scenario: Exact decimal and invalid money
- **WHEN** valid decimal estimates are summed, or input contains negative, exponential, excess-precision, out-of-bound or different-currency values
- **THEN** valid sums retain exact minor units and invalid values reject the entire mutation without float conversion or FX.

#### Scenario: Budget warning
- **WHEN** known estimates exceed the stated budget
- **THEN** the saved plan exposes a budget warning; a lower known subtotal never proves a complete within-budget result.

### Requirement: Atomic revision checked changes
Each effective update SHALL atomically replace one owner's validated plan and advance its positive revision once. PUT and DELETE SHALL require one strong exact revision precondition. Stale updates SHALL NOT overwrite later changes; no-op saves SHALL preserve revision. Reads SHALL expose the authoritative saved snapshot.

#### Scenario: Competing editors
- **WHEN** two edits use the same saved revision and the first changes the plan
- **THEN** only one commits and the other receives412 while its local edits remain available for explicit reconciliation.

#### Scenario: Missing or malformed precondition
- **WHEN** PUT or DELETE omits If-Match or supplies a malformed, wildcard, weak or multi-value tag
- **THEN** missing receives428 and malformed receives400 with no mutation.

#### Scenario: No op save
- **WHEN** a valid exact-revision replacement makes no effective change
- **THEN** the saved revision and update timestamp remain unchanged.

#### Scenario: Forged server fields
- **WHEN** POST supplies more than brief/title or PUT attempts to write ownership, revision, timestamps or calculated fields in its body
- **THEN** strict write-contract validation rejects the request and the server never adopts those fields from the client.

#### Scenario: Delete wins against edit
- **WHEN** deletion commits before an update can commit
- **THEN** the update does not recreate the absent plan and subsequent owner reads return404.

### Requirement: Bounded reads and owner capacity
The system SHALL cap active plans at100per owner, including concurrent creates. Lists SHALL use pages of at most20 and bounded owner-scoped cursors with deterministic ordering. A malformed cursor or unavailable storage SHALL NOT be represented as a successful empty list.

#### Scenario: Capacity race
- **WHEN** concurrent creates compete for the last permitted slot
- **THEN** at most one commits and the other receives a typed capacity conflict.

#### Scenario: List continuity
- **WHEN** the owner lists plans over multiple pages while a plan is updated
- **THEN** every returned item is owner-authorized, the UI handles repeated IDs and refresh is available without a snapshot-isolation claim.

#### Scenario: Empty versus unavailable
- **WHEN** no plans exist, or storage/protection is unavailable
- **THEN** only the former returns a successful empty page and the latter returns a typed unavailable outcome.

### Requirement: Explicit handling of uncertain writes
Clients SHALL NOT automatically repeat POST, PUT or DELETE after timeout, cancellation or lost transport. They SHALL retain safe local edits and provide authoritative rereads before an explicit next action. A reread SHALL NOT be presented as proof that a particular lost command committed; M1 SHALL NOT promise exactly-once creation.

#### Scenario: Lost creation response
- **WHEN** POST may have committed but its response is lost
- **THEN** the UI shows uncertainty and offers the owner's current list before another explicit creation, with no automatic second POST.

#### Scenario: Lost save response
- **WHEN** PUT may have committed but its response is lost
- **THEN** the local buffer survives and the user can reread and explicitly reconcile against the current revision without an automatic PUT retry.

#### Scenario: Double click
- **WHEN** a mutation submit is already pending
- **THEN** another click does not dispatch an additional mutation.

### Requirement: Payload validation and private rendering
Requests and complete detail/list bodies SHALL be bounded to128KiB and lists to20summary items. Before commit the system SHALL ensure the full future canonical detail fits its limit, including derived/server fields and revision reserve. Titles SHALL be1–100Unicode scalars, notes at most500; unknown fields/controls SHALL fail. Money/revision/date SHALL round-trip losslessly. User strings SHALL render as text.

#### Scenario: Oversized or unrecognized payload
- **WHEN** a body exceeds its byte limit or contains unknown properties or invalid text
- **THEN** validation refuses it without committing or logging the original body.

#### Scenario: Hostile display text
- **WHEN** a saved title contains markup-looking text
- **THEN** the browser displays literal text without HTML or URL execution.

#### Scenario: Near limit Unicode round trip
- **WHEN** a bounded request with multibyte and escaped characters would produce an oversized full canonical detail
- **THEN** the whole mutation is rejected before commit without advancing revision; an accepted near-limit plan remains readable and unchanged through GET/reload.

#### Scenario: Bounded summary page
- **WHEN** twenty plans contain their maximum accepted private content
- **THEN** the list returns only bounded summaries without blocks/notes/interests/budget and the complete serialized page remains within128KiB.

### Requirement: Private storage and diagnostics
Saved private payloads SHALL be protected at rest and bound to owner, plan and revision. Private responses and errors SHALL be no-store. Logs, traces and errors SHALL exclude brief/title/note/body; plaintext plans SHALL NOT be persisted in browser storage or SSR HTML. Missing protection SHALL fail closed.

#### Scenario: Foreign or damaged ciphertext
- **WHEN** ciphertext is moved across plan/owner/revision or fails protection validation
- **THEN** the affected read is unavailable without plaintext disclosure or a fabricated empty plan.

#### Scenario: Observe a failure
- **WHEN** validation, persistence or protection fails
- **THEN** emitted diagnostics and RFC7807 responses contain no original private fields and retain no-store policy.

### Requirement: Session and editor isolation
The editor SHALL separate saved and dirty state, warn before navigation discard and clear memory on logout/owner change. Replies SHALL respect owner, opened plan, local edit version and monotonic accepted revision. A save reply SHALL NOT clear later edits; late reads SHALL NOT regress state or resurrect deletion. Conflict, uncertain, unavailable, empty and unauthenticated SHALL stay distinct.

#### Scenario: Owner changes during request
- **WHEN** a response completes after logout or a different owner signs in
- **THEN** it is discarded and no previous-owner plan or dirty buffer appears in the new session.

#### Scenario: Navigate while dirty
- **WHEN** the user navigates away from a plan with unsaved changes
- **THEN** the UI offers to keep editing or explicitly discard, while logout always clears private state.

#### Scenario: Old detail response
- **WHEN** a delayed request for plan A finishes after the editor has opened plan B
- **THEN** A does not replace B or its pending edits.

#### Scenario: Editing during save
- **WHEN** snapshot A is submitted and the owner makes further edits B before its successful reply
- **THEN** A becomes the saved baseline while B remains visible and dirty for an explicit later save; the reply neither replaces B nor marks it saved.

#### Scenario: Old read after save
- **WHEN** a GET for revision1 finishes after the editor has accepted revision2 from a successful save
- **THEN** it is discarded without regressing the saved baseline, revision or local edits.

#### Scenario: Old read after deletion
- **WHEN** an outstanding detail or save reply arrives after confirmed deletion of the opened plan
- **THEN** it cannot reopen the editor, recreate a list entry or restore private content.

### Requirement: Owner deletion
An owner with the current revision SHALL be able to delete a plan atomically. Deleted payloads SHALL cease to be available through Trips reads or lists. This action SHALL NOT delete supplier bookings or imply immediate erasure of existing backups. Reads and failed updates SHALL NOT recreate deleted content.

#### Scenario: Delete own plan
- **WHEN** deletion succeeds with the current revision
- **THEN** the plan disappears from the owner list and detail returns404 while no external cancellation or financial action is dispatched.

#### Scenario: Stale delete
- **WHEN** the owner deletes using a revision superseded by another edit
- **THEN** deletion fails412 and the latest plan remains intact.

### Requirement: Accessible explicit editing
Structured inputs and day/block controls SHALL expose labels, keyboard access, focus recovery and announced validation/status changes. Creation, saving and deletion SHALL be explicit actions. M1 SHALL NOT display a nonfunctional generate button or silently submit planning preferences to an AI service.

#### Scenario: Correct validation by keyboard
- **WHEN** a keyboard user submits invalid dates or a block
- **THEN** the relevant error is announced, linked to its control and reachable for correction without losing valid edits.

#### Scenario: Save and reload
- **WHEN** the owner explicitly saves valid changes and reloads the plan
- **THEN** the same accepted brief, blocks, money and revision appear without model or supplier requests.

### Requirement: Isolated opt in capability
Trips SHALL remain unavailable until explicitly enabled with ready schema and protection. Disabled operation SHALL NOT provision Trips dependencies. M1 SHALL preserve other module contracts and SHALL NOT require Rail completion, shared internal DTOs or an AI platform rollout.

#### Scenario: Disabled capability
- **WHEN** Trips is disabled
- **THEN** no Trips private endpoint or navigation action is offered and Trips causes no database/key/model/provider work.

#### Scenario: Enabled but unready
- **WHEN** Trips is enabled but required schema or protection is missing
- **THEN** it fails readiness/requests with typed unavailable status rather than creating resources at ordinary startup or exposing partial plans.

#### Scenario: Existing module regression
- **WHEN** Trips is composed alongside accepted Flights, Hotels, Support and Rail implementations
- **THEN** their routes, authorization, provider gates and module boundaries keep their existing contracts.
