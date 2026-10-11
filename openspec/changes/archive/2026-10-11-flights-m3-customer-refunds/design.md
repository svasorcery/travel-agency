# Design

## Context

Мотивация — [proposal](proposal.md); карта фактов и источники — [research](research.md).
В booking stream уже есть cancellation terms/evidence и confirmation capture marker с
`TestWalletObserved`. Есть atomic enrolled outbox, но нет customer payout receipt.
Проект демонстрационный: дизайн должен работать после restart без повторной выплаты и
без предположения, что process-local wallet cache является денежным реестром.
Design обязателен из-за денежных исходов, авторизации review и additive event compatibility.

## Goals / Non-Goals

**Goals:** одна доказанная клиентская выплата после отмены; отдельные сумма, назначение,
provenance и исход; конечная ручная эскалация; совместимость с cancellation, services и history.
Три читаемых факта: отмена, supplier entitlement/settlement, customer refund.

**Non-Goals:** real PSP/Duffel Payments, новые customer captures/платёжные методы,
общий ledger, автоматические повторные выплаты, provider polling/reconciliation,
операторский UI, refund email, SSE UI, fare summary/AI, partial passenger/leg/service
refunds, FX, комиссия Travel, chargebacks, ключи/миграции/deploy.
Ручная выплата вне Travel не отправляется новым endpoint: он только сохраняет её
проверенные доказательства для уже заведённого случая.

## Decisions

### D1. Политика и eligible money basis

Пользователь подтвердил supplier сумму без комиссии и авансирование из средств агентства.
Автоматический путь требует одновременно:

- consistent owner и точного provider order; завершённая creation/confirmation,
  без неизвестного competing effect;
- собственная доказанно успешная consent-bound cancellation с exact accepted terms,
  cash-only/credits-known-empty, supplier destination Balance, согласованное cancellation
  evidence, а не `Cancelled`/`Refunded` label;
- сохранённый `ConfirmationCaptureObserved` с source TestWalletObserved, положительной
  accepted money и тем же PaymentRef, завершённая matching confirmation и supplier balance
  payment evidence; деньги/валюта берутся из persisted facts, не current cache или EF total;
- `0 <= refund <= captured` в одной валюте, точное decimal представление, original fictional
  customer payment method; supported exact amount, без округления или FX;
- для automatic admission и terms, и outcome имеют SupplierApi provenance.
  OperatorVerified cancellation и неподдерживаемые money facts остаются manual.

`CustomerPaymentBasis` — immutable snapshot: source=TestWalletObserved,
booking/owner, confirmation attempt, PaymentRef, accepted captured amount/currency,
capture timestamp и original-fictional-payment destination. Это fictional proof,
никогда real-PSP receipt. Source определяется event type, а не DI class name при чтении.
При restart adapter получает этот сохранённый basis; не вызываются authorize/capture заново.

Клиентская сумма относится ко всему order entitlement. GBP100 captured, GBP80 supplier
entitlement -> одна GBP80 выплата, supplier penalty GBP20 видна как разница без service allocation.
GBP110 entitlement не обрезается до100 — manual. Credits не обналичиваются.
Нулевой cash entitlement при доказанном платеже -> NoRefundDue без gateway.
NoCustomerPayment — отдельная ветка assessment, а не применение paid eligibility к Held:
нужны positively completed unpaid creation, coordination marker, отсутствие confirmation
effect admissions под общим cancellation fence и доказанное завершение отмены с Unpaid /
AwaitingPayment terms. Для legacy Held простого отсутствия confirmation events недостаточно.
Missing capture/неизвестная old writer activity -> manual, не unpaid.

Supplier cancellation confirmation доказывает условия/обязательство поставщика, но не его
settlement. Owner response показывает `supplierSettlement=NotEstablished` и отдельный
customer result. Новый учёт supplier balance transactions сюда не добавляется.

### D2. Один booking stream, одна автоматическая выплата

Не создавать вторую saga/table. Partial `BookingAggregate.CustomerRefund.cs` содержит typed
decisions; events past tense реализуют IDomainEvent. Нормальное состояние order остаётся
Cancelled; customer refund не пишет старый OrderRefunded.

На новой запись успешной cancellation существующий writer атомарно публикует
`AssessCustomerRefund(booking,cancellation)` через тот же outbox.
Это не новый owner POST. GET и historical replay не создают работу.
Assessment загружает свежий stream, проверяет D1 и append'ит:
`CustomerRefundRequested` с frozen basis/policy-v1/source/amount/destination либо
`CustomerRefundRequiredManualReview` / `CustomerRefundWasNotRequired`.
Для каждого booking/payment basis сохраняется один case; другая cancellation/request
identity не допускает вторую выплату. CAS сериализует assessment, claim и review.

Примерный минимальный набор новых events:
CustomerRefundRequested, CustomerRefundDispatchClaimed, CustomerRefundOutcomeRecorded,
CustomerRefundRequiredManualReview, CustomerRefundWasNotRequired,
CustomerRefundReviewRecorded, SupplierCancellationReported и SupplierCancellationReportResolved.
Последний содержит только подтверждённый факт сообщения, не денежный исход.
Финальные имена уточняются при реализации, количество workflow/store не увеличивается.

С admission атомарно сохраняются execute и `CustomerRefundDeadline` на admission+180s.
Claim фиксирует sender identity и единственный permission-to-dispatch; для external step
независимый от browser ct бюджет не более120s и не позже абсолютного deadline.
Claim после deadline запрещён. После claim redelivery не вызывает gateway повторно.
Deadline сохраняет manual case даже при crash до/после вызова и без возвращения клиента.
До claim истёкшая admission становится manual-not-dispatched, без ложного no-refund-due.

### D3. Narrow port и честные исходы

Добавить `ICustomerRefundGateway` в Core с одной `ExecuteAsync`.
Вход: frozen refund identity, typed original payment basis, Money и original destination.
Выход `ErrorOr<CustomerRefundObservation>`, где ожидаемые external outcomes:
Sent (matching affirmative receipt), Pending (matching accepted receipt),
ConfirmedNoEffect (affirmative no-refund/no-pending receipt), Unknown.
Generic error/exception/timeout/missing result/mismatched receipt -> Unknown, не rejection.

Отдельный `FictionalCustomerRefundGateway [TestOnly]` в Infrastructure без HTTP/keys,
со startup-fixed offline сценариями. Никакой автоматической fallback на
`IPaymentGateway.RefundAsync`, который остаётся legacy seam.
DI registration только non-Production плюс TestOnlyGuard/architecture guard.
Production не получает fake и не получает implicit real adapter; capability unavailable
до separately designed provider. Ports не включают PSP wire DTO и card data.

Сохраняется outcome observation с exact identity/basis/money/currency/destination,
receipt reference только серверно и safe provenance `FictionalGateway`.
Запись result после claim не требует новой выплаты. При local commit failure после
внешнего success повторная доставка не исполняет внешний шаг: case становится manual;
known receipt может быть приложен отдельно. Это сознательная граница минимального дизайна.

State у владельца:
- Assessing / Ready / DispatchClaimed — pending локальной работы, не квитанция;
- Pending — provider принял обработку, но payout outcome ещё не terminal;
- Unknown — dispatch/result мог потеряться;
- ManualReviewRequired — нужен разбор; deadline не доказывает no effect;
- Sent — возврат отправлен на исходный способ, с доказанной суммой и provenance;
- ConfirmedNoEffect — проверенная неисполненная попытка, entitlement ещё не выплачен;
- NoRefundDue / NoCustomerPayment — два разных нулевых исхода;
- LegacyUnverified / NotApplicable — нет подходящих фактов/не было отмены.

Pending/Unknown к фиксированному deadline получают manual handling, без provider GET и
без обновления бюджета на restart. Sent не означает bank credited:
`customerCredit=NotEstablished`; в demo подпись «Демо: возврат отправлен».
При реальном PSP будущем смысл его receipt должен пройти новый дизайн.

### D4. Manual review без новой операторской системы

В Flights Api facade добавить отдельную policy/scope `flights:customer-refund-review`.
Existing cancellation-review scope сам по себе не даёт право записать клиентскую выплату.
Внешняя выдача scope и новый operator UI в план не входят.

Owner получает case только в no-store GET. Привилегированные GET/POST review работают
по booking resource; case/refund/resolution identities только в bounded POST body.
POST хранит expected case revision, resolution identity, typed decision/evidence,
actor из JWT и серверное TimeProvider время; exact normalized replay после owner/target
binding возвращает прежний receipt, changed payload под тем же ID конфликтует.

Допустимые решения для frozen known basis:
1. Sent: matching payment/refund identity, точная money/currency/destination и affirmative
   payout receipt. Source OperatorVerified, отдельно от FictionalGateway.
2. ConfirmedNoEffect: affirmative no-effect/no-pending evidence и sender-quiescence proof
   после остановки/изоляции exact old sender. Empty cache/404/deadline не достаточно.
3. Inconclusive: сохраняет manual uncertainty и barrier.

Системный stopped-process флаг — operator attestation, не криптографическое proof.
Ни одно решение не отправляет новую выплату/не включает кнопку «повторить».
Late matching positive result может дополнить still-unknown case. Противоречащие receipts
не затираются; требуют manual conflict. Negative closure без quiescence запрещена.
Во всех состояниях второго automatic payout для этого basis нет.

Неподдерживаемый/historical case без frozen compatible payment basis виден как manual,
но endpoint не принимает произвольную сумму или fabricated basis.
Для него разбор/выплата проходят вне данного automatic protocol; owner message это
объясняет. Автоматический импорт внешних/старых платежей и universal operator adjudication
отложены. При новом eligible case unknown manual resolution работает полностью.

### D5. Truthful airline signal и history

Новый airline cancellation webhook не append'ит OrderRefunded/TotalAmount/GUID receipt.
Без exact verified финансовых фактов он сохраняет `SupplierCancellationReported` как
сообщение с server observation time, provider identity и protected-inbox provenance.
Он не доказывает собственную согласованную cancellation и не запускает customer payout.
Не присваивать время обработки фактической дате отмены.

Report имеет собственные retained identity/revision/owner и exact source binding.
Core guards учитывают unresolved report перед дальнейшими confirmation/cancellation effects;
status/privileged review доступны без cancellation operation и без supplier polling.
Existing cancellation-review расширяется target kind SupplierCancellationReport:
CancellationReviewResult получает optional SupplierCancellationReportTarget независимо от
старого primary Target, поэтому report виден даже при unresolved confirmation/creation.
Owner customer-refund GET показывает metadata-only report state и manual reason без его
server identities; существующие owner controls учитывают этот guard.

Existing ResolveCancellationReviewRequest принимает отдельный bounded typed ReportEvidence
только для нового target; authority — flights:cancellation-review, не customer money scope.
Core manual branch допускает3исхода:
- ConfirmedCancellation: exact provider order и positive cancellation proof; проверенный
  actual CancelledAt хранится отдельно от server ObservedAt. Append only verified supplier
  fact/OrderCancelled и report-resolution audit. Нет invented terms/entitlement, customer
  payment/payout остаются unknown до своего proof и не запускаются автоматически.
- ConfirmedNotCancelled: affirmative not-cancelled/no-pending-cancellation evidence, без
  противоречия уже proven terminal facts; снимается только report guard. Любой новый action
  снова проходит обычные admission/ownership/competing-effects проверки.
- Inconclusive: audit без закрытия report/barrier.

Revision/CAS, typed source OperatorVerified и stable normalized resolution replay сохраняются.
Старые manual target hash bytes не меняются: новые поля участвуют только в new-target branch.
Report resolution никогда не закрывает ConfirmationAttempt/CancellationOperation и не
переводит неизвестные customer capture/refund в no-effect. При possible old worker нужна
отдельная существующая проверка его effects/quiescence; supplier report не доказывает остановку.
Exact повтор signed report/review не создаёт нового эффекта; новый distinct report после
закрытия имеет новую retained identity, а old accepted resolution не закрывает новый report.
Если exact собственная отмена уже доказана, report не отменяет её и не создаёт вторую выплату.
Эта branch живёт в existing stream/review: нет новой saga/store/queue/read loop/operator UI.

Это обязательное локальное исправление ложного денежного факта, не расширение до
полного airline disruptions/SSE. Старые OrderRefunded, enum, JSON, Apply и SQL поля
остаются читаемыми, но UI на detail/list сообщает «Исторический статус возврата;
клиентская выплата не подтверждена». Ни old label, ни TotalAmount не seed'ят новую выплату.

V1/V2/V3, ancillaries actual services и прошлые consent/hash bytes не переписываются.
Unpaid service-difference cancellation не становится captured customer payment.
Service refund не выводится из сумм bags/seats.

### D6. Минимальные изменения по слоям

| Слой | Конкретная поверхность |
|---|---|
| Core | CustomerRefund types/basis/policy/observations, partial aggregate decisions/Apply, новые events, narrow port, guards для supplier signal |
| Application | Refund assessment/execute/deadline/status/review handlers; refund writer поверх SaveBookingWithWorkAsync; один assessment envelope на новую cancellation success; truthful webhook signal; existing CancellationReviewFactory/Result и ResolveCancellationReviewHandler поддерживают отдельный report target |
| Infrastructure | TestOnly fictional adapter; event registration; replay support; OrderReadModelEventApplier признаёт новые events и advances version без новых полей; safe delivery diagnostics |
| Api | CustomerRefundDeliveryPolicy для3work types; отдельная review authorization; GET /api/flights/orders/{aggregateId}/customer-refund и /customer-refund/review, POST /customer-refund/review; bounded16KiB bodies и no-store including errors |
| Shared TS/UI | Typed exact-decimal decoder/client; отдельная owner-bound refund card на existing order page; historical labels detail/list; GET-only bounded polling без SSE |
| Demo/tests | Existing fictional Node API + fixtures/startup presets; no runtime fault-control API; regressions в existing test suites |

Refund work queue владеет только module facade. Safe local storage/CAS retry1/5/30s затем
DLQ; после persisted claim re-entry не вызывает gateway. Не назначать global transport
retry и не включать side-effect message в generic replay для повторной выплаты.
Delivery lag/DLQ — инфраструктурная диагностика, не business failure/успех.
Existing Infrastructure/Diagnostics/BookingConsistencyDiagnostics.cs использует один
AllowedTypes для inspection и replay. Разделить diagnostic correlation от replay authority:
3refund work types видны в read-only inspection; generic replay допускает только local
CustomerRefundDeadline, не assessment/execute. Operator diagnostics содержит server-only
references; owner DTO/logs их не получает. Source ownership/validation gates сохранить.
Классификацию startup/provider exceptions выполнять с redaction без raw message chain.

Authoritative GET replay'ит stream после проверки владельца, не decrypt'ит passengers,
не query'ит supplier и не ждёт EF. Response: bookingVersion, case revision, assessment state,
money decimal string/currency, original-method label, outcome/provenance, observedAt и
manual reason; provider/PaymentRef/refund operation references не входят в owner DTO.
Resource URL содержит только booking aggregate ID.
UI использует owner epoch + stream/case version; ответы прошлого владельца/старой версии
не затирают новое. Ошибка чтения не превращает known Sent в Unknown.
GET poll конечен; reload/read никогда не создают mutation.

### D7. Acceptance и последовательность доказательств

Первым этапом идут Core policy/state tests и serializers; затем persisted admission/CAS/outbox,
adapter/orchestration, API ownership/review, UI/card и fictional demo.
Сначала positive, затем zero/unpaid и unknown/manual paths; полный набор ниже в tasks.

Локально: strict OpenSpec/harness checks в этом планировании; при разрешённой реализации
pure audited .NET source subset в temporary isolated project, без AssemblyFixture,
ModuleInitializer, ключей, stores/Host. Полные Flights Unit tests имеют ключевой assembly fixture
и выполняются существующим CI. .NET на Windows последовательно.
Shared TS/Angular tests, Node fixtures/demo/browser/build отдельно доказывают свои границы.

CI на новом implementation head должен доказать real Marten/Wolverine atomicity, competing
admission/claim, crash/restart, deadline, EF catch-up/rebuild, review auth, actual Host routing
и mandatory PR E2E. Существующий зелёный CI базы не доказывает эти изменения.
Новые tests входят в existing suites/jobs; .github workflows не редактируются.
Не запускать CI dispatch/push/PR без отдельного authorization.
Live PSP/supplier checks вообще не нужны для принятия fictional среза и требуют нового
контракта/разрешения; новый rollout из этого плана не следует.

## Risks / Trade-offs

- Не получили receipt после возможной выплаты -> сохраняем case/barrier, manual; автоматического
  recovery polling нет. Доступность уступает защите от повторной выплаты.
- TestWalletObserved — fictional source -> явная маркировка every receipt и Production exclusion;
  сохранённый event помогает restart, но не доказывает реальные клиентские деньги.
- Авансирование -> business policy принята для demo; ликвидность/real settlement не реализованы.
- Старый Refunded звучит как завершённые деньги -> compatible reads плюс truthful UI notice;
  historical events не переписываются.
- Новые события остановят старые readers -> compatible event registration/applier до writes;
  rollback только на reader-compatible версию.
- Ручной operator может ошибиться -> typed exact facts, CAS/audit/provenance и explicit trust boundary;
  нового механизма независимого банковского подтверждения здесь нет.
- Сложность средняя/повышенная из-за денежных crash/concurrency cases, не из-за UI.
  Оценка для одного исполнителя: порядка5–8 рабочих дней реализации/review/CI при текущем
  контракте, без real PSP/rollout; это диапазон планирования, не срок поставки.
  Узкий port,3work messages и existing stream ограничивают объём; polling/retries/new ledger не нужны.

## Migration Plan

Схема/миграция не предполагаются. После отдельного разрешения: deploy compatible readers
и handlers вместе до включения новых refund-producing writers; stop/drain old writers,
которые создают старые fake refund facts. Historical terminal orders не backfill'ить.
Никакой automatic activation при startup/GET/rebuild.
При rollback остановить новые admissions и завершать известные cases совместимым reader;
не запускать old binary поверх новых событий. Execution/deployment этого порядка отдельно
разрешаются и доказываются, сейчас только план.

## Open Questions

Обязательных продуктовых решений для этого demo не осталось: сумма и funding trigger
подтверждены пользователем. Реальный PSP, его receipt/settlement semantics, partial payouts,
кастодиальные требования и operator UX — отдельные будущие задачи, не скрытые зависимости.
