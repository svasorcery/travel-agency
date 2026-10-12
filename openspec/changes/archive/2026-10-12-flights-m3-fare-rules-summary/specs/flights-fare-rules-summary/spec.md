# Spec Delta

## Purpose

Дать пользователю краткое проверяемое объяснение подтверждённых условий конкретного предложения, сохранив неизвестные сведения, различия областей действия и независимость финансовых операций от AI.

## ADDED Requirements

### Requirement: Three states for supplier permissions
The system SHALL distinguish confirmed permission, confirmed prohibition and unknown information. Missing, null, malformed or historical lossy fields SHALL NOT become a confirmed prohibition or permission.

#### Scenario: Missing allowed member
- **WHEN** the supplier omits conditions, a condition object or its allowed member
- **THEN** the corresponding information is unknown, without displaying a prohibition.

#### Scenario: Explicit true or false
- **WHEN** a valid scoped condition explicitly supplies true or false
- **THEN** that permission or prohibition applies to that scope only.

### Requirement: Exact known penalty and explicit missing money
The system SHALL show a penalty only from a complete valid source amount and currency, using exact decimal representation. Unknown, malformed and zero penalties SHALL remain distinct. Penalties SHALL NOT be converted, multiplied or subtracted from the ticket price to produce a refund.

#### Scenario: Allowed with unknown penalty
- **WHEN** a change is allowed and either penalty amount or currency is missing
- **THEN** permission is shown with an unknown fee, without saying the change is free.

#### Scenario: Zero and a different currency
- **WHEN** a valid penalty is GBP0 and the offer price uses EUR
- **THEN** GBP0 remains the indicated penalty; no FX, refund amount or absence of other costs is inferred.

#### Scenario: Invalid penalty pair
- **WHEN** supplied money is negative, overflowing or malformed, or currency is invalid
- **THEN** the fee is unavailable and other valid scoped information remains.

### Requirement: Separate whole-offer and leg conditions
The system SHALL preserve each condition's supplied scope. Whole-offer conditions SHALL cover the stated complete party and route; individual-leg conditions SHALL not be extended to the whole route or used to infer partial refunds.

#### Scenario: One leg allows change
- **WHEN** whole-offer change is prohibited while one of four legs allows change with a fee
- **THEN** both facts retain distinct scopes, without promising a whole-route change or partial refund.

#### Scenario: No whole-offer condition
- **WHEN** legs have change information but the whole-offer condition is absent
- **THEN** the whole-offer condition remains unknown rather than being computed from the legs.

### Requirement: Fare code brand and baggage keep their source scopes
The system SHALL distinguish actual fare basis codes from brands and show included baggage per confirmed adult and segment. Missing allowance SHALL not become zero; a maximum or one passenger's allowance SHALL not become a whole-route or group entitlement.

#### Scenario: Brand without code
- **WHEN** a leg has a fare brand but no segment-passenger fare basis code
- **THEN** the brand is shown as a brand and the code remains unknown.

#### Scenario: Nine adults with different allowances
- **WHEN** nine adults on four legs have different known and missing allowances
- **THEN** grouping retains exact passenger-segment coverage and unknown exceptions, without a sum or maximum becoming a guaranteed group allowance.

#### Scenario: Missing baggage type
- **WHEN** an array is empty or omits a type without a confirmed zero for that type
- **THEN** the corresponding allowance remains unknown in the explanation.

#### Scenario: Missing or invalid quantity and malformed labels
- **WHEN** informational baggage quantity is omitted, null, wrong-type or overflowing, or an optional fare label is malformed
- **THEN** the affected explanation fact is unknown or unavailable, no zero is invented and the valid ordinary offer remains usable; mandatory price, party, itinerary and purchasable services retain their strict validation.

### Requirement: Limited completeness and temporal applicability
The system SHALL identify source observation, offer validity and before-departure scope separately. It SHALL disclose absent full text, after-departure, no-show and involuntary rules, without deriving fare deadlines from offer expiry or payment requirements.

#### Scenario: Full text not provided
- **WHEN** only structured conditions are available
- **THEN** they are identified as limited information, without claiming a summary of complete fare rules.

#### Scenario: First flight departed
- **WHEN** the earliest flight departure instant is reached
- **THEN** before-departure information is no longer presented as current applicable conditions and no after-departure rule is invented.

### Requirement: Explanation bound to the exact quoted offer
An active explanation SHALL match the displayed offer, quote revision, complete party, route and evidence fingerprint. Recalculation or replacement SHALL invalidate the prior active explanation even when price is unchanged. Reading or generating SHALL NOT extend validity.

