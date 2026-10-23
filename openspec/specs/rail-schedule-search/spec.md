# Rail schedule search Specification

## Purpose

Позволить пользователю искать прямые поезда и электрички между конкретными станциями РФ в собственном интерфейсе Travel, сохраняя происхождение расписания, неполноту поиска и неизвестность сведений о билетах.

## Requirements

### Requirement: Bounded RF direct rail scope
Система SHALL искать прямые поездки между двумя различными железнодорожными станциями РФ на одну выбранную дату через единственный источник Yandex.Rasp. Поезда и электрички SHALL быть отдельными видами запроса; поиск не SHALL резервировать, покупать или подтверждать наличие мест.

#### Scenario: Both rail modes use one source
- **WHEN** пользователь выбирает оба вида сообщения
- **THEN** выполняются не более двух ограниченных запросов одного источника, а электрички и поезда не объявляются двумя поставщиками.

#### Scenario: Transfers and other transport are outside M1
- **WHEN** запрос требует пересадки, multi-leg, автобус или самолёт
- **THEN** он отклоняется до supplier HTTP и не преобразуется в прямой железнодорожный поиск.

#### Scenario: Source cannot be used
- **WHEN** источник выключен или не допущен
- **THEN** ответ имеет unsupported/skipped с причиной и0 supplier calls, без фиктивной замены.

### Requirement: Explicit RF station selection
Система SHALL давать ограниченные подсказки отдельных станций по имени станции или населённого пункта и требовать явного выбора source-qualified refs из текущего временного каталога. Название, код города и географическая близость не SHALL заменять идентичность станции.

#### Scenario: Ambiguous station names
- **WHEN** две станции имеют одинаковое название
- **THEN** выдаются отдельные refs с населённым пунктом/регионом; выбор одной не выбирает другую.

#### Scenario: Bounded autocomplete
- **WHEN** передано от3 до80 символов
- **THEN** возвращается не более20 подсказок с generation reference bundle; холодный flow делает максимум2 API GET(copyright+stations), дополнительный ввод не создаёт отдельную загрузку, не более2 callers одновременно ожидают/сканируют без очереди.

#### Scenario: Invalid or stale selection
- **WHEN** ref неизвестен, относится к settlement/не-РФ/не-rail или generation истекла
- **THEN** поиск отклоняется с явной причиной до supplier search, а UI просит заново выбрать станцию.

#### Scenario: Catalogue unavailable is not empty autocomplete
- **WHEN** source выключен/local quota не допускает загрузку либо loader/атрибуция failed/timeout/cooldown
- **THEN** валидный q получает200 envelope со skipped либо failed и причиной, generation=null; это не complete с пустым suggestions. More-than20 совпадений имеют limited/hasMore, настоящий no-match имеет complete-empty semantics.

#### Scenario: Generation changes between two selections
- **WHEN** первая станция выбрана до refresh, а вторая после него
- **THEN** оба выбора инвалидируются и запрашиваются заново; отправленный search с устаревшей generation получает400 Rail.StationSelection.Expired и0 supplier calls.

#### Scenario: Unsupported RF station records
- **WHEN** RF ветка содержит неизвестный transport/passenger type рядом с пригодными станциями
- **THEN** такие records не угадываются, catalogue coverage limited и no-match не становится гарантией отсутствия станции; отсутствие RF branch или0 пригодных станций даёт unavailable, не successful empty catalogue.

### Requirement: Explicit search date and request validation
Система SHALL принимать строгую календарную дату отправления YYYY-MM-DD и известный transport mode; ограничивать запрос8KiB и диапазоном UTC-date(now)−1 … UTC-date(now)+30. Этот диапазон не SHALL объявляться окном продажи; дата станции не SHALL выводиться из timezone сервера или браузера.

#### Scenario: Valid local departure date
- **WHEN** выбрана дата внутри разрешённого диапазона
- **THEN** она передаётся источнику явно и сравнивается с offset-local датой отправления результата, а не с датой начала полного рейса.

#### Scenario: Malformed request
- **WHEN** дата невозможна, поле неизвестно/дублировано, mode неизвестен, body превышает8KiB или from=to
- **THEN** возвращается400/413 ProblemDetails без supplier calls и без отражения raw body.

#### Scenario: No implicit today
- **WHEN** пользователь ещё не выбрал дату
- **THEN** поиск не запускается и приложение не подменяет дату локальным today сервера/браузера.

