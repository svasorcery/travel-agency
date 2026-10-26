# Spec Delta

## MODIFIED Requirements

### Requirement: Bounded first itinerary scope
Generation SHALL support outdoor sightseeing in Moscow and Saint Petersburg for 1–3 inclusive days and 1–9 adults under explicit protocol v2; legacy v1 SHALL retain Moscow-only scope. Start/end SHALL be from today in the saved destination IANA zone through 30 days ahead. Other valid M1 inputs SHALL remain manually editable and return an explicit unsupported generation outcome. Free-text extraction, indoor reservations and origin/hotel transfers SHALL be excluded.

#### Scenario: Supported saved brief
- **WHEN** the owner requests a candidate for a supported saved empty plan for the requested protocol
- **THEN** the system proposes actual reviewed stops according to its accepted structured preferences without changing the saved plan.

#### Scenario: Manual scope beyond generation
- **WHEN** a valid manual plan selects a city unsupported by the requested protocol, historical dates,4–14days or dates outside the generation horizon
- **THEN** generation returns unsupported with a reason and ordinary M1 editing remains available.

### Requirement: Permitted reviewed fact package
Every used POI, access fact and derived walking connection SHALL have a stable scoped ID,permitted fields,rights basis,source method/input identity and validity. Modelled records SHALL NOT claim an observation or human review. Generation SHALL use a reviewed version/hash, without runtime source calls. Fictional fixtures and local city metadata SHALL NOT establish real-city coverage.

#### Scenario: Missing rights or corrupt package
- **WHEN** the selected package lacks an accepted rights register, fails integrity/schema checks or is not released
- **THEN** generation is unavailable and makes no provider call or fabricated empty success.

#### Scenario: Real data release
- **WHEN** a real city package is proposed for release
- **THEN** its18–48realPOI,60–256directed derived edges,source hashes/paths/model assumptions,licence notices and coverage are checked;passing fictional tests or download success cannot replace them or imply human/live acceptance.

### Requirement: Observable generation availability
Generation SHALL expose an authenticated no-store availability read, bounded to2KiB, with ready destination and dataset ID/version/hash,policy version,data mode,human-quality status and release validity. Legacy selection and explicit v2 destination selection SHALL follow their respective strict protocol contracts. Disabled SHALL return404before auth/dependencies; enabled-unready SHALL return typed503without stale identity. Preview transfer SHALL require fresh ready identity matching its candidate, separately from owner revision.

#### Scenario: Package changes without a plan edit
- **WHEN** package A or its generation policy is replaced or withdrawn after preview while the saved plan revision stays unchanged
- **THEN** the mandatory availability reread mismatches or fails, transfer is blocked and preview becomes stale or unavailable without overwriting editor content.

#### Scenario: Feature off or failed availability read
- **WHEN** generation is disabled or its required pre-transfer availability read fails, times out or returns invalid metadata
- **THEN** transfer is blocked in a distinct disabled or unavailable state and manual M1 editing remains available, with no automatic retry or regeneration.

#### Scenario: Valid observations and a later withdrawal
- **WHEN** both revision and matching dataset checks succeed but a withdrawal races after the last observation
- **THEN** the observation is explicitly best effort, and transferred/saved blocks remain ordinary intentions without a trusted-current-source guarantee.

## ADDED Requirements

### Requirement: Explicit itinerary protocol compatibility
The existing routes SHALL preserve their full v1 behavior when the version header is absent. Version 2 SHALL require one exact `X-Trips-Itinerary-Version: 2` header and strict separate decoding. Unknown, empty, repeated or list-valued versions SHALL return 400. Candidate bodies SHALL remain empty objects with no query; their destination SHALL come only from the owner's saved plan.

#### Scenario: Legacy callers
- **WHEN** no version header is sent
- **THEN** availability remains query-free and Moscow-only, Moscow candidate/empty/unsupported decoding remains v1, and a Saint Petersburg plan returns the former v1 destinationUnsupported result without loading its package.

#### Scenario: Version two callers
- **WHEN** a v2 candidate is requested for a saved empty Moscow or Saint Petersburg plan
- **THEN** candidate, empty and unsupported responses carry candidateVersion 2 and strict destination semantics; legacy v1 response fixtures remain valid only in the legacy decoder branch.

#### Scenario: Invalid protocol or client-selected candidate city
- **WHEN** a version is unsupported/duplicated or candidate query/body attempts to choose a city
- **THEN** the request is rejected before package processing, with no write and no fallback protocol.

