# Flights M3 cancellation — полный план исполнения

> Для исполнителя: `superpowers:executing-plans`, последовательно по зависимостям; на отмеченных границах — независимый reviewer. Дополнительный вопрос о способе планирования не нужен. Execution начинается только по команде запустить соответствующий этап.

**Goal:** cancellation целого owned order с актуальными условиями, точным consent и достоверным outcome после ошибок/reload/restart.

**Architecture:** cancellation и bounded confirmation coordination находятся в BookingAggregate stream. Supplier DTO — Infrastructure. Scheduled recovery атомарен с claim через существующий Marten/Wolverine outbox; manual judgments имеют отдельный operator trust boundary.

**Stack:** .NET10, Marten9.14.0, Wolverine6.17.0, EF read projection, Angular21/TypeScript, xUnit v3/Shouldly/WireMock/Playwright. Node22 для tooling; existing bundled Node24.19.0 для locked frontend graph. Pinned OpenSpec1.14.0.

**Spec:** [feature scenarios](specs/flights-whole-order-cancellation/spec.md), [design E1–E6](design.md), [tooling contract](harness-integration.md). Единственный plan/spec corpus, без новых копий в Superpowers.

**Статус 2026-10-06:** D1–D8/вариант A и полный план T1–T12 приняты; product execution разрешён. T1–T5 завершены. T6–T8 source/local exit завершён, durable runtime acceptance остаётся T9. T9 fixtures/fault sources подготовлены и скомпилированы; реальные DB/Host/CI не запускались. T10 и T11 source/local proof завершены. T12 independent source review и local checks завершены; exact-head CI, final acceptance и Delivery ещё открыты. Evidence:458 isolated .NET,133 shared client,377 Angular,55 Node demo,169 architecture и4 focused browser cases PASS. См. [карта всех17 требований/38 сценариев](verification.md), [review](review.md), [единственный execution ledger](process-log.md). Ни одного будущего CI/merge/archive PASS не записано.

## Общие ограничения

- Fetched base `31a3803a4d558199eec79b48dc8cf91e078ac681`, checkpoint/M1 ancestry проверены. Managed worktree `C:/Users/Vladimir_sva/.codex/worktrees/flights-m3-openspec-design/travel-agency`, detached. Проверять requested ancestry перед branch switch; archives не восстанавливать.
- Fictional data only; no supplier/payment/Anthropic/paid API/evals. No local Host/AppHost/DB/schema apply/migrations/key provisioning/deploy. Source migration выбранному дизайну не нужна.
- Core→Application→Infrastructure→Api; Host только existing facade/global builders. ErrorOr imports, TimeProvider и past-tense IDomainEvent обязательны.
- .NET команды последовательно. Перед test fixture читать startup/client/schema paths; название Unit/HTTP не гарантирует безопасность. DB/full Host/Aspire — существующий CI.
- JWT/auth не ослаблять; signed fictional JWT + real validator в isolated TestServer. Application fake auth только demo. PII/tokens/client operation/evidence не в URL/history/browser storage/logs.
- Полный разрешённый tooling этап включает T1/T2 и их проверки, в том числе authenticated verifier; повторное разрешение на каждую штатную команду не нужно. Явное исключение пользователем проверки сохраняется как незавершённый gate без false runtime claim.
- Product и publication не выводятся из planning approval. Delivery authority должна покрывать product и closure PR; штатные проверки/исправления внутри разрешённого этапа автономны.

## Пути и зависимости

Алиасы ниже — точные repository-relative roots, не wildcard и не новые проекты:

| Alias | Path |
| --- | --- |
| Core | modules/flights/Travel.Modules.Flights.Core |
| App | modules/flights/Travel.Modules.Flights.Application |
| Infra | modules/flights/Travel.Modules.Flights.Infrastructure |
| Api | modules/flights/Travel.Modules.Flights.Api |
| Unit | tests/flights/Travel.Modules.Flights.Tests.Unit |
| Integration | tests/flights/Travel.Modules.Flights.Tests.Integration |
| HttpTests | tests/Travel.Host.Tests.Integration/Flights |
| Client | shared/ts/api-client/src |
| Web | apps/web/src/app/flights |

Dependencies: T1→T2; T3→T4→T6; T3→T5→T6; T4/T6→T7; T6/T7→T8; T5–T8→T9 durable proof; T8→T10 shared contracts→T11 UI/demo; всё→T12. UI drafting после contracts допустим, acceptance до T9 нет. Event readers/projector T4 предшествуют writers T6/T7.

## Зафиксированные интерфейсы

**C1 — Core (T3/T4).** `CancellationTerms.Create(CancellationTermsInput input, DateTimeOffset now)` returns ErrorOr<CancellationTerms>; immutable input: revision/aggregateId/ownerId/operationId, providerOrderRef/providerCancellationRef, itineraryPartyHash, Money refund, destination/composition, expiry/noticeVersion. Server computes Hash. `CancellationOperation` хранит ID/revision/stage/terms/consent/claims/observations/outcome; `ConfirmationAttempt` — admission/effects claim/payment ref/provenance. `ManualResolutionInput` — E1 exact typed body, `ManualResolutionDecision` — Allowed/Rejected/NoOp + safe reason.

Phase: Preparing, TermsReady, Accepted, DispatchClaimed, Unknown, ManualReviewRequired, Succeeded, Rejected, Abandoned, Expired, UnsupportedTerms. Outcome: None/Succeeded/Rejected/Unknown. Source: TravelAdmission/SupplierApi/OperatorVerified. UnknownStage=Preparation не утверждает отправленную отмену.

**C2 — provider port (T3/T5):**

```csharp
public interface IFlightCancellationProvider
{
    Task<ErrorOr<CancellationEligibility>> InspectOrderAsync(string orderRef, CancellationToken ct);
    Task<ErrorOr<CancellationQuoteResult>> CreateTermsAsync(string orderRef, CancellationToken ct);
    Task<ErrorOr<CancellationEffectResult>> ConfirmAsync(CancellationTerms terms, CancellationToken ct);
    Task<ErrorOr<CancellationObservation>> ObserveAsync(CancellationCorrelation correlation, CancellationToken ct);
}
```

