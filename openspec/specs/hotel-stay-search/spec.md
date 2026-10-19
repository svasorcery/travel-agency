# Hotel stay search Specification

## Purpose

Let a traveller search and select an exact hotel stay through capability-aware providers while preserving availability, completeness and failure information from execution to cache and UI.

## Requirements

### Requirement: Supported stay and occupancy
M1 SHALL support one room for one or two adults, future check-in dates and exclusive checkout within configured admission bounds. Stay dates SHALL retain accommodation-local calendar meaning. Invalid occupancy, children, multiple rooms or non-increasing dates SHALL be refused before supplier access.

#### Scenario: Calendar nights and intake boundaries
- **WHEN** valid requests cross a daylight-saving change or reach configured date/night bounds
- **THEN** validation counts calendar nights, retains exact dates/occupancy and uses the configured clock-based admission policy rather than elapsed UTC hours.

#### Scenario: Unsupported M1 party
- **WHEN** a request includes zero or three adults, a child or multiple rooms
- **THEN** it is refused without a supplier call or silently changing the requested party.

### Requirement: Bounded geographical destination search
The UI SHALL offer a small curated destination list with bounded geographic criteria and visibly separate fictional destinations. Search SHALL NOT require paid geocoding, device location or a global supplier catalogue import.

#### Scenario: Normal destination selection
- **WHEN** a traveller chooses a supported destination and submits valid stay criteria
- **THEN** the UI searches that disclosed area without requesting geolocation or paid location services.

#### Scenario: Invalid location input
- **WHEN** a caller supplies invalid coordinates, a disallowed radius or a provider URL
- **THEN** it is rejected before supplier access rather than becoming an arbitrary network fetch.

### Requirement: Explicit supplier availability and failure
M2 SHALL plan Duffel Stays and Hotelbeds as two external sources. Offline support and execution guards SHALL agree. Unsupported/skipped, complete-empty, attempted failure and limited execution SHALL remain distinct in handler, snapshot, API and UI. Each source SHALL retain kind, fixed reason and attempted flag. No failure SHALL substitute another source or fictional data.

#### Scenario: Preflight and direct unsupported execution
- **WHEN** a domain-valid two-adult request targets a test provider supporting only one adult
- **THEN** preflight reports the skip reason, direct execution reports that same unsupported reason, and neither makes a supplier call or returns empty success.

#### Scenario: Empty versus failure or partial inventory
- **WHEN** execution returns complete empty inventory, an attempted failure or limited results including zero offers
- **THEN** each meaning is preserved and displayed separately; limited zero results are not complete empty success.

#### Scenario: Disabled or unconfigured source
- **WHEN** a source is disabled or unavailable by configuration
- **THEN** its skipped/unavailable reason differs from attempted provider failure and successful no-inventory, and the other source may return its own result with incomplete aggregate coverage.

#### Scenario: Truthful zero-result aggregation
- **WHEN** the two sources return Complete0/Complete0, Complete0/Timeout, Limited0/Failed, Failed/Skipped or Skipped/Skipped
- **THEN** aggregate kinds are respectively Complete, Limited, Limited, Failed and Unsupported, regardless of offer count, with both source statuses retained.

#### Scenario: Local refusal and supplier refusal
- **WHEN** local capacity prevents a dispatch or the supplier returns a rate limit/authentication/transport/decode failure
- **THEN** the first is Failed/LocalCapacity with attempted=false, and attempted supplier outcomes retain different fixed reasons without raw private response text.

### Requirement: Property room and rate distinction
Property, supplier-specific room description and rate terms SHALL remain separate. Names or coordinates SHALL NOT establish property/room equivalence. Stars, review scores, beds, board and benefits SHALL preserve their source scales, guest scopes and unknowns.

#### Scenario: Similar room with different rates
- **WHEN** room-only and breakfast rates have similar room names
- **THEN** they remain separately selectable with their payment/cancellation terms and actual breakfast guest scope.

#### Scenario: Missing source facts
- **WHEN** bed details or review scores are absent
- **THEN** they remain unavailable rather than becoming guaranteed beds, zero scores or another source's facts.

### Requirement: Whole-stay exact prices and separate obligations
Prices SHALL identify whole-stay occupancy scope and currency. Included breakdown components SHALL NOT be counted twice. Mandatory local payments SHALL be separate in their actual currencies; unknown SHALL differ from zero. No implicit FX or repeated night/guest multiplication SHALL change the purchase amount.

#### Scenario: Group stay and included tax
- **WHEN** GBP200 covers two adults and two nights including GBP20 tax
- **THEN** the whole-stay total remains GBP200 rather than GBP220, GBP400 or GBP800.