### Requirement: Source qualified journey identity
Система SHALL различать source station, reference нитки и конкретный вариант поездки. Snapshot identity варианта SHALL включать source, непрозрачный ref нитки, transport, точки посадки/высадки и instants. Номер/название и optional source date не SHALL становиться глобальным ID физического рейса.

#### Scenario: Same train number on different days
- **WHEN** источник возвращает одинаковый номер/UID с разными departure instants или thread refs
- **THEN** варианты остаются отдельными.

#### Scenario: Optional source reported date
- **WHEN** start_date отсутствует/невалидна либо отличается от boarding date
- **THEN** она не становится обязательным ключом/датой начала полной нитки и не ломает валидное расписание; неизвестная/invalid metadata обозначается отдельно, дата поиска определяется departure offset.

#### Scenario: Exact and conflicting duplicates
- **WHEN** два records имеют одинаковую variant identity
- **THEN** одинаковые обязательные/билетные факты сворачиваются; противоречащие факты делают mode failed, без произвольного выбора версии.

### Requirement: Faithful timetable timestamps and provenance
Система SHALL сохранять плановые instants, предоставленные offsets и provenance, различая local departure date, optional source-reported date, mode receivedAt и sourceUpdatedAt. Отсутствующий IANA ID/sourceUpdatedAt SHALL оставаться неизвестным; расписание не SHALL выдаваться за realtime или физическое движение.

#### Scenario: Midnight and different offsets
- **WHEN** поезд отправляется и прибывает в разные календарные даты/offsets
- **THEN** UI показывает дату и offset каждого конца, а длительность проверяется по instants.

#### Scenario: Unresolvable required timestamp
- **WHEN** offset отсутствует, формат невалиден, arrival≤departure или конечная положительная duration в секундах не равна разности instants
- **THEN** mode failed; UTC/Moscow/host timezone не подставляется.

#### Scenario: No realtime evidence
- **WHEN** имеется только расписание
- **THEN** задержка/отмена/движение вовремя остаются неизвестными; receivedAt не называется временем обновления поставщиком.

### Requirement: Match the requested railway journey
Система SHALL проверять обязательный search echo станций/type/date даже для пустого ответа, затем соответствие records endpoints/transport/local date/прямой поездке. Невалидный echo/обязательный record не SHALL превращаться в complete-empty; duplicate обязательные JSON поля не SHALL приниматься по last-wins.

#### Scenario: Wrong station or route kind
- **WHEN** непустой ответ содержит чужую станцию, дату, transport или has_transfers=true
- **THEN** соответствующий mode failed, а валидный другой mode может сохраниться только в limited результате.

#### Scenario: No required source identity
- **WHEN** отсутствует обязательный thread ref или endpoint identity
- **THEN** record не получает идентичность из номера поезда; mode failed с безопасной причиной mapping.

#### Scenario: Empty reply belongs to another query
- **WHEN** segments пуст, но search echo отсутствует или имеет другую from/to/date либо settlement вместо station
- **THEN** mode failed/query-mismatch и cache write отсутствует; даже total=0 не даёт complete-empty.

#### Scenario: Duplicate critical JSON property
- **WHEN** echo/identity/pagination/price/indicator имеет повторённое имя свойства
- **THEN** затронутый обязательный mode или необязательный компонент отклоняется; последнее значение не выбирается произвольно, неизвестное добавочное поле не исполняется.

### Requirement: Source ticket facts without invented inventory
Система SHALL отображать возвращённые типы/цены билетов и electronic-ticket indicator как отдельные source facts. Массив типов не SHALL трактоваться как число мест, indicator не SHALL доказывать seat inventory, а неизвестные данные не SHALL становиться sold out или закрытой продажей.

#### Scenario: Reported fare and electronic ticket
- **WHEN** источник вернул валидные тип, цену и electronic-ticket=true
- **THEN** они видны с источником/receivedAt, при этом свободные места/схема вагона неизвестны и покупки в Travel нет.

#### Scenario: Electronic ticket false
- **WHEN** источник вернул electronic-ticket=false
- **THEN** отображается именно этот признак, а не «мест нет» или «продажи не открыты».

#### Scenario: Missing or empty ticket data
- **WHEN** билетный block отсутствует/null/пуст и цен нет
- **THEN** корректный рейс остаётся; цена и наличие неизвестны; отдельно переданный indicator сохраняется даже при пустом списке цен.

