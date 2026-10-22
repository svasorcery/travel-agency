# Rail consumer refinement — 2026-10-21

This is the additive design for the confirmed "Выбрать поезд и добавить намерение в план" scenario, within the existing Trips M2 change. Moscow walking generation, its frozen quality corpus and accepted historical evidence remain intact. This document resolves implementation choices; new implementation/tests have not run.

## Authority, source identity and current status

Initial fresh base e654c76915103a82083ef2abc01c0579117c9749 remains ancestor of integrated dev a8721effd9056459d217966083816455fbea599d. The integration was an ordinary fast-forward, not a substitute branch base. Controller receipt: D:/_Projects/_github/travel-agency/TestResults/trips-m2-pre-rail-sync/20261021T1439423939Z/local-dev-integration-receipt.json. It records the complete 695-file backup, 139 reviewable WIP files unchanged byte-for-byte and zero overlap between upstream25 and tracked WIP32 paths. Task1.5 requires controller validation; this author does not close it.

Dependency integration is tracked by the controller in [rail-dev-integration.md](rail-dev-integration.md). The a872 lock cache is retained at D:/_Projects/_github/travel-agency/TestResults/trips-m2-dependencies-a8721eff/node_modules; the root consumer was changed from the owned junction to an exact byte-verified physical copy because local security requires a physical tree. Controller reports consumer7/7, dependency validator15/15 and local security60/60; final receipt remains authoritative and1.5 is not closed here. Full network npm audit remains separately blocked/pending approval; no CI or full security acceptance is inferred. Historical e654 walking S/browser/build closures remain historical.27 tasks now contain17 prior accepted tasks;1.5 and4.5–4.8 remain unchecked. No real Q output is permitted while5.2 PHASE A is paused.

User authority covers source implementation/local fictional tests/audit and this Rail refinement, not stage/commit/push/merge, Host/DB/schema/keys, real provider/model execution or enablement. Complete OSS/source plus fictional demo is supported without external operator activation.

## Published contract and retention decision

Read [Rail main spec](../../../specs/rail-schedule-search/spec.md), especially "Attribution and restricted retention" and "No durable or browser retention". It prohibits supplier data persistence/export/combination. A minimal train/source/time note is still source data: calling it historical does not bypass that contract.

Therefore the public component displays real timetable data transiently, but Trips disables automatic stage for every real-profile row with a clear source-retention explanation. No pseudo-live allow flag, automatic stripping of source fields, fallback to fictional, or conversion of real data into fictional is introduced. A future lawful real projection requires separately revised Rail capability and rights evidence; real-source enablement alone cannot unlock stage. Fictional selection→stage→explicit Save→reload is the complete current scenario. Manual M1 editing remains ordinary owner-authored text and does not acquire authoritative Rail meaning.

## Reuse and component boundary

Extract a small standalone reusable Rail search component under apps/web/src/app/rail from the existing RailPageComponent template/styles/transport/banner code. RailPageComponent becomes its standalone /rail wrapper; Trips embeds the same component with optional selection affordance. Keep the existing RailState and RailApiService; enrich their narrow current-selection check rather than creating another client, autocomplete/search state machine or backend aggregator. No .NET module internals, registry, framework, DTO persistence changes or dependency package additions. M1 authentication/availability controls the workspace and existing default-off Rail configuration determines its profile/404; add no consumer-wide feature-flag framework or new Rail availability endpoint. Merely opening the picker sends no provider search/request and never resolves a city/date automatically. Existing source configuration defaults remain unchanged.

Shared component owns its own RailState instance, network cancellations, timers, source/criteria authority and banner lifecycle. It exposes only a short-lived current selected-row event/context, never private Trips data. The selection action rechecks state synchronously and emits only a member of the present result; parent validates the opaque Trips foundation immediately before append. There is no await gap between final guards and local staging, no durable selected-row store and no accepted stale event replay. Component teardown calls dispose and removes the CSP listener. Keep browser-only transport and transferCache:false.

