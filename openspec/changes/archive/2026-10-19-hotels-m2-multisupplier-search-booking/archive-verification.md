# M2 archive acceptance — 2026-10-19

Согласованный M2 завершён:25/25 задач, отдельный исполнитель `gpt-6.1-sol`, личная проверка основным агентом, автономный browser и отдельно разрешённая local PostgreSQL/actual Host/privacy приёмка. Исторические промежуточные receipts остаются в [verification](verification.md), [implementation report](implementation-report.md) и [relational verification](relational-verification.md).

## Публикация

- [PR55](https://github.com/svasorcery/travel-agency/pull/55), source `a7f67b0070b194e02b85c68d5d4a6370da05f9b6`, merge `5a0fb7a36de6101170c4b676415e616890b4bfa6` в `dev`.
- [PR CI39257646397](https://github.com/svasorcery/travel-agency/actions/runs/39257646397), attempt1:17 обязательных jobs passed, включая actual E2E2/2; paid AI job skipped.
- [Post-merge CI39260364791](https://github.com/svasorcery/travel-agency/actions/runs/39260364791), attempt1:16 обычных jobs passed. PR-only E2E и paid AI skipped по workflow.
- Personally inspected CI: Hotels unit79/79, integration125/125, Host HTTP485/485, Host integration115/115, architecture183/183; post-merge integration125/125 и Host115/115, все без skipped tests.

## Синхронизация

M1 сначала архивирован без повторного sync, поскольку его main baseline уже полностью совпадал. Затем все4 M2 deltas перенесены в main:18 ADDED requirements и6 MODIFIED. Существующие Purpose и неупомянутые требования сохранены, ни один прежний сценарий изменённого требования не потерян. Новые matching/ranking Purpose перенесены из утверждённых deltas. Проверка12 main specs прошла строго, без ошибок.

Текущие Hotels contracts содержат51 требование/136 сценариев в5 main specs, включая неизменённый общий Duffel webhook contract. [Completion review](../../../../docs/architecture/hotels-completion-review.md) — единая текущая карта. Архивные deltas сохраняются как история; их не нужно снова применять к main. `.openspec.yaml` и нормативные блоки дельт сохранены; исправлены относительные ссылки и устаревшие статусные абзацы planning artifacts.

Исходные красные проверки, включая stale OpenAPI snapshot и последующий проверенный repeat, не объявляются зелёными задним числом. Сохранённые source freezes относятся к своим фазам; новые doc-only edits их не переписывают. Исходное ограничение M1 L1 **16/17**, `workingSetRecovery=false`, `passed=false`, exit1 остаётся failed и не переносится в M2 как waiver.

Source/local/CI acceptance не устанавливает реальных прав доступа, mapping/comparison permission, mTLS/account certification, load/performance или target readiness. Реальные supplier/paid API, новые migration files, target schema/keys и deploy не выполнялись. Архивирование и документационный PR не меняют код или default-off flags.
