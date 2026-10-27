# Trips M1: private structured planning

M1 creates a calendar foundation from a structured brief and saves explicitly edited user blocks. It does not generate an itinerary, verify feasibility, quote services, book, pay, cancel, or call models/providers. The separately approved M2 source adds an optional Moscow demo; M3 natural-language input remains a later change. Source/local, CI and target acceptance are tracked separately.

## Accepted input and domain rules

The local catalogue version `1` contains `moscow` (Москва, `Europe/Moscow`), `saint-petersburg` (Санкт-Петербург, `Europe/Moscow`) and `kaliningrad` (Калининград, `Europe/Kaliningrad`). These are first-party city identities, without station, airport, hotel or POI coverage claims. A saved destination keeps its display name, zone and catalogue-version snapshot when its ID stays unchanged.

A brief selects one destination, optional origin, inclusive date-only range of 1–14 days, 1–9 adults, currency `RUB`/`EUR`/`USD`, optional total budget, unique interests from `architecture,culture,food,nature,history`, and pace `relaxed`/`balanced`/`active`. Historical dates are valid. Creation produces revision `1` with one empty day per date.

Plan and block titles contain 1–100 Unicode scalars; optional notes contain at most 500. Invalid surrogate sequences and control characters are rejected; notes permit newline (`\n`). Text is preserved as user content. Each day permits eight blocks, at most 112 across a plan. Block UUIDs remain unique and stable through explicit editing, moving and reordering; array order defines display order. The complete date-sorted day set is required on replacement.

Local time boundaries are both absent or both `HH:mm` in one day, with end later than start. `10:00–11:30` is the user's intended slot, not a provider timetable or proof of opening hours, travel time or availability. Overlap is accepted with `OverlappingTimes`.

Amounts are exact decimal strings (`0`, `123` or `123.45`), normalized to two decimal places. Negative, exponential, leading-zero and fractional-precision variants are rejected. Each value is at most `999999999.99`, and estimates use the brief currency. For blocks with estimates `100.10`, `200.20` and no estimate, the known subtotal is `300.30` and the unknown count is `1`. This is neither a complete trip price nor proof that the trip fits a budget; exceeding the stated budget adds `KnownEstimateExceedsBudget`. Calculation uses checked integer minor units, without floating point or FX.

Shortening cannot drop a populated old date. First move/remove its blocks and save. New dates begin empty. Changing destination/currency requires the saved plan to be empty first. Other valid preference edits preserve the submitted blocks; there is no automatic replanning.

The editor distinguishes unapplied parameter fields from accepted local calendar changes and the saved baseline. Applying parameters does not save them. New dates must first be saved empty before adding/moving their blocks; changes to blocks on dates being removed must be saved before shortening. The parameter-only reset preserves independent block/title edits, while explicit adoption of the server version discards all local form/calendar edits. Ordinary save replies preserve later edits.

## Application and persistence boundary

`TripPlanService` owns create/get/list/replace/delete orchestration. `OwnerIdentity` contains the trusted issuer and normalized GUID subject. The API derives it from the validated principal; no write command can choose ownership, IDs, revision, timestamps, destination metadata, warnings or subtotals. Foreign/unknown detail or mutations return the same not-found outcome. Application verifies row ownership before requesting decryption, in addition to owner-scoped persistence queries.

`ITripPlanStore` owns owner-scoped row access, deterministic `updatedAt DESC, TripId ASC` pagination, the atomic 100-plan owner capacity check, and revision-checked replacement/deletion. It never upserts a missing plan. Every no-op still checks CAS, while preserving revision, timestamp and protected envelope. An effective replacement increments revision once; `Int64.MaxValue` produces a typed terminal conflict. Production timestamps come from injected `TimeProvider` and are normalized to UTC microseconds before snapshot protection to round-trip PostgreSQL `timestamptz` exactly.

`ITripPlanCodec` owns one canonical full-detail representation used for both protected payload and HTTP output. Before protection/commit, Application checks the full representation including derived/server fields and a maximum revision/timestamp reserve against 128 KiB. A separate HTTP body limit protects parsing. There is no truncation or post-commit rejection of accepted content. Reads revalidate identity/revision/timestamps and the bounded representation.

