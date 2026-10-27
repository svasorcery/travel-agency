# trip-brief-extraction Spec Delta

## Purpose

Allow authenticated Trips users to turn short natural-language wishes into a temporary, inspectable candidate for existing structured form fields, with explicit correction and confirmation before any ordinary planning action.

## ADDED Requirements

### Requirement: Independently optional private extraction
Extraction SHALL remain independently default-off and excluded from Production in the first slice. Disabled routes SHALL return 404 before auth or dependencies. Enabled routes SHALL require trusted identity. Model/provider/pricing/private-text authorization SHALL be separate from feature enablement, and failure SHALL NOT disable manual Trips operations.

#### Scenario: Capability disabled
- **WHEN** an anonymous or authenticated caller requests extraction with its flag off
- **THEN** it receives no-store 404 with no store, package, model or provider work.

#### Scenario: Identity and readiness
- **WHEN** enabled extraction has an anonymous caller, invalid trusted ownership or an unavailable AI dependency
- **THEN** it returns 401, 403 or a typed unavailable result respectively without exposing private text or poisoning manual form readiness.

#### Scenario: No execution authorization
- **WHEN** source tests pass or the feature flag is enabled without accepted model, price and text-processing permission
- **THEN** no real model request is made.

#### Scenario: Discover processing terms without inference
- **WHEN** an authenticated client reads extraction capability before entering or dispatching text
- **THEN** a no-store response of at most2KiB exposes only versioned public processor/model/policy identifiers and configured/unconfigured status, with no AI, bus, store, package or accounting dependency access; disabled still returns404 before auth.

#### Scenario: Processing policy changed before dispatch
- **WHEN** the submitted processing-policy version differs from the currently approved Host or AI configuration
- **THEN** dispatch fails typed unavailable with no provider call, no alternate processor fallback and no automatic retry.

### Requirement: Bounded first extraction vocabulary
Extraction version 1 SHALL propose only one catalogue1 destination, date-only dates, auxiliary inclusive duration, explicit adult count, supported interests and pace from Russian text. Origin, budget/currency and other wishes SHALL remain manual-only with visible exclusions. Catalogue coverage SHALL NOT imply itinerary coverage or supplier availability.

#### Scenario: Representative wishes
- **WHEN** text says “Хочу в Петербург на три дня, нас двое, интересуют архитектура и история, темп спокойный”
- **THEN** it proposes saint-petersburg, duration 3, architecture/history and relaxed; dates stay missing, adults stay ambiguous until clarified, and no itinerary or save is dispatched.

#### Scenario: Manual-only wishes
- **WHEN** text includes an origin, budget, currency, children, indoor bookings or transport wishes
- **THEN** detected clauses are marked unsupported/manual-only, the fixed scope notice remains visible, and no corresponding form value is inferred or overwritten.

#### Scenario: Catalogue versus generation
- **WHEN** text unambiguously selects Kaliningrad or fourteen inclusive days
- **THEN** valid extraction can be reviewed while existing walking generation retains its independent unsupported scope.

### Requirement: Explicit uncertainty and conflict states
Every supported slot SHALL distinguish extracted, missing, ambiguous, unsupported and invalid. Conflicts SHALL block affected selections until explicit correction. Missing or unselected fields SHALL preserve form values without claiming they were extracted. Output SHALL NOT invent defaults, clamp values or present unhandled text as satisfied.

#### Scenario: Missing preferences
- **WHEN** no adults, interests or pace are mentioned
- **THEN** those slots remain missing without substituting 1 adult, an empty interest set or balanced pace.

#### Scenario: Ambiguous or contradictory wishes
- **WHEN** two possible destinations, incompatible adult counts, conflicting paces or a date-duration disagreement appear
- **THEN** alternatives/conflicts are visible and affected fields cannot transfer until the user resolves them.

#### Scenario: Invalid supported value
- **WHEN** a supported expression gives 0 or 10 adults, fifteen days, reversed dates or an impossible calendar date
- **THEN** affected fields are invalid or conflicting without partial correction or nearest valid substitution.

#### Scenario: Retained manual defaults
- **WHEN** an extracted slot is missing or ambiguous while the form already contains an initial or manually edited value
- **THEN** that value remains unchanged and explicitly labelled as retained from the form, without claiming it came from text or resolves the ambiguity; unsupported origin/money clauses remain visible.

