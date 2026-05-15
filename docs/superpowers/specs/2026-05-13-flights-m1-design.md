# Flights — Subproject 1, Milestone M1 — Design Spec

**Дата:** 2026-05-13
**Статус:** approved, implemented
**Remediation:** see `docs/superpowers/specs/2026-05-14-flights-m1-remediation-design.md` for
ratified deviations from this spec (D1–D8). Key deviations noted inline below.
**North star:** [`docs/superpowers/specs/2026-05-03-travel-platform-concept.md`](../specs/2026-05-03-travel-platform-concept.md)
**Foundation:** [`docs/superpowers/specs/2026-05-04-foundation-design.md`](../specs/2026-05-04-foundation-design.md) (Subproject 0 завершён)
**Скоуп:** первый milestone флагмана — search и бронирование одного пассажира с полным lifecycle BookingAggregate, mixed bookable + deeplink aggregation, NL-search через Travel.AI, авторизованный booking, sandbox-платежи, email/SSE-уведомления, полный observability контур.

---

## 1. Отправная точка

Foundation (Subproject 0) предоставил каркас модуля `modules/flights/` с пустыми `Core/Application/Infrastructure/Api`-проектами и пустой Marten/EF-конфигурацией. M1 наполняет этот каркас production-grade функциональностью одного слайса: купить туда-обратно билет на одного пассажира.

Концепт 7.1 разбивает флагман на milestones M1/M2/M3. Этот спек покрывает только M1 — M2 (multi-pax/multi-leg/saved travelers/explainable ranking) и M3 (ancillaries/user-initiated refunds) получат собственные спеки.

---

## 2. Скоуп M1

### 2.1. Включено

- **Search:** one-way + round-trip, mixed bookable (Duffel) + deeplink (Travelpayouts) aggregation в единой выдаче.
- **Booking lifecycle:** полная цепочка `OfferQuoted → Held → Confirmed → Ticketed → (Refunded | Cancelled)` для single-passenger.
- **Order management:** view (GET), user-initiated cancel (POST cancel).
- **Webhook ingestion:** Duffel webhooks → inbox → outbox → доменные события.
- **Idempotency:** `Idempotency-Key` header на booking-операциях.
- **AI feature #1:** NL-search через Travel.AI (Wolverine request/reply).
- **Identity:** анонимный search, авторизованный booking (Keycloak JWT).
- **Payments:** `IPaymentGateway` + единственная реализация `DuffelTestWalletPaymentGateway` (TestOnly).
- **Notifications:** email на `OrderConfirmation` и `OrderCancellation` (MailKit + Mailpit); SSE на `OrderConfirmed`, `OrderTicketed`, `OrderCancelled`.
- **Observability:** OTel metrics + spans + structured logs, AI cost ledger.
- **Sorting:** default — price asc, tie-break duration asc.

### 2.2. Не входит в M1 (явно)

| Фича | Куда переносится |
|---|---|
| Multi-passenger | M2 |
| Multi-leg / open-jaw | M2 |
| Saved travelers (с PII encryption) | M2 |
| Explainable ranking | M2 |
| Seat selection / bag add-ons | M3 |
| User-initiated refunds + политики | M3 |
| Partial refunds | концепт 7.3 — Tier 3, только ADR |
| Schedule changes от перевозчика | концепт 7.3 — Tier 3 |
| Price-change alerts / watch | концепт 6.4 — будущий агент |

### 2.3. Refunded — особый случай

User-flow для refund'а целиком — M3. Но `Refunded` state машины и event `OrderRefunded` реализуются уже в M1, **триггерясь только webhook'ом от Duffel** на airline-initiated cancellation (когда перевозчик отменяет рейс — Duffel выпускает auto-refund в test wallet, мы консьюмим webhook и аппендим событие). UI-кнопки «Вернуть деньги» в M1 нет, политик возвратов в M1 нет.

---

## 3. Архитектурные решения