#### Scenario: Separate local payment
- **WHEN** EUR20 is additionally due at accommodation
- **THEN** GBP200 and EUR20 are shown separately without a fabricated converted grand total.

#### Scenario: Missing local amount
- **WHEN** one rate has an unknown mandatory local payment and another an explicit zero
- **THEN** the two are not shown as equivalent or both free.

### Requirement: Current selection and indicative availability
Versioned immutable snapshots SHALL retain criteria, source leaves, groups, statuses, limits, ranking version and expiry. Issued local tokens SHALL resolve exact source/account/environment/profile and rate scope through a bounded server-only handle in the same entry. Public projections SHALL expose no opaque context. Stale, substituted or evicted selections SHALL not prepare a quote or alter an admitted purchase.

#### Scenario: Full snapshot round-trip
- **WHEN** complete, skipped, failed and limited result fixtures pass through snapshot/cache
- **THEN** API/UI receive the same statuses, reasons and completeness rather than reconstructing missing evidence from offers.

#### Scenario: Stale response or expired cache
- **WHEN** an old response arrives after changed dates/guests or its snapshot expires/is evicted
- **THEN** it cannot replace current selection; a fresh search is requested without affecting a saved booking attempt.

#### Scenario: Source identity substituted
- **WHEN** a selection is changed to another source/environment or unissued provider identifiers
- **THEN** it is refused before preparation or booking effects.

#### Scenario: Colliding supplier identifiers
- **WHEN** Duffel and Hotelbeds return the same textual property or rate ID
- **THEN** their local tokens resolve different complete source namespaces, and neither token can purchase the other offer.

#### Scenario: Group rates and exact quote selection
- **WHEN** rates is requested with a valid propertyGroupKey instead of selectionKey
- **THEN** at most its two verified source variants are read under aggregate budgets and the frozen rates snapshot retains all source results; quote still requires an exact issued rate selectionKey.

#### Scenario: Invalid selector shape
- **WHEN** a rates request supplies both selectors, neither selector or an unissued group/token
- **THEN** validation rejects it without supplier access; the existing searchId/selectionKey shape remains supported.

#### Scenario: Opaque selection context remains private
- **WHEN** Availability or CheckRate returns an opaque Hotelbeds rateKey
- **THEN** the exact key is retained in the entry's server-only normalized handle for the selected source/profile, never in public RateId, JSON, URLs or diagnostics; a key over4096 UTF-8 bytes excludes that offer with Limited/ContextTooLarge without truncation.

#### Scenario: Private context eviction
- **WHEN** a whole entry expires or is evicted before Prepare
- **THEN** its public selection and private handle disappear together; no unbounded adapter dictionary revives them, while an admitted reservation uses its own protected durable context.

### Requirement: Eligibility and source restrictions
Known rate eligibility and supplier content restrictions SHALL be preserved. M1 SHALL NOT imply unsupported membership/negotiated/closed-user eligibility. Real supplier data SHALL not become public fixtures or a cross-source mapping dataset without applicable permission.

#### Scenario: Unsupported restricted rate
- **WHEN** a rate requires qualification M1 cannot establish
- **THEN** it cannot be purchased as an unrestricted public rate.

#### Scenario: Images and public fixtures
- **WHEN** the UI displays permitted supplier image links or repository demos are prepared
- **THEN** links remain remote and public reusable fixtures use fictional values and owned assets.

### Requirement: Bounded safe search interaction
Requests, materialization, concurrency and storage SHALL obey the finite M2 profile in design D3. Refusal, invalid responses and truncation SHALL be explicit. Whole-entry eviction SHALL preserve execution meaning. Supplier text SHALL remain inert. Bounds SHALL not truncate accepted material terms or permit unbounded work after cancellation.

#### Scenario: Excessive or malformed data
- **WHEN** requests exceed configured bounds or contain duplicate/unknown fields, or a response cannot be safely decoded
- **THEN** fixed errors occur without input echo, extra supplier dispatch or fabricated complete empty inventory.

#### Scenario: Entry and byte limits
- **WHEN** full search/rates snapshots reach configured count or accounted-payload limits
- **THEN** refusal or whole-entry eviction preserves result meaning; a hotel/card is not miscounted as a snapshot entry and admitted purchases remain independent.

#### Scenario: Raw body and item bounds
- **WHEN** a source exceeds2MiB raw body or a valid response exceeds100 properties/200 variants
- **THEN** oversized raw data yields ResponseTooLarge without partial JSON use, while valid item reduction uses a stable prefix and Limited with reason; aggregate retains at most100 groups/200 leaves/2MiB.

