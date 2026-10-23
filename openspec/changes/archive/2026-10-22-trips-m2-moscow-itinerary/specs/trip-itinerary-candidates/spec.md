# Trip itinerary candidates

## Purpose

Provide a private system-proposed itinerary from confirmed structured wishes and permitted reviewed city facts, with explicit user adoption, revision safety and visible uncertainty before it becomes ordinary editable planning content.

## ADDED Requirements

### Requirement: Bounded first itinerary scope
Generation SHALL support Moscow outdoor sightseeing for1–3inclusive days and1–9adults, with start/end from today's Moscow local date through30days ahead. Other valid M1 inputs SHALL remain manually editable and return an explicit unsupported generation outcome. Free-text extraction, indoor reservations and origin/hotel transfers SHALL be excluded.

#### Scenario: Supported saved brief
- **WHEN** the owner requests a candidate for a supported saved empty Moscow plan
- **THEN** the system proposes actual reviewed stops according to its accepted structured preferences without changing the saved plan.

#### Scenario: Manual scope beyond generation
- **WHEN** a valid manual plan selects another city, historical dates,4–14days or dates outside the generation horizon
- **THEN** generation returns unsupported with a reason and ordinary M1 editing remains available.

### Requirement: Permitted reviewed fact package
Every used POI, access fact and derived walking connection SHALL have a stable scoped ID,permitted fields,rights basis,source method/input identity and validity. Modelled records SHALL NOT claim an observation or human review. Generation SHALL use a reviewed version/hash, without runtime source calls. Fictional fixtures and local city metadata SHALL NOT establish real-city coverage.

#### Scenario: Missing rights or corrupt package
- **WHEN** the selected package lacks an accepted rights register, fails integrity/schema checks or is not released
- **THEN** generation is unavailable and makes no provider call or fabricated empty success.

#### Scenario: Real data release
- **WHEN** a real Moscow package is proposed for release
- **THEN** its18–48realPOI,60–256directed derived edges,source hashes/paths/model assumptions,licence notices and coverage are checked;passing fictional tests or download success cannot replace them or imply human/live acceptance.

### Requirement: Explicit modelled demo semantics
OSM-derived walking minutes SHALL be labelled modelled and uncalibrated,with source/input hash,path evidence,model version,assumptions and provenance. Observed timestamps and human review SHALL remain absent unless actually performed. Demo source SHALL be independently default-off and excluded from Production;M1 behavior SHALL remain intact.

#### Scenario: Display a modelled transition
- **WHEN** a demo candidate uses a derived mapped path
- **THEN** it exposes approximate time/distance,model assumptions and OSM attribution without presenting an empirical interval,live access or guaranteed feasibility.

#### Scenario: No invented human acceptance
- **WHEN** the package was prepared and checked by software only
- **THEN** it states humanQuality=not-evaluated and nullable human actors rather than invented curator signatures or numeric human scores.

#### Scenario: Production attempts demo source
- **WHEN** Production attempts to enable or resolve the demo source
- **THEN** composition/readiness fails closed under the existing TestOnly boundary without weakening ordinary M1 planning.

### Requirement: Bounded offline preparation
A dedicated offline helper SHALL consume a pinned local OSM XML/gzip snapshot and bounded POI inventory,emit immutable package/evidence/licence files and perform no network call. It SHALL reject unsupported access,directions,missing anchors/disconnections and malformed or oversized input rather than inventing connections. Runtime SHALL NOT import OSM or run a routing service.

#### Scenario: Reproducible mapped graph
- **WHEN** identical input bytes,inventory and profile/model versions are processed
- **THEN** output source paths,lengths,estimates and hashes are reproducible;directed paths follow mapped connected eligible segments.

#### Scenario: More reachable pairs than the package cap
- **WHEN** all18anchors are connected or their input order changes
- **THEN** the pinned nearest5outgoing-per-source selector retains90directed pairs reproducibly;48anchors retain at most240 and no fabricated edge fills a shortfall.

#### Scenario: Fixed profile and crossing semantics
- **WHEN** input has a crossing node on a crossing way,endpoint crossings,crossing=no,barriers or conflicting access/direction tags
- **THEN** the declared profile table and distinct-node counter determine eligibility/allowance without double counting or guessing unsupported tags.

#### Scenario: Old input rebuild and absent publication
- **WHEN** the same old snapshot is rebuilt today or publication metadata are missing
- **THEN** the30day allowance cannot restart from build/download time;missing publication prevents release and execution-time receipts do not change deterministic package hashes.
#### Scenario: Invalid or unsupported source
- **WHEN** XML contains a DTD/external entity,stream/graph limits are exceeded,or a route requires an unsupported conditional/private connection or no mapped anchor
- **THEN** import fails or excludes that path with an explicit reason,without network/entity access,straight-line repair or a released incomplete package.

#### Scenario: Licensed derived data
- **WHEN** a demo package or its derived output is prepared for separately authorized public use
- **THEN** OSM credit,ODbL licence/derived-data offer and reproduction inputs are included independently of editable user notes;private plans are not automatically published.
#### Scenario: Bound source indexing separately from the retained graph
- **WHEN** the offline input contains many non-pedestrian nodes or irrelevant tags
- **THEN** the temporary coordinate index is capped at750000records,the eligible graph remains capped at100000nodes/50000ways,and the200MiBcompressed/2GiBexpanded/five-minute caps remain. A sampled1GiBprocess budget and measured peak are checked before release;irrelevant tags never hide deny/conditional tags or enter runtime.
### Requirement: Immutable dataset release identity
A released dataset ID/version SHALL bind exact UTF-8 LF payload bytes through a separate SHA256 manifest, with no self-referential digest. Payload and manifest SHALL be bounded to256KiB and2KiB. Changed facts, rights or validity SHALL require a new version. Human evaluation SHALL bind the built dataset and policy before any evaluation output is observed.

#### Scenario: Changed bytes under the same release
- **WHEN** a payload byte, right or validity changes while its released identity or manifest digest stays the same
- **THEN** loading is unavailable rather than silently normalizing or accepting it as the old release.

#### Scenario: Freeze cases before binding an artifact
- **WHEN** quality cases and rubric were frozen before the runtime dataset artifact existed
- **THEN** the built payload hash,policy version,source identity and evaluation clock are added in a separate immutable run-binding before evaluation; cases and thresholds remain unchanged.

### Requirement: Fact freshness and unknowns
Source-record applicability SHALL be bounded to90days for identity,7days for verified access and30days for map/model snapshot use. Reading SHALL NOT extend validity. Snapshot publication/retrieval/build SHALL NOT become a survey date. Modelled minutes SHALL remain estimates,while absent/expired/unpermitted values remain unknown or exclude the connection.

#### Scenario: Unknown opening hours
- **WHEN** a selected POI has no permitted current hours for the travel date
- **THEN** its hours are explicitly unknown and no open-at-arrival or guaranteed-access claim is made.

#### Scenario: Expired mapped walking estimate
- **WHEN** a walking estimate is expired for the requested date
- **THEN** it cannot connect consecutive stops and the result becomes partial or empty if alternatives do not fill the plan.

#### Scenario: Package expiration
- **WHEN** the complete released package is unavailable or past its release validity
- **THEN** generation returns unavailable while manual saved plans remain readable.

### Requirement: Explainable bounded route selection
The system SHALL prefer distinct requested interests covered across chosen days and the current path, then filled stop targets, lower walking upper totals and stable ID order. Relaxed/balanced/active targets SHALL be2/3/4stops per day, walking upper caps60/90/120minutes and dwell-plus-walking caps240/360/480minutes. POIs SHALL NOT repeat across the trip; every intraday adjacency SHALL use a valid directed mapped path and declared model profile.

#### Scenario: Interest and pace
- **WHEN** the same brief,destination,fact hash,policy version and evaluation instant are generated repeatedly
- **THEN** the route IDs and reasons are reproducible and meet the chosen pace caps without invented travel minutes or a global-optimum claim.

#### Scenario: Repeated interest tags and input order
- **WHEN** many stops repeat one interest tag or equivalent fact arrays arrive in a different order
- **THEN** one tag counts as one distinct interest and stable ID expansion/ranking determines the route rather than raw tag frequency or JSON array order.

#### Scenario: Disconnected places
- **WHEN** attractive POIs lack a permitted connecting edge
- **THEN** generation excludes that adjacency rather than using straight-line distance as walking time.

#### Scenario: More days than distinct coverage
- **WHEN** the package cannot fill all day targets with distinct POIs
- **THEN** the result is partial or empty with reasons instead of reusing stops or lowering targets silently.

