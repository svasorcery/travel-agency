# Tasks

**Статус 2026-10-07:** product source и PR39/postmerge CI приняты; main specs синхронизированы и corpus архивирован. Source checkpoints25/25. Documentation publication and managed-worktree disposition receipts follow in the delivery PR/thread; no live supplier/payment/deploy acceptance is claimed.

**Goal:** безопасное создание → багаж → места в одном existing booking flow.
**Spec:** [requirements](specs/flights-booking-ancillaries/spec.md), [cancellation delta](specs/flights-whole-order-cancellation/spec.md), [design D1–D8](design.md).
**Stack:** существующие .NET/Marten/Wolverine/ErrorOr, Angular/TS, current tests; без новых packages/schema/migrations.

Во всех группах paths C/A/I/P означают существующие projects `modules/flights/Travel.Modules.Flights.Core/`, `Application/`, `Infrastructure/`, `Api/` с тем же полным префиксом Travel.Modules.Flights. W = `apps/web/src/app/flights/`, TS = `shared/ts/api-client/src/`, U = `tests/flights/Travel.Modules.Flights.Tests.Unit/`. Create/Modify ниже перечисляют конкретные файлы относительно этих roots.

Каждый source task: meaningful failing behavioral test, bounded implementation, GREEN, запись commands/evidence. Future local fixtures сначала inspect (включая linked test files/initializers); no local Host/AppHost/DB/Aspire/keys/schema/paid APIs. Windows .NET строго последовательно. Real persisted/full Host/Aspire proof — existing CI. Stage/commit/push/merge/rollout требуют своей authority. Не менять CI/CD/tooling или чужую работу.

## 0. Execution boundary

- [x] 0.1 Перед product changes получить отдельную implementation authority для этой редакции; проверить worktree HEAD/checkpoint ancestry и root/module AGENTS. Проверка: точный scope записан в process-log.md; решение об упрощении не выдано за начало исполнения.
- [x] 0.2 Зафиксировать допустимые local test selections после fixture audit и текущую A1 contract assumption из research.md. Проверка: нет supplier calls/установок latest, full Host/DB/key fixtures оставлены CI; A1 не превращена обратно в блокировку offline source.

## 1. Один защищённый путь создания (A)

**Create:** C/Booking/BookingPurchase.cs, C/Booking/BookingCreationAttempt.cs, C/Aggregates/BookingAggregate.Creation.cs, C/DomainEvents/BookingCreationEvents.cs; A/Booking/CheckBookingCreation.cs, A/Handlers/Booking/CheckBookingCreationHandler.cs; P/Composition/BookingCreationDeliveryPolicy.cs; U/Booking/BookingCreationTests.cs, BookingCreationReplayTests.cs, BookingCreationCommitTests.cs.

**Modify:** existing C/Aggregates/BookingAggregate.cs; A/Commands/HoldOfferCommand.cs; A/Handlers/Booking/HoldOfferHandler.cs; I/Marten/BookingAggregateConfig.cs; P/Middleware/IdempotencyKeyMiddleware.cs; P/Endpoints/HoldOfferEndpoint.cs; P/Composition/FlightsModule.cs; existing A/Persistence/DocumentSessionExtensions.cs only where helper integration requires it.

**Data:** BookingPurchase = quoteRevision, owner?, base/extra/grand Money, selected normalized service lines and notices/expiry. BookingCreationAttempt = ID (retained request key scoped to booking/owner), digest, accepted purchase, protected party, sender instance/time, current outcome/actual facts. New events only BookingPurchaseQuoted, BookingCreationStarted, BookingCreationObserved. Existing manual audit reused. Observation outcomes InProgress/Matches/CreatedWithDifferences/NotCreated/ManualReviewRequired; no stored full catalog.

**Boundary:** HTTP middleware exposes existing key/digest as request metadata, not raw body; command carries them plus protected party. Handler orders auth→retained identity→fresh guards→decrypt→CAS start/outbox→one provider create→save result. Same key/different bytes conflicts; different keys cannot bypass a current attempt. Old persisted commands without usable identity refuse before HTTP; never invent a replacement identity. All fresh HTTP no-service holds use the same path.