#### Scenario: Invalid optional ticket data
- **WHEN** расписание валидно, но одна цена невалидна
- **THEN** её price unavailable с причиной, другие исправные цены/типы и marker сохраняются; fares limited, schedule completeness не изменяется и повреждённая сумма не показывается.

#### Scenario: Ticket component independence
- **WHEN** marker невалиден при исправной цене либо places неверного типа при marker=false
- **THEN** первый случай сохраняет цену и unknown marker, второй — reported false marker и unavailable fares; отсутствие одной части не стирает независимую.

#### Scenario: Ticket type limit
- **WHEN** источник возвращает более32 типов билетов
- **THEN** сохраняются первые32, fares limited/output-limit; цена всего journey/места не выводятся из неполного списка, schedule не объявляется неполным только из-за optional ticket limit.

### Requirement: Exact source money and unknown price scope
Система SHALL сохранять валидную неотрицательную цену как точную decimal string с исходной поддержанной валютой RUB/USD/EUR. Оба компонента суммы обязательны; другая валюта/неизвестные тип или scope не SHALL интерпретироваться по догадке. FX, commission, умножение на пассажиров и гарантированная quote не SHALL добавляться.

#### Scenario: Exact minor units
- **WHEN** источник сообщает whole=1234 и cents=5 с RUB
- **THEN** публичная цена равна decimal string1234.05 RUB без binary floating point округления.

#### Scenario: Explicit zero differs from unknown
- **WHEN** цена явно равна whole=0/cents=0 либо не предоставлена
- **THEN** первый случай показывает0.00 в своей валюте, второй не показывает нулевую цену.

#### Scenario: Invalid or incomparable fares
- **WHEN** cents вне0..99, значение отрицательное/нецелое/overflow, валюта невалидна или цены разных валют/неизвестного scope
- **THEN** затронутая цена unavailable; несопоставимые валидные цены не конвертируются и не получают общий price rank, независимые факты сохраняются.

#### Scenario: Incomplete price or unsupported currency
- **WHEN** price=null, один компонент отсутствует либо currency вне RUB/USD/EUR
- **THEN** отсутствующая цена not-provided/incomplete-price, неподдержанная unavailable/unsupported-currency; cents не заполняется нулём и minor-unit scale чужой валюты не угадывается.

#### Scenario: Integral JSON number representation
- **WHEN** whole=1234.0 и cents=5.0 математически целые
- **THEN** принимается1234.05 без округления;1234.5 как whole не принимается как1234.

#### Scenario: Exact price survives browser rendering
- **WHEN** валидная цена9007199254740993.05RUB проходит CLR→JSON→TS decoder→UI
- **THEN** все цифры сохраняются строкой, JS Number conversion и currency formatting с потерей точности не используются.

### Requirement: Explicit completeness and failure outcomes
Система SHALL сохранять mode outcomes и различать complete, complete-empty, partial/limited, unsupported/skipped и failure с причинами в API/UI. Complete-empty SHALL требовать исчерпанного валидного результата всех запрошенных modes; недостаточная информация не SHALL означать отсутствие поездов.

#### Scenario: All requested modes exhausted empty
- **WHEN** все выбранные modes успешно проверены и total=0
- **THEN** результат complete-empty, а UI сообщает об отсутствии поездок в выбранном расписании.

#### Scenario: Empty success plus failed mode
- **WHEN** один mode завершился пустым, а другой timeout/failed
- **THEN** результат limited с0 journeys и обеими причинами; UI не сообщает, что поездов нет.

#### Scenario: All attempted modes fail
- **WHEN** ни один mode не дал usable результата и есть attempted failure
- **THEN** результат failure, включая supplier401/403/429 или malformed envelope; это не skipped и не empty.

#### Scenario: All modes deliberately skipped
- **WHEN** ни один mode не выполнялся из-за enablement/quota/capability
- **THEN** результат unsupported/skipped и перечислены причины; failure не выдумывается.

#### Scenario: Complete mode plus quota skipped mode
- **WHEN** один mode успешно исчерпан, а второй не запущен из-за общего quota budget
- **THEN** результат limited с обеими причинами, включая0 journeys; публичный kind ровно complete/limited/skipped/failed, empty определяется items=[], unsupported — skipped reason.

### Requirement: Bounded pagination and interval coverage
Система SHALL выполнять максимум одну страницу из50 records на выбранный transport mode и максимум2 supplier search GET на запрос. Следующая страница, неподдержанное interval service или неполная выдача SHALL обозначаться limited; система не SHALL изобретать точное время интервального рейса.

