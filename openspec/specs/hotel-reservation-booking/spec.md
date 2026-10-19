# Hotel reservation booking Specification

## Purpose

Let an authenticated owner accept local stay terms and obtain a truthful private booking result through one retained supplier attempt, including asynchronous facts and bounded recovery of details that arrive late.

## Requirements

### Requirement: Provider-neutral accepted purchase
Local quotes SHALL be Travel revisions of accepted terms, not mandatory supplier quote resources. Supported purchase behavior SHALL use normalized facts without requiring a particular supplier quote ID, HTTP code, funding name or webhook. Search-only sources SHALL not be required to buy. Equivalent supported adapters SHALL use the same application flow.

#### Scenario: Alternate synchronous adapter
- **WHEN** a small fictional adapter prepares a supported stay without a supplier quote ID and confirms synchronously without webhook or observation reader
- **THEN** the ordinary local quote, consent, protected admission and private result flow succeeds without a provider-name branch.

#### Scenario: Search-only source or unsupported purchase
- **WHEN** a source has no purchase capability or a selected purchase is unsupported
- **THEN** purchase is unavailable with a reason rather than fake methods, network effects or empty success.

### Requirement: Owner-bound reservation and quote
Quote creation/replacement, booking and private reads SHALL require validated owner and Hotels booking permission. Owner SHALL come from the principal. The reservation resource SHALL exist before supplier booking; foreign and missing resources SHALL be concealed equivalently before disclosure, decrypt or provider access.

#### Scenario: Foreign or unauthorized request
- **WHEN** a foreign user or caller lacking valid subject/permission targets a reservation, quote or action
- **THEN** no private facts, decrypt or provider effects occur and foreign existence is concealed.

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

### Requirement: Faithful cancellation and check-in disclosures
Review SHALL preserve source cancellation scopes/deadlines and mandatory check-in conditions. Unknown/malformed information SHALL not be invented. Documented explicit non-refundable evidence SHALL differ from missing evidence. Refund conditions SHALL not promise a customer payout or an available cancellation action.

#### Scenario: Known non-refundable and missing policy
- **WHEN** the adapter reports explicit non-refundable terms for one rate and unknown policy for another
- **THEN** the UI distinguishes them and retains the source scope without inventing deadlines.

#### Scenario: Timezone and restrictions
- **WHEN** confirmed conditions include a time-specific deadline and check-in restriction
- **THEN** both appear with their timezone/scope before consent, without changing local stay dates to browser-local dates.

### Requirement: Durable single booking dispatch
New dispatch SHALL obtain provider capacity before committing purchase, protected guests, attempt and durable claim. Safe capacity refusal SHALL not spend the creation barrier. Only the winning bounded local sender SHALL create once; new keys, expiry, abort or restart SHALL NOT reset a committed claim. Permits SHALL be released on failed commit, lost race or sender completion.

#### Scenario: Failed commit or lost admission race
- **WHEN** a new sender obtained capacity but its admission fails to commit or loses the conditional race
- **THEN** it sends nothing, releases its permit and does not reset any winning committed claim.

#### Scenario: Valid concurrent purchase with available capacity
- **WHEN** valid contenders target one purchase with capacity available to every contender and healthy store and fake supplier
- **THEN** exactly one create occurs and one retained attempt/result exists; all other contenders replay or conflict without additional effects, and zero creates cannot pass this proof.

#### Scenario: Safe capacity refusal before claim
- **WHEN** a new otherwise-valid purchase cannot obtain provider capacity
- **THEN** zero create requests and zero new claims occur, and the quote remains unsubmitted with its creation barrier unspent.

#### Scenario: Crash or lost client
- **WHEN** the process stops after the claim or the browser disconnects
- **THEN** the retained purchase remains discoverable; replacement processes observe/escalate without sending again, even when the first invocation might never have sent.

### Requirement: Retained action identity and safe replay
Matching authorized repeats SHALL resolve the retained attempt; changed content under the same identity SHALL conflict. Another identity SHALL not bypass a spent claim. Missing keys/receipt or elapsed retention SHALL not prove no effect. Even after no-creation, a new deliberate purchase requires a new resource, fresh terms and consent.