- [x] 1.1 Pin one winner/two keys, failed start commit→zero effects, crash after start before/after POST, exact replay after quote/cache expiry and random ciphertext. Include same-owner re-quote vs start CAS: all quote writers freeze after start, pre-start refresh losing CAS cannot replace the binding. Проверка: focused pure/fake-session RED→GREEN, retained accepted/held revision and one create maximum; blocked legacy command sends nothing.
- [x] 1.2 Implement start/result events and atomic existing outbox check at +150s, with independent bounded operation token after start. Проверка: client abort cannot erase admission or cause replay; normal completion has exact required stream version/AllowMultiples, no background create/fire-and-forget.
- [x] 1.3 Implement one CheckBookingCreation worker: terminal→no-op, known correlated ID→bounded read, missing ID/deadline +180s→manual, retry/DLQ only safe reads/storage. Проверка: zero list/search calls, deadline not reset by restart, delivery failure not no-effect; document actual results.

## 2. Временный catalog и итоговый quote (B)

**Create:** C/Providers/IFlightAncillaryProvider.cs, C/Providers/Dtos/AncillaryCatalogFacts.cs; I/Providers/Duffel/DuffelFlightAncillaryProvider.cs, DuffelAncillaryMapper.cs, Dto/DuffelAncillaryServiceDto.cs, Dto/DuffelSeatMapDto.cs; A/Handlers/Booking/GetAncillariesHandler.cs; P/Endpoints/GetAncillariesEndpoint.cs, P/Contracts/AncillaryContracts.cs; U/Ancillaries/AncillaryCatalogTests.cs, AncillaryQuoteTests.cs.

**Modify:** I/Providers/Duffel/Dto/DuffelOfferDto.cs, DuffelSegmentDto.cs, DuffelSliceDto.cs, DuffelSegmentPassengerDto.cs (additive wire IDs/facts); existing A/Handlers/Booking/QuoteOfferHandler.cs, A/Commands/QuoteOfferCommand.cs, P/Endpoints/QuoteOfferEndpoint.cs, P/Contracts/Contracts.cs; module registration and URL redaction.

**Port:** `ReadCatalogAsync(string offerRef, bool includeSeats, CancellationToken) -> ErrorOr<AncillaryCatalogFacts>`. Facts contain fresh base offer, reference graph, included allowances, services/maps/pricing-intent status. These objects remain transient. `QuoteOfferRequest` gets optional `selections: [{selectionKey, quantity}]`; new selected quotes require authenticated actor from API. Server maps current refs to existing passenger slots and quote-bound segment addresses; quote response adds Purchase with string money totals. No CatalogId, ReviewId, durable read receipts or separate prepare/review command family.

- [x] 2.1 Pin per-adult/per-segment included allowances, current identity/membership, multi-segment bag counted once, quantity/currency/overflow/pricing-intent rejection, valid empty vs failed response. Проверка: mapper/quote tests RED→GREEN; raw wire types stay Infrastructure and Max summary never becomes actual allowance.
- [x] 2.2 Implement explicit catalog read and quote selected terms, preserving original offer events and appending one BookingPurchaseQuoted in the same commit. Проверка: stream contains selected lines only, not maps/inventory; base offer and aggregate grand total each applied once; omitted selections preserves existing choices, explicit [] clears them; current client compares grand-to-grand and empty legacy behavior stays equal.
- [x] 2.3 Enforce claimed quote owner and creation freeze before provider access, including owner refresh; only positive NotCreated with no other barriers may refresh again. Preserve empty old quote compatibility; bound body/response and redact refs/failures. Проверка: foreign/anonymous/owner-after-start zero-provider cases, known-created refresh refusal, safe no-create refresh and no supplier keys/PII in DTO/logs.

## 3. Existing Duffel booking provider и actual result (B)

**Create:** C/Providers/Dtos/BookedOrderFacts.cs; I/Providers/Duffel/DuffelOrderCreationClient.cs, DuffelBookedServicesMapper.cs; U/Ancillaries/DuffelPurchaseTests.cs, BookedServiceMappingTests.cs; `tests/fixtures/flights-ancillaries.json`.