#### Scenario: Next page exists
- **WHEN** корректная pagination сообщает больше records, чем возвращено первой страницей
- **THEN** ответ limited/page-limit с сохранёнными journeys; автоматической загрузки следующей страницы нет.

#### Scenario: Only interval services
- **WHEN** ответ содержит interval_segments без фиксированных departure/arrival
- **THEN** ответ limited/interval-service-unsupported с0 scheduled journeys, а не complete-empty.

#### Scenario: Inconsistent pagination
- **WHEN** total/offset/limit отсутствуют/нецелые, offset≠0, limit≠50, scheduled records>50 или total меньше их исходного количества
- **THEN** mode failed, без заявления полноты или скрытого дополнительного запроса.

#### Scenario: Interval count does not invent pagination semantics
- **WHEN** присутствуют interval records и total не равен сумме двух массивов
- **THEN** непрописанное источником равенство не требуется; результат limited независимо от total, обязательные route/mode refs interval records всё равно проверяются.

#### Scenario: Missing interval array and duplicate folding
- **WHEN** interval_segments отсутствует, а scheduled records имеют точные повторы
- **THEN** отсутствие массива означает[], полнота определяется total и исходным count до dedup; число уникальных journeys не создаёт ложную следующую страницу.

### Requirement: Bounded execution and cancellation
Система SHALL ограничивать поиск и подробности суммарно2 admitted операциями/process без очереди; поиск SHALL сохранять максимум2 children/search,5s/child и8s/search. Отмена caller SHALL прекращать ожидание и запретить позднюю публикацию/cache write; незавершённая работа SHALL удерживать permit своей операции до фактического завершения.

#### Scenario: Capacity exhausted
- **WHEN** две admitted операции поиска/подробностей ещё владеют работой
- **THEN** следующий поиск или запрос подробностей получает429 без supplier calls и без постановки в очередь.

#### Scenario: Cancellation with uncooperative child
- **WHEN** caller отменяет поиск, а child игнорирует отмену
- **THEN** caller освобождается своевременно, child не публикует результат и продолжает занимать ограниченный permit; detached work не растёт с новыми callers.

#### Scenario: Deadline and retry limit
- **WHEN** child не завершился за5s или supplier ответил ошибкой
- **THEN** соответствующий mode timeout/failed без retry; aggregate завершается в пределах8s, сохраняя уже пригодные mode results.

#### Scenario: Mixed search and detail workload
- **WHEN** одновременно выполняются один all-mode search и одни подробности
- **THEN** заняты два общих permits; подробности не получают второй внутренний permit для своей перепроверки и не обходят общий quota ledger. Третья операция отклоняется, без deadlock и независимого дополнительного пула.

### Requirement: Temporary bounded catalogue and cache
Система SHALL хранить supplier data только во временном ограниченном RAM cache: каталог30min, поиски≤300s, complete-empty≤30s, без sliding extension. Переполнение/истечение SHALL приводить к miss/refusal, а не неограниченному storage или выдаче expired data.

#### Scenario: Cold catalogue single flight and bounds
- **WHEN** параллельные подсказки требуют холодный каталог
- **THEN** максимум2 admitted callers делят один bundle loader с20s deadline/≤2API calls; catalogue≤64MiB decoded, depth≤32,token≤16KiB,≤100000 stations/48MiB payload; copyright≤128KiB/normalized4KiB, browser не получает весь каталог.

#### Scenario: Catalogue refresh and failure
- **WHEN** catalogue refresh истёк/повреждён/переполнен
- **THEN** нет усечённого «полного» snapshot,0 search calls с expired refs и30s cooldown после failed refresh; лимит2snapshots включает leased старые generations, новый loader не наращивает retired list.

#### Scenario: Shared catalogue waiter cancellation
- **WHEN** один waiter отменяется, а другой ещё ожидает каталог, либо отменились все
- **THEN** первый случай сохраняет единственную загрузку для оставшегося waiter; второй отменяет loader без поздней публикации/cache write и удерживает permit до завершения.

#### Scenario: Cold waiter capacity and cancelled loader
- **WHEN** уже2 autocomplete callers ожидают bundle либо отменённый loader ещё не завершился
- **THEN** третий не ставится в очередь/ожидание(429), а новые admitted callers не присоединяются к умершему loader и не создают новый поверх него.

