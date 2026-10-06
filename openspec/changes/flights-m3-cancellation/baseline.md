# Проверенный baseline

**База:** fetched origin/dev = FETCH_HEAD = `31a3803a4d558199eec79b48dc8cf91e078ac681`, 2026-10-04 10:24:27 MSK. Checkpoint совпадает, M1 `d75186054a33de0e1cb688da7e3e7cc42927dadf` ancestor, exit 0. Native managed worktree от этого SHA: `C:/Users/Vladimir_sva/.codex/worktrees/flights-m3-openspec-design/travel-agency`, detached HEAD, чистый перед документами. Root dev не переключён; архивы не восстановлены. Детали в process-log.

Все пути ниже относительно этого worktree; точные file/symbol links являются source evidence, не утверждением live-проверки.

| Слой/контракт | Факт из исходников | Значение для M3 |
| --- | --- | --- |
| Core `modules/flights/Travel.Modules.Flights.Core/Aggregates/BookingAggregate.cs`, `DecideCancel`/`DecideOwner` | Held/Confirmed allowed; Ticketed rejected; Cancelled/Refunded owned no-op; ownerless fail closed. Apply replay unconditional. | Нужна явная новая матрица, не обход guard в Infrastructure. No-op не доказывает конкретную supplier operation. |
| Core `Providers/IFlightBookingProvider.cs` | Cancel возвращает ErrorOr<Success>, где Success означает terminal provider cancellation; GetOrderStatus имеет грубый status. | Этого порта недостаточно для terms/consent/evidence; нужен capability-specific нейтральный контракт. |
| Application `Handlers/Booking/CancelOrderHandler.cs` | FetchForWriting → owner/state → provider → OrderCancelled → explicit Marten commit/outbox. При ошибке provider событий нет. | Provider success до локального commit оставляет crash/conflict gap; server operation не сохранена. |
| Application `Handlers/Booking/ConfirmOrderHandler.cs`, ADR0016 amendment M2.3 | Внешние действия предшествуют success commit; неизвестность сохраняет Held без durable operation/barrier. | Новая отмена не может считать Held доказательством отсутствия capture. Нужен bounded barrier либо более узкий scope. |
| Infrastructure `Providers/Duffel/DuffelFlightBookingProvider.cs`, CancelOrderAsync/GetOrderStatusAsync | Cancel returns ProviderCancellationNotSupported до HTTP. Status использует cancelled_at/documents, не cancellation terms. | Unsupported остаётся до implementation approval. Нужны новые нормализованные observation facts, не reuse грубого status. |
| Infrastructure `Providers/Duffel/Dto/DuffelOrderDto.cs` | Order DTO не содержит available_actions/cancellation; cancellation DTO имеет только id/order_id. | Полный consent/recovery контракт сейчас отсутствует. Supplier wire DTO остаются здесь. |
| Infrastructure `FlightsInfrastructureServiceCollectionExtensions.cs`, ShouldRetryDuffelRequest | Автоматический retry Duffel только GET/HEAD. | Не расширять retry на create/confirm POST без доказанной supplier семантики. |
| Api `Endpoints/CancelOrderEndpoint.cs` | JWT flights:book, GUID subject validation, no-store, bodyless POST, OrderResponse из command snapshot. | Consent нельзя молча добавить старому вызову; требуется явный новый контракт. |
| Api `Middleware/IdempotencyKeyMiddleware.cs` | method/path/raw body hash, 16KiB, cache только 2xx, errors/exception abandon; targeted hold/confirm/cancel. | HTTP replay ≠ durable operation. Новый protocol должен жить независимо от cached 202 и TTL. |
| Infrastructure `Persistence/Configurations/IdempotencyKeyConfig.cs` | PK только Key; lookup концептуально owner/route-scoped. | Глобальная UUID collision gap известна B5. Не обещать независимый namespace и не чинить схему незаметно. Новый operation identity не зависит от этой таблицы. |
| Application `Persistence` commit helpers, `Handlers/Booking/ReconcileOrderReadModelHandler.cs`; Infrastructure `Persistence/OrderReadModelReconciler.cs`/`OrderReadModelEventApplier.cs` | Marten source + atomic durable reconcile/outbox; EF versioned eventual projection. | Recovery результат отдавать из authoritative operation snapshot, EF lag отдельно. |
| Application `Handlers/Webhooks/DuffelWebhookHandler.cs` | Ticket callback и airline cancellation отдельны; нынешний refund берёт agg.TotalAmount и generated local RefundRef. | Старый Refunded не является подтверждением новой пользовательской операции или фактической суммы supplier. Не применять его shortcut к M3. |
| Angular `apps/web/src/app/flights/flight-order-operations.service.ts` | attempts/keys/terminal overlay только в памяти owner/session; unknown barriers, same-key retry, no reload/multi-tab guarantee. | Серверный owned operation read + безопасный refresh; browser state не сохранять. |
| M2.5 source/ADR0025 | 1–4 ordered legs/open-jaw и 1–9 adults в одном offer/order; current-state содержит M2.5. | Whole-order consent включает полный маршрут/группу. Старое one-way/return в modules/flights/AGENTS.md описательно устарело. |

## Архитектурные и тестовые источники

Прочитаны root/Flights/Shared AGENTS; applicable docs nested AGENTS не обнаружены. ADR0015–0017/0023–0025 обязательны: Core → Application → Infrastructure → Api, Host только facade, единые глобальные builders, past-tense IDomainEvent, ErrorOr factories, TimeProvider. Продолжение cancellation state внутри booking stream предложено в design; отдельный Saga запрещённым baseline не подменяется.

B5 design `docs/superpowers/specs/2026-10-01-flights-m1-b5-own-order-cancellation-design.md`, selected brief `2026-10-02-flights-cancellation-openspec-pilot-brief.md`, `docs/superpowers/results/2026-10-03-flights-m25-multi-leg.md`, `docs/architecture/current-state.md` восстановлены. M2.5 CI/merge/check counts в user checkpoint являются supplied evidence, remote runs заново здесь не проверялись.

`tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/CancelOrderHandlerTests.cs` реально StartAsync PostgreSQL, Marten AutoCreate.All, EF EnsureCreatedAsync: **не запускать локально**. FlightsEfInitializer вызывает MigrateAsync вне Production; FlightsMartenInitializer и WolverineMessageStoreInitializer применяют schema. Host/AppHost не стартуют. Provider WireMock/HTTP fixtures не автоматически safe: аудит конкретного startup перед запуском.

Dependency-free `npm.cmd run check:ai-harness` в новом worktree: **90 passed, 0 failed**, exact 8 instruction pairs/8 skills/5 agent pairs/5 legacy commands. Это текущий baseline до tooling edits. Authenticated `verify:ai-harness:codex` и product suites не запускались.
