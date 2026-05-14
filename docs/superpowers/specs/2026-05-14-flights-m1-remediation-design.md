# Flights M1 Remediation — Design Spec

**Дата:** 2026-05-14
**Статус:** approved, ready for implementation plan
**Базовый milestone:** [`docs/superpowers/specs/2026-05-13-flights-m1-design.md`](2026-05-13-flights-m1-design.md)
**Базовый план:** [`docs/superpowers/plans/2026-05-13-flights-m1-backend.md`](../plans/2026-05-13-flights-m1-backend.md)
**Триггер:** скрупулёзный аудит ветки `flights-m1` (8 параллельных code-review агентов, диапазон `02508ac..20c2c6e`) выявил ~10 критических блокеров, ~30 important и ~50 minor находок.
**Скоуп:** починить **всё** — Critical + Important + Minor — так, чтобы ветка `flights-m1` проходила acceptance criteria §21 базового спека и не имела регрессий.

---

## 1. Контекст

`flights-m1` собирается без warnings (под solution-wide `TreatWarningsAsErrors`), 406 тестов зелёные. Но аудит показал, что **зелёные тесты систематически не покрывают именно сложные места** — конкуренцию, таймауты, реальные внешние контракты, — и часть фич спека описана, но не подключена в коде.

Этот документ — **remediation design**: он не вводит новых фич, он закрывает разрыв между базовым спеком M1 и реализацией. Каждый workstream (§4) ссылается на конкретные находки аудита (§7 — traceability).

Базовый спек M1 **остаётся источником истины**. Там, где реализация осознанно отошла от спека и решено это сохранить (Razor→HTML, Pact→snapshot, см. §2.3), правится **спек/ADR**, а не код.

---

## 2. Скоуп и зафиксированные решения

### 2.1. Включено

Все находки 8 агентов: Critical (блокеры merge), Important (закрытие M1 по спеку), Minor (вычистка). Полный перечень — §7.

### 2.2. Развилки — зафиксированные решения

| # | Развилка | Решение |
|---|---|---|
| D1 | `OfferReQuoted` re-quote путь (C6) | **Реализовать.** Добавить `AggregateId?` в `QuoteOfferCommand`; при наличии и стриме в `OfferQuoted` — append `OfferReQuoted` вместо `StartStream`. |
| D2 | `currency` / `locale` расположение в API (§19) | **Править код под спек.** `currency` → `?currency=` query-param; `locale` → `Accept-Language` header. |
| D3 | `flights:book` scope (C7, §10.2) | **Сделать default.** Перенести scope в `defaultClientScopes` realm-а + добавить authorization policy `flights:book` на booking-эндпоинты. |
| D4 | `DateRange` dead code (Domain I4) | **Удалить.** Не используется; перечисление в §4.3 базового спека не обязывает держать мёртвый код. Обновить §4.3 базового спека. |
| D5 | `OrderTicketed` record equality (Domain I2) | Ввести `EquatableArray<T>` (value-equality обёртка) в `Travel.Shared.Abstractions`; применить в `OrderTicketed`, `OrderStatus`, `BookingAggregate.TicketNumbers`. |
| D6 | Razor → HTML token-replacement (Notif I9) | **Ратифицировать как осознанную девиацию.** RazorLight 2.3.1 не работает на .NET 10; шаблоны статические. Обновить §12.1 базового спека + новый ADR `0021-email-rendering-without-razor`. |
| D7 | Pact → JSON-snapshot contract test (Build I3) | **Ратифицировать.** Обе стороны контракта в одном репозитории, дрейф ловится на build. Зафиксировать в ADR `0020` (дополнить). |
| D8 | `OfferHeld` — singular vs array (Persist I6) | **Оставить singular.** M1 — один пассажир; перевод на массив — это M2 forward-compat работа, не remediation. Код не трогаем; правим ADR `0015` — честно описать, что M2 multi-pax потребует event evolution (текущее утверждение ADR ложно). |