#### Scenario: Same price and new revision
- **WHEN** recalculation keeps the price but changes revision or evidence
- **THEN** the old explanation loses active status and only the matching new result can be current.

#### Scenario: Late AI result
- **WHEN** a result arrives after another offer was selected or its quote expired
- **THEN** it cannot replace the current display or restore prior acceptance.

#### Scenario: Expiry while inference is pending
- **WHEN** the quote was valid at commit but offer expiry or first departure occurs before explanation response
- **THEN** the response marks its retained facts stale rather than current, preserves the ordinary quote outcome and does not submit another quote or financial operation.

#### Scenario: Concurrent replacement or unknown source freshness
- **WHEN** a competing recalculation or creation replaces the source context during inference, or final freshness cannot be checked within the deadline
- **THEN** the pending explanation is explicitly stale or unavailable and cannot claim the latest applicable quote conditions.

### Requirement: Truthful retained history
The system SHALL retain new confirmed quote evidence and read prior booking histories without rewriting them. Saved explanations SHALL be historical rather than current order conditions. Missing replacement evidence, including incomplete old recalculations, SHALL not inherit another revision's evidence.

#### Scenario: Historical bool-only quote
- **WHEN** saved data has only old fare flags or no fare data
- **THEN** it remains readable, but detailed conditions are unavailable and not reconstructed from flags or brand.

#### Scenario: Old recalculation lacks updated offer
- **WHEN** history changes price or revision without replacement evidence
- **THEN** the current explanation is unavailable rather than retaining earlier evidence as current.

#### Scenario: Saved conditions after hold
- **WHEN** an owner reads saved conditions after order creation
- **THEN** they are labelled as conditions recorded at quote time; a hold deadline does not extend offer validity.

#### Scenario: Reader or normalization version changes
- **WHEN** a retained new quote or recalculation is replayed after normalization or presentation code changes
- **THEN** its recorded evidence, revision, source observation and fingerprint retain their stored meanings without recomputation or source backfill.

### Requirement: Available deterministic explanation
Every successful supported quote SHALL provide a concise deterministic explanation or a specific unavailable result independent of model availability. Compact display SHALL preserve restrictions and unknowns with accessible complete scoped details. Accepting AI explanation SHALL NOT be required.

#### Scenario: No AI key or model disabled
- **WHEN** a valid quote is obtained without an available model
- **THEN** the structured explanation includes known fees and missing information.

#### Scenario: Complex scoped data
- **WHEN** evidence exceeds compact model or display limits
- **THEN** deterministic grouping and details retain applicable facts without silently truncating restrictions.

### Requirement: AI organizes only validated claims
The AI variant SHALL organize only supplied admissible claims. Displayed money, scope, permissions, deadlines and missing-information notices SHALL come from confirmed facts and controlled rendering. Unsupported, omitted or altered claims SHALL reject AI output and preserve deterministic explanation.

#### Scenario: Invented fee or claim
- **WHEN** output introduces another amount, currency, deadline or unadmitted claim
- **THEN** no invented information is displayed and the AI result is discarded.

#### Scenario: Incorrect grouping or omitted restriction
- **WHEN** output groups unequal facts, repeats a claim or omits a required restriction
- **THEN** the complete deterministic explanation is shown without a corrective model retry.

### Requirement: Untrusted input and private model payload
Supplier text SHALL NOT direct model or application behavior. Model input SHALL exclude profiles, identity documents, contacts, protected snapshots, user/provider/payment identifiers, tokens and secrets. Model output SHALL NOT execute markup, tools or business actions.

#### Scenario: Malicious supplier label
- **WHEN** a label includes instructions or markup
- **THEN** it is safely treated as display data, excluded from model instruction input and unable to invoke actions.

#### Scenario: Passenger details exist elsewhere
- **WHEN** the flow holds fictional profiles or protected party data
- **THEN** the captured model request contains only permitted condition facts and no passenger detail or secret.

### Requirement: Bounded optional inference and honest cost failure
AI SHALL be disabled by default until evaluated, require an explicit eligible organized quote request and satisfy deadline, size, cost and admission limits. Refusal, timeout, invalid output or accounting failure SHALL retain deterministic facts with an honest reason and no automatic inference retry.

#### Scenario: Budget or concurrency exceeded
- **WHEN** input size, estimated cost, deadline or admission capacity exceeds its bound
- **THEN** no model dispatch occurs and deterministic explanation remains available.

#### Scenario: Timeout or unknown usage
- **WHEN** inference times out or trustworthy usage is absent
- **THEN** the interface uses the deterministic result, no automatic retry occurs and unknown cost is not reported as zero.