Records в `Core/Providers/Dtos/CancellationProviderResults.cs`: Eligibility — identity/available action/payment state/accepted total facts; QuoteResult — outcome + nullable quote facts + safe reason; EffectResult — Confirmed/DefinitivelyRejected/Unknown + nullable observation; Observation — exact IDs/state, nullable refund/expiry/confirmed time/composition + observedAt/provenance; Correlation — persisted refs/accepted terms. No wire JSON/raw refund_to/passengers. ErrorOr после mutation claim не доказывает отсутствие эффекта.

**C3 — Application (T6/T7).** Все IDs Guid, revisions/versions long, slot int, accepted/isOperator bool, fingerprint/hash/notice string; Guid.Empty и неположительные required versions invalid. ActionStage — enum Prepare/Confirm. OperatorActor — Application record с проверенным Guid UserId, создаваемый endpoint после scope validation. `App/Cancellation/CancellationCommands.cs`: PrepareCancellationCommand(aggregateId,userId,operationId,expectedBookingVersion,fingerprint); ConsentCancellationCommand(aggregateId,userId,operationId,expectedOperationRevision,termsRevision,termsHash,noticeVersion,accepted,fingerprint); AbandonCancellationCommand(aggregateId,userId,operationId,expectedOperationRevision); RefreshCancellationCommand(aggregateId,actorUserId,operationId,expectedOperationRevision,refreshRequestId,isOperator); GetCancellationStatusQuery(aggregateId,userId); GetCancellationReviewQuery(aggregateId,OperatorActor); ResolveCancellationReviewCommand(OperatorActor,ManualResolutionInput). Operator flag/actor создаёт только авторизованный endpoint, не body.

`App/Cancellation/CancellationMessages.cs`: ExecuteCancellationPreparation(aggregateId,operationId,admissionId); ExecuteCancellationConfirmation(aggregateId,operationId,admissionId); ObserveCancellation(aggregateId,operationId,recoveryEpoch,slot,refreshRequestId nullable); AdmissionDeadline(aggregateId,operationId,admissionId,actionStage); RecoveryDeadline(aggregateId,operationId,recoveryEpoch); ConfirmationBarrierDeadline(aggregateId,attemptId,admissionId). Slot0–3 automatic, slot=-1+refreshRequestId on-demand. Message не разрешает effect без authoritative decision. Один handler/endpoint на файл.

**C4 — results (T6/T8/T10).** `CancellationStatusResult`/`CancellationStatusResponse`: aggregateId, bookingStatus, bookingVersion, operation nullable, blockingConfirmation nullable, serverNow. Nested shapes E3/S1: requestedOperationId/currentOperationId nullable и isCurrentOperation обязательны для различения historical replay; наружу не выходит whole operation dictionary. Owner DTO не раскрывает supplier/process/operator refs. Operator query имеет отдельный Application `CancellationReviewResult`, который endpoint маппит в `CancellationReviewResponse` с разрешённой evidence metadata. GetCancellationStatusHandler обрабатывает только GetCancellationStatusQuery, GetCancellationReviewHandler — только GetCancellationReviewQuery; one message→one handler/result. Добавить routing regression и тест отсутствия private metadata в owner response. TS versions — positive safe integers.

**C5 — commit (T6).** `public static Task SaveBookingWithWorkAsync(this IDocumentSession session, IMartenOutbox outbox, Guid aggregateId, IReadOnlyList<BookingWork> work, IReadOnlyList<object> notifications, CancellationToken ct)` в existing DocumentSessionExtensions; `BookingWork(object Message, DateTimeOffset? DueAt)` там же. Enroll→work ScheduledTime→notifications+Reconcile→one SaveChanges. Existing helper делегирует с empty work, concurrency classification остаётся.

**C6 — receipt (T7).** `SupplierPaymentEvidence(string Reference, Money Amount, SupplierPaymentKind Kind)` с validated factory, нынешний Kind=Balance. `ConfirmedOrder(string ProviderOrderId, DateTimeOffset ConfirmedAt, SupplierPaymentEvidence? PaymentEvidence = null)` additive CLR DTO. New success requires evidence, historical domain events не меняются; это supplier provenance, не customer payout.

## Review focus

1. Crash claim→HTTP и reserve→GET: independent future messages/deadline, zero duplicate mutation (T6/T9).
2. Legacy Held без attempt/instance: отдельная binding/drain procedure, memory wallet absence не proof (T7/T8/T9).
3. Lost receipt: OperatorVerified correlation к attempt/order/Money/saved PaymentRef, known ref не подменять (T7/T8).
4. Harmless stream progress/old Prepare watchdog: immutable admission identity + fresh CAS (T4/T6/T9).
5. Owner/operator concurrent refresh и чужой global openspec: durable shared limiter (T8/T9), absolute launcher (T1/T2).

## T1. Pinned launcher и deterministic adapter

**Create:** `tools/openspec/package.json`, `package-lock.json`, `run.mjs`, `adapter.mjs`, `manifest.json`, `run.test.mjs`, `adapter.test.mjs`, `cli.integration.test.mjs`. **Modify:** root package.json только npm alias; .gitignore только own nested node_modules/temp rule при необходимости. Root dependency graph не менять.

**Interfaces:** `runPinned(args,{repositoryRoot,environment})`; `adaptSkill(name,upstreamText,manifest)` → effective text/failure. Manifest package/tag/integrity/upstream+effective hashes/adapter version+exact sites/files. Reviewed source provenance обязательна, self-consistent manifest недостаточен.

