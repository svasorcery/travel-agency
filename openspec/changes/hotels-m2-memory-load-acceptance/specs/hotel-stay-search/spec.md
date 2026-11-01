# Spec Delta

## MODIFIED Requirements

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