#### Scenario: Exact replay and changed guests
- **WHEN** the owner repeats accepted content or changes guests under its action ID
- **THEN** matching content returns its attempt/result and altered content conflicts, with zero extra create requests.

#### Scenario: Existing attempt replay without capacity
- **WHEN** an authorized matching repeat targets a retained attempt while provider capacity is exhausted
- **THEN** it resolves that attempt without requesting another provider permit, creating a new claim or dispatching again.

#### Scenario: Key outage or old identity after cleanup
- **WHEN** content cannot be safely checked or an old consumed identity is reused after private cleanup
- **THEN** no plaintext fallback or new create occurs; metadata/barrier remains available under its access rules.

### Requirement: Truthful supplier outcomes
Normalized outcomes SHALL distinguish confirmed reservation, trustworthy reservation without details, pending, positive no-creation and unknown. Review reasons SHALL remain separate from reservation facts. Transport/HTTP failure, absent resource, elapsed time or a later read error SHALL not erase positive facts or establish no-creation.

#### Scenario: Confirmed versus details pending
- **WHEN** attributable evidence confirms a complete booking or only establishes reservation without details
- **THEN** the first is confirmed and the second awaits details, without invented hotel reference or customer-payment proof.

#### Scenario: Pending and unknown
- **WHEN** the provider is still deciding or a response is lost/malformed after possible dispatch
- **THEN** pending or unknown remains explicit and cannot trigger another create.

#### Scenario: Conclusive no-creation
- **WHEN** positive attributable no-creation evidence exists and the original sender is finished without conflicting reservation evidence
- **THEN** the outcome is no booking for that attempt; a generic error or timeout alone cannot produce that result.

### Requirement: Accepted-to-actual matching and conflicts
Created facts SHALL be correlated to the retained purchase and compared with accepted stay, occupancy, property, rate scope, money and material terms. A confirmed actual booking with differences SHALL stay confirmed with review-required differences, not pretend accepted terms were fulfilled. No automatic cancellation, refund or replacement SHALL repair differences.

#### Scenario: Different stage identifiers
- **WHEN** source evidence uniquely correlates a booking whose offer/preparation/booking identifiers differ
- **THEN** it can be recognized without requiring those identifiers to be equal or requiring a supplier quote ID.

#### Scenario: Confirmed booking differs
- **WHEN** an attributable active booking has different dates, board or money
- **THEN** actual confirmation and accepted-to-actual differences are shown together with review required and no replacement effects.

### Requirement: Authenticated correlated supplier observations
Where notifications are supported, their raw authenticity, account/environment and event semantics SHALL be verified. Owner-module observation and processing work SHALL commit atomically with durable deduplication. Unknown/ambiguous references SHALL not attach by name, amount, time proximity or latest resource.

#### Scenario: Early or repeated creation observation
- **WHEN** an authenticated event arrives before the direct response or is delivered repeatedly
- **THEN** exact saved source correlation finds the retained attempt and no duplicate business transition is produced.

#### Scenario: Invalid or uncorrelated event
- **WHEN** signature/account/mode is invalid or purchase linkage is not established
- **THEN** it cannot establish an owned outcome or release the purchase barrier; unmatched owned evidence is handled by the routing contract.

#### Scenario: Observation commit fails
- **WHEN** observation and processing work cannot commit atomically
- **THEN** durable acceptance is not reported and partial processing is not treated as complete.

### Requirement: Monotonic evidence and late events
Positive facts SHALL survive stale reads, duplicates and conflicting failures. Verified cancellation SHALL make the booking cancelled, retain creation history and imply no payout. Complete late facts SHALL clear only resolved waiting/missing-detail reasons. Material mismatch or contradictory evidence SHALL retain review; old creation evidence SHALL NOT reactivate cancellation.

#### Scenario: External cancellation and delayed creation
- **WHEN** exact cancellation is established and an older creation/confirmed observation for that booking arrives later
- **THEN** it remains cancelled and not active, with creation history retained and no refund claim.

#### Scenario: Review resolves or persists
- **WHEN** complete matching late facts resolve deadline-only uncertainty, or conflict with an earlier no-creation/positive fact
- **THEN** only the first clears resolved waiting reasons; contradictory facts or material differences remain review-required despite later ordinary matching reads.

