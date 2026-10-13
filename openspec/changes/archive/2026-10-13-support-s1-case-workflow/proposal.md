# Proposal

## Why

После source-завершения Flights M3 клиенту нужен способ сообщить о проблеме с заказом, а оператору — увидеть обращение, ответить и завершить работу. Сейчас Support-модуля и пользовательского процесса нет; Laya сможет помогать сортировке только после появления этого процесса и размеченных примеров.

## What Changes

- S1: один входящий канал в приложении; приватное обращение, необязательная ссылка на собственный заказ, очередь оператора, переписка и статусы Open / WaitingForCustomer / Resolved.
- Клиент видит только свои обращения. Оператор работает по отдельному `support:operator` scope; Support не даёт права отменять/возвращать/подтверждать заказ.
- Ручные категория и приоритет имеют явный источник Operator. Создание не зависит от AI или доступности supplier/payment API.
- Новый Support-модуль с relational persistence, optimistic concurrency, защищённым свободным текстом и ограниченной retention; связь с Flights через опубликованный read-only contract.
- Потерянный ответ проверяется через приватную квитанцию конкретного действия; отсутствие записи не превращается в разрешение повтора. Потеря Support прав очищает приватную переписку даже при прежнем user ID. Шифрование и чтение после restart/rotation получают отдельное доказательство настоящего provider в disposable CI.
- Совместимость existing Flights auth/booking/uncertainty/readers сохраняется. Не вводятся multichannel helpdesk, attachments, SLA engine, assignment engine, email/SSE для Support или автоматические финансовые действия.
- Laya S2 и evaluation/limited enablement S3 — следующие отдельные changes. В S1 фиксируется проверенное исследование реального [Laya](https://github.com/NandhaKishorM/laya), без placeholder SDK/классификатора или запуска модели.

## Capabilities

### New Capabilities

- `support-case-workflow`: создание приватного обращения, owner/operator доступ, безопасная связь с собственным заказом, ручная очередь, переписка, статусы, concurrency, privacy и независимость от AI/денежных операций.

### Modified Capabilities

Нет изменений требований пяти действующих Flights capabilities. Public read-only module contract — новое интеграционное чтение; согласие, финансовые статусы, booking hashes, SSE и customer refund rules сохраняются.

## Impact

Предлагаются четыре слоя `modules/support`, один минимальный опубликованный Flights access contract, Support Api.Composition в Host, Angular client/operator routes с Client-only rendering и typed TS client. Отдельный public PKCE operator client и role-gated scope входят в source realm fixture; нынешний customer client/JWT protocol/Flights guards сохраняются. Потребуется расширить exact architecture inventory с20до24module projects и реально выполняемое Support CI coverage, не ослабляя правила границ. EF schema/migration source, Support key lifecycle, source auth/CI changes и operator scope provisioning требуют явно согласованных действий перед реализацией/rollout; в планировании ничего не генерируется и не применяется.

Канонический корпус — этот OpenSpec change. [M3 review](../../../../docs/architecture/flights-m3-completion-review.md), [current-state](../../../../docs/architecture/current-state.md), [Support roadmap](../../../../docs/superpowers/specs/2026-05-03-travel-platform-concept.md), [ревизия S1](audit.md), [варианты/проектирование](design.md), [Laya research](laya-research.md). Это план для проверки, не разрешение на apply/публикацию/ключи/БД/deploy.
