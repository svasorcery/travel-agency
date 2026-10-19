# Hotel reservation booking M2 delta

Delta against the accepted [hotel-reservation-booking main contract](../../../../specs/hotel-reservation-booking/spec.md), synced from M1 with owner authorization on2026-10-19. Reconciled against that baseline; M2 is not applied to main. Unmentioned owner/privacy/retention/outcome/recovery/single-dispatch requirements remain authoritative. Duffel webhook routing is unchanged.

## MODIFIED Requirements

### Requirement: Exact current purchase consent
Local quote revision SHALL bind source/account/environment/execution profile, rate/scope, money and material terms. M2 SHALL persist normalized AdmissionBinding outside accepted terms and recheck current approval/config/mapping/validity before a new claim, independently of cache. Change SHALL invalidate unsubmitted consent even at the same price. Replacement SHALL NOT overwrite an admitted purchase.

#### Scenario: Same-price changed terms
- **WHEN** refresh changes board coverage or cancellation rules without changing money
- **THEN** changes are shown and fresh consent is required.

#### Scenario: Replacement races with booking
- **WHEN** replacement and booking target one expected revision
- **THEN** only a valid winning transition proceeds and an admitted purchase cannot be replaced or sent under expired consent.

#### Scenario: Same hotel and price different supplier
- **WHEN** the user selects the other supplier's offer before admission, even on the same card at the same price
- **THEN** a new source-bound quote revision and explicit consent are required; canonical property or ranking cannot substitute its context.

#### Scenario: Approval revoked after preparation
- **WHEN** actual M2 was permitted at Quote but its approval is absent/revoked before a new Purchase
- **THEN** the stored AdmissionBinding fails the current admission guard with0 creates/0 claims; an existing admitted attempt still replays locally without a new admission permit.

#### Scenario: Execution profile or account changes
- **WHEN** a quoted fictional context would be used by actual transport, or current source account/environment/pricing configuration differs from its binding
- **THEN** new admission refuses instead of dispatching the old context through the new source configuration, even if names and money match.

#### Scenario: Quote survives search cache loss
- **WHEN** a prepared quote remains valid after cache eviction or process restart
- **THEN** its stored normalized binding supplies admission checks without cache or supplier-context parsing; changed/expired material binding refuses, while legacy M1 follows its original recognized scope.

### Requirement: Supported test purchase and complete obligations
M2 SHALL admit only supported immediate test purchases with known debit/local obligations and eligibility, without cards/customer PSP. Hotelbeds SHALL be limited to the documented Net/AT_WEB, one-room/1–2-adult unrestricted subset. Unsupported payment/profile/qualification or unknown material obligations SHALL refuse dispatch. Production booking and customer money movement SHALL remain unavailable.

#### Scenario: Review supported purchase
- **WHEN** normalized terms are within the supported test subset
- **THEN** the UI shows the whole stay, guests, exact debit, separate local payments and restrictions before submission, independent of adapter funding vocabulary.

#### Scenario: Unknown mandatory money or unsupported payment
- **WHEN** mandatory local money is unknown or the rate needs cards, deposit scheduling or unsupported qualification
- **THEN** no booking is dispatched and the reason is visible without treating unknown as zero.

#### Scenario: Hotelbeds profile outside first slice
- **WHEN** a Hotelbeds rate needs hotelMandatory selling-rate enforcement, commissionable/unknown profile, AT_HOTEL, packaging, special qualification or additional mandatory private inputs
- **THEN** purchase is explicitly unsupported without invented retail price, customer fields, source substitution or Book dispatch.

### Requirement: Complete fictional UI and separate acceptance
M2 SHALL provide complete one-/two-adult search-to-private-result UI for Duffel and Hotelbeds, exact source selection, condition review, truthful pending/unknown/cancelled states and reload recovery. Workflow jargon SHALL not become customer text. Source/fictional/load/sandbox/live evidence SHALL stay distinct; external test profiles SHALL default off and be excluded from Production.

#### Scenario: Complete flow and understandable status
- **WHEN** isolated browser cases exercise purchase, changed terms, lost reply, external cancellation and identity changes
- **THEN** exact price/obligations/conditions and factual state are readable without claim/inbox/reconciliation jargon, fictional payment proof or automatic resend.

#### Scenario: Missing configuration and evidence limits
- **WHEN** capability configuration is absent, Production attempts to enable a test adapter, or planning/source/load checks pass
- **THEN** unsafe activation fails explicitly and evidence reports only its proven scope, without real integration/keys/schema/payment/rollout claims.

#### Scenario: Second supplier complete path
- **WHEN** fictional HTTP fixtures drive Hotelbeds Availability, required CheckRate/conditions and one Booking through the ordinary browser flow
- **THEN** the owner can select, review, consent, submit, read/reload its source-bound private result while Duffel happy path still works, and no real supplier request occurs.

## ADDED Requirements

### Requirement: Hotelbeds protocol stays behind Infrastructure
Hotelbeds DTOs/auth/mTLS/error mapping and opaque rateKey SHALL remain in Infrastructure. Prepare SHALL only perform non-purchasing freshness/conditions work; Create SHALL be the sole Booking effect. Public selections SHALL expose no rateKey. A documented supported protocol SHALL use the ordinary provider-neutral application flow and existing Api.Composition boundary.

