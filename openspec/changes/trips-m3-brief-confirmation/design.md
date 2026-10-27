# Design

## Context

See [proposal](proposal.md) for motivation and [research](research.md) for source provenance. This is a proposed first slice, not an accepted provider or runtime configuration. Base: `35953fd6c8feabf16acd7fb944201711e3d8c5fa`.

M1 already separates `briefDraft`, locally applied calendar buffer and protected saved baseline. Travel.AI already has feature-specific IChatClient extractors and Core NATS request/reply. Its current Flights model, UTC-based date prompt and parse-then-search use case do not define Trips behavior. Host globally enables durable local queues; privacy must be demonstrated through the actual transport composition.

## Goals / Non-Goals

**Goals:** short Russian wishes → bounded, evidence-linked candidate → explicit correction/field selection → existing form. Preserve manual editing and all M1 validation, non-destructive Apply, owner isolation, revision/CAS and separate Save.

**Non-Goals:** model-generated itineraries, booking, new cities/catalogue versions, geocoding, Moscow v2, package maintenance, runtime routing, free-text chat/history, automatic defaults/retries, new AI framework/core, or changes to Flights/Hotels/Rail capabilities. No existing local demo or frozen human-review material changes.

## Decisions

### 1. Deliberately small extraction vocabulary

| Item | First-slice behavior |
| --- | --- |
| destinationId | Exactly one catalogue1 destination: moscow, saint-petersburg, kaliningrad. Extraction support does not imply generation coverage |
| startDate/endDate | Date-only Gregorian values, both needed to transfer dates; complete explicit dates or narrow relative grammar below |
| durationDays | Auxiliary 1–14 inclusive days, not a new M1 field; with one definite start derive end=start+days−1 |
| adults | Only explicit count of adults 1–9; “нас двое/два человека/семья” stays ambiguous until user supplies an adult count |
| interests | Unique architecture,culture,food,nature,history in stable catalogue order; no interests mentioned means missing, not [] |
| pace | relaxed,balanced,active from a versioned finite Russian alias table; no mention means missing |
| originId | Deferred. Recognized origin clause is unsupported/manual-only, never mistaken for destination |
| totalBudget/currency | Deferred as a pair. Money wishes are shown unsupported/manual-only and existing form values remain untouched |
| title, blocks, notes, transport, children, hotels, POIs | Never extracted into M1 content; relevant unhandled clauses remain visible |

This preserves the example's useful destination, duration, interests and pace without inventing dates or treating two people as two adults. The preview asks for dates and adult clarification. A user can transfer the useful subset and finish the ordinary form manually.

Alternatives: all M1 fields would add financial-scope and origin-role ambiguity to the first slice. Dates-only or city-only offers too little value for the example. Proposed scope can be approved independently of a provider.

City aliases are first-party, versioned and finite: canonical IDs and Russian Москва/Москвы/Москву/Москве, Санкт-Петербург/Санкт-Петербурга/Санкт-Петербурге, Петербург/Петербурга/Петербурге, Питер/Питера/Питере, СПб, Калининград/Калининграда/Калининграде. Mixed-script confusables are rejected rather than admitted as aliases. Use ordinal case-insensitive matching after Unicode NFC and whitespace normalization, with exact word boundaries; offsets always refer to the original unnormalized string. No fuzzy autocorrection, transliteration inference or online lookup. “Петербург или Москва” is ambiguous; “Москва → Петербург → Калининград” is unsupported multi-destination. “из Москвы в Петербург” can yield SPb plus unsupported-origin. Unknown/near-miss city names never fall back to Moscow.

### 2. Candidate is evidence, never authority

A candidate has exactly seven supported slots (destinationId,startDate,endDate,durationDays,adults,interests,pace) and issue entries for detected deferred/unsupported wishes. No slot is added for origin or money. Per-slot state:

| State | Meaning | Transfer |
| --- | --- | --- |
| extracted | Unambiguous in-scope value validated against cited original-text span(s) | Only explicitly selected fields |
| missing | Not stated; no value inferred | Leave current form untouched; user can supply review value |
| ambiguous | Alternatives, missing year/timezone or adult composition uncertain | No selection until explicit manual correction |
| unsupported | Outside first-slice vocabulary/capability | Show source clause and manual path; no projection |
| invalid | Stated supported value breaks bounds/format | Show linked correction; no clamp/truncation |

Conflicts are separate issue codes, not a sixth guessed value. Both affected slots are blocked until correction: reversed dates, date/duration disagreement, contradictory adults/pace, multiple possible destinations. Duplicate equivalent interests normalize; an explicit “без интересов” yields an intentional empty set only after user selection. Negative preferences such as “не интересует история” must not become a positive history tag.

Model response is an untrusted structured span/intent proposal. The model cannot pick catalogue metadata, trusted timestamps, money, ownership, revision, tool actions, URLs or UI explanation strings. Strict parser rejects duplicate/unknown keys, enum/type/range mismatch, control characters, invalid Unicode, invalid span bounds, oversized lists and prose/fenced JSON.

Proposed leaf result: supported field entries with state, typed value where admissible, up to four evidence spans/alternatives per field; up to16 issues. Evidence uses UTF-16 code-unit half-open [start,end) offsets matching C#/TS strings, with surrogate-pair boundary checks; no copied source text. Application independently normalizes recognized spans through catalogue/finite lexicons and date/adult rules; it rejects unsupported generated values. Host builds controlled UI issue codes. UI derives excerpts from its own request text and renders them literally.

The external candidate fields object has exactly those seven keys, each with exactly {state,value,evidence,alternatives}; evidence is an array of up to4 {start,end} integer offsets and alternatives is an array of up to4 bounded values of that slot type. Extracted values are string city/date/pace, integer duration/adults or ordered unique string[] interests; non-extracted value is null. Missing has empty evidence/alternatives. Ambiguous may expose valid typed alternatives without choosing one. Invalid/unsupported carry evidence and controlled issues, never a clamped value. Model span proposals are validated before producing this normalized HTTP shape: an admissible span containing a stated out-of-range adult count yields an invalid slot, while an invented extracted count or malformed schema rejects the whole provider result. Parsed JSON unknown/duplicate fields and unmappable model claims are never normalized away.

Mechanical validation proves a date/city/count is permitted and matches a supported span grammar; it does not prove the model located every negation, conflict or unsupported wish. Always show the original text and the entire proposed field list, including the fixed “не переносим: отправление, деньги, дети…” notice. Missing-clause/semantic recall is measured separately. An unclaimed clause does not imply “no other constraints”.

### 3. Deterministic dates and exact money boundary

Capture `referenceInstant` once at HTTP admission using TimeProvider; never use the browser clock or model's “today”. Request includes an explicit user-visible `referenceZone` selected from the catalogue IANA zones (Europe/Moscow or Europe/Kaliningrad), or null. The UI may visibly preselect the current form destination's zone, labelled for relative words; request submission confirms that displayed interpretation. No browser/OS/geolocation inference. If relative wording exists with null zone, it stays ambiguous. The candidate echoes instant, zone and referenceLocalDate; no client-supplied instant is accepted.

Supported date grammar: YYYY-MM-DD, DD.MM.YYYY, full Russian day/month/year, complete “с … по …” pairs; “сегодня/завтра/послезавтра” anchored to the displayed reference zone. Explicit dates remain date-only without UTC conversion, past dates remain valid M1 input. “на N дней/на один…четырнадцать дней” means inclusive duration; nights, business days, weekends, next Friday, holidays, seasons, missing years and numeric slash dates require manual clarification, never nearest-future/year rollover guesses. Fixed weekdays accompanying an explicit date must agree or cause conflict.

