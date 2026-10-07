# Spec Delta

## Purpose

Let an authenticated traveller choose optional baggage and seats in an existing complete booking flow, accept one exact quote and obtain truthful results with limited automatic checking and explicit manual handling of uncertainty.

## ADDED Requirements

### Requirement: Complete pre-booking scope
The system SHALL support optional checked baggage and seats before creating one order for 1–9 adults and 1–4 flight legs. Booking without extras SHALL remain possible. Post-booking additions, accepting replacements and standalone service refunds SHALL be outside this slice.

#### Scenario: Extras skipped
- **WHEN** no service is selected
- **THEN** the complete ordinary booking remains available without automatic charges or assigned seats.

#### Scenario: Existing order
- **WHEN** the owner reads a created order
- **THEN** recorded services are visible without a new add-or-replace action.

### Requirement: Temporary catalog and retained purchase
The system SHALL treat available inventory as temporary and retain only the selected quoted terms and booking outcomes for durable purchase history. Empty inventory, unavailable capability and failed retrieval SHALL remain distinguishable.

#### Scenario: Catalog is no longer available
- **WHEN** a displayed catalog becomes stale or disappears before purchase
- **THEN** the user can request current inventory and obtain a new quote; no old catalog is treated as an accepted or booked service.

### Requirement: One current quote and preserved draft
The system SHALL present one final quote covering flights and selected services. Refresh SHALL preserve usable preferences as an unaccepted draft, identify changes and require new consent for the new quote revision. A local catalog timer SHALL NOT erase the entire selection independently of supplier offer expiry.

#### Scenario: Group takes more than two minutes
- **WHEN** a party selects seats across four legs for longer than two minutes while the supplier offer remains valid
- **THEN** selections are not cleared merely by a local two-minute timer.

#### Scenario: New service identity or changed terms
- **WHEN** refresh changes a selected identity, price, eligibility, scope or restriction
- **THEN** changes are shown and the prior consent cannot purchase the replacement automatically.

### Requirement: Ownership and current scope
The system SHALL require authenticated ownership for selected quotes and creation results. Anonymous or foreign refresh of a claimed quote SHALL fail before provider access. After creation starts, every quote writer SHALL freeze the accepted scope until positive no-creation permits a new quote; the same owner SHALL NOT overwrite it during creation.

#### Scenario: Foreign or anonymous refresh
- **WHEN** another user or an anonymous request targets a claimed quote
- **THEN** no private facts, supplier read or quote mutation is performed.

#### Scenario: Different passenger or segment
- **WHEN** a submitted service belongs to another quote, adult or segment
- **THEN** it is rejected before creation regardless of the submitted price.

#### Scenario: Owner refresh races with creation
- **WHEN** the same owner refreshes a quote while creation starts
- **THEN** only one revision transition wins, the held purchase retains its original binding, and an already-started refresh cannot overwrite an admitted or created order.

### Requirement: Baggage allowance and extra quantity
The system SHALL show included allowance separately per adult and segment and sell checked baggage only within its current quantity and scope limits. Missing allowance SHALL remain unavailable. A multi-segment service unit SHALL be charged once; group or unsupported product types SHALL not be silently split.

#### Scenario: Two units cover two segments
- **WHEN** two units of a GBP 10.00 baggage service cover two connected segments
- **THEN** the extra charge is GBP 20.00 and both segments show coverage, without counting the charge twice.

#### Scenario: Missing allowance or invalid quantity
- **WHEN** allowance is missing or selected quantity exceeds the supported integer limit
- **THEN** missing is not shown as zero and the invalid selection cannot be purchased.

### Requirement: Passenger-specific seats and disclosures
The system SHALL allow at most one seat per adult per segment and one adult per physical seat, using passenger-specific eligibility and price. Known zero-price seats SHALL remain real services. Seat restrictions SHALL be safely shown and accepted during review; incomplete maps SHALL not produce invented assignments.

#### Scenario: Two adults and one physical seat
- **WHEN** the adults have different service prices for one seat and both try to select it
- **THEN** their own prices apply and only one can hold that physical seat.

#### Scenario: Free seat, absent map or irregular layout
- **WHEN** a seat costs zero, another segment lacks a map, or rows contain facilities and irregular sections
- **THEN** the free service is retained, missing selection is labelled unavailable, and accessible keyboard/text selection cannot select a non-seat element.

### Requirement: Exact accepted total
The system SHALL compute the final amount from current base fare and selected unit prices times quantities in one currency, with explicit acceptance bound to that quote. Missing or incompatible money, arithmetic overflow and unsupported pricing intent SHALL refuse purchase before financial effects.