### 2.3. Не входит

- Новые фичи M2/M3 (multi-pax, refunds flow, ranking explainability).
- Frontend (отдельный план `2026-05-13-flights-m1-frontend.md`).
- Acceptance §21 item 12 («2-3 черновика статей для блога») — не код-задача, выносится на отдельное решение пользователя.

---

## 3. Архитектурный подход — 4 сквозных решения

Четыре блокера требуют единообразного подхода, применённого во многих файлах. Они выделены в фундаментные workstream-ы (WS1) или применяются по всем затронутым WS единообразно.

### A1. Транзакционный outbox (Booking C1, Webhooks C1)

Модуль персистит через **два** хранилища — Marten (event store) и EF (`FlightsDbContext`: inbox, idempotency, read-model, deeplink cache). Outbox нужен для обоих.

- **Marten-сторона (booking handlers).** Включить нативную интеграцию Wolverine ↔ Marten (`IntegrateWithWolverine()` в Marten-конфиге). `IDocumentSession` становится транзакционным outbox-ом. Handlers **перестают** звать `bus.PublishAsync` напрямую — нотификации возвращаются как **cascaded messages** Wolverine либо публикуются через session-enrolled `IMessageContext`. Append события и запись outbox-строки коммитятся атомарно.
- **EF-сторона (webhook endpoint).** Включить `FlightsDbContext` в Wolverine (`AddDbContextWithWolverineIntegration<FlightsDbContext>()`), endpoint/handler помечается `[Transactional]`. INSERT inbox-строки и публикация `ProcessDuffelWebhookCommand` коммитятся в одной транзакции.
- **Задача 1 WS1** — провести аудит уже существующей Wolverine-настройки Foundation, чтобы **расширить**, а не продублировать её.

### A2. Конкурентность (Booking C2, Webhooks C2, Idempotency I3)

Один принцип: **арбитром конкуренции выступает БД**, а не read-then-check в памяти.

- **Marten stream.** `FetchForWriting<BookingAggregate>(streamId)` (трекает expected version) во всех command-handler-ах; `ConcurrencyException` → типизированный `FlightsErrors` conflict (409). Wolverine может авто-ретраить на concurrency.
- **Webhook dedup.** Сохранить предварительную проверку, но ловить `PostgresException` SqlState `23505` на unique-constraint `(source, event_id)` и трактовать как dedup-путь → вернуть 200.
- **Idempotency middleware.** Вставлять **in-flight строку** (unique constraint = блокировка) **до** вызова handler-а; конкурентный запрос с тем же ключом ловит `23505` → 409 «in progress». В кэш ответа кладётся **только завершённый 2xx**.

### A3. Duffel HMAC (Webhooks C3, Duffel C3)

Находка аудита (`t=,v1=` с timestamp в подписываемом payload) — **гипотеза**. **Задача 1 WS3** — верифицировать актуальную схему подписи Duffel v2 webhooks по текущей документации Duffel (WebFetch), затем реализовать строго по ней, с тестовой фикстурой на основе **захваченного реального заголовка**, а не самосгенерированного.

### A4. Resilience (Duffel C1/C2, Search I5, §19)

Именованные Polly-pipeline-ы per-typed-client (`AddResilienceHandler`) с точными числами §19:
- retry 3× / jitter 50–500ms / circuit breaker 5 fails / 30s window;
- per-request timeout: 4s (Duffel search, Travelpayouts), 10s (Duffel orders), 2s (Frankfurter);
- подключить мёртвые `TimeoutSeconds` опции (`DuffelOptions`, `TravelpayoutsOptions`).

---

## 4. Workstreams

~35 укрупнённых фиксов в 11 workstream-ах. **WS0 + WS1 выполняются первыми и последовательно** (на них опирается остальное). **WS2–WS9** затем выполняются параллельно через субагентов. **WS10** — последним (документация отражает финальный код; CI/simulator требуют стабильных handler-ов). После каждого WS — code-review субагент. Чекпойнты пользователю: после WS0+WS1, после параллельного батча, после WS10.