**Modify:** C/Providers/IFlightBookingProvider.cs and provider result DTOs; I/Providers/Duffel/DuffelFlightBookingProvider.cs, Dto/DuffelOrderDto.cs, FlightsInfrastructureServiceCollectionExtensions.cs; fake implementations in owning tests. No IFlightAncillaryBookingProvider or second create-order implementation.

**Ports:** replace internal HoldOfferAsync signature with `(BookableOffer, QuoteBinding, EquatableArray<BookingPassenger>, BookingPurchase, Guid attemptId, CancellationToken) -> ErrorOr<BookingCreationObservation>`; add `ReadOrderForBookingAsync(string orderId, BookableOffer, QuoteBinding, BookingPurchase, Guid attemptId, CancellationToken) -> ErrorOr<BookedOrderFacts>`. Accepted context is required for parent-scoped identity mapping and receipt correlation; a naked ID is insufficient. Internal signature changes update all fakes/callers; old HTTP DTO and serialized events remain compatible. HTTP create client has 130s total budget/no POST retry; existing read/payment/cancel budgets unchanged.

**Facts:** separate known provider ID/receipt from complete, correlated actual itinerary/party refs, services/amount, awaiting-payment/deadline and creation-completion proof. Provider outcome does not itself overwrite original accepted purchase.

- [x] 3.1 Implement one type=hold request with exact party/services and safe attempt metadata, no payments. Проверка: loopback one POST for bags q2/free seat/empty selection; complete matching 201 vs timeout/malformed/missing identity/different services; no real APIs or real-time 130s waits.
- [x] 3.2 Map separate offer/order segment and booked-service IDs through unique full graph/topology; price booked quantity-inclusive lines once. Проверка: changed IDs positive fixture, ambiguous graph/foreign passenger/extra lines negative fixtures, no name/position/amount-only matching.
- [x] 3.3 Classify a complete first-response structured no-create rejection separately from uncertainty; services_not_allowed gives explicit unsupported outcome. Проверка: unknown codes/malformed envelopes/late negative evidence never erase prior uncertainty; no implicit drop, instant fallback, refund or POST retry.

## 4. Исходы, existing review, cancellation и HTTP (A/B)

**Create:** A/Queries/GetBookingCreationQuery.cs, A/Handlers/Booking/GetBookingCreationHandler.cs; P/Endpoints/GetBookingCreationEndpoint.cs; U/Booking/CreationResolutionTests.cs; U/Ancillaries/AncillaryHttpTests.cs, AncillaryConfirmationTests.cs, AncillaryProjectionTests.cs.

**Modify:** C/Aggregates/BookingAggregate.Creation.cs, BookingAggregate.ManualResolution.cs, confirmation/cancellation partials; C/Cancellation/ManualResolutionInput.cs, ConfirmationAttempt.cs; C/DomainEvents/ConfirmationCoordinationEvents.cs; C/Providers/Dtos/ConfirmedOrder.cs. Existing A/Handlers/Cancellation/ResolveCancellationReviewHandler.cs, A/Cancellation/CancellationReviewFactory.cs, CancellationStatusFactory.cs, CancellationScope.cs; A/Handlers/Booking/ConfirmOrderHandler.cs; I/Persistence/OrderReadModelEventApplier.cs, OrderReadModelReconciler.cs; P/Contracts/CancellationRequests.cs, CancellationRequestValidation.cs, CancellationResponse.cs; P/Endpoints/HoldOfferEndpoint.cs and existing review endpoints.

**Manual:** add Creation target and typed AttachMatches/AttachDifferences/ConfirmNoCreatedOrder/RecordInconclusive decisions to the existing privileged review. ConfirmNoCreatedOrder requires affirmative no-effects and quiescence evidence. Existing flights:cancellation-review remains mandatory; ordinary owner cannot resolve. New CreationEvidence is optional trailing metadata, only applicable for Creation; existing target shape guards remain strict. Old payload hash bytes unchanged when new fields absent; versioned normalization for new proof. Api/OperatorVerified provenance, actor/time/revision/receipt retained.

**Confirmation:** extend existing validation/guarded confirmation with optional expected purchase services and typed returned service proof. New service-bearing confirmation completion/manual completion requires matching proof; preserve historical null/no-service semantics. Persist proof additively on existing confirmation completion, not a parallel confirmation workflow.