| # | Решение | Альтернативы | Обоснование |
|---|---|---|---|
| 1 | **Capability-segregated provider abstraction:** `IFlightSearchProvider` (Duffel + Travelpayouts), `IFlightBookingProvider` (Duffel only) | Один `IFlightProvider` с null-методами; per-provider concrete без интерфейсов | ISP. Travelpayouts честно не умеет booking — `NotSupportedException` врёт компилятору. |
| 2 | **Domain offer как discriminated record:** базовый `Offer` + два наследника `BookableOffer`, `DeeplinkOffer` | Один `Offer` с nullable booking-полями | Type safety: `HoldOfferHandler` принимает только `BookableOffer`, deeplink не пройдёт компиляцию. |
| 3 | **Search fan-out:** parallel `Task.WhenAll` с per-provider timeout (Polly, 4s); partial results allowed | Wolverine routing slip; pub-sub aggregation с reduce | Sync HTTP-запрос, latency-критичный. Один провайдер тормозит — отдаём что есть. |
| 4 | **Booking saga = Marten event-sourced BookingAggregate:** state живёт в stream, Wolverine handlers читают/аппендят события | Wolverine `Saga` с EF state; standalone orchestrator class | Концепт 4.5 фиксирует ES для booking. Event stream **есть** saga state — отдельная state-таблица дублирует. |
| 5 | **Webhook inbox/outbox:** raw payload в `flights.webhook_inbox`, обработчик публикует domain event через Wolverine outbox | Прямой апдейт агрегата из webhook handler | At-least-once → exactly-once семантика. Аудит + replay. |
| 6 | **Idempotency:** клиент шлёт `Idempotency-Key`, server хранит в `flights.idempotency_keys` (TTL 24h) | Server-generated nonce; без idempotency | Стандарт Stripe/Duffel. Защита от ретраев FE. |
| 7 | **Search cache:** Redis 5 мин TTL, ключ `flights:search:{hash(criteria)}`, **нормализованные** `Offer`-объекты | Без кэша; persist в EF | Свежесть критична; нормализованное хранение — кэш переживёт смену провайдера. |
| 8 | **Round-trip как один `Itinerary` с двумя `Slice`** (outbound/inbound) | Два отдельных Order | Совпадает с Duffel-моделью; один Order = одна транзакция. |
| 9 | **NL-search в Travel.AI без MAF:** `IChatClient` (Microsoft.Extensions.AI) + Anthropic SDK + structured outputs | NL-search в Travel.Host; NL-search на MAF | Концепт 6.2: NL-search — tool call (single-shot), не агент. MAF подключим в подпроекте 4. Cross-service вызов = соответствие концепту «AI вынесен в отдельный сервис». |
| 10 | **Default ranking M1: price asc, tie-break duration asc** | Hardcoded weights; explainable scoring | Explainable — M2. M1 не должен делать выбор, который M2 переопределит. |
| 11 | **Currency conversion через Frankfurter** (free side-car из концепта 3.1) | Без конвертации (raw валюта Duffel); платный курс | RU-фокус → RUB по умолчанию; Frankfurter покрывает все нужные пары, no auth, дневной курс кэшим в Redis на 24h. |

---

## 4. Domain model

### 4.1. State machine BookingAggregate

```
        ┌─────────────────────────────────────────┐
        ▼                                         │
   [OfferQuoted] ──hold──► [Held] ──confirm──► [Confirmed]
        │                    │                    │
        │                    └─cancel──┐          │
        └─re-quote (re-enter)          │          │ webhook
                                       ▼          ▼
                                   [Cancelled]  [Ticketed]
                                                    │
                                                    │ airline cancel webhook
                                                    ▼
                                                [Refunded]
```

**Терминальные состояния:** `Cancelled`, `Refunded` — после них события не принимаются.

**Из Ticketed** можно перейти в:
- `Cancelled` — пользователь жмёт cancel **до** ticketing'а (защищено в `CancelOrderHandler` бизнес-инвариантом).
- `Refunded` — только через webhook (M1) или user-initiated flow (M3).

### 4.2. Domain events

Все события implement `IDomainEvent` (из `Travel.Shared.Abstractions`), хранятся в Marten stream `BookingAggregate-{id}`.

| Event | Поля | Триггер |
|---|---|---|
| `OfferQuoted` | `OfferId, Itinerary, TotalAmount, ExpiresAt, ProviderRef, QuotedAt` | `QuoteOfferCommand` → re-fetch offer у Duffel |
| `OfferHeld` | `OrderId (provider), PassengerInfo, HeldUntil, HeldAt` | `HoldOfferCommand` после успешного hold у Duffel |
| `PaymentAuthorized` | `PaymentRef, Amount, AuthorizedAt` | `IPaymentGateway.Authorize` ok |
| `OrderConfirmed` | `OrderId, ConfirmedAt, PaymentRef` | `ConfirmOrderCommand` после `OfferHeld` + успешный capture |
| `OrderTicketed` | `TicketNumbers[], TicketedAt` | Duffel webhook `order.created.documents_issued` |
| `OrderCancelled` | `CancelledAt, Reason (User/Airline/System)` | `CancelOrderCommand` или Duffel webhook |
| `OrderRefunded` | `RefundRef, RefundedAmount, RefundedAt, InitiatedBy (Airline only в M1)` | Duffel webhook `order.airline_initiated_change.cancelled` |
| `OfferReQuoted` | `OfferId, OldAmount, NewAmount, ReQuotedAt` | Повторный quote после expiry/price change |

**Naming convention:** past tense, по правилам root CLAUDE.md.

### 4.3. Value objects

В `Core/ValueObjects/` (records, validation в `static Result<T> Create(...)`):

```csharp
record IataCode(string Value)                     // 3 буквы, A-Z
record Money(decimal Amount, CurrencyCode Currency)
record CurrencyCode(string Value)                 // ISO 4217
// DateRange was removed (remediation D4): departure + return dates are plain DateOnly fields
// in SearchRequest/SearchCriteria; a dedicated value object added no type safety for M1.
record CabinClass(CabinClassEnum Value)           // Economy/PremiumEconomy/Business/First
record PassengerInfo(string GivenName, string FamilyName, DateOnly DateOfBirth, Gender Gender, string Email, PhoneNumber Phone)
record Slice(IataCode Origin, IataCode Destination, DateTimeOffset DepartAt, DateTimeOffset ArriveAt, Segment[] Segments, Duration Duration)
record Segment(IataCode Origin, IataCode Destination, DateTimeOffset DepartAt, DateTimeOffset ArriveAt, string CarrierCode, string FlightNumber, CabinClass Cabin)
record Itinerary(Slice[] Slices, Duration TotalDuration)
record SearchCriteria(IataCode Origin, IataCode Destination, DateOnly DepartureDate, DateOnly? ReturnDate, int PassengerCount, CabinClass CabinClass, CurrencyCode Currency)
```

