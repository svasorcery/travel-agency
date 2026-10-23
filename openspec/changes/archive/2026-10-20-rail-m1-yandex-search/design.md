# Rail M1: Yandex.Rasp

## Context

Мотивация и продуктовая граница: [proposal.md](proposal.md). План утверждён для source/offline implementation. Реализованный source, локальные проверки и отдельные pending gates записаны в [verification.md](verification.md); actual Host/CI, measured и live acceptance не следуют из реализации.

На `799e31a6b31ff256ba017861cd911e547e4e9896` Rail имеет четыре проекта, marker и пустые Unit/Integration проекты. Host его не импортирует. Hotels завершён по source/local/ordinary CI, но исходный M1 L1 остаётся 16/17, `workingSetRecovery=false`, `passed=false`, exit1. Это не waiver Rail. Laya/AI не нужны этому поиску.

### Основания и статус решений

**Выбрано пользователем:** РФ; единственный источник Yandex.Rasp; собственный интерфейс; отображение доступных именно этому API функций вместо обещания отдельного inventory API; без бронирования. Ошибочный выбор Швейцарии отменён пользователем.

**Предложенные границы M1:** прямые поездки, оба вида rail transport; выбор конкретных станций; краткие сведения билета/цены; без пересадок, сложных multi-leg, табло станции, карты/геокодинга, полного списка промежуточных остановок и AI. Это первый срез, а не заявленная поддержка всех методов Yandex.Rasp. Численные настройки ниже — решения автора проекта, подлежащие общей проверке спецификации, а не отдельное согласие пользователя на каждый лимит.

**Подтверждено официальными страницами 2026-10-19; без supplier calls:**