- [x] node:test RED: missing/wrong local version→no fallback; nested/path-with-spaces; adversarial global shim; cleanup on failure; wrong site/hash/count; double adaptation refused; same args/JSON stdout. Mock subprocess fixtures позволяют RED без установки CLI.
- [x] После execution-start создать package/lock, exact ignore-scripts install по tooling doc, verify integrity/version; no global/user config/telemetry.
- [x] Реализовать absolute spawn и staged adapter, без shell lookup:

```javascript
const child = spawn(process.execPath, [absolutePinnedBin, ...args], {
  cwd: physicalRepositoryRoot, env: scopedEnvironment, shell: false,
});
// Effective content derives only from verified upstream scratch output.
// Validate exact sites/effective hash before writing allowlisted target files.
```

- [x] `node --test tools/openspec/run.test.mjs tools/openspec/adapter.test.mjs`; after install `node --test tools/openspec/cli.integration.test.mjs`. Actual effective skill read command from root/nested temp checkout uses pinned binary despite fake global first in PATH.

**Exit — выполнен:** tests/provenance PASS, independent launcher/adapter review PASS. Generated target skills активированы локально; publication не выполнялась.

## T2. Harness inventory, adoption и actual skill workflow

**Modify:** existing four `tools/ai-harness/{validate.mjs,validate.test.mjs,verify-codex.mjs,verify-codex.test.mjs}`; `.agents/skills/spec/SKILL.md` только pilot routing; root/Flights descriptions по факту. **Create after generation:** exact6 skills+marker из tooling doc, openspec/config.yaml; change metadata .openspec.yaml при explicit pinned schema adoption. CLAUDE adapters сохраняют canonical references.

**Interface:** validateHarness работает без installed CLI. Exact4 harness files/8 Travel skills/probes сохраняются; dependency-free new tests импортируются через existing entrypoint, CI command unchanged.

- [x] RED negatives missing/extra/symlink/marker/profile/hash/adapter drift; сохранить смысл existing90 cases.
- [x] Separate exact inventory groups, no glob exemption:

```javascript
const expectedSkills = [...travelSkillNames, ...manifest.effectiveSkillNames];
// Original Travel schema remains; OpenSpec effective hashes/marker get exact separate validation.
```

- [x] Staged init core/codex/skills; validate-before-apply, preserve corpus/Travel hashes, no duplicate change/force/cloud. Double init/update stable; pin spec-driven metadata rather than global default.
- [x] `npm.cmd run check:ai-harness`; launcher --version/status/instructions apply/validate --strict JSON на flights-m3-cancellation. Instructions read не запускает apply.
- [x] Полный разрешённый tooling этап: `npm.cmd run verify:ai-harness:codex -- --working-tree --model gpt-5.6-sol` для current uncommitted tooling на наблюдаемом CLI0.147.0 catalog, old probes+discovery root/Flights+actual read command через effective skill. Own temporary tree/protocol/cleanup restrictions сохраняются.

**Exit — выполнен:** source/CLI/inventory и полный authenticated core/role/OpenSpec/literal verifier PASS. Original probes/validators сохранены; standard Windows status использует verified exact plan при never/readOnly/networkfalse без callback. Product T3 требует собственного execution-start; supplier/durable product proof этим результатом не подтверждаются.


T1/T2 exact inventory/allowlist и original90 test semantics сохранены. Старый unsafe prototype удалён; новый отдельно разрешённый single-status opt-in отделён от стандартного never/read-only, который прошёл весь native verifier без callback/grants. Historical180s timeout не считаются PASS: original literal probes затем действительно завершились при transparent bounded300s. Product/publication authority не выводится из tooling acceptance.

## T3. Terms, evidence и provider-neutral port

**Create:** `Core/Cancellation/CancellationTerms.cs`, `CancellationTermsFingerprint.cs`, `CancellationEvidence.cs`, `ManualResolutionInput.cs`; `Core/Providers/IFlightCancellationProvider.cs`, `Core/Providers/Dtos/CancellationProviderResults.cs`; `Unit/Cancellation/CancellationTermsTests.cs`, `CancellationEvidenceTests.cs`, `CancellationFingerprintTests.cs`.

**Consumes:** existing Money/Currency/Itinerary/owner. **Produces:** C1 terms/evidence/factory, C2. Manual decision enum: RecordInconclusive, CloseNotDispatched, ConfirmPreparedTerms, ConfirmCancellation, ConfirmNoEffect, ConfirmBooking, EnableLegacyCoordination; target matrix E1.

- [x] RED: zero valid; negative/null/unknown currency/expiry fail; credits/mixed/missing facts manual; same amount+different destination/revision/scope changes hash; support attestation cannot become SupplierApi. Historical JSON не проходит fresh rule validation.
- [x] Factory pattern, existing Money не менять:

```csharp
public static ErrorOr<CancellationTerms> Create(CancellationTermsInput input, DateTimeOffset now)
{
    var validation = Validate(input, now);
    if (validation.IsError) return validation.Errors;
    return CreateValidated(input, CancellationTermsFingerprint.Compute(input));
}
```

`Validate(input,now)` — private ErrorOr<Success> helper того же файла: все identity/known-settlement/expiry/notice validation из C1/E3 выполняются до fingerprint. `CreateValidated` — private constructor helper с defensive copies. Code excerpt показывает форму; все обязательные C1 поля проверяются до success. Не отдавать PII/ciphertext в terms response.

- [x] Implement neutral enums/facts; unknown enum не CashOnly. Hash decimal invariant/UTC/fixed fields не зависит от culture/JSON order.
- [x] Selected terms/evidence/fingerprint source tests passed in the audited isolated verifier (original Unit assembly PFX fixture was not executed locally). Exact linked-source proof is recorded in verification.md/process-log.md; regular assembly execution is existing-CI-only.

**Exit — выполнен:** pure factories/portable hash/neutral port self-reviewed; Core без HTTP. Local62/62 Core-only tests PASS, CSharpier10files; original assembly fixture/full-suite coverage reserved for existing CI.

## T4. Aggregate operation/confirmation state и compatible readers