The evidence parser, not the model, computes day arithmetic and checks calendar validity, leap years, rollover and 1–14 days. A duration alone is retained but cannot supply either date. One start plus duration yields a linked end; one end plus duration does not infer a start in v1. Full range plus a different duration blocks the date group. Overflow is invalid.

Relative candidates with a known referenceZone expire at min(admitted+10minutes, next midnight in referenceZone), other candidates at admitted+10minutes; relative wording with a null zone remains ambiguous and does not trigger a timezone lookup. TTL is checked at confirmation, not timer-only. At client dispatch capture monotonic time; compare elapsed since dispatch against the server-supplied expiresAt−referenceInstant duration (validated positive and at most600seconds). Subtracting the entire round trip is conservative because server admission occurs after client dispatch. Never compare expiresAt to browser wall time or restart TTL on receipt; invalidate on resume/bfcache/clock uncertainty where monotonic continuity cannot be guaranteed. Changing referenceZone/text invalidates the attempt synchronously; correcting candidate values does not renew TTL. After midnight no silent recomputation; user re-enters/reviews or explicitly extracts again.

Budget/currency are intentionally not transferred, even for “10000 RUB всего”, zero or complete exact amounts. They do not become 0/RUB/default budget or derived per-person/per-day totals. The ordinary form retains its current explicitly visible currency/budget; the preview says they were not extracted. Manual entry uses existing PlanningMoney: 0..999999999.99, integer or two fractional digits, exact strings/minor units, RUB/EUR/USD, no double/FX/rounding. “100,50”, “10 тысяч”, “$”, “по100”, “до …”, several currencies or contradictory totals require manual interpretation. Any later money extraction would need its own expanded contract and corpus, not silent parser widening.

### 4. Explicit UX and concurrency

Flow:

```text
manual form available
  → explicit “Разобрать текст” with provider/privacy notice
  → pending (at most one request)
  → separate review: original text, anchor, values, sources, issues
  → correction + per-field “перенести” choices
  → explicit “Подтвердить и перенести в форму”
  → briefDraft only
  → existing “Применить параметры” / Create or Save
  → existing itinerary Generate from a clean saved empty plan, as another action
```

No submit-on-Enter through nested forms. M3 buttons are type=button. Review fields are local only, never an interactive model conversation; manual correction makes provenance “исправлено пользователем”. Selection starts unchecked for every field; changing an existing value always shows old→new. Correcting or declining one issue never silently confirms others. Partial transfer is allowed for independently valid selected non-date fields; dates transfer atomically as a valid pair. Missing/deferred/unselected fields preserve current form values. A manual value must itself validate; no unresolved selected conflict can transfer. Unselected warnings remain visible and are never declared satisfied. Transfer has only five choices: destination, the complete dates group, adults, interests, pace. durationDays is editable review evidence for deriving a date pair, never a transferable field or new M1 property. It cannot borrow an unstated start/end from the current form: the user must explicitly enter the review start to derive an end. Changing a review start recomputes an end only if its derivation link is still explicitly selected; editing a derived end breaks that link and requires resolving any duration disagreement. Explicit supplied end dates are never overwritten automatically.

The diff labels retained form values as “оставлено в форме, не извлечено”. Existing new-form defaults (saint-petersburg,1adult,RUB,[],balanced) stay unchanged when unselected, but they are never presented as extracted or as resolving ambiguous wishes. Show retained origin/money beside relevant unsupported clauses; an earlier form origin is not silently rewritten or declared compatible. Do not invent a new M1 rule prohibiting origin=destination. Nonempty calendar conflicts remain subject to ordinary Apply, not hidden deletion or automatic repair.

