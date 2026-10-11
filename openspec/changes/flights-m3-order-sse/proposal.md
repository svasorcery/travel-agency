# Proposal

## Why

Открытая страница заказа сейчас перестаёт опрашивать GET после короткого окна либо terminal status и не слушает существующий SSE endpoint. Поэтому ticket webhook, новый supplier cancellation report и позднее изменение клиентского возврата остаются незаметны до ручного чтения.

## What Changes

- Подключить одну управляемую подписку страницы заказа к существующему `/events/flights/orders/{orderId}` через fetch с Bearer-заголовком. Новый валидный сигнал инвалидирует применимые чтения, повторные сигналы объединяются; состояния и деньги берутся только из owner GET.
- Сохранить Confirmed/Ticketed/Cancelled notifications, readiness, bounded registry и heartbeat. Добавить один минимальный `OrderChanged` сигнал для committed cancellation/report/customer-refund batches созданного или впервые удержанного заказа, используя существующий Marten outbox и readiness.
- Аддитивно вернуть `projectedStreamVersion` в order GET и `bookingVersion` в creation GET: сейчас эти две поверхности не позволяют проверять свежесть ответа. Cancellation/refund GET уже имеют bookingVersion.
- Защитить все четыре чтения страницы от stale owner/epoch/generation/version callbacks. Объединять сигналы во время GET, сохранять доказанные результаты при лаге/ошибке, не переносить consent. Повторный auth-status той же личности не пересоздаёт сессию наблюдения; пауза чтений сохраняет уже допущенные команды.
- Восстанавливать пропущенные сигналы обязательным GET после подключения, редким GET fallback, при возврате на вкладку и ручным обновлением. Не обещать replay или межпроцессную доставку.
- Добавить fictional demo и тесты protocol/parser, commit/readiness, lifecycle/recovery и бизнес-инвариантов. Существующие mandatory CI и PR E2E остаются обязательными.

## Capabilities

### New Capabilities

- `flights-order-live-updates`: безопасная актуализация открытой owner-страницы заказа по best-effort SSE и авторитетным чтениям.

### Modified Capabilities

Нет. Действующие `flights-whole-order-cancellation`, `flights-booking-ancillaries` и `flights-customer-refund-after-cancellation` сохраняют бизнес-контракты. Новый capability определяет доставку актуализации и соблюдает их инварианты; не копирует денежную политику.

## Impact

Application: targeted invalidation envelopes/writers/handler и версия creation query. Infrastructure: reuse registry/readiness/projection без схемы. Api: аддитивные read DTO, notification retry ownership. UI/shared TS: небольшой fetch SSE transport, page-scoped refresh coordination, монотонные readers и `/events/**` dev proxy. Core: без новых domain events, состояний или правил.

Новых runtime-пакетов, брокеров, generic recovery frameworks, auth/CI/CD изменений, миграций, real PSP, fare-rules summary или Support нет. Детали аудита и границ — [research.md](research.md), решения — [design.md](design.md), задачи — [tasks.md](tasks.md).

Статус: после планирования, независимого review и дополнительного self-review пользователь разрешил source-реализацию словом «го». Реализация и локальная проверка идут в единственной рабочей копии этого change — managed worktree от freshly fetched `7b7fb0f2831cb5067d4dea025e9e7d9d3f16e347` (2026-10-11); HEAD совпадает с checkpoint. Stage/commit/push/PR/merge/sync/archive и live требуют отдельного разрешения. Текущие результаты и обязательные следующие gates — [verification.md](verification.md).