#### Scenario: RECHECK and opaque context
- **WHEN** a rate requires recheck or material conditions are incomplete
- **THEN** preparation obtains and validates fresh facts before consent; exact opaque rateKey is retained only in bounded versioned protected context without parsing it or treating clientReference as proven idempotency.

#### Scenario: Transport requirements and missing evidence
- **WHEN** actual Hotelbeds transport is composed or a required wire semantic cannot be established from the pinned official subset
- **THEN** key/signature/client-certificate requirements fail closed, raw values are not logged, and missing protocol evidence is surfaced rather than filled with guessed fields; fictional transport proves neither real mTLS nor account rights.

### Requirement: Source-specific dispatch capacity
Each supplier SHALL have separate finite budgets. Application SHALL acquire Create capacity before claim; the runner SHALL acquire recovery capacity; adapters SHALL not reacquire those leases. Read steps SHALL acquire their own leases. Hotelbeds HTTP timeout SHALL be90s, Duffel140s; application145s cancellation and10s completion-store budgets SHALL remain distinct, with no retry/hedging or no-creation inference.

#### Scenario: Other provider saturated
- **WHEN** Duffel capacity is exhausted but Hotelbeds capacity is available and vice versa
- **THEN** the selected-source purchase can obtain only its own capacity, while a refusal creates no claim/effect and does not silently route to the other supplier.

#### Scenario: Purchase contender race
- **WHEN** valid contenders have sufficient capacity and a healthy store/fictional supplier
- **THEN** one retained attempt and exactly one selected-source Booking occur, losers release capacity/replay, and zero creates cannot satisfy the proof.

#### Scenario: Lease ownership and deadline separation
- **WHEN** Create or known-reference recovery runs with one available source slot, or a controlled-clock HTTP deadline expires
- **THEN** the adapter does not reject itself by acquiring the same slot twice, each permit releases exactly once after its underlying operation completes, and HTTP90/140, application145 and completion10 budgets are not conflated into an end-to-end guarantee or repeat permission.

### Requirement: No cross-source replacement after uncertain purchase
An admitted purchase SHALL retain its selected source/account/environment/context regardless of grouping, ranking, expiry or source disablement. Unknown/pending/transport loss SHALL never trigger automatic replacement, another source's Create or a new key bypass. UI SHALL explain uncertainty and duplicate-booking risk without offering retry-as-new-source as recovery.

#### Scenario: Lost Hotelbeds booking response
- **WHEN** Booking may have been sent but no trustworthy booking reference is known
- **THEN** outcome is Unknown with retained spent barrier, zero BookingList/discovery/resend/failover requests, and private reload returns that local state.

#### Scenario: Cheaper alternate after admission
- **WHEN** another source later offers the same mapped hotel cheaper or the admitted writer is disabled
- **THEN** the retained purchase remains tied to its original source with no fresh create, cancellation or reassignment.

#### Scenario: Source choice keeps the purchase resource
- **WHEN** the same purchase flow changes source before admission or encounters an uncertain outcome afterwards
- **THEN** source replacement uses its existing reservation ID before admission; after uncertainty UI opens the retained result without issuing another resource/action to continue that purchase, without claiming global deduplication of independent bookings.

### Requirement: Hotelbeds bounded known-reference observation
Hotelbeds optional reader SHALL use only an attributable known booking reference under the existing durable3-read/5-minute allowance. Pending/missing details, conflicting actual facts and no-reference Unknown SHALL retain M1 outcome semantics. A read timeout/not-found SHALL not prove no-creation or reset allowance; no webhook SHALL be invented.

#### Scenario: Known reference without details
- **WHEN** direct evidence establishes a reservation reference without full facts
- **THEN** limited persisted reads can complete details or leave review required, without another Booking and without owner GET making supplier calls.

#### Scenario: Actual facts differ or reads exhaust
- **WHEN** actual money/property/conditions differ, a read fails, or three reads are consumed across restart
- **THEN** positive reservation evidence and material review survive, allowance is not reset, and no cancellation/refund/replacement is dispatched.

#### Scenario: Recovery account or profile differs
- **WHEN** current reader transport/account/environment differs from the admitted source binding
- **THEN** recovery reports ObservationUnavailable/review with0 supplier reads under the wrong binding, while private metadata/barrier survives and no new create occurs.

### Requirement: Preserve legacy accepted state and private boundaries
New normalized metadata SHALL not reinterpret legacy Duffel accepted JSON/context or change its consent/actual matching through invented defaults. Old absent comparison facts SHALL remain unknown/legacy for display. Existing authorization, protection, retention, local owner reads, browser epochs and Duffel receiving/drain SHALL remain intact; no new mandatory guest fields SHALL be fabricated.

#### Scenario: Legacy private reservation reload
- **WHEN** a saved M1 reservation lacks M2 comparison metadata and provider/guest keys are unavailable
- **THEN** authorized metadata/outcome remains readable locally, accepted history/barrier stays unchanged and no supplier call, guest decrypt or new create occurs.

#### Scenario: Logout and writer disable
- **WHEN** a stale private response arrives after logout or booking is disabled with unresolved attempts
- **THEN** UI guest/consent state cannot return, while retained authority and already required read/receive/drain remain available under their access rules.

#### Scenario: Missing binding is not an M2 bypass
- **WHEN** an old recognized Duffel/context-v1 reservation lacks new metadata or a new M2 quote lacks its required v2 AdmissionBinding
- **THEN** only the former uses unchanged M1 legacy rules; the latter refuses new admission rather than treating missing metadata as a permissive legacy default.