**Modify:** `Core/Aggregates/BookingAggregate.cs` partial+owned decisions; `Infra/Marten/BookingAggregateConfig.cs`, `BookingAggregateProjection.cs` только если dispatcher требует forwarding; `Infra/Persistence/OrderReadModelEventApplier.cs`. **Create:** `Core/Aggregates/BookingAggregate.Cancellation.cs`; `Core/Cancellation/CancellationOperation.cs`, `ConfirmationAttempt.cs`, `RecoverySchedule.cs`; `Core/DomainEvents/CancellationEvents.cs`, `ConfirmationCoordinationEvents.cs`; `Unit/Cancellation/CancellationDecisionTests.cs`, `RecoveryScheduleTests.cs`, `ManualResolutionDecisionTests.cs`, `CancellationReplayTests.cs`.

**Produces:** C1 decisions и replayed operation/receipt dictionaries по S1 (не освобождать старые IDs); `RecoverySchedule.Create(Guid epoch, DateTimeOffset claimedAt)` → ErrorOr; `Reserve(int slot, DateTimeOffset now)` → `RecoveryReservation(Kind, UpdatedSchedule, ConsumedSlots)`, Kind Read/Skipped/NotDue/NoOp/Manual. Four slots/deadline310/window10 по design. Operation revision advances on its events, booking CAS по actual stream version.

**Exact new events:** BookingMutationCoordinationEnabled; CancellationPreparationStarted; CancellationPreparationDispatched; CancellationTermsObtained; CancellationTermsWereUnavailable; CancellationTermsAccepted; CancellationConfirmationDispatched; CancellationObservationStarted; CancellationObservationRecorded; CancellationOutcomeBecameUnknown; CancellationSucceeded; CancellationRejected; CancellationManualReviewRequired; BookingOperationReviewRecorded; CancellationReviewWasAbandoned; CancellationReviewExpired; CancellationRefreshRequested. ConfirmationAttemptStarted; ConfirmationEffectsClaimed; ConfirmationPaymentReferenceRecorded; ConfirmationCaptureObserved; ConfirmationAttemptCompleted; ConfirmationAttemptClosedWithoutEffects; ConfirmationAttemptRequiredManualReview. IDomainEvent+OccurredAt, no raw payload/PII; existing identities unchanged. BookingOperationReviewRecorded — общий audit только трёх M3 targets: targetKind/id, resolutionId, actor GUID, server recordedAt, request payload hash, normalized evidence/category/refs/source, accepted decision. Он обеспечивает replay/dedup и не меняет booking status сам; отдельные outcome/coordination events в той же транзакции меняют state. ConfirmationAttemptCompleted содержит attemptId, saved PaymentRef, acceptedMoney, supplierPaymentEvidence, ResolutionSource и known completion timestamps. CaptureObserved содержит attemptId/PaymentRef/acceptedMoney/observedAt и source=TestWalletObserved; raw receipts не сохранять.

- [x] RED transition matrix owner/state/consent/dispatch/replay; consent-abandon race; positive completed UnsupportedTerms can close, lost create cannot; Ticketed conditional new path; missing legacy marker; manual proof constraints; terminal no-op.
- [x] Pure reservation regression:

```csharp
[Fact]
public void Duplicate_slot_does_not_get_a_second_read()
{
    var at = new DateTimeOffset(2026, 10, 4, 0, 0, 0, TimeSpan.Zero);
    var schedule = RecoverySchedule.Create(Guid.NewGuid(), at).Value;
    var first = schedule.Reserve(0, at.AddSeconds(2));
    first.Kind.ShouldBe(RecoveryReservationKind.Read);
    first.UpdatedSchedule.Reserve(0, at.AddSeconds(3)).Kind
        .ShouldBe(RecoveryReservationKind.NoOp);
}
```

- [x] Implement decisions before effects, Apply unconditional/pure; metadata events advance checkpoint without changing displayed fields. Do not silently skip arbitrary unknown event. Terminal RequiredStreamVersion=loadedVersion+actual batch count, not old +1/+2 constants.
- [x] Unit filter `FullyQualifiedName~CancellationDecisionTests|FullyQualifiedName~RecoveryScheduleTests|FullyQualifiedName~ManualResolutionDecisionTests|FullyQualifiedName~CancellationReplayTests`; existing aggregate/replay/projection unit regression.

**Exit:** V1/V2/V3 compatible, all new registrations/projector cases covered, domain review before writers.

## T5. Duffel adapter и offline contracts

**Create:** `Infra/Providers/Duffel/DuffelFlightCancellationProvider.cs`, `DuffelCancellationMapper.cs`, `Dto/DuffelCancellationDetailsDto.cs`; `Unit/Cancellation/DuffelCancellationMapperTests.cs`; `Integration/Providers/Duffel/DuffelFlightCancellationProviderTests.cs`. **Modify:** existing `Dto/DuffelOrderDto.cs` additive current facts; `DuffelClient.cs` explicit bodyless confirm support if needed by exact contract; `FlightsInfrastructureServiceCollectionExtensions.cs` registration, no stacked retries.

**Produces:** C2. InspectOrder readonly; CreateTerms once; Confirm exact saved ref once; Observe cancellation GET then bounded order fallback, maximum2 logical GETs under total10s. No unbounded list scan; unknown create correlation manual, no new quote as observation.

- [x] RED pending vs confirmed; mismatched refs/money/destination; null expiry/credits; malformed/empty2xx; order cancelled different ID; GET404/pending remains Unknown; mixed grand total never cash-only. Valid pending quote with explicit null financial fields may yield UnsupportedTerms with positive completion; missing data/IDs or malformed response stays unknown and cannot enable abandon.
- [x] Mapper proof rules:

```csharp
// Matching validated confirmed facts => Confirmed.
// Exact documented first-attempt order_cancellation_stale => RejectedNoEffect.
// Prior unknown prevents that later refusal from releasing the barrier.
// Generic HTTP status/empty body/null confirmed_at => Unknown, not terminal proof.
```

