# Spec Delta

## Purpose

Allow a private Trips owner to attach existing owned flight orders to a saved trip and inspect minimal Flights observations, while preserving independent planning, order management, privacy and explicit mutation boundaries.

## ADDED Requirements

### Requirement: Private access to trip flight links
The system SHALL derive ownership from the trusted authenticated principal for every link, candidate and status request. Foreign and absent trips SHALL have indistinguishable not-found responses before any order lookup. Client-supplied ownership SHALL NOT grant access, and link access SHALL NOT grant booking or operator permissions.

#### Scenario: Foreign trip
- **WHEN** an authenticated owner requests links, candidates, status or a mutation for another owner's trip
- **THEN** the response is the same 404 as an absent trip, without disclosing order IDs or querying Flights.

#### Scenario: Invalid identity or injected owner
- **WHEN** a principal is anonymous or has invalid/conflicting trusted ownership claims, or a request supplies an undeclared owner field
- **THEN** the operation is rejected before private access or mutation; valid access never uses a browser-supplied owner identifier.

### Requirement: Bounded whole order associations
A saved trip SHALL support zero to four distinct complete flight-order associations in explicit addition order. An association SHALL use the internal order ID and server-recorded linking time. A full 1–4-leg order SHALL remain one association. The same owned order SHALL be associable with multiple owned trips without exclusivity or implied booking.

#### Scenario: Separate outward and return orders
- **WHEN** the owner explicitly attaches two different owned orders to a saved trip
- **THEN** both persist in addition order and each is shown as a complete order.

#### Scenario: Duplicate or fifth order
- **WHEN** an already-associated ID is attached with the current collection precondition, or a fifth distinct order is attempted
- **THEN** the duplicate is a checked no-op without another source read, and the fifth is a typed 409 with the previous collection unchanged.

#### Scenario: Independent trips and history
- **WHEN** the owner attaches a previously linked or cancelled owned order to another owned trip
- **THEN** that trip receives its own association without removing the other association or claiming that the order is active.

### Requirement: Authoritative owner checked admission
Before adding a new association, the system SHALL verify an existing materialized order and its owner against authoritative Flights state using a positive source version and observation time. A projected list item or user text SHALL NOT establish ownership. Invalid or misbound proof SHALL fail closed. This read SHALL NOT mutate, reconcile, purchase, pay, cancel or refund an order.

#### Scenario: Stale candidate or crafted ID
- **WHEN** a candidate is selected or an order ID is submitted directly
- **THEN** admission independently rechecks source ownership; absent, foreign and quote-only orders return a concealed 404 without saving a link.

#### Scenario: Source unavailable or state changes later
- **WHEN** ownership cannot be verified, or a verified order changes state after the admission read
- **THEN** the unavailable admission saves nothing, while a later state change is discovered by a separate observation rather than invalidating an otherwise valid association or implying an atomic cross-module snapshot.

### Requirement: Explicit bounded order discovery
The owner SHALL explicitly open a picker of their own projected orders, with pages of at most 20 and a bounded offset from 0 to 10000. Candidate data SHALL be labelled as projected and SHALL NOT prove current state or complete inventory. Date, destination and party differences SHALL NOT silently hide owned orders or certify suitability for the plan.

#### Scenario: Recent hold and changing pages
- **WHEN** a recent hold is not projected yet or new orders shift later offset pages
- **THEN** the UI offers explicit refresh, handles repeated IDs, and never claims a complete snapshot, missing order proof or reason to repeat booking.

#### Scenario: Empty damaged or failed page
- **WHEN** the list is empty, an owned candidate has unusable route data, or listing fails
- **THEN** only the empty list is successful empty; the damaged route is explicitly unavailable without invented legs, and listing failure is an unavailable outcome.