Keep one picker per active route, so existing rail-* control IDs can remain; use component-local focus querying rather than document-global assumptions. Keep the standalone page main/H1/header in RailPageComponent and make the reusable content a semantic section with appropriate result headings, so embedding creates neither nested main nor a second page H1. Do not add a generalized ID/variant framework. Standalone /rail has no Trips save callback or owner data.

## Inputs, placement and form behavior

Inputs are explicit from/to source refs chosen from current station suggestions, departureDate and train/suburban/all. Never infer station from the Trips destination/origin city. No private title, wishes/interests, adult count or budget goes to Rail. Station q is only the user's explicit station input.

Trips supplies target-day options from the captured saved plan dates. Label them "День плана для намерения"; keep Rail "Дата отправления" independently chosen, with an explanation that they may differ and no feasibility/connection claim follows. Changing target day does not mutate the M1 form or rewrite departureDate. Require an explicitly selected existing plan day before stage; it is not an unsaved brief/date edit.

Render the picker as a sibling after the existing M1 form, never under it. Its search form submits only Rail search. Open/close/choose/target-day helper buttons use type=button. Enter in autocomplete/search must not trigger M1 Save; verify actual DOM ancestry and HTTP effects in browser tests. Opening moves focus to its heading/first input; close and successful stage restore focus to a meaningful editor control. Keep labelled fields, keyboard station selection, live status and clear disabled explanations without a modal framework.

## One opaque Trips foundation, atomic local append

Refactor existing TripsStateService cleanEmpty into a shared clean-saved predicate plus the existing empty-only walking wrapper. Use the same WeakMap-backed opaque ticket, session/read/edit generations, selected plan/revision and monotonic foundationVersion. Expose a narrow captured clean-saved foundation/current check/append seam for Rail; do not add another CAS/epoch machine. A cloned public plan alone is not authority.

Open only with saved/current owner plan, clean buffer AND clean form, no pending mutation/read, barrier, lost-command uncertainty or missing/deleted state. A nonempty saved plan is allowed. Every existing invalidate path (owner/logout/plan navigation/delete/refresh/save/edit/formDirty/read/revision change) invalidates the Rail foundation and closes/destroys pending picker state. Changing the independent target-day selector does not count as a Trip edit. The Rail component's request epochs remain its source authority; they do not duplicate M1 write authority.

At click: validate current foundation and source eligibility, explicit target day, fewer than8 blocks in that day, then create ONE new UUID block and ONE full prospective ReplaceTrip with that block appended. Preserve all old block IDs/order/notes, every other day, title, brief, currency, budget and existing estimates. Run actual decodeTripWrite against the entire prospective write, including100/500 Unicode scalar bounds, ordinary128KiB UTF-8 body cap and existing M1 invariants. Recheck foundation and synchronously call existing edit once. Failure leaves editor exactly unchanged and presents an actionable status. Never add a blank block then update it in a second operation.

Successful local edit invalidates the foundation and closes the picker, so doubleclick/repeated stale callback cannot append again. Walking capture/read/stageEmptyFoundation remains empty-only with its existing owner GET+availability checks. A common supported path is walking stage→explicit save→open Rail→append→explicit save. No hidden PUT/auto-save, implicit source refresh, retry, rebase, owner-read side effect or generated receipt workflow. Rail staging needs no new owner GET: local foundation guards the synchronous append and explicit PUT/CAS checks the server revision; walking adoption retains its existing two-GET proof. M1 Save/CAS/lost-PUT uncertainty remains authoritative; remote edits after the last observation can still produce412. No claim of cross-client instant freshness.

## Exact Rail selection eligibility

Selection is fictional-only. At every choose click and immediately before staging:

- Require current strict decoded result kind complete or limited with at least one current item, exact item membership/key/object identity from this result, matching source/from/to/date/mode request, profile/profileEpoch and catalogueGeneration against current reference and selected station refs. A previous A reply cannot be accepted after A→B→A.
- Reference must be successful complete/limited with valid attribution, non-null receivedAt/expiresAt and generation. A later station/reference failed/skipped/read failure disqualifies the pending choice even if RailState previously retained the same-epoch reference; clear choice/source authority and require explicit manual reselection. Do not reuse a stale prior successful bundle.
- Parse non-null absolute result and reference receivedAt/expiresAt at click, require finite times, receivedAt no more than the existing decoder's1s future tolerance, receivedAt<expiresAt and now<both expiries. Result expiry cannot exceed reference expiry. Selected item/mode receivedAt must match their decoded evidence. Never renew TTL or use completedAt as receivedAt. Timers provide cleanup, not the sole authority check.
- Selected journey must have intact required identity/time/route data and tickets.damaged=false. Unknown/not-provided ticket facts are allowed; damaged ticket data disallows this choice even when its timetable displays. Other rows remain independent; valid selected row in a mixed result uses the envelope's original shorter expiry.
- Limited envelope is admissible only when the selected mode is complete, or selected mode is limited solely by page-limit and/or interval-service-unsupported and the row is a concrete scheduled direct journey. These are current parser reasons, not a general unknown-reason allow list. Other mode failure/quota-skip may make the overall result limited; keep its warning visible. Selected mode failed/skipped, unknown limiting reason, malformed/damaged response, interval-only/zero-item result, failed reference or missing/failed attribution cannot stage.
- For real profile the source-retention refusal applies even if every freshness/identity check passes. Unknown profile never receives fictional privileges.

Closing, criteria changes, profile notice, expiry, attribution failure or Trips foundation invalidation destroys the pending choice and cancels reads/timers. No automatic refresh. Server changes not yet observed have only the published best-effort expiry/profile-notice guarantee, not push revocation.

## Small historical projection, no new trusted DTO

The only persisted fields are an ordinary M1 TripBlock: generated blockId; title "{transport noun} {exact optional number} (демо)" when number is non-null and the entire title passes the actual M1 title rules including100 Unicode scalars; otherwise fixed "Поезд (демо)" for train or "Электричка (демо)" for suburban; startTime=null; endTime=null; estimate=null; note plain text at most500 Unicode scalars. Do not copy departure into HH:mm or a fare into budget/estimate.

Use this exact small projection format, substituting original decoded strings, not HTML/URLs:

    Вымышленное демо; историческое намерение, перепроверить.
    Источник=yandex-rasp; профиль=fictional.
    Вид={transport}; нитка={threadReference}.
    Станции={from.code}→{to.code}.
    Отправление={departure}; прибытие={arrival}.
    Получено={journey.receivedAt}; истекает={result.expiresAt}.

Thread reference+source+transport+station endpoints+departure/arrival identify the selected snapshot without treating number/title as a global train identity. The title retains an ordinary train number for calendar recognition when it fits and is M1-compatible; a missing/overlong/control-containing optional number is deliberately omitted using the fixed noun fallback, never truncated or normalized into another factual number. Omit optional source title/carrier/platform/stops and all ticket data; the note does not need the optional train number because the exact essential snapshot identity is already present. The panel shows human-readable route/number; the note retains exact small identity. Persist the selected row's original receivedAt and enclosing expiry, not the editor/save time; fictional source marker cannot claim real Yandex evidence.

Current strict Rail bounds permit station code≤128UTF-8 bytes each, thread≤100 UTF-16 code units/400bytes, optional labels≤1024bytes and key≤1024bytes. Two maximal station refs plus identity/time labels can exceed500scalars. Build the exact note, count Unicode scalars and actual whole-write bytes, reject overflow without truncation/hashing/replacing essential facts. Explain that this source reference cannot fit the note and suggest choosing another result or writing an ordinary manual intention. This is an intentional bounded subset, not a false universal fit guarantee.

Saved text may be edited by the owner. After save/reload it is only an M1 intention; there is no trusted Rail badge/panel, parsing/rehydration of note, timetable freshness assertion, ticket/booking guarantee or fare recalculation. The generated fictional marker survives an unedited round trip; manually deleted/changed marker does not create trusted status.

