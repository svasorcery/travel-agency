# Trips M2: прогулочный маршрут по Москве и Rail-намерение

## Why

M1 сохраняет пожелания и ручной календарь, но ещё не выполняет целевой сценарий: система предлагает содержательный маршрут, владелец редактирует и явно принимает его. M2 добавляет этот сценарий на малом проверяемом покрытии Москвы, не выдавая неизвестные сведения за проверенную выполнимость.

## What Changes

- Москва выбрана пользователем 2026-10-20; остальные решения ниже предложены автором по поручению «остальное сам реши».
- После source research21Oct2026 пользователь выбрал offline OSM+модельные оценки и уточнил demo цель. Первый срез: прогулочные остановки в центре Москвы на 1–3 дня для 1–9 взрослых. Система учитывает pace и поддержанные interests, предлагает порядок остановок и показывает источники/ограничения. M1 сохраняет свои 1–14 дней и остальные города для ручного планирования.
- Детерминированный ограниченный генератор над Wikidata identities и offline OSM-derived modelled walking package. Один bounded build-time helper готовит JSON;runtime по-прежнему читает файл. Модель, Travel.AI, сетевые карты/POI/маршрутизация и платные API не нужны этому срезу.
- Кандидат строится только для сохранённой пустой основы и её точной revision; preview не меняет план. После просмотра кандидат явно переносится в локальный редактор, пользователь редактирует блоки и явно сохраняет обычным M1 PUT/CAS.
- При смене revision/сессии/плана кандидат устаревает; локальные правки не заменяются поздним ответом. Перед переносом обязательны owner GET и отдельный bounded generation-availability GET; смена/отзыв пакета,feature-off или read failure запрещают перенос. Нет regeneration поверх непустого плана, pin/merge, autosave или нового workflow receipts.
- Источники и неизвестные данные видны в preview. После принятия сохраняются обычные пользовательские намерения M1 с ограниченной текстовой ссылкой на исторический набор, без доверенного «verified» статуса. Полноценный постоянный реестр provenance не входит в этот срез.
- Modelled minutes помечены uncalibrated,без ложных observations/human scores. D_demo/source/сквозной demo scenario отделены от будущей D_live/Q_live приёмки реальных прогулок. Exact-byte immutable dataset identity, явная route policy поверх M1 middleware и отдельные data, structural, human-quality и target gates; вымышленные тесты не подтверждают покрытие Москвы. Полный audit/recheck и последующее саморевью плана сохраняются в этом change. Направление географического расширения — design §8,без увеличения текущего Moscow scope.

## Capabilities

### New Capabilities

- `trip-itinerary-candidates`: bounded owner-private system-proposed itinerary, permitted source facts, honest uncertainty, revision-bound preview and explicit adoption into the existing workspace.
- `trip-rail-intentions`: reusable public Rail timetable picker within Trips, explicit fictional train selection into one historical ordinary M1 intention; real-source results remain transient under the published retention contract.

### Modified Capabilities

Нет. `trip-plan-workspace` сохраняет контракт M1: сохранённые блоки остаются намерениями владельца; новые trusted/supplier поля не добавляются в M1 DTO. Дополнительная opt-in capability отдельно определяет генерацию и preview.

## Impact

Предполагаемые места будущей реализации: Core/Application/Infrastructure/Api Trips, текущий Angular Trips editor, shared TS strict client и существующие Trips/architecture/browser tests. Подробная карта — [research](research.md), решения/альтернативы — [design](design.md), требования — [delta spec](specs/trip-itinerary-candidates/spec.md), порядок — [tasks](tasks.md). Последующее [саморевью и исправления](self-review.md) дополняет сохранённый независимый аудит.

Нет нового persistence schema, migration, брокера, worker, AI contract, общего engine/SDK, импортов внутренних типов Flights/Hotels/Rail или booking/payment effects. M3 free-text extraction, Flights/Hotels selection и совместная оптимизация транспорта/POI остаются поздними отдельными capabilities. Подтверждённый Rail consumer описан отдельно ниже; .NET internal imports не добавляются.

Авторизация21Oct2026:source implementation v1в новом fresh-dev worktree,tests и independent audit; затем отдельно выбран offline OSM/modelled demo. Позднее пользователь разрешил публикацию, merge и ordinary post-merge CI. Текущий итог22Oct2026: source/demo, audit/fixes и CI приняты,26/27tasks complete; target task6.3/human/live gates не выполнены. Sync/archive отдельно разрешены без включения. Phase-bound receipts и прежние неудачные попытки сохранены в [verification](verification.md) и [archive verification](archive-verification.md).

Не требуются новый runtime service,DBschema,LLM,paid API,interactive map или universal SDK. Дополнительная сложность сосредоточена в offline подготовке данных,licence/provenance и воспроизводимых модельных estimates. Полевая калибровка/human suitability и real-user/Production rollout остаются отдельными этапами.
## Подтверждённое Rail-уточнение, 2026-10-21

Пользователь разрешил добавить Rail и получить свежий dev, затем выбрал «Выбрать поезд и добавить намерение в план»; обычные детали поручены автору. Это добавочный consumer уже опубликованной Rail capability, без изменения Moscow walking coverage и без второго источника/агрегатора.

Trips использует общий Rail picker, явные source-qualified станции, дату отправления и train/suburban/all. Пользователь отдельно выбирает существующий день плана. Один выбранный fictional рейс переносится только в чистый сохранённый editor как обычное историческое намерение с null time/estimate; затем нужен явный M1 Save. Непустые календари поддержаны до 8 блоков в дне. Walking generator остаётся empty-only.

Публичный Rail contract запрещает durable supplier retention. Поэтому real profile показывает только временное расписание, а автоматический перенос в M1 отключён с объяснением ограничения источника. Fictional demo с явной меткой в сохранённой заметке — полноценный поддержанный OSS/source сценарий. Реальный перенос потребует отдельного пересмотра Rail capability и подтверждения прав; ни toggle, ни стирание части source facts не обходят этот запрет. Real failure никогда не подменяется fictional.

Дизайн и interface/dependency preflight: [Rail refinement](rail-refinement.md), поведенческий контракт: [trip-rail-intentions](specs/trip-rail-intentions/spec.md). Всего27задач,26приняты; включая интеграцию dev/Rail, bound Q_demo, final audit/fixes/recheck и PR/post-merge CI. Frozen12Q+12S inputs и пороги не изменены. Task6.3target/rollout остаётся открытым; sync/archive не разрешают Host/DB/model/provider/enablement.