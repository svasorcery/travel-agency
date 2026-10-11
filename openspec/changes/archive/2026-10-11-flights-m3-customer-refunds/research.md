# Ревизия M3 и выбор следующего среза

Дата проверки: 2026-10-08. Источник кода — fetched `origin/dev` и `FETCH_HEAD`:
`7aaf3d60e852670dd4a1f2f257d3e26995e5306f`.
Fetch завершён до финальной проверки SHA; checkpoint является ancestor (в данном случае равен базе).
Managed worktree создан именно от этого SHA, HEAD detached, исходный dev был чистым.
Старые архивные worktrees не восстанавливались.

## 1. Доставка предыдущего среза

Read-only GitHub проверка:
[PR39](https://github.com/svasorcery/travel-agency/pull/39) merged как
`492a2932d834e1ab00b3eefb301ff9e89db0aa26`;
[PR40](https://github.com/svasorcery/travel-agency/pull/40) merged как fetched SHA.
[Postmerge CI37655891205](https://github.com/svasorcery/travel-agency/actions/runs/37655891205)
завершён success на том же SHA: 12 обычных jobs success, E2E и paid evals skipped.
Это подтверждает прежнюю доставку, а не новый refund.
Pending passages в frozen archives и старые временные пути не являются текущими blockers.
Сырые blog evidence.zip/manifest не понадобились: исторические счётчики тестов здесь не перепроверяются.

## 2. Что действительно существует

Пути ниже относительно repository root; номера строк относятся к зафиксированной базе.

| Область | Наблюдение | Источник / предел доказательства |
|---|---|---|
| Whole-order cancellation | Согласие на точные supplier terms, durable claims/recovery, owner status, привилегированный review; выплата клиенту явно отдельна | `openspec/specs/flights-whole-order-cancellation/spec.md`, `Core/Aggregates/BookingAggregate.Cancellation*.cs`, `Application/Cancellation/CancellationDecisionWriter.cs:58` |
| Ancillaries | Одна принятая покупка, actual/selected services, создание с claim, unknown/manual и отмена доказанного неоплаченного заказа с отличиями | `openspec/specs/flights-booking-ancillaries/spec.md`, `Core/Aggregates/BookingAggregate.Creation*.cs` |
| Клиентский payment seam | Authorize/Capture/Refund port существует | `modules/flights/Travel.Modules.Flights.Core/Providers/IPaymentGateway.cs:7`; port не доказывает деньги |
| Test wallet | Authorize кэширует GUID только до restart; Capture всегда success; Refund возвращает новый GUID, без HTTP и проверки платежа | `modules/flights/Travel.Modules.Flights.Infrastructure/Payments/DuffelTestWalletPaymentGateway.cs:22`; единственная refund реализация, production исключён |
| Refund вызов в product flow | Production orchestration не вызывает `RefundAsync`: поиск даёт только interface и fake implementation | `rg -n 'RefundAsync\\(' modules/flights`; тесты gateway проверяют только GUID/non-error |
| Capture facts | Новое confirmation сохраняет accepted money, PaymentRef, capture observation, затем отдельный succeeded supplier balance receipt | `Core/DomainEvents/ConfirmationCoordinationEvents.cs:9`, `Core/Cancellation/ConfirmationAttempt.cs:25`, `Application/Handlers/Booking/ConfirmOrderHandler.cs:294`; capture source — `TestWalletObserved`, не real PSP |
| Airline refund history | Обработчик сам создаёт RefundRef и использует TotalAmount; gateway не вызывается, сумма actual refund не читается | `Application/Handlers/Webhooks/DuffelWebhookHandler.cs:187`, `Core/DomainEvents/OrderRefunded.cs:12`; это исторический статус, не payout receipt |
| Webhook parsing | Нормализует ticket numbers либо airline cancellation signal по event type и provider ID; денежных фактов нет | `Infrastructure/Webhooks/DuffelWebhookPayloadReader.cs:34`; актуальность старых event names для real Duffel не доказана |
| Projection | Старый OrderRefunded выставляет Refunded/refundedAt; неизвестный тип события вызывает terminal error | `Infrastructure/Persistence/OrderReadModelEventApplier.cs`; новые события нужно явно поддержать даже без новых EF полей |
| UI | Refunded сейчас подписан «Возврат оформлен»; cancellation UI объясняет разделение supplier/customer | `apps/web/src/app/flights/flight-order-page.component.html:34,135`, `flight-cancellation-review.component.html`; уточнение исторической подписи входит в refund срез |
| SSE backend | Авторизованный owner endpoint, heartbeat, bounded registry, required stream version; Confirmed/Ticketed/Cancelled notifications публикуются | `Api/Endpoints/OrderEventsSseEndpoint.cs:14`, `Application/Handlers/Notifications/PublishOrderSseHandler.cs`, `Infrastructure/Notifications/Sse/OrderSseConnectionRegistry.cs`; best effort для одного процесса |
| SSE ограничения | Refund webhook публикует только reconcile, не refund notification; Angular/shared client не содержат EventSource/streaming fetch/SSE route | Поиск `EventSource|text/event-stream|/events/flights` в UI/client/modules; backend ticket webhook -> notification есть, полного UI journey нет |
| Email | Confirmation/cancellation handlers и templates существуют; readiness ждёт EF version, obsolete messages подавляются | `Application/Handlers/Notifications/SendOrder*EmailHandler.cs`, ADR0016; новый refund email не нужен для этого среза |
| Fare facts | Есть bool ChangeAllowed/RefundAllowed, fare brand/cabin и baggage; quote UI печатает простые условия | `Core/ValueObjects/FareConditions.cs`, `Api/Contracts/Contracts.cs:292`, `apps/web/src/app/flights/flight-quote-panel.component.html:47` |
| Fare summary gap | Нет отдельного summary contract/endpoint, prompt, offline corpus/evals. Null supplier conditions превращаются в false; fee/unknown distinctions теряются | `Infrastructure/Providers/Duffel/DuffelOfferMapper.cs:174`, `Dto/DuffelConditionsDto.cs`; текстовый bool summary не является AI fare-rules capability |
| AI runtime | Прямой Anthropic IChatClient только для NL-search; новый AI summary не обнаружен | `apps/Travel.AI/NlSearch/`, current-state; никакой AI вызов не выполнялся |

Existing `Infrastructure/Diagnostics/BookingConsistencyDiagnostics.cs` связывает inspection
с replay allowlist; новые payment work types нельзя просто добавить в общий AllowedTypes.
В tail current-state ещё есть historical формулировка об ancillaries как будущем M3:
актуальны accepted верхний раздел, main capability spec и merged PR39/40, а не этот tail.

Полный префикс коротких Core/Application/Infrastructure/Api путей:
`modules/flights/Travel.Modules.Flights.<Layer>/`.

## 3. ADR и исторические ограничения

Прочитаны root/Flights/Shared AGENTS, architecture/current-state, concept §7,
ADR0015–0019 и0023–0025, действующие specs и релевантные frozen design/verification passages.
ADR0015/0018 описывают старый airline OrderRefunded; ADR0019 прямо подтверждает fake wallet.
Их историческое намерение не заменяет сумму/исход/назначение новой выплаты.
В ADR0019 есть старая отсылка partial refunds к M3; текущий concept §7.3 относит partial
refunds к Tier3. Следуем более узкой цели: целый заказ, одна выплата суммы его entitlement;
это может быть меньше исходного платежа, но не означает частичную отмену или несколько выплат.
Изменения ADR при реализации должны уточнять эту границу, а не создавать второй feature plan.

## 4. Актуальные внешние документы

Проверены только официальные публичные страницы 2026-10-08; API не вызывались.

- [Duffel Order Cancellations](https://duffel.com/docs/api/order-cancellations):
  refund относится к исходной оплате агентства; возврат клиенту выполняется отдельно.
  Сумма/валюта могут отсутствовать, airline credits не равны cash.
- [Cancelling an Order guide](https://duffel.com/docs/guides/cancelling-an-order):
  quote и подтверждение — отдельные этапы; cancellation identity и terms важны для результата.
- [Duffel customer Refunds](https://duffel.com/docs/api/refunds):
  customer refunds привязаны к payment intent и его валюте/исходному payment method.
  Есть pending/failed/succeeded; succeeded означает отправку к назначению, не доказанное
  банковское зачисление. Страница сообщает, что новые клиенты продукта сейчас не принимаются.
  Текущий Travel PaymentRef — UUID fake wallet, не Duffel payment_intent_id.
  Следовательно, подключать этот API как «готовое продолжение RefundAsync» нельзя.
- [Duffel Webhooks](https://duffel.com/docs/api/webhooks):
  доставка повторяется и не упорядочена; это причина сохранить existing inbox/outbox.
  Общая гарантия не доказывает соответствие нынешних старых event names актуальному contract.

Документация — основание для различения смыслов, а не принятие live integration.
Для выбранного fake пути не нужны PSP wire DTO, токен, supplier read или AI contract.

## 5. Сравнение вариантов и последовательность

| Вариант | Польза | Цена/ограничение | Выбор |
|---|---|---|---|
| Один fictional customer refund с manual исключениями | Закрывает разрыв между доказанной отменой и отдельной клиентской выплатой | Нужны durable claim, независимый status и честный receipt; реальные деньги не подтверждаются | Рекомендуется |
| Только ручной денежный учёт | Самый маленький кодовый срез | Не показывает нормальный исполняемый денежный процесс; весь happy path зависит от оператора | Не основной |
| Сначала завершить SSE UI | Меньше нового бизнес-моделирования; использует уже готовый backend | Не устраняет денежный пробел; transport не делает старый Refunded достоверным | Следующий после refund |
| Реальный PSP/полное settlement recovery | Реальная финансовая capability | Нет выбранного PSP/customer capture contract; новый отдельный проект, вне разрешений | Отложить |

Далее: customer refund -> отдельный SSE owner UI с GET fallback -> fare-rules summary.
Для summary сначала нужно сохранить known/unknown/fees исходных правил и собрать offline
reference corpus; LLM summary никогда не становится authority для денежного entitlement.
Объединение всех трёх в один change не рекомендуется.

## 6. Решения пользователя

2026-10-08 пользователь явно выбрал:
1. Точная supplier сумма без комиссии Travel; currency mismatch, credits или сумма выше
   доказанного captured payment -> manual, без `min`, FX или распределения услуг.
2. Запуск после доказанной отмены из средств агентства, без ожидания supplier settlement.

Это решения для демонстрационного плана, не разрешение реализации или реальных выплат.
Остальных обязательных продуктовых решений для составления плана нет. Выбор real PSP,
его custody/settlement/receipt semantics и production launch относятся к будущей отдельной задаче.

## 7. Проверки и ограничения исследования

Sandbox process setup дважды не запустил PowerShell; команды продолжены через штатный
require_escalated с read-only/scoped-authoring действиями. Основной dev не использовался как
замена fetched base. Native creation завершена успешно, архивы не восстановлены.
Ignored OpenSpec runtime отсутствовал в новом worktree: existing runtime прошёл pinned
integrity check, скопирован в worktree и повторно проверен. Tracked tooling не менялся.

Обычный Flights Unit project включает `tests/fixtures/dotnet/FlightPiiTestRing.cs`;
assembly fixture создаёт сертификат и инициализирует keyring до выбранных тестов.
Поэтому даже фильтр новой pure refund группы в этом assembly не даёт безопасного local proof.
.NET/Host/DB/Aspire suites в исследовании не запускались. Будущая локальная .NET проверка —
только предварительно audited isolated compilation/test harness без этой assembly fixture.
Существующие реальные CI lanes потребуются отдельно на новом разрешённом implementation head.
