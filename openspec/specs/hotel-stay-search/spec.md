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

#### Scenario: Independent M2 load acceptance
- **WHEN** M2 source/local tests or ordinary CI pass, or the M1 owner limitation is cited
- **THEN** M2 load and memory acceptance SHALL remain unproven until a separately approved frozen two-source fictional profile meets its explicitly versioned, approved mandatory gates and retains the historical classification of all earlier runs
- **AND** controlled-clock concurrency/deadline evidence SHALL NOT be represented as measured throughput, p95 or memory recovery.

#### Scenario: Complete source and campaign accounting
- **WHEN** an approved campaign offers search, grouped rates, selected-source purchase, private status and observations under normal, fault, burst and recovery work
- **THEN** every predeclared job and profile SHALL retain a terminal classification including failed, refused, cancelled, unfinished and not-run, with useful throughput, latency sample counts and unavailable values reported explicitly
- **AND** each provider's actual HTTP starts, local admissions, active operations and protected recovery allowance SHALL be accounted separately; source failures or empty/limited outcomes SHALL NOT become healthy success merely through HTTP200.

#### Scenario: Protocol-faithful fictional inventory
- **WHEN** a load profile uses owned fictional transports and a bounded configured property inventory
- **THEN** it SHALL use the supported fictional destination without weakening source/profile guards, and each supplier request SHALL select the same approved property scope that its fixture may return
- **AND** Hotelbeds fictional inventory SHALL contain1–100 unique positive property IDs, with the existing single-property default preserved; invalid inventory SHALL fail before dispatch, and external transport/request behavior SHALL remain unchanged
- **AND** multi-property success SHALL NOT be established by returning unrequested hotels to a single-property request.

#### Scenario: Fixed phase and recovery window have separate cutoffs
- **WHEN** a purchase completes its HTTP response before a fixed phase ends but authoritative confirmation is observed afterwards
- **THEN** fixed-phase success and rolling-window success SHALL be calculated separately from the retained timestamps; failure of the former SHALL NOT permanently exclude the purchase from a later eligible recovery window
- **AND** a purchase offered at284.5s, with HTTP complete at285s and confirmation observed at301s, SHALL fail a phase ending300s but may count in a window ending305s when its other invariants pass
- **AND** confirmation first observed at310s SHALL NOT count in that305s window; the original latency and purchase-duration bounds SHALL remain unchanged.

#### Scenario: Memory measurements retain their scope
- **WHEN** memory recovery is evaluated
- **THEN** working set, private commit, managed heap estimates, last-natural-GC facts, cache payload and complete retained fixture/durable state SHALL be distinguished, with exact sample windows and endpoint inclusion
- **AND** equal reservation counts SHALL NOT establish equal total retained state; a changed retained vector SHALL be disclosed as not-comparable for a matched-state comparison
- **AND** a separate diagnostic comparison SHALL NOT replace a failed original gate or establish leak absence.
- **AND** a diagnostic experiment SHALL have its own explicit protocol and authorization, independent of a successful load-acceptance run; an idle control in a fresh process SHALL NOT be treated as recovery after the tested workload or as a mandatory substitute for that workload.

#### Scenario: Frozen workload and whole-contour budget
- **WHEN** a fictional M2 load run is authorized
- **THEN** the reviewed packet SHALL identify source/build/profile/dataset hashes, process identities, finite data sizes, stages, cycles, concurrency, actual CPU/RAM/storage conditions, operational stop budgets and resource-accounting boundaries for the Host, driver, test handlers and external local dependencies
- **AND** missing or violated evidence/resource boundaries SHALL prevent acceptance; fictional transport SHALL NOT establish real supplier or hard-limited reference capacity.
- **AND** observed resource use SHALL remain distinct from an unproven hard whole-system capacity claim; unavailable shared-environment measurements SHALL be disclosed rather than replaced by invented hardware requirements.

#### Scenario: No result-driven tuning or repair claim
- **WHEN** any memory, useful-service or safety gate fails
- **THEN** the original raw result SHALL remain failed, without forced GC, memory trimming, state/claim resets, extended recovery, weakened thresholds or selective denominator
- **AND** a proposed repair SHALL distinguish code, measurement and environment hypotheses and SHALL require confirming evidence before a root-cause or fixed claim.

#### Scenario: Controlled stop and exact resource ownership
- **WHEN** the user stops a campaign or a budget, identity, evidence or transport boundary fails
- **THEN** new dispatch SHALL stop, planned remaining work SHALL be marked unfinished or not-run, admitted uncertainty SHALL remain protected, and only assets identified as owned by that run SHALL be cleaned up
- **AND** retries, resume or a new attempt SHALL require new explicit authorization; failed cleanup SHALL retain residual-resource evidence rather than trigger broad deletion.