`ITripPlanProtector` binds the private payload to owner, TripId, schema version and revision. Infrastructure owns the actual framework protection and key provisioning. `ITripPlanCursor` validates bounded, owner-bound opaque cursors (at most 256 characters). List pages contain at most 20 bounded summaries: identity, revision, title, destination ID/display name, dates and updated timestamp. They contain no blocks, notes, interests or budget. Failure to decrypt one result fails the page; unavailable storage/protection never becomes an empty success. Pagination allows movement/repetition during concurrent updates and makes no snapshot-isolation promise.

The create HTTP endpoint explicitly opts out of Wolverine automatic transaction middleware. `TripPlanStore.TryCreateAsync` owns the PostgreSQL transaction and owner-capacity advisory lock, and commits before the endpoint returns success. Other Host transaction policies remain unchanged. Actual-Host regression tests cover creation, concurrent last-slot admission, protected editing, owner/CAS guards and reopening after a Host restart.

The service performs one persistence attempt per command. Cancellation, timeout or lost transport is not automatically retried. A subsequent GET/list reports current authoritative state and cannot prove which lost command committed; M1 does not provide exactly-once creation or a generic command-receipt framework.

## Verification and enablement

Core/Application unit tests use xUnit v3 and Shouldly without network, database, model or provider access. The Application fake codec exercises orchestration and actual UTF-8 byte boundaries; production canonical/protection/relational/HTTP behavior has separate Infrastructure/API tests and receipts in the change's verification record. Passing these unit tests does not establish deployment readiness, live key/schema provisioning, real sources or itinerary quality.

Trips remains default-off. Enabling it requires the separately authorized schema and protection readiness path; ordinary startup must not provision its schema or keys. See the authoritative [change design](../../openspec/changes/archive/2026-10-20-trips-m1-structured-planning/design.md), [capability specification](../../openspec/specs/trip-plan-workspace/spec.md) and [task evidence](../../openspec/changes/archive/2026-10-20-trips-m1-structured-planning/tasks.md).

M1 source acceptance is complete: all 21 tasks, independent review fixes, PR58 and post-merge CI are recorded in the [completion receipt](../../openspec/changes/archive/2026-10-20-trips-m1-structured-planning/finalization.md). Archiving does not enable Trips or establish generated-route quality.

The [local verification contract](../../openspec/specs/trips-local-verification/spec.md) is also accepted: PR70/PR71 provide an isolated real Host/Keycloak/PostgreSQL browser journey with complete service restart and protected-state comparisons. All 13 tasks and final local/PR/post-merge receipts are retained in the [archive](../../openspec/changes/archive/2026-10-24-trips-reproducible-local-e2e/archive-verification.md). Follow the [local stack guide](../../tools/trips/README.md) for explicit commands. Fixed-time E2E does not establish current data, human route quality or shared-target readiness.

[Moscow data maintenance](../../tools/trips/data-maintenance.md) provides explicit publication/expiry inspection, two-build candidate preparation and current-release reproduction. The [accepted contract](../../openspec/specs/trip-data-package-maintenance/spec.md) and [PR73 archive](../../openspec/changes/archive/2026-10-25-trips-moscow-data-refresh/archive-verification.md) retain all 8 tasks, historical-plan/expiry regressions and successful final PR/post-merge CI. V2 is deferred pending a genuinely new snapshot and separately accepted release; existing v1 and real-user defaults remain unchanged.

## M2 source and policy boundary (source/demo and ordinary CI accepted)

### M2.2: Saint Petersburg and explicit v2

The completed [M2.2 archive](../../openspec/changes/archive/2026-10-27-trips-m22-saint-petersburg/archive-verification.md)
adds a separately pinned Saint Petersburg outdoor package (20 POI/62 directed
paths) through the same policy, validator and protected M1 saving. All27 tasks are complete. PR79 and final PR/post-merge ordinary CI passed, including both full-stack city profiles; see [publication receipts](../../openspec/changes/archive/2026-10-27-trips-m22-saint-petersburg/publication.md). The five current contracts are in `openspec/specs`; human A/B, field calibration, target and rollout acceptance remain separate. The original Moscow files, pins, expiry, catalogue1 and saved codec
are unchanged. Both city readers are default-off and TestOnly.

