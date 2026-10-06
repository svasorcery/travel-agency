# Flights M3: whole-order cancellation — proposal

## Why

После M2 пользователь умеет оформить один заказ на 1–9 взрослых и 1–4 перелёта. B5 показывает отмену, но Duffel capability намеренно unsupported до HTTP, а память вкладки не восстанавливает результат после reload/restart. Нужен достоверный исход отмены после согласия на актуальные условия, без обещания клиентской выплаты.

**Статус 2026-10-06:** selected cancellation pilot and pinned OpenSpec integration are implemented. Final PR36 head49e203c passed all13normal checks including E2E, merged ascf9731a; postmerge CI37470390776 passed all12normal checks. Docs-only sync/archive closure is authorized and prepared here; its publication/CI/cleanup are separate remaining gates. This is the only feature corpus; measured evidence and boundaries are retained in verification/review/process-log.

## What Changes

- Отмена целого принадлежащего пользователю заказа; условия поставщика, сумма/валюта/направление возврата, срок и явное согласие на неизменную ревизию.
- Серверная идентичность и история операции до внешних действий; recovery messages сохраняются атомарно до possible dispatch и восстанавливаются после restart без клиента. Owned read возвращает результат после reload.
- Подтверждённый успех, подтверждённый отказ, неизвестность и явная ручная проверка. Отмена заказа и финансовое свидетельство поставщика имеют независимые поля.
- Координация повторов, двух вкладок и confirm/cancel на сервере; projection lag не переписывает подтверждённый результат.
- **BREAKING** старый bodyless `/orders/{id}/cancel` не может инициировать новую supplier cancellation без terms/consent. Для terminal owned no-op остаётся чтение существующего локального факта; это не внешний receipt.
- Accepted D1–D8 include Ticketed eligibility only with actual supplier capability and coordination evidence; the implemented code and ADR0015 record that decision.
- Воспроизводимое подключение одной проверенной версии OpenSpec к точному inventory Travel: deterministic skill adapter направляет все CLI commands через pinned launcher, со сверкой upstream/effective hashes и сохранением старых проверок.

## Capabilities

### New Capabilities

- `flights-whole-order-cancellation`: согласие на актуальные условия, внешний результат, восстановление и server-side coordination одного заказа.

### Modified Capabilities

Нет существующих `openspec/specs` в базе. B5/M2/ADR — источники baseline; import всего старого корпуса и параллельная новая спецификация в `docs/superpowers` не нужны. После принятия/реализации delta будет синхронизирована в единственный OpenSpec capability при archive.

## Impact

Core booking transition/operation facts; Application orchestration/confirmation barrier/queries; Infrastructure Duffel cancellation capability and safe read mapping; Api contracts/composition; shared TypeScript readers; Angular order operations/pages/feed; existing unit/HTTP/provider/integration/contract/demo suites. Новый универсальный движок, store, процесс или Support module не нужен; bounded cancellation delivery использует существующий Wolverine.

Accepted implementation amendments to ADR0015/0016/0017/0023 and current-state are recorded; старые history identities и PII purposes сохраняются. `harness-integration.md` records completed tooling approval and its actual bounded proof. CI/CD не меняется.

## Boundaries and approval

Fictional data only; без supplier/payment/Anthropic/paid APIs и paid evals. Никаких local Host/AppHost/DB/schema apply/migrations/key provisioning/deploy. Настоящие DB/Host/Aspire tests — существующий CI после отдельной публикационной авторизации. .NET команды последовательно, новые fixtures сначала аудит startup.

**Historical planning authorization:** основа D1–D8/вариант A согласована; final design/tasks закрывают технические детали. На этом исходном checkpoint было разрешено довести план до конца. Следующий execution этап (tooling или product) определяется отдельно от этого документального поручения. Stage/commit/push/PR/merge/deploy не подразумеваются. Историческое разрешение полного delivery M2.5 на M3 не переносится.

Начать чтение с `baseline.md`, затем `research.md`, `design.md`, `harness-integration.md`, `specs/flights-whole-order-cancellation/spec.md`, `tasks.md`, `review.md`, `process-log.md`.