### Requirement: Bounded observation and explicit manual limit
Escalation time SHALL change review need rather than forbid new trustworthy facts. The first known booking identity SHALL receive one finite durable read allowance/window, including when first learned after escalation. Duplicates/restart/another event SHALL not reset it; a different identity SHALL require review. Unknown identity SHALL cause no supplier reads or list discovery; create SHALL never repeat.

#### Scenario: Late identity after escalation
- **WHEN** a reservation-without-details result reaches its escalation deadline and a trustworthy first booking identity arrives hours later
- **THEN** limited detail retrieval is admitted under the one retained identity budget, without creating again; complete facts can resolve deadline/missing-detail review.

#### Scenario: Three reads and repeated events
- **WHEN** a configured three-read allowance is exhausted or a read claim is followed by restart and duplicate creation events
- **THEN** counters and read-window end remain unchanged and neither fresh polling nor another three-read allowance starts.

#### Scenario: Unknown identity or no reader capability
- **WHEN** no trustworthy booking ID is known or the adapter has no observation capability for an inconclusive purchase
- **THEN** escalation requires review without futile supplier reads, owner reset, list scans or automatic resend.

### Requirement: Protected minimal guest details
M1 SHALL collect only required guest names/lead contacts and adulthood/check-in confirmations. Private content SHALL be protected before durable storage/messaging and bound to owner, reservation, revision and party. Authorization SHALL precede decrypt. Missing protection SHALL fail closed without plaintext or normal-startup key bootstrap.

#### Scenario: Missing keys or modified envelope
- **WHEN** protection is unavailable or a stored envelope is tampered with or copied across bindings
- **THEN** no guest-bearing purchase is sent and no plaintext fallback is stored or returned.

#### Scenario: Unneeded information
- **WHEN** the traveller enters guest details
- **THEN** passports, birth dates, cards, saved profiles, medical details and free-form special requests are not requested.

### Requirement: Private authoritative reads and browser lifetime
Owner reads SHALL expose retained local metadata without supplier calls, guest decrypt or shared caching. Private content SHALL not enter SSR/browser storage/URLs/diagnostics; only resource identity supports navigation. Logout, permission/identity loss and stale completions SHALL clear private UI without releasing uncertain purchases.

#### Scenario: Reload or sign-in after response loss
- **WHEN** the owner reloads or signs in again after an admitted action loses its reply
- **THEN** detail/list reads recover the reservation and truthful status without repeating book or calling the supplier.

#### Scenario: Identity change and outages
- **WHEN** an old private response completes after logout/scope loss, or supplier/PII keys are unavailable
- **THEN** old content/consent cannot return, while authorized key-independent metadata remains readable without inventing failure of the original purchase.

### Requirement: Bounded retention without replay revival
Draft and completed private content SHALL expire under declared retention and be physically removed by bounded idempotent cleanup without decrypt. Unresolved purchase identities/barriers SHALL not expire automatically. Private cleanup SHALL preserve minimal consumed identities and correlation needed to prevent duplicate purchase; independent receipt/journal/tombstone systems SHALL not be required.

#### Scenario: Private content cleanup
- **WHEN** configured draft or completed-content retention expires
- **THEN** content is unavailable and bounded cleanup removes it while retained minimal identity cannot be replayed as a new create.

#### Scenario: Still-unresolved purchase
- **WHEN** ordinary retention time passes but the outcome is unresolved
- **THEN** its uncertainty and spent claim remain, rather than disappearing or enabling resend.

### Requirement: Safe correlation and diagnostic recovery
Private diagnostic evidence SHALL retain local attempt/resource, provider/environment, available request/source references, time and fixed outcome classification. It SHALL exclude raw errors, guest/contact data, tokens and arbitrary headers. A short read-only runbook SHALL support unknown-outcome investigation without a resolver/admin UI, direct DB reset or repeated creation.

#### Scenario: Lost response investigation
- **WHEN** an operator examines retained safe metadata for an unknown attempt
- **THEN** known provider request/preparation/booking references support investigation and missing references remain unknown, without exposing raw private payload or issuing another create.

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