#### Scenario: Search cache pressure and identity
- **WHEN** добавляются новые запросы или меняются profile/config/catalogue generation
- **THEN** cache удерживает≤128entries/16MiB суммарно/256KiB на entry; старые ключи не попадают в новый контекст, failed/limited/cancelled результаты не сохраняются.

#### Scenario: Cache hit without extending freshness
- **WHEN** повторно запрашивается ещё действительный complete result
- **THEN** supplier calls=0, исходные receivedAt/expiry сохраняются; mutation возвращённой коллекции не меняет cache.

### Requirement: Controlled official source access
Система SHALL обращаться только к разрешённому официальному host по HTTPS с secret в Authorization; redirects, cookies и произвольные URL из ответа SHALL быть запрещены. Real transport SHALL быть default-off и требовать отдельных access/rights/quota разрешений; API key не SHALL попадать в UI/logs.

#### Scenario: Disabled configuration and cache
- **WHEN** нет real admission либо source выключен после предыдущего поиска
- **THEN** supplier calls=0, startup не загружает каталог и старый real cache не обходят запрет.

#### Scenario: Local and remote quota limits
- **WHEN** локальный source budget исчерпан либо supplier возвращает429
- **THEN** локальный отказ skipped/quota не делает HTTP, remote429 является attempted failure без retry и с допустимым cooldown; все methods делят один source budget.

#### Scenario: Quota debit survives uncertain transport outcome
- **WHEN** copyright/catalogue/search dispatch произошёл, затем caller отменился или потерян ответ
- **THEN** quota debit не возвращается с concurrency permit; restart не восстанавливает полный real allowance без нового подтверждения безопасного остатка.

#### Scenario: Retry After is not shortened
- **WHEN** remote429 имеет Retry-After позже локального стандартного cooldown или непредставимую дату
- **THEN** валидный срок не сокращается; overflow блокирует admission до review; missing/invalid header даёт60s без отдельной timer/task на каждый отказ.

#### Scenario: Unconfirmed account quota
- **WHEN** не подтверждены ceilings/сброс и безопасный accounting при restart/scale-out
- **THEN** real transport остаётся выключенным; исторические500/day не объявляются действующим account-wide лимитом.

#### Scenario: Malicious transport response
- **WHEN** response redirect, oversized body/header, excessive depth/token или произвольный method URL
- **THEN** adapter отказывает без follow-up HTTP; secret, body и raw provider error отсутствуют в telemetry/API.

### Requirement: Public HTTP and client contract
Система SHALL публиковать нормализованные station/search contracts с source evidence и typed outcomes. Допустимый завершённый поиск SHALL иметь200 execution envelope даже при source failure; malformed client input/admission/platform failure SHALL различаться HTTP статусом. Ответы SHALL иметь no-store и строгий клиентский decoder.

#### Scenario: Source failure remains typed
- **WHEN** допустимый запрос завершился supplier failure
- **THEN**200 response сохраняет outcome/reasons и исходный request, а consumer отличает его от400/429/5xx.

#### Scenario: Malformed public reply
- **WHEN** клиент получает неправильный тип, лишний размер массива, невалидные деньги/время или несовместимые outcome/counts
- **THEN** decoder отклоняет ответ и UI показывает ошибку чтения вместо выдуманного корректного результата.

#### Scenario: Mode and request authority is inconsistent
- **WHEN** публичный reply имеет другой request/source/profile/generation, повторённый/лишний mode, items failed/skipped mode или normalized count≠items.length
- **THEN** consumer отклоняет его; count не путает supplier rows с unique journeys после dedup; skipped/failed без source data могут иметь null freshness, transport failure до ответа не получает выдуманный supplier receivedAt.

#### Scenario: Safe transport metadata
- **WHEN** читаются подсказки, поиск или ошибки
- **THEN** ответы no-store, не содержат key/raw DTO/private method URL и представлены в фактическом Host OpenAPI.

### Requirement: Bounded public data size
Система SHALL ограничивать нормализованный public response4MiB, выдачу100journeys/20suggestions/32ticket types на journey и длины source refs/text. Обязательные identity не SHALL усекаться для прохождения лимита; неполнота необязательных сведений SHALL обозначаться явно.

#### Scenario: Normalized payload exceeds total bound
- **WHEN** допустимый wire body приводит к public response>4MiB
- **THEN** возвращается bounded200 failed/normalization-limit с0items и без cache write; усечённый complete не публикуется.