### 4.4. Offer discriminated hierarchy

```csharp
abstract record Offer(OfferId Id, Itinerary Itinerary, Money TotalAmount, ProviderId Provider, DateTimeOffset FetchedAt);

record BookableOffer(
    OfferId Id,
    Itinerary Itinerary,
    Money TotalAmount,
    ProviderId Provider,
    DateTimeOffset FetchedAt,
    DateTimeOffset ExpiresAt,
    FareConditions FareConditions,
    string ProviderOfferRef        // Duffel offer.id
) : Offer(Id, Itinerary, TotalAmount, Provider, FetchedAt);

record DeeplinkOffer(
    OfferId Id,
    Itinerary Itinerary,
    Money TotalAmount,
    ProviderId Provider,
    DateTimeOffset FetchedAt,
    Uri DeeplinkUrl,                // партнёрская ссылка с marker
    string PartnerName              // "Aviasales" / etc.
) : Offer(Id, Itinerary, TotalAmount, Provider, FetchedAt);
```

`HoldOfferHandler`, `ConfirmOrderHandler` принимают **только** `BookableOffer` — компилятор отбивает попытку забронировать deeplink.

---

## 5. Provider abstraction

### 5.1. Контракты

```csharp
// modules/flights/Travel.Modules.Flights.Core/Providers/

public interface IFlightSearchProvider
{
    ProviderId Id { get; }
    Task<ErrorOr<IReadOnlyList<Offer>>> SearchAsync(SearchCriteria criteria, CancellationToken ct);
}

public interface IFlightBookingProvider
{
    ProviderId Id { get; }
    Task<ErrorOr<BookableOffer>> RefreshOfferAsync(string providerOfferRef, CancellationToken ct);
    Task<ErrorOr<HeldOrder>> HoldOfferAsync(BookableOffer offer, PassengerInfo passenger, CancellationToken ct);
    Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(string providerOrderId, PaymentRef payment, CancellationToken ct);
    Task<ErrorOr<Unit>> CancelOrderAsync(string providerOrderId, CancellationToken ct);
    Task<ErrorOr<OrderStatus>> GetOrderStatusAsync(string providerOrderId, CancellationToken ct);
}

public interface IPaymentGateway
{
    Task<ErrorOr<PaymentRef>> AuthorizeAsync(Money amount, string idempotencyKey, CancellationToken ct);
    Task<ErrorOr<Unit>> CaptureAsync(PaymentRef payment, CancellationToken ct);
    Task<ErrorOr<RefundRef>> RefundAsync(PaymentRef payment, Money amount, CancellationToken ct);
}
```

### 5.2. Реализации

| Интерфейс | Реализация | Где |
|---|---|---|
| `IFlightSearchProvider` | `DuffelFlightSearchProvider` | `Infrastructure/Providers/Duffel/` |
| `IFlightSearchProvider` | `TravelpayoutsSearchProvider` | `Infrastructure/Providers/Travelpayouts/` |
| `IFlightBookingProvider` | `DuffelFlightBookingProvider` | `Infrastructure/Providers/Duffel/` |
| `IPaymentGateway` | `DuffelTestWalletPaymentGateway` (`[TestOnly]` attribute) | `Infrastructure/Payments/` |

DI-регистрация в `FlightsModuleStartup.cs`:

```csharp
services.AddSingleton<DuffelClient>(...);
services.AddScoped<IFlightSearchProvider, DuffelFlightSearchProvider>();
services.AddScoped<IFlightSearchProvider, TravelpayoutsSearchProvider>();   // multi-registration
services.AddScoped<IFlightBookingProvider, DuffelFlightBookingProvider>();
services.AddScoped<IPaymentGateway, DuffelTestWalletPaymentGateway>();
```

`SearchFlightsHandler` инжектит `IEnumerable<IFlightSearchProvider>` — fan-out по всем.

### 5.3. ACL / mapping

Каждый провайдер имеет свой mapper:

- `DuffelOfferMapper`: Duffel `Offer` JSON DTO → `BookableOffer`. Маппит slices/segments, fare basis в `FareConditions`, baggage allowance (только summary в M1, детали — M3).
- `TravelpayoutsOfferMapper`: Aviasales `prices_for_dates` response → `DeeplinkOffer`. Партнёрский marker подставляется в URL через `TravelpayoutsDeeplinkBuilder`.

Provider DTO **никогда** не покидают `Infrastructure` (правило root CLAUDE.md).

### 5.4. Версии API (pinned)

- **Duffel:** header `Duffel-Version: v2`.
- **Travelpayouts:** Aviasales Data API v3 (`prices_for_dates` endpoint).

Зафиксировано в `appsettings.json` + ADR `0013`.

---

## 6. Search pipeline

### 6.1. Flow

```
1. POST /api/flights/search  { criteria }
   │
   ├─► Cache check (Redis, key=hash(criteria), TTL=5min)
   │   └─► HIT → return cached
   │
   ├─► Fan-out (Task.WhenAll, per-provider Polly timeout 4s):
   │     ├─► DuffelFlightSearchProvider.SearchAsync
   │     └─► TravelpayoutsSearchProvider.SearchAsync
   │
   ├─► Aggregate:
   │     ├─► Dedup by (carrier+flight_number+departure_time)
   │     ├─► Sort (M1: price asc, tie-break duration asc)
   │     └─► Truncate to top 200
   │
   ├─► Cache write (Redis)
   │
   └─► Return SearchResponse { offers[], partial_failure[] }
```

