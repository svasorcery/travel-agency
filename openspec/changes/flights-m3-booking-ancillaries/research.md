# Research and baseline

Исследование источников: 2026-10-06/07, Europe/Moscow. Решение об упрощении принято пользователем 2026-10-07. Исследование — чтение source и публичных официальных документов. Supplier API, ключи и dashboard не использовались. Ссылки ниже подтверждают документацию, а не runtime.

## Git и доставка предыдущего среза

- Новый `git fetch origin dev`: `refs/remotes/origin/dev = 31f4a1da3571386044946240e957088e5ea21714`.
- `git merge-base --is-ancestor 31f4a1da3571386044946240e957088e5ea21714 <fetched SHA>`: exit 0; база равна checkpoint.
- Новый managed worktree: `C:\Users\Vladimir_sva\.codex\worktrees\flights-m3-seats-baggage-plan\travel-agency`, detached HEAD на этой базе. Архивы checkout не восстанавливались.
- User handoff подтверждает merged PR36/37/38, PR CI 13 обычных checks, postmerge CI37477274741 success/12 обычных checks. Эти CI counts приняты как предоставленная текущая evidence; новое чтение GitHub runs в этой задаче не выполнялось. Git/source независимо подтверждают merge checkpoint. Postmerge E2E/paid evals skipped по существующей policy.
- Pending delivery фразы в archived cancellation, ADR и current-state — исторические freezes; они не являются новым delivery blocker. Разовый PR37 Aspire exit134 и успешный неизменённый повтор не устанавливают причину.

## Прочитанные правила и архитектура

Root и Flights/Shared/Travel.AI `AGENTS.md`, [current-state](../../../docs/architecture/current-state.md), [roadmap §7](../../../docs/superpowers/specs/2026-05-03-travel-platform-concept.md), ADR [0015](../../../docs/adr/0015-booking-aggregate-event-model.md), [0016](../../../docs/adr/0016-booking-saga-via-marten-es.md), [0017](../../../docs/adr/0017-flights-idempotency-strategy.md), [0023](../../../docs/adr/0023-module-api-facades-and-cross-cutting-ownership.md), [0024](../../../docs/adr/0024-flights-pii-protection.md), [0025](../../../docs/adr/0025-flights-ordered-journeys-and-airport-time.md). ADR0001 дополнительно прочитан для domain-modeling.

Current contract отмены: [main spec](../../specs/flights-whole-order-cancellation/spec.md). Исторический [пилот](../archive/2026-10-06-flights-m3-cancellation/proposal.md), research/review/process-log и partial cost evidence прочитаны только как предыдущие решения и уроки. Новый change не меняет эти файлы и не переиспользует старую implementation authority.

## Наблюдаемое в source на fetched SHA

| Evidence | Факт и следствие для плана |
|---|---|
| `Core/ValueObjects/Segment.cs`; `Infrastructure/Providers/Duffel/Dto/DuffelSegmentDto.cs`, `DuffelSliceDto.cs` | Duffel public JSON имеет segment/slice IDs, но текущие wire DTO их даже не десериализуют; Core Segment также не сохраняет ID. Нужны additive Infrastructure DTO fields и отдельный quote-bound binding полного графа; не изменять historical Segment constructors. |
| `Core/ValueObjects/QuoteBinding.cs`; `Application/Booking/QuoteBindingFactory.cs` | Есть revision и local adult IDs → supplier refs. Допуслуги должны использовать это соответствие и сбрасываться при новой revision. |
| `Infrastructure/Providers/Duffel/DuffelOfferMapper.cs` | Включённый багаж сводится к Max по passengers/segments. Это не норма для каждого перелёта; прежнее поле нельзя использовать для покупки. |
| `Dto/DuffelOfferDto.cs`, `Dto/DuffelOrderDto.cs`, `DuffelFlightBookingProvider.cs` | Нет available_services/seat maps/ordered services. Hold отправляет type/offer/passengers без services и возвращает только order ID/deadline. |
| `Application/Handlers/Booking/HoldOfferHandler.cs` | Supplier effect предшествует первому held commit; coordination marker появляется только после успешного hold. Сохраняемой hold admission/recovery пока нет. |
| `QuoteOfferEndpoint.cs`, `QuoteOfferHandler.cs` | Anonymous quote/re-quote не принимает owner; новый claimed quote требует отдельного signed refresh и отказа старого anonymous re-quote до вызова провайдера. |
| `BookingAggregate.Confirmation.cs`; `ConfirmOrderHandler.cs` | Confirmation уже имеет admission, money claim, receipt и barrier. Нужно проверять также полный набор held services до wallet и после последнего awaited supplier read. |
| `DocumentSessionExtensions.cs` | `SaveBookingWithWorkAsync` атомарно сохраняет events, reconcile, scheduled work и siblings. `AllowMultiples` обязателен; второй saga store не нужен. |
| `OrderReadModelEventApplier.cs`, `OrderReadModelReconciler.cs` | EF eventual; quoted streams не materialized. Pre-hold discovery нельзя строить на EF 404 или старом ownerless quote. |
| `CancellationScope.cs` | Version 1 hash связывает order/owner/party/legs, но не услуги. Для новых service-bearing orders нужен version 2; прежние сохранённые hashes не пересчитываются. |
| `DuffelOptions.cs`, infrastructure registration | Один Duffel pipeline имеет total timeout 10s; POST не retry. Новый order-creation бюджет должен быть capability-specific; увеличивать весь клиент нельзя. |
| `shared/ts/api-client/src/`; Angular booking/order components; `tools/demo/flights-search-api.mjs` | Есть memory-only attempts, строгие readers, owner epochs и fictional proof. Новые услуги требуют своего строгого reader и отдельного состояния; произвольный provider HTML/component не нужен. |

Paths в таблице относительны соответствующим Flights projects, если не указан корень. Точные пути task-owned новых и изменяемых файлов перечислены в tasks.md.

## Официальные Duffel contracts

### C1 — каталог багажа

[Get offer v2](https://duffel.com/docs/api/v2/offers/get-offer-by-id) позволяет запросить services через `return_available_services=true`. Повторное чтение может изменить цену и service IDs. [Extra bags](https://duffel.com/docs/guides/adding-extra-bags) связывает услугу с `passenger_ids`/`segment_ids`, даёт quantity/max и денежные поля. Созданные услуги находятся отдельно от включённых норм; EMD может быть отдельным документом. Это основания хранить обе части, а не складывать сводный Max с заказанным количеством.

### C2 — места

[Seat maps v2](https://duffel.com/docs/api/v2/seat-maps/get-seat-maps): maps запрашиваются по offer, наличие неполное; после создания order выбор/отмена отдельного места не поддержаны. [Adding seats](https://duffel.com/docs/guides/adding-seats): цена/service ID относятся к конкретному пассажиру; выбор направляется как услуга quantity 1, результат проверяется по booked services и seat designator. Физический layout не обязан иметь одинаковые колонки. Наше решение: optional выбор, ограничения/раскрытия показываются до согласия, без обещания соседних мест или гарантии против поздней замены самолёта.

### C3 — создание с услугами: документальное расхождение

[Create order v2](https://duffel.com/docs/api/v2/orders) перечисляет `services_not_allowed_for_order_type` для hold, хотя [точная цена](https://duffel.com/docs/guides/getting-an-accurate-price-before-booking) содержит hold с services, а [официальный changelog](https://changelog.duffel.com/announcements/you-can-now-create-hold-orders-with-services) объявил поддержку 2023-11-13. Date на странице объявления отличается от даты карточки в общем changelog; для этого факта использована сама страница.

**A1, выбранное documentary assumption (2026-10-07):** реализуем hold+services по official guide/changelog. Таблица ошибок, вероятно, устарела; это вывод из источников, не runtime proof. Прежний G1 как блокировка написания supplier writer отменён после аудита и выбора простого пути. Runtime capability и real acceptance по-прежнему отдельно. Unsupported response не запускает instant fallback, drop services или повторный POST; при подтверждённой несовместимости возвращаемся к владельцу с фактами.

### C4 — цена и оплата

[Holding orders](https://duffel.com/docs/guides/holding-orders-and-paying-later) отделяет requires_instant_payment, deadline и price guarantee. Hold сам не устанавливает бессрочную фиксированную стоимость. Исходная сумма из search не разрешает payment другой суммы. Для выбранного balance-only flow наша формула — fare плюс quantity-weighted services; currency и supplier order total проверяются до денег. Card/credits/surcharges/intended-payment pricing исключены. Если offer уже несёт неизвестный `intended_services`/payment intent, новая покупка fail closed; не вызывать скрытый POST pricing и не считать services дважды.

### C5 — после бронирования

[Post booking bags](https://duffel.com/docs/guides/adding-post-booking-bags) описывает отдельные available-services и add-services requests, baggage-only и ограничение на исходный baggage. Справочник Orders дополнительно исключает type=hold; Confirmed/Ticketed не доказывает изменение supplier order type. Наш вывод: post-booking bags требуют отдельного следующего change и eligibility/financial recovery; этот change их не обещает. Post-booking seats не являются доступной альтернативой.

### C6 — неуспех, ожидание и rate limits

[Response handling](https://duffel.com/docs/api/overview/response-handling) отдельно рассматривает order creation: рекомендует timeout минимум 130s; документальные incomplete 200/202 Flights responses описаны для card payments и не являются ожидаемым happy path выбранного hold/balance flow; неожиданный неполный ответ всё равно обрабатывается безопасно; 503 для создания имеет специальный no-booking смысл; `ancillary_service_not_available` — именованный отказ. Общий статус нельзя переносить на payment/cancellation или смешивать с сетевым timeout. Наш более строгий первый flow не повторяет POST даже после документального no-create ответа; он показывает classified результат и требует нового решения. Неполное/потерянное тело остаётся unknown. Supplier throttling отделяется от местного admission; `ratelimit-reset` учитывается только в разрешённых reads.

### C7 — поиск возможного созданного заказа

Orders документирует `offer_id` фильтр и metadata, а Response handling — webhook order.created с offer/order IDs. Это помогает discovery, но не доказывает uniqueness/idempotency. **В текущем срезе supplier list discovery и новый webhook-correlation workflow отложены.** Lost ID требует ручного поиска оператором; приложение проверяет только уже известный/явно предъявленный exact ID, attribution и scope. Existing webhook reader не превращается в recovery engine. Бounded known-ID check не означает автоматическое обнаружение потерянного заказа.

### C8 — offer/order identities и booked price stage

[Официальное объявление IDs](https://changelog.duffel.com/announcements/slices-and-segments-now-have-ids) прямо ограничивает уникальность slice/segment parent resource: одинаковый flight получает другие IDs в offer/order. [Официальный SDK Order types](https://raw.githubusercontent.com/duffelhq/duffel-api-javascript/main/src/booking/Orders/OrdersTypes.ts), ссылка на repo подтверждена [Duffel JS guide](https://duffel.com/docs/guides/javascript-client-library), описывает отдельный booked service ID, quantity-inclusive line total и disclosures как string[]. SDK прочитан по main на 2026-10-06, не установлен и не выдан за API runtime guarantee. Наши правила D2/D3 задают полное однозначное graph remapping, semantic service matching и отказ при недоказанной passenger linkage; available-service refs не являются booked-service identity. Новые official input/model observations перепроверяются при реализации группы 3 без supplier calls.

## Принятые для proposal выводы и границы

### Необязательный запрос для A1 (подготовлен, не отправлен)

Для будущего live acceptance владелец может отдельно разрешить отправку вопроса в Duffel support. Это не prerequisite source/offline реализации:

> We are preparing a v2 integration without making supplier calls. The current Orders reference lists services_not_allowed_for_order_type for hold, while the Accurate Price guide and your 2023 hold-services announcement show hold orders with services. Please confirm whether POST /air/orders with type=hold, selected_offers, passengers and seats/checked-baggage services is supported without an actions/price call or intended_payment_methods, followed by a separate balance payment. Which restrictions apply, and is the validation-error entry outdated? Please provide the current official contract link. We also need to confirm whether a paid order originally created as hold remains excluded from post-booking available_services/add_services.

Ссылки и scope — C3–C5. Никаких ключей, реальных пассажиров, заказов или логов к такому запросу не требуется. Утверждение плана не разрешает отправку сообщения. Непротиворечивое публичное уточнение также полезно для будущего live acceptance; при подтверждённом unsupported hold отдельно согласовать изменение этой модели, не добавлять instant flow автоматически.

Пользователь выбрал простой вариант D1–D8 текущего design: временный каталог; одна итоговая котировка; одна сохраняемая попытка существующего hold; один bounded check только known ID; больше ручного разбора. CreatedWithDifferences даёт отмену целиком после доказанного завершения, без принятия замен. Отдельные catalog/review identities, durable полный каталог, provider-list discovery и большой recovery/новый admin workflow отложены. Это утверждение направления и переработки плана, не product implementation authority. Багаж первым, места вторыми на общей покупке; post-booking additions/обмены/customer payout — последующие задачи.

Общая стоимость этой подготовки в токенах/деньгах не предоставлена инструментами; оценка не выдумывается. Historical partial s03a02 figures не переносятся на эту задачу и не доказывают превосходство OpenSpec.
