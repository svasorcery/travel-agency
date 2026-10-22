# Trip Rail intentions

## Purpose

Allow a private Trips owner to inspect the existing Rail timetable and explicitly stage one fictional journey as a historical manual intention, while preserving source retention restrictions and the ordinary M1 save contract.

## ADDED Requirements

### Requirement: Explicit Rail request within Trips
The system SHALL require explicit source-qualified station selections, departure date and transport mode for Rail search, and a separately selected existing plan day for staging. It SHALL preserve existing M1 city and day bounds and Moscow-only walking generation.

#### Scenario: Independent travel and plan dates
- **WHEN** the owner selects a departure date and a different existing plan day
- **THEN** both meanings are displayed distinctly, with no inferred connection or feasibility claim and no automatic brief edit.

#### Scenario: No station guessing or private brief transfer
- **WHEN** the owner opens Rail in a plan
- **THEN** opening alone issues no source request, no city or implicit date is inferred, and no private title, wishes, adult count or budget is sent to Rail; existing source defaults remain unchanged.

#### Scenario: Existing manual geography remains valid
- **WHEN** a saved plan uses another supported M1 city or four to fourteen days
- **THEN** Rail can be inspected under its published RF station scope while walking generation remains unsupported.

### Requirement: Reused bounded Rail search semantics
The system SHALL preserve the published Rail request, result, cancellation and outcome contract in both standalone Rail and Trips. It SHALL distinguish complete-empty, limited, skipped and failed outcomes and SHALL NOT silently replace a real failure with fictional data.

#### Scenario: Mixed limited result
- **WHEN** one requested mode succeeds and another fails or is skipped
- **THEN** both outcomes remain visible and missing rows are not presented as proof of no journeys.

#### Scenario: Empty or failed search
- **WHEN** a result is complete-empty, limited-zero, skipped or failed
- **THEN** the corresponding explanation remains distinct and no selectable journey is invented.

#### Scenario: Standalone Rail regression
- **WHEN** the standalone Rail route uses the shared public search state and transport
- **THEN** station selection, keyboard access, cancellation, exact fares and attribution retain their published behavior without Trips effects.

### Requirement: Current eligible Rail selection
The system SHALL accept a selection only from the current intact result and successful reference bundle, with matching request, profile, profile epoch and catalogue generation. It SHALL recheck item membership and non-null reception/expiry times at the selection action, not rely only on timers.

#### Scenario: Expiry before delayed timer
- **WHEN** result or reference expiry has passed at click time, or any required freshness value is null or invalid
- **THEN** staging is refused without modifying the plan and manual refresh or reselection is explained.

#### Scenario: Safe limited choice
- **WHEN** the selected fictional row is intact and current, and its mode is complete or limited only by page-limit or interval-service-unsupported
- **THEN** it can be selected with the limited warning retained even if another mode failed or was skipped.

#### Scenario: Ineligible row or reference
- **WHEN** the selected mode failed or was skipped, its limitation is unknown, the row has damaged ticket data, membership changed, or the latest reference read failed
- **THEN** selection is refused even if an older successful bundle remains in memory.

#### Scenario: Criteria or profile changed
- **WHEN** input changes A to B to A, a new catalogue generation arrives, or the source profile changes
- **THEN** an earlier response or selection cannot regain authority merely by matching visible criteria.

### Requirement: Rail source retention boundary
The system SHALL permit automatic durable Rail intention staging only for the explicitly fictional profile. Real-profile data SHALL remain transient under the published Rail retention contract, with staging disabled and an explanation; enabling the real source alone SHALL NOT unlock persistence.

#### Scenario: Real row is otherwise valid
- **WHEN** a real-profile row has valid identity, freshness and attribution
- **THEN** it remains view-only and no automatic projection or stripped derivative is persisted to the plan.

#### Scenario: No permission bypass
- **WHEN** a future real projection is desired
- **THEN** it requires separately revised Rail capability and rights evidence, not a runtime allow switch or relabelling real data as fictional.

#### Scenario: Fictional full path
- **WHEN** a valid fictional row is staged and explicitly saved
- **THEN** the ordinary M1 intention contains an explicit fictional historical marker after an unedited reload.

### Requirement: Clean saved foundation for Rail staging
The system SHALL open and stage Rail only against the current owner's clean saved plan and exact local foundation, with no pending read or mutation, unsaved form changes, barrier or uncertain write. Nonempty saved calendars SHALL be supported; walking generation SHALL remain empty-only.

#### Scenario: Dirty brief or pending save
- **WHEN** the form has an unapplied edit, a read or save is pending, or a lost-command barrier exists
- **THEN** Rail opening or staging is refused until the ordinary M1 state is resolved.

#### Scenario: Walk then Rail
- **WHEN** the owner stages and saves a walking candidate and then opens Rail
- **THEN** a permitted Rail intention can be appended to a nonfull existing day without replacing the walking blocks.

#### Scenario: Remote competing write
- **WHEN** another client changes the saved revision after the local foundation was observed
- **THEN** the eventual explicit M1 save applies ordinary CAS, preserves local draft on conflict and does not auto-retry or silently rebase.

### Requirement: Atomic bounded Rail intention append
The system SHALL append exactly one validated intention locally to an explicitly selected existing day, preserving every existing block, order, ID, note, brief, title and monetary value. It SHALL validate the full ordinary write and limits before any local mutation; save SHALL remain a separate explicit owner action.

#### Scenario: Full day or oversized write
- **WHEN** the target already has eight blocks or the prospective full write exceeds M1 scalar or UTF-8 bounds
- **THEN** nothing changes and an actionable error explains the limit.

