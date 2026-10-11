# Tasks

**Goal:** один fictional customer refund после доказанной whole-order cancellation, без повторного денежного эффекта.
**Architecture:** один BookingAggregate stream, narrow refund port,3durable work messages и отдельный owner status; неизвестное завершается ручным разбором.
**Tech Stack:** текущие .NET10/Marten/Wolverine/xUnit v3, Angular21/shared TS/Vitest, Node/Playwright; новые пакеты не нужны.
**Spec:** [requirements](specs/flights-customer-refund-after-cancellation/spec.md), [design D1–D7](design.md), [current evidence](research.md).
Checkbox означает полное выполнение и доказательство acceptance пункта. Частичная реализация отмечена в process-log.md и checkpoint.md.

Global constraints: только вымышленные данные; без real supplier/payment/Anthropic/paid API и paid evals;
без local Host/AppHost/DB/schema/migrations/provision/deploy; .NET последовательно;
CI/CD/auth/чужую работу не менять. Stage/commit/push/merge и исполнение плана отдельно разрешаются.
Никакого второго feature plan. Для реализации inline execution через executing-plans;
отдельный whole-change reviewer не заменяет согласование.

Review focus: (1) Refunded label без capture proof, (2) signal между claim и отправкой,
(3) success после deadline/failed commit, (4) response прежнего owner/version,
(5) эквивалентный replay manual resolution со старым revision.
Каждый пункт получает regression в группе владельца кода ниже.

## 1. Доменный денежный контракт и compatibility

**Files:** создать Core `Refunds/CustomerRefundContracts.cs`, `Refunds/CustomerRefundCase.cs`,
`Providers/ICustomerRefundGateway.cs`, `DomainEvents/CustomerRefundEvents.cs`,
`DomainEvents/SupplierCancellationReported.cs`, `Aggregates/BookingAggregate.CustomerRefund.cs`.
Изменить `Aggregates/BookingAggregate.Confirmation.cs`, `BookingAggregate.Cancellation.cs`
и необходимые creation/mutation guards; existing `Cancellation/ManualResolutionInput.cs` и
`Aggregates/BookingAggregate.ManualResolution.cs` расширить report target. Полный Core prefix:
`modules/flights/Travel.Modules.Flights.Core/`.
Тесты: новые `tests/flights/Travel.Modules.Flights.Tests.Unit/Refunds/CustomerRefundPolicyTests.cs`,
`CustomerRefundDecisionTests.cs`, `CustomerRefundReplayTests.cs`, `RefundTestBookings.cs`.

**Interfaces:** immutable CustomerPaymentBasis/source, CustomerRefundCase, CustomerRefundDecision
по existing allowed/rejected/no-op pattern; enum outcomes в D3.
Shared orchestration seam:
```csharp
public sealed record CustomerRefundRequest(
    Guid RefundId, CustomerPaymentBasis OriginalPayment,
    Money Amount, CustomerRefundDestination Destination);
public interface ICustomerRefundGateway
{
    Task<ErrorOr<CustomerRefundObservation>> ExecuteAsync(
        CustomerRefundRequest request, CancellationToken ct);
}
// Aggregate typed decisions: admission, claim, outcome, deadline, manual review.
// Все принимают target identity, expected revision и TimeProvider-derived now;
// Apply остаётся unconditional историческим reader.
```

