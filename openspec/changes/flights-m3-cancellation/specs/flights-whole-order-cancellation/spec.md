## ADDED Requirements

### Requirement: Whole-order eligibility and ownership
The system SHALL allow a new cancellation only for an authenticated owner, an eligible booking, explicit current supplier API capability and no unresolved competing mutation. Eligible statuses are Held, Confirmed and Ticketed, with the historical coordination verification required below. The complete passenger party and all booked legs form one cancellation scope.

#### Scenario: Foreign or ownerless order
- **WHEN** another user, invalid identity or an ownerless history requests terms, consent or operation status
- **THEN** the system rejects access before supplier invocation, disclosure or no-op.

#### Scenario: Whole group and itinerary
- **WHEN** an owned order contains nine adults and four ordered legs
- **THEN** consent and cancellation cover the complete order; no individual passenger or leg cancellation is offered.

### Requirement: Current concrete cancellation terms
The system SHALL present persisted terms identifying the order, cancellation proposal and revision, known nonnegative supplier refund amount, original supplier currency, supported destination, expiry and complete scope. The system SHALL NOT invent missing money or expiry facts. For the accepted D2 boundary, unknown money/expiry and credits/vouchers/mixed settlement require explicit manual handling before confirmation. Financial terms SHALL expose SupplierApi or OperatorVerified provenance independently from outcome resolution provenance, before consent and after confirmed success.

#### Scenario: Mixed or ambiguous original settlement
- **WHEN** a numeric refund includes airline credits, the credit facts are absent/malformed, or original payment composition is not positively established for an original-form destination
- **THEN** the system does not present the total as cash-only and does not confirm automatically; manual/unsupported handling is explicit.

#### Scenario: No monetary refund
- **WHEN** a valid supported proposal has zero refund
- **THEN** the review displays zero explicitly and still requires consent; unknown amount is never displayed as zero.

#### Scenario: Unsupported or missing financial facts
- **WHEN** supplier terms contain an unknown amount/currency/expiry or unsupported settlement
- **THEN** no supplier confirmation is dispatched and the user sees the specific safe manual/unavailable outcome.

### Requirement: Consent bound to an immutable proposal
The system SHALL obtain explicit consent to the exact current terms revision, supplier cancellation identity, financial destination, deadline, scope and notice. Consent SHALL NOT transfer to changed or replacement terms.

#### Scenario: Expired, replaced or changed proposal
- **WHEN** consent or dispatch refers to expired/replaced terms or a meaningful fact changed
- **THEN** no confirm is sent for those terms and fresh review/consent is required.

#### Scenario: User has not accepted
- **WHEN** the user has only viewed a proposal or reloaded the review page
- **THEN** the system performs no automatic consent or supplier confirm.

### Requirement: Durable operation discovery
The system SHALL retain operation identity, accepted terms, progress and authoritative outcome before dispatching external actions. An owner SHALL discover the current operation from the order after lost response, reload, re-authentication or server restart without keeping tokens, retry keys, terms or operation IDs in browser storage/history/URLs.

#### Scenario: Lost prepare or consent response
- **WHEN** a server admission was committed but the client received no response
- **THEN** an authorized read returns the committed operation; the client does not create a replacement mutation automatically.

### Requirement: Single dispatch and stage deduplication
The system SHALL serialize cancellation creation, consent, confirmation and competing confirmation admissions per booking. Exact stage repeats SHALL resolve to the same operation. Conflicting identities/payloads SHALL not dispatch additional effects. A dispatch that may have occurred SHALL never be repeated solely because a lease, response-cache TTL or timeout expired.

#### Scenario: Two tabs or different request keys
- **WHEN** two clients attempt cancellation or one attempts confirm while another cancels
- **THEN** only the winning server admission may reach its external action and the loser reads or receives a safe conflict.

#### Scenario: Interrupted worker after claim
- **WHEN** a worker restarts or delivery repeats after a persisted dispatch claim
- **THEN** the system performs read recovery or manual review, without a second supplier mutation.

### Requirement: Separate conclusive and inconclusive outcomes
The system SHALL distinguish confirmed success, confirmed rejection and unknown. Pending/accepted, generic HTTP error, null confirmation timestamp, missing resource or elapsed wait SHALL NOT establish a terminal outcome. Prior uncertainty SHALL persist across later inconclusive errors.

#### Scenario: Lost confirmation response
- **WHEN** the supplier may have confirmed the accepted cancellation but the response was lost
- **THEN** the operation is unknown until matching affirmative evidence establishes its outcome.

#### Scenario: Later refusal after uncertainty
- **WHEN** a subsequent read/request fails or returns stale/refusal after a possible earlier dispatch
- **THEN** that result does not erase prior uncertainty or authorize a fresh confirm.

### Requirement: Evidence-based recovery
The system SHALL correlate supplier observations to the accepted operation/order and terms. It SHALL preserve evidence provenance. An observed cancelled order without matching operation evidence SHALL be shown as an order fact while the operation/financial outcome remains unknown or manual.

#### Scenario: Matching confirmed cancellation
- **WHEN** a validated authoritative observation confirms the accepted cancellation with matching terms
- **THEN** the operation succeeds and the cancellation fact is durably reconciled without repeating the supplier mutation.