`order_not_cancellable` belongs documented create boundary; never import order-creation503 or another endpoint's error into confirm. `already_cancelled` requires observation/manual, not our consent success. Safe categories only; no raw response/exception logging.

- [x] Registered client tests prove zero automatic POST retries, bounded GET budget. Existing direct `DuffelFlightBookingProvider.CancelOrderAsync` stays unsupported.
- [x] Mapper and actual loopback Duffel provider test sources passed in the audited isolated verifier. Original Integration assembly provisions PFX/key material, so its filtered command was not run locally. No Host/container/schema/real supplier was used.

**Exit:** offline contract PASS; supplier runtime remains untested.

## T6. Atomic commit, writers и durable recovery

**Modify:** `App/Persistence/DocumentSessionExtensions.cs`, `Api/Composition/FlightsModule.cs`, `Infra/Diagnostics/BookingConsistencyDiagnostics.cs`. **Create:** C3 command/message files and `App/Cancellation/CancellationStatusResult.cs`; handlers under `App/Handlers/Cancellation/`: PrepareCancellationHandler.cs, ExecuteCancellationPreparationHandler.cs, ConsentCancellationHandler.cs, ExecuteCancellationConfirmationHandler.cs, ObserveCancellationHandler.cs, CancellationAdmissionDeadlineHandler.cs, CancellationRecoveryDeadlineHandler.cs, GetCancellationStatusHandler.cs, AbandonCancellationHandler.cs. `Api/Composition/CancellationDeliveryPolicy.cs`; `App/Cancellation/IDispatchInstanceIdentity.cs`; `Infra/Cancellation/ProcessDispatchInstanceIdentity.cs`. **Tests:** `Integration/Cancellation/CancellationCommitTests.cs`, `CancellationDeliveryTests.cs`, `CancellationRecoveryTests.cs`, `CancellationTestFixture.cs`.

**Consumes:** C1–C3/T5. **Produces:** C4/C5. Singleton dispatch identity returns fresh random GUID per process, no PID/hostname/registry. No scanner/index/new scheduler.

- [ ] Actual T9 CI runtime gate (source prepared; not run locally): losing commit suppresses work; claim contains4 observations+deadline; one CAS winning sender; harmless revision change doesn't silence watchdog; Prepare watchdog cannot close Confirm; restart no republish/user GET.
- [x] Commit order:

```csharp
outbox.Enroll(session);
foreach (var item in work)
    await outbox.PublishAsync(item.Message,
        new DeliveryOptions { ScheduledTime = item.DueAt });
// Publish notifications + ReconcileOrderReadModel through same enrolled outbox.
await session.SaveChangesAsync(ct);
// Only successful claim commit permits provider mutation.
```

Null DueAt=immediate; no TTL-drop. NonTransactional explicit commit and classified conflicts remain; no generated second save after caught409.

- [x] Loaded claimed stage always observes, never resends. Slot CAS before GET; active-window consume+skip; late positive accepted through fresh session/decision. Storage1/5/30→DLQ, terminal source→DLQ; no error clears barrier.
- [x] Diagnostics correlation/whitelist adds ObserveCancellation, AdmissionDeadline, RecoveryDeadline, ConfirmationBarrierDeadline only. Execute/HTTP booking commands denied. New safe-recovery replay can proceed with missing/behind EF because stream authoritative; existing unrelated projection-first policy remains. Source-owner corruption fails closed.
- [x] Local compile only DB suites: `dotnet build tests/flights/Travel.Modules.Flights.Tests.Integration/Travel.Modules.Flights.Tests.Integration.csproj --maxcpucount:1`. Execute fixtures only existing CI T9 after authorized publication.

**Source/local Exit — выполнен; runtime acceptance T9:** source compile/review; durable gate still T9, fake outbox/source inspection isn't its substitute.

## T7. Existing hold/confirm coordination

**Modify:** `App/Handlers/Booking/HoldOfferHandler.cs`, `ConfirmOrderHandler.cs`, `CancelOrderHandler.cs`; `Core/Providers/Dtos/ConfirmedOrder.cs`; `Infra/Providers/Duffel/DuffelFlightBookingProvider.cs`. **Create:** `Core/Providers/Dtos/SupplierPaymentEvidence.cs`, `App/Handlers/Cancellation/ConfirmationBarrierDeadlineHandler.cs`; `Unit/Cancellation/ConfirmationBarrierTests.cs`, `Integration/Cancellation/ConfirmationCoordinationTests.cs`. **Extend:** existing `Integration/Booking/ConfirmOrderHandlerTests.cs`, `CancelOrderHandlerTests.cs`, successful provider receipt fixtures.

**Contract:** E2. New hold commits OfferHeldV3 then coordination marker in one batch; old event not changed. Existing confirm HTTP body/middleware retained; persisted claim prevents repeat effects even after cached key loss/new key.

- [ ] Actual T9 financial fault/race runtime gate (pure barrier/source checks passed): confirm/cancel winner, crash before/after EffectsClaim, authorize before ref-save, capture before observation save / supplier success before final commit; old/new keys never repeat effects; unmarked legacy Held named manual gate.
- [x] Admission/deadline and effects claim before wallet. Known paymentRef persisted before capture; successful Capture result сохраняется как ConfirmationCaptureObserved **до** supplier Confirm. Failure этой записи останавливает chain без compensation. Each awaited step rechecks state. Success receipt adds C6 only after existing strict provider validation; missing evidence after capture→Unknown.

```csharp
// Persist ConfirmationEffectsClaimed before AuthorizeAsync.
// Persist ConfirmationPaymentReferenceRecorded before CaptureAsync.
// Persist ConfirmationCaptureObserved after Capture success, before provider Confirm.
// Loaded EffectsClaim never re-enters money chain.
// PaymentAuthorized + OrderConfirmed + AttemptCompleted + exact-version siblings commit together.
```

