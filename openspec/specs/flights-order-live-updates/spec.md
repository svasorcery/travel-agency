# flights-order-live-updates Specification

## Purpose

Keep an authenticated owner's open flight order page current through best-effort change signals and authoritative reads, without repeating business operations or inventing financial outcomes.

## Requirements

### Requirement: Authoritative read after a committed change
The page SHALL use a change signal only to request owner-authorized order, creation, cancellation and customer-refund reads. It SHALL display business facts only from those reads or existing proven command evidence. A received signal SHALL NOT establish a status, refund amount, supplier settlement, customer dispatch or bank credit.

#### Scenario: Ticket webhook while page remains open
- **WHEN** a fictional ticket webhook commits and its projection becomes readable
- **THEN** the subscribed owner page reads the authoritative resources and shows the returned tickets without reload or another booking command.

#### Scenario: Refund notification without a successful GET
- **WHEN** a signal arrives but the refund read fails
- **THEN** no new monetary success is shown and any previously proven refund result remains visible with a separate refresh error.

### Requirement: Notification follows committed evidence
The system SHALL signal committed cancellation/report/customer-refund changes for materialized or newly held orders through the existing notification boundary. Failed or no-op writes SHALL NOT publish a new change. Delivery SHALL wait for the required owned projection version; later valid state can replace intermediate signals.

#### Scenario: Conflict or transaction rollback
- **WHEN** a report or refund append loses an expected-version race or its transaction rolls back
- **THEN** no signal from the failed batch is delivered.

#### Scenario: Projection is behind
- **WHEN** a notification reaches its consumer before its required projection checkpoint
- **THEN** no signal is published yet, existing bounded notification recovery applies, and source GET remains independent of that lag.

#### Scenario: Pre-hold creation review has no order projection
- **WHEN** a source-only creation review concludes no order was created or remains inconclusive before any hold
- **THEN** its source state stays discoverable by GET fallback without enqueuing a projection-dependent notification that can never become ready; an actually held order with EF lag still receives normal bounded notification recovery.

#### Scenario: Same-version signals coalesce
- **WHEN** status and generic change notifications have the same checkpoint
- **THEN** receipt of either is sufficient to refresh every relevant panel; the page does not require both.

### Requirement: Truthful report and refund display
New supplier cancellation reports and later report/refund resolutions SHALL become observable on an already open page, including one showing a terminal booking status. Existing separation of supplier entitlement, settlement, customer dispatch and bank credit SHALL remain. Observation SHALL NOT release unresolved financial barriers.

#### Scenario: Airline report without money evidence
- **WHEN** a fictional cancellation report arrives for a Ticketed order
- **THEN** the page shows the retained unverified report/manual state after GET, without inferring a refund from the ticket total or starting payout.

#### Scenario: Late resolution after the old polling window
- **WHEN** a report review or fictional refund receipt commits after the workflow polling window ended
- **THEN** the open page can discover it through a signal or fallback read and applies only the corresponding authoritative outcome.

### Requirement: Private authenticated subscription
Subscription SHALL require the existing authenticated order owner and conceal foreign/missing orders. Tokens SHALL travel in authorization headers only. PII and internal money-operation references SHALL NOT enter event payloads, URLs, history, storage or logs. The existing booking resource identity remains permitted in its route.

#### Scenario: Foreign owner subscribes
- **WHEN** another owner requests the order stream
- **THEN** no subscription or order data is disclosed and the existing non-disclosing response is retained.

#### Scenario: Browser connection and redirect
- **WHEN** the browser opens or reconnects the stream
- **THEN** its URL has no token/query secret, credentials are not persisted, and an unexpected redirect cannot forward the token through the client transport.

### Requirement: Identity and page lifetime isolation
Logout, authorization loss, identity replacement, navigation and page destruction SHALL close the stream, cancel scheduled observation and invalidate pending callbacks. Token refresh SHALL reuse the existing auth contract and rotate the connection without carrying state across an identity epoch.