### Requirement: Independent collection and plan revisions
Flight associations SHALL have their own exact monotonic collection revision and strong ETag, independent of the existing plan revision. Link changes SHALL NOT change plan content, revision, timestamps or list ordering. Existing plan writes SHALL preserve associations. An untouched collection SHALL read as empty revision zero without rewriting a historical plan.

#### Scenario: Old protected plan and ordinary editing
- **WHEN** a historical plan is opened, an association is changed, or the calendar is subsequently saved
- **THEN** the old plan representation remains compatible, link changes leave its revision unchanged, and ordinary calendar changes preserve the saved associations.

#### Scenario: Concurrent calendar and link editing
- **WHEN** two editors independently save a valid calendar edit and a valid link edit
- **THEN** neither overwrites the other's resource and each response carries only its resource's authoritative precondition.

### Requirement: Atomic conditional collection mutations
A client SHALL add or remove one association with `PATCH /api/trips/{tripId}/flight-links`, using the exact strong ETag returned by GET of that collection in `If-Match`. Each effective change SHALL atomically advance the collection revision once. Checked no-ops SHALL preserve it. A previously written empty collection SHALL retain its revision until trip deletion. Missing, malformed (including plan tags), and stale preconditions SHALL return 428, 400 and 412 respectively.

#### Scenario: One operation on the collection
- **WHEN** the owner sends a JSON object with exactly `op` (`add` or `remove`) and `orderId` (a nonempty GUID) to the same URI used to read the collection, with its current ETag
- **THEN** a successful PATCH returns 200 with the complete saved collection and its matching ETag, including for a checked no-op.

#### Scenario: First insert race
- **WHEN** two different additions use the same empty revision zero
- **THEN** at most one commits and the other receives 412 without a lost association.

#### Scenario: Remove last then stale replay
- **WHEN** the last link is removed and an old add request with revision zero arrives
- **THEN** the collection remains empty at its newer revision and the stale request cannot recreate an association.

#### Scenario: Cross resource precondition and no-op race
- **WHEN** a plan ETag is used for a link mutation, a link ETag is used for a plan mutation, or a supposedly no-op mutation races with a newer collection change
- **THEN** cross-resource tags are rejected and a stale no-op returns 412 rather than successful historical state.

### Requirement: Explicit uncertainty after link writes
Clients SHALL NOT automatically repeat an add or remove after timeout, cancellation, lost transport or malformed success. They SHALL retain a visible uncertain-write barrier, reread the saved collection and require a new explicit action before another mutation. A reread SHALL NOT be presented as execution proof of the lost command.

#### Scenario: Lost add or removal reply
- **WHEN** a link mutation may have committed but its response is unavailable
- **THEN** no second mutation is sent automatically, the UI explains uncertainty, and a metadata reread establishes only the current collection and ETag.

#### Scenario: Double click
- **WHEN** the owner activates the same pending mutation twice
- **THEN** at most one request is dispatched and later explicit recovery remains available.

### Requirement: Minimal complete order observations
For each associated order the system SHALL display only owner-checked order state, source version/time, passenger count and up to four complete leg summaries in original order. Leg endpoints and original offset timestamps SHALL be preserved. Unusable route facts SHALL remain unknown. No IANA zone, calendar time, passenger detail, ticket, supplier reference or financial fact SHALL be invented or imported.

#### Scenario: Multi leg and historical timestamps
- **WHEN** an owned open-jaw or four-leg order is observed, including a historical order without timezone metadata
- **THEN** every leg appears with original airport/date/offset meaning, one order remains one association, and an IANA timezone is not inferred from its offset.

#### Scenario: Invalid or oversized route
- **WHEN** the saved order has absent, invalid or out-of-bound itinerary data
- **THEN** its route is explicitly unavailable rather than truncated, repaired or displayed as a successful empty itinerary.

### Requirement: Honest status and source freshness
Order observations SHALL describe stored Flights facts at a recorded source version/time, not live supplier checks or aircraft movement. Unresolved creation, confirmation, cancellation/report barriers, creation differences blocking confirmation, and expired or unknown hold expiry SHALL expose review-required uncertainty. Closed/refunded labels SHALL NOT prove customer bank credit or remove an association automatically.