The existing routes opt in via exactly one `X-Trips-Itinerary-Version: 2` header.
Without the header they retain the complete Moscow-only v1 behavior. Other,
empty, repeated or comma-separated versions return400. V2 GET
`/api/trips/itinerary-availability` requires exactly one `destinationId` query:
`moscow`, `saint-petersburg` or `kaliningrad`. Kaliningrad returns explicit200
unsupported without a data identity/source read; supported but unready cities
return503. Availability does not depend on private plan storage.

V2 POST `/api/trips/{tripId}/itinerary-candidate` retains the exact empty `{}`
body, no query and strong If-Match. Destination comes only from the saved owner
plan. Owner/revision/nonempty/scope guards precede source selection; both cities
share the existing admission gate/deadline. No city fallback, cache or retry is
introduced. Encoding and separate v1/v2 TS decoders bind city/zone/catalogue,
dataset/version/hash, profile/source and the fixed public data offer.

The UI requests v2 for the saved foundation. Destination changes invalidate its
opaque epoch synchronously, including A→B→A. Adoption rereads owner state and the
same city's availability; it transfers only into the local editor. Save is the
ordinary M1 CAS. The preview links only that validated city's full offered files,
including the SPb exclusions profile. Unsupported, unavailable, empty and partial
remain distinct. Access/hours/prices/calibration and human quality remain unknown.

`Trips:Itinerary:Withdrawn` remains a global stop. A separate
`Trips:Itinerary:Destinations:{destinationId}:Withdrawn` withdraws only that city.
These controls do not disable M1 reading or manual editing. Historical snapshots
remain historical; a note never establishes current access or trusted provenance.

The following paragraphs retain the phase-bound accepted Moscow M2 history.

M2 adds an optional Moscow1–3day outdoor itinerary preview over the existing private M1 editor. It remains default-off. The final independent full-source audit, four test/UI fixes and same-auditor scoped recheck passed with no open findings. M1 APIs,snapshots,keys and schema are unchanged. PR65 and ordinary PR/post-merge CI are accepted;target enablement and human field/Q_live remain pending. Exact phase-bound status and receipts are in [the M2 verification](../../openspec/changes/archive/2026-10-22-trips-m2-moscow-itinerary/verification.md).

Core immutable facts bind dataset identity/version/hash,saved destination snapshot,typed evidence,18–48POIs/60–256directed edges and at least3POIs per supported tag. Factories return ErrorOr and deep-copy collections. Source-record identity is exact-revision Wikidata CC0; walking is explicitly modelled/ODbL with nullable physical observations,software preparation,null human reviewers and not-evaluated calibration/quality. Access/hours remain unknown without independent permitted evidence. Applicability is at most90days(identity),7days(verifiedaccess),30days(map extract publication),using the saved IANA zone through the selected local day's end. Reading/rebuilding does not renew source age.

One offline BCL-only .NET helper prepares a small immutable JSON/evidence/attribution bundle from a local OSM extract. Runtime reads the bundle and has no OSM parser,router service,map database,tiles,provider call or model. The reviewed preparation produced20POIs/90modelled paths twice with identical hashes in about16seconds and under214MiB working set. Two POIs have no selected connections; no repair is invented. Original titles/tags/dwell are editorial recommendations. Source/path hashes and mathematical minute ranges do not certify access,safety,empirical travel time or legal/human approval.

Core policy uses stable-ID beam16 search with pace targets2/3/4stops and explicit limits,unique POIs acrossdays,distinct requested-interest coverage and directed date-valid connections. Unknown hours/modelled estimates stay visible independently of filled coverage; default money/times are null. Historic notes include bounded dataset/POI identity and OSM/ODbL credit. Independent task review/recheck passed with a fresh136/136Unit receipt and maximum note386/500Unicode scalars. The current shape implies at most7056expansion attempts under validated bounds;20000 remains a defensive ceiling,not a reached benchmark or global-optimality claim.