> **Cross-WS shared artifacts.** Несколько артефактов создаются в одном WS и потребляются в другом параллельном (например `FlightsFeatureFlags` — создаётся в WS6, `travelpayouts.enabled` потребляется в WS5; `BookingAggregate.GuardCanTicket/GuardCanRefund` — в WS3, terminal-guards — в WS2; `EquatableArray` — в WS9, `OrderStatus`-обёртка нужна WS4). Implementation-план (writing-plans) разрешает такие зависимости на уровне отдельных задач — выносит создание shared-артефакта в раннюю задачу либо в WS0/WS1.

### WS0 — Architecture guard rails + `DateTime.UtcNow`
**Зависимости:** нет. **Закрывает:** Domain C1, Persist C1/C2/C3, Build I1.
- Добавить недостающие ArchUnit-правила §17: (b) `Marten.*` используется только в Flights/Trips; (d) нет `DateTime.UtcNow`/`DateTimeOffset.UtcNow` в `Travel.Modules.Flights.{Core,Application,Infrastructure}`.
- Снять `.WithoutRequiringPositiveResults()` с правил, у которых subject-set уже непуст; где нужно — добавить companion `Should().Exist()`.
- Починить `PassengerInfo.Create` — прокинуть `TimeProvider` (или параметр `DateOnly today`); единственный caller (`HoldOfferHandler`) уже имеет `TimeProvider`.
- Выполняется первым: новые arch-правила охраняют весь последующий код.

### WS1 — Transactional outbox infrastructure (A1)
**Зависимости:** нет. **Закрывает:** Booking C1, Webhooks C1 (инфраструктурная часть).
- Аудит существующей Wolverine-настройки Foundation.
- Marten ↔ Wolverine интеграция (`IntegrateWithWolverine()`), EF ↔ Wolverine (`AddDbContextWithWolverineIntegration<FlightsDbContext>`).
- Правки composition root (`FlightsModuleServiceCollectionExtensions`) и `Travel.Host/Program.cs`.
- Интеграционный тест: краш между persist и publish не теряет сообщение (outbox его дослать).

### WS2 — Booking saga correctness
**Зависимости:** WS1. **Закрывает:** Booking C1/C2/C3, I1–I8 + minors; Domain I1/I7 (Version/terminal guards в агрегате).
- Транзакционная публикация нотификаций через outbox (cascaded messages) — `ConfirmOrderHandler`, `CancelOrderHandler`.
- Оптимистичная конкуренция: `FetchForWriting` во всех booking command-handler-ах; `ConcurrencyException` → 409.
- `OfferReQuoted` (D1): `AggregateId?` в `QuoteOfferCommand`; append в существующий стрим.
- Запретить cancel из `Ticketed` — в `CancelOrderHandler` **и** в `BookingAggregate.GuardCanCancel`.
- `OrderReadModelProjectorImpl`: писать timestamp-ы (`TicketedAt`/`CancelledAt`/`RefundedAt`) из события, не из `GetUtcNow()`; ставить только если `null` (replay-safe).
- Idempotency: кэшировать только 2xx; in-flight строка до handler-а (A2); включить HTTP-метод в hash; route-target `EndsWith` вместо `Contains`.
- `HoldOfferHandler`: пробросить реальные `FareConditions` с момента quote (через payload `OfferQuoted` / поле агрегата), не фабриковать.
- `BookingAggregate`: убрать/поддержать `Version`; добавить тесты нелегальных `Apply`-последовательностей и terminal re-entry.
- Валидация полей команд (non-empty `ProviderOfferRef` и т.п.); консолидировать дублированные test-фейки (`RecordingMessageBus`, `NullFlightsMetrics`) в shared test infra.
- Тесты: все строки compensation-таблицы §7.2 (включая Authorize-fail), cancel-from-Ticketed, idempotency replay/conflict/concurrent-race.