### Requirement: Separate outcome and coverage states
Generation SHALL distinguish candidate, successful empty, unsupported, unavailable, busy and stale outcomes. Candidate coverage SHALL distinguish filled stop targets from partial coverage. Filled SHALL NOT mean verified feasibility, complete trip price or satisfaction of excluded needs. Unmet interests and search/data limits SHALL be visible.

#### Scenario: Food or incomplete interests
- **WHEN** food-only or mixed interests cannot be fully served by the outdoor package
- **THEN** unmet interests remain explicit and a route with other useful stops is partial rather than falsely complete.

#### Scenario: No eligible stop
- **WHEN** a valid supported request has no eligible stop in a released package
- **THEN** it returns successful empty with reasons and zero plan mutations.

#### Scenario: Filled but unknown
- **WHEN** all stop targets are filled but access hours or total cost are unknown
- **THEN** those unknowns remain visible independently of filled coverage, without a feasible or within-budget badge.

### Requirement: Owner and revision bound computation
Generation SHALL derive owner only from the trusted principal, require one exact strong If-Match, and use the authoritative saved revision. Unknown/foreign plans SHALL return the same404 before decrypting private data. A revision change observed during generation SHALL return stale, with no persisted candidate or plan mutation.

#### Scenario: Foreign request
- **WHEN** one owner requests a candidate using another owner's TripId
- **THEN** the response is404 and neither that private payload nor its facts are processed or disclosed.

#### Scenario: Stale or missing precondition
- **WHEN** the request omits If-Match, uses a malformed tag or refers to a superseded revision
- **THEN** it receives428,400or412respectively and no candidate is adopted.

#### Scenario: Concurrent change during generation
- **WHEN** another editor changes or deletes the saved plan before the final authoritative recheck
- **THEN** the request returns412or404without a success candidate.

### Requirement: Preserve existing manual blocks
Generation SHALL require a saved empty calendar and a clean local editor/form. It SHALL NOT clear, pin, merge, replace or regenerate over accepted manual blocks. Owners SHALL retain existing M1 editing and explicit create/delete actions.

#### Scenario: Nonempty saved plan
- **WHEN** generation is requested for any saved block
- **THEN** it returns a typed409and preserves every block/revision; the UI explains that the pilot needs an empty foundation.

#### Scenario: Local unsaved edit
- **WHEN** the editor or unapplied parameters are dirty
- **THEN** generation/adoption is disabled until the owner explicitly saves or discards, without hidden autosave.

### Requirement: Structurally validated candidate facts
Every candidate SHALL be validated independently of selection against permitted fact IDs, types, scope, dates, directed edges, pace and size constraints. Invented,duplicate or mismatched IDs,invalid model derivation and modelled-as-observed/expired-as-current claims SHALL reject the entire result. Suggested times and monetary estimates SHALL be null; explanations SHALL use reviewed original text.

#### Scenario: Invalid result reference
- **WHEN** a result invents a POI, reverses a directed edge, duplicates a stop or associates a fact with the wrong POI
- **THEN** the whole result is unavailable without a repaired partial success.

#### Scenario: Invented factual amount or schedule
- **WHEN** a result introduces a price, exact arrival time, unsupported travel duration or open-at-arrival claim
- **THEN** independent validation refuses it and the saved plan remains unchanged.

### Requirement: Bounded no effect computation
Generation SHALL make zero model, supplier, booking, payment or other external calls and zero persistent writes. It SHALL admit at most2active computations per process with no queue, stop within a2second computation deadline and at most20000search expansions, and never retry automatically. Limits SHALL NOT be presented as measured SLA.

#### Scenario: Busy or deadline
- **WHEN** admission is full or computation reaches its deadline
- **THEN** it returns distinct429busy or504deadline, releases bounded work safely and does not auto-submit a second request.

#### Scenario: Preview read effects
- **WHEN** the owner generates, rereads, dismisses or expires a candidate
- **THEN** plan revision/timestamps/capacity and external operations stay unchanged.

### Requirement: Strict bounded candidate wire contract
The candidate request SHALL accept only an empty object with at most1KiB. Complete response SHALL be at most128KiB,3days,12blocks and96facts, with strict versioned decoding and lossless revision strings. Proposed ordinary M1 blocks SHALL pass its full-detail size checks. Oversize and unknown/duplicate properties SHALL fail without truncation or mutation.

#### Scenario: Oversize or extra fields
- **WHEN** request/result exceeds bounds or includes undeclared fields or duplicate properties
- **THEN** it is rejected before adoption and private source text is not logged.