### Requirement: Destination qualified availability
V2 availability SHALL require exactly one canonical destinationId query and no body. Ready SHALL identify only that city's package within 2 KiB. A valid catalogue city without generation support SHALL return 200 unsupported without dataset identity; supported but unready SHALL return typed 503. Authentication, no-store, disabled-before-auth 404 and storage-independent availability SHALL remain intact.

#### Scenario: Two ready cities
- **WHEN** the owner requests v2 availability separately for moscow and saint-petersburg
- **THEN** each reply identifies its own destination, dataset/version/hash, policy, mode, human-quality status and validity, without reading private plan storage.

#### Scenario: Catalogue city without a package capability
- **WHEN** v2 availability requests kaliningrad
- **THEN** it returns only candidateVersion 2, status unsupported, destinationId kaliningrad and reason destinationUnsupported, with no package read or Moscow fallback.

#### Scenario: Invalid availability selection
- **WHEN** destinationId is absent, duplicated, noncanonical or unknown, an extra query key is supplied, or a body is present
- **THEN** v2 returns 400; a v1 request with any query retains its former 400 result.

#### Scenario: Independent unavailable city
- **WHEN** Saint Petersburg is expired, withdrawn, missing or corrupt while Moscow remains valid
- **THEN** Saint Petersburg returns 503 without stale metadata and Moscow availability/generation remains usable; ordinary manual operations for both remain usable.

### Requirement: City bound package and preview identity
Selection, validation, encoding, decoding and adoption SHALL bind destination, saved zone/catalogue version, dataset/version/hash, profile, source and derived-data offer to one accepted city release. A mismatch SHALL reject the complete result. Package failure SHALL NOT cause cross-city fallback. Adoption SHALL recheck the same protocol and saved destination as its candidate.

#### Scenario: Mixed package or response
- **WHEN** a Saint Petersburg plan receives Moscow facts, paths, package metadata or offer, or a path/POI references another release
- **THEN** the complete result is unavailable or a client contract error and cannot become a preview or be adopted.

#### Scenario: Destination changes while availability is pending
- **WHEN** the owner changes plan, destination, session or local epoch, including A to B to A, before a late reply
- **THEN** the old reply and adoption ticket remain invalid even if visible city text matches again.

#### Scenario: Shared admission
- **WHEN** both supported cities generate concurrently
- **THEN** they share the existing two-active/no-queue capacity and unchanged computation bounds rather than receiving independent doubled limits.

### Requirement: Bounded Saint Petersburg outdoor package
The first Saint Petersburg release SHALL cover only the declared central left-bank district with 18–20 eligible distinct POIs, at least three per supported tag and 60–100 directed modelled edges. Indoor visits, gated museum gardens/courtyards and crossings of the Neva SHALL be excluded. Identity/anchor/path failures SHALL remain explicit; no invented coordinates, tags or connections SHALL fill a shortfall.

#### Scenario: Restricted transit despite absent map denial
- **WHEN** an otherwise mapped path enters an excluded garden, courtyard or Neva crossing
- **THEN** it is excluded using retained reproducible preparation evidence and cannot enter the released paths or candidate.

#### Scenario: Insufficient real coverage
- **WHEN** anchors within the existing 50 metre bound, rights, tag coverage or directed edges fail the package minimum
- **THEN** data acceptance fails pending an explicit reviewed inventory/scope revision; caps and coordinates are not silently relaxed.

#### Scenario: Exterior without current access evidence
- **WHEN** an eligible outdoor stop has no permitted current access, hours or cost evidence
- **THEN** those values remain unknown, proposed times and amounts remain null, and a filled route does not assert entry, safety, feasibility or a total price.

### Requirement: Independent city acceptance evidence
Saint Petersburg SHALL have separately bound data, mechanical evaluation and factual-audit records. Moscow results SHALL NOT count as Saint Petersburg quality. Human scores SHALL remain absent until actual independent human review, and target/rollout SHALL remain separately authorized. Failed or excluded cases SHALL NOT be relabelled to inflate the accepted denominator.

#### Scenario: Mechanically valid route
- **WHEN** the two builds, strict validation, frozen SPb corpus and factual audit pass
- **THEN** only the corresponding source/demo evidence is accepted; human suitability/calibration and real-user rollout remain pending.

#### Scenario: Frozen quality inputs
- **WHEN** SPb mechanical evaluation begins
- **THEN** twelve real-package cases and twelve separate structural/negative cases, expected outcomes, thresholds, policy/package hashes and clocks are bound before outputs are observed; human A/B forms remain blank.