### WS3 — Webhooks
**Зависимости:** WS1. **Закрывает:** Webhooks C1/C2/C3/C4, I1/I7 + minors.
- Публикация `ProcessDuffelWebhookCommand` через EF-outbox в одной транзакции с INSERT inbox (A1).
- Атомарный dedup: ловить `23505` → 200 (A2).
- Terminal-state guard в `DuffelWebhookHandler`: не аппендить `OrderRefunded`/`OrderTicketed` в `Refunded`/`Cancelled`/уже-`Ticketed` стрим; добавить `BookingAggregate.GuardCanTicket()`/`GuardCanRefund()`.
- HMAC (A3): верифицировать реальную схему Duffel v2, реализовать по ней, фикстура с реальным заголовком.
- `order.airline_initiated_change` (не cancelled): явный `case` с OTel-метрикой, не `default`.
- `WebhookInboxStore.MarkProcessedAsync`: логировать warning при отсутствии строки, не молчаливый no-op.
- `PublishAsync` с `ct`.

### WS4 — Providers & resilience (A4)
**Зависимости:** WS0. **Закрывает:** Duffel C1/C2/C3(совм. WS3), I4–I10 + minors; Search I5.
- Именованные Polly-pipeline-ы для `DuffelClient`, `TravelpayoutsClient`, `FrankfurterClient` по §19; подключить `TimeoutSeconds`.
- Idempotency-key в Duffel payments: пробросить ключ в `ConfirmOrderAsync` и заголовок Duffel; `DuffelTestWalletPaymentGateway` — учитывать/эхоить ключ; добавить `DuffelClient` API для per-request заголовков.
- `RefreshOfferAsync` → выявлять `PriceChanged` (пробросить прежнюю `Money` в quote-путь) либо явно задокументировать отсрочку.
- `HoldOfferAsync` 422 → корректная ошибка (`OfferExpired`/новая hold-unavailable), не `OrderNotCancellable`.
- Hold expiry: при отсутствии `payment_required_by` — ошибка или fallback на `Offer.ExpiresAt`, не магические +20 мин.
- `ConfirmOrderAsync`/`CancelOrderAsync`: не утекать сырой JSON Duffel в domain-ошибку (логировать, возвращать санитизированную причину).
- `GetOrderStatusAsync`: `OrderStatus.Status` → enum; репортить `ticketed` при наличии ticket-документов (нужно для §7.2 polling fallback).
- `DuffelOfferMapper`: маппить baggage summary (§5.3); читать fare basis из всех slices.
- Тесты: edge-cases маппера (unparseable amount, пустые slices, null conditions), failure-пути booking-провайдера, ветки `catch` в search-провайдере.

### WS5 — Search pipeline & caching
**Зависимости:** WS0. **Закрывает:** Search C1/C2/C3, I4/I6–I11 + minors.
- Deeplink EF-кэш — **только аудит** (§6.4): `TravelpayoutsSearchProvider` перестаёт из него **читать**, только пишет.
- `FrankfurterRatesCache`: `InvariantCulture` на `ToString`/`TryParse`.
- Fan-out изоляция: расширить `catch` в `SearchFlightsHandler.RunWithTimeout` до общего (log + `ProviderFailure`); в `TravelpayoutsSearchProvider` ловить `JsonException` и пустое тело → `ProviderUnavailable` (не `throw`).
- `SearchCacheRedis`: guarded deserialize (битая/старая запись → cache miss, не исключение); `JsonStringEnumConverter`.
- Подключить `IFxRates.ConvertAsync` в pipeline: нормализовать офферы к `criteria.Currency` до dedup/rank.
- Round-trip dedup-ключ: для `Itinerary.IsRoundTrip` — composite-ключ из primary-сегмента **каждого** slice.
- Детерминированный финальный tie-break в `OfferRanker` и `OfferDeduplicator` (`ThenBy(o => o.Id.Value)` / provider+ref).
- Feature flag `flights.travelpayouts.enabled` (см. WS6); `TravelpayoutsSearchProvider` коротит в пустой список (не failure) при выключении.
- Travelpayouts token: редактировать из OTel HTTP-span-ов (URL filter / enrich).
- Minors: Frankfurter base address из конфига; `SearchCacheKey` — явный `.Value`; маппер — не фабриковать `XX0`-офферы (skip + log).
- Тесты: timeout-путь провайдера, mixed bookable+deeplink через handler, Redis TTL.