Application's read-only candidate service retains owner Get, exact revision and saved-empty guards. It captures one clock instant; unsupported scope needs no source or admission. Supported requests share a process-wide two-slot/no-queue gate and cooperative two-second deadline. After Core generation and independent validation, the actual API encoder produces complete bounded bytes and the temporary ordinary plan passes the existing maximum-server-field reserve before final owner/revision/expiry read. The endpoint writes these frozen bytes. No persistence, revision/timestamp changes, protection writes, background work or retry. Final-read races remain best effort; adoption rereads and ordinary M1 CAS remain required. Accepted backend receipts remain294Unit and85boundary+30M1HTTP. Final UI fixes passed864web and fresh75Trips+9standaloneRail browser cases; historical5M1 browser cases are not claimed as a current80case run. Final source/local audit and ordinary PR/post-merge CI passed;target acceptance remains pending. The counts in this paragraph are phase-bound local receipts;final CI identities/counts are in [completion review](../../docs/architecture/trips-completion-review.md).

Unit facts/policy fixtures are explicitly FICTIONAL. The strict pinned loader passed98leanfilesystemIntegration tests; the independent Core result validator has accepted110focused/246fullUnit receipts. API/client/UI/browser,D_demo/S/Q_demo and final full-source/fix reviews passed. The first bound24record run filled12/12Q targets and passed13raw-body Node checks; no tuning or rerun followed test/UI fixes because producer/Data/model/decoder closures are unchanged. Ordinary PR/post-merge CI is accepted;target rollout and human field/Q_live remain pending. Source tests and offline build do not establish route feasibility or enablement.

[PR75 explanation closure](../../openspec/changes/archive/2026-10-25-trips-demo-explanations/archive-verification.md) retains all four tasks, source review, successful PR/post-merge CI and the rebuilt local-demo preservation check. The [itinerary contract](../../openspec/specs/trip-itinerary-candidates/spec.md) now distinguishes whole-trip/day interest coverage and crossing allowance on the upper walking bound. Human A/B acceptance is deferred with blank scores; v1, v2 deferral and real-user defaults remain unchanged.

The [PR77 E2E repair archive](../../openspec/changes/archive/2026-10-25-trips-e2e-original-response/archive-verification.md) records five completed tasks and accepted PR/post-merge CI. Candidate verification captures the single original response before Chromium can release its inspector resource, retaining all 143 prior assertions and adding two one-POST checks. Thirteen browser regressions and complete real-service restart checks passed; product contracts, v1 and real-user defaults are unchanged.

## M3: review a short Russian brief (source only)

The independent `Trips:BriefExtraction:Enabled` startup flag is false by default and excluded from Production. Manual M1 editing does not depend on extraction, AI availability, accounting or city-package readiness. A successful candidate is transient evidence for a separate review; it does not read or save a plan and does not generate an itinerary. Model quality, real processor execution, private-text permission and rollout are not evaluated or enabled by this implementation.

There are four separate user actions: **extract text → review/correct/select and transfer into the form → ordinary Apply/Create/Save → ordinary itinerary Generate**. Transfer changes only explicitly selected fields in the form. It does not run Apply or Save. Dates transfer together; duration is auxiliary evidence and never becomes an M1 property. The existing populated-calendar, owner and revision checks still govern ordinary Apply/Save.

`GET /api/trips/brief-extraction-capability` publishes only public local configuration; it never probes AI/NATS, the database, protection, packages or ledger. Off returns 404 before authentication/dependency resolution. When on, trusted authentication is required. Unconfigured metadata contains null processor/model/policy identifiers. A configured result describes a reviewed processor/model/notice tuple, not model readiness, quality or permission to process text. Only the existing Anthropic adapter and notice `trips-brief-ru-v1` are recognised in this first source slice. The public policy identifier is the lowercase SHA256 of the canonical JSON array `[processorId, modelId, noticeVersion]`; changing any tuple member changes consent binding.

`POST /api/trips/brief-candidate` accepts exactly `{requestId,text,locale:"ru",referenceZone:null|"Europe/Moscow"|"Europe/Kaliningrad",processingPolicyVersion}`. No query, If-Match, TripId, owner, browser clock or existing form content is accepted. The server captures the reference anchor before reading the body and spends one 5000 ms deadline on the bounded body read, transport, validation and response. Size ceilings are 1000 Unicode scalars, 4096 text UTF-8 bytes, 8 KiB request and 16 KiB complete result. UTF-16 half-open evidence offsets refer to the original unnormalised text and cannot split surrogate pairs. There is no automatic retry. Every outcome is no-store. Invalid input is 400; oversize is 413; busy/rate/budget is 429; invalid model output is 502; unavailable/refused/policy-changed is 503; deadline is 504. Neither problems nor diagnostics include source text or provider errors.