#### Scenario: Oversized optional label differs from identity
- **WHEN** optional label>1024UTF-8 bytes либо station ref>128bytes/thread UID>100characters
- **THEN** label становится unavailable с причиной без угадывания класса/текста, а невалидная обязательная identity отклоняет запись/mode; ограничение ticket array имеет отдельный limited outcome.

### Requirement: Search telemetry minimization
Система SHALL исключать keys/raw bodies/текст подсказок/поисковые from-to-date из application logs, tracing и exception diagnostics. Browser attribution requests не SHALL получать поисковый контекст через Referer; проверка не SHALL ограничиваться одним HttpClient logger.

#### Scenario: Incoming and outgoing query redaction
- **WHEN** выполняются autocomplete/search или отказ/malformed response
- **THEN** проверенные inbound и outbound telemetry events содержат безопасные reasons/counters, но не q/from/to/date/key/body; Authorization скрыт.

#### Scenario: Attribution does not leak route
- **WHEN** браузер загружает разрешённый banner frame
- **THEN** frame имеет no-referrer, source query/body/key не передаются; fictional browser tests не обращаются к реальному домену.

### Requirement: Reference bundle attribution readiness
Система SHALL атомарно публиковать временный source-bound reference bundle со станциями и проверенной атрибуцией. Она SHALL показывать banner/text/link и не SHALL исполнять arbitrary provider HTML; real enablement требует отдельного подтверждения безопасной работы официального баннера.

#### Scenario: Missing or unsafe copyright metadata
- **WHEN** copyright API timeout/malformed, logo отсутствует, содержит srcdoc/on*/extra elements или чужой/homograph host
- **THEN** bundle не публикуется, результат attribution-unavailable не превращается в empty stations и real supplier data не показываются с одной подписью вместо атрибуции.

#### Scenario: Controlled official banner rendering
- **WHEN** metadata содержит поддержанный logo_vm с точным официальным ASCII URL
- **THEN** Infrastructure выдаёт normalized descriptor без raw HTML, UI создаёт отдельный sandboxed240×130frame с ограниченным CSP/no-referrer, затем source text/link; поиск остаётся собственным интерфейсом.

#### Scenario: Fictional and live attribution proof differ
- **WHEN** offline metadata/parser/local-frame tests проходят
- **THEN** они доказывают только fictional contract; actual HTTPS/CSP/sandbox/видимость official banner проверяются отдельно до real enablement, frame load event сам по себе не объявляется доказательством отображения.

### Requirement: Current profile and temporal data authority
Система SHALL связывать cache/bundle/results с internally generated source profile epoch и absolute expiry. Она SHALL перепроверять epoch перед dispatch/publication/cache write и удалять истёкшие данные из UI memory; прошлый cache или поздний ответ не SHALL обходить disable/смену конфигурации.

#### Scenario: Source changes during a request
- **WHEN** source/credentials/permissions/config изменились после admission
- **THEN** старый epoch отменяется, его поздний ответ и cache write отклоняются, cache/bundle старого профиля не используются новым запросом.

#### Scenario: Already delivered browser snapshot after server disable
- **WHEN** сервер выключил source после доставки ответа и браузер ещё не получил нового ответа
- **THEN** instant push-revocation не обещается, snapshot очищается не позже исходного expiresAt или полученного profile notice; hidden polling/SSE для этого не добавляются.

#### Scenario: Bundle expires before search cache TTL
- **WHEN** у bundle остаётся10s, а search обычный TTL300s
- **THEN** expiresAt результата не позже bundle expiry; cache hit не продлевает его и не возвращает expired station generation.

#### Scenario: Received times are not rewritten
- **WHEN** два modes завершились в разное время и ответ затем читается из cache
- **THEN** факты/цены имеют собственные receivedAt, envelope сохраняет самый ранний receivedAt и отдельный completedAt; cache read не выдаётся за новое получение от supplier.

#### Scenario: UI snapshot expires
- **WHEN** result expiresAt или bundle validity истекли
- **THEN** старые source rows/цены либо оба station refs удаляются из component state, UI сообщает об истечении и предлагает ручное обновление, без auto supplier query.

### Requirement: Own interface with current request authority
Система SHALL предоставлять собственную Rail форму и результаты с явными состояниями, датами и билетными неизвестными. Новый ввод/поиск SHALL отменять прежнее чтение и исключать применение старого ответа. Fictional profile SHALL иметь видимую метку и не SHALL автоматически заменять real failure.