### WS6 — API & composition
**Зависимости:** WS1. **Закрывает:** API C1, I2–I8 + minors; Persist I7.
- `flights:book` (D3): authorization policy в `Program.cs`, `[Authorize("flights:book")]` на hold/confirm/cancel; `defaultClientScopes` в `travel-realm.json`.
- Feature flags (§19): `FlightsFeatureFlags` options, `IOptionsMonitor` — `travelpayouts.enabled` (в `SearchFlightsHandler`), `nl_search.enabled` (в `NlSearchEndpoint`/handler).
- Healthchecks (§19): Duffel `/api/identity` ping, Travelpayouts `prices_for_dates?test=1` ping, Marten/EF.
- currency/locale (D2): `?currency=` из `[FromQuery]`, `locale` из `Accept-Language`.
- `offset` clamp `Math.Max(0, offset)`.
- `OrderResponseMapper`: ловить конкретный `JsonException` + log warning (не пустой `catch {}`), либо канонизировать форму в проекторе.
- HTTP-pipeline тесты: гонять реальный Wolverine pipeline (Alba `MapWolverineEndpoints`), проверять auth на реальных discovered-эндпоинтах.
- `ContractMappingTests`: добавить request→domain и validation-error пути.
- Minors: `CancelOrderRequest` DTO — использовать или удалить; зафиксировать в `modules/flights/CLAUDE.md` исключение «DTO-bundle files».

### WS7 — Notifications & observability
**Зависимости:** WS0. **Закрывает:** Notif/Obs I1–I9 + minors + matrix-пробелы (§13.2 spans, §13.3 correlation_id, контент писем).
- Метрики §13.1: добавить `search.partial_fill_rate`, `offer_to_book_conversion`, `payment.success_rate` (ObservableGauge + rolling-window state), `payment.duration_ms` (histogram), `nl_search.duration_ms`.
- `nl_search.tokens_used`: тег `direction` (input/output) — два `Add` (совм. WS8).
- Spans §13.2: `ActivitySource` в модуле — HTTP-call per provider, saga state transition, Anthropic call.
- Structured logs §13.3: enrichment `correlation_id` (W3C traceparent).
- SSE backpressure §12.2: byte-accounting + disconnect после 1MB, либо — если оставляем `DropOldest` — отдельный ADR + правка §12.2; **решение: реализовать 1MB-disconnect по спеку**. Безусловный drain каждой итерации цикла.
- Контент писем: cancellation — reason + refund expectation (пробросить `CancelReason` в модель); confirmation — «ticket follows» disclaimer.
- `KeycloakUserDirectory`: `catch ... when (ex is not OperationCanceledException)`; null-body → `FallbackProfile` (не молчаливый drop письма).
- Locale fallback в renderer → `ru` (не `en`).
- Minors: `Register` внутрь `try` (leak window); удалять пустой per-order list из словаря `OrderSseConnectionRegistry`; template root из `AppContext.BaseDirectory`.
- Тесты: XSS-escaping (`<script>` в `GivenName`), locale fallback, SSE backpressure/concurrency/heartbeat/endpoint, Keycloak refresh.