#### Scenario: Unicode near saved size limit
- **WHEN** a bounded candidate would exceed the ordinary plan's complete canonical detail limit including server fields
- **THEN** it is refused before preview adoption; user-edited saves still repeat M1 precommit validation.

### Requirement: Route specific HTTP admission
The candidate POST SHALL accept its1KiB empty-object body through the real Trips middleware, separately from M1 create validation. Availability SHALL bypass plan storage/protection readiness after trusted authentication. Generation-off404 SHALL precede authentication. Other M1 request guards SHALL retain their behavior and new typed outcomes SHALL retain their declared status.

#### Scenario: Empty candidate body through the pipeline
- **WHEN** an authorized current-revision candidate POST sends exactly an empty object through the production middleware order
- **THEN** it reaches generation rather than failing M1 title/brief validation; chunked and known-length bodies both obey1KiB.

#### Scenario: Availability without private infrastructure
- **WHEN** the package is ready and availability is read while plan readiness/store/protection sentinels would throw if resolved
- **THEN** ready metadata is returned without resolving them, while candidate POST still requires normal private-plan readiness.

#### Scenario: Disabled path and unchanged manual guards
- **WHEN** generation is off and the caller is anonymous,or an existing M1 POST/PUT is submitted
- **THEN** M2 routes return404before auth and M1 still applies its original strict body/precondition/readiness rules,including routed case/trailing-slash variants.

#### Scenario: Typed generation errors
- **WHEN** nonempty-plan,unavailable,busy or computation-deadline errors pass through the real API error formatter
- **THEN** they return409,503,429or504respectively rather than falling into400; only Busy emits Retry-After1.

### Requirement: Observable generation availability
Generation SHALL expose an authenticated no-store availability read, bounded to2KiB, with ready dataset ID/version/hash,policy version,data mode,human-quality status and release validity. Disabled SHALL return404before auth/dependencies; enabled-unready SHALL return typed503without stale identity. Preview transfer SHALL require fresh ready identity matching its candidate, separately from owner revision.

#### Scenario: Package changes without a plan edit
- **WHEN** package A or its generation policy is replaced or withdrawn after preview while the saved plan revision stays unchanged
- **THEN** the mandatory availability reread mismatches or fails, transfer is blocked and preview becomes stale or unavailable without overwriting editor content.

#### Scenario: Feature off or failed availability read
- **WHEN** generation is disabled or its required pre-transfer availability read fails, times out or returns invalid metadata
- **THEN** transfer is blocked in a distinct disabled or unavailable state and manual M1 editing remains available, with no automatic retry or regeneration.

#### Scenario: Valid observations and a later withdrawal
- **WHEN** both revision and matching dataset checks succeed but a withdrawal races after the last observation
- **THEN** the observation is explicitly best effort, and transferred/saved blocks remain ordinary intentions without a trusted-current-source guarantee.

### Requirement: Explicit preview adoption and acceptance
A candidate SHALL remain a separate in-memory preview until explicit transfer to the editor. Transfer SHALL preserve brief/title and require successful owner and availability rereads, unchanged revision, matching ready dataset, clean buffer and unexpired preview. Only an explicit ordinary M1 save SHALL persist edited intentions using the candidate's base revision, with no automatic rebase.

#### Scenario: Preview then edit and save
- **WHEN** the owner reviews sources, explicitly transfers the preview, edits a stop and saves
- **THEN** only the submitted ordinary planning content is committed once through M1 CAS, with null default prices/times and a bounded historical source note.

#### Scenario: Saved revision changed before transfer
- **WHEN** authoritative reread before transfer finds a changed revision
- **THEN** transfer fails stale and does not overwrite local or server content.

#### Scenario: Edit or session change during adoption checks
- **WHEN** local form/calendar state, owner, opened plan or candidate epoch changes while either mandatory pre-transfer read is pending
- **THEN** the combined adoption ticket is invalid and no preview content is copied, even if both replies are otherwise successful.

#### Scenario: Competing save after transfer
- **WHEN** another editor commits before the candidate-derived save
- **THEN** the save receives412and retains local edits for explicit comparison, without automatically merging or retrying on the new revision.

#### Scenario: Lost acceptance response
- **WHEN** the save may have committed but its response is lost
- **THEN** M1 uncertain-write handling retains edits, permits authoritative reread and does not treat that reread as proof of the lost command.