#### Scenario: Cancellation or unresolved operation
- **WHEN** a later source observation reports cancellation or an unresolved order operation
- **THEN** the card updates to the observed closed or review-required state, preserves the association, and does not retain an unqualified active label.

#### Scenario: Refund label
- **WHEN** Flights reports its historical Refunded state
- **THEN** the card identifies that as a Flights order status without asserting that money reached the customer's bank.

#### Scenario: Materialized order with creation differences
- **WHEN** a held order has been created with differences that block confirmation, even though its creation outcome is no longer unresolved
- **THEN** its card is review-required and never an unqualified active/ready order; after accepted cancellation, the original creation difference alone does not keep a permanent review warning.

#### Scenario: Independent customer refund review
- **WHEN** an already closed order has only a standalone customer-refund manual-review flag and no unresolved booking creation/confirmation/cancellation or supplier-report barrier
- **THEN** that refund flag alone does not set this card's review-required state; Trips imports no refund lifecycle and directs the owner to the existing order page for details.

### Requirement: Independent saved links and source failure
Ordinary plan operations, saved-link reads and unlinking SHALL NOT require Flights availability. Source observations SHALL be separate from saved metadata and bounded to the saved IDs. Per-order unavailable and concealed not-found results SHALL remain distinct from an empty saved collection. Source failure SHALL NOT remove saved links or silently replace current facts with fictional data.

#### Scenario: Source outage while editing or removing
- **WHEN** Flights is unavailable
- **THEN** the owner can still read and edit the ordinary plan, read the saved collection and remove an association; adding requires a successful ownership read.

#### Scenario: Partial observation failure
- **WHEN** one associated order cannot be observed while another succeeds
- **THEN** the successful observation remains visible and the failed item has an explicit unavailable state with the collection unchanged; retained earlier facts are labelled stale.

#### Scenario: Source no longer proves access
- **WHEN** a fresh observation cannot find or prove ownership of an associated order
- **THEN** the card clears previous route/status facts, shows a concealed not-found state and allows removing the saved association without disclosing foreign information.

### Requirement: Association lifecycle follows the trip
Removing an association SHALL affect only that trip's collection. Deleting an owned trip SHALL atomically remove its collection without mutating any flight order or another trip. Concurrent link creation SHALL NOT recreate a deleted trip or leave an orphan association. Ordinary trip deletion SHALL retain this behavior even when the link feature is disabled.

#### Scenario: Unlink or delete trip
- **WHEN** the owner removes one association or deletes the containing trip with the appropriate current precondition
- **THEN** the flight order and its associations with other trips remain intact and no cancellation, refund or supplier action is dispatched.

#### Scenario: Delete wins against attach
- **WHEN** trip deletion wins a race against link creation
- **THEN** link creation cannot resurrect the trip, no orphan private link remains, and subsequent reads return concealed 404.

### Requirement: Explicit accessible association UI
The UI SHALL keep link actions outside the calendar save form and require explicit picker opening, attaching, unlinking and status refresh. Actions SHALL preserve dirty calendar data and require a clean saved foundation without a pending or uncertain plan operation. Linking SHALL NOT alter trip dates, city, adults, blocks or estimates. Controls and outcome messages SHALL support keyboard use and focus recovery.

#### Scenario: Dirty calendar or Enter in picker
- **WHEN** the calendar has unsaved edits or Enter is used in the independent picker
- **THEN** edits are retained, no implicit calendar save occurs, and link mutation waits until the ordinary plan state is resolved.

#### Scenario: Different travel dates and order navigation
- **WHEN** an owned order differs from the plan's dates, destination or party count, or its details are opened
- **THEN** the mismatch is not silently corrected, the user retains responsibility for relevance, and navigation uses the existing validated relative order route with its ordinary access rules.