### WS8 — NL-search
**Зависимости:** WS0. **Закрывает:** Webhooks/NL I2–I6 + minors.
- Inject `[today: yyyy-MM-dd]` в user-message `NlSearchExtractor.ExtractAsync` (параметр `DateOnly today`/`TimeProvider`); `NlSearchAiHandler` передаёт `time.GetUtcNow()`, eval-runner — фиксированную дату.
- `NlSearchHandler`: `catch (OperationCanceledException) { throw; }` первым; `ILogger` + warning в общем `catch`.
- AI-eval: ambiguous-кейсы должны что-то ассертить; date-inference tolerance → ±1 (по §17); aggregate pass-rate assertion; skip-без-ключа сохранить.
- Contract test: ассертить **обе** стороны (`Travel.AI` и `Travel.Modules.Flights.Application` records).
- Minors: `CorrelationId` из `Activity.Current?.TraceId`; `user_id` в cost_ledger (или зафиксировать как dead-until-M2).

### WS9 — Domain core minors
**Зависимости:** WS0. **Закрывает:** Domain I2–I6 + minors.
- `EquatableArray<T>` (D5) в `Travel.Shared.Abstractions`; применить в `OrderTicketed`, `OrderStatus`, `BookingAggregate.TicketNumbers`; исправить вводящий-в-заблуждение тест равенства.
- `Itinerary.Create`: валидация round-trip continuity (`slices[1]` стыкуется со `slices[0]`).
- Удалить `DateRange` + тест + ссылку (D4); обновить §4.3 базового спека.
- Typed identifiers: `IsEmpty`/`None` + валидация на границах агрегата/handler-ов; тест-документация о `default`.
- Тесты: негативные ветки VO (ассертить error-**коды**, не только типы), Money overflow, `PhoneNumber` граница 15/16 цифр, `PassengerInfo` DOB-сегодня, `Slice` 3+ сегмента, `Gender.Parse(null)`, `FlightsErrors` `ErrorType` каждой ошибки.

### WS10 — Docs, test infra, CI
**Зависимости:** WS2–WS9. **Закрывает:** Persist I4–I6 + minors, Build I2/I3/I6, Notif I9, ADR-дрейф.
- ADR-фиксы: `0015` (`OfferHeld` остаётся singular per D8 — честно описать, что M2 multi-pax потребует event evolution), `0017` (правильные routes, `body_hash`), `0018` (`OrderTicketed` вместо несуществующего `BookingConfirmed`), `0019` (DI-sample + «enforcement via runtime `TestOnlyGuard`»).
- Новый ADR `0021-email-rendering-without-razor` (D6); дополнить `0020` про Pact→snapshot (D7).
- Обновить базовый спек: §12.1 (Razor→HTML), §19 (currency/locale — без изменений, код подогнан), §4.3 (убрать `DateRange`).
- `WebhookSimulator` mini-service (`tests/flights/Travel.Modules.Flights.WebhookSimulator/`, спек §17.2) — генерит реальные Duffel-формат webhooks (включая корректную HMAC-подпись из WS3).
- CI: job `test-integration` (фильтр `Category=Integration`, Docker/Testcontainers); проверить запуск Flights/Host integration-тестов.
- `FlightsDbContext.OnConfiguring` → `UseSnakeCaseNamingConvention()` как single source of truth.
- README: при необходимости — отметить dev-only статус пароля realm-а; синхронизировать с финальными изменениями.
- `dotnet csharpier .` — формат всего изменённого.

---

## 5. Модель исполнения

- **Подход:** writing-plans → план по задачам → subagent-driven-development. Каждая задача: failing test → fix → green → commit (TDD).
- **Порядок:** WS0 → WS1 (последовательно, фундамент) → WS2–WS9 (параллельно через субагентов) → WS10.
- **Code-review субагент** после каждого WS; находки фиксятся до перехода дальше.
- **Чекпойнты пользователю:** (1) после WS0+WS1, (2) после параллельного батча WS2–WS9, (3) после WS10.
- **Commit format:** conventional commits, scopes из `commitlint.config.mjs` (`flights`, `host`, `ai`, `shared`, `arch`, `test`, `docs`, `infra`, `ci`).
- **Ветка:** продолжаем в `flights-m1`.