### Requirement: Session and local edit isolation
Candidate requests and adoption SHALL respect owner/session, opened plan, local edit/form version, pending mutation and monotonic revision. Late results SHALL NOT overwrite newer edits or resurrect deleted content. Logout/owner/plan changes SHALL clear private preview and cancel or discard previous requests.

#### Scenario: Edit while generating
- **WHEN** the owner changes form/calendar after request dispatch
- **THEN** the late candidate cannot replace those edits and is discarded or shown stale.

#### Scenario: Owner or plan changes
- **WHEN** logout, different owner, plan navigation or confirmed delete occurs before the reply
- **THEN** no old candidate appears in the new session/plan or restores deleted content.

#### Scenario: Duplicate click and late result
- **WHEN** a generation request is pending or an older reply follows a newer request
- **THEN** there is one pending submit and only the current guarded response can become preview.

### Requirement: Historical intentions after save
Accepted candidate-derived blocks SHALL remain ordinary editable M1 intentions. A bounded editable historical dataset/POI note SHALL NOT establish trusted facts. Reload SHALL NOT claim current source verification, generated origin of edited text, booking or complete pricing; structured preview provenance SHALL NOT be silently persisted in v1 fields.

#### Scenario: Reload accepted route
- **WHEN** the saved plan is reloaded after acceptance
- **THEN** blocks and historical note survive as user planning content while no live/verified source panel or supplier state is inferred.

#### Scenario: User edits historical note
- **WHEN** the owner changes a source-looking title or note
- **THEN** it remains untrusted plain text and cannot change source rights, trust, expiry or supplier state.

### Requirement: Private accessible candidate rendering
Candidate responses/errors SHALL be no-store and exclude private payloads from diagnostics. Browser storage, SSR and caches SHALL NOT retain private previews. Sources SHALL render as text or reviewed safe HTTPS links, without arbitrary HTML or URL execution. Keyboard labels, announced outcomes and focus recovery SHALL cover preview and adoption.

#### Scenario: Source or private text rendering
- **WHEN** hostile-looking text or an invalid URL appears in source data
- **THEN** it cannot execute as HTML/script and invalid source data fails validation without leaking the payload into logs.

#### Scenario: Keyboard review
- **WHEN** a keyboard user reviews a partial candidate or validation error
- **THEN** sources, unknowns and explicit adoption are reachable and announced without losing valid editor content.

### Requirement: Isolated optional generation
Candidate generation SHALL be independently default-off under Trips enablement. Disabled candidate routes SHALL return404before auth or DB/package resolution. An unready candidate package SHALL NOT break manual M1 readiness or cause resource provisioning. Rollback SHALL preserve plans, keys and existing module contracts.

#### Scenario: Candidate disabled with M1 enabled
- **WHEN** only M1 is enabled
- **THEN** manual planning works and candidate route/navigation is unavailable with zero package/model/provider work.

#### Scenario: Candidate package unavailable
- **WHEN** candidate generation is enabled but its package is invalid or missing
- **THEN** only generation is unavailable and ordinary M1 operations retain their existing semantics.

### Requirement: Separate real data and route quality acceptance
Demo acceptance SHALL require permitted real-map data,reproducible model derivations and source/end-to-end checks. It SHALL NOT claim calibrated travel times or human suitability. Human field/route review,enabled Host/Keycloak target acceptance and rollout SHALL remain separate live gates;source/CI/AI review SHALL NOT substitute for them.

#### Scenario: Source tests pass without real dataset
- **WHEN** fictional structural tests pass while real Moscow data or human evaluation is pending
- **THEN** status is source-only while demo data are pending;after D_demo it remains explicitly modelled-demo with human/live gates pending and no enablement.

#### Scenario: Corpus averages conceal a weak route
- **WHEN** other routes score highly but any of the12real route cases has a reviewer score below3 or two-reviewer mean below4 for a dimension
- **THEN** human quality fails that case regardless of the corpus median; all original scores and the full12-case denominator remain.

#### Scenario: Structural fixtures are separate from human quality
- **WHEN** induced expiry,disconnection or negative privacy/scope cases use modified or fictional structural fixtures
- **THEN** their12-case structural results are reported separately from the12real-route human cases and cannot inflate accepted city-quality coverage.

#### Scenario: Human quality failure
- **WHEN** frozen route evaluation misses its predeclared thresholds
- **THEN** quality remains failed, all cases remain in the denominator and tuning cannot retrospectively relabel them as an untouched holdout.
