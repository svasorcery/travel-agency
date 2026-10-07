# Spec Delta

## MODIFIED Requirements

### Requirement: Whole-order eligibility and ownership
The system SHALL allow a new cancellation only for an authenticated owner, an eligible booking, explicit current supplier API capability and no unresolved competing mutation. Eligible statuses are Held, Confirmed and Ticketed, with the historical coordination verification required below. The complete passenger party, all booked legs and recorded actual services form one cancellation scope. A conclusively created unpaid order with service differences SHALL remain cancellable once creation completion and absence of competing financial effects are established.

#### Scenario: Foreign or ownerless order
- **WHEN** another user, invalid identity or an ownerless history requests terms, consent or operation status
- **THEN** the system rejects access before supplier invocation, disclosure or no-op.

#### Scenario: Whole group and itinerary
- **WHEN** an owned order contains nine adults and four ordered legs
- **THEN** consent and cancellation cover the complete order; no individual passenger or leg cancellation is offered.

#### Scenario: Known created order has different services
- **WHEN** the exact complete unpaid order and finished creation are established but its services differ from the accepted quote
- **THEN** the owner may consent to whole-order cancellation of the recorded actual order; the system does not require acceptance of replacements or proof that the created order never existed.

#### Scenario: Creation or financial outcome remains unknown
- **WHEN** attribution, creation completion or a competing confirmation/money effect is unresolved
- **THEN** cancellation cannot bypass that uncertainty; a missing service alone is not confused with an unknown creation once the required facts are proven.

### Requirement: Separate supplier refund and customer payout
The system SHALL label the supplier financial amount, currency and destination with their provenance. Cancellation success SHALL NOT declare a customer payout, use original booking total as a refund receipt or manufacture a refund reference. For orders with services, the exact supplier cancellation quote SHALL remain the whole-order financial amount; service allocation SHALL NOT be inferred. Partial cancellation, standalone service refunds and customer-money movement remain outside scope.

#### Scenario: Cancellation confirmed with supplier return
- **WHEN** the supplier cancellation is confirmed
- **THEN** the UI reports that fact and supplied financial information while explicitly stating that customer payout is not established.

#### Scenario: Refund differs from selected or actual services price
- **WHEN** the supplier supplies a smaller or zero whole-order refund
- **THEN** the review shows that exact quote without adding service prices, guaranteeing free cancellation or declaring an individual seat or bag refunded.

### Requirement: Historical compatibility and safe activation
The system SHALL read existing booking events/routes/party snapshots without rewriting history or applying new creation rules to it. Existing bodyless cancellation SHALL not trigger a new external cancellation without the consent protocol. A historical unresolved confirmation SHALL not be inferred safe from Held alone. Historical accepted cancellation scope, terms, notice and fingerprints SHALL retain their original semantics when service-aware readers are introduced.

#### Scenario: Existing terminal or legacy owned order
- **WHEN** the owner reads an old Cancelled/Refunded order
- **THEN** its existing state remains readable but is not presented as a receipt for a new cancellation operation.

#### Scenario: Legacy confirmation ambiguity
- **WHEN** activation encounters Held history for which absence of an old external effect is not established
- **THEN** automatic cancellation is withheld pending explicit verification/manual handling.

#### Scenario: Existing accepted cancellation uses earlier scope
- **WHEN** an old accepted cancellation is replayed or observed after service-aware readers are installed
- **THEN** its saved scope and acceptance are not recomputed, and exact request replay sends no new mutation.