#### Scenario: Explicit empty and negated interests
- **WHEN** the text explicitly declines interests or negates a particular interest
- **THEN** an intentional empty set remains distinct from missing and a negated interest is never added as a positive selection.

### Requirement: Evidence linked untrusted candidates
Candidate values SHALL reference bounded spans in the original input and pass independent deterministic normalization and domain bounds. Unknown/duplicate members, wrong types, malformed Unicode, invalid spans, prose or unsafe generated values SHALL reject the response. User/model strings SHALL render only as text. Structural validity SHALL NOT be claimed as semantic extraction accuracy.

#### Scenario: Hallucinated value or broken evidence
- **WHEN** a model proposes a city/count/date without admissible matching evidence or splits a surrogate pair in an evidence offset
- **THEN** its response is rejected without a transferable partial candidate or raw diagnostic payload.

#### Scenario: Instructions inside wishes
- **WHEN** input asks the system to ignore rules, execute a tool, save a plan or render markup
- **THEN** it cannot change execution policy or invoke tools/business actions and markup remains inert.

#### Scenario: Undetected clause
- **WHEN** structural validation passes but the extractor misses a semantic constraint
- **THEN** the original text and complete field review remain available for human correction and the evaluation records that omission as an error rather than proven completeness.

### Requirement: Stable city identity and role interpretation
City normalization SHALL use a versioned finite first-party alias vocabulary and catalogue1 identities, without fuzzy correction, geocoding or metadata invention. Unknown cities SHALL NOT fall back. Multiple destinations and optional origin roles SHALL remain explicit and SHALL NOT become a single inferred route.

#### Scenario: Alias and role
- **WHEN** text says “из Москвы в Петербург”
- **THEN** saint-petersburg can be proposed as destination and the Moscow origin stays manual-only without swapping roles.

#### Scenario: Near miss or multiple cities
- **WHEN** a city is misspelled/confusable or several destinations are requested
- **THEN** the former requires clarification and the latter is unsupported as a multi-destination brief without new catalogue entries or fallback city.

### Requirement: Anchored conservative date interpretation
Relative dates SHALL use one server-captured instant and a displayed explicit catalogue IANA reference zone. Only today/tomorrow/day-after-tomorrow and complete explicit dates are normalized in version1. Date arithmetic SHALL be deterministic, inclusive and bounded to1–14days. Missing years, vague weekdays, nights and unsupported date expressions SHALL require manual clarification.

#### Scenario: Duration only
- **WHEN** text provides only three days
- **THEN** duration stays auxiliary and neither start nor end is invented.

#### Scenario: Inclusive end
- **WHEN** an unambiguous start and three-day duration are given
- **THEN** end is start plus two days, with checked leap-year/year-boundary arithmetic and a visible derived relationship.

#### Scenario: Date contradiction
- **WHEN** an explicit complete range disagrees with duration or an accompanying weekday
- **THEN** the affected date group is blocked until manually corrected rather than selecting one interpretation.

#### Scenario: Reference zone boundary
- **WHEN** the same instant is a different local date in Europe/Moscow and Europe/Kaliningrad
- **THEN** relative normalization follows the displayed selected zone and echoes the anchor; null zone with relative wording stays ambiguous.

#### Scenario: Missing year and historical dates
- **WHEN** text omits a year or supplies a valid fully specified past date
- **THEN** the former is ambiguous without nearest-future guessing and the latter stays valid without a current-availability claim.

#### Scenario: Expiry while reviewing
- **WHEN** ten minutes since admission or the next reference-zone midnight for relative wording is reached
- **THEN** confirmation is blocked at click time using conservative monotonic elapsed time from dispatch and the server lifetime, without browser wall-clock authority, silently recomputing dates or renewing TTL.

### Requirement: Exact manual money boundary
Version1 extraction SHALL NOT project budget/currency, multiply per-person/per-day amounts, infer currency or use FX. The manual form SHALL retain its existing exact decimal-string bounds and currencies. Unknown or deferred money SHALL NOT become zero, a complete price or a budget-fit assertion.

#### Scenario: Complete or ambiguous budget
- **WHEN** text gives “10000 RUB всего”, “по100 долларов”, a zero budget or conflicting amounts
- **THEN** money remains visibly manual-only and existing budget/currency are untouched.

#### Scenario: Manual exact amount
- **WHEN** the user manually enters a budget after reviewing extraction
- **THEN** existing M1 exact money validation accepts or rejects it without floating-point normalization, rounding or model interpretation.