### 6.2. Partial failure handling

Если один провайдер timed out / errored — возвращаем то, что есть, + `partial_failure[]` с метаданными:

```json
{
  "offers": [ ... ],
  "partial_failure": [
    { "provider": "Travelpayouts", "error_code": "Timeout", "elapsed_ms": 4000 }
  ]
}
```

FE может показать тонкий уведомитель «не все источники ответили». Если **оба** упали — `ErrorOr` с `Errors.Flights.ProviderUnavailable` (500-эквивалент).

### 6.3. Dedup

Ключ дедупа: `(primary_carrier_code, primary_flight_number, departure_date_utc)`. При дублях оставляем offer с **меньшей ценой**. Если цены равны — bookable бьёт deeplink (UX: пусть пользователь сразу видит «можно забронировать здесь»).

### 6.4. Deeplink cache

Travelpayouts вызовы дополнительно персистятся в `flights.deeplink_offers_cache` (EF) — это не replace Redis, а **аудит**:
- Зачем: для дальнейшей аналитики (CTR по партнёрам, какие маршруты популярны).
- TTL: 1 час. Ежечасный Wolverine scheduled handler `PurgeExpiredDeeplinkOffersHandler` чистит.

---

## 7. Booking saga

### 7.1. Sequence (happy path)

```
User                FE              Travel.Host                    Duffel
 │ click "Book"      │                  │                              │
 │──────────────────►│ POST .../quote   │                              │
 │                   │─────────────────►│ DuffelBookingProvider        │
 │                   │                  │   .RefreshOfferAsync ────────►│
 │                   │                  │◄────────── refreshed offer ──│
 │                   │                  │ append OfferQuoted           │
 │                   │◄──── 200 quote ──│                              │
 │ fills passenger   │                  │                              │
 │──────────────────►│ POST .../hold    │                              │
 │                   │   {pax, idem-key}│                              │
 │                   │─────────────────►│ append OfferHeld             │
 │                   │                  │   .HoldOfferAsync ──────────►│
 │                   │                  │◄────────── held order ──────│
 │                   │◄──── 201 held ───│                              │
 │ enters payment    │                  │                              │
 │──────────────────►│ POST .../confirm │                              │
 │                   │   {idem-key}     │                              │
 │                   │─────────────────►│ IPaymentGateway.Authorize    │
 │                   │                  │ append PaymentAuthorized     │
 │                   │                  │ IPaymentGateway.Capture      │
 │                   │                  │   .ConfirmOrderAsync ───────►│
 │                   │                  │◄────────── confirmed ───────│
 │                   │                  │ append OrderConfirmed        │
 │                   │                  │ publish SendEmail + SSE ─────┼─► subscriber
 │                   │◄──── 200 ok ─────│                              │
 │                   │                  │                              │
 │                   │ SSE: OrderConfirmed                             │
 │                   │                  │                              │
 │                   │                  │◄────── webhook ticketed ────│
 │                   │                  │ inbox → outbox → handler     │
 │                   │                  │ append OrderTicketed         │
 │                   │                  │ publish SSE ─────────────────┼─► subscriber
 │                   │ SSE: OrderTicketed                              │
```

### 7.2. Compensation / failure paths

| Сценарий | Compensation |
|---|---|
| `RefreshOfferAsync` → `OfferExpired` | Return `ErrorOr<Errors.Flights.OfferExpired>` с новой выдачей по тем же критериям. Aggregate **не создаётся** (stream пуст). |
| `HoldOfferAsync` fails after `OfferQuoted` | Stream остаётся в `OfferQuoted` — пользователь может re-quote или закрыть. TTL агрегата = `ExpiresAt + 1h`, после чего purge job его прибирает. |
| `Authorize` fails | Stream в `OfferHeld`, hold у Duffel истечёт сам. Возвращаем `Errors.Flights.PaymentFailed`. |
| `Capture` fails после `Authorize` ok | Append `PaymentAuthorized`, затем `OrderCancelled (Reason=System)`. Reversal authorization через `IPaymentGateway.RefundAsync` (на authorize-only это void, Duffel test wallet это поддерживает). |
| `ConfirmOrderAsync` fails после `Capture` | Critical. Append `OrderCancelled (Reason=System)`, инициируем refund, OTel alert. |
| Duffel webhook задержан > 1h | OTel alert `flights.webhook.delayed`. Поллим Duffel `GetOrderStatusAsync` каждые 10 мин до 24h (Wolverine recurring job per aggregate). |

### 7.3. Idempotency

`Idempotency-Key` header **обязателен** на `POST .../hold` и `POST .../confirm`. Сервер:

1. Хэширует `(method, route, user_id, body)`.
2. Смотрит в `flights.idempotency_keys`:
   - Нет записи → выполняет операцию, пишет результат + response_hash.
   - Запись есть, body hash совпадает → возвращает закэшированный ответ.
   - Запись есть, body hash не совпадает → `Errors.Flights.IdempotencyConflict` (409).
3. TTL записи = 24h.

