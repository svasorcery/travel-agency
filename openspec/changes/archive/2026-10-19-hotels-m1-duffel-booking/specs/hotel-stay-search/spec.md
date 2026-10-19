# Hotel stay search delta

## Purpose

Let a traveller search and select an exact hotel stay through capability-aware providers while preserving availability, completeness and failure information from execution to cache and UI.

## ADDED Requirements

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
M1 SHALL use one external Duffel Stays source. Request support SHALL be checkable without network access; preflight and execution guards SHALL use the same rule and reason. Unsupported/skipped, successful-empty, attempted failure and limited/partial execution SHALL remain distinct in handler, snapshot, API and UI. No failure SHALL silently substitute another provider or fictional data.

#### Scenario: Preflight and direct unsupported execution
- **WHEN** a domain-valid two-adult request targets a test provider supporting only one adult
- **THEN** preflight reports the skip reason, direct execution reports that same unsupported reason, and neither makes a supplier call or returns empty success.

#### Scenario: Empty versus failure or partial inventory
- **WHEN** execution returns complete empty inventory, an attempted failure or limited results including zero offers
- **THEN** each meaning is preserved and displayed separately; limited zero results are not complete empty success.

#### Scenario: Disabled or unconfigured source
- **WHEN** the source is disabled or unavailable by configuration
- **THEN** its skipped/unavailable reason differs from attempted provider failure and successful no-inventory.

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
Indicative results SHALL bind supplier/environment, stay, occupancy and supplied validity. Snapshots SHALL preserve the complete execution meaning with offers, source statuses, limitations and expiry. Changed criteria, stale responses, eviction or restart SHALL NOT revive an old selection or modify an admitted purchase; booking requires a current local quote.

#### Scenario: Full snapshot round-trip
- **WHEN** complete, skipped, failed and limited result fixtures pass through snapshot/cache
- **THEN** API/UI receive the same statuses, reasons and completeness rather than reconstructing missing evidence from offers.

#### Scenario: Stale response or expired cache
- **WHEN** an old response arrives after changed dates/guests or its snapshot expires/is evicted
- **THEN** it cannot replace current selection; a fresh search is requested without affecting a saved booking attempt.

#### Scenario: Source identity substituted
- **WHEN** a selection is changed to another source/environment or unissued provider identifiers
- **THEN** it is refused before preparation or booking effects.

### Requirement: Eligibility and source restrictions
Known rate eligibility and supplier content restrictions SHALL be preserved. M1 SHALL NOT imply unsupported membership/negotiated/closed-user eligibility. Real supplier data SHALL not become public fixtures or a cross-source mapping dataset without applicable permission.

#### Scenario: Unsupported restricted rate
- **WHEN** a rate requires qualification M1 cannot establish
- **THEN** it cannot be purchased as an unrestricted public rate.

#### Scenario: Images and public fixtures
- **WHEN** the UI displays permitted supplier image links or repository demos are prepared
- **THEN** links remain remote and public reusable fixtures use fictional values and owned assets.

### Requirement: Bounded safe search interaction
Requests, response materialization, concurrency and snapshot storage SHALL be bounded. Refusal, invalid/truncated response and limited inventory SHALL be explicit without raw-value diagnostics. Eviction SHALL remove a whole snapshot. Supplier text SHALL remain inert display data, not executable markup or instructions.

#### Scenario: Excessive or malformed data
- **WHEN** requests exceed configured bounds or contain duplicate/unknown fields, or a response cannot be safely decoded
- **THEN** fixed errors occur without input echo, extra supplier dispatch or fabricated complete empty inventory.

#### Scenario: Entry and byte limits
- **WHEN** full search/rates snapshots reach configured count or accounted-payload limits
- **THEN** refusal or whole-entry eviction preserves result meaning; a hotel/card is not miscounted as a snapshot entry and admitted purchases remain independent.

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