### Requirement: Explicit selective confirmation into the form
A candidate SHALL remain separate from form, calendar and saved state. Review SHALL show original text, anchor, per-field evidence/status, old-to-new changes and exclusions. Transfer choices SHALL start unchecked, permit explicit valid corrections and copy only selected fields. Dates SHALL transfer as one valid pair. Confirmation SHALL NOT Apply, Create, Save or Generate.

#### Scenario: Partial review and transfer
- **WHEN** the user selects valid destination/interests while leaving missing dates and deferred money unresolved
- **THEN** only those selected values enter the existing parameter form, all other values remain untouched and unresolved warnings remain visible.

#### Scenario: Manual correction
- **WHEN** the user supplies a missing adult count or start date during review
- **THEN** it is labelled user-corrected, validated and used only after explicit selection/confirmation; derived end still follows the selected inclusive duration.

#### Scenario: Auxiliary duration and edited dates
- **WHEN** only duration is extracted, or the user edits review dates
- **THEN** duration has no standalone transfer or M1 field, never borrows an unstated form start, and derives an end only through an explicit valid link; conflicting or explicitly supplied ends require deliberate reconciliation before atomic date transfer.

#### Scenario: Capability disabled before confirmation
- **WHEN** mandatory capability reread before transfer returns disabled, unconfigured, changed policy, invalid metadata or unavailable
- **THEN** the candidate cannot transfer, pending replies are invalidated and manual form content remains available without a model call.

#### Scenario: Existing populated plan
- **WHEN** confirmed form changes would change destination or remove populated dates
- **THEN** existing non-destructive Apply rules remain authoritative and no title, block, revision or saved snapshot is changed by extraction/confirmation.

#### Scenario: Separate itinerary action
- **WHEN** reviewed fields are transferred
- **THEN** no route request occurs; subsequent itinerary generation still requires its normal clean saved foundation, scope, protocol and package checks.

### Requirement: Monotonic session and edit isolation
Requests, replies and confirmation SHALL bind owner/session, plan-or-new-form generation, saved revision, editor/form/text/zone versions and attempt identity. Any intervening context or manual edit SHALL invalidate the attempt even after values return to their prior appearance. Duplicate clicks SHALL dispatch at most one pending request/confirmation; consumed candidates SHALL NOT be reused.

#### Scenario: Manual edit while pending
- **WHEN** the owner changes any form/title/calendar content after dispatch
- **THEN** a late reply cannot overwrite those edits or become a current transferable candidate.

#### Scenario: Owner plan and ABA changes
- **WHEN** logout, owner replacement, plan A to B to A, reset or deletion occurs
- **THEN** previous private text/candidate state is cleared and old callbacks cannot resurrect it.

#### Scenario: Competing saved revision
- **WHEN** the saved plan reread before confirmation differs from the captured revision or fails
- **THEN** transfer is blocked and local content is retained without automatic adoption/rebase.

#### Scenario: Edits during confirmation reread
- **WHEN** editor or candidate review changes while confirmation checks await a read
- **THEN** its original ticket is invalid and no values are copied.

#### Scenario: Repeated clicks and ordinary save
- **WHEN** the user clicks extraction/confirmation repeatedly or later submits an ordinary save
- **THEN** pending clicks do not duplicate actions and Save retains existing owner/revision/CAS and uncertain-write handling.

### Requirement: Bounded inference and typed fallback
Extraction SHALL enforce1000Unicode-scalar/4096UTF8-byte text,8KiBHTTP input,4096total input tokens,1024output tokens and16KiBoutput. Inference SHALL fit3500ms within4500msAI and5000msHost deadlines. Limits SHALL reject before dispatch where possible without truncation. Failures SHALL keep manual editing available and SHALL NOT auto-retry.

#### Scenario: Oversized chunked request
- **WHEN** a known-length or chunked body/text exceeds a limit or the complete prompt envelope exceeds its token bound
- **THEN** it is rejected before any model call without trimming wishes.

#### Scenario: Empty review versus failed inference
- **WHEN** a valid result has no transferable slots, or the provider refuses/fails/returns malformed output
- **THEN** the former is a successful review with visible slot states and the latter is a distinct unavailable/contract-error outcome, never a fabricated empty success.

#### Scenario: Deadline or lost transport
- **WHEN** a deadline, cancellation or lost reply follows possible inference
- **THEN** no caller, transport, handler or SDK automatic retry occurs; any late result is discarded and cancellation is not claimed as proof of no billing.