| Источник | Факт и следствие |
|---|---|
| [Search reference](https://yandex.ru/dev/rasp/doc/ru/reference/schedule-point-point) | Поиск имеет station/date/transport/transfers и pagination. Ответ содержит расписание и необязательные билетные сведения. Предпочтительный опубликованный host — `api.rasp.yandex-net.ru`. Название `places` не является полем seat count. |
| [Station list](https://yandex.ru/dev/rasp/doc/ru/reference/stations-list) | Полный географически вложенный каталог около40МБ; country code `l225` обозначает РФ. Параметра station-name autocomplete здесь нет. |
| [Copyright reference](https://yandex.ru/dev/rasp/doc/ru/reference/query-copyright) | Документация просит разместить баннер, уведомительный текст и URL в этом порядке. Опубликованный баннер является iframe; это отдельный блок атрибуции, а не готовый экран поиска. Одной статической подписи недостаточно для выбранного консервативного соблюдения опубликованных требований. |
| [Access](https://yandex.ru/dev/rasp/doc/ru/concepts/access) | Нужен API key; можно передавать его в Authorization. |
| [Product](https://yandex.ru/dev/rasp/) / [Terms](https://yandex.ru/legal/timetable_api/ru/) | API представлен как бесплатный; стандартное использование требует бесплатного открытого продукта и атрибуции. П.3.2.4 ограничивает сохранение/обработку, кроме временного кэширования в рамках функциональности сервиса. Постоянная нормализованная БД не становится разрешённой благодаря смене формата. |

Точная квота, пригодность ключа/условий для конкретного проекта и железнодорожная полнота билетных полей **не установлены**. Search reference описывает `start_date` только как дату отправления рейса; устойчивость run identity по этой дате и её отношение к первому отправлению всей нитки не определены. Майские500/day, TTL1h и предположение о постоянном нормализованном storage не принимаются за действующие разрешения. Синтетические fixtures проверяют заявленный протокол, а не фактическую полноту supplier data. Примеры в документации местами противоречат таблицам (локализованные enums, offsetless время, несогласованные даты); они не заменяют нормативное описание и live shape acceptance. Источники DB/GTFS, исследованные в чате, не входят в этот change.

### Существующие компоненты

- ADR0006: семь уровней тестирования; ADR0008: `ErrorOr<T>` для ожидаемых исходов; ADR0009/0023: Wolverine endpoints, одна process composition, `Api.Composition` facade и provider-owned HTTP policy.
- `apps/Travel.ServiceDefaults` уже предоставляет `TimeProvider`, ProblemDetails и HTTP telemetry/redaction; `shared/dotnet/Travel.Shared.Web` — web helpers. Не создавать второй global resilience pipeline.
- `HotelReadCoordinator`, `HotelSnapshotCache` и `HotelAggregation` — образцы ограничений/отмены/исходов; `SearchFlightsHandler`/`SearchCacheKey` — образцы capability-bound cache. Они module-internal и не импортируются в Rail.
- Flights `DuffelAirportTimeResolver`/ADR0025 демонстрируют отказ от host-zone fallback. Yandex wire timestamps с offset имеют другой контракт: не переносить airport resolver и не требовать отсутствующий в ответе IANA ID.
- Existing Hotels fictional HTTP tests, Host fixtures/OpenAPI, TS decoders и Playwright показывают подходящие точки проверки. Shared PostgreSQL fixture пригодится только если появится действительный DB consumer; здесь его нет.

## Goals / Non-Goals

**Goals:** один небольшой законченный read-only поиск, где происхождение, неполнота, даты и неизвестные билетные сведения сохраняются от adapter до экрана; запуск обычных source tests без ключей и сети.

**Non-Goals:** booking/payment/hold, inventory/seat map, подтверждение sold out/неоткрытой продажи, отдельный ticket supplier, агент, общая transport/search платформа, cross-source matching, permanent data collection, streaming updates, load run или live rollout в рамках проектирования.

## Decisions

### 1. Границы слоёв и модель

```text
Angular /rail --> Rail.Api endpoints --> Rail.Application queries/ports
                                           |
                                           v
                                       Rail.Core
Rail.Infrastructure --> implements Application ports; owns Yandex wire + mapping
Host --> Rail.Api.Composition only (after approved implementation milestone)
```

Core содержит небольшие immutable values: `StationRef(source, opaqueCode)`, `RailStation`, критерии, конкретный `ScheduledJourney`, `TicketInformation`, `SourceEvidence`, типизированные outcomes. Value factories возвращают `ErrorOr<T>`. Никаких aggregates/events только ради DDD. Application владеет ports выбора станций и поиска, orchestration, admission и временными snapshot contracts. Infrastructure владеет typed HTTP client, DTO/stream parser, parsing/date/money mapping и cache implementation. Api преобразует нормализованный результат в HTTP contract. Shared добавления не нужны.

Provider ID один: `yandex-rasp`; `fictional` — transport profile, не provider. Два transport subqueries одного источника не считаются двумя поставщиками. Референсы сохраняются как непрозрачные source-qualified значения; перенос названий wire DTO наружу запрещён.

### 2. Выбор станций без внешнего геокодинга

`GET /api/rail/stations?q=...`:3–80 символов, максимум20 подсказок, plain text названия станции/населённого пункта/региона; искать по имени станции или населённого пункта, показывать реальные отдельные станции. Одноимённые станции не объединяются. Возвращать generation временного каталога. Поиск A→B требует refs из актуальной generation; station и settlement не взаимозаменяемы. Любой произвольный код, город вместо станции или неизвестная generation отклоняются до supplier search.

Для валидного q ответ200 имеет нормализованный envelope: `kind=complete|limited|skipped|failed`, `reasons`, `suggestions`, `hasMore`, `catalogueGeneration`, `receivedAt/expiresAt`, source/profile/attribution. Успешный scan без совпадений — complete с пустым массивом; более20 совпадений — limited/hasMore. Дополнительная причина limited — неполнота распознанной RF passenger-station subset. Выключенный source/недопущенный transport/local quota — skipped; loader timeout/invalid/oversize, failed-refresh cooldown или недоступная атрибуция — failed с разными reasons, generation=null. Они никогда не выглядят как «станции не найдены». Невалидный q —400; весь autocomplete request, включая cold-load waiters, занимает один из2 permits без очереди, overflow —429 до подписки на single-flight. Это не только лимит CPU scans. При смене/истечении generation UI инвалидирует оба выбранных refs и просит выбрать обе станции заново; search возвращает400 `Rail.StationSelection.Expired`, не выполняя supplier HTTP. Одна станция из новой generation не связывается с другой из старой.

На первом подходящем запросе один single-flight loader формирует временный reference bundle: GET `/v3.0/copyright/?format=json`, затем GET `/v3.0/stations_list/?format=json&lang=ru_RU`, оба с Authorization и отдельным quota debit. На cold station flow максимум2 API calls; POST search их скрытно не выполняет. Общий deadline bundle20s; copyright child≤5s, каталог использует остаток общего бюджета. Оба ресурса и source profile публикуются атомарно в одной generation, TTL≤30min от более раннего receivedAt. Startup/background загрузок нет.

В каталоге требуются `countries` и ветка РФ `l225` с хотя бы одной пригодной станцией. Canonical transport `train|suburban` и passenger types `station|train_station|platform|stop` имеют закрытый mapping; совместимость с русскими представлениями из описания/примеров (`Поезд`, `Электричка`, `станция`, `вокзал`, `платформа`, `остановочный пункт`) — явное решение адаптера, не обещание такого wire shape. Известные не-rail/non-passenger records исключаются. Нераспознанный RF transport/passenger type даёт catalogue coverage limited и причину, а не ложную полную выдачу; при0 пригодных станций каталог unavailable. Противоречащие duplicate station IDs делают каталог непригодным; одинаковые IDs/содержимое сворачиваются. Transit kind станции не доказывает наличия обоих видов поездов и не ограничивает разрешённый search mode.

Bundle — только RAM, без disk/Redis/browser storage и sliding TTL. Каталог читается потоково, decoded body≤64MiB, depth≤32, token≤16KiB,≤100000 сохранённых stations и≤48MiB суммарного UTF-8 payload snapshot. Copyright body≤128KiB, normalized attribution≤4KiB. Не собирать40МБ в string/DOM/raw copy. Structural/byte/count overflow отказывает целиком, а не публикует усечённый snapshot. Max2 snapshots включает все leased старые generations и candidate: при expiry новый loader не запускается поверх ещё leased старого snapshot; завершить/отменить старые scans, затем освободить его. Search копирует только2 station values, не держит весь каталог. Перед возвратом scan повторно проверяются generation/expiry/profile epoch. Истёкший bundle не используется; failed refresh имеет30s cooldown, без повторов в том же запросе. Нет неограниченного списка retired generations.

У каждого из максимум2 waiters своя отмена; она не отменяет чтение другого. Когда отменились все, loader отменяется и помечается непригодным для новых подписок; его permit освобождается после фактического завершения. Поздний loader не публикует candidate/cache. Новый caller при незавершённом отменённом loader получает unavailable/loading-cancelled без создания новой задачи. Quota debit и concurrency permits различаются: debit после dispatch не возвращается при timeout/cancel.

Каталог не содержит обязательной timezone: отсутствие IANA ID не заполняется по host, названию станции или offset. Source catalogue/RAM shape и объём проверяются отдельно;48MiB — logical payload bound, не обещание такой же RSS процесса.

### 3. Поиск, полнота и API

`POST /api/rail/search`: station refs, generation, `departureDate` как строгая YYYY-MM-DD, `transport=train|suburban|all`. Оба конца — выбранные RF rail stations, разные; date picker не подставляет host/browser date как дату станции. Разрешён интервал UTC-date(now)−1 … UTC-date(now)+30; это локальный продуктовый бюджет, не обещание booking window Яндекса. `TimeProvider` применяется ко всем часам/TTL.

Для `train`/`suburban` делать по одному `/v3.0/search/` с `system=yandex`, `lang=ru_RU`, `transfers=false`, обязательной date, `offset=0`, `limit=50`; `result_timezone` не задавать. `all` запускает эти два GET параллельно. Итого максимум2 supplier calls,100 journeys; без автоматического paging, retries, per-card enrichment и thread calls. UI явно говорит об ограниченной выдаче при наличии следующих страниц; порядок — отправление instant, прибытие instant, source run key, внутри полученной части, без обещания «самых ранних во всём расписании».

До обработки rows, в том числе пустых, обязательный `search` echo проверяется на from/to code+type=station и date, точно равные admitted query. Отсутствующий/чужой echo делает mode failed и не попадает в cache. Обязательны object envelope, `segments` array и nonnegative integer pagination с offset0/limit50; `interval_segments` может отсутствовать(тогда[]), но не иметь иного типа. Для scheduled rows n≤50 и total≥n. Если interval rows нет, total=n означает исчерпанную страницу, total>n — limited; dedup не меняет исходный n. Связь interval count с total официально не уточнена: не требовать произвольного равенства суммы массивов. Любые interval rows дают limited/interval-service-unsupported и не становятся точным departure. Их обязательные route/mode refs тоже проверяются.

Envelope: admitted request, source/profile epoch, mode outcomes, journeys, coverage/reasons, receivedAt/expiresAt, attribution. Коллекции≤100 journeys,≤32 ticket types/journey,≤20 suggestions; normalized public response≤4MiB. Превышение всего public payload даёт200 failed/normalization-limit,0 items, без truncated complete/cache; per-ticket bound имеет отдельную limited семантику решения5. `complete-empty` только после echo и успешного исчерпания всех выбранных modes с0 scheduled/interval rows. Public `kind` ровно `complete|limited|skipped|failed`; empty — complete+items[], unsupported — skipped+reason, без конкурирующих enum aliases. Failed-only envelope без данных может иметь attribution=null; неисправная атрибуция не разрешает показывать source facts.

Public mode statuses — ровно множество выбранных train/suburban без повторов/лишнего source ID; каждый journey принадлежит своему usable mode, failed/skipped mode не содержит journeys. В mode evidence отдельно original supplier count и normalized count после dedup; root normalized count равен items.length. Этот же invariant проверяет TS decoder, включая source/profile/generation/request echo; mismatched envelope отклоняется. Null receivedAt/expiry допустимы для skipped/failed без source data; complete и limited с source evidence (включая successful empty mode) имеют непрошедшую absolute expiry. Failure/normalization-limit может иметь responseReceivedAt диагностики, если ответ действительно получен; DNS/transport failure до ответа не выдумывает supplier timestamp. AttemptedAt/completedAt — собственные времена выполнения, не freshness.

Каждый mode/journey/price сохраняет свой receivedAt. Envelope receivedAt — самый ранний из included mode timestamps, completedAt — время сборки ответа; cache hit не меняет их. Проверка порядков: client shape→current enablement→refs/generation→request admission/cache→source quota/HTTP. Runtime profile epoch строится самим процессом при любых изменениях источника/credentials/permissions/config; caller не задаёт его вручную. Перед dispatch, публикацией и cache write epoch проверяется повторно. Отключение во время работы отменяет её; старые данные не публикуются.

| Mode/source outcome | Публичный результат |
|---|---|
| Все выбранные modes complete | complete;0 journeys — complete-empty |
| Хотя бы один mode complete/limited, другой failed/skipped, или есть truncation/interval-service | partial/limited, в том числе с0 journeys |
| Все выбранные modes намеренно не запущены | unsupported/skipped + причины |
| Нет usable mode, хотя бы один действительно failed | failure; это не отсутствие поездов |

Неверный клиентский запрос —400 ProblemDetails. Допустимый запрос возвращает200 typed execution envelope, включая failed/limited/skipped. Переполнение собственного admission —429 ProblemDetails; инфраструктурная необработанная ошибка — platform5xx. Supplier401/403/429, transport/timeout, malformed/oversize body — различимые безопасные причины failure; не отдавать raw errors и key. Requested caller cancellation не маскируется под provider failure.

### 4. Идентичность, время и соответствие запросу

StationRef не является canonical global station. ESR/Express/названия не используются для matching. `thread.uid` — reference нитки, а не доказанный глобальный ID физического рейса. Variant identity: source, opaque thread UID, transport, from/to, departure/arrival instants. Она намеренно snapshot-local; одинаковый номер поезда/UID в разные даты не схлопывается, изменение времени создаёт новый вариант. `start_date` — optional source-reported date, не обязательный ключ и не доказательство начала полного рейса; не проверять выдуманное отношение к boarding date. Отсутствие/невалидность этого необязательного поля не ломает расписание, invalid metadata reason сохраняется. Точные повторы variant сворачиваются только при совпадении normalized обязательных/билетных фактов; conflicting duplicate mode failed, никакого смешения разных цен/доказательств.

Требуются совпадающие endpoint station IDs/type=station, `thread.transport_type` выбранного mode, `has_transfers=false`, offset-local departure date=query date и оба ISO timestamps с явным offset. Разрешены только полные секунды и допустимый ISO offset; UTC chronology положительная, source duration — конечное положительное число секунд, точно равное разности instants. Номер/название, carrier, platform и stops summary необязательны, отсутствие не создаёт invented value. Невалидный required record отклоняет mode; другой успешный mode сохраняется limited. Явные duplicate JSON properties в identity/echo/pagination/price/indicator не принимаются по last-wins; неизвестные дополнительные поля игнорируются в рамках byte/depth bounds и не исполняются.

В Core/API сохранять instants и предоставленные offsets; UI показывает местные дату/время каждого конца и UTC±offset, переход даты явно. Не изобретать IANA ID: null означает «источник не предоставил». Offsetless timestamp, offset вне допустимого ISO диапазона и противоречащая chronology — failed mapping. Никакого UTC/Moscow/host fallback. IANA zone допускается только при появлении документированного собственного source evidence; тогда её согласованность с offset проверяется в Infrastructure. Плановые времена никогда не помечаются actual/predicted. receivedAt — время нашего получения, sourceUpdatedAt остаётся unknown, если не передано.

### 5. Цена и билетные сведения входят в M1

Нормализованная модель разделяет:

- `schedule`: плановые факты и отдельная полнота поиска;
- `electronicTicket`: `true|false|unknown` и собственная причина unknown;
- `fares`: `not-provided|reported|limited|unavailable`, список ticket types с индивидуальными `reported|not-provided|unavailable` price states и reasons;
- `seatAvailability`: всегда `unknown` для принятого Yandex contract; нет count/seat map;
- `operationalUpdates`: `not-provided` в M1, без вывода «вовремя».

`places` — список типов, не места в вагоне. `et_marker=false` описывает электронный билет, не sold out/закрытие продажи. Отсутствующее/null `tickets_info` даёт fares not-provided и indicator unknown. Пустой places не означает sold out. Независимые поля валидируются независимо: invalid marker не стирает валидную цену; invalid цена не стирает marker/название типа/другие валидные цены. Если весь ticket block/places имеет неверный структурный тип, цены unavailable; если это object с исправным indicator, он сохраняется. Один невалидный fare даёт fares limited с безопасным reason, его собственная цена unavailable. Свыше32 типов сохраняются первые32 с fares limited/output-limit. Schedule completeness от optional failures не меняется; UI отдельно показывает билетную неполноту, не выдумывая сумму.

Цена передаётся только при наличии обоих integral компонентов: `whole`≥0, `cents`0..99, checked exact decimal и известной двухразрядной валюты. Поддержанная денежная subset M1 — RUB/USD/EUR (решение автора для RF поиска); другая валюта — price unavailable/unsupported-currency, без догадки о числе minor units и без FX. Явные целые JSON числа вроде1234.0 математически допустимы, fractional1234.5/NaN/overflow — нет. Null price или отсутствующий компонент — not-provided/incomplete-price, а не ноль. Наружу decimal string с двумя дробными цифрами/оригинальная валюта; valid0 требует обоих нулей. Null name остаётся неизвестным типом; неизвестный/слишком длинный необязательный текст не подменяется классом вагона.

Нет множителя пассажиров, commission, total-party price, скидок или guaranteed quote. У цены source + mode receivedAt, sourceUpdatedAt unknown; текст «Цена по данным Яндекс.Расписаний», без обещания немедленной покупки. Нет общего price rank для разных валют/scope, выбора кресла, покупки или закрытого Yandex Travel API.

TS decoder и render сохраняют decimal string, включая значения выше Number.MAX_SAFE_INTEGER; `Number(amount)`/binary float форматирование денежных значений не применяется. Показ decimal string+currency допустим и не требует валютного formatter/framework.

### 6. Время ожидания, квоты и память

Предложенные фиксированные потолки M1; превышение не приводит к скрытому увеличению:

| Контур | Предел |
|---|---|
| Search | 2 одновременных admitted searches/process, без очереди; максимум2 upstream children/search; каждый5s, весь search8s |
| Reference bundle / autocomplete | 1 loader/process,≤2 cold API GET, общий20s; весь station request22s/UI25s; максимум2 admitted requests, включая waiters/scans, без очереди |
| HTTP search body | 8KiB, known fields/enums, без binder echo |
| Supplier search | 2MiB decoded body/mode, depth32, token16KiB; headers16KiB; no redirects/cookies |
| Public data | ≤4MiB serialized response; refs≤128UTF-8 bytes, UID≤100characters, label≤1024UTF-8 bytes,32fare items/journey; oversized optional label unavailable, no invented/truncated mandatory identity |
| Cache searches | 128entries/16MiB payload суммарно,256KiB/entry, absolute TTL300s; complete-empty30s |
| Catalogue | bounds из решения2; max2 snapshots, никаких неограниченных generation/history коллекций |

Не кэшировать schedule failed/limited/cancelled results. Cache key: schema, source/runtime profile epoch, bundle generation, ordered refs/date/mode. Maximum expiry=min(самый ранний included mode receivedAt+TTL, bundle expiry); complete-empty и schedule complete с damaged/limited/unavailable ticket component имеют TTL≤30s. Cache hit не оживляет generation и не продлевает freshness. Fictional/real не смешиваются; response immutable, disable/config epoch запрещает старые entries. Cache miss/отказ записи не повторяет HTTP. Cache-Control:no-store на API/browser replies; component memory очищает source results по expiresAt, refs по bundle expiry, без автоматического нового supplier query. На экране остаётся сообщение об истечении и ручное действие, а не вечная старая цена.

Telemetry не сохраняет raw bodies, keys, station query text или from/to/date. Для outbound query names используется существующий redaction contributor, для inbound autocomplete q — scoped route logging/tracing redaction (проверить реальные events, не только HttpClient). Authorization redacted; статический copyright frame referrerpolicy=no-referrer исключает передачу route/date в сторонний Referer.

Все supplier methods, включая copyright/catalogue, имеют один quota budget. Debits атомарны при dispatch, не возвращаются после неизвестного transport исхода/cancel и не смешиваются с освобождаемыми concurrency permits. Реальный режим требует проверенных minute/day ceilings;500/day не hard-code. Rolling60s/24h бюджет не приписывает источнику неизвестный reset. После restart real admission закрыт до подтверждения безопасного оставшегося бюджета; автоматического восстановления полного allowance и scale-out нет. Невозможность установить бюджет оставляет источник off.

Remote429 — failure без retry. Валидный Retry-After не сокращается локальным cap: хранить bounded timestamp gate без отдельного timer/task на отказ; missing/invalid header даёт60s cooldown. Overflow/непредставимая дата закрывает real admission до operator review. Пока cooldown действует, новый network request skipped/quota, cache hit может использовать только ещё разрешённые данные и permit не тратит.

При caller cancellation UI прекращает чтение, backend отменяет children и возвращает управление без публикации результата/записи cache. Если child игнорирует cancellation, его admission permit удерживается до фактического завершения; остальные запросы получают capacity refusal. Отложенные задачи/буферы остаются ограничены числом занятых permits; не создавать бесконечную очередь/single-flight registry. Source и platform retries не складываются.

### 7. UI, атрибуция и границы использования

Один `/rail`: station autocomplete → явный выбор обеих станций → date/mode → поиск → список. Подсказки с300ms debounce/отменой; отдельные epochs для каждого поля origin/destination и общего search. Ввод в origin отменяет только его подсказку и текущий search, но не уничтожает параллельную destination подсказку; изменение выбранного ref/date/mode очищает результаты. A→B→A не разрешает первый поздний ответ: проверяется request epoch, не только равенство criteria. Смена generation очищает оба refs; expiry или полученное сообщение о смене/отзыве profile очищает результаты/цены. Server disable немедленно запрещает новые backend публикации; уже доставленный browser snapshot без push остаётся максимум до своего исходного expiresAt. Instant remote revocation браузера не обещается: в M1 нет SSE/hidden polling. API search UI timeout10s; copyright/catalogue используют отдельные25s. Empty/limited-zero/failed/skipped и ticket partial различимы. Данные escaped text; carrier HTML/URLs не исполняются, дополнительных logo/carrier network requests нет.

Рядом с supplier data и в справке используется normalized attribution из reference bundle: выбранный официальный вертикальный монохромный баннер240×130, переданный уведомительный текст и активный URL в порядке баннер→текст→ссылка, размер/цвет текста не меньше/не отличен от основного. Статическая фраза из первоначального draft не заменяет required copyright metadata. Внешний HTML никогда не передаётся в innerHTML: Infrastructure принимает только документированный logo_vm fragment с одним iframe и точным ASCII src `//yandex.st/rasp/media/apicc/copyright_vert_mono.html` либо тем же HTTPS URL, без других элементов/on*/srcdoc/query/userinfo. Text≤1024UTF-8 bytes, URL только канонический `https://rasp.yandex.ru/`(документированный http эквивалент переводится на HTTPS). Наружу идут проверенные text/url и descriptor; UI сам создаёт ограниченный iframe sandbox без scripts/forms/top-navigation, title и no-referrer. В существующую web CSP добавляется только нужный banner frame endpoint, без wildcard/unsafe разрешений и без удаления уже согласованных auth/self directives. Это исключение для блока атрибуции, не встраивание чужого поиска.

Неизвестная форма/logo host (включая Unicode homograph), отсутствие metadata или установленная несовместимость официального баннера с безопасным режимом дают AttributionUnavailable и блокируют real data display; не включать arbitrary HTML/permissions ради обхода. Metadata/offline-frame тесты не доказывают live баннер: отдельный разрешённый browser acceptance до real enablement проверяет actual metadata/HTTPS/CSP/sandbox/видимость. Frame load event не доказывает, что cross-origin содержимое отрисовалось: нельзя обещать универсальное runtime обнаружение блокировщиков. Обнаруженные descriptor/CSP failures очищают source data с сообщением, а реальные browser ограничения фиксируются отдельным gate; не выдавать один load event за его замену. Fictional profile использует явно вымышленный local banner, terminal network, без реального Яндекса. Остальные обязательные notices не скрываются. Нет search history/export/browser storage/SSR transfer cache; fictional profile не подменяет real failure.

Наше минимальное отображение/временный cache проектируются в рамках API. Это не юридическое разрешение на произвольные derived datasets, cross-source joins или публикацию fixtures реальных ответов. Перед live ключом отдельно подтвердить стандартные условия/атрибуцию/временное хранение и actual quotas для бесплатного открытого продукта; при несовместимых условиях stop, без обхода private endpoints/scraping.

### 8. Автономная проверка и acceptance

Основной путь после утверждения: отдельный implementer `gpt-6.1-sol` → точечные проверки → личный review основного агента. Новая реализация/Host wiring не разрешены этим planning change.

- Pure Unit: identity/date/duration/money/outcomes, FakeTimeProvider и контролируемые children, quotas/cache bounds, no-write-after-cancel. Fake не внешний supplier.
- Adapter: production parser/client с terminal `HttpMessageHandler` или loopback, synthetic wire JSON,0 external destinations. Позитивные и malformed/truncated/oversize/401/429/timeout cases, корректный auth header/redaction, максимум supplier calls.
- Public contract/HTTP: реальные routing/validation/cancellation/metadata и OpenAPI, strict TS decoder; действительный Host fixture — отдельный разрешаемый локальный/CI контур, никакого заявления что lean fixture доказывает весь Host.
- Browser: fictional form → выбор станции → date → цена и неизвестные билеты; complete-empty/limited-zero/failure/cancel/old reply/keyboard attribution; zero purchase requests и нулевой внешний network.
- Architecture: положительные Rail selectors, project/type/method-body boundaries, only Api.Composition exception; Trips остаётся scaffold. Не ослаблять тесты ради зелёного результата.
- Deterministic stress без load runner: cold catalogue generation, repeated refresh/cancellation, cache eviction и uncooperative children доказывают счётчики/byte ceilings. Физическую RSS/recovery или throughput они не доказывают.

Отдельный предложенный fictional load/memory gate, **не запускать без разрешения**:3 цикла cold bundle→warm unique searches→cancel/expiry→idle, профили1/2 admitted searches плюс отдельное saturation. Dataset около40MiB(РФ/не-РФ),100000 station boundary,128entry pressure; copyright fixture локальный. До измерения прогреть тот же Host/JIT/parser, загрузить один bundle, очистить search cache, завершить children: baseline берётся в этом одинаковом business state, минимум5 samples за30s. После каждого цикла test clock истекает/освежает caches, затем возвращает ровно тот же state(один действительный bundle,0search entries/children), проверяется recovery через120s≤baseline+max(16MiB,15%). Нельзя требовать нулевой RSS при ещё действительном bundle или сравнивать разные retained states.

На каждом warm profile минимум1000 полезных successful uncached searches с instantaneous fictional supplier, p95≤1s;100% ожидаемых healthy-mode запросов дают valid useful data. Saturation/cancel cases отдельно: expected refusal/error counts,0 лишних upstream calls, permit/task recovery. Record all attempts/useful successes/refusals/failures, p50/p95, peak/current working set и retained counters; отказ/cancel не заменяют useful denominator. Предварительно выпущенный профиль/thresholds неизменны в пределах attempt, исходный RED сохраняется. Этот gate проверяет Rail, не переносит Hotels waiver и не доказывает реальную supplier latency.

| Acceptance | Что требуется / текущий статус |
|---|---|
| Specification | strict validation + semantic review + решение пользователя; сейчас draft |
| Source/autonomous | unit/adapter/lean HTTP/TS/browser/architecture receipts; ещё не выполнено |
| Ordinary CI / actual Host | exact head/run/attempt, required jobs/no hidden skipped cases; ещё не выполнено |
| Measured fictional load/memory | отдельные разрешение, профиль и retained receipts; pending, без waiver |
| Real supplier | личное разрешение, key/access/terms/quota/budget + фактические calls; pending |
| Target rollout | отдельный provisioning/deploy/acceptance; pending |

Не помечать весь Rail runtime-ready, пока обязательные для соответствующего этапа gates pending/RED. Source-only результат может быть предъявлен с открытыми gates, но не переименован в полную приёмку.

## Risks / Trade-offs

- [Нет ticket inventory contract] → показать цены/индикатор только как source facts; не обещать места; отдельный ticket provider потребует нового scope/rights.
- [Денежная subset] → RUB/USD/EUR поддержаны явно; другую валюту не считать ошибкой расписания и не угадывать её minor units. Расширение списка требует точного mapping/tests, а не FX framework.
- [Обязательная атрибуция] → metadata входит в bounded bundle, frame не исполняет произвольный HTML; actual официальная безопасная форма/видимость остаётся отдельным real gate, tool-inaccessible не объявляется endpoint outage.
- [40МБ каталог и RAM-only условия] → streaming, single-flight и counters; реальную память проверить отдельно, не увеличивать bounds скрытно.
- [Публичные примеры имеют локализованные значения/разные timestamps] → закрытый документированный mapping, обязательный offset, synthetic drift cases; live shape не считается доказанным.
- [Одна страница/transport] → limited с причиной, а не ложная полнота; paging — следующий отдельный increment.
- [Только endpoints РФ] → не заявлять глобальное покрытие всех станций/поездов или географию промежуточного пути.
- [Процессные quotas не account-wide] → single-instance/restart budget решается до live enablement; нет автоматического scale-out.

## Migration Plan

Миграции/постоянная БД не нужны. После отдельно утверждённой реализации Host получает только Rail.Api.Composition; default-off real transport не запускает supplier calls. Существующие маршруты/контракты других модулей сохраняются. Документация current-state/rail instructions и исторические противоречащие предположения concept уточняются по фактическому source результату, не удаляя историческую evidence.

Откат source integration отключает Rail, убирает navigation и очищает только его RAM entries; shared caches/data не flush. Никаких production deploy или смены ключей этот порядок не разрешает. Следующие increments: явный paging/детали, provider-supported пересадки, отдельный inventory/price supplier при наличии прав, затем multisource matching; порядок после M1 ещё не выбран.

## Open Questions

Только внешние activation gates: фактическая квота/сброс, права и доступ аккаунта, реальные wire shapes/покрытие билетных полей и actual copyright metadata/banner HTTPS/CSP/sandbox, пригодность memory/load thresholds целевой среде. Публичная попытка прочитать документированный static banner через web tool вернула inaccessible; это ограничение чтения, а не доказательство неработающего endpoint. Нужен отдельный разрешённый live browser receipt. Если минимальный безопасный banner не работает, варианты для владельца — поддержанная официальная безопасная форма/согласованное право на иной способ атрибуции либо сохранение real mode off; arbitrary scripts/private API/text-only обход не принимается. Неизвестность gates не разрешает live enablement. Если проверка опровергнет source/rights или потребует постоянного каталога, вернуться к владельцу и обновить change до реализации обхода.

## Planning Review

Отдельный read-only builder check выявил один P2: отсутствовал публичный contract исходов station autocomplete и поведения при смене generation. Уточнено в решении2: типизированный200 envelope, разные empty/skipped/failed/limited,400/429 для client/admission failures и повторный выбор обеих станций. Это уточнение дизайна автора, не новое разрешение на реализацию. Повторный отдельный check подтвердил закрытие P2 и отсутствие существенных противоречий между четырьмя artifacts; основной агент также проверил source/time/ticket/cancellation и acceptance границы.

Planning verification2026-10-19: `node tools/openspec/run.mjs validate rail-m1-yandex-search --strict --json` — valid:true, issues:[]; status —4/4 planning artifacts. Локальная проверка документов:19 requirements/61scenarios,22 невыполненные implementation tasks,19 точных traceability rows,6 корректных относительных links, нет duplicate IDs, unknown task refs или requirement descriptions длиннее500chars. Git HEAD остался `799e31a6b31ff256ba017861cd911e547e4e9896`; единственное изменение — новый каталог этого change. Код/зависимости/Host/schema не менялись, supplier calls/ключи и runtime/tests/load не выполнялись. Эти проверки доказывают согласованность planning corpus, а не реализацию или согласие пользователя на неё.

### Deep review2026-10-19 — финальная перепроверка2026-10-20

Пользователь отдельно поручил глубокое ревью и исправление этого change без реализации. Первоначальный краткий builder check и strict validation выше сохранены как исторический receipt; они не были исчерпывающей проверкой API семантики. Повторно прочитаны все4 artifacts, официальные search/station/copyright/access/terms и точечно существующая telemetry boundary. Реальные provider API/ключи/регистрация/переписка и product runtime не использовались. Следующая таблица фиксирует найденные дефекты планирования и исправления, а не обнаруженные runtime уязвимости.

| ID / приоритет | Проблема прежнего draft | Исправление / проверяемое обязательство |
|---|---|---|
| R1/P1 | Пустой ответ чужой пары/date мог стать complete-empty/cache | Обязательный search echo до rows, type=station, exact refs/date; сценарий empty query mismatch |
| R2/P2 | start_date без основания стало mandatory run-start date/key | Optional source date; snapshot identity по thread/mode/endpoints/instants; missing date не ломает расписание |
| R3/P1 | Один loader не ограничивал HTTP waiters; leased generations могли пережить max2snapshot |2admitted autocomplete callers, cancelled loader quarantine, все leases входят в общий bound; flood/refresh tests |
| R4/P1 | Статический текст и полный запрет iframe противоречили copyright reference | Banner/text/link, bounded atomic metadata, closed safe descriptor; отдельный actual banner gate, без HTML/permissions обхода |
| R5/P2 | Ошибка цены стирала исправный marker/другие цены; неполная цена/чужая валюта/JS rounding неоднозначны | Независимые states, partial reasons, обе компоненты суммы, supported currency subset,32fare cap; exact string до render |
| R6/P2 | Придумана общая арифметика interval pagination и несколько вариантов enum | Не предполагать sum(interval+scheduled)=total, count до dedup, optional array; единственный public kind contract |
| R7/P1 | Config смена после admission/expiry каталога могли переживаться cache/result | Process-owned epoch перед dispatch/publication/write, expiry=min(mode TTL,bundle validity), no late source data |
| R8/P2 | UI field epochs, A→B→A и срок жизни component data не определены | Отдельные field/search epochs,25s/10s deadlines, очистка по expiry; не обещать instant browser revoke без push |
| R9/P2 | Unknown RF station type/missing RF branch могли выглядеть полным no-match | Passenger subset, limited coverage и missing/empty catalogue unavailable |
| R10/P2 | Не ограничен output/consumer authority, q/from/to/date могли остаться в telemetry |4MiB/field bounds, exact mode/request/counts и честная null freshness; actual inbound/outbound redaction/no-referrer |
| R11/P2 | Quota debit смешан с permit; Retry-After/restart allowance не закрыты | No debit refund при unknown/cancel, общий API budget, не сокращать server cooldown, новый restart admission закрыт |
| R12/P2 | Memory baseline мог сравнивать разные retained states; harness готовился после personal review | Same warm business state,1000useful samples/profile, отдельная saturation; profile6.1 до integration6.2/personal6.4 |

Исходный snapshot снят перед правками. SHA256: proposal `f73baaabf323e64abee16d26292efe42477b14d2bcd883a25e28bcb58094137b`; design `41c004c3a47ba185e0bce5ee7743e1022f043437da0f58f4fab010d17e08ef3a`; spec `a2988654d8d961cb06095c52bce8170f3d304f8edf0d9fd348be46ee4b427578`; tasks `7d233bcd95748db1e81573667feaf246109cec814891c5758867e4b1efdb4524`.

После исправлений:23requirements/90scenarios,25невыполненных tasks,23traceability rows,6валидных relative links. Strict validation:valid:true/issues:[]; descriptions≤500chars, нет duplicate IDs/scenario names/unknown task refs/готовых implementation checkbox. Исправлены только4 существующих Markdown artifacts; `.openspec.yaml`, root/main specs, приложение, тесты, зависимости/security backport и Host не менялись. Новые thresholds, currency subset и безопасная attribution форма остаются предложенными решениями автора для утверждения всего change; никаких новых runtime/внешних разрешений этот review не создаёт.
