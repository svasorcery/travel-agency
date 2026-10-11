# Spec Delta

## Purpose

Provide one truthful fictional customer refund after a proven whole-order cancellation, with separate supplier and customer financial facts, single execution and explicit manual handling of uncertainty.

## ADDED Requirements

### Requirement: Separate supplier and customer money
The system SHALL present supplier cancellation entitlement, supplier settlement and customer refund as separate facts with their own amount, currency, destination and provenance. Neither cancellation success nor a historical refund status SHALL establish customer payout or supplier settlement.

#### Scenario: Proven cancellation with no settlement receipt
- **WHEN** cancellation confirms GBP80 returned to the agency balance
- **THEN** GBP80 supplier entitlement is shown while actual supplier settlement and customer payout remain unestablished until separately evidenced.

#### Scenario: Refund execution is proven
- **WHEN** a matching refund receipt proves dispatch to the original customer payment method
- **THEN** the UI reports the exact refund sent, without claiming the customer's bank has credited it.

### Requirement: Whole-order refund policy
The system SHALL return the exact confirmed supplier cash entitlement once for the whole order, without Travel fees, FX, service allocation or silent capping. Automatic execution SHALL require matching original captured money and currency, a supported agency-balance destination and known cash-only settlement.

#### Scenario: Supplier penalty
- **WHEN** the original customer capture is GBP100 and the confirmed whole-order cash entitlement is GBP80
- **THEN** one GBP80 refund is prepared; no bag, seat, leg or passenger allocation is invented.

#### Scenario: Unsupported financial facts
- **WHEN** entitlement exceeds capture, currencies differ, credits/mixed settlement are present or destination/payment facts are unsupported
- **THEN** automatic refund is withheld and manual handling explains the incompatible facts without rounding, FX or reducing the amount.

### Requirement: Evidence-bound automatic eligibility
Automatic refund SHALL require an owned complete order, a proven consent-bound cancellation, compatible fictional capture evidence and completed matching confirmation. Unknown competing effects SHALL block it. Saved payment evidence SHALL remain explicitly fictional across restart; payment labels or cache absence SHALL not replace that evidence.

#### Scenario: Restart after capture
- **WHEN** a valid fictional capture and cancellation are retained but the process-local wallet cache is empty
- **THEN** the existing retained basis is used without another authorize/capture, and no real payment claim is made.

#### Scenario: Legacy or unresolved confirmation
- **WHEN** a historical PaymentRef has no compatible capture proof or an earlier confirmation may still have effects
- **THEN** automated refund is unavailable and the owner sees manual/unverified state.

#### Scenario: Operator-verified cancellation
- **WHEN** cancellation money or outcome was established only by operator judgment
- **THEN** it is explicitly manual for customer refund and does not silently trigger automatic payment.

### Requirement: Funding after proven cancellation
The system SHALL admit eligible customer refund after cancellation is conclusively established, using agency funding without waiting for supplier settlement. It SHALL NOT treat a pending, unknown or unmatched cancellation as eligibility.

#### Scenario: Supplier settlement is still unknown
- **WHEN** the exact accepted cancellation succeeded and the refund basis is eligible
- **THEN** agency-funded customer refund proceeds while supplier settlement remains independently unestablished.

### Requirement: Zero and unpaid outcomes
The system SHALL distinguish proven zero entitlement, proven no customer capture, missing capture evidence and executed refund. Known zero or proven unpaid cancellation SHALL require no payment execution; absence of evidence SHALL not be displayed as zero or unpaid.

#### Scenario: Nonrefundable paid order
- **WHEN** captured GBP100 has a proven cash entitlement of GBP0
- **THEN** NoRefundDue displays GBP0 and no gateway call or payout receipt is created.

#### Scenario: Unpaid service-difference cancellation
- **WHEN** creation/confirmation evidence proves no customer capture and cancellation is complete
- **THEN** NoCustomerPayment is shown without manufacturing a paid refund.

#### Scenario: Missing payment facts
- **WHEN** capture evidence is absent
- **THEN** payment remains unverified and is not classified as NoCustomerPayment.