`POST .../quote` идемпотентен по природе (no side effect на агрегате если стрим уже в `OfferQuoted` — просто append `OfferReQuoted`).

`POST .../cancel` идемпотентен по агрегату — повторный вызов на `Cancelled`-stream вернёт current state без ошибки.

---

## 8. Payments

### 8.1. Контракт

См. §5.1. Trio: `Authorize / Capture / Refund`.

Money flows:
- `Authorize` — резервирует средства, возвращает `PaymentRef`.
- `Capture` — списывает зарезервированное.
- `Refund` — возвращает (полный, M1; partial — M3).

### 8.2. Реализация — DuffelTestWalletPaymentGateway

```csharp
[TestOnly]
public sealed class DuffelTestWalletPaymentGateway : IPaymentGateway
{
    // Duffel test wallet API:
    // POST /payments/payments  { type: "balance", amount, currency }
    // Always succeeds in sandbox, no real card.
}
```

`[TestOnly]` — новый attribute, **вводится в M1** в `Travel.Shared.Abstractions`. ArchUnit-тест запрещает регистрацию `[TestOnly]`-классов в production DI-конфигурации (production-config обнаруживается через переменную окружения `ASPNETCORE_ENVIRONMENT != Development` в тестовом ассерте). Документируется в ADR `0019`.

### 8.3. Расширяемость

Когда (или если) появится реальный платёжный интегратор — `StripePaymentGateway : IPaymentGateway` добавляется без изменений в booking saga. DI-выбор реализации — через `IConfiguration["Flights:PaymentGateway"]`.

---

## 9. NL-search cross-service

### 9.1. Flow

```
FE                  Travel.Host                              Travel.AI                Anthropic
 │ POST .../search/nl                                          │                          │
 │  { query: "из Москвы в Питер на выхах" }                    │                          │
 │──────────────────►│                                         │                          │
 │                   │ Wolverine.RequestAsync<NlSearchParsed>  │                          │
 │                   │ ── NlSearchRequested(query, corrId) ───►│                          │
 │                   │                                         │ IChatClient.GetResponseAsync │
 │                   │                                         │  (structured output:     │
 │                   │                                         │   SearchCriteria)        │
 │                   │                                         │────────────────────────►│
 │                   │                                         │◄────────────────────────│
 │                   │                                         │ write ai.cost_ledger     │
 │                   │◄── NlSearchParsed(criteria, corrId) ────│                          │
 │                   │ run standard search pipeline (§6)       │                          │
 │◄──── 200 with same SearchResponse shape ──┘                 │                          │
```

### 9.2. Wolverine request/reply

NATS subject: `travel.ai.nl_search`. Timeout: 6s (Anthropic typical latency 1-3s + buffer). Fallback при timeout: `Errors.Flights.NlSearchUnparseable` + suggestion «введите фразу проще».

### 9.3. MAF — не используется в M1

Подчёркнуто явно: NL-search — single-shot tool call, не агент. Travel.AI в M1 хостит **один контроллер** для NL-парсинга, без MAF runtime. MAF подключим в подпроекте 4 (Trip Planning) — там, где multi-step orchestration.

### 9.4. Cost ledger

Каждый вызов Anthropic пишется в `ai.cost_ledger` (EF, Travel.AI). Таблица **создаётся в M1** (схема `ai` зафиксирована в концепте 4.5, но материализация — задача первой AI-фичи):

```
ai.cost_ledger (
  id, feature, model, input_tokens, output_tokens, cost_usd,
  user_id, correlation_id, occurred_at
)
```

M1 feature: `flights.nl_search`. Будущие features (`flights.explainable_ranking`, etc.) — другие значения. Миграция: `Travel.AI/Infrastructure/Persistence/Migrations/20260513_CostLedgerInit.cs`.

---

## 10. Identity & authorization

### 10.1. Scope в M1

- **Анонимно:** `POST /api/flights/search`, `POST /api/flights/search/nl`, `POST /api/flights/orders/quote` (read-like — refresh offer без side effect на user-aggregate).
- **Авторизация (Keycloak JWT bearer):** `POST .../hold`, `POST .../confirm`, `POST .../cancel`, `GET /api/flights/orders/{id}`, `GET /api/flights/orders` (list), `GET /events/flights/orders/{id}` (SSE).

### 10.2. Authorization

- Keycloak realm `travel`, client `travel-host` (из Foundation).
- Scope `flights:book` (новый, добавляется в Foundation realm-config через PR).
- Role mapping: дефолтный пользователь получает `flights:book` автоматически после первого логина (registration hook в Keycloak).

### 10.3. PII

В M1 единственное хранилище PII — `flights.order_read_model.passenger_info_json` (один пассажир на ордер). Field-level encryption переезжает в M2 вместе с saved travelers (шифрованное хранилище через Data Protection API или KMS — решение в M2-спеке). В M1 PII хранится **plaintext** в JSON-колонке. README получает явный disclaimer о sandbox-статусе хранилища; ADR `0015` фиксирует решение «PII plaintext до M2».

---

## 11. Webhooks (Duffel)

### 11.1. Endpoint

`POST /webhooks/duffel` (анонимный, no JWT — Duffel дёргает извне). HMAC-signature verify через `Duffel-Signature` header (sha256 над raw body с shared secret из appsettings).

### 11.2. Какие webhooks в M1