Versioned finite vocabulary (`trips-brief-lexicon-v1`, date normalizer `trips-brief-dates-v1`):

- Destination: catalogue 1 `moscow`, `saint-petersburg`, `kaliningrad`. Russian aliases are Москва/Москвы/Москву/Москве, Санкт-Петербург/Санкт-Петербурга/Санкт-Петербурге, Петербург/Петербурга/Петербурге, Питер/Питера/Питере, СПб, Калининград/Калининграда/Калининграде, plus the exact canonical IDs. Matching is case-insensitive with complete word boundaries. Mixed-script lookalikes, spelling guesses, transliteration and online lookup are excluded.
- Dates: YYYY-MM-DD, DD.MM.YYYY, full Russian day/month/year, `с … по …`, and сегодня/завтра/послезавтра interpreted only in the explicitly selected zone. Historical dates remain valid. Missing year, slash dates and missing relative zone stay ambiguous. A weekday contradicting a full date blocks the date group.
- Duration: `на N дней` or finite Russian number words один–четырнадцать (with день/дня/дней), inclusive. Explicit start plus duration derives an end; end plus duration never derives a start. Nights, weekends, business days, holidays and seasons require manual interpretation.
- Adults: explicit number plus взрослый/взрослого/взрослых, integer 1–9; finite words include один, два/двое, три/трое through девять/девятеро. `нас двое`, `два человека` and `семья` remain ambiguous, never two adults by default.
- Interests: positive explicit архитектура/архитектуру/архитектуры/архитектурой, культура/культуру/культуры/культурой, еда/еду/еды/едой, природа/природу/природы/природой, история/историю/истории/историей. Output order is architecture,culture,food,nature,history; duplicates collapse. `не интересует история` cannot produce history. Only explicit `без интересов` yields an intentional empty set; silence stays missing.
- Pace: relaxed = спокойный/спокойном/спокойно/неторопливый; balanced = умеренный/умеренном/сбалансированный; active = активный/активном/активно. Conflicting explicit paces are ambiguous.

Each of the seven fields has state `extracted`, `missing`, `ambiguous`, `unsupported` or `invalid`, value, evidence and alternatives. Non-extracted values are null. Missing has no evidence/alternatives. Invalid stated counts are shown invalid, never clamped; invented values or inadmissible spans reject the whole result. The Application validator independently checks the source grammar; it is not a model fallback and does not establish semantic recall or quality.

Controlled issue categories are `partyComposition`, `adultBounds`, `adultConflict`, `destinationChoice`, `multipleDestinations`, `cityUnresolved`, `cityOutsideCatalogue`, `paceConflict`, `durationBounds`, `yearMissing`, `reversedDates`, `dateDurationConflict`, `invalidDate`, `dateConflict`, `dateBounds`, `referenceZoneMissing`, `dateFormatAmbiguous`, `weekdayDateConflict`, `unsupportedDateExpression`, `unsupportedOrigin`, `unsupportedMoney`, `unsupportedChildren`, `unsupportedAction`, `unsupportedBooking`. Issue slots use only supported field names; deferred/action issues have an empty slot list and original-text evidence.

Origin, money/currency, children, hotels, POIs, transport, title, notes, blocks and requested actions are never extracted into M1. For example, `Из Москвы в Петербург на три дня, нас двое, бюджет 10000 RUB` may offer Petersburg and a three-day duration, while origin/money stay unsupported, adult composition stays ambiguous and dates remain missing. Existing form values are explicitly retained as not extracted. Unknown wishes remain visible in the original text; absence of an issue is not proof that no other constraint exists.

Candidates expire ten minutes after the server anchor, or at the next selected-zone midnight for relative wording, whichever comes first. Correction does not renew that lifetime. Confirmation must recheck monotonic TTL, the current capability/policy and (for a saved plan) exact owner-read revision before transferring selected fields. Leaving, logout, text/zone changes and later editor changes invalidate private transient state. Browser storage and SSR never retain extraction content.