### Requirement: Retained refund admission
The system SHALL retain one frozen customer-refund case per booking/payment basis before possible execution, including source cancellation, exact money, policy and original destination. Concurrent, replayed or replacement identities SHALL resolve to that case without admitting another automatic payout.

#### Scenario: Concurrent admissions
- **WHEN** assessment messages repeat or two workers assess the same cancellation
- **THEN** only one frozen case is committed and at most one execution may be admitted.

#### Scenario: Historic request or cache expiry
- **WHEN** delivery repeats after restart or HTTP response-cache expiry
- **THEN** the retained case is authoritative and no new payment permission is created.

### Requirement: Single dispatch after durable claim
The system SHALL persist one execution claim before a possible refund mutation. Claim redelivery, elapsed deadlines, process restart and lost responses SHALL NOT authorize another refund execution. Claim and retained escalation work SHALL commit consistently.

#### Scenario: Crash around execution
- **WHEN** the process stops after claim persistence either before sending or after losing the result
- **THEN** recovery exposes the same unknown/manual case without repeating the refund.

#### Scenario: Admission cannot commit
- **WHEN** persistence or an expected-version conflict prevents the winning claim from committing
- **THEN** that invocation makes no payment call.

### Requirement: Truthful receipt outcomes
The system SHALL distinguish matching affirmative refund dispatch, pending processing, affirmative no-effect and unknown. Receipt identity, original payment, money, currency and destination SHALL match the frozen case. Generic errors, timeout, missing receipt or conflicting data SHALL not become success, refusal or permission to retry.

#### Scenario: Matching dispatch receipt
- **WHEN** a validated receipt binds the exact payment and GBP80 to its original method
- **THEN** Sent is retained with exact money and provenance; bank credit remains unestablished.

#### Scenario: Lost or mismatched result
- **WHEN** execution times out or the receipt refers to another payment, amount, currency or destination
- **THEN** the case remains unknown/manual and another execution stays unavailable.

#### Scenario: Affirmative no effect
- **WHEN** positive evidence proves the exact attempt caused no refund and has no pending outcome
- **THEN** ConfirmedNoEffect is recorded while the customer's entitlement remains unpaid; no automatic replacement is sent.

### Requirement: Finite manual escalation without a client
The system SHALL retain a fixed deadline for every admitted case. Once admitted, restart or duplicate work SHALL not reset it. Unresolved pending/unknown work SHALL reach explicit manual handling when delivery becomes available, without supplier polling, a returning browser or a second payout.

#### Scenario: Browser does not return
- **WHEN** execution result is lost and the owner closes the browser
- **THEN** retained work escalates the same case within its unchanged deadline after infrastructure recovery.

#### Scenario: Delivery fails
- **WHEN** storage or message delivery is unavailable or exhausted
- **THEN** delivery diagnostics are separate, monetary uncertainty stays retained, and no financial failure or success is fabricated.

### Requirement: Separate privileged manual resolution
The system SHALL accept revision-bound typed outcome evidence only from a separately authorized customer-refund reviewer. It SHALL retain actor, server time, resolution identity and OperatorVerified provenance. Resolution SHALL never send money or accept an owner's override, arbitrary amount, timeout-only proof or unbound payment evidence.

#### Scenario: Ordinary owner
- **WHEN** an owner or cancellation-only reviewer attempts customer-refund resolution
- **THEN** access is rejected before disclosure, event append or effects.

#### Scenario: Exact resolution replay
- **WHEN** the same accepted resolution is repeated with its original revision and equivalent bound evidence
- **THEN** the original receipt is returned with no new effect; changed evidence under that identity conflicts.

#### Scenario: Negative result and late sender
- **WHEN** a reviewer lacks affirmative no-effect/no-pending evidence or cannot establish original sender quiescence
- **THEN** the unresolved case cannot be negatively closed.

#### Scenario: Late positive evidence
- **WHEN** a matching affirmative receipt arrives after escalation
- **THEN** it can establish Sent without another execution; conflicting prior evidence stays visible for manual review.

### Requirement: Authoritative private owner status
Owner status SHALL be available from retained booking evidence independently of EF lag and without external calls or mutations. Money SHALL use exact decimal representation. Access SHALL be owner-bound and no-store; payment/provider/operation references SHALL not enter owner URLs, browser history/storage or logs.