- [x] Pre-effect deadline CAS-closes NotDispatched; post-effect only manual. No auto refund/compensation/payment recovery. Memory wallet absence never negative proof. Legacy clear uses all-old-writer drain, not nonexistent instance equality.
- [x] Bodyless cancel TermsRequired before new HTTP; terminal owned no-op retained. New Ticketed path C1 conditional, no passenger decrypt.
- [x] Pure barrier Unit filter and serialized compile; real handler/outbox proof T9.

**Source/local Exit — выполнен; runtime acceptance T9:** server coordination survives reload, old history preserved, independent source review; no promise to recover missing wallet facts automatically.

## T8. Manual review, refresh и HTTP authorization

**Create:** `App/Handlers/Cancellation/ResolveCancellationReviewHandler.cs`, `RefreshCancellationHandler.cs`, `GetCancellationReviewHandler.cs`; `Api/Contracts/CancellationRequests.cs`, `CancellationResponse.cs`; endpoints in `Api/Endpoints/`: PrepareCancellationEndpoint.cs, ConsentCancellationEndpoint.cs, AbandonCancellationEndpoint.cs, RefreshCancellationEndpoint.cs, GetCancellationStatusEndpoint.cs, GetCancellationReviewEndpoint.cs, RefreshCancellationReviewEndpoint.cs, ResolveCancellationReviewEndpoint.cs. `Api/Middleware/CancellationRequestBodyGuard.cs`; `Unit/Cancellation/CancellationRequestValidationTests.cs`; `HttpTests/CancellationApiFixture.cs`, `CancellationJwtHttpTests.cs`, `CancellationHttpTests.cs`, `CancellationEndpointMetadataTests.cs`; `Integration/Cancellation/ManualCancellationResolutionTests.cs`, `CancellationRefreshConcurrencyTests.cs`.

**Modify:** Api facade policies/discovery/body guard; existing CancelOrderEndpoint still RFC7807. New fixture follows lean TestServer/ProfileJwtTestTokens: real signature/issuer/audience/lifetime validation, fake bus only; full Host fixture CI. Actual isolated Wolverine metadata discovery has separate test; full OpenAPI snapshot acceptance CI.

- [x] Pure RED matrix: owner can't resolve; wrongscope/badGUID fail; known/new receipt binding; LegacyHeld aggregate target+current revision; missing/wrong/stale quiescence fields insufficient; RecordInconclusive stays blocked; same resolutionId/body with stale revision returns receipt/no new event, changed payload409; same old prepare/consent ID after later operation/restart never dispatches again.
- [x] Resolver load→actor/target authorization→resolutionId+fingerprint receipt lookup→для нового решения revision/evidence decision→operator audit+outcome atomic. Exact retry со старой expectedRevision возвращает receipt до fresh-version guard; different fingerprint409. Source OperatorVerified never SupplierApi, owner DTO omits operator/process refs. Trust boundary explicitly E1/E4.

```csharp
[Authorize("flights:cancellation-review")]
// Validated GUID sub supplies actor; body cannot supply isOperator/actor/status.
// Core decision validates target binding, proof requirements and current revision.
// One commit records audit + outcome; no implicit supplier or payment call.
```

- [x] E3 exact route/body/error/no-store; duplicate/unknown fields and16KiB checked before bus. Raw prepare/consent fingerprints bound to owner/stage; lookup exact historical request before current revision/expiry guards, no reused old operation ID; no logs/EF response cache.
- [x] Persisted owner+booking NextOwnerRefreshAt60s is shared by owner/operator endpoint; pending read uses common10s window. Duplicate last ID/body reuses accepted work; early new ID429+Retry-After. Refresh stage matrix S2 prevents supplier reads/empty epochs before claim and for completed unaccepted review. Operator scope uses dedicated `/review/refresh`; ordinary owner endpoint gets no bypass. Resolve/query has no hidden network.
- [x] Status blocker is discriminated: ConfirmationAttempt includes real attemptId/revision; LegacyHeld uses kind=LegacyHeld, targetId=aggregateId, booking revision, no invented attempt history. GET works when cancellation operation=null.
- [x] Local pure, signed-JWT lean HTTP and actual Wolverine metadata tests passed in audited isolated verifier. Regular Host test assembly provisions PFX at assembly startup and remains CI-only. Actual DB refresh/manual races T9 only.

**Source/local Exit — выполнен; runtime acceptance T9:** operator contract executable via API and fictional tests, no new Support/admin subsystem or real credentials/provisioning. Manual API use/evidence procedure E1/E4 is the operator runbook in this canonical corpus.

## T9. Durable backend proof в существующем CI

**Files:** T6–T8 suites + `Integration/Cancellation/CancellationRestartTests.cs`; current Integration project normal source inclusion. Existing full-Host OpenAPI verified snapshot changes only actual endpoint metadata. No new project/job/runner/workflow. Snapshot acceptance is actual CI execution, not manually fabricated JSON pass.

- [x] Audit fixtures: Testcontainers/Marten AutoCreate/EF EnsureCreated and PFX/key assembly fixture are CI-only. Integration and Host test projects compiled locally without fixture execution.
- [ ] Authorized PR uses current CI. Required named cases: LostConfirmResponseRestoresSuccess; ClaimBeforeSendRestartStaysSafe; SlotClaimBeforeGetUsesLaterDeadline; LatePositiveAfterManualFinalizes; DuplicateWorkersDoNotResend; WatchdogIdentitySurvivesRevisionAdvance; OldPrepareWatchdogCannotCloseConfirm; ExpiredLeaseNeverResends; ExternalSuccessLocalRollbackRecovers; ProjectionLagDoesNotHideOutcome; ConcurrentConfirmCancel; ManualEvidenceAndSourceLabels; OwnerOperatorShareRefreshLimit; LegacyMarkerReplay.
- [x] Prepare CI-only fault sources at commit/send boundaries against controlled fictional supplier; compilation passed, actual injection runtime remains the preceding CI gate. HostB consumes persisted messages without republish/user GET. Delayed hostA callback proves missing quiescence evidence cannot clear an unknown barrier; host replacement alone is not proof of physical process drain.
- [ ] Failure returns to owning task and affected lane. Existing projection-green doesn't replace new atomic scheduled-outbox proof. Record exact head/run/limits.