#### Scenario: Double click
- **WHEN** the same choice is activated twice
- **THEN** at most one block is staged and the original foundation no longer authorizes another append.

#### Scenario: No half-created intention
- **WHEN** projection or full-write validation fails
- **THEN** no empty placeholder block, hidden PUT, retry or source refresh occurs.

### Requirement: Bounded historical fictional projection
The system SHALL persist only an ordinary block with a title of at most100 Unicode scalars, a plain historical fictional note of at most500 scalars, null times and null estimate. The note SHALL identify source, fictional profile, transport, thread, station refs, original departure/arrival, reception and expiry without truncating essential facts.

#### Scenario: Recognizable bounded title
- **WHEN** a fictional selected row has an optional train number
- **THEN** the title contains the transport noun, exact number and demo marker if the complete title fits M1 rules; missing, overlong or incompatible optional numbers use a fixed transport noun plus demo marker without truncating or rewriting the number, while all essential identity remains in the note.

#### Scenario: Long source identity
- **WHEN** the exact minimal identity and historical notice cannot fit the note or complete write
- **THEN** staging is refused with an explanation; references are not silently truncated, hashed or omitted to make it fit.

#### Scenario: Historical ordinary text
- **WHEN** the owner saves, reloads or edits the note
- **THEN** it is ordinary owner text with no trusted Rail badge, restored source panel or authoritative parsing of the note.

#### Scenario: No ticket or schedule conversion
- **WHEN** a selected row contains a fare and offset departure time
- **THEN** neither becomes an estimate, budget update or HH:mm field, and no seat, booking or live-status guarantee is created.

### Requirement: Rail picker lifetime and privacy
The system SHALL invalidate and destroy pending Rail choices on owner, plan, session, navigation, read generation, revision, edit, refresh, save or delete changes. Closing SHALL cancel pending requests and timers; private plan or source content SHALL NOT enter browser persistence, history, SSR transfer state or telemetry.

#### Scenario: Late response after edit or logout
- **WHEN** a search or selection callback arrives after a dirty form edit, logout, different plan or save
- **THEN** it cannot change the local draft or reopen the old source state.

#### Scenario: Close and reopen
- **WHEN** the owner closes the picker and opens it again
- **THEN** prior source choices are destroyed and are not restored from storage or saved notes.

#### Scenario: Target day is not an editor mutation
- **WHEN** only the independent picker target-day selection changes
- **THEN** it does not mutate the trip brief or mark the M1 form dirty; an actual M1 edit still invalidates the picker.

### Requirement: Independent accessible Rail form
The system SHALL render the Rail search form outside the M1 save form, with keyboard-operable choices, labelled independent dates, status announcements and predictable focus. Search and selection SHALL NOT submit the trip.

#### Scenario: Enter searches without saving
- **WHEN** Enter submits Rail search or selects a station
- **THEN** no Trips save request is issued and actual form ancestry contains no nested forms.

#### Scenario: Open close and staged focus
- **WHEN** the owner opens, closes or stages from the picker
- **THEN** focus moves to the relevant labelled picker or editor control and status is announced.

### Requirement: Faithful transient Rail facts
The system SHALL display original offset dates, source and freshness while keeping IANA timezone and source-update unknowns explicit. Ticket, seat and realtime facts SHALL remain independent; exact money strings SHALL NOT be rounded, multiplied by adults or copied into the plan.

#### Scenario: Exact large money
- **WHEN** a valid fare is9007199254740993.05RUB
- **THEN** every digit is rendered unchanged, with unknown price scope and no total for the party.

#### Scenario: Unknown ticket or timezone fact
- **WHEN** the source provides no seats, realtime update, IANA zone or sourceUpdatedAt
- **THEN** those facts remain unknown; reception time is not labelled as a supplier update.

### Requirement: Rail attribution remains authoritative
The system SHALL preserve the approved banner, sandbox, no-referrer, source text/link and CSP failure behavior alongside transient Rail facts. Missing or failed attribution SHALL clear affected source state and prohibit selection; fictional browser proof SHALL use only local attribution fixtures.

#### Scenario: Frame blocked or attribution absent
- **WHEN** the approved frame reports an error, a matching enforced CSP rule blocks it, or required attribution is missing
- **THEN** source state and pending choice are cleared and staging is unavailable.

#### Scenario: Report only and load events
- **WHEN** a report-only CSP event or frame load event occurs
- **THEN** neither is misreported as enforced failure or proof of actual official banner visibility.

### Requirement: Separate Rail source acceptance
The system SHALL support a complete local fictional Rail intention scenario with independent regression evidence, separate from walking quality denominators and real-source acceptance. Fresh-base ancestry and current-source dependency identity SHALL remain verifiable; publication, real provider and target actions SHALL require separate authority.

#### Scenario: Current integration proof
- **WHEN** Rail is accepted locally after integrating fresh dev
- **THEN** receipts identify current source and dependencies while older walking receipts remain historical, and the original requested base remains an ancestor.

#### Scenario: Frozen quality remains frozen
- **WHEN** Rail fictional cases are added
- **THEN** walking12Q+12S inputs, thresholds and fixed asOf remain unchanged, Rail cases are reported separately, and Q outputs wait for current structural and review acceptance.

#### Scenario: Source demo does not require real activation
- **WHEN** local source and fictional integration checks pass
- **THEN** the supported demo can be accepted without claiming real banner, rights, quota, human route quality, CI, target or rollout acceptance.