#### Scenario: One complete accepted total
- **WHEN** fare is GBP 50.00, two bags cost GBP 10.00 each and a seat costs GBP 3.50
- **THEN** the review and accepted order amount are GBP 73.50.

#### Scenario: Old client cannot accept extras
- **WHEN** an old hold request targets a quote with selected services without the new explicit ancillary acceptance
- **THEN** creation is refused; an old request for an empty-service quote retains its compatible behavior.

### Requirement: Separate offer and order identities
The system SHALL reconcile created orders using complete unambiguous route, passenger and service facts rather than equal offer/order identifiers. Booked line amounts that already include quantity SHALL not be multiplied again. Ambiguous identity SHALL remain unknown.

#### Scenario: Correct order uses new identifiers
- **WHEN** supplier order segments and a booked bag have new IDs but map uniquely to the accepted scope and quantity two has a GBP 20.00 line total
- **THEN** the result verifies without requiring ID equality or turning that price into GBP 40.00.

#### Scenario: Ambiguous identity
- **WHEN** two segments cannot be uniquely matched or passenger linkage is not established
- **THEN** the system neither guesses by name/position nor classifies the result as a safely cancellable known order.

### Requirement: Durable single creation attempt
The system SHALL retain owner, accepted purchase, protected party and request identity before a possible supplier creation. All new hold entry points SHALL share the same per-booking barrier. Restart, timeout, a different retry key or expired response cache SHALL NOT authorize another creation for an unresolved attempt.

#### Scenario: Commit fails or two requests race
- **WHEN** admission cannot commit or two clients submit the same quote
- **THEN** the failed admission sends nothing and at most one winning invocation may create the order.

#### Scenario: Crash before or after sending
- **WHEN** a process stops after saving the attempt but before saving a conclusive result
- **THEN** the attempt remains visible and no replacement invocation resends creation, even if the original POST might never have happened.

### Requirement: Retained identity and compatible hold response
The system SHALL resolve exact accepted repeats to their original attempt and reject changed payloads under that identity. A successful legacy hold response SHALL identify a verified matching held order; pending, unknown or different terms SHALL never masquerade as that success.

#### Scenario: Browser stops waiting
- **WHEN** the browser times out before creation finishes
- **THEN** it reads saved status instead of repeating hold, while the server can retain a later result independently of that browser connection.

#### Scenario: Exact replay after expiry
- **WHEN** the same accepted request reaches the server after quote or response-cache expiry
- **THEN** the durable receipt prevents a second effect; authoritative metadata status remains available even if a new plaintext POST cannot be processed during a key outage.

### Requirement: Distinct outcomes and cancellation-only differences
The system SHALL distinguish a matching order, a conclusively created unpaid order with differences, conclusive no-creation and unresolved outcome. Known differences SHALL block payment and acceptance of replacements but permit whole-order cancellation once identity, completion and absence of competing financial effects are established.

#### Scenario: Bag is missing from a known created order
- **WHEN** the exact complete unpaid order is established but its baggage differs and creation is finished
- **THEN** actual versus accepted facts are shown and the owner can request whole-order cancellation; the original purchase is not silently changed or permanently blocked as unknown.

#### Scenario: Existence or completion is unproven
- **WHEN** an order reference is uncorrelated, creation may still be pending or payment facts are unknown
- **THEN** confirm and cancellation remain guarded until manual resolution supplies the required evidence.

#### Scenario: New attempt after conclusive no-creation
- **WHEN** no-creation is positively established and a new request identity targets the old accepted quote
- **THEN** a fresh quote is required before another create; an exact repeat retains the original no-creation receipt.

### Requirement: Limited checking and explicit manual fallback
The system SHALL retain one bounded background check for a started creation and read only an already trustworthy known order identity. Missing identity or an inconclusive result SHALL lead to manual review. It SHALL NOT automatically search supplier order lists, repeatedly recreate orders or run an extended recovery cycle in this slice.

#### Scenario: Saved known ID can be checked
- **WHEN** a correlated creation receipt includes an order ID but the saved service facts are incomplete
- **THEN** a bounded read can establish the result without another creation request.

#### Scenario: Lost ID, late work or unavailable delivery
- **WHEN** the ID was lost, the check's fixed deadline passed or durable delivery failed
- **THEN** the UI shows manual review or delivery diagnostics with the original barrier; elapsed time is not no-effect evidence.

### Requirement: Privileged manual creation review
The system SHALL extend existing operator review with creation outcomes and retain actor, time, revision, resolution identity and evidence provenance. Positive attachment SHALL require exact attribution and sender quiescence; negative closure SHALL require affirmative no-created/no-pending evidence. Owners SHALL not override these facts.