**Exit:** required actual backend CI before UI acceptance. Without publication authority this is an unexecuted verification gate, never a planning gap or invented pass.

## T10. Shared TypeScript contracts

**Create:** `Client/flights-cancellation.types.ts`, `flights-cancellation.decoder.ts`, `flights-cancellation.decoder.spec.ts`, `flights-cancellation-api.service.ts`. **Modify:** `Client/index.ts`; additive cancellation cases in `tests/fixtures/flights-booking.json`, historical raw responses unchanged.

**Produces:** E3 request/status types and strict decoder; service status/prepare/consent/abandon/refresh. Tokens headers/memory only; raw body frozen per stage. Ordinary owner service has no operator resolver.

- [x] RED: invalid status/version/aggregate mismatch, operation=null+real/legacy blocker, null proof/source false success, malformed dates/money, unknown enum; old terminal order without operation still readable; historical exact replay after a newer prepare uses isCurrentOperation=false and cannot become current UI; late old confirm success cannot override cancellation.
- [x] Decode unknown, no unchecked casts; consumer checks monotonic revisions. Client echoes termsHash, does not calculate it.

```typescript
status(aggregateId: string, token: string | null): Observable<CancellationStatusResponse>;
prepare(rawBody: string, token: string | null): Observable<CancellationStatusResponse>;
consent(rawBody: string, token: string | null): Observable<CancellationStatusResponse>;
```

Abandon/refresh use explicit E3 raw body.15s client timeout means response uncertainty, not supplier rejection; no storage/backdoor retry key persistence.

- [x] `npx.cmd nx test api-client`, preserve existing journey/history/booking readers.

**Exit — выполнен:** single shared transport contract,133/133 client regressions PASS.

## T11. Owner UI и fictional acceptance

**Create:** `Web/flight-cancellation.service.ts`, `.spec.ts`; `flight-cancellation-review.component.ts`, `.html`, `.scss`, `.spec.ts`; `tests/travel-e2e/demo/flights-cancellation-recovery.spec.ts`. **Modify:** existing flight-order-page.component.*, flight-order-operations.service.*, flight-orders-feed.service.* (только их existing .ts/.html/.scss/.spec.ts files); `tools/demo/flights-search-api.mjs`, `.test.mjs`; existing demo cancellation/order tests. Production fake auth/provider не добавлять.

**Contract:** view of server operation; existing confirm UI consults persisted blocker. Tab memory+owner epoch only. E3 polling; terminal/manual stop auto polling, explicit refresh follows nextRefreshAt. Background recovery doesn't depend on UI.

- [x] RED Angular: full legs/party consent, changed terms clears acceptance, rapid double click uses same request, reload onlyGET, A→B→A stale callbacks dropped, same-owner token refresh valid, old EF Held/404 cannot erase success, confirmation/legacy blocker without cancellation operation.
- [x] Display amount/currency/destination/expiry/scope and separate provider return/customer payout notice. Ticketed availability server-owned. Distinct Preparing/TermsReady/Unknown/Manual and provenance. No success for202/CreateTerms or generated refund from booking total.

```typescript
// Current owner epoch + aggregate guard every callback.
// Lower operation revision/booking evidence cannot replace newer state.
// Reload invokes status only; acceptance checkbox is never restored from storage.
```

- [x] Demo API test-only faults: lost create/confirm, save fault, delayed reply, two tabs. Typed manual resolution is verified against production Core/signed-JWT lean HTTP and CI-only audit/retry sources, without copying operator rules into Node or adding an owner/admin resolver flow. See verification.md for this proof-placement refinement. Uses fictional data only, exposes no real credentials. Demo restart is UI evidence; real durable restart is T9.
- [x] Local `npx.cmd nx test web --watch=false`; `npx.cmd nx build web --configuration=production`; `node --test tools/demo/flights-search-api.test.mjs`; `npx.cmd playwright test -c tests/travel-e2e/playwright.flights-demo.config.ts tests/travel-e2e/demo/flights-cancellation-recovery.spec.ts`. Config starts only Node fictional API and Angular, not Host/AppHost. Then affected/full demo regressions as changes justify.
- [x] Inspect desktop/mobile360px/keyboard/focus/back-to-feed anchor/auth/manual. Stop owned preview processes.

**Source/local Exit — выполнен:**377 Angular/55 Node/4 browser cases and production build PASS. Feature acceptance remains dependent on actual T9 CI; no live supplier/payout claim.

## T12. Final review/docs и delivery handoff

**Modify in approved product change:** ADR0015/0016/0017/0023/current-state/root and Flights instruction descriptions where behavior changed; link to this corpus, not a second specification. AGENTS/CLAUDE adapters remain within harness contract. Actual result/review/process evidence kept in this change.

- [x] All17 requirements/38 scenarios mapped to real local proof and separately open decisive gates in verification.md. Source/unit/UI never substitutes T9 or supplier runtime proof.
- [x] Sequential local formatting/architecture/inventory checks and independent whole-change source review/fix pass. Four P2 closed; no open P1/P2 in reviewed source scope.
- [ ] Actual exact-head CI, full Host OpenAPI snapshot received-artifact review and durable acceptance; no fabricated snapshot PASS.
- [ ] Freeze corpus with actual product head/checks/review/costs; distinguish adapter overhead, manual planning and CLI/model work. Do not preclaim future CI/merge.
- [ ] Execute Delivery only with applicable publication authority; complete product and docs closure before cleanup, no local deliverables/unpublished commits left.

## Требования → task → proof