#### Scenario: Reload during EF lag
- **WHEN** cancellation/refund evidence is committed while EF is missing or stale
- **THEN** authorized status displays the retained case, and reload does not create or resend a payout.

#### Scenario: Foreign owner or stale callback
- **WHEN** another owner requests status or a response from an earlier owner/version arrives
- **THEN** access or application of that response is rejected before disclosure or overwriting newer facts.

#### Scenario: Status read fails
- **WHEN** a later GET fails after Sent was already authoritatively shown
- **THEN** the UI preserves proven Sent and shows the read failure separately.

### Requirement: Truthful new airline reports
A new airline cancellation report without exact financial evidence SHALL retain only the proven report and unresolved money status. It SHALL NOT invent a refund reference, use the original booking total as refund amount or automatically pay the customer. Competing effects SHALL remain guarded until their facts are established.

#### Scenario: Amount-free signed report
- **WHEN** an owned order receives a cancellation report carrying no proven refundable money
- **THEN** the system records its source/observation fact and manual uncertainty without appending a customer refund receipt or starting payout.

#### Scenario: Report races with confirmation
- **WHEN** an airline report arrives while a confirmation worker may still act
- **THEN** the report does not declare that worker stopped, and existing unknown-effect barriers remain.

### Requirement: Resolvable supplier cancellation reports
The system SHALL expose retained cancellation reports to owner status and privileged supplier review even without a cancellation operation. Typed review SHALL establish cancellation, disprove it with affirmative no-cancel/no-pending evidence, or retain inconclusive. Report resolution SHALL NOT close competing financial attempts, authorize payout or invent monetary facts.

#### Scenario: Report without an existing operation
- **WHEN** a Confirmed or Ticketed order has a retained cancellation report and no cancellation operation
- **THEN** the owner sees manual report state and an authorized reviewer can resolve that report using its own retained identity and revision.

#### Scenario: Confirmed report
- **WHEN** typed affirmative evidence proves cancellation of the exact reported order
- **THEN** the verified actual cancellation time is retained separately from server observation time, while customer money remains unestablished without separate proof.

#### Scenario: Disproved or inconclusive report with competing uncertainty
- **WHEN** a report is disproved by positive no-cancel/no-pending evidence or the review is inconclusive while another attempt remains unresolved
- **THEN** only the report's own barrier can be changed; previous confirmation/cancellation uncertainty remains and normal guards control any new action.

#### Scenario: Old resolution and a later report
- **WHEN** an accepted report resolution is replayed after a distinct new report arrived
- **THEN** its original receipt is returned without resolving the new report, changing historical hashes or causing payment effects.

### Requirement: Historical and service compatibility
The system SHALL read prior booking/refund/cancellation events unchanged and SHALL NOT backfill payouts from historical labels or totals. Previous consent, scope and fingerprint bytes SHALL retain their meaning. Customer payout SHALL not infer individual service refunds or convert a proven unpaid differences case into a captured payment.

#### Scenario: Historical Refunded
- **WHEN** a retained old refund event is read
- **THEN** the old wire state remains readable with a notice that customer payout is unverified, without a new automatic refund case.

#### Scenario: Whole group with extras
- **WHEN** an order has nine adults, four legs and actual baggage/seats
- **THEN** one whole-order entitlement is used, without multiplying it by passengers, legs or service quantities.

### Requirement: Fictional verification and production exclusion
The slice SHALL demonstrate sent, zero and uncertain/manual outcomes with reproducible fictional fixtures and startup presets. Fictional payment execution SHALL remain excluded from Production. Local demo evidence, persisted CI acceptance and separately authorized live checks SHALL be distinguished; auth and existing CI SHALL not be weakened.

#### Scenario: Demonstrations run independently
- **WHEN** the three documented startup presets are exercised
- **THEN** their expected results are reproducible with no real supplier/payment/AI call and no runtime control that releases an active barrier.

#### Scenario: Production or real PSP
- **WHEN** Production has no separately designed payment implementation
- **THEN** fictional execution is unavailable rather than silently registered or routed to a real provider.