#### Scenario: Token await completes after logout
- **WHEN** an old token request or stream callback completes after logout or a same-user new epoch
- **THEN** it cannot open a connection, issue a read or reveal/update the old page.

#### Scenario: Auth refresh retains the same page identity
- **WHEN** token refresh reports the same owner and identity epoch again
- **THEN** page observation and accepted watermarks remain in the same session; only an actually changed token rotates its connection.

#### Scenario: Owner changes during GET
- **WHEN** a former owner's GET succeeds after identity replacement
- **THEN** the response is ignored and no prior-owner data or consent is restored.

### Requirement: Recovery from lost signals
Every successful initial connection or reconnection SHALL request current authoritative state. Visible authenticated pages SHALL use periodic read fallback even if no disconnection was observed. Hidden/offline pages SHALL pause observation and refresh on return. There SHALL be no durable replay or multi-node delivery claim.

#### Scenario: Change between initial GET and registration
- **WHEN** a change occurs after the initial GET but before subscription registration
- **THEN** the post-connect GET discovers it without needing replay.

#### Scenario: Silent loss or process restart
- **WHEN** a notification is lost, emitted by another process or emitted with no active connection
- **THEN** later fallback/reconnect GET restores state once reads are available; the client does not repeat a mutation.

#### Scenario: Hidden page returns
- **WHEN** the page becomes visible and online after missing changes
- **THEN** it performs a fresh read and creates at most one current subscription.

#### Scenario: Read completes after observation is paused
- **WHEN** the page becomes hidden or offline while a workflow GET and an explicitly admitted command are pending
- **THEN** automatic GET timers and late read callbacks pause, retained evidence and the admitted command keep their guards, and resume begins a new observation epoch with a fresh coalesced cycle even if the previous catch-up budget expired during the pause, without extending terms or business deadlines.

### Requirement: Bounded coalesced observation
Signals, manual refresh and existing polling SHALL share single-flight per-resource reads and a coalesced follow-up. Reconnection and catch-up SHALL have bounded rates; duplicate signals SHALL NOT extend workflow or recovery deadlines. Slow or malformed streams SHALL not cause unbounded buffering or request storms.

#### Scenario: Signal during an active read
- **WHEN** a newer signal arrives while a GET started before that signal is active
- **THEN** at most one follow-up is queued with the highest target version, and the newer signal is not lost.

#### Scenario: Repeated hint is already satisfied
- **WHEN** a hint repeats a version already confirmed by applicable readers after the earlier GET completed
- **THEN** it starts no new immediate read cycle and normal fallback remains available.

#### Scenario: Pending hint repeats after catch-up exhaustion
- **WHEN** the bounded catch-up window ended without convergence and the same pending hint repeats
- **THEN** it does not restart rapid reads or reset the budget; slower fallback can still converge when authoritative reads recover.

#### Scenario: Endless lag or malformed stream
- **WHEN** reads remain behind or stream parsing repeatedly fails
- **THEN** bounded catch-up ends, prior evidence stays visible, and slower fallback/manual refresh remains available without spinning.

### Requirement: Monotonic versioned snapshots
Current order and creation reads SHALL expose versions bound to their actual snapshots. Every panel SHALL reject lower-version data and preserve existing case/operation revision guards. A signal version is a freshness target, not proof of state. Lagging prerequisite data SHALL NOT enable new actions contrary to newer evidence.

#### Scenario: New result followed by stale EF or cached GET
- **WHEN** version12 is accepted and version11, a temporary404 or a failed read arrives later
- **THEN** version12 facts remain, lag/error is shown separately, and no operation is resent.

#### Scenario: One panel is newer than another
- **WHEN** cancellation/refund source state advances while order/creation reads are behind
- **THEN** newer facts remain visible and actions requiring stale prerequisites stay blocked until authoritative guards can be evaluated safely.

#### Scenario: Creation timeout without a stream append
- **WHEN** an equal-version later creation read reports the existing elapsed-time manual diagnosis
- **THEN** it can reduce permitted actions without fabricating supplier failure; an older observation cannot reverse that diagnosis.