- [x] 1.1 Написать failing policy matrix:100/80GBP ->80;100/0 -> NoRefundDue; proven unpaid -> NoCustomerPayment; absent capture -> LegacyUnverified/manual;110/100, FX, credits, unsupported destination, OperatorVerified cancellation и unresolved confirmation ->0gateway permissions. Использовать только pure RefundTestBookings; завершение: каждый spec D1/zero сценарий имеет точное ожидаемое decision.
- [x] 1.2 Реализовать basis/policy factories с ErrorOr, immutable frozen money/currency/source/payment/whole-order binding; не читать wallet cache и не создавать captures. Проверка: policy matrix PASS,9adults/4legs/ancillaries не умножают сумму, old OrderRefunded не даёт payout eligibility.
- [x] 1.3 Добавить admission/claim/result/deadline/review decisions и Apply: one case per booking/payment; expected-version conflict; no claim после admission+180s; existing claim never repeats; receipt identity/money/destination match; pending/unknown != terminal success; terminal no-effect != paid entitlement. Проверка: pure decision tests включая duplicate delivery, разные request IDs,120s operation budget и unchanged deadline после replay.
- [x] 1.4 Добавить retained supplier-report identity/revision/guard и existing manual target: ConfirmedCancellation/ConfirmedNotCancelled/Inconclusive разрешают только report, с verified actual CancelledAt отдельно от server ObservedAt. Не закрывать competing attempts/не создавать money terms. Проверка: report для Confirmed/Ticketed без operation разрешим; report-before-confirm/between-claim-and-call, negative no-pending, unrelated live attempt, repeated old resolution/new report и proven cancellation остаются truthful,0новых payout effects.
- [x] 1.5 Проверить additive serialization/replay V1/V2/V3, old Confirmation/Cancellation/OrderRefunded и consent/hash bytes; новые events не меняют old BookingStatus/refundedAt. В том же change описать fixture outputs/policy-v1; завершение: historical replay и exact-money tests PASS в audited isolated Core subset либо existing CI.

Минимальный тестовый смысл (fixture из1.1):
```text
booking = RefundTestBookings.ProvenCancelled(captured=100GBP, entitlement=80GBP)
admission = booking.DecideCustomerRefundAdmission(...)
assert admission freezes 80GBP and original TestWalletObserved basis
assert applying/repeating admission creates one case
assert existing claim returns no new gateway permission
```

## 2. Durable admission/execute/deadline и truthful webhook

**Files:** создать Application `Refunds/CustomerRefundMessages.cs`,
`CustomerRefundWriter.cs`, `CustomerRefundStatusResult.cs`, `CustomerRefundStatusFactory.cs`,
`Handlers/Refunds/AssessCustomerRefundHandler.cs`, `ExecuteCustomerRefundHandler.cs`,
`CustomerRefundDeadlineHandler.cs`, `GetCustomerRefundStatusHandler.cs`;
изменить `Cancellation/CancellationDecisionWriter.cs` и
`Handlers/Webhooks/DuffelWebhookHandler.cs`.
Application prefix `modules/flights/Travel.Modules.Flights.Application/`.
Infrastructure: изменить `Marten/BookingAggregateConfig.cs`,
`Persistence/OrderReadModelEventApplier.cs`,
`Diagnostics/BookingConsistencyDiagnostics.cs`; replay projection проверить без рефакторинга.
Тесты: `Tests.Unit/Refunds/CustomerRefundOrchestrationTests.cs`,
`Tests.Integration/Refunds/CustomerRefundOutboxTests.cs`, `CustomerRefundRestartTests.cs`,
и existing `Webhooks/DuffelWebhookHandlerTests.cs`, `ReadModels/`.
Полный tests prefix `tests/flights/Travel.Modules.Flights.`.

**Consumes:** Core contract1; existing SaveBookingWithWorkAsync and FetchForWriting.
**Produces:** AssessCustomerRefund(booking,cancellation), ExecuteCustomerRefund(booking,case),
CustomerRefundDeadline(booking,case,deadline), stream-owned status.
```text
commit(cancellation_success + outbox assessment)
assessment CAS: freeze case + outbox execute + fixed deadline
execute CAS: save claim -> at most one gateway ExecuteAsync
redelivery after claim: no gateway call; keep unknown/manual
deadline: only local transition, no supplier/payment call
record receipt: fresh local CAS; never re-execute external effect
```