#### Scenario: Manual refresh after another tab removes a link
- **WHEN** the owner clicks refresh while tab A holds revision 3 and tab B has saved revision 4
- **THEN** tab A first reads the collection, applies revision 4 and clears removed cards. It requests status once for a nonempty collection and skips status for an empty collection.

#### Scenario: A second change during refresh
- **WHEN** status returns a different collection revision after the metadata read in the same refresh
- **THEN** the client discards the mismatched observations, tells the owner that the collection changed and offers an explicit refresh. One click issues at most one metadata GET, one status GET and zero mutations.

### Requirement: Existing trusted order authentication boundary
Order navigation SHALL preserve the existing Flights page authentication and scope checks. The validated internal order ID SHALL be permitted only in its existing same-origin order route and the corresponding redirect URI to the configured trusted identity provider. Trips SHALL NOT add trip/association data, tokens, arbitrary return URLs or booking grants to that flow.

#### Scenario: Fresh Flights login
- **WHEN** the owner opens an associated order and Flights requires login or its existing booking scope
- **THEN** the existing trusted login flow handles access; the order GUID can appear in its validated redirect URI, while TripId, plan content and the association list are absent.

#### Scenario: Insufficient Flights access
- **WHEN** the existing Flights page denies access or login is cancelled
- **THEN** Trips has not granted additional permissions, altered the order or disclosed a token through navigation.

### Requirement: Isolated private browser state
Link and candidate state SHALL stay in memory, scoped to the authenticated owner, opened trip and request generation. Observation replies SHALL also match the current collection revision and SHALL NOT regress a greater accepted source version. Logout, owner/plan change, deletion or navigation SHALL clear private state and pending work; stale responses SHALL NOT restore it.

#### Scenario: Late source or mutation callback
- **WHEN** a reply arrives after logout, opening another trip, collection change or deletion
- **THEN** it cannot expose prior-owner facts, add removed links, reopen an old picker or regress the current UI.

#### Scenario: Older observation
- **WHEN** an older request returns a lower source version for the same order and collection
- **THEN** it does not replace a newer accepted observation or remove the explicit stale/unavailable notice from a later failed refresh.

### Requirement: Protected references and bounded diagnostics
Saved association payloads SHALL be protected at rest and bound to owner, trip, collection revision and format version. Private responses/errors SHALL be no-store. New custom logs, traces, error text and metrics SHALL exclude private link payloads and order facts. Private link data SHALL NOT enter browser persistence or SSR transfer state. Missing protection or damaged ciphertext SHALL fail closed.

#### Scenario: Ciphertext moved or corrupted
- **WHEN** a protected collection is copied across owner, trip or revision, or its ciphertext is damaged
- **THEN** reading/mutating it is unavailable without plaintext disclosure or a fabricated empty collection; owner-authorized trip deletion remains possible without decryption.

#### Scenario: Inspect persistence and diagnostics
- **WHEN** a fictional association is saved, read or rejected
- **THEN** stored payloads are protected, no order route/passenger/financial facts are copied into Trips storage, and custom diagnostics, browser storage and SSR contain no private payload.

### Requirement: Relationship safe host telemetry
Host HTTP logs, exported traces and metric labels for link routes SHALL protect the TripId in the path, the orderId and operation payload in the PATCH body, query data and tokens. Safe route templates or narrowly scoped instrumentation exclusion SHALL preserve other Host diagnostics and existing module privacy policies. Protection SHALL apply before routing and to failed or disabled requests.

#### Scenario: Actual exporter boundary
- **WHEN** an actual Host handles successful, malformed, unauthorized, missing, timed-out or disabled requests with a sentinel TripId in the path and orderId in the PATCH body
- **THEN** captured spans, structured and formatted logs, scopes and metric labels exclude raw identifiers, PATCH payloads and tokens, including events before endpoint execution.