#### Scenario: Complete user path with price
- **WHEN** пользователь выбирает две станции, дату, mode и получает рейс с ценой
- **THEN** UI показывает маршрут, оба местных времени/даты, длительность, source ticket type/точную цену и неизвестное наличие мест; purchase requests=0.

#### Scenario: Old reply races with new query
- **WHEN** старый autocomplete/search ответ приходит после смены ввода или следующего поиска
- **THEN** он не меняет выбор/результаты нового запроса, loading/error и показываемый request остаются согласованными.

#### Scenario: Independent station field epochs and repeated criteria
- **WHEN** одновременно вводятся origin/destination либо запрос меняется A→B→A
- **THEN** поля имеют отдельные epochs, поздний первый A не принимается только из-за совпадения criteria; любой новый выбор/date/mode очищает старый результат.

#### Scenario: Accessible empty limited and failed views
- **WHEN** результат empty, limited-zero, failed или skipped
- **THEN** тексты и доступные keyboard/live-region состояния различаются; кнопки покупки/выбора места отсутствуют, а строка поставщика отображается plain text.

### Requirement: Attribution and restricted retention
Система SHALL сохранять обязательную атрибуцию/уведомления источника и ограничивать обработку возможностями API и временным cache. Она не SHALL сохранять raw archive, постоянный нормализованный каталог, search history или supplier fixtures, экспортировать/объединять данные либо обходить условия через private API.

#### Scenario: Source attribution next to data
- **WHEN** экран показывает supplier station/schedule/ticket data
- **THEN** рядом и в справке видны официальный баннер, source уведомительный текст и активная ссылка в этом порядке, текст размером/цветом основного; статическая подпись не заменяет остальные обязательные notices.

#### Scenario: No durable or browser retention
- **WHEN** пользователь ищет, закрывает/перезагружает страницу или cache истекает
- **THEN** supplier data отсутствуют в DB/files/browser storage/SSR transfer cache; RAM entries удаляются по TTL/capacity, а source bodies не попадают в logs.

#### Scenario: Incompatible rights
- **WHEN** external rights review не подтверждает нужный собственный UI/временную обработку
- **THEN** real enablement блокируется и scope возвращается владельцу; scraper или постоянная БД не добавляются как обход.

### Requirement: Module composition and isolation
Система SHALL подключать реализованный Rail только через его Api.Composition facade, сохраняя Core→Application→Infrastructure→Api ownership и Host-owned process pipeline. Внешние DTO/protocol/mapping SHALL оставаться в Infrastructure; другой модуль и Shared не SHALL служить обходом границы.

#### Scenario: Enabled module architecture
- **WHEN** source milestone подключается к Host
- **THEN** Host импортирует только Rail.Api.Composition, positive selectors охватывают непустую реализацию, а общие project/type/method-body guards сохраняются.

#### Scenario: No speculative infrastructure
- **WHEN** добавляется read-only Rail
- **THEN** Rail не создаёт DB/schema/migration/events/broker или общий multi-provider framework, не меняет подключение других модулей и сохраняет существующий security backport.

### Requirement: Autonomous fictional proof
Система SHALL позволять воспроизводимую проверку выбранного source contract на собственных вымышленных данных без ключей и внешней сети. Fake SHALL использовать тот же нормализованный контракт, а adapter tests SHALL исполнять production wire parser; проверки не SHALL объявлять fake второй интеграцией.

#### Scenario: Offline positive and negative wire cases
- **WHEN** запускаются adapter tests
- **THEN** они проверяют production parsing/auth/call bounds через terminal fictional transport, включая malformed/timeout/price cases, и запрещают каждый внешний destination.

#### Scenario: Distinct test evidence
- **WHEN** проходят unit, lean HTTP, TS и fictional browser checks
- **THEN** receipts указывают точные команды/counts/profile; lean checks не выдаются за actual Host, реальные supplier data или live acceptance.

### Requirement: Separate measured and external acceptance
Система SHALL отделять specification/source/autonomous/ordinary CI, actual Host, measured fictional load/memory, real supplier и target rollout acceptance. Pending/failed gate не SHALL становиться passed из-за другого контура; Hotels L1 waiver не SHALL переноситься на Rail.

#### Scenario: Source green with pending measurement
- **WHEN** source checks passed, но Rail memory/load не разрешён или не пройден
- **THEN** отчёт допускает source-ready только с явным pending/failed measured gate и не объявляет весь Rail runtime-ready.