- [x] 2.1 Написать fake-session failing tests для cancellation success+assessment atomicity, duplicate assessment, admission-before-send и losing-CAS0calls. Реализовать separate refund writer/handlers через existing enrolled outbox, без второй saga/store. Проверка: rollback не публикует работу; success publishes one assessment; foreign/ownerless stream ничего не отправляет.
- [ ] 2.2 Реализовать one claim и независимый bounded external ct; generic errors/timeout/cancellation после claim -> unknown. Receipt validates frozen target; known result может быть сохранён только без нового external вызова. Проверка: crash before send/after send, cancelled browser token, Pending/ConfirmedNoEffect/mismatch, late positive result и failed local save оставляют truthful case; repeated handler gateway call count<=1.
- [ ] 2.3 Реализовать fixed admission+180s deadline через retained work: duplicate/restart не меняют дату; sender не claim'ит после deadline; no-browser crash достигает manual; delivery failure остаётся diagnostic. Добавить реальные persisted restart/concurrency/atomic rollback tests в existing Integration suite. Проверка этих сценариев — existing CI, locally fake-session cases only.
- [ ] 2.4 Register каждый новый event и event-applier no-display-field branch с owner validation/checkpoint advance; поддержать replay/rebuild без EF column/migration. Проверка: consecutive refunds events не дают SourceEventUnsupported, validate/rebuild равны incremental projection, old events/JSON читаются.
- [ ] 2.5 Заменить новый airline OrderRefunded append на SupplierCancellationReported; поддержать SupplierCancellationReportResolved и старый audit в writer/replay. Сохранить inbox binding/processed acknowledgement после commit/no-op. Проверка: payload без суммы не даёт TotalAmount/RefundRef/customer payout; concurrent/repeated inbox и late confirmation сохраняют guards,0refund calls; actual cancellation time не заменяется временем обработки.
- [x] 2.6 Обновить ADR0015/0016/0018/0019 короткими factual amendments и этот change evidence; исключить historical user-initiated reuse OrderRefunded promise. Проверка: ссылки ведут в один capability corpus, отсутствие claims про real refunds, schema или полное M3.

## 3. Fictional adapter, API и privileged manual proof

**Files:** создать Infrastructure `Payments/FictionalCustomerRefundGateway.cs`;
изменить `FlightsInfrastructureServiceCollectionExtensions.cs`.
Создать Api `Composition/CustomerRefundDeliveryPolicy.cs`,
`Contracts/CustomerRefundResponse.cs`, `CustomerRefundReviewRequest.cs`,
`Endpoints/GetCustomerRefundStatusEndpoint.cs`, `GetCustomerRefundReviewEndpoint.cs`,
`ResolveCustomerRefundReviewEndpoint.cs`; модульная policy в `Composition/FlightsModule.cs`.
Existing Api `Contracts/CancellationRequests.cs` и Application
`Cancellation/CancellationReviewFactory.cs`, `CancellationReviewResult.cs`,
`Handlers/Cancellation/ResolveCancellationReviewHandler.cs` расширить optional report target
и bounded ReportEvidence. Owner status4 получает metadata-only report state.
Body/no-store guards рядом с existing CancellationRequestBodyGuard, без изменения Host order.
Application: `Handlers/Refunds/GetCustomerRefundReviewHandler.cs`,
`ResolveCustomerRefundReviewHandler.cs`, typed review mapping в `Refunds/`.
Тесты: Unit/Refunds adapter/receipt tests, existing architecture guard tests,
`tests/Travel.Host.Tests.Integration/Flights/CustomerRefundHttpTests.cs`,
`tests/flights/Travel.Modules.Flights.Tests.Integration/Refunds/CustomerRefundReviewTests.cs`.
Префиксы module files — `modules/flights/Travel.Modules.Flights.<Layer>/`.

**Consumes:** frozen Core contract и stream status из1–2.
**Produces:** owner GET response exact decimal money; review GET/POST только отдельной scope,
no owner mutation и никакой retry money action.

- [ ] 3.1 Написать typed fake adapter tests Sent/Pending/ConfirmedNoEffect/Unknown и invalid receipt; реализовать startup-fixed offline presets без HTTP/key/card data. Verification: adapter accepts persisted TestWalletObserved basis after restart,0legacy RefundAsync calls; Production registration/marker/runtime guard tests exclude fake.
- [ ] 3.2 Module-owned queue настроить только для3messages, safe storage/conflict retries1/5/30s+DLQ, без payment POST retry. Добавить policy tests: claim re-entry не вызывает adapter, terminal/owner failures не превращаются в успех. Дополнить read-only BookingConsistencyDiagnostics correlation для3work types, отделив её
  от replay allowlist; только CustomerRefundDeadline может быть replayable. Проверка:
  assessment/execute видны при inspection, generic replay отвергнут до dispatch; Host global
  policies/CI files неизменны.