#### Scenario: Known order attached with differences
- **WHEN** an authorized operator establishes the exact unpaid order, its actual scope and creation completion
- **THEN** a recorded cancellation-only result can replace uncertainty without approving different services or sending another create.

#### Scenario: Timeout-only or unauthorized resolution
- **WHEN** an owner attempts resolution or an operator has only timeout, missing data or an unverified stopped sender
- **THEN** the barrier cannot be released.

### Requirement: Confirmation verifies accepted services
The system SHALL check current service composition and money before financial effects and before supplier payment. After financial uncertainty, existing confirmation barriers SHALL remain. Manual money evidence alone SHALL not establish completion of a new service-bearing purchase.

#### Scenario: Same price and different seat before payment
- **WHEN** the order now has a different seat at the same total and no money effect started
- **THEN** payment is withheld and a conclusively identified order is eligible only for the agreed cancellation path.

#### Scenario: Different service after capture
- **WHEN** capture may have happened and the service composition does not match
- **THEN** an old money-only manual confirmation cannot complete the purchase or release its financial barrier; no capture, payment or refund is retried automatically.

### Requirement: Authoritative status and resource-based recovery
The system SHALL expose owner-authorized creation and service facts independently of eventual order projections. The existing booking resource URL SHALL support reload without browser operation storage. Stale responses or identity changes SHALL not overwrite newer facts; loss of all resource context may require operator help in this slice.

#### Scenario: Reload before order projection
- **WHEN** admission is saved and the owner reloads the booking resource page while EF has no order
- **THEN** saved creation state remains readable and the UI does not resubmit the purchase.

#### Scenario: Older response or another owner
- **WHEN** an old projection, cached hold response or a previous owner's callback arrives
- **THEN** it cannot restore old consent/PII or enable a mutation contrary to the current authoritative state.

### Requirement: Protected details and private operation state
The system SHALL protect passenger details before durable dispatch, authorize before decrypting them and fail closed when protection is unavailable. Tokens, PII, bodies and operation/retry IDs SHALL NOT enter browser storage, URLs, history, logs or traces. Only the booking resource identity may be used for navigation.

#### Scenario: Key unavailable
- **WHEN** protection keys cannot be used
- **THEN** new PII ingress fails safely while authorized metadata-only status remains readable without a plaintext fallback.

### Requirement: Readable purchase summary and differences
The system SHALL provide a compact adult-and-journey summary of included baggage, selected extras and seats with separate fare, extras and total. It SHALL show proven selected-to-actual differences and current permitted actions, keep unknown facts explicit and treat no seat selection as optional. Display grouping SHALL NOT duplicate a service charge or require new passenger-detail storage.

#### Scenario: Group summary with shared segment coverage
- **WHEN** a party has different allowances and one selected bag covers multiple segments
- **THEN** coverage and segment exceptions are readable for each adult, the service price is counted once, and an unselected seat is not shown as a blocking error.

#### Scenario: Different seat at the same total
- **WHEN** an actual seat differs from the selected seat while the price is unchanged
- **THEN** the difference remains visible and the interface offers only actions permitted by the current creation and financial state, without accepting a replacement implicitly.

#### Scenario: Unknown facts or different currency
- **WHEN** actual baggage is unverified or accepted and actual amounts use different currencies
- **THEN** the UI shows that uncertainty or currency change without treating unknown baggage as zero, calculating a cross-currency delta or inventing a service refund.

### Requirement: Reproducible fictional demonstrations
The demo SHALL offer documented, reproducible presets for successful purchase, a conclusively created unpaid order with differences followed by cancellation, and an unknown result requiring manual review. Presets SHALL use fictional fixtures and existing workflow behavior, remain isolated from production and SHALL NOT create a runtime fault-control API or reset an active operation's safeguards.

#### Scenario: Three demonstrations run from isolated starts
- **WHEN** a presenter follows the documented steps for each preset
- **THEN** the three expected user-visible outcomes can be reproduced, no unknown hold is resent, fictional scope is clear and no real supplier, payment or AI call is made.

### Requirement: Historical and evidence compatibility
The system SHALL preserve historical party events, cancellation scope and manual receipt semantics. Fictional tests, durable CI and live supplier acceptance SHALL be described separately. Unsupported supplier contracts SHALL produce explicit safe failure without silently dropping services or changing order type.

#### Scenario: Historical order and operator receipt
- **WHEN** new readers encounter old V1/V2/V3 orders or accepted manual resolutions
- **THEN** their prior facts and exact replay hashes remain valid, without invented services or refunds.

#### Scenario: Fictional demonstration passes
- **WHEN** offline and fictional UI checks pass
- **THEN** the report does not claim live supplier/payment/deployment acceptance or completion of all M3.
