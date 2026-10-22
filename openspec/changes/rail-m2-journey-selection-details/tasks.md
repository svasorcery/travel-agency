# Tasks

Пользователь утвердил предложение и отдельно разрешил source implementation и offline-проверки 2026-10-21. Завершены21/21задач в этой области; [личный review](personal-review.md) принят. Scope: [proposal](proposal.md); решения: [design](design.md); [verification](verification.md) сохраняет исходные RED и результаты. По отдельному разрешению 2026-10-22 actual Host/OpenAPI прошёл 3/3, mixed load/memory — 6/6 циклов и 6000/6000 полезных операций; [runtime receipt](runtime-verification.md). CI, реальные supplier calls/keys и публикация остаются непройденными этапами; commit/push/PR/deploy не разрешались и не выполнялись.

## 1. Передача утверждённого среза

- [x] 1.1 После отдельного approval передать исполнение агенту `gpt-6.1-sol`: ownership Rail, его TS/Angular consumers и перечисленных tests/docs; явно сохранить чужие изменения и исключить Trips. До работы проверить актуальный base/ancestry и записать exact SHA, branch и разрешённые проверки в receipt. Проверка: receipt не подменяет запрошенную fresh base текущим HEAD и не переносит разрешения других чатов.

## 2. Правила маршрута и локального выбора

- [x] 2.1 Добавить Core factories/values для stop facts, selected occurrence и route validation с `ErrorOr`, без внешних DTO. Одновременно unit tests: repeated stations, уникальная пара refs+instants+offsets, terminal null, missing expected time, supported seconds precision, zero/unknown/invalid dwell без округления, wrong chronology и required station identity. Проверка: пригодная частичная информация сохраняется, неверный участок даёт0 stops; README объясняет источник времени и отсутствие realtime/мест.
- [x] 2.2 Добавить чистую TS-модель sort/filter текущего snapshot. Тестами проверить3 sorts, stable ties, разные offsets/даты, все4 half-open time buckets/точные boundaries, carrier all/exact/unknown/blank/literal «Не указан», options из всего snapshot, AND, reset, filter-empty vs source-empty и immutability exact fare strings. Проверка:0 HTTP на любой view action, counts относятся к исходному snapshot; UI contract/defaults описаны рядом с Rail документацией.

## 3. Один существующий источник и fictional transport

- [x] 3.1 Реализовать Infrastructure route provider/parser для `thread`: bounded wire parsing, duplicate structural/optional fields, uid/type и calendar echoes, список2–200 stops, обязательная identity, независимые optional facts. Adapter tests исполняют production parser на самостоятельно созданных fixtures: date-only vs timestamp echo/unsupported echo form, offsetless/fractional/terminal null, midnight/разные offsets, exact/non-integral stop_time, repeated stations, malformed JSON, wrong date/UID и200/201 stops. Проверка:0 внешней сети, нет guessed timezone/start_date/реальных responses в fixtures; mapping documented.
- [x] 3.2 Расширить фиксированный allowlist YandexRailClient только методом thread и использовать тот же quota/profile/Authorization/redirect/header policy. Tests: строгое query encoding UID/from/to/date, no result_timezone/credentials-in-query, local quota, remote401/403/429/Retry-After, oversize/token/depth. Проверка:≤2MiB decoded,16KiB headers,depth32/token16KiB,0 retries/arbitrary URL; никакой второй provider/package/framework.
- [x] 3.3 Расширить явно выбранный FictionalRailTransport и Rail profile dispatch для нового route port, добавить default-off `Rail:JourneyDetailsEnabled` с epoch revocation при изменении. Проверка: позитивные/негативные cases проходят тот же parser, прежний включённый M1 и switch=false делают0detail calls, disable во время запроса запрещает второй этап/publication, real failure не вызывает fake, Production по-прежнему отвергает Fictional. Обновить fictional instructions без выдачи их за live integration.
- [x] 3.4 Добавить normalized verification capability того же Yandex provider, переиспользуя search parser и извлекая bounded locator facts внутри Infrastructure. Tests: sourceLookupDate=D при boarding=D+1, оба поддержанных hosts/формы ссылки, percent-decoding once, чужой/duplicate UID/date, userinfo/port/fragment/backslash/секрет/unknown params, missing vs malformed locator, конфликт дублей выбранного key и повреждённый locator другой строки. Проверка:0direct URL follows, фиксированный outbound host, lookup date никогда не берётся из посадки/start_date; M1 public DTO/обычный search сохраняют прежнюю форму. Нормализованный port и поддержанный locator contract документированы.