## Facts and attribution in the transient panel

Preserve complete/empty/limited/skipped/failed distinctions and per-mode reasons. Show departure/arrival dates with original offsets; IANA zones and sourceUpdatedAt remain explicitly unknown. Show source, row receivedAt and original result/reference expiry; receivedAt is reception, not provider update time. Preserve all independent ticket states and exact amount strings (including9007199254740993.05), never JS Number conversion, adult multiplication, totals/FX or seat/realtime claims.

Carry the approved240×130 exact-allowlisted banner, sandbox="", referrerpolicy="no-referrer", source text/link and restricted CSP reaction through extraction. Do not execute source HTML. Enforced matching frame-src/child-src/default-src violation and frame error call attributionFailed, clear data and prohibit selection; report-only policy events do not prove a load failure. Frame load alone proves neither official banner visibility nor real acceptance. Fictional browser uses only local /rail-fictional-banner.html; no real domain calls. No private Trips fields in DOM URL/referrer, telemetry, local/session storage, history, SSR or transfer cache.

## Interface and dependency preflight

| Task | Inputs and existing interface | Owned/shared files and prerequisite | Output/review |
|---|---|---|---|
|1.5|Controller integration receipt, original ancestry, a872 lock/cache/physical consumer|Controller owns git/dependency/ledger verification; docs author leaves unchecked|Current integration identity only; no new product proof|
|4.5|RailApiService stations/search with RailExpectedAuthority; RailState criteria/expiry/dispose; strict RailJourney/RailSearchResult|apps/web/src/app/rail/rail-search.component.* (new), rail-page.component.*, rail-state.ts and focused tests; existing shared/ts/api-client Rail DTO/decoder stay authoritative. Depends1.5+accepted refinement review|One public component + eligibility; standalone regressions|
|4.6|TripsStateService captureEmptyFoundation/current/read/stage seam, edit and decodeTripWrite;4.5 current selection|apps/web/src/app/trips/trips-state.service.ts, trips-page.component.*, narrow Rail projection helper/tests if retained; shared Rail component touched only by same implementer. Existing trips-itinerary-state consumers unchanged in behavior|4.5+4.6 one coupled fresh gpt-6.1-sol worker; primary personally reviews whole patch, fresh task reviewer/recheck|
|4.7|Accepted coupled patch and current a872 dependencies|Existing tests/travel-e2e/trips-demo, standalone Rail fictional browser suite/fixtures; no frozen walking quality inputs|Fresh test implementer, fresh reviewer;0external fictional browser receipt|
|4.8|Accepted4.5–4.7 and source hashes|Controller/current structural/build scripts, root config/dependency identity; harness binding base updated only by owning controller/implementer|Current proof before frozen Q5.2; no install=CI/security claim|
|5.2|1.5/4.5–4.8 accepted; frozen walking files unchanged|Existing Q harness/run-binding; source base=a8721eff; fixture asOf=2026-10-21T14:16:33.0000000Z|PHASE A paused before any real Q output; Rail cases excluded from12Q+12S|
|6.1|All approved final source, including Rail/root config/deps, own exact local receipts|Fresh most-capable reviewer, one unified confirmed fix wave and recheck|Final source review;6.2publication CI/6.3real target still separate|

## Risks and checks required

Named blockers for implementation review: nested forms; stale/null TTL and failed-reference reuse; nonempty foundation/UTF8 preflight; extraction regression in standalone Rail and scoped focus; dirty brief versus independent target day; iframe/CSP failure; fictional persistence/real retention; lost PUT/late reply; original fresh-base ancestry; historical receipts and changed dependencies. Tests must exercise actual component+state+strict codec boundaries, not duplicate guessed validators.

No new geography/package, model, migration, provider call or Q result is produced by this planning refinement. Separate source, demo integrity, human/live suitability, CI and target acceptance remain visible.