#### Scenario: Failure after a possible model call
- **WHEN** accounting or reply delivery fails after inference may have occurred
- **THEN** caller, message processing and SDK do not automatically repeat inference; cost uncertainty and fallback remain separate from booking success.

#### Scenario: Anonymous organized request
- **WHEN** an anonymous quote requests optional AI mode
- **THEN** the ordinary quote remains available with deterministic explanation and no paid model dispatch.

### Requirement: Separate explanation from business authority
Reading or generating SHALL NOT create or confirm a booking, grant cancellation consent, create cancellation terms, dispatch money or release uncertainty. Existing financial contracts SHALL remain authoritative; a penalty or refundable flag SHALL not establish customer entitlement, payout or bank credit.

#### Scenario: Refunds are allowed
- **WHEN** an explanation contains a permitted before-departure refund condition
- **THEN** current cancellation terms and explicit consent are still required, with no automatic refund or credit promise.

#### Scenario: Unknown operation and repeated reads
- **WHEN** the page refreshes conditions while a financial operation is unresolved
- **THEN** its barrier and outcome stay unchanged and no financial or model operation is repeated.

### Requirement: Fare conditions do not establish ancillary refund rules
The system SHALL disclose missing service-specific change and refund rules when selected extras have no confirmed rule source. Fare permission or penalty SHALL NOT establish a seat or baggage refund, a grand-total refund or unchanged financial eligibility.

#### Scenario: Refundable fare with selected extras
- **WHEN** a quote includes seats or extra bags and only the base offer's fare conditions are known
- **THEN** the explanation identifies service refund rules as unprovided and does not extend the fare permission or fee to the extras.

#### Scenario: Zero fare penalty and priced services
- **WHEN** a zero fare penalty is known while selected services have a separate price
- **THEN** no free cancellation, service repayment or refund of the grand total is inferred; the existing current whole-order cancellation quote remains authoritative.

### Requirement: Existing private access and browser lifetime
Saved conditions SHALL use existing owner-authorized access and no-store responses. Anonymous quotes SHALL NOT expose public retained-history lookup. Identity changes, navigation and expiry SHALL invalidate callbacks; private state SHALL not enter browser storage, URLs or logs.

#### Scenario: Foreign or ownerless retained resource
- **WHEN** another owner or an anonymous requester reads saved conditions
- **THEN** existing concealed access rules apply before disclosure or model dispatch.

#### Scenario: Owner changes during pending response
- **WHEN** an earlier identity's response completes after logout or identity replacement
- **THEN** it cannot reveal history, restore acceptance or overwrite the new session.

### Requirement: Additive readers and scoped compatibility
Existing events, quote fields, financial fingerprints and readers SHALL remain readable. New readers SHALL treat absent evidence conservatively and reject malformed new evidence. Format compatibility SHALL NOT certify an older UI's two-state interpretation as a truthful new explanation.

#### Scenario: Old API response
- **WHEN** the client receives a valid old quote with no rich summary
- **THEN** the quote remains usable and explanation is explicitly unavailable.

#### Scenario: Invalid added summary
- **WHEN** a valid quote contains malformed or mismatched added summary
- **THEN** the summary is suppressed with a contract notice, without invented conditions or financial permission.

### Requirement: Measured value and separate acceptance evidence
Acceptance SHALL separate fictional proof, mandatory CI including PR E2E, authorized real-model evaluation and authorized runtime checks. AI enablement SHALL require factual safety and measured improvement over the baseline; failed evaluation SHALL retain the baseline with AI disabled.

#### Scenario: Fake model tests pass
- **WHEN** fictional checks validate safe rendering and rejected unsafe output
- **THEN** evidence claims modeled application behavior only, without real-model quality or live supplier/payment acceptance.

#### Scenario: Model adds no measured benefit
- **WHEN** an approved evaluation fails its factual, latency, cost or readability criteria
- **THEN** structured explanation remains and AI is neither enabled nor declared validated.

#### Scenario: Source CI passes
- **WHEN** ordinary checks and required PR E2E pass on an exact implementation head
- **THEN** source acceptance is recorded separately from model enablement and runtime proof, without declaring all M3 complete.

#### Scenario: Safe fallback in an eligible evaluation case
- **WHEN** a preclassified eligible holdout case times out, is skipped or uses deterministic fallback
- **THEN** application safety can pass, but the case is not a successful AI plan or an AI readability win; it remains in the eligible evaluation denominator.

#### Scenario: Insufficient or postselected comparison cases
- **WHEN** the fixed holdout lacks the required eligible mixed cases within real payload bounds or cases are selected after model output
- **THEN** AI usefulness acceptance is withheld rather than established from successful examples alone.