- [ ] 3.3 Owner GET `/api/flights/orders/{aggregateId}/customer-refund` читает только authorized stream. Review GET/POST `/customer-refund/review` требуют `flights:customer-refund-review`, JWT actor, expected revision, normalized resolution identity и bounded16KiB body/no-store. Проверка:401/403/foreign404/ownerless/malformed/extra fields, no-store включая errors, нет PII/refs в owner DTO и diagnostic echo.
- [x] 3.4 Реализовать Sent/ConfirmedNoEffect/Inconclusive manual decisions по frozen compatible basis. Exact accepted replay до new revision guards; changed payload conflicts. Отсутствие quiescence+positive no-effect/no-pending не закрывает unknown; unsupported history нельзя arbitrary seed'ить. Проверка: negative old sender still active, timeout-only, wrong receipt, duplicate old revision, late positive and conflicting evidence; POST never calls gateway.
- [ ] 3.5 Расширить existing cancellation-review GET/POST отдельным SupplierCancellationReportTarget: доступен без cancellation operation и одновременно с unresolved primary Target, flights:cancellation-review scope, typed ReportEvidence, stable legacy hashes. Проверка: source-bound report на Confirmed/Ticketed resolve'ится; actual CancelledAt/ObservedAt различены; owner/cancellation reviewer не получает customer-money authority; competing attempt не закрывается. Обновить request examples/OpenAPI snapshot и процедуру в этом change; actual Host checks через existing CI.

## 4. Shared client и owner UI

**Files:** создать
`shared/ts/api-client/src/flights-customer-refund.types.ts`,
`flights-customer-refund.decoder.ts`, `flights-customer-refund.decoder.spec.ts`;
client export/HTTP метод рядом с existing flights cancellation.
Создать `apps/web/src/app/flights/flight-customer-refund.service.ts`,
`flight-customer-refund.component.ts`, `.html` и focused specs;
изменить `flight-order-page.component.ts/.html`, historical labels existing order/list view.
Стили — existing order/cancellation card pattern, без redesign.

**Consumes:** GET response3, existing auth owner/epoch и booking resource route.
**Produces:** read-only refund card, server money/provenance/state, independent loading/error.

- [x] 4.1 Добавить typed decoder строгой money decimal-string формы, case/booking version и states; reject wrong booking, unknown required status, mismatched currency shape, malformed receipt fields. Verification: shared Vitest decoder cases и explicit privacy check; браузер не рассчитывает сумму через Number/FX.
- [x] 4.2 Реализовать owner-bound GET service с epoch и monotonic stream/case version; bounded polling только local GET, конечное ожидание и explicit refresh. Проверка: old owner callback, stale version, reload, logout, response error after Sent и missing EF никогда не создают mutation/не стирают evidence.
- [x] 4.3 Card отдельно показывает supplier entitlement, settlement NotEstablished, customer exact amount/original method и FictionalGateway/OperatorVerified; Sent -> «Демо: возврат отправлен», bank credit unestablished;0/NoCustomerPayment/Unknown/manual distinct. Historical Refunded на detail/list не заявляет payout. Проверка: Angular rendering/accessibility/mobile cases, ни input payment details, ни кнопка «повторить выплату».
- [x] 4.4 Пояснить owner manual fallback: сохранять case/order resource для помощи без provider references; никакие identities/token/PII не сохранять в storage/URL/history/log. Проверка: storage/URL/console regression, existing cancellation consent/hash and ancillaries displays unchanged; UI tests/build verify existing styling warning отдельно.

## 5. Fictional demonstrations и cross-layer acceptance