#### Scenario: Lost create response
- **WHEN** proposal creation may have completed but no supplier cancellation identity was saved
- **THEN** discovery does not choose a candidate by amount, time or newest alone; no consent is inherited and ambiguous correlation is explicit.

#### Scenario: Different cancellation or changed terms
- **WHEN** the order was cancelled through a different supplier cancellation or materially different terms
- **THEN** the system preserves the actual order fact and requires provenance review rather than declaring this consent fulfilled.

### Requirement: Local finalization after external success
The system SHALL recover a known supplier success after local save failure or optimistic conflict, preserving the original operation. Booking terminal facts, operation result and derived-state delivery SHALL commit consistently. Duplicate observations SHALL not emit duplicate domain cancellation or sibling notifications.

#### Scenario: External success and failed local commit
- **WHEN** supplier confirmation succeeded but local persistence failed
- **THEN** subsequent recovery records the proven outcome in a fresh local transaction; no provider confirm retry or false rejection occurs.

### Requirement: Projection lag and monotonic user evidence
The system SHALL expose authoritative operation outcome independently of the derived order read model. Older responses and delayed projections SHALL not revert accepted user evidence, and the UI SHALL show synchronization separately from supplier uncertainty.

#### Scenario: Stale order GET after success
- **WHEN** operation success is committed and order GET still returns Held/Confirmed/Ticketed or temporarily 404
- **THEN** the UI retains the authoritative success and displays read-model synchronization without resending cancellation.

### Requirement: Explicit bounded manual recovery
The system SHALL use bounded automatic read recovery and then surface ManualReviewRequired with the retained operation. A separately authorized operator path SHALL record a revision-checked resolution with typed evidence, actor and server-recorded time. Supplier-support/infrastructure attestations SHALL be explicitly labeled OperatorVerified judgments, never represented as machine-verified API receipts. Unknown outcomes SHALL remain blocked when required evidence cannot be established, with no automatic time-based release. It SHALL not accept an owner's status override, timeout-only proof or arbitrary sensitive supplier payload. Unblock after unknown dispatch requires affirmative outcome evidence and assurance that an old worker cannot still send.

#### Scenario: Reads cannot establish outcome
- **WHEN** supplier observations remain missing, inconsistent or unavailable through the read budget
- **THEN** the result remains unknown/manual and competing mutation stays blocked.

#### Scenario: Unauthorized resolution or late worker
- **WHEN** an ordinary owner attempts manual resolution or an operator cannot rule out a later original dispatch
- **THEN** resolution cannot clear the uncertainty/barrier.

### Requirement: Separate supplier refund and customer payout
The system SHALL label the supplier financial amount, currency and destination with their provenance. Cancellation success SHALL NOT declare a customer payout, use original booking total as a refund receipt or manufacture a refund reference. Partial cancellation, ancillaries and customer-money movement are outside this pilot.

#### Scenario: Cancellation confirmed with supplier return
- **WHEN** the supplier cancellation is confirmed
- **THEN** the UI reports that fact and supplied financial information while explicitly stating that customer payout is not established.

### Requirement: Historical compatibility and safe activation
The system SHALL read existing booking events/routes/party snapshots without rewriting history or applying new creation rules to it. Existing bodyless cancellation SHALL not trigger a new external cancellation without the consent protocol. A historical unresolved confirmation SHALL not be inferred safe from Held alone.

#### Scenario: Existing terminal or legacy owned order
- **WHEN** the owner reads an old Cancelled/Refunded order
- **THEN** its existing state remains readable but is not presented as a receipt for a new cancellation operation.

#### Scenario: Legacy confirmation ambiguity
- **WHEN** activation encounters Held history for which absence of an old external effect is not established
- **THEN** automatic cancellation is withheld pending explicit verification/manual handling.

### Requirement: Evidence boundaries of the pilot
The pilot SHALL use only fictional data and offline controlled provider behavior. Verification SHALL separate source/local UI evidence, real durable CI evidence and supplier runtime proof. Existing authentication, PII boundaries and CI checks SHALL not be weakened to demonstrate success.

#### Scenario: Controlled test passes
- **WHEN** an offline provider/demo test passes
- **THEN** its report describes only Travel behavior under the modeled facts, without claiming live Duffel or real payment acceptance.

### Requirement: Safe end of an unaccepted review
The system SHALL permit explicit abandonment of a positively completed unaccepted preparation, including unsupported terms, and expiry of known valid unaccepted terms only when supplier confirmation cannot have been dispatched and no preparation worker can still issue an action. Missing terms or a lost preparation response SHALL NOT establish this condition. Ending a review SHALL NOT mean the booking was cancelled. Accepted or uncertain operations SHALL not release their mutation barrier through abandonment or elapsed time.

#### Scenario: Consent competes with abandonment or expiry
- **WHEN** consent races with closing an unaccepted review
- **THEN** exactly one admission wins, and stale consent cannot confirm the closed proposal.