- [x] 4.1 Implement terminal Matches vs conclusively CreatedWithDifferences: save actual owned Held facts and bar payment, while whole-order cancellation becomes eligible only after completion/quiescence and no money blocker. Проверка: missing bag→owner cancellation succeeds through current consent protocol; unknown identity/sender/money remains blocked; replacements cannot be accepted.
- [x] 4.2 Separate immutable creation-completed evidence from later actual-service comparison; before-wallet mismatch saves actual facts and closes the not-dispatched financial attempt in the same batch. Проверка: subsequent GET offers cancellation-only, cannot loop back into confirm, and after-capture mismatch retains the financial barrier.
- [x] 4.3 Extend existing operator resolver with exact attribution/readable typed evidence, safe attachment and affirmative no-effects rules. Проверка: owner forbidden, timeout-only proof rejected, wrong order/party rejected, golden v1 replay retained; after-capture same-price seat mismatch cannot be cleared by money-only ConfirmBooking.
- [x] 4.4 Preserve old hold 200 shape only for Matches; known differences→409 Flights.HeldOrderNeedsCancellation, unresolved→409 Flights.HoldOutcomeUnknown when a response is possible. Add owner GET /orders/{aggregateId}/creation with no supplier reads/no-store. Проверка: current 15s client timeout path recovers later success by GET, stale cached 200 cannot enable confirm, auth/PII/body guards hold.
- [x] 4.5 Update pure projection/checkpoint/owner checks and service-aware cancellation scope while retaining old v1 terms/hashes. Проверка: base and actual/grand amount not double-counted; V1/V2/V3 replay unchanged, refund never inferred from services; current-state/ADR link to this single corpus and actual evidence only.

## 5. Shared client и сквозной багаж (B)

**Create:** TS/flights-ancillaries.types.ts, flights-ancillaries.decoder.ts, flights-ancillaries-api.service.ts and focused specs; W/flight-baggage-picker.component.ts/html/scss and spec, W/flight-ancillaries.service.ts and spec. **Modify:** existing TS quote/booking client/types/decoders/index.ts; W/flight-booking-panel.component.ts/html, flight-order-operations.service.ts, flight-order-page.component.ts/html, flights quote/auth integration.

**Interfaces:** `getCatalog({aggregateId, quoteRevision, includeSeats})`; existing `quote({... selections})`; existing `hold({... acceptAncillaries})`; `getCreation(aggregateId)`. Status includes accepted/actual facts, state and server-derived allowed actions. No new general money framework: bounded exact decimal helper only if client preview needs arithmetic; backend quote always authoritative.

- [x] 5.1 Implement strict DTO/reader compatibility, exact amount strings and current owner/quote guards. Проверка: malformed responses fail distinctly from empty inventory; no float payment totals; old no-service payload fixtures remain valid.
- [x] 5.2 Implement draft-preserving baggage selection and one final review with compact adult/leg summary: included vs extra baggage, coverage, seat, base/extras/grand totals. No local 120s reset; changed selections invalidate acceptance. Проверка: 9 adults/4 legs beyond two minutes, changed service key/removed bag, mixed allowances, optional no-seat, missing allowance, q2 multi-segment line charged once and no new PII reads/name storage.
- [x] 5.3 Before hold navigate to existing booking resource route; on timeout/unknown use GET only. Show a concise selected→actual difference list and current allowed actions, preserving stale-response guards. Проверка: same-price seat change visible, unknown not zero/missing, no cross-currency delta or inferred refund, proven differences cancellation-only; reload before EF row, A→B→A, cached hold after cancellation and no PII/operation storage.

## 6. Места на той же модели (C)

**Create:** W/flight-seat-picker.component.ts/html/scss and spec. **Modify:** temporary catalog mapper/fixtures, quote/booking review from B, order service view.

- [x] 6.1 Add per-adult/per-segment optional seat map and equivalent keyboard/text controls with plain-text disclosures. Проверка: irregular rows/decks, free seat, missing map, one seat/two adults, differing passenger prices, unshowable restriction and non-seat elements.
- [x] 6.2 Include chosen seats in the same final quote, hold payload/result and cancellation view. Проверка: changed seat/restriction requires consent; no second create/payment protocol, no adjacent/permanent-seat promise or accepting replacements.