#### Scenario: Planning and external authority remain separate
- **WHEN** research, planning validation or fictional performance completes
- **THEN** it SHALL NOT authorize implementation, runtime execution, publication, supplier access, real credentials, paid APIs, target provisioning or rollout
- **AND** historical M1 failed receipts, saved demonstrations and human-review evidence SHALL remain intact.


#### Scenario: Explicit future memory acceptance revision
- **WHEN** the owner-approved acceptance revision2 is used by a new frozen M2 S/L packet
- **THEN** the finalSameRetainedRowsMemoryStable five-second comparison SHALL remain diagnostic with blocking=false and its original true/false result, exact windows/counts/means/delta and observed row/cache state retained
- **AND** the mandatory memory limits SHALL remain RSS<=1073741824 bytes, final<=last pre30s baseline+134217728 bytes and mean[330,last]<=mean[300,330), without epsilon, forcedGC, changed windows or additional idle phase
- **AND** all other mandatory source/business/cache/queue/evidence/resource gates SHALL remain enforced, including3152jobs/361samples per profile and full S/L acceptance
- **AND** both profiles and receipts SHALL declare the same acceptance version; unsupported or mixed versions SHALL be rejected, and version1/M1 SHALL retain their original blocking result
- **AND** changed observed retained data SHALL be disclosed; equal row counts SHALL NOT establish comparable complete retained state
- **AND** prior failed/not-run results SHALL remain unchanged; offline replay or source readiness SHALL NOT accept an earlier packet under the new rules.

#### Scenario: Evidence-based required due work in future revision3
- **WHEN** a new frozen M2 S/L packet explicitly uses acceptanceVersion3 after the delegated source/data correction
- **THEN** it SHALL require zero terminal pending inbox, required due work and unknown due work, with complete bounded queue evidence for all361samples
- **AND** only an exact non-escalation HotelWork with complete body and an owned attempted booking in Confirmed/Cancelled state MAY be classified as redundant
- **AND** unconfirmed work, escalations and webhook observations SHALL remain required; unknown type/body/owner/identity and incomplete or overflowed evidence SHALL prevent acceptance
- **AND** original raw due counts, lag, legacy boolean and bounded message/state metadata SHALL remain observable, without logging payload bodies or processing/deleting messages to obtain acceptance
- **AND** versions1/2 and all historical receipts SHALL retain their original semantics; no memory, cache, source, business, timing, sampling or safety threshold SHALL change
- **AND** compact fixture storage SHALL preserve logical entries, all original wire keys/responses, fault recipes and count/effect semantics without clearing history.

#### Scenario: Fixed chain origin defines future recovery cohorts
- **WHEN** a future packet explicitly uses acceptanceVersion4
- **THEN** rolling recovery SHALL retain the16candidate ends300..315 and60second windows, but include a due search/rates/prepare/create step in the eligible cohort only when its fixed root search was also offered within that window
- **AND** carry-in descendants SHALL retain their IDs, failures and raw offered/useful denominators; every global/fixed-phase planned job SHALL remain accounted, and no not-yet-offered child SHALL improve the window
- **AND** membership SHALL depend only on the frozen dependency graph and due times, never on success, latency or source availability
- **AND** every eligible kind SHALL be nonempty and meet the unchanged95%useful threshold, unchangedsearchp95 and authoritative completion cutoff; missing or inconsistent facts/graphs SHALL fail
- **AND** the old recovery boolean/windows SHALL remain diagnostic; versions1/2/3 and historicalfailedreceipts SHALL remain unchanged, while the fresh version4 campaign must pass the new mandatory recovery gate and all other unchanged mandatory checks.

#### Scenario: Explicit owner decision makes RSS trend diagnostic in revision5
- **WHEN** a new frozen M2 S/L packet uses the owner-approved acceptanceVersion5
- **THEN** RSS<=1073741824bytes and finalRSS<=last pre30s baseline+134217728bytes SHALL remain mandatory, together with every other version4 mandatory check
- **AND** the original[300,330) and[330,last] non-growth comparison SHALL be diagnostic only, retaining exact samples/counts/sums/means/delta, its original boolean and the original combined workingSetRecovery result without rounding or epsilon
- **AND** complete valid361sample evidence and consistent original memory results SHALL be required; missing,duplicate,nonfinite,early/late or invalid memory observations SHALL fail
- **AND** versions1–4, historicalfailedreceipts and their original classifications SHALL remain unchanged; positive diagnostic trend SHALL NOT be reported as non-growth or proof of leak absence
- **AND** both profiles SHALL pass a fresh version5 campaign before acceptance; source checks or offlinecounterfactual evaluation SHALL NOT reclassify an older run.

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
