# Rail journey details — M2 delta

## Purpose

Позволить пользователю открыть предоставленный источником маршрут выбранной прямой железнодорожной поездки, проверить её участок посадки/высадки и прочитать доступные плановые времена, не создавая обещания наличия мест или покупки.

## ADDED Requirements

### Requirement: Explicit bounded detail selection
Система SHALL получать подробности только по явному действию над текущей строкой Rail, через один Yandex.Rasp, для выбранного train/suburban. Запрос SHALL связывать исходный search request, profile epoch, ключ выбранного варианта и его departure/arrival с offsets. Произвольный UID, URL, номер поезда или название станции не SHALL заменять эту привязку.

#### Scenario: Open one current journey
- **WHEN** пользователь открывает подробности действительного результата
- **THEN** отображается одна панель выбранной поездки; prefetch остальных строк, polling, пересадки, места и покупки отсутствуют.

#### Scenario: Invalid selection or request
- **WHEN** body больше8KiB, поля неизвестны/дублированы, station refs/date/mode/ключ/timestamps невалидны либо при текущем допущенном profile каталог устарел
- **THEN** возвращается400/413, для истёкшей generation — Rail.StationSelection.Expired; supplier calls=0, raw body не отражается. Синтаксис проверяется раньше feature admission; выключенный или сменившийся profile возвращает skipped до resolve каталога.

#### Scenario: Disabled or changed profile
- **WHEN** источник/отдельная default-off capability подробностей выключены либо ожидаемый profile epoch больше не текущий
- **THEN** ответ200 skipped/source-disabled, skipped/details-disabled либо skipped/profile-changed содержит0 stops; нет вызовов поставщика или fictional fallback. Прежнее включение M1 само по себе не включает новый метод.

### Requirement: Fresh confirmation before route lookup
Перед каждым новым чтением маршрута система SHALL выполнить один свежий ограниченный поиск того же участка/даты и выбранного mode, минуя search cache, и подтвердить ровно выбранный key и времена с offsets. Только подтверждённый результат SHALL давать UID для следующего запроса. Неподтверждённый вариант не SHALL автоматически заменяться похожим поездом.

#### Scenario: Exact current selection
- **WHEN** свежая первая страница поиска содержит выбранный вариант с тем же key, instants и offsets
- **THEN** после проверки route locator выполняется один запрос маршрута с UID и source lookup date из свежего ответа и явными from/to; all-mode исходного поиска не вызывает перепроверку другого mode.

#### Scenario: Selection changed or outside refreshed page
- **WHEN** точного совпадения нет либо UID/время изменились
- **THEN** подробности skipped/selection-not-confirmed,0 thread calls; UI предлагает вручную обновить поиск, не сообщает об отмене поезда и не ищет следующие страницы или совпадение по номеру. Прежняя запись cache исходного request адресно инвалидируется, чтобы ручной поиск не повторил тот же старый snapshot.

#### Scenario: Conditional cache invalidation and concurrent refresh
- **WHEN** после начала verification другой поиск уже сохранил новый snapshot того же request либо verification failed/skipped из-за источника/квоты
- **THEN** новый snapshot не удаляется поздним no-match; источник/квота сами по себе не вызывают eviction. Подробности не записывают результаты перепроверки в search cache и не запускают автоматический новый поиск.

#### Scenario: Partial verification with a matching journey
- **WHEN** свежий search limited, но выбранный вариант подтверждён точно
- **THEN** разрешён запрос маршрута; verification.kind/reasons сохраняются отдельно от полноты маршрута, исходная выдача не становится полной.

#### Scenario: Recheck fails or is not admitted
- **WHEN** свежий поиск failed либо skipped/quota
- **THEN** подробности имеют соответственно failed либо skipped с этапом verification и причиной;0 thread calls, неизвестность не становится «остановок нет».

### Requirement: Source supplied route locator
Система SHALL брать lookup date только из проверенного `thread_method_link` выбранной строки свежего поиска и связывать его UID с подтверждённым вариантом. Ссылка SHALL разбираться только как входные данные; исходящий запрос SHALL собираться заново на фиксированном HTTPS host. Дата посадки, optional start_date, browser date и соседние даты не SHALL подставляться вместо source locator.

#### Scenario: Boarding occurs after the route lookup date
- **WHEN** выбранная посадка имеет дату D+1, а свежий валидный locator содержит date=D
- **THEN** thread получает date=D, from/to — выбранные станции; принадлежность поездки подтверждается по endpoint timestamps в stops, а не равенством lookup date и даты посадки.

#### Scenario: Missing or invalid locator
- **WHEN** locator отсутствует/null либо непустой locator повреждён, содержит чужой UID, host/path, duplicate query keys, секрет или лишние параметры
- **THEN** отсутствие даёт skipped/route-locator-unavailable, повреждение — failed/invalid-route-locator, stage=verification и0 thread calls; M1 search/его цены сохраняются, URL не исполняется и не публикуется.

#### Scenario: Safe locator normalization
- **WHEN** locator имеет поддержанный официальный host, точный thread path и единственные uid/date
- **THEN** UID/date декодируются и валидируются один раз, исходный URL отбрасывается; новый запрос использует только эти значения и проверенные параметры приложения, без redirects, cookies или переноса query credentials.

### Requirement: Route belongs to the selected occurrence
Система SHALL проверять source UID/type, порядок остановок и ровно одну пару посадка→высадка, соответствующую source-qualified refs и подтверждённым departure/arrival instants с offsets. Весь полученный список SHALL сохранять порядок, включая остановки за выбранным участком; выбранный участок SHALL быть выделен. Номер/название не SHALL использоваться для matching.

#### Scenario: Whole supplied route and selected segment
- **WHEN** источник возвращает остановки до посадки и после высадки
- **THEN** виден весь предоставленный маршрут с индексами выбранного участка; времена первой/последней станции не подменяют времена поездки пользователя. Промежуточные станции не добавляются в каталог выбора.

#### Scenario: Repeated station visits
- **WHEN** станция повторяется в нитке
- **THEN** выбирается ровно одна пара в правильном порядке по refs и временам; при неоднозначности ответ failed/route-mismatch, без выбора первого имени или первой пары.

#### Scenario: Missing or contradictory route identity
- **WHEN** stops пуст, обязательная identity любой остановки повреждена, выбранная пара отсутствует, type/UID чужой либо известные времена противоречат хронологии
- **THEN** ответ failed с безопасной причиной и0 stops; повреждённые строки не удаляются для создания кажущегося полного маршрута.

#### Scenario: Calendar echoes are not stop timestamps
- **WHEN** thread содержит departure_date/arrival_date как календарные даты
- **THEN** они сравниваются с локальными датами выбранных концов, не с полными timestamps; точные instants подтверждаются только stops. Известная противоречащая дата даёт failed/route-mismatch, отсутствующая дата не отменяет корректную пару, нераспознанный формат даёт limited/echo-date-unavailable без угадывания времени.

#### Scenario: Duplicate structural identity and optional facts
- **WHEN** UID/type/stops/station identity/echo fields дублируются либо дублируется optional факт остановки
- **THEN** структурные дубли дают failed; optional факт становится unavailable/limited, но если это выбранный endpoint timestamp, маршрут failed. Последнее значение не выбирается.

#### Scenario: Unsupported interval detail
- **WHEN** ответ выбранной scheduled поездки оказался interval-only
- **THEN** ответ failed/unsupported-route-shape; точные остановочные времена не изобретаются и исходный поиск не превращается в empty.

### Requirement: Faithful stop facts and missing times
Система SHALL представлять arrival/departure как reported timestamp с явным offset либо not-provided/unavailable с причиной. Offsetless время не SHALL получать UTC/Moscow/browser timezone. Стоянка SHALL быть независимым reported/not-provided/unavailable значением секунд; неизвестная стоянка не SHALL становиться нулём. Подробности SHALL оставаться плановым расписанием.

#### Scenario: Terminal nulls and explicit zero dwell
- **WHEN** первая остановка не имеет arrival, последняя не имеет departure, а другая имеет stop_time=0
- **THEN** терминальные времена показываются как не предоставленные и сами по себе не делают маршрут limited; стоянка0 показывается как явный факт, null не отображается нулём.

#### Scenario: Damaged optional intermediate time
- **WHEN** время остановки отсутствует (кроме первого arrival/последнего departure), невалидно или не содержит offset
- **THEN** соответствующее поле not-provided/unavailable с причиной, остановка и независимые валидные поля сохраняются, kind=limited. Неизвестное время не вычисляется из duration/start_date/соседних остановок.

#### Scenario: Required boarding and alighting times
- **WHEN** departure выбранной посадки либо arrival выбранной высадки не позволяет точное сравнение с подтверждённым вариантом
- **THEN** ответ failed/route-mismatch с0 stops, даже если названия и номер похожи.

#### Scenario: Inconsistent dwell or platform
- **WHEN** stop_time отрицателен/дробный/overflow или расходится с разностью двух валидных времён одной остановки, либо optional platform повреждена
- **THEN** затронутый optional факт unavailable с причиной и kind=limited; исправные времена/станция сохраняются. Missing dwell не вычисляется автоматически, платформа не угадывается.

#### Scenario: Empty optional platform
- **WHEN** platform отсутствует/null/пустая строка
- **THEN** она not-provided и сама по себе не делает маршрут limited; пустое значение не показывается как известный путь.

#### Scenario: Exact numeric and timestamp representation
- **WHEN** источник даёт stop_time=60.0, дробное/округляемое число либо время с дробными секундами
- **THEN** математически целое60.0 принимается как60; непредставимое точным safe integer значение unavailable без округления. Scheduled stop times поддерживают целые секунды как M1: неподдержанная точность не обрезается, optional поле unavailable/limited, выбранный endpoint — failed.

### Requirement: Distinct detail outcomes and provenance
Система SHALL возвращать200 typed envelope complete/limited/skipped/failed для завершённых допустимых detail requests, сохраняя binding, причины, этап verification/route и собственные времена получения. Complete SHALL означать пригодный предоставленный маршрут, а не полноту реального движения. Successful-empty для выбранного маршрута не SHALL существовать.

#### Scenario: Complete and limited details
- **WHEN** маршрут подтверждён и обязательные факты пригодны
- **THEN** complete содержит2–200 stops и boardingIndex<alightingIndex; повреждение/отсутствие ожидаемых необязательных фактов даёт limited с теми же обязательными гарантиями и явными причинами.

#### Scenario: Failure after successful verification
- **WHEN** перепроверка прошла, а thread получил timeout/401/403/429/malformed response
- **THEN** ответ failed с этапом route и0 stops, verification evidence сохраняется; локальный отказ quota перед thread является skipped. Строка исходного поиска остаётся пригодной до своего expiresAt.

#### Scenario: Detail fetch does not refresh fares
- **WHEN** подробности получены позже исходного поиска
- **THEN** verificationReceivedAt и routeReceivedAt различаются, цены/receivedAt/expiresAt исходной карточки не перезаписываются; цены перепроверки не публикуются как новые quotes, sourceUpdatedAt остаётся неизвестным.

#### Scenario: Profile change while source work is running
- **WHEN** epoch отозван между verification и final publication
- **THEN** ответ skipped/profile-changed использует текущий profile,0 stops, verification=null, routeReceivedAt=null, expiresAt=null; сведения старого profile не публикуются. Клиент распознаёт notice и очищает старые search/detail данные, а не оставляет их под обычной ошибкой decoder.

#### Scenario: Invalid detail envelope
- **WHEN** complete/limited reply имеет чужой request/profile, неверные indices/endpoint times, пропущенную verification, невозможные receivedAt/expiresAt либо state/value/reason противоречат друг другу
- **THEN** строгий decoder отклоняет весь ответ; несовпадающий профиль вызывает отдельное очищение source state, неполный UI не строится из непроверенной части.

### Requirement: Bounded route execution and shared quota
Операция подробностей SHALL занимать один общий с поиском permit, выполнять не более2 последовательных supplier GET, с5s/child и8s/operation, без очереди/retry/pagination/reference loads. Каждый dispatch SHALL учитывать общий source quota. Отмена/смена profile/expiry SHALL запрещать поздний результат; permit SHALL удерживаться до фактического завершения работы.

#### Scenario: Saturation and uncertain outcomes
- **WHEN** общая ёмкость2 занята либо dispatched request отменён/потерял ответ
- **THEN** новый запрос получает429 без HTTP; использованная quota не возвращается вместе с permit, restart не восстанавливает разрешённый остаток.

#### Scenario: Cancellation between or during stages
- **WHEN** пользователь закрывает панель, выбирает другой рейс или child игнорирует cancellation
- **THEN** UI/HTTP ожидание прекращается, новый этап после отмены не запускается, поздняя публикация запрещена; фактически незавершённый child продолжает занимать общий permit.

#### Scenario: Bounded payload
- **WHEN** thread превышает2MiB decoded,16KiB headers,depth32/token16KiB,200 stops либо нормализованный reply превышает512KiB
- **THEN** ответ bounded failed/output-limit с0 stops, без усечённого complete, follow-up URL или сохранения тела. Ref≤128UTF-8 bytes, UID≤100characters, labels≤1024UTF-8 bytes.

#### Scenario: Body and parser remain inside the operation deadline
- **WHEN** headers получены быстро, но body чтение/проверка продолжаются до child/aggregate deadline
- **THEN** operation не публикует просроченный результат и не начинает второй этап; permit удерживается до фактического окончания текущей работы. Лимиты относятся к полному чтению/валидации, не только к получению headers.

### Requirement: Temporary detail authority and private transport metadata
Подробности SHALL жить только во временной памяти текущего запроса/одной панели, максимум60s от routeReceivedAt и не позже reference expiry; UI SHALL дополнительно ограничивать их исходным search expiry. Server detail cache, persistent storage, export и hidden refresh запрещены. Атрибуция, no-store и защита route/UID/key от telemetry SHALL сохраняться.

#### Scenario: Earlier snapshot expiry wins
- **WHEN** search истекает через10s, а detail reply допускает60s
- **THEN** UI очищает подробности через10s; expiry не вызывает supplier query. Истечение reference/profile инвалидирует оба контура.

#### Scenario: Expiry during an unfinished request or suspended view
- **WHEN** исходный snapshot истёк до detail reply либо timer был задержан скрытой вкладкой
- **THEN** перед каждым open/apply/render после возобновления проверяется expiry, stale reply не применяется; первый доступный UI turn очищает данные и отменяет request. Background timer сам по себе не является доказательством свежести.

#### Scenario: Reopen and delayed response
- **WHEN** пользователь закрывает и вновь открывает строку либо переключается A→B→A
- **THEN** закрытая detail state удаляется; новое явное открытие имеет собственный epoch, максимум одну активную панель и отдельный bounded request; поздний первый A не принимается.

#### Scenario: Attribution and secret redaction
- **WHEN** отображается маршрут или фиксируется success/error/cancel
- **THEN** безопасные banner/text/link доступны рядом с подробностями, source text выводится plain text; bodies, UID, search key, станции, дата и Authorization не попадают в logs/spans/errors/browser storage/Referer. Raw supplier HTML/URL не исполняются.

#### Scenario: Route attribution becomes unavailable
- **WHEN** атрибуция текущего reference признана недействительной или её разрешённое отображение потеряно
- **THEN** details очищаются и in-flight запрос отменяется вместе с source state; маршрут не остаётся без обязательных source notices.

### Requirement: Accessible selection and isolated delivery scope
Система SHALL предоставлять доступные controls фильтров/сортировки и панель маршрута с явными loading/limited/skipped/failed/expired состояниями. Изменение SHALL быть ограничено Rail и его существующими web/HTTP consumers; Trips, второй поставщик, пересадки и покупка не SHALL добавляться. Source/fictional/CI и реальный доступ SHALL иметь отдельные статусы приёмки.

#### Scenario: Filter hides the opened journey
- **WHEN** локальный фильтр скрывает выбранную строку
- **THEN** панель закрывается, запрос отменяется и state удаляется; изменение только порядка оставляет ту же выбранную key без нового HTTP.

#### Scenario: Keyboard and mobile route reading
- **WHEN** пользователь работает клавиатурой или на узком экране
- **THEN** controls имеют labels, кнопка раскрытия aria-expanded/controls, статусы доступны live-region, закрытие возвращает фокус к доступному trigger либо результатам; длинные станции не создают горизонтальный overflow, смысл не зависит только от цвета.

#### Scenario: Fictional implementation proof
- **WHEN** автономные parser/HTTP/TS/browser проверки проходят на самостоятельно составленных fixtures
- **THEN** это подтверждает только source/fictional contract,0 real network, visible fictional label и отсутствие fallback; реальные account rights/квоты/banner/rollout остаются отдельным выбором владельца экземпляра. M1 receipts не выдаются за M2 acceptance.

#### Scenario: No Trips or sales side effects
- **WHEN** пользователь фильтрует или открывает маршрут
- **THEN** Trips endpoints/storage/events, покупка/резервация/пассажирские данные и другие поставщики не вызываются; никакой временный Rail→Trips контракт не создаётся.
