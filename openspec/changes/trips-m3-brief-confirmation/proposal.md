# Proposal

Status (2026-10-27): source and ordinary PR/post-merge CI accepted through [PR81](https://github.com/svasorcery/travel-agency/pull/81). [Publication receipt](publication.md) records exact identities; [current contract](../../specs/trip-brief-extraction/spec.md) is synced without archiving. Tasks remain22/23: real-model6.3 is pending/not run, and private-text, target and rollout gates remain pending. M3 remains default-off; fake tests do not prove model quality.

## Why

Пользователь уже может вручную заполнить проверяемую форму Trips, но не может начать с привычного описания поездки. M3 предлагает временный разбор пожеланий в поля этой же формы с видимыми пробелами и явным подтверждением, сохраняя ответственность за параметры у пользователя.

## What Changes

- Добавить независимо default-off/Test-only извлечение из короткого русского текста: один город назначения из catalogue v1, даты/длительность, взрослые, интересы и темп. Город отправления, бюджет/валюта, дети, несколько направлений и прочие пожелания не извлекаются в первом срезе; обнаруженные ограничения показываются явно.
- Отделить candidate brief от полей формы, локального календаря, сохранённого плана и существующего itinerary candidate. Никаких автоматических Apply, Create, Save или Generate.
- Различать missing, ambiguous, unsupported, invalid и конфликты; не заполнять их значениями модели или скрытыми defaults. Ограничить относительные даты явным часовым поясом и зафиксированным серверным временем.
- Дать пользователю исправить и явно выбрать переносимые поля; вручную введённые значения, поздние ответы и смена контекста защищены монотонными epoch.
- Использовать существующий процесс Travel.AI, IChatClient и отдельный versioned Core NATS request/reply; не наследовать Flights search action, UTC-today semantics или durable delivery.
- Задать приватность, ограничение размеров/времени/стоимости, отсутствие автоматических повторов и независимую ручную форму. До отправки текста UI читает публичные processor/policy metadata через отдельный authenticated read-only endpoint; запрос и подтверждение привязаны к принятой версии условий.
- Разделить fake contract proof и отдельно разрешённую оценку настоящей модели на заранее фиксированном fictional корпусе. Не объявлять модель, затраты или реальную обработку согласованными.

## Capabilities

### New Capabilities

- `trip-brief-extraction`: временное извлечение, проверка и явный перенос пожеланий в существующую форму Trips, включая bounded inference и отдельную оценку качества.

### Modified Capabilities

Нет. `trip-plan-workspace` уже запрещает считать текст подтверждённым brief и сохраняет отдельные ручные действия. `trip-itinerary-candidates` продолжает исключать извлечение из собственной generation capability; M3 не меняет его endpoints, protocol v1/v2, policy или пакеты.

## Impact

Предлагаемые изменения затронут Trips Application/Infrastructure/Api/Composition, Travel.AI, leaf `Travel.IntegrationContracts.AI`, Angular Trips и строгий API client, focused tests и отдельный fictional evaluation runner. Shared не получает domain rules; Host сохраняет единую endpoint map и глобальную transport policy. Новая схема БД, persistence для текста/candidate, AI core, agent framework и второй планировщик не нужны.

План подготовлен в отдельном чистом worktree от обновлённого `origin/dev=35953fd6c8feabf16acd7fb944201711e3d8c5fa`, ветка `codex/trips-m3-brief-confirmation`. Точное исследование: [research](research.md); решения и ограничения: [design](design.md); статус задач: [tasks](tasks.md); отдельная оценка: [evaluation plan](evaluation-plan.md).

При планировании этот change не давал разрешения на реализацию или model execution; последующая реализация и merge отдельно разрешены и приняты через PR81. M1 validation/owner/revision/CAS/explicit Save, catalogue1, historical snapshots, Москва no-header v1, обе geography/package identities и existing demo/human materials сохраняются. Human A/B, field calibration, target provisioning, original M2 rollout6.3 и Moscow v2 остаются отдельными gates. PR81 смержен с успешным PR/post-merge CI. Реальные вызовы моделей, включение M3 и изменение сохранённого локального demo не выполнялись.