| Event | Что делаем |
|---|---|
| `order.created` (с `documents` issued) | Append `OrderTicketed` |
| `order.airline_initiated_change` | M1: только лог + OTel metric, без действий |
| `order.airline_initiated_change.cancelled` | Append `OrderRefunded` |
| Прочие (`offer_request.created`, etc.) | Ignore, write to inbox для аудита |

### 11.3. Inbox/Outbox flow

```
1. Webhook POST → endpoint
2. Verify HMAC. Если invalid → 401, no write.
3. INSERT into flights.webhook_inbox (raw payload, signature, received_at).
   Если запись с тем же Duffel `event.id` уже есть → 200 ok (dedupe).
4. Publish DuffelWebhookReceived(inbox_id) через Wolverine outbox.
5. Return 200 to Duffel ASAP (< 2s).
6. Outbox dispatcher → DuffelWebhookHandler:
   - Загружает inbox запись
   - По event.type определяет домен event
   - Загружает BookingAggregate stream
   - Append domain event
   - Mark inbox.processed_at
   - Publish downstream (email, SSE)
```

Идемпотентность гарантирована unique constraint на `(source, event_id)` в inbox.

---

## 12. Notifications

### 12.1. Email

| Шаблон | Триггер | Содержимое |
|---|---|---|
| `OrderConfirmation` | `OrderConfirmed` event | Itinerary, total, PNR/booking ref, "ticket follows" disclaimer |
| `OrderCancellation` | `OrderCancelled` event | Reason, refund expectation |

Реализация: `SendOrderConfirmationEmailHandler` (Wolverine handler на domain event), MailKit, Mailpit на dev (Foundation).

Templates: ~~Razor templates~~ **HTML token-replacement** (remediation D6, ADR 0021) via
`HtmlTemplateEmailRenderer`. RazorLight 2.3.1 is incompatible with .NET 10; static templates
with `{{Token}}` placeholders and `WebUtility.HtmlEncode` of every substituted value are used
instead. RU + EN, выбор по `User.Locale` из Keycloak claim.

### 12.2. SSE

Endpoint: `GET /events/flights/orders/{id}` (SSE stream, JWT-protected).

Events streamed:
- `OrderConfirmed` (immediate after confirm)
- `OrderTicketed` (after Duffel webhook → domain event)
- `OrderCancelled` (after cancel command или airline webhook)

Pattern: `OrderEventsSseEndpoint` подписывается на Wolverine handler `PublishOrderSseHandler`, который держит реестр live-connections per `order_id`. Backpressure: bounded channel, slow client → disconnect after 1MB buffer.

Heartbeat: `:` comment каждые 15s (стандарт SSE).

---

## 13. Observability

### 13.1. Metrics (OpenTelemetry)

| Metric | Type | Tags |
|---|---|---|
| `flights.search.duration_ms` | histogram | provider, status (ok/error/timeout) |
| `flights.search.partial_fill_rate` | gauge | — (rolling 5min: % searches with at least 1 partial_failure) |
| `flights.offer_to_book_conversion` | gauge | provider |
| `flights.payment.duration_ms` | histogram | outcome |
| `flights.payment.success_rate` | gauge | — |
| `flights.webhook.processing_lag_ms` | histogram | event_type (received_at → processed_at) |
| `flights.webhook.received_total` | counter | event_type |
| `flights.nl_search.duration_ms` | histogram | — |
| `flights.nl_search.tokens_used` | counter | direction (input/output) |
| `flights.nl_search.cost_usd` | counter | — |
| `flights.aggregate.events_appended_total` | counter | event_type |

### 13.2. Spans

- HTTP-call per provider (с `http.url`, `http.status_code`, `provider.id`).
- Saga state transition (`booking.event.{event_name}`, `aggregate.id`, `aggregate.version`).
- Anthropic call (`gen_ai.system=anthropic`, `gen_ai.request.model`, `gen_ai.usage.*`).

### 13.3. Structured logs

Каждый запрос имеет `correlation_id` (W3C traceparent). Booking-операции добавляют `user_id`, `order_id`, `aggregate_version`, `idempotency_key`. Provider-calls добавляют `provider.id`, `supplier_offer_id`/`supplier_order_id`.

Log level: Information для happy path, Warning для retried, Error для unrecoverable.

### 13.4. AI cost ledger

См. §9.4. Aspire dashboard + Grafana panel (Foundation OTel pipeline) — отдельный AI cost dashboard будет добавлен в M1.

---

## 14. Error catalog

В `Travel.Modules.Flights.Core/Errors/FlightsErrors.cs`:

```csharp
public static class FlightsErrors
{
    public static Error OfferExpired => Error.Validation("Flights.OfferExpired", "Offer has expired, please refresh.");
    public static Error OfferNotFound => Error.NotFound("Flights.OfferNotFound", "Offer not found.");
    public static Error PriceChanged(Money old, Money @new) => Error.Conflict("Flights.PriceChanged", $"Price changed from {old} to {new}.");
    public static Error ProviderUnavailable(string provider) => Error.Failure("Flights.ProviderUnavailable", $"Provider {provider} unavailable.");
    public static Error ProviderRateLimited(string provider) => Error.Failure("Flights.ProviderRateLimited", $"Provider {provider} rate limited.");
    public static Error PaymentFailed(string reason) => Error.Failure("Flights.PaymentFailed", reason);
    public static Error PassengerInvalid(string detail) => Error.Validation("Flights.PassengerInvalid", detail);
    public static Error OrderNotCancellable(string reason) => Error.Conflict("Flights.OrderNotCancellable", reason);
    public static Error IdempotencyConflict => Error.Conflict("Flights.IdempotencyConflict", "Idempotency key reused with different payload.");
    public static Error NlSearchUnparseable => Error.Validation("Flights.NlSearchUnparseable", "Could not parse the query.");
}
```