#### Scenario: Red memory recovery
- **WHEN** предложенный отдельный measured профиль нарушил memory recovery/p95/retained-work пороги
- **THEN** сохраняются исходный RED receipt и полный знаменатель attempts; timeout/refusal не считаются полезными успехами, пороги не меняются скрытно.

#### Scenario: Comparable memory baseline and useful performance denominator
- **WHEN** отдельно разрешённый measured профиль проходит3 цикла с1/2 admitted search profiles
- **THEN** baseline/recovery имеют одинаковое прогретое состояние(1действительный bundle,0search cache/children), recovery120s≤baseline+max(16MiB,15%); каждый warm profile имеет≥1000useful uncached successes,p95≤1s, а saturation/refusal/cancel считаются отдельно и не заменяют успешную нагрузку.

#### Scenario: Implementation authority
- **WHEN** planning artifacts подготовлены
- **THEN** реализация ждёт отдельного утверждения спецификации; после него требуется отдельный implementer gpt-6.1-sol и личная проверка основным агентом, а внешние действия остаются отдельными разрешениями.

### Requirement: Local journey ordering
Система SHALL предоставлять сортировку полученной выдачи по отправлению, прибытию и длительности по возрастанию, без новых HTTP-запросов и изменения исходных source facts. По умолчанию SHALL использоваться отправление. Сортировка SHALL применяться только к текущему действительному snapshot, сохраняя признаки неполноты.

#### Scenario: Absolute times and stable ties
- **WHEN** варианты имеют разные даты/offsets либо равные значения сортировки
- **THEN** отправление/прибытие сравниваются по instants, длительность — по durationSeconds, равенство разрешается исходным индексом; исходный массив и точные строки цен не меняются.

#### Scenario: Limited results are not a global ranking
- **WHEN** пользователь сортирует limited выдачу
- **THEN** остаются исходный limited notice и причины, виден текст «Среди полученных вариантов»; самый короткий в списке не объявляется лучшим/самым быстрым поездом на всём направлении.

### Requirement: Local time and carrier filters
Система SHALL фильтровать текущие строки по местному времени отправления, местному времени прибытия и сообщённому названию перевозчика без supplier calls. Фильтры времени SHALL предлагать «Любое», 00:00–06:00, 06:00–12:00, 12:00–18:00, 18:00–24:00 с включённой нижней и исключённой верхней границей; по умолчанию все фильтры выключены.

#### Scenario: Station local clock rather than browser time
- **WHEN** фильтр применяется к ночному рейсу с переходом даты/offset
- **THEN** сравнивается время суток из собственного offset timestamp соответствующего конца; browser/server timezone не используется, дата прибытия остаётся видимой.

#### Scenario: Carrier selection without identity invention
- **WHEN** в snapshot есть несколько названий перевозчика, null либо пустая/whitespace подпись
- **THEN** пользователь выбирает «Все», одно точное название или отдельное «Не указан»; одинаковые строки группируются только как подписи, разные названия не объединяются как одна компания.

#### Scenario: Carrier options remain tied to the whole snapshot
- **WHEN** другие фильтры скрыли все строки конкретного перевозчика
- **THEN** option этого перевозчика остаётся доступной до смены snapshot; options строятся из всей полученной выдачи. Unknown включает только null/blank, а literal source label «Не указан» остаётся отдельным exact значением; UI различает их.

#### Scenario: Reset and boundary controls
- **WHEN** пользователь сбрасывает фильтры либо время равно06:00,12:00,18:00 или00:00
- **THEN** сброс сохраняет sort и возвращает все полученные строки без HTTP; граничная минута относится только к следующему bucket. Новый принятый поиск сбрасывает и sort, и filters; исходные failed/skipped/limited-zero views не получают обещания наличия поездов.

#### Scenario: Combined filters and empty visible list
- **WHEN** одновременно выбраны фильтры и ни одна из полученных строк им не соответствует
- **THEN** условия объединяются AND, UI показывает «Среди полученных вариантов нет подходящих» и сброс фильтров, visibleCount/receivedCount; исходный complete/limited/failed/skipped outcome не переименовывается в отсутствие поездов.

#### Scenario: Change view without extending data lifetime
- **WHEN** меняется сортировка/фильтр или истекает исходный snapshot
- **THEN** изменение представления не вызывает поиск и не продлевает expiresAt; expiry очищает source rows, выбор и подробности. Новый успешный поиск сбрасывает view controls к значениям по умолчанию.