## 4. Ограниченный coordinator и общий admission

- [x] 4.1 Вынести узкий Rail-only shared admission2 из private search semaphore и подключить search/details без nested acquisition. Mixed tests: search+detail,2details,2search, третий429, отменённый uncooperative child удерживает permit до settlement. Адаптировать существующие read-only retained-state bindings RailMeasuredProfile, которые обращаются к private searches.admission, к реальному общему permit source. Проверка: прежние search deadlines/cache и profile source tests проходят с прежними assertions/thresholds; quota/reference bounds не ослаблены, нового пула/очереди нет; общий предел отражён в README, старые receipts не меняются.
- [x] 4.2 Реализовать detail coordinator: strict selection binding, свежая1mode verification page, exact match key+instant+offset, validated locator, затем1thread call. Unit tests: old/rotated UID, same number, no-match на complete/limited page, matching limited, locator missing/invalid, failed/skipped verification, этапная quota и чужой profile. Проверка:0 thread при неподтверждённом выборе/locator, cache не используется как source proof и не заполняется recheck, нет цен перепроверки в reply и нет изменения исходной карточки.
- [x] 4.3 Реализовать8s aggregate/5s child deadlines на весь child (headers/body/parser), проверки reference/profile/cancel перед этапами и publication, bounds512KiB и60s lifetime без detail cache. Controlled-clock/cancellation tests: expiry между этапами, delayed/stalled body, поздний success/fault child, A→B→A caller cancellation,0second child после timeout, cleanup после settlement, null provenance без полного source body. Проверка:0late publication/retention growth, sanitized profile-changed не содержит старых evidence, server expiry ограничен reference, consumed quota не возвращается; limits/provenance documented.
- [x] 4.4 Реализовать условную адресную invalidation исходной search cache entry только после пригодного fresh no-match; общий Rail-only key builder и compare-entry identity сохраняют новые snapshots. Tests: rotated UID→ручной обычный поиск действительно uncached; concurrent refresh не удаляется; source failure/quota/invalid locator/route failure не очищают cache; другие request/profile/generation не затронуты. Проверка:0auto search/полной очистки/recheck cache insertion, delivered search rows/цены не перезаписаны; поведение ручного обновления документировано.

## 5. HTTP, TS и telemetry contract

- [x] 5.1 Добавить `POST /api/rail/journey-details`, strict8KiB reader и API composition/OpenAPI metadata по текущему Rail pattern. Lean HTTP tests: actual routing/binding, unknown/duplicate input,400/413/429/platform5xx vs200typed source outcomes, cancellation и no-store. Проверка: request/reply соответствуют design, nullable evidence и indices согласованы, Host не получает module internals; API documentation обновлена.
- [x] 5.2 Добавить Rail detail DTO/service/strict decoder в существующий shared TS client с10s timeout и `transferCache:false`. Общие contract vectors проверяют полную matrix design§3.1: stage/evidence nullability, selection endpoint equality и2–200 stops/512KiB, safe integer dwell, state/value/reason, seconds/offsets/chronology/indices/sequence, malformed и expired replies. Profile mismatch должен использовать отдельный notice/clear path. Проверка: нельзя принять чужой участок, impossible timestamps/TTL, usable data у failed/skipped или старые evidence после epoch change; изменены только Rail exports, Trips contracts сохранены.
- [x] 5.3 Добавить uid в outbound redaction и покрыть новый endpoint emitted log/span/exception tests. Проверка success/error/cancel:0key/UID/search-key/from/to/date/timestamps/body/Authorization в telemetry; attribution имеет no-referrer, provider text plain text. Документировать только safe reasons/counters; общий security/braces backport не менять.

## 6. Собственный Rail UI

- [x] 6.1 Подключить controls sort/time buckets/carrier к текущей странице и snapshot counts. Angular tests проверяют reset/new-search semantics, limited notices, filter-empty text, сохранность цен/expiry и0 HTTP. Проверка: labels/keyboard доступны, нет заявления глобально лучшего или самого дешёвого поезда; пользовательская справка описывает «среди полученных вариантов».
- [x] 6.2 Добавить одну inline route panel с отдельным epoch/controller, выбранным участком, stop facts и независимыми states. State/component tests: close/reopen,A→B→A,sort без reload,filter hides selection,new criteria,late reply,profile/reference/search/detail expiry,min TTL, delayed timer/visibility resume и attribution failure. Проверка: freshness проверяется при open/apply/resume, expired data удаляются без polling, route failure не стирает пригодный search, no update fares, profile notice очищает оба контура; focus/live-region/атрибуция сохранены.
- [x] 6.3 Расширить fictional browser tests и fixtures: выбрать/отфильтровать поезд→открыть route, terminal null/partial times, no-match/recheck failure/route failure/quota, keyboard и узкие экраны с длинными names. Проверка в разрешённом browser-контуре:0 external/Trips/purchase calls, visible fictional label,0horizontal overflow, exact source notices и0browser storage; README demo flow актуален.

## 7. Совместная приёмка и личный review

- [x] 7.1 Расширить actual Host/OpenAPI test source и snapshot только новым Rail path/schemas; сохранить текущие routes/composition и изменения соседних модулей. Проверка в отдельно разрешённом Host/обычном CI контуре: реальный Program.cs выполняет detail request на terminal fictional transport, schema/body/no-store соответствуют5.1. Без разрешения записать runtime gate pending; lean fixture не выдавать за actual Host.
- [x] 7.2 До финального source review подготовить воспроизводимый M2 mixed search/detail measured profile и отдельные counters без запуска нагрузки. Зафиксировать design Migration Plan:6cycles,200-stop pressure, полный attempts denominator, exact useful predicate (200+complete+валидные данные),500all-mode search/500detail на cycle,2000supplier reads; failures/skips/limited/refusals не successes. Сохранить M1 reference/cache pressure, одинаковый baseline/recovery и исходные thresholds. Проверка: dry description/source tests/hash profile подтверждены; fake200failed не может пройти acceptance, measurement остаётся pending до отдельного разрешения, M1 receipts не переименованы в M2.
- [x] 7.3 Выполнить разрешённые targeted Rail unit/adapter/HTTP, TS/Angular и architecture checks, formatting, dependency-free harness и strict OpenSpec; browser/Host только в применимом разрешённом контуре. Проверка: receipt содержит exact source/diff, команды/counts/outcomes/skips и traceability каждого scenario; обычные CI gates не обходятся. Обновить Rail README/completion review с отдельными source, fictional, CI, measured и live statuses; Trips не объявлять завершённым этой работой.
- [x] 7.4 Основному агенту лично проверить итог исполнителя: selected occurrence/date/offset, local-only view actions, independent facts, shared admission/cancellation/TTL, source bounds, source authority и отсутствие Trips/sales изменений. Проверка: отдельный review receipt и закрытые defects с targeted regressions; спецификация и source не объявляются реальным доступом. Commit/push/PR/sync/archive/deploy — только по отдельному поручению.

## Traceability

Все scenarios требования входят в указанные задачи, а не заменяются одним happy-path test.

| Capability / requirement | Tasks |
|---|---|
| rail-schedule-search / Local journey ordering | 2.2,6.1,6.3 |
| rail-schedule-search / Local time and carrier filters | 2.2,6.1,6.2,6.3 |
| rail-schedule-search / Bounded execution and cancellation | 4.1,4.3,7.2 |
| rail-journey-details / Explicit bounded detail selection | 2.1,4.2,5.1,5.2,6.2 |
| rail-journey-details / Fresh confirmation before route lookup | 3.2,3.4,4.2,4.3,4.4,6.3 |
| rail-journey-details / Source supplied route locator | 3.1,3.2,3.4,4.2,5.2 |
| rail-journey-details / Route belongs to the selected occurrence | 2.1,3.1,5.2,6.2 |
| rail-journey-details / Faithful stop facts and missing times | 2.1,3.1,5.2,6.2,6.3 |
| rail-journey-details / Distinct detail outcomes and provenance | 4.2,4.3,5.1,5.2,6.2,6.3 |
| rail-journey-details / Bounded route execution and shared quota | 3.2,4.1,4.3,5.1,7.2 |
| rail-journey-details / Temporary detail authority and private transport metadata | 4.3,5.2,5.3,6.2,6.3 |
| rail-journey-details / Accessible selection and isolated delivery scope | 1.1,3.3,6.1,6.2,6.3,7.1,7.3,7.4 |

## Audit regression vectors

Конкретные обязательные cases дополняют mapping всех scenarios выше; не являются уже написанными или пройденными тестами.

| Вектор | Ожидаемый результат | Tasks |
|---|---|---|
| Boarding D+1 /locator D /source start_date иная | Request thread с locator D; выбранные stops D+1 проверяются независимо;0fallback dates | 3.1,3.4,4.2 |
| Missing locator /invalid host /duplicate encoded uid /чужой UID /query apikey | Missing=skipped, остальное failed;0thread,0URL follow/secret echo | 3.2,3.4,4.2,5.3 |
| Дубли выбранного key с разными locators /невалидный locator другой строки | Первый выбор не допускается; чужая необязательная metadata не ломает пригодный выбранный вариант | 3.4,4.2 |
| Calendar echo YYYY-MM-DD /contradicting date /unknown date form | complete /failed /limited при корректных остальных stops; даты не сравниваются с полным timestamp | 3.1,5.2 |
| Repeated station visits, same train number, wrong selected indices | Только единственная точная пара допустима; сервер и decoder отвергают неверный участок | 2.1,3.1,5.2 |
|60.0 /60.00000000000000000001 /zero/null dwell /fractional timestamp |60 /unavailable без округления /независимые0 иunknown /unavailable или selected failure | 2.1,3.1,5.2 |
| UID changed→manual same-criteria search /parallel new cache entry | Старый cache удалён; новый не удалён; manual search не получает заведомо старый snapshot | 4.2,4.4,6.3 |
| Verification network/quota failure /route failure | Старый пригодный cache не удалён, search UI живёт до своего expiry | 4.2,4.4,6.2 |
| Epoch change послеverification /malformed mismatching-profile reply | Старые evidence не публикуются; UI очищает source state через notice path | 3.3,4.3,5.2,6.2 |
| Deadline послеheaders /late success илиfault /closeA→B→A | Нет publication/second child; lease доsettlement, fault наблюдён, позднийA не принят | 4.1,4.3,6.2 |
|200stops с максимальными labels/aggregate>512KiB /201stops /compressed oversize | Bounded failed без truncation и сохранения тела | 3.1,3.2,4.3,5.2 |
| Search TTL10s /detail TTL60s /hidden tab delayed timer /attribution failure | Min TTL и проверки действий очищают данные;0polling; без атрибуции route не остаётся | 5.2,6.2,6.3 |
| Carrier literal «Не указан» vs null /filter hides all options /06:00 boundary | Exact иunknown различны; options от полного snapshot; одно корректное bucket | 2.2,6.1,6.3 |
|200failed/limited в measured useful workload /missing source calls | Не засчитываются в successes; полныйattempts/call denominator даётfailed verdict | 7.2 |