Маппинг на ProblemDetails — через `Travel.Shared.Web.ErrorOrExtensions.ToProblemDetails(List<Error>)` (Foundation).

---

## 15. API contracts

### 15.1. Endpoints (WolverineFx.Http)

| Method | Route | Auth | Body | Response |
|---|---|---|---|---|
| POST | `/api/flights/search` | anon | `SearchRequest` | `SearchResponse` |
| POST | `/api/flights/search/nl` | anon | `NlSearchRequest` | `SearchResponse` |
| POST | `/api/flights/orders/quote` | anon | `QuoteOfferRequest` | `QuotedOfferResponse` |
| POST | `/api/flights/orders/hold` | JWT | `HoldOfferRequest` | `HeldOrderResponse` |
| POST | `/api/flights/orders/confirm` | JWT | `ConfirmOrderRequest` | `ConfirmedOrderResponse` |
| POST | `/api/flights/orders/{id}/cancel` | JWT | `CancelOrderRequest` | `OrderResponse` |
| GET | `/api/flights/orders/{id}` | JWT | — | `OrderResponse` |
| GET | `/api/flights/orders` | JWT | query: `?limit=50&offset=0` | `OrderListResponse` |
| GET | `/events/flights/orders/{id}` | JWT | — | SSE stream |
| POST | `/webhooks/duffel` | HMAC | Duffel payload | 200 |

### 15.2. Forward-compat M2

`HoldOfferRequest.passengers: PassengerInfo[]` (массив, не одиночный) — M1 валидирует `length == 1`, M2 расширит без breaking change. Аналогично `passenger_count` в SearchCriteria.

### 15.3. OpenAPI + heyAPI

OpenAPI генерится автоматически через WolverineFx.Http source generator. heyAPI generates TS client в `shared/ts/api-client/` (CI step из Foundation). Spectral OpenAPI-diff в CI ловит breaking changes.

---

## 16. EF migrations (M1)

Новые таблицы в схеме `flights`:

| Таблица | Назначение |
|---|---|
| `flights.idempotency_keys` | `(key PK, user_id, route, body_hash, response_hash, response_status, created_at, expires_at)` |
| `flights.webhook_inbox` | `(id PK, source, event_id UNIQUE, event_type, raw_payload jsonb, signature, received_at, processed_at NULL)` |
| `flights.deeplink_offers_cache` | `(id PK, criteria_hash, offers jsonb, fetched_at, expires_at)` |
| `flights.order_read_model` | `(id PK, aggregate_id UNIQUE, user_id, status, total_amount, currency, itinerary jsonb, passenger_info_json, ticket_numbers text[], booked_at, ticketed_at NULL, cancelled_at NULL, refunded_at NULL)` |
| `flights.outbox` | управляется Wolverine, имя как из Wolverine.PostgreSQL |

Marten автоматически создаёт `mt_streams` / `mt_events` под BookingAggregate (Foundation Marten config).

Файл миграции: `Infrastructure/Persistence/Migrations/20260513_FlightsM1Init.cs`.

---

## 17. Testing strategy

Foundation 7-layer pipeline применяется без изменений. Per-layer фокус для M1:

| Слой | Что покрываем |
|---|---|
| **Unit** (xUnit v3) | Value object validation; ranking algorithm (price asc + duration tie-break); `DuffelOfferMapper`, `TravelpayoutsOfferMapper`; idempotency-key hash logic; saga state-transition guards (нельзя `Hold` из `Cancelled`, etc.); HMAC verifier |
| **Integration** (Testcontainers: Postgres + Redis + NATS) | Полный Duffel sandbox flow (с WireMock-stub'ом для детерминизма CI); webhook ingestion + outbox + projection; saga happy path и все compensation paths; idempotency replay; cache hit/miss; partial fan-out failure |
| **Architecture** (ArchUnitNET) | Module boundary: no cross-module imports; Marten используется только в Flights; `[TestOnly]` payment gateway не разрешён в production runtime; нет `DateTime.UtcNow` в Core/Application/Infrastructure; provider DTO не покидают Infrastructure |
| **Contract** (Pact .NET) | `Travel.Host` (consumer) ↔ `Travel.AI` (provider) для `NlSearchRequested` / `NlSearchParsed` pair |
| **AI-evals** (Foundation framework) | 15 фраз NL-search → expected `SearchCriteria`. RU+EN, дат-инференция, ambiguity-кейсы. Success criteria: exact match on origin/destination IATA + departure-date within ±1 day window (tolerance для «на выходных») |
| **E2E** (Playwright) | Полный путь: search → выбор bookable offer → passenger form → quote → hold → pay → confirm → wait for ticketed (через webhook simulator). Отдельный happy path для deeplink: search → клик «Купить у партнёра» → assert новая вкладка с правильным URL |
| **Visual regression** (Playwright `toHaveScreenshot()`) | Search results screen (mixed list), offer details modal, passenger form, booking confirmation page. Masked: prices (динамика), times (текущая дата) |

### 17.1. Тестовые ID/маршруты

- Search: `LED → DME 2026-07-15` (стабильный sandbox-маршрут Duffel + Travelpayouts).
- Round-trip: `LED → DME 2026-07-15`, return `2026-07-22`.

### 17.2. Webhook simulator

Кастомный mini-сервис `tests/flights/Travel.Modules.Flights.WebhookSimulator/` — эмулирует Duffel webhooks для E2E (concept upstream: реальные тикеты в sandbox сами не выпускаются). Используется только в Testcontainers + Playwright средах.

---

## 18. ADRs к M1

| # | Title | Status |
|---|---|---|
| 0013 | flights-provider-abstraction | new |
| 0014 | mixed-aggregation-bookable-deeplink | new |
| 0015 | booking-aggregate-event-model | new |
| 0016 | booking-saga-via-marten-es | new |
| 0017 | flights-idempotency-strategy | new |
| 0018 | duffel-webhook-inbox-outbox | new |
| 0019 | payment-gateway-abstraction | new |
| 0020 | nl-search-cross-service-contract | new |

Создаются параллельно с реализацией каждой соответствующей функциональности.

---

## 19. Defaults фиксируются (без отдельных ADR)

- **TimeProvider** инжектится во все сервисы (root CLAUDE.md). Тесты — `FakeTimeProvider`.
- **Healthchecks:** `Duffel: /api/identity` ping, Travelpayouts `prices_for_dates?test=1` ping, Marten + EF (Foundation).
- **Pagination:** default `limit=50`, max `limit=200`, `offset`-based.
- **Rate limiting (outbound):** Polly retry 3x с jitter (50-500ms) + circuit breaker (5 fails / 30s window).
- **Feature flags:** `flights.travelpayouts.enabled` (bool, default true), `flights.nl_search.enabled` (bool, default true). Через `IOptionsMonitor`, без runtime UI.
- **Locale:** `Accept-Language: ru|en` header, default `ru`.
- **Currency:** query param `?currency=RUB|USD`, default `RUB`. Frankfurter дневной курс, кэш в Redis 24h.
- **Hold expiry:** `Offer.ExpiresAt` (от Duffel) — single source of truth. UI таймер обратного отсчёта.

---

## 20. Открытые вопросы для следующих milestones

- **M2:** multi-pax payload shape; saved travelers PII encryption (field-level via Data Protection API? KMS?); explainable ranking algorithm (rule-based vs LLM-based); multi-leg search criteria.
- **M3:** seat selection UX (Duffel seat maps API); refund policy engine (fare rules parsing); SSE event scope расширение; ancillaries в Money flow.
- **Кросс-milestone:** observability dashboard layout (Grafana JSON); load testing baseline; chaos testing для partial fan-out failure.

---

## 21. Acceptance criteria для M1

Milestone считается завершённым, когда:

- [ ] Можно искать рейсы LED→DME по фиксированной дате, получать смешанную выдачу bookable + deeplink (badge виден на FE).
- [ ] Можно забронировать один bookable-offer end-to-end: quote → hold → confirm → ticketed (через webhook).
- [ ] Можно отменить order в состояниях `Held` и `Confirmed` (не `Ticketed`).
- [ ] Webhook'и от Duffel ингестятся идемпотентно, обновляют состояние агрегата.
- [ ] NL-search парсит «из Москвы в Питер на выхах» → корректный `SearchCriteria` и нормальная выдача.
- [ ] Email и SSE приходят на все три зафиксированных события.
- [ ] Все 7 тестовых слоёв зелёные.
- [ ] OTel dashboard показывает все metrics из §13.1.
- [ ] Cost ledger пишется per NL-search call.
- [ ] 8 ADR написаны.
- [ ] README обновлён: BYO-keys для Duffel + Travelpayouts + Anthropic, инструкция запуска happy path.
- [ ] 2-3 черновика статей для блога подготовлены.

---

## 22. Что зафиксировано

- Скоуп M1 точно очерчен: search + single-pax booking lifecycle + NL-search + auth booking + sandbox payments + email/SSE + observability.
- Refund в M1 — только airline-initiated через webhook; user-initiated flow — M3.
- Provider abstraction разделена по capabilities (`IFlightSearchProvider` × 2, `IFlightBookingProvider` × 1).
- BookingAggregate — Marten ES, события past-tense, state machine с 6 состояниями (Refunded и Cancelled терминальные).
- Domain Offer — discriminated `BookableOffer | DeeplinkOffer`, type-safe на этапе компиляции.
- Search fan-out — parallel с per-provider timeout + partial result tolerance.
- NL-search — в Travel.AI, через Wolverine request/reply, без MAF (MAF в подпроекте 4).
- Webhook ingestion — inbox/outbox pattern, HMAC-verified, идемпотентно через `(source, event_id)`.
- Idempotency — header-based, TTL 24h, conflict → 409.
- 8 ADR покрывают все ключевые решения.
- Тестовая стратегия — все 7 слоёв с конкретным фокусом для M1.
- Forward-compat для M2 (multi-pax) заложен в API сразу.
- Деплой/operability — все metrics, spans, cost ledger определены.