#### Scenario: Whole-group byte reduction
- **WHEN** aggregate bytes exceed the public bound
- **THEN** the2MiB bound counts the entire public-plus-private entry, whole ranked groups and their contexts are removed deterministically with Limited, both source statuses remain, and a minimum envelope that cannot fit yields a fixed error without a snapshot.

#### Scenario: Expiry at freeze
- **WHEN** an offer expires during search or the full cache reaches128 entries/16MiB accounted key-plus-payload bytes
- **THEN** all private context bytes count toward the same limit, expired variants cannot be issued, retained TTL is at most300s and source validity, and whole-entry eviction never revives a selection or releases a purchase barrier.

### Requirement: Useful bounded single-process resilience
Acceptance SHALL include a predeclared reproducible fake-supplier load profile with normal work, delayed/errors, concurrent burst and recovery. Evidence SHALL report useful throughput, p95, all refusals and resources. Admission/cache/queue bounds SHALL hold; search load SHALL not starve private status or admitted observations. Rejecting all work SHALL not establish useful capacity.

#### Scenario: Short resilience run
- **WHEN** the fixed disposable profile is executed
- **THEN** normal/recovery meet predeclared useful-success gates, provider budgets/concurrency and memory remain bounded, and private reads/observation admission stay responsive; available-capacity purchase concurrency is proven separately rather than by overload refusals.

#### Scenario: Overload and evidence denominator
- **WHEN** overload produces429 or degraded replies
- **THEN** all offered requests remain in metrics, the result is compared with unchanged gates and useful service recovers after load removal; failed gates are not hidden by postselection or raised limits.

#### Scenario: M1 completion with an explicitly accepted memory limitation
- **WHEN** the owner explicitly accepts the memory-check limitation on2026-10-17 and requests completion of this M1 without new runs
- **THEN** M1 source/fictional acceptance may be completed with the documented limitation while preserving the measured16/17 result, `workingSetRecovery=false`, overall `passed=false` and exit1
- **AND** the failed check is not reported as passed, thresholds/profile/raw evidence are unchanged, the separate diagnostic run does not replace acceptance, and no claim of leak absence, hard2GiBreference capacity or target/supplier readiness follows from completion
- **AND** this exception applies only to completion of this M1; the owner decision and rationale are retained in the same change's verification record.

### Requirement: Bounded parallel read execution
The two eligible source reads SHALL run independently with15s per-source and17s aggregate deadlines, no retry/hedging and no unbounded queue. Module admission SHALL allow at most two aggregate requests. Cancellation SHALL cancel child reads and prevent late snapshot mutation while permits remain held until actual operation completion.

#### Scenario: One delayed source
- **WHEN** one fictional source completes while the other exceeds its deadline
- **THEN** the first result is retained, the second is Timeout and aggregate is Limited, within the17s bound under a controlled clock; elapsed time is not the sum of two source budgets.

#### Scenario: Caller cancellation and uncooperative transport
- **WHEN** the caller cancels or a child ignores cancellation beyond the aggregate deadline
- **THEN** no late completion mutates current/frozen state, cancelled interaction issues no usable snapshot, and still-running child work retains its bounded permit rather than enabling unlimited detached work.

#### Scenario: Independent provider and recovery admission
- **WHEN** one source foreground allowance is exhausted
- **THEN** it cannot consume the other's separate1-active/24-starts-per-minute allowance, each source recovery retains separate1-active/6-starts-per-minute capacity, and local private GET invokes neither source.

#### Scenario: Multi-step source read shares one deadline
- **WHEN** one Search/Rates/Prepare execution requires more than one read HTTP step
- **THEN** all steps share its original15s deadline, every next step uses only the remaining time, and success cannot be obtained by resetting15s for each call or borrowing the90s Create HTTP timeout.

### Requirement: Autonomous source profile and external-use gate
M2 SHALL provide actual Duffel and Hotelbeds adapter implementations with explicitly configured fictional transports for autonomous verification. Both external test profiles SHALL default off and be excluded from Production. Actual aggregation SHALL remain disabled without separate access/rights approval; fictional fixtures SHALL not establish supplier permission or become error fallback.

#### Scenario: No account or secrets
- **WHEN** source-only acceptance uses owned fictional property/guest/wire fixtures
- **THEN** no supplier request, real credential, certificate issuance or paid service is needed; evidence identifies fictional transport and does not claim real sandbox acceptance.

#### Scenario: Actual mixed-source configuration
- **WHEN** actual aggregation is configured without separately approved applicable access, mapping/comparison rights and profile
- **THEN** no mixed-source supplier dispatch occurs and the gate reason is explicit; this does not disable isolated accepted M1 behavior or erase admitted recovery.