---

## 6. Acceptance criteria

Remediation считается завершённой, когда:

- [ ] Все Critical и Important находки §7 закрыты; Minor — закрыты или явно отложены с записью в этом документе.
- [ ] `dotnet build Travel.sln` — 0 warnings (под `TreatWarningsAsErrors`), 0 errors.
- [ ] `dotnet test Travel.sln` — все слои зелёные, включая новый CI integration-job; AI-eval перестаёт быть «green-by-skip» по существу (ассерты есть, skip только без ключа).
- [ ] ArchUnit-правила §17 (a)-(e) реализованы и реально проверяют (subject-set непуст).
- [ ] Re-audit: повторный прогон уменьшенного набора аудит-агентов против нового HEAD подтверждает закрытие каждого Critical/Important и отсутствие регрессий.
- [ ] Scorecard §21 базового спека — **12/12** (item 12 «блог-черновики» — выносится на решение пользователя, не код).
- [ ] 8 ADR обновлены + 1 новый (`0021`); базовый спек синхронизирован с осознанными девиациями.

---

## 7. Traceability — находки аудита → workstream

Сокращения агентов: **DOM** domain core, **DUF** Duffel, **SRCH** search, **SAGA** booking saga, **WHK** webhooks/NL, **NOB** notifications/observability, **API** api/composition, **PER** persistence/ADR, **BLD** build/cross-cutting.

| Workstream | Закрываемые находки |
|---|---|
| **WS0** | DOM-C1, PER-C1, PER-C2, PER-C3, BLD-I1 |
| **WS1** | SAGA-C1 (инфра), WHK-C1 (инфра) |
| **WS2** | SAGA-C1, SAGA-C2, SAGA-C3, SAGA-I1–I8 + minors, DOM-I1, DOM-I7 |
| **WS3** | WHK-C1, WHK-C2, WHK-C3, WHK-C4, WHK-I1, WHK-I7, WHK-minors, DUF-C3 |
| **WS4** | DUF-C1, DUF-C2, DUF-I4–I10 + minors, SRCH-I5 |
| **WS5** | SRCH-C1, SRCH-C2, SRCH-C3, SRCH-I4, SRCH-I6–I11 + minors |
| **WS6** | API-C1, API-I2–I8 + minors, PER-I7 |
| **WS7** | NOB-I1–I9 + minors, §13.2/§13.3/email-content matrix-пробелы |
| **WS8** | WHK-I2, WHK-I3, WHK-I4, WHK-I5, WHK-I6, WHK-minors, BLD-I4 |
| **WS9** | DOM-I2, DOM-I3, DOM-I4, DOM-I5, DOM-I6, DOM-minors |
| **WS10** | PER-I4, PER-I5, PER-I6, PER-I8, PER-minors, BLD-I2, BLD-I3, BLD-I6, NOB-I9 |

Полные тексты находок с `file:line` — в транскрипте аудита (8 отчётов code-review агентов, диапазон `02508ac..20c2c6e`). Каждая задача implementation-плана будет цитировать конкретную находку.

---

## 8. Что зафиксировано

- Скоуп: **всё** — Critical + Important + Minor, цель — `flights-m1` проходит §21 без регрессий.
- 7 развилок решены (§2.2): OfferReQuoted реализуем, currency/locale правим в коде, flights:book — default, DateRange удаляем, EquatableArray вводим, Razor/Pact девиации ратифицируем в спеке/ADR.
- 4 сквозных архитектурных решения (§3): транзакционный outbox (Marten + EF), конкуренция через БД-арбитраж, HMAC по верифицированной схеме Duffel, resilience по точным числам §19.
- 11 workstream-ов (§4), WS0+WS1 — фундамент, WS2–WS9 параллельны, WS10 — финал.
- Исполнение: subagent-driven + TDD + code-review после каждого WS + 3 чекпойнта.
- Базовый спек M1 — источник истины; девиации правят спек, а не наоборот.