## 7. Combined acceptance и отдельно разрешённая delivery

**Modify:** `tools/demo/flights-search-api.mjs`, `tools/demo/flights-search-api.test.mjs`, `tests/travel-e2e/demo/fictional-route-guard.ts`, `fictional-browser-proof.ts`, existing booking fixtures; **Create:** `tests/travel-e2e/demo/flights-ancillaries.spec.ts`.

**Documentation:** modify existing root `README.md` with a short fictional ancillaries demonstration section: how to select each preset in the local demo runner, inputs/steps/expected outcome and how to begin the next isolated run. This is usage documentation, not another feature plan. Optional UI scenario selector is outside the required work; no fault-control endpoint or new operator UI.

**CI-only create:** `tests/flights/Travel.Modules.Flights.Tests.Integration/Ancillaries/BookingCreationDurabilityTests.cs`, `AncillaryProjectionTests.cs`, `CreationManualReviewTests.cs`; `tests/Travel.Host.Tests.Integration/Flights/AncillaryApiTests.cs`. Existing full Host OpenAPI baseline only updated from real received CI output.

- [x] 7.1 Package existing planned fictional cases as purchase-success, purchase-diff and purchase-unknown presets, sharing fixtures between demo and browser tests; document repeatable selection/steps in README. Проверка: all three user-visible endings reproducible from an isolated start, reload never repeats hold, diff leads only to permitted cancellation, unknown remains manual; existing localhost/no-query/no-Authorization guards, no PII echo and clear fictional label retained. No preset control in production or switching an active attempt.

Preset purchase-unknown means supplier result/ID lost after saved server admission. Preserve the separate success-preset regression: browser response lost but server saved Matches → GET/reload finds success. Do not force manual merely because the browser missed a response.
- [x] 7.2 Add persisted start/finalization rollback, restart before/after send, worker redelivery/DLQ, no duplicate effect, manual attach/difference cancellation and version/checkpoint proof. Проверка: run only existing CI after fixture review; source compilation/fakes do not substitute for persisted results.
- [x] 7.3 Run authorized local format/client/web/demo/build gates and independent review; obtain actual existing CI including signed JWT/full Host/architecture/Aspire/PR E2E only after publication authority. Проверка: exact SHA/run/results; no CI weakening or paid evals, repeated exit134 explained with evidence and options.
- [x] 7.4 After separately authorized merge/postmerge, perform approved main-spec sync/archive/docs delivery and own-worktree cleanup. Проверка: one canonical corpus, unchanged postmerge policy, no whole-M3/live/payment/deploy claims and no archived checkout restoration.

## Verification commands and fixture ruling

Fixture audit found that the regular Unit project initializes FlightPiiTestRing even with a filter. Do not run that project locally under this task's no-key boundary. The isolated local runner linked only audited pure/mapper/fake-session sources and did not link that fixture or the Unit assembly. Its exact source and logs are retained in ignored .nx/cache/flights-ancillaries-proof; after its final run the project was retired to Proof.csproj.source.txt so the unchanged project-inventory gate remains valid. Regular Unit/Integration/Host execution belongs to existing CI. Source builds use --maxcpucount:1 and run sequentially, with DOTNET_GENERATE_ASPNET_CERTIFICATE=false.

```powershell
npx.cmd nx test api-client
npx.cmd nx test web
node --test tools/demo/flights-search-api.test.mjs
npx.cmd playwright test -c tests/travel-e2e/playwright.flights-demo.config.ts
npx.cmd nx build web --configuration=production
dotnet csharpier check .
npx.cmd biome ci .
node tools/ai-harness/validate.mjs
node tools/openspec/run.mjs validate flights-m3-booking-ancillaries --strict
git diff --check
```

Local product results and independent review are recorded in verification.md and review.md. 7.2 has source coverage compiled, with runtime pending; 7.3 has local gates/review completed, with existing CI pending. No automatic provider-list discovery, durable catalog, separate ancillary review/hold API or replacement-acceptance work is hidden in these groups.