#### Scenario: Existing module redaction
- **WHEN** link privacy contributions run alongside existing Rail or other configured redaction
- **THEN** those policies remain effective without globally disabling unrelated Host observability.

### Requirement: Strict bounded link protocol
New requests SHALL use strict shapes, bounded bodies/query values, exact lossless IDs/revisions and one propagated five-second application budget. Results SHALL be bounded: 2 KiB availability, 16 KiB metadata, 32 KiB status and 64 KiB candidate pages. Invalid/unknown/duplicate fields SHALL fail explicitly; no truncation, hidden repair, automatic retries or unbounded background reads SHALL occur.

#### Scenario: Oversized or malformed request
- **WHEN** a PATCH exceeds 1 KiB, or a bounded request contains an array of operations, an unknown `op`, a missing, extra or duplicate field, an invalid orderId, path, query or malformed precondition
- **THEN** the operation returns a typed 413 or 400 before source work and no mutation is committed.

#### Scenario: Budget exhaustion
- **WHEN** the operation budget expires during storage or source work
- **THEN** unfinished work is cancelled, the response is a typed 504 or a bounded partial status result, no read/mutation is automatically repeated, and a timed-out mutation is treated as potentially committed.

### Requirement: Independently opt in flight associations
The capability SHALL be disabled by default and require enabled Trips. With Trips enabled, an authenticated availability read SHALL report public configuration without resolving private dependencies. Other disabled link routes SHALL return 404 before auth and link/source dependencies. Enabling the feature SHALL NOT enable AI, itinerary generation or providers, nor waive schema/key/target gates.

#### Scenario: Disabled feature and dependency spies
- **WHEN** Trips or flight associations are disabled and feature routes are requested
- **THEN** the effective disabled response is produced without resolving link storage, protection or the Flights port, while existing ordinary Trips behavior is preserved.

#### Scenario: Enabled but unready
- **WHEN** the feature is enabled with missing required schema or protection
- **THEN** readiness and requests fail closed without request-time provisioning or a successful empty collection.

#### Scenario: Applied migration with a missing table or column
- **WHEN** Trips is enabled and migration history is current but the link table or one of its mandatory columns is absent
- **THEN** a read-only schema probe reports Unhealthy even if no links have been created. With the feature enabled a private metadata read returns unavailable. With the correct schema and keys the same existing trip can return a genuine empty collection.

### Requirement: Reproducible compatibility and persistence acceptance
Source acceptance SHALL cover UI behavior with intercepted Playwright requests and the real path through proxy, authentication, Host and database in the owned local-stack profile. Evidence SHALL count every collection PATCH. Both city profiles, M1 snapshot and module-boundary checks SHALL continue to pass. The restart scenario SHALL use fictional data and zero external requests.

#### Scenario: Browser demo request accounting
- **WHEN** Playwright intercepts the link API and the owner double-clicks or loses the reply after a committed link change
- **THEN** the demo records one PATCH for that action, shows recovery and permits a metadata reread with zero further mutations.

#### Scenario: Development Host prepares Flights and messaging
- **WHEN** the flight-links profile starts its Development Host on an owned database prepared by the existing Trips schema helper
- **THEN** the ordinary Host initializers prepare Flights EF, Marten and Wolverine. Only after Host readiness does the fixture seed source events and await normal projection visibility in the picker.

#### Scenario: Real service restart
- **WHEN** two fictional owners use real login and the real picker through the proxy to PATCH their API-created trips, then owned Host/Keycloak/PostgreSQL services restart with retained data and keys
- **THEN** the write ledger counts each PATCH, the same authorized associations and revisions survive restart, foreign access stays concealed, a later source event updates the card without relinking, and owned cleanup is verifiable.

#### Scenario: Compatibility and failed evidence
- **WHEN** old plans and both existing city profiles run alongside the new feature, or a required acceptance case fails
- **THEN** old canonical responses, saved content and existing assertions remain preserved; a failure remains failed and cannot be waived by mocked browser results or by enabling the feature.