#### Scenario: Accepted or uncertain operation is abandoned
- **WHEN** the owner tries to abandon an already accepted, dispatched or uncertain operation
- **THEN** the request cannot clear the barrier or create permission for a new external action.


### Requirement: Recovery proceeds without a returning client
The system SHALL retain the work needed to observe or escalate every admitted cancellation across process failure before allowing a possible supplier mutation. When persistence and message processing are available again, recovery SHALL resume without a browser request, republishing by the client or a repeated supplier mutation. Automatic observation budget and deadline SHALL remain finite and SHALL NOT reset on restart or duplicate delivery. Infrastructure delivery failure SHALL be reported separately from the business outcome.

#### Scenario: Crash immediately after dispatch admission is persisted
- **WHEN** the process stops after the dispatch claim commits but before the supplier request or its response is saved
- **THEN** retained recovery work discovers the operation and observes or escalates it without client activity or another supplier POST.

#### Scenario: Observation fails after reserving its attempt
- **WHEN** an observation worker stops after consuming its attempt but before reading the supplier
- **THEN** later retained work still reaches a truthful outcome or manual review within the unchanged budget; duplicate workers cannot reset the budget.

#### Scenario: Persistent delivery is unavailable or exhausted
- **WHEN** recovery delivery cannot finish because storage is unavailable or its transport retries are exhausted
- **THEN** the system retains the mutation barrier and exposes delivery diagnostics without inventing a rejected/succeeded outcome; safe recovery can resume after infrastructure repair.

#### Scenario: Confirmation blocks cancellation before a cancellation operation exists
- **WHEN** an owned confirmation has an unresolved persisted admission and the user reloads the order
- **THEN** an authorized read exposes the confirmation blocker and verification path even if there is no cancellation operation; it does not infer permission to cancel from the last booking status.

### Requirement: Shared refresh admission and readable confirmation blockers
The system SHALL keep a shared per-owner-booking supplier refresh limit of one accepted request per60 seconds for owner and operator, with at most one pending on-demand observation and a common bounded read window. Replays SHALL not schedule another read or reset the automatic recovery budget. Ordinary status reads SHALL never call suppliers. Owner and operator refresh SHALL use separate authorization boundaries.

#### Scenario: Owner and operator refresh concurrently
- **WHEN** owner and authorized operator request a new supplier observation for the same booking within60 seconds
- **THEN** only one new read admission succeeds; the other receives current pending information or429 with Retry-After, without scope bypass or another provider call.

#### Scenario: Legacy Held has no historical confirmation attempt
- **WHEN** an owned old Held order has no coordination marker or recorded confirmation attempt
- **THEN** status identifies a LegacyHeld blocker by booking identity/version without manufacturing an attempt; operator clearance needs the separate legacy-writer drain and positive no-effects evidence.

#### Scenario: New supplier receipt discovered during manual confirmation review
- **WHEN** an authorized operator establishes a previously unknown supplier receipt after a lost response
- **THEN** it can be recorded only as OperatorVerified evidence bound to the exact persisted attempt/order/accepted amount and saved wallet PaymentRef; a missing saved wallet reference or mismatched known supplier receipt prevents successful resolution.

#### Scenario: Confirmation might have touched an in-memory wallet before restart
- **WHEN** a confirmation effects claim exists but durable payment evidence is unavailable after restart
- **THEN** the missing wallet entry is not accepted as proof of no effect; the manual blocker remains and no payment capture/refund/confirm is automatically repeated.

#### Scenario: Negative manual resolution after possible supplier dispatch
- **WHEN** an operator cannot establish both old-sender quiescence and affirmative supplier no-effect/no-pending evidence (plus wallet facts for confirmation)
- **THEN** the resolver refuses to release the barrier; an inconclusive record remains explicitly manual rather than becoming a supplier rejection.

### Requirement: Stable historical request identity
The system SHALL retain accepted cancellation operation/request and manual-resolution identities across later revisions, subsequent operations and restarts. After authorization and target binding, an exact accepted replay SHALL resolve the original operation before applying new-action revision or expiry guards. A historical replay SHALL NOT replace the current operation in the UI or repeat an external action.

#### Scenario: Exact consent or manual-resolution retry has an old revision
- **WHEN** the original request was accepted and a byte-identical consent or equivalent normalized manual-resolution request is retried with its original expected revision
- **THEN** the original receipt/current result is returned without a new event, supplier call or notification; changed payload under that ID is rejected.

#### Scenario: Old prepare arrives after another operation started
- **WHEN** an old accepted prepare is delivered again after it closed and a newer operation exists
- **THEN** the response identifies that historical operation and the current operation separately; the old ID is not reused and no new create is sent.

#### Scenario: Positively completed preparation has unsupported terms
- **WHEN** quote creation is conclusively completed, no consent or confirmation exists, no create worker can still act, and the returned terms are unsupported
- **THEN** explicit abandonment can close the review; a lost or malformed create response cannot use this shortcut.

#### Scenario: Refresh is requested before a dispatch claim exists
- **WHEN** an admitted operation has no recovery epoch yet or only a completed unaccepted review
- **THEN** refresh returns current local/pending information without scheduling a supplier read with a missing epoch or manufacturing another mutation.