| Spec behavior | Tasks | Decisive proof |
| --- | --- | --- |
| Whole-order owner/eligibility/Ticketed | T3/T4/T8/T11 | Core matrix + realJWT foreign/no-owner/no-provider + whole-party UI |
| Terms/monetary composition | T3/T5/T10 | zero/null/mixed/original-form provenance fixtures |
| Immutable consent/stale expiry/replace | T4/T6/T8/T11 | stage CAS, zero outbound on stale |
| Durable discovery/reload/restart | T6/T9/T11 | stored messages hostB, no republish/clientGET |
| Single dispatch/competing confirm | T4/T6/T7/T9 | concurrent writer/delayed original sender |
| Confirmed/Rejected/Unknown | T3/T5/T6/T8 | structured evidence, first rejection vs prior unknown |
| Correlation/different cancellation | T5/T8/T9 | exact refs/terms, external cancellation not own success |
| External success/local failure | T6/T9 | rollback then observed finalization |
| Projection lag/monotonic view | T4/T9/T10/T11 | checkpoint/rebuild/stale GET UI |
| Manual recovery/operator trust | T4/T8/T9 | scope/source/attestation/drain/time/identity matrix |
| Supplier return vs customer payout | T3/T8/T11 | no invented refundRef/order-total refund, copy/assertions |
| Historical/activation | T4/T7/T9 | V1/V2/V3 replay/marker/legacy gate |
| Evidence boundaries | T1/T2/T9/T12 | distinct CLI/agent/durable/runtime reports |
| Safe unaccepted review end | T4/T6/T8/T11 | consent-abandon-expiry race, unknown remains blocked |
| Recovery without client/bounded budget | T4/T6/T9 | all5 envelopes, slot loss/deadline/restart/DLQ |
| Shared refresh admission | T8/T9/T11 | owner/operator CAS/429, no reset |

## Команды финальной проверки

После product execution/fixture audit, последовательно локально: audited isolated verifier source set из verification.md/process-log.md (regular PFX-fixture assemblies CI-only); `dotnet test tests/Travel.Tests.Architecture/Travel.Tests.Architecture.csproj --maxcpucount:1`; `dotnet csharpier check .`; `npx.cmd biome ci .`; `npm.cmd run check:ai-harness`; `npm.cmd run check:dotnet-inventory`; `npm.cmd run check:readme-examples`. DB/full Host/Aspire — existing CI, paid evals excluded. No local dotnet run Host/AppHost/solution aggregate tests. Reviewed source/plan не являются test pass.

## Delivery и публикация OpenSpec — обязательный протокол PR3

Этот раздел — lifecycle/publication protocol **без implementation checkboxes**: будущий merge нельзя заранее отметить выполненным ради archive. Закрытие research/tooling/product checkboxes происходит только по фактической evidence. Archive не пытается «выполнить сам себя» как последнюю unchecked задачу. Workflow self-check не выдаёт delivery authority.

1. **Product PR.** Отдельно получить применимую branch/stage/commit/push/PR/merge authorization. При промежуточном PR для T9 proof сохранить все unfinished tasks, не archive. Final product head содержит реализованный продукт, актуальный active change и фактический test/review report. Exact-head mandatory checks (включая E2E по действующей policy), merge и postmerge обязательны. M2.5 permissions не переносятся.
2. **Документальное закрытие отдельным docs-only PR.** После product acceptance и postmerge fresh fetch origin/dev, записать SHA и ancestry checkpoint/M1/product merge. Создать clean docs branch/worktree от этого fetched SHA, без current-HEAD substitute или archive restore. В нём сверить spec↔merged code, закрыть только действительно выполненные implementation tasks, выполнить через pinned launcher штатный archive с **однократной** синхронизацией delta в current capability. Отдельный предварительный sync не запускать, чтобы ADDED requirements не применились дважды. Если что-либо остаётся unresolved, change остаётся active; archive не обходить force/skip. Product/tooling code и CI/CD в closure PR не менять.
3. **Review и проверки docs diff.** Проверить, что `openspec/specs/flights-whole-order-cancellation/spec.md` содержит принятую implemented capability, active change перенесён в archive ровно один раз, нет второго current corpus; evidence/review/process-log перенесены без потери, relative references поправлены. Full OpenSpec strict validation, exact harness, ссылки/формат, independent closure review. Зафиксировать product merge/run evidence и измеренные process costs до публикации docs PR.
4. **Публикация closure.** Отдельная применимая commit/push/PR/merge authorization должна покрывать и этот docs PR. Выполнить current mandatory exact-head checks и postmerge без изменения CI/CD. До его merge итоговый canonical OpenSpec state не считается доставленным, cleanup запрещён. Полученные **после** freeze closure commit/run результаты сообщить в чате/PR metadata; не делать бесконечный новый journal commit только ради ссылки на собственный merge. Если всё-таки изменён файл, опубликовать этот diff и снова проверить состояние до cleanup.
5. **Cleanup gate.** Fresh fetch/sync local dev до фактически merged origin/dev; в remote tree проверить final canonical spec и archived change, отсутствие второй active копии. Все собственные planned source/docs файлы должны быть опубликованы; в owned worktrees нет staged/unstaged/untracked deliverables и неопубликованных коммитов относительно принятого dev. Только затем удалить/архивировать свои ресурсы штатными tools. Root dev и чужую работу не трогать, archived worktrees не восстанавливать.

Supplier sandbox acceptance — отдельный будущий scope/authorization; реальные suppliers/платежи/Anthropic/paid API, local schema/Host/deploy не входят в текущий план. D1–D8 и направление уже приняты; окончательный план закрывает технические решения. Execution/delivery выполняются в разрешённом scope, без нового approval на само планирование.












T4 execution interface refinement (review regressions): CancellationObservationStarted(...,OccurredAt,bool ReadAllowed=true) explicitly distinguishes a real read reservation from consumed busy-window coalescing. T6 Observe handler checks ReadAllowed before HTTP. Core persists read binding and stage-specific uncertainty from the same events; no new database fields or scheduler are required.