### Requirement: Explicit cost and admission authorization
Real dispatch SHALL require selected model, verified pricing and separately authorized limits within USD0.03/request and USD0.30/rolling hour/process. There SHALL be one active call/no queue, at most6starts/minute/process and6requests/minute/owner. Spend uncertainty SHALL retain conservative reservation; these process bounds SHALL NOT be presented as fleet/invoice limits.

#### Scenario: No selection or insufficient budget
- **WHEN** model/pricing/permission is missing, accounting cannot be admitted or the full reserved request exceeds an authorized limit
- **THEN** no provider call occurs and manual input remains available.

#### Scenario: Unknown usage or accounting failure
- **WHEN** a possible model call has missing usage or cannot complete accounting
- **THEN** cost remains unknown rather than zero, the conservative reservation remains and there is no retry.

#### Scenario: Missing or different billed model
- **WHEN** usage is absent or the responder reports no model identity or a revision outside the explicitly priced selection
- **THEN** no candidate is delivered, no tariff is guessed, cost stays unknown with conservative reservation, and the outcome remains typed unavailable.

#### Scenario: Dependency ignores cancellation
- **WHEN** the caller times out but inference/accounting is still unsettled
- **THEN** the process slot is held until settlement and no late candidate is delivered; logical UI expiry does not claim that an uncooperative SDK has erased its own prompt references.

### Requirement: Transient private text and safe transport
Text/candidates SHALL remain transient and no-store, excluded from app logs, traces, caches, URLs, browser storage, SSR, durable messages and accounting payloads. Metadata SHALL contain no text/hash, owner or trip identity. Only minimal text/anchor/schema data SHALL reach an explicitly accepted processor. User action SHALL NOT imply processor/privacy authorization.

#### Scenario: Failure path observation
- **WHEN** parsing, model, bus, ledger or transport fails through actual Host/AI composition
- **THEN** logs/errors and durable storage contain no input, prompt, completion or candidate values and responses retain no-store.

#### Scenario: Explicit processing and later logout
- **WHEN** an authorized user explicitly dispatches text and later leaves/logs out/changes owner or its lifetime expires
- **THEN** application references are cleared, no model conversation/history is retained and the UI makes no unsupported claim about processor retention or physical RAM erasure.

#### Scenario: Global durable bus policies
- **WHEN** the existing Host durable queue/transaction policies are active
- **THEN** extraction request/reply remains non-durable without inbox/outbox/deadletter body or replay, with unrelated transport policies preserved.

### Requirement: Separate mechanical and model quality acceptance
Acceptance SHALL separate fake contract safety from real-model extraction quality on a predeclared fictional corpus. Inputs, expected slots/issues, clocks, partitions, thresholds and prompt/model identities SHALL bind before outputs. Failures/skips/fallbacks SHALL retain denominators. Model execution, private-text use, target and rollout SHALL require separate authority.

#### Scenario: Fake tests pass
- **WHEN** structural/HTTP/browser/transport tests use fake IChatClient
- **THEN** only application mechanics are accepted and model quality/latency/cost remain not evaluated.

#### Scenario: Real evaluation or failed safety
- **WHEN** an authorized run misses an oracle, safety threshold or useful extraction threshold
- **THEN** it retains every case and failure and stays failed without enablement; the published qualification corpus is not called an independent holdout, and any separately authored sealed holdout stays inaccessible to tuning.

#### Scenario: Fixed quality anchor and honest latency
- **WHEN** the fictional quality harness evaluates relative dates
- **THEN** it supplies frozen reference data to the production extraction and normalization path while deadlines/accounting use real execution time; component timings are not represented as full Host/bus/browser latency and no public client clock override is added.

#### Scenario: Independent holdout acceptance
- **WHEN** real-user model acceptance is sought after published-corpus qualification
- **THEN** a new independently authored and sealed fictional holdout is bound after configuration freeze and before any of its outputs, evaluated without tuning access under separately authorized time/cost limits, and kept separate from qualification and mechanical evidence.

#### Scenario: Historical compatibility and unrelated gates
- **WHEN** M3 source checks pass
- **THEN** catalogue1, protected historical plans, both immutable city packages and legacy v1/explicit v2 remain unchanged, while human A/B, field calibration, target provisioning, M2 rollout6.3 and Moscow v2 remain separate.