### Requirement: Read failures remain distinct from business outcomes
Transient HTTP failures, timeouts, invalid data and projection absence SHALL NOT erase last proven same-owner results or turn an unknown money outcome into success/failure. Authorization failures SHALL instead revoke access. Independent panel reads SHALL settle independently.

#### Scenario: Order read fails while refund succeeds
- **WHEN** order GET returns500 but refund GET authoritatively returns Sent
- **THEN** the refund panel updates, prior order facts remain with a refresh error, and bank credit remains unproven.

#### Scenario: Missing direct resource versus projection lag
- **WHEN** all owner reads return404 for an unrelated direct resource with no retained evidence
- **THEN** the page stops automatic observation and shows unavailable/manual refresh; a known owned source snapshot with missing EF instead remains visible while synchronization retries are bounded.

#### Scenario: Successful unchanged read after a refresh error
- **WHEN** a current authorized GET succeeds with the same accepted version after an earlier transient failure
- **THEN** it clears that read error and acknowledges freshness without replacing the proven snapshot or weakening revision guards.

#### Scenario: Source booking has never materialized as an order
- **WHEN** current owner source evidence establishes OfferQuoted with a NotCreated or pending creation and no prior materialized order
- **THEN** absent order projection is treated as not applicable, not endless lag; source reads continue, existing barriers stay intact, and proven NotCreated recovery does not wait for a nonexistent EF row.

### Requirement: Observation never authorizes a mutation
Signals, reconnect, reload, manual status refresh and fallback SHALL perform reads only. They SHALL NOT create operations, restore consent, invoke supplier observation commands, confirm/cancel a booking or resend a payout. Existing explicit user actions and server guards SHALL retain their meaning.

#### Scenario: Replayed signal with expired terms
- **WHEN** duplicate or old signals arrive after terms expire or are replaced
- **THEN** no consent is restored and no cancellation confirmation is submitted.

#### Scenario: Unknown payout and repeated reconnects
- **WHEN** refund execution remains Unknown and the browser reconnects repeatedly
- **THEN** it only reads status, preserves the uncertainty and causes no second gateway call.

### Requirement: Additive compatibility
Existing SSE event names, owner routes, historic booking/refund states and monetary contracts SHALL remain readable. Absent legacy version fields SHALL not be treated as proof of freshness or replace versioned evidence. No historical rewrite, schema change or new financial state is required.

#### Scenario: Old response after a versioned response
- **WHEN** a compatible old server response omits version metadata
- **THEN** it cannot overwrite a retained versioned result or clear synchronization barriers.

#### Scenario: Historical unknown projection checkpoint
- **WHEN** a historical EF row has a nonpositive checkpoint sentinel
- **THEN** the API exposes unknown version metadata rather than a negative version that breaks the compatible decoder; no backfill or historical rewrite occurs.

#### Scenario: Historical Refunded with a full party
- **WHEN** an old Refunded order with one to nine adults and up to four legs is read
- **THEN** its route and state remain readable, customer payout stays explicitly unverified, and no amount is multiplied or inferred.

### Requirement: Separate fictional and runtime acceptance
Acceptance SHALL distinguish offline/local fictional tests, persisted mandatory CI including PR E2E, and separately authorized live checks. Demonstrations SHALL use only fictional data and retain production/auth restrictions. Passing parser/demo tests SHALL NOT establish real supplier, payment or deployment acceptance.

#### Scenario: Demonstrated webhook update
- **WHEN** the isolated browser demo receives a scheduled fictional webhook change and updates through GET
- **THEN** evidence records the stream and read behavior plus zero observation-induced writes, without claiming production JWT/HMAC/outbox proof.

#### Scenario: Persisted integration acceptance
- **WHEN** existing CI runs signed fictional webhook, commit/rollback, projection and owner stream tests
- **THEN** its result is recorded separately from the local demo; a later live check still requires explicit authorization.
