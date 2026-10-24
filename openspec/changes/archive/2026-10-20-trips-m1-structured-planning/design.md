# Design: Trips M1 и граница следующей генерации

## Context

2026-10-20. Мотивация и согласованная последовательность — в [proposal](proposal.md). Это проект решения для утверждения, не разрешение реализации. Детали ниже являются предлагаемыми решениями автора.

Проверенная отправная точка:

- [Trips](../../../../modules/trips/AGENTS.md) имеет четыре scaffold-проекта, Core marker и пустые unit/integration projects; собственных сценариев и persistence нет.
- [ADR 0001](../../../../docs/adr/0001-modular-monolith.md) и [ADR 0023](../../../../docs/adr/0023-module-api-facades-and-cross-cutting-ownership.md) закрепляют bounded contexts, фасады Composition и владение глобальными builders в Host. Исторический перечень активных модулей в старых ADR не является текущей inventory.
- [Travel.AI](../../../../apps/Travel.AI/AGENTS.md) уже имеет bounded model calls, ledger и versioned contracts. [ADR 0012](../../../../docs/adr/0012-maf-as-primary-agent-runtime.md) откладывает выбор агентного runtime. У Trips нет готового AI-контракта.
- [Концепт](../../../../docs/superpowers/specs/2026-05-03-travel-platform-concept.md) предусматривает trip planning, агентов и возможность разделить основу и генерацию. Его историческая цепочка `Draft → Planned → Booked` не определяет нынешний контракт: наличие плана не доказывает бронирование.
- Rail активно меняет Host/frontend/inventory/CI в другой работе. [Hotels review](../../../../docs/architecture/hotels-completion-review.md) сохраняет default-off поставщиков и отдельные внешние gates. Ни один их pending gate не блокирует независимый M1 Trips.
- [Support store](../../../../modules/support/Travel.Modules.Support.Infrastructure/Persistence/SupportCaseStore.cs), [защита текста](../../../../modules/support/Travel.Modules.Support.Infrastructure/Privacy/SupportTextProtectionProvider.cs) и [shared TS client](../../../../shared/ts/api-client/src/support-api.service.ts) дают локальные примеры owner guards, relational CAS, защиты данных и strict decoders. Trips не импортирует их внутренние реализации.

## Goals / Non-Goals

**Goals:** один приватный план поездки в один выбранный город; структурированные пожелания; предсказуемые календарные дни; ограниченное редактирование; сохранение/reload без потери параллельных правок. M1 строится параллельно с Rail. Область требований — [trip-plan-workspace](specs/trip-plan-workspace/spec.md).

**Non-Goals:** M1 не предлагает содержательный маршрут, не парсит естественный язык, не вызывает AI или поставщиков, не сохраняет их предложения и не управляет заказами. Нет collaborative editing, sharing, детей, нескольких городов, оптимизации дорог, карт, погодных/POI API, фоновых агентов и lifecycle реальной поездки.

## Decisions

### 1. Последовательность и критерий полезного результата

| Этап | Вход → результат | Что принимается |
|---|---|---|
| M1, этот change | Форма → календарная основа → ручные блоки → сохранение | Основа Trips; UI явно сообщает отсутствие автоподбора |
| M2, отдельный change | Подтверждённая форма → предложенный маршрут → правки | Первый законченный целевой сценарий пользователя |
| M3, отдельный change | Свободный текст → проверяемые параметры той же формы | Удобный альтернативный ввод, а не второй планировщик |
| AI service core | Реальные потребители → необходимые общие средства | Оркестрация/инструменты/evals только под подтверждённую потребность |

Альтернатива «сначала парсер и общий agent framework» не даёт проверенного плана и добавляет зависимости. Альтернатива «полноценный ручной travel organizer» превращает промежуточный M1 в отдельный продукт; поэтому M1 ограничен формой, календарём, простыми блоками и persistence.

### 2. Вход M1 и фиксированные пределы

`TripBrief`: выбранный destinationId; optional originId; startDate/endDate как ISO date-only, включительно; adults 1–9; currency RUB/EUR/USD; optional totalBudget как decimal string; interests из `architecture,culture,food,nature,history` без дублей; pace `relaxed|balanced|active`.

- 1–14 календарных дней; даты могут быть прошедшими для просмотра/редактирования истории. Пожелания не обещают доступность услуг для указанного состава.
- Выбор городов из versioned first-party metadata: Москва/Europe-Moscow, Санкт-Петербург/Europe-Moscow и Калининград/Europe-Kaliningrad с реальными IANA IDs `Europe/Moscow`, `Europe/Kaliningrad`. Отображаемое имя не является provider station/airport/property ID. Сетевого геокодинга нет; пользователь не присылает произвольную зону или доверенный статус места.
- Полный список metadata мал и хранится в Trips; расширение списка не создаёт автоматически POI/transport coverage. Названия городов не заменяют проверку источников M2.
- title 1–100 Unicode scalars, block title 1–100, optional note ≤500; до 8 блоков/день и 112/план. Unicode/UTF-8 проверяются одинаково на клиенте и сервере; запрет control characters кроме newline в note.
- Входной JSON ≤128KiB, decoded payload ≤128KiB; invalid fields/unknown properties не отбрасываются молча. Не более 100 активных планов на владельца.
- Денежные значения: неотрицательная строка `0` или integer без leading zeros с optional `.dd` (ровно 2 знака); для единичного budget/estimate ≤`999999999.99`. Сервер нормализует до двух знаков, JS использует integer minor units/BigInt. Ни float, ни FX нет.

Точные числа — ограничивающие предположения автора, не измеренные performance guarantees. Конечный detail/list JSON body также ограничен 128KiB; list page ≤20, metadata ≤10KiB. До commit POST/PUT сервер формирует полный будущий detail DTO той же канонической UTF-8 сериализацией, что GET: server fields, нормализованный текст, destination snapshot, все derived warnings/subtotal/count и резерв для максимальной Int64 revision и timestamp входят в размер. Если граница превышена — `Trips.PlanTooLarge`,400, без записи/смены revision. Body-limit128KiB проверяется независимо до parsing. Проверка после commit недостаточна. Опциональное сокращение/усечение данных запрещено.

List возвращает только bounded summary DTO: TripId, revision, title, destinationId/displayName, dates, updatedAt; без blocks/notes/interests/budget. Суммарный page body≤128KiB. Destination display name и IANA zone фиксируются в protected metadata snapshot при принятии выбора; неизменённый city ID не переинтерпретирует сохранённый план через новое live catalogue metadata. В M1 существующие catalogue IDs/zone не удаляются и не меняют смысл. Изменения detail serialization/schema должны сохранять проверенную читаемость уже принятых snapshots.

### 3. Модель и язык

`TripPlan` — единственный aggregate с `TripId`, trusted `OwnerIdentity`, positive `Revision`, `TripBrief`, title, days/blocks и timestamps. `TripId`/`BlockId` — UUID; identity блока стабильна при переносе. Состояние M1 — редактируемый draft; нет `Booked`, `Confirmed`, `Paid` или `Completed`.

Value objects: `TripDateRange`, `DestinationSelection`, `PartySize`, `PlanningMoney`, `DayLocalTimeRange`, `TripBlockId`. Factories возвращают `ErrorOr<T>`. Domain facts `TripPlanCreated`, `TripPlanRevised`, `TripPlanDeleted` реализуют past-tense `IDomainEvent`; в M1 они не публикуются в broker и не запускают effects. Реализация может оставить их in-memory domain notifications, без event sourcing/outbox.

`TripBlock` — пользовательский title/note, dayDate, optional пара local start/end HH:mm, optional estimate в валюте brief. M1 не имеет typed supplier reference или поля «забронировано». Точное время здесь — намерение путешественника, не расписание поставщика.

Инварианты:

1. Полный набор дней равен каждому date в range, без пропусков/дублей. Каждый block принадлежит ровно одному дню, IDs уникальны во всём плане.
2. Обе границы времени отсутствуют либо обе заданы и end > start в тот же локальный день. Overnight block пользователь делит на два дня. Slot не доказывает travel duration, opening hours или выполнимость пересадки.
3. Пересечения timed blocks разрешены с вычисленным `OverlappingTimes` warning; конфликт нельзя показывать как проверенную совместимость. Смена порядка без времени задаёт только display order.
4. Блоки переносятся пользователем между днями. При смене диапазона совпавшие даты сохраняют IDs/блоки, новые дни пусты; если удаляемые дни непусты, PUT получает `DateRangeWouldDropBlocks` и ничего не меняет. Пользователь сначала явно переносит/удаляет блоки. Нет скрытого удаления или автоматического сдвига по номеру дня.
5. Смена destination или currency при наличии любого блока получает `BriefChangeRequiresEmptyPlan`, без изменения данных. Для M1 пользователь сначала очищает блоки; title, interests, pace, adults и budget менять можно. Origin описательный и не переносит времена. В M2 правила могут стать точнее отдельным spec change.
6. Сумма известных оценок — `knownUserEstimateSubtotal`, неизвестные оценки подсчитываются отдельно. Отсутствие estimate не равно нулю; сумма не является полной ценой поездки. При exceeding budget возвращается warning; без полной стоимости нельзя показывать «в пределах бюджета». Currency mismatch отклоняется, переполнение проверяется до записи.

### 4. Команды, ревизии и ошибки

Предлагаемый HTTP contract:

| Route | Семантика |
|---|---|
| `GET /api/trips/destinations` | Authenticated bounded catalogue metadata |
| `GET /api/trips?cursor=...` | Свои активные планы, updatedAt desc + TripId, pageSize=20 |
| `POST /api/trips` | Создать из brief/title, server-generated TripId, revision=1 и пустые дни |
| `GET /api/trips/{id}` | Owner-bound snapshot и strong ETag |
| `PUT /api/trips/{id}` | Полная bounded замена brief/title/blocks, mandatory If-Match, одна CAS transaction |
| `DELETE /api/trips/{id}` | Mandatory If-Match, атомарное физическое удаление payload/row |

Owner вычисляется из trusted issuer+subject валидированного principal, не из body/query. Все endpoints используют существующую Identity/fallback policy. Trips service не получает токен клиента и не доверяет client ownerId. Missing auth=401; unknown/foreign TripId=404 без payload/decrypt; отсутствующий If-Match=428, malformed=400, stale=412, validation=400, cap/destination-change conflict=409, protection/schema unavailable=503. Error codes — `Trips.*` с RFC7807, без исходного title/note/body.

Write DTO whitelist: POST содержит только brief/title; PUT — только brief/title/days/blocks и их явно перечисленные editable поля. TripId, owner, revision, timestamps, calculated warnings/subtotal и trusted metadata задаёт сервер; присланные в body варианты отклоняются как unknown fields, а не игнорируются. `OwnerIdentity` требует единственного nonempty normalized GUID subject и согласованного trusted issuer; отсутствующие/противоречивые owner claims у authenticated principal дают403 до работы с payload. Это использует существующую Identity normalization, не меняет её правила для других модулей.

Revision — positive Int64, в JSON decimal string; ETag `"<revision>"`; wildcard/list/weak ETags запрещены. No-op PUT сохраняет revision; effective change увеличивает ровно на 1, timestamps берутся из `TimeProvider`. Overflow не wrap: terminal typed conflict без записи. Read+CAS не разделяются незащищённым upsert; DELETE/PUT никогда не создают отсутствующий TripId.

POST — один submit; автоматического retry/resubmit нет. При потере ответа создание неопределённо: UI открывает актуальный список для сверки перед новым явным созданием; source не обещает exactly-once creation или receipt о потерянном ответе. GET/перечитывание списка не создают план. Для PUT timeout UI сохраняет локальный буфер и предлагает перечитать сервер; GET показывает текущее состояние, а не доказательство конкретной команды. Последующий save — явное решение на новой revision. При DELETE uncertainty разрешено перечитать список/detail; отсутствие доказывает только текущее отсутствие. Все действия M1 локальны и обратимы до удаления, без supplier/money effects.

Cursor opaque, bounded ≤256 chars, owner-scoped, валидируется; сортировка stable tie-break. Список не обещает snapshot isolation через параллельные обновления: возможны повтор/смещение строки между страницами, UI dedups по TripId и предлагает refresh. Первая страница после save/delete перечитывается.

#### 4.1. Примеры общего HTTP контракта

Это предложенный M1 wire contract для одновременной реализации Api и TS client. Локальные city IDs: `moscow`, `saint-petersburg`, `kaliningrad`; им соответствуют metadata из §2. Значения UUID, дат и времени ниже — вымышленные примеры, не созданные записи.

POST принимает только `title` и `brief`. В brief optional `originId`/`totalBudget` отсутствуют или равны null; ответ нормализует их в null. Остальные поля обязательны; interests может быть пустым массивом. Пример `POST /api/trips`:

```json
{
  "title": "Выходной в Петербурге",
  "brief": {
    "destinationId": "saint-petersburg", "originId": "moscow",
    "startDate": "2026-11-14", "endDate": "2026-11-14", "adults": 2,
    "currency": "RUB", "totalBudget": "15000.00",
    "interests": ["architecture", "food"], "pace": "relaxed"
  }
}
```

Успешный POST возвращает201, Location своего detail route, ETag `"1"` и полный detail описанной ниже формы с revision1, пустыми blocks, subtotal `"0.00"`, unknown count0 и пустыми warnings. Полный PUT содержит `title`, `brief` и `days`; каждый день — `{date, blocks}`. Набор дней полный и date-sorted; block принадлежит дате своего контейнера, отдельного dayDate у него в wire нет. Display order блоков — порядок массива, без параллельного position/order поля. Новый blockId генерирует клиент как UUID; при редактировании/переносе тот же ID сохраняется, сервер проверяет уникальность в плане, но не доверяет ID как подтверждению владения.

Пример `PUT /api/trips/11111111-1111-4111-8111-111111111111` с `If-Match: "1"`:

```json
{
  "title": "Выходной в Петербурге",
  "brief": {
    "destinationId": "saint-petersburg", "originId": "moscow",
    "startDate": "2026-11-14", "endDate": "2026-11-14", "adults": 2,
    "currency": "RUB", "totalBudget": "15000.00",
    "interests": ["architecture", "food"], "pace": "relaxed"
  },
  "days": [{"date": "2026-11-14", "blocks": [{
    "blockId": "22222222-2222-4222-8222-222222222222",
    "title": "Прогулка", "note": null,
    "startTime": "10:00", "endTime": "11:30",
    "estimate": {"amount": "500.00", "currency": "RUB"}
  }]}]
}
```

`note`, time pair и estimate отсутствуют или null; canonical detail использует явные null. Ненулевой estimate — только `{amount,currency}`, не supplier price. Обе time границы должны быть заданы совместно. Известные block IDs не заменяются при reorder, день переноса отражается контейнером. Read-only server fields нельзя копировать из detail в PUT; TS строит write DTO по whitelist, а не spread всего ответа.

PUT возвращает200 с принятым detail/ETag; следующий GET возвращает ту же сохранённую версию. Пример ETag `"2"` и detail после изменения:

```json
{
  "tripId": "11111111-1111-4111-8111-111111111111", "revision": "2",
  "title": "Выходной в Петербурге",
  "brief": {
    "destinationId": "saint-petersburg", "originId": "moscow",
    "startDate": "2026-11-14", "endDate": "2026-11-14", "adults": 2,
    "currency": "RUB", "totalBudget": "15000.00",
    "interests": ["architecture", "food"], "pace": "relaxed"
  },
  "destination": {
    "destinationId": "saint-petersburg", "displayName": "Санкт-Петербург",
    "timeZone": "Europe/Moscow", "catalogueVersion": "1"
  },
  "createdAt": "2026-10-20T09:00:00Z", "updatedAt": "2026-10-20T09:05:00Z",
  "days": [{"date": "2026-11-14", "blocks": [{
    "blockId": "22222222-2222-4222-8222-222222222222",
    "title": "Прогулка", "note": null,
    "startTime": "10:00", "endTime": "11:30",
    "estimate": {"amount": "500.00", "currency": "RUB"}
  }]}],
  "knownUserEstimateSubtotal": "500.00", "unknownEstimateCount": 0,
  "warnings": []
}
```

Subtotal выражен в brief.currency; unknown count относится только к созданным блокам. Даже count0 и subtotal ниже budget не означают полную стоимость поездки: пользователь мог не добавить дорогу/проживание. Warnings — детерминированный массив уникальных кодов `OverlappingTimes`/`KnownEstimateExceedsBudget`, без утверждения выполнимости. DELETE с текущим If-Match возвращает204 без detail; после него GET404. Summary page не включает blocks или эти cost поля. Даты/time/revision/money сохраняют строковые типы. Эти примеры должны стать общими contract vectors HTTP/TS tests, а не вторым schema corpus.

### 5. Persistence, privacy и runtime ownership

EF Core в Infrastructure, schema `trips`, одна строка на план: TripId PK, trusted owner identity, revision, createdAt/updatedAt и protected JSON payload. Plaintext payload содержит весь brief/title/day/block; БД не хранит raw notes, destination, interests или budget в отдельных индексах. Owner+updatedAt+TripId index для list. Aggregate limit=100 проверяется с owner-scoped DB serialization/lock, а не только count-before-insert. Read/list/decrypt bounded, foreign guard before decrypt.

Для защиты — framework Data Protection с Trips-owned purpose и собственным configured key ring/certificate, по существующему pattern без импорта Support/Flights internals и без создания общего privacy framework. Binding включает owner/TripId/schemaVersion/revision; revision и payload обновляются атомарно. Ciphertext нельзя переносить между владельцами/планами/ревизиями. List расшифровывает до20результатов; повреждение одного payload не выдаётся за empty list:503 typed unavailable без частичных приватных данных. Защищённый JSON не используется как auth truth.

Trips feature default-off. Disabled не регистрирует runtime DB/key dependencies и не делает scaffold рабочим модулем. Enabled включает Api.Composition и отдельную readiness schema/protection; обычный startup не создаёт схему/пустой key ring. Provisioning/migrations/keys — отдельный допуск, включая disposable local Host/DB. Existing platform initialization pattern сохраняется; не добавляем новый scheduler/queue/service.

Private endpoints и ошибки: Cache-Control no-store; auth/body/error checks не сохраняют личные данные в логах, traces или URLs. Список через query имеет только bounded cursor, без brief. Angular не пишет планы/буфер в localStorage/sessionStorage/IndexedDB/service worker/SSR HTML; при logout/owner change очищает in-memory данные, aborts requests и не принимает поздний ответ старого owner. UI рендерит plain text, без HTML/URL execution. Диагностика содержит outcome/duration/count, без owner/trip ID high-cardinality labels или содержимого.

Клиентские планы не шарятся; operator bypass отсутствует. Payload хранится до явного удаления владельцем; автоматической retention job нет в M1. После DELETE payload больше недоступен через API; физическая история backup не считается мгновенно стёртой и следует существующей политике backup. Это не обещание удаления копий во внешних системах.

### 6. UI и модульная интеграция

Lazy Angular `/trips` и `/trips/:id`; существующий auth flow. Форма → явное «Создать основу плана» → календарь → добавление/редактирование/перенос/удаление блока → явное «Сохранить». Dates, money и validation отображаются без потерь. Кнопки и errors доступны с клавиатуры, labels связаны с controls, conflicts/status имеют live-region, focus возвращается к исправляемому полю.

Server snapshot и local dirty buffer разделены. Editor tracks owner/open-plan epochs, latest read request, local edit version и highest accepted server revision. Каждый запрос захватывает свои epochs; mutation также submitted buffer version. Ответы для прежних epochs или revision ниже уже принятой отбрасываются. GET при появившихся после dispatch dirty edits обновляет только comparison snapshot; он не заменяет локальный buffer и не помечает его clean. Во время save дальнейшее редактирование разрешено: успешный ответ принимает только отправленный snapshot и revision; более новый локальный buffer остаётся dirty. Чистым становится только неизменённый со времени submit buffer с принятым результатом. Следующий save явный, на полученной revision; foreign revision/conflict требует сверки, без скрытого merge.

Перед сменой плана/маршрута предупреждать о несохранённых изменениях. Logout очищает приватный buffer даже при dirty, без переноса между пользователями. Подтверждённый DELETE меняет open-plan epoch и очищает editor, поэтому поздний GET/save не восстанавливает удалённый план. Редактирование и другие mutations на время DELETE блокируются после явного подтверждения отказа от dirty edits. Только одна mutation плана pending; timeout не возобновляет POST/PUT/DELETE автоматически. Loading/empty/unauthenticated/unavailable/conflict/uncertain сохраняют разные states.

Core — правила; Application — use cases/ports, owner/revision orchestration; Infrastructure — EF/protection; Api — DTO/validation/HTTP/Composition. Host ссылается только на Trips.Api и сохраняет один map/глобальные builders. Shared TS client получает только реально используемые M1 DTO/service/strict decoders. DTO/decimal/date/revision/output bounds проверяются обеими сторонами.

Во время разработки M1 separate worktree/branch создаётся только по применимому поручению, с проверкой ancestry запрошенной базы. Эта planning-сессия не переключает текущую Rail ветку. До интеграции Rail согласовать собственника Host project/Program, web routes/navigation, API-client index, architecture inventory, CI inventory; разрешать конфликты на актуальной принятой базе без ослабления tests. Docker-heavy suites последовательно на общем daemon; в CI — существующее разнесение по runner.

### 7. Граница M2/M3, которая сохраняет продуктовую цель

M2 нельзя объявлять готовым на основании формы или template дней. Отдельный proposal фиксирует один пилотный город, источники POI/opening hours/travel facts, права/стоимость/обновление, model profile и критерии качества. При планировании M2 первым выбором для малого покрытия должен быть проверенный first-party/разрешённый curated набор; окончательный источник и его объём не определены этим M1. Fictional fixtures доказывают код, но не покрытие реального города.

Future request boundary: normalized brief + accepted revision + явно разрешённые source facts → candidate с provenance/unknowns. AI не получает доступ к Host DB, токенам, PII пассажиров или mutation tools. Trips валидирует структурный кандидат и сохраняет его только по явному действию пользователя. Изменение accepted revision делает кандидат устаревшим; автоматического merge поверх ручных правок нет. Закрепление blocks — поведение M2, поэтому не добавляем бессмысленное поле lock без потребителя в M1.

Разделяем качество: структура и отсутствие выдуманных fact IDs проверяются кодом; пригодность маршрута/темп/соответствие интересам оцениваются отдельно на заранее зафиксированных сценариях, включая человеческий review. AI не придумывает цены, наличие, часы работы или длительность дороги. Неизвестное не превращается в feasible/within-budget. Нет автоматически принятого ограничения стоимости/провайдера/модели; текущая работа не вызывает модели.

Подключение Flights/Hotels/Rail — последующие явные capabilities: read-only подбор, provenance+expiry, owner-bound подтверждённый заказ отдельно от выбранного варианта; каждое соединение требует своего published contract. Trips не персистит raw Rail responses, не продлевает source TTL и не переносит supplier default-off permissions. План не запускает purchase/cancel/refund.

M3 выдаёт candidate brief для той же формы, с явными missing/ambiguous fields и подтверждением владельца. Парсер не меняет принятую поездку и не запускает скрытое повторное планирование. Общий AI core не является предварительным условием: сначала consumer-specific bounded contracts; framework выбирается только при доказанной необходимости.

## Risks / Trade-offs

- M1 ещё не выполняет автоподбор → отмечаем его как промежуточную основу; первым завершённым целевым сценарием считаем M2.
- Календарь без реального POI/travel источника выглядит убедительнее доказательств → M1 не показывает «маршрут готов», M2 имеет отдельные data/quality gates.
- Full-snapshot PUT ограничивает гибкость → небольшой bounded aggregate и одна CAS проще patch merge; позднее patch API вводится при retained consumer.
- Encryption/key provisioning увеличивает объём M1 → приватные пожелания защищены; при недоступности protection fail closed; чужие ключи не переиспользуются.
- Lost POST может потребовать проверки списка и явного нового создания → не обещаем exactly-once и не строим отдельный receipt framework для локального черновика.
- Strict empty-plan rule при смене города/валюты менее удобен → исключает скрытый перенос времени/денег; M2 может предложить подтверждаемый replan.
- Общие integration files конфликтуют с Rail → отдельные владельцы и последовательная интеграция; основной код Trips не зависит от текущего Rail head.

## Migration Plan

Только после утверждения design и отдельного implementation scope подготовить additive source migration для `trips`, SQL review и rollback notes. Генерация migration требует явной применимой авторизации, применение не следует из source approval. Никаких Host/DB/schema/keys/model действий сейчас.

Default-off revision может быть поставлена без включения Trips. Перед включением: readiness schema/key ring, auth/private HTTP acceptance и rollback plan. Rollback выключает capability; не удаляет пользовательские планы и не откатывает таблицы автоматически. Destructive down после появления данных — отдельное решение.

## Verification and acceptance

M1: все сценарии delta spec; Core invariants/date/money, Application owner/CAS/limits, relational atomicity and owner-race, HTTP auth/headers/size/status/no-store/no-leak, TS decoder/Angular editor/logout/late replies, architecture positive+negative selectors, fictional E2E create→edit→reload→conflict→delete. Targeted source checks сначала; Docker/Host fixtures только в применимом разрешённом disposable contour; ordinary CI без paid AI jobs и coverage bypass. Полный denominator/skips и exact revision/run сохраняются отдельно от assertion «source готов».

Планирование: CLI strict validation, относительные links, requirement/scenario uniqueness, task traceability, независимый дизайн-аудит до tasks, личная сверка его исправлений. Ни code tests, ни AI evaluation этой документационной работой не заявляются пройденными.