Requests are allowed on a new unsaved form or an existing plan with dirty form/calendar, provided no M1 read/mutation/barrier or itinerary/Rail adoption is pending. Capture a monotonic ticket: owner/session identity epoch, route/plan-or-new-form generation, accepted saved revision (if any), editor version (including title/block changes), form version, text version, reference-zone version, attempt ID. Existing dirty values are captured for a diff, not transmitted. Any later manual edit, navigation (including A→B→A), logout, owner replacement, server revision adoption, new-form reset, delete, new attempt or text/zone change invalidates the attempt synchronously, cancels best effort and discards late replies. Candidate review edits have a separate review version and do not mutate the form.

Before transfer, recheck ticket, TTL, no pending action/barrier and selected-field validity. For every form, reread extraction capability and require the same configured processingPolicyVersion shown at dispatch; off/unconfigured/mismatch/error blocks transfer and invalidates the candidate without dispatching another model call. For saved plans, also perform an owner GET using ordinary auth/no-store and compare exact revision to captured baseline without adopting its payload into the editor; failure/404/412-equivalent revision mismatch blocks transfer and preserves dirty content. Recheck ticket/review version after that await; confirm pending blocks repeat clicks. New unsaved forms need the capability reread but no plan read. An authenticated API success alone cannot establish current ownership at UI receipt.

Atomic local transfer writes only selected values into briefDraft, calls the existing touch/dirty invalidation hook once, consumes the candidate once and preserves title, blocks, saved baseline and revision. It does not call changeBrief/applyBrief/submit/create/save/generate. Existing non-destructive Apply can still refuse a city change on populated plans or shortening occupied dates. Explain this before transfer where knowable; never delete blocks. Navigation guards cover text/review edits; logout clears all private memory unconditionally.

Fallback is always the unchanged manual form plus a specific status. There is no fake result, deterministic guess, automatic retry, silent rebase or auto-save. Text may remain in same-session memory for user correction after failure, and is cleared on leaving/owner change; no browser storage.

### 5. Smallest architecture seam and HTTP contract

Proposed code ownership:

| Area | Responsibility |
| --- | --- |
| Trips Application/BriefExtraction | feature use case, ITripBriefExtractionClient port, evidence validation/normalization and candidate rules; existing Core value objects reused |
| Trips Infrastructure/BriefExtraction | Core NATS port implementation, provider-independent transport mapping; no imports of Travel.AI implementation or other module internals |
| Trips Api/Contracts + Endpoints + Composition | strict extraction-only HTTP reader/encoder, trusted owner, flags, routing/no-store; Api outside Composition has no Infrastructure import |
| shared/dotnet/Travel.IntegrationContracts.AI/TripsBriefExtraction | leaf BCL/versioned messages only, retained Trips/AI consumers; no domain rules/provider wire DTOs |
| apps/Travel.AI/TripsBriefExtraction | bounded extractor, scoped IChatClient/admission/prompt, usage ledger, controlled outcomes; provider wire mapping internal to AI capability, not shared |
| Angular + shared/ts/api-client | strict v1 decoder and transient review state beside existing editor |
| Host/AppHost | existing process/bus composition; only explicit routing/policy contribution if required by existing facade pattern |

No direct Anthropic SDK in Trips, new HTTP AI service, platform registry or generic orchestration layer.

Proposed GET `/api/trips/brief-extraction-capability`: authenticated, no-store, no query or body, at most2KiB. It reads only local approved public configuration, not AI/NATS/store/ledger/package readiness, and returns exactly {candidateVersion:1,status:"configured"|"unconfigured",processorId,modelId,processingPolicyVersion}. The last three fields are nonempty reviewed public identifiers of at most128ASCII characters when configured and null otherwise. Off returns404 before auth/dependencies; enabled identity follows401/403. Configured does not promise live model availability, quality or execution permission. UI displays a controlled notice for the reviewed policy version; an unknown version is unconfigured for the client. No active text submit is offered before a current configured read. This new DTO does not modify existing catalogue/itinerary availability responses.

Proposed POST `/api/trips/brief-candidate`, JSON only, no query/If-Match/TripId/owner fields. Body exactly `{requestId,text,locale:"ru",referenceZone:null|<allowed-zone>,processingPolicyVersion}`. requestId is a random UUID echoed for UI binding, not deduplication or authority. processingPolicyVersion binds the immutable public processorId+exact modelId+reviewed privacy-notice version tuple, using a canonical public-configuration digest computed identically in Host and AI. Changing any component necessarily changes that digest; it cannot be reused for a different processor/model. This is a hash of public configuration, never of user text. Trust ownership solely from authenticated principal. Independent gates: Trips:Enabled and Trips:BriefExtraction:Enabled, false by default and excluded from Production in the first slice; off404 before auth or dependency access. When on:401/403 normal identity rules;400 invalid contract;413 size limit;429 busy/limit (Retry-After only advisory, no retry);503 disabled AI responder/model/pricing/accounting/dependency unavailable;504 deadline. HTTP400 never echoes private text. Host checks the submitted public processingPolicyVersion against its current approved configuration before forwarding text. The AI responder independently checks the same policy/model configuration version before provider dispatch; mismatch returns503/ProcessingPolicyChanged with zero provider calls. Metadata publication and any external execution authorization are separate gates; absence of private-text permission is not repaired by publishing metadata.

A valid parsed result returns200 with `candidateVersion:1,requestId,processingPolicyVersion,referenceInstant,referenceZone,referenceLocalDate,expiresAt,fields,issues`. Field statuses may all be missing/unsupported; this is a valid review with no transferable values, never an infrastructure failure or a confirmed empty brief. AI refusal is a separate typed unavailable reason. Invalid provider output is502/InvalidModelOutput without showing a partial provider response. Client decoder failure is a distinct contract-error. Auth/disabled outcomes do not become empty success.

Endpoint requires no stored plan/package/schema/protection access to parse text: it validates owner only. M1 startup/readiness stays unchanged; extraction AI readiness failures do not poison manual Trips health. Add exact classification for both new routes in Trips response/guard middleware before generic M1 write validation, including ordinal-ignore-case/trailing-slash variants; do not treat arbitrary Trips POSTs as extraction. Existing M1 payload guards/If-Match rules remain intact. Actual-Host OpenAPI/HTTP tests verify this route through one normal endpoint map.

### 6. Travel.AI transport, execution and accounting

New immutable identities `travel.ai.trips-brief.requested` / `travel.ai.trips-brief.parsed`, Version1; subject `travel.ai.trips_brief`, stable queue group `travel.ai.trips_brief.workers`. Names are proposed and must be pinned by shape/identity tests. Forward text, locale, reference date/zone/instant, absolute deadline and random correlation/attempt ID only. No JWT, issuer/subject, TripId, existing brief/title/calendar, protected snapshot, contacts, history or raw-text fingerprint. Public alias/schema versions and processingPolicyVersion are transmitted; no raw text is sent to a different processor/model configuration on mismatch. Correlate reply to the exact attempt/version; late replies never re-enter UI or workflow.

Core NATS live request/reply only: no JetStream, persistence, inbox/outbox, durable local forwarding, delayed retry, deadletter body or response cache. Register a listener only when this capability is explicitly enabled and initialized; unrelated AI listeners unchanged. Queue group prevents ordinary fanout but not all possible duplicate requests/execution. No exactly-once claim. No automatic reconnect replay or caller/SDK/handler retry; scoped Wolverine exception policy sanitizes/discards only this message type. No generic durable policy changes. Real Host+AI+NATS+PostgreSQL tests must prove zero text in durable tables and sanitized failure output; if framework composition cannot provide this, stop for a concrete design revision before execution, do not silently switch transport.

Proposed ceilings (engineering recommendations, NOT approved spend):