**Files:** `tools/demo/flights-search-api.mjs`, новый cohesive
`tools/demo/flights-customer-refunds.mjs`, relevant `.test.mjs`,
`tests/fixtures/flights-customer-refunds.json`,
`tests/travel-e2e/demo/flights-customer-refunds.spec.ts`;
`openspec/changes/flights-m3-customer-refunds/demo.md`, `verification.md`.
Tests этой группы проверяют end-to-end interactions, не заменяют group1–4 tests.

- [x] 5.1 Startup presets `refund-sent`, `refund-zero`, `refund-result-lost` через existing fictional demo config: успешная покупка с услугами -> согласованная отмена ->80GBP Sent; zero ->0gateway; result loss ->same retained manual case. Проверка: Node tests count calls, no raw PII/Authorization, не добавлен runtime fault/resolve API, GET не исполняет workflow.
- [x] 5.2 Playwright three independent starts: all passengers/legs/services, exact80GBP, no supplier settlement/bank-credit claim, reload+stale GET, zero/no-send, lost result without retry. Операторское завершение показать separately typed API fixture3.4, без unauth review endpoint в demo. Проверка: fictional browser suite, clipboard/console/storage/URL leakage assertions; воспроизводимые шаги в demo.md.
- [ ] 5.3 Свести requirements->tests evidence matrix и self-review whole change: one claim, source/basis binding, no hidden second mutation/store, truthful historical event, manual safety и unchanged CI/auth. Verification: each scenario получает test/proof и claimed boundary; независимый reviewer проверяет final code после разрешённой реализации.
- [ ] 5.4 Получить existing mandatory CI на точном новом implementation head после отдельного разрешения публикации; Flights Unit/Integration, Host HTTP/Integration, architecture, AspireSmoke и PR E2E должны подтвердить durable/runtime claims. Сохранить job/SHA receipts, postmerge policy отдельно; base CI не использовать для new changes. Verification: все обязательные lanes accepted, skipped paid evals не маскируют отсутствие mandatory proof.
- [ ] 5.5 По разрешённому source результату обновить current-state и docs/ADR factual references, затем sync/archive данного change только при отдельном разрешении финализации. Verification: one canonical contract/history, не объявлять real payout или завершение всего M3; при Aspire exit134 фиксировать logs/unchanged rerun отдельно без выдуманной root cause.

## 6. Verification commands and gates

Это команды будущей реализации, а не результат их выполнения в планировании:

```powershell
node tools/openspec/run.mjs validate flights-m3-customer-refunds --strict
npm.cmd run check:ai-harness
node --test tools/demo/flights-search-api.test.mjs tools/demo/flights-customer-refunds.test.mjs
# Shared/Angular: existing project test target, после аудита его config и startup.
# Browser: existing fictional Playwright config; product Host/AppHost не запускается.
npx.cmd playwright test -c tests/travel-e2e/playwright.flights-demo.config.ts
```

Точный backend proof split:
- Pure domain, typed observations, fake-session и redaction source checks — audited temporary
  isolated .NET project, Compile Include только выбранных refund sources/tests, без linked
  FlightPiiTestRing/AssemblyFixture/ModuleInitializer/startup/store registrations.
- Перед любым .NET запуском проверить source imports/static initialization и fixtures.
  У обычного Flights Unit и Host project есть assembly key bootstrap: `--filter` не защита.
- Temporary test project вне solution inventory и repository tracked tooling; существующие
  packages/target framework, последовательные restore/build/test, без source migration.
  Если такая изоляция требует нового repo tooling — остановиться и вынести её на согласование.
- Real DB/Host/Aspire/existing-project assemblies — только существующий CI. Ни новую suite,
  ни test category не использовать для обхода startup/authority.

Planning acceptance: complete proposal/design/spec/tasks, strict validation, self-review и
independent review без unresolved blockers. Product acceptance — all group checkboxes и exact
head CI после отдельного implementation approval. Live/deploy/publication остаются отдельными
gates. Сейчас после планирования остановиться.

## Implementation checkpoint

Source/local criteria above are checked where verified. Remaining boxes retain their full runtime/budget/HTTP/snapshot/review/CI acceptance; source implementation alone does not close them. See verification.md and implementation-review.md. Source publication and OpenAPI regeneration require separate authorization.