| Limit | Ceiling |
| --- | --- |
| text | 1000 Unicode scalars and4096 UTF-8 bytes, valid Unicode; LF permitted, other controls rejected; whitespace-only invalid |
| HTTP request | 8KiB including envelope, known/chunked body, max JSON depth8 |
| complete model input | conservative total prompt+schema+payload+framing upper bound4096 tokens (existing byte upper-bound pattern; refuse rather than trim); API text cap does not promise every text fits model envelope |
| model output | 1024 tokens and16KiB validated text; one nonstreaming completion |
| HTTP candidate | 16KiB complete encoded representation; no original full text echo |
| inference | at most3500ms, within AI handler4500ms and Host5000ms end-to-end deadline |
| browser observation | at most5500ms; timeout never initiates another POST |
| admission | one active inference per M3-enabled AI process, no queue; at most6 starts/minute; Host also6 requests/minute/trusted owner |
| cost | max reservation USD0.03/request and USD0.30/rolling hour/process; both must be explicitly configured <= ceilings; unset/zero permission means no dispatch |

Capture the Host anchor/deadline at entry to the authenticated extraction route before reading its body; the bounded body read/validation spends the same5000ms budget and must honor cancellation. A slow chunked body cannot reset the clock before forwarding. The Host propagates absolute deadline. AI rejects expired input, accounts for elapsed transport time and reserves time for ledger completion; does not restart a full timeout at each hop. Cancellation is best effort: after timeout the provider may still bill. Hold the active slot/reservation until the underlying call/accounting settles even if it ignores cancellation. Never deliver its late output. Do not start another inference to repair JSON.

Use existing CostLedgerEntry with new feature/message identity and random correlation; no schema change. Commit an Admitted row with unknown nullable usage/cost before model dispatch; accounting unavailable means zero dispatch. Verify response.ModelId against the explicitly selected, priced exact revision (or a preapproved revision set with the same verified tariff); missing/mismatched identity is503/ModelUnavailable, leaves cost unknown and keeps the full reservation. Never price a different model using the requested model tariff. Missing/malformed usage is503/AccountingUnavailable and cannot deliver a candidate. Record available usage on all outcomes without fabricated zeros; SDK exceptions are reduced to controlled codes without raw exception/body. Ledger failure after dispatch returns accounting-unavailable and leaves reservation conservative. Unknown spend keeps full reservation for the hour; never infer no bill from missing reply. Before authorized pilot require one M3-enabled responder: process limits are not fleet/account budgets and restarts do not prove reset-safe global spend; larger topology needs a separate budget design/authorization. Application cannot guarantee provider invoice ceiling from advertised tokens alone.

### 7. Provider and privacy choices stay gated

Recommended integration is a feature-specific IChatClient in current Travel.AI, with exact model and verified pricing blank/unset until selected. It must not inherit global Flights Opus configuration. Public synchronous comparisons in [research](research.md): Haiku4.5 USD0.009216 vs Sonnet4.6 USD0.027648 at the proposed token ceilings. Neither has measured Russian extraction quality or latency here. Local serving remains an option with unmeasured hardware/operations cost; a new hosted provider adds an adapter and separate review. No provider switching/fallback chain.

No project logs, traces, telemetry events, crash reports, caches, SSR, browser storage, URLs, durable messages or ledger contain raw text, source excerpts, candidate values, prompt/completion or reversible/text hashes. Metadata is limited to random attempt ID, feature/model/prompt/schema version, controlled outcome, numeric usage/cost/duration/size. Disable provider/SDK payload logging, HTTP body logging, bus diagnostic serialization and private browser capture for this path. User content is untrusted data, not tool/system instructions. Feature has no model tools, browsing, retrieval, function calls or business-action capability.

User-visible text/review lifetime is at most10minutes after admission (or relative-date midnight), cleared earlier on leave/logout/owner change/reset, with local references invalidated on success/failure according to the review state. Service-owned request/prompt/result references are released on completion or bounded observer timeout and no extra detached payload copies are kept. An SDK/transport task that ignores cancellation can still retain its own prompt references until it settles; the application cannot guarantee a10minute physical-memory retention bound for that dependency. Keep only safe observer/admission metadata outside that task, hold its slot and suppress late output. This limitation belongs to processor/dependency acceptance; do not claim physical RAM zeroization or initiate unapproved process cleanup. Raw input is intentionally sent to the chosen processor when explicitly requested; automatic redaction cannot guarantee removal of personal data. Give a clear “не вводите персональные данные” notice and show processor/retention terms before dispatch. Runtime must have a separately authorized processor/model/privacy policy version and private-text permission; the click and feature flag alone are not organizational permission. Provider retention is separate from app no-store and must be accepted before real texts. Local fictional fake testing requires no such external transmission.

### 8. Acceptance and rollout stages

[Evaluation plan](evaluation-plan.md) separates mechanical contract tests from real-model known-corpus qualification with fixed inputs/oracles/thresholds. The published48 rows are not a closed holdout. Independent model acceptance before rollout additionally needs a newly independently authored/sealed24-case holdout after source/prompt/model freeze; it is not produced or run here. Quality evaluation invokes the same production AI extractor and Trips normalizer in an owned in-process harness, supplying fixed reference anchor data separately from real TimeProvider deadlines/cancellation/accounting. It does not send a client-supplied referenceInstant through the public HTTP API or globally freeze runtime timers/authentication. Its timed pipeline excludes actual Host/NATS/browser transport, so it cannot prove the real end-to-end5000ms deadline; actual composed transport tests verify deadline mechanics separately and a real-model full-path pilot remains a later gate. Evaluation pacing is six8-case windows at65-minute starts/11-second dispatch spacing within6hours (three windows/3hours for the later24), with planned pre-admission waits excluded from per-request latency and explicit no-auto-resume stop behavior. Source tasks can finish with fake IChatClient and zero model calls, labelled source-only/model quality not evaluated. Paid execution/real-text processing never becomes a prerequisite for ordinary CI.

No Trips/AI migration generated or applied. Future source build defaults off; source acceptance does not provision target, keys/schema, model configuration or suppliers. Optional separately authorized disposable fictional-model pilot must use new owned state, never the retained demo. Real model evaluation, target readiness, private-text permission and rollout are separate recorded decisions. These flags are startup configuration unless a later explicitly implemented reload mechanism says otherwise; do not claim instantaneous revocation from editing a config file. Rollback applies disabled Host and AI configuration through a separately authorized restart, after which new requests/listeners are off. UI clears the transient candidate on the next capability read/404 and confirmation requires that read. Already admitted provider work may settle and bill, but its late result cannot become a current UI candidate. Existing forms/plans remain readable. Rollback needs no data rewrite; persisted ordinary confirmed M1 fields remain historical user content.

## Risks / Trade-offs

- Conservative grammar misses natural expressions → show unsupported/ambiguous and editable manual values; expand only with a new measured corpus.
- A semantic model mistake can pass structural validation → evidence/diff/explicit confirmation plus separate quality gate; no safety guarantee from fake tests.
- Privacy conflicts with durable/global diagnostics → actual failure-path transport/storage/log proof is a source acceptance gate.
- Hard deadlines can discard billed work → retain conservative accounting/admission and never retry automatically.
- All selections start unchecked and money/origin stay manual → extra clicks and limited convenience, with clear scope.
- Per-process admissions cannot enforce fleet/monthly invoice caps → no multi-responder real pilot in this slice; separate deployment budget decision.
- Current route packages expire independently → successful extraction may still have unsupported/unavailable itinerary generation; preserve that distinction.

## Open Questions

Only external execution choices are deferred, and no real dispatch is allowed until resolved: exact provider/model and processor terms; authorized per-request/hour/evaluation spend (or no model run). They do not change the proposed first-slice semantics, fake source work or task decomposition. Approval of this plan/implementation is a separate user decision; target and rollout are never implied.
