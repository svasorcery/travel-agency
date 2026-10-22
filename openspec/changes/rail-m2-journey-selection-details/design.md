# Design

## Context

Мотивация и согласованный scope: [proposal](proposal.md). Нормативное поведение: [выбор из расписания](specs/rail-schedule-search/spec.md), [подробности](specs/rail-journey-details/spec.md). Это planning-only, 2026-10-21; исходная проверенная база `a8721effd9056459d217966083816455fbea599d`, без переключения ветки.

### Наблюдаемое в репозитории

- [Текущий контракт](../../specs/rail-schedule-search/spec.md) и [Rail README](../../../modules/rail/README.md): один источник, прямые станции РФ, train/suburban, ограниченная первая страница, временная RAM и независимые билетные факты.
- [RailSearchService](../../../modules/rail/Travel.Modules.Rail.Application/RailSearchService.cs): private semaphore2, 8s aggregate/5s child, search cache, profile/generation checks. Новые подробности должны разделять этот admission, а не создавать ещё два permits.
- [RailFacts](../../../modules/rail/Travel.Modules.Rail.Core/RailFacts.cs): key состоит из source/UID/mode/refs/UTC ticks. [Schedule parser](../../../modules/rail/Travel.Modules.Rail.Infrastructure/YandexRailScheduleParser.cs) даёт времена с offsets, строковые цены, carrier label и краткий stops text; structured stops ещё нет.
- [YandexRailClient](../../../modules/rail/Travel.Modules.Rail.Infrastructure/YandexRailClient.cs): фиксированный HTTPS host, allowlist методов, Authorization, общий quota ledger, no redirect/cookies. [Redaction](../../../modules/rail/Travel.Modules.Rail.Infrastructure/RailUrlRedactionContributor.cs) сейчас перечисляет from/to/date; новый uid также нужно закрыть.
- [RailState](../../../apps/web/src/app/rail/rail-state.ts) и [template](../../../apps/web/src/app/rail/rail-page.component.html): существующие request epochs, cancellation и expiry; строки выводятся в source order, локальных фильтров и route panel нет.
- Tests уже разделены на [unit](../../../tests/rail/Travel.Modules.Rail.Tests.Unit), [adapter/HTTP](../../../tests/rail/Travel.Modules.Rail.Tests.Integration), [browser demo](../../../tests/travel-e2e/demo/rail-search.spec.ts) и [actual Host](../../../tests/Travel.Host.Tests.Integration/Rail). Они являются точками расширения, а не основанием повторять старый технический аудит.

### Проверенные публичные источники и пределы доказательства

Прочитана только документация; provider endpoints, даже без ключа, не вызывались.

| Источник, дата чтения 2026-10-21 | Установленный факт / предел |
|---|---|
| [Yandex: список станций следования](https://yandex.ru/dev/rasp/doc/ru/reference/list-stations-route) | `thread` принимает UID/from/to/date; UID требуется получать актуальным перед чтением нитки. Stop arrival/departure — времена; departure_date/arrival_date описаны как даты выбранных концов; stop_time — секунды. Пример смешивает null/offsetless времена: это не разрешение угадывать timezone. |
| [Yandex: поиск между станциями](https://yandex.ru/dev/rasp/doc/ru/reference/schedule-point-point) | Свежий `thread_method_link` описан как URL получения нитки для UID; примеры включают date. Это источник lookup date, а не доказательство равенства дате посадки или optional sourceReportedDate. Сам URL не выполняется. |
| [Условия API](https://yandex.ru/legal/rasp_api/ru/) | Сохраняются требования к доступу, атрибуции и ограничениям обработки/временного хранения. Документация не подтверждает права конкретного экземпляра или его квоту. Реальное использование новых представлений и метода проверяет владелец экземпляра отдельно. |

Совместимость конкретного real ответа с parser не проверена. Ни текст thread.date, ни общий search.start_date не дают основания считать дату посадки идентификатором дня полной нитки. M2 получает дату lookup из параметров проверенного свежего thread_method_link. В документационных примерах встречаются внутренние несогласованности UID/дат; примеры не копируются как положительные fixtures. Поддержанные формы locator/echo и реакции на неоднозначность ниже — явные решения нашего контракта. Если реальный source shape окажется вне них, capability остаётся выключенной до отдельной проверки; offline proof не является live acceptance.

## Goals / Non-Goals

**Goals:** сохранить существующий поиск и сделать его результаты удобными для выбора; получить один ограниченный маршрут с проверяемой привязкой; не увеличить скрыто source concurrency, хранение или authority.

**Non-Goals:** перечислены в proposal. В частности, не создаём generic ranking/provider engine, station crosswalk, новый published contract для Trips или новый слой persistence. Промежуточные остановки не расширяют каталог выбора: география/тип полного рейса за выбранным участком не выводятся из РФ endpoints, иностранная либо неизвестная география source stop не угадывается и сама по себе не удаляет остановку.

## Decisions

### 1. Локальная модель представления

Одна чистая функция над immutable `RailSearchResult.items` возвращает visible rows и counts. Исходные DTO, tickets, order index, mode evidence и expiry не мутируются. Никаких вызовов даже к собственному backend при изменении view controls.

Предложенные defaults для review:

- sort: departure instant ASC (default), arrival instant ASC, durationSeconds ASC. Tie-breaker — исходный индекс snapshot; сортировка не по строке ISO и не по локальному времени суток. Scheduled times сохраняют M1 точность целых секунд, поэтому Date.parse не теряет различия поддержанных instants; дробная точность не обрезается для принятия.
- departure/arrival bucket: all (default), night `[00:00,06:00)`, morning `[06:00,12:00)`, afternoon `[12:00,18:00)`, evening `[18:00,24:00)`. Из timestamp берётся его собственная HH:mm:ss часть; midnight принадлежит night независимо от даты. Непустая дата прибытия не скрывается.
- carrier: discriminated `all | unknown | exact(value)`, максимум одна выбранная подпись. Options получаются из полного текущего snapshot, а не уже отфильтрованной части. Ordinal exact comparison, без trimming/casefold/склейки компаний; пустая/whitespace подпись трактуется как unknown только для control. Literal название «Не указан» остаётся exact option с пояснением «название источника», а unknown обозначает отсутствие значения. Raw source label не переписывается.
- фильтры AND; «Сбросить фильтры» оставляет sort, новый принятый search сбрасывает и sort, и фильтры. До принятия нового результата старые строки очищаются существующим механизмом.

Показываем visibleCount из receivedCount и исходный source outcome. При hidden selected row закрываем подробности, при сортировке сохраняем key. Исходный complete-empty отличается от filter-empty и limited-zero.

Альтернатива server-side сортировке добавляла бы запросы и контракт без новых данных. Произвольные временные интервалы/множественные carrier filters оставлены вне этого среза: фиксированные buckets покрывают сценарий и имеют простые тестируемые границы. Цена не подходит для сравнения из-за неизвестного scope и не участвует.

### 2. Явная перепроверка и route read

Добавляем `Rail:JourneyDetailsEnabled=false` как отдельный capability switch. Он не заменяет существующий Rail.Enabled/real admission; оба допуска обязательны. Включённый ранее M1 не начинает использовать новый метод после обновления кода. Явное fictional demo может включить switch в своём профиле, без fallback. Смена switch обновляет общий profile epoch и отзывает in-flight работу, поэтому disable не обходится уже начатым запросом. Дополнительные account flags/framework не вводятся.

Новый Application coordinator принимает нормализованный запрос, а Infrastructure реализует provider capability. Последовательность одной операции:

1. Validate input; взять текущий profile; сначала epoch mismatch, затем source disable/details disable возвращают skipped с соответствующей причиной. Resolve существующий reference bundle без загрузки. Взять один общий permit поиска/подробностей без ожидания.
2. Выполнить один свежий поиск выбранного mode и исходных from/to/date через normalized verification port. Он использует существующий search wire parser и transport, дополнительно извлекая route locator metadata внутри Infrastructure. Не вызывать cached `RailSearchService.SearchAsync`, не запрашивать второй mode или следующие страницы. Существующий M1 search DTO/публичный wire contract не меняется.
3. Application находит точное ordinal совпадение key среди пригодных свежих строк и проверяет departure/arrival по instant **и offset**. Client key не парсится в UID. Нет match — skipped/selection-not-confirmed и условная адресная cache invalidation по правилам ниже; это не свидетельство отмены/отсутствия поезда. UID rotation намеренно требует ручного нового поиска.
4. Для совпавшей строки проверить locator, затем cancellation/profile/reference validity. Infrastructure собирает новый запрос на фиксированном HTTPS host: `thread`, `format=json`, `lang=ru_RU`, свежий `uid`, `date=sourceLookupDate` из locator и точные from/to. Исходный URL не выполняется; ни его host, ни произвольные параметры не переносятся. Authorization остаётся header, undocumented result_timezone не добавляется.
5. Проверить route binding/shape, сформировать bounded envelope, ещё раз проверить профиль/reference/deadline до публикации. После выбора сохраняются только нужный normalized journey/locator и verification evidence; прочие строки/сырой body перепроверки не удерживаются на время показа. Полученные при перепроверке fares не переносятся в исходный snapshot и не возвращаются route API.

Совпадение на limited search page допустимо: выбранная строка подтверждена, а `verification.kind/reasons` сохраняет предел проверки. Нельзя превратить «не найдено на первой странице» в подтверждение отсутствия.

Альтернативы: старый UID из cache нарушает требование свежести; поиск по номеру может выбрать другой рейс; обход всех страниц расходует дополнительную квоту. Поэтому выбран точный отказ с ручным обновлением. Для forged key не требуется серверная сессия/подпись: это anonymous read-only API, а все публикуемые source facts заново проверяются по разрешённому участку и общему budget. Присланные timestamps не становятся trusted source facts.

#### 2.1. Locator без произвольных URL и новой интеграции

Узкий `IRailJourneyVerificationProvider` возвращает нормализованную страницу поиска и bounded locator facts, связанные с её keys; ≤50 input rows. Application выбирает journey, а Infrastructure не решает бизнес-matching. `RailRouteLocator` содержит source, подтверждённый UID и `SourceLookupDate`, не URL, DTO или функцию HTTP. Это дополнительная capability того же Yandex provider: новый source/provider framework не создаётся. M1 parser logic переиспользуется; locator facts не добавляются в ScheduledJourney public DTO, search cache или TS search decoder.

Поддержанный parser locator: строка≤2048UTF-8 bytes, точный путь `/v3.0/thread/`; HTTPS URL либо документированная форма `api.rasp.yandex-net.ru/...` без схемы. Разрешены только точные ASCII hosts `api.rasp.yandex-net.ru` и прежний `api.rasp.yandex.net` (второй только как входной идентификатор, исходящие запросы всегда к первому). Без whitespace/control/backslash/userinfo/port/fragment, HTTP и `//`-формы. Query содержит ровно один uid и один date, порядок любой. Percent encoding валидируется и декодируется один раз; duplicate keys проверяются после decoding, raw `+` отклоняется (literal plus передаётся `%2B`), неизвестные поля, включая apikey, не разрешены. UID обязан ordinal совпасть с UID той же свежей строки; date — строгая существующая YYYY-MM-DD.

`SourceLookupDate` не объявляется boarding date или run-start, не сравнивается с ними по неописанному правилу и не ограничивается пользовательским today−1…today+30: это источник выбранного run, который мог начаться раньше. Окончательную принадлежность доказывают выбранные stop instants. Missing/null locator — skipped/route-locator-unavailable; пустой/malformed/unsafe — failed/invalid-route-locator, оба stage=verification и0 thread calls. Повреждённый locator другой строки не ломает выбранную строку; конфликтующие locator facts при дублях выбранного key дают invalid-route-locator, без last-wins. На source start_date или соседние даты fallback отсутствует.

#### 2.2. Ручное обновление действительно может получить свежий поиск

Перед verification coordinator может захватить identity текущей cache entry **только** для условного удаления; её rows/UID не заменяют свежую проверку. Key cache строится тем же Rail-only helper по original request (включая all), profile и generation. При пригодном fresh search без exact selected match удаляется только захваченная entry, содержащая исходный выбор, и только если в cache всё ещё именно эта entry. Сравнение identity/version, не structural equality, не позволяет позднему ответу удалить более новую запись.

Failed/skipped verification, invalid locator и route failure не вызывают eviction. Нет capture/entry — удалять нечего. Не очищать общий cache, не вставлять recheck page и не менять already-delivered snapshot; пользователь сам запускает следующий обычный поиск. Это устраняет цикл stale cache→тот же UID→тот же отказ и сохраняет M1 успешный cache при временной сетевой ошибке.

### 3. Контракт HTTP и source-normalized values

Добавляем `POST /api/rail/journey-details`, anonymous/no-store, strict8KiB reader с rejection неизвестных/duplicate fields по существующему Rail pattern. Body:

```json
{
  "search": {
    "from": { "source": "yandex-rasp", "code": "s100001" },
    "to": { "source": "yandex-rasp", "code": "s100002" },
    "catalogueGeneration": "11111111111111111111111111111111",
    "departureDate": "2026-11-01",
    "transport": "all"
  },
  "expectedProfileEpoch": "22222222222222222222222222222222",
  "selection": {
    "key": "opaque-key-from-current-search",
    "transport": "train",
    "departure": "2026-11-01T21:00:00+03:00",
    "arrival": "2026-11-02T05:00:00+03:00"
  }
}
```

Это вымышленные иллюстративные значения, не готовый provider fixture. selection.transport должен соответствовать search.transport либо его all; key непустой, без control characters, ≤1024UTF-8 bytes, сравнивается целиком. expectedProfileEpoch/catalogueGeneration — текущий формат N GUID. Core validators принимают дату/станции по текущим M1 правилам; time parser требует явный offset, departure local date равна search date, arrival>departure. Клиент не передаёт цену, UID отдельным полем, source URL или expiresAt как authority.

Нормализованный reply (поля, а не wire DTO):

| Поле | Правило |
|---|---|
| kind, reasons, stage | complete/limited/skipped/failed; safe reason codes; stage=admission/verification/route; complete/limited только stage=route |
| request | Проверенный исходный body как binding; не включать его в telemetry |
| source, profile, profileEpoch | yandex-rasp и текущий real/fictional profile; mismatch очищает source state клиента |
| verification | null если verification не начата либо profile отозван; иначе transport/kind/reasons/receivedAt, как у normalized mode. Нет journeys/tickets/locator/raw DTO |
| routeReceivedAt, expiresAt, completedAt | nullable source receive/expiry и серверное завершение; получение HTTP-ответа не объявляется временем обновления источником. Для failed/skipped expiresAt=null |
| stops | 2–200 при complete/limited, [] при skipped/failed; порядок источникa |
| boardingIndex, alightingIndex | valid indices с boarding<alighting для usable; null/null для unusable |
| attribution | безопасный descriptor reference bundle для usable; null для unusable, UI сохраняет прежнюю атрибуцию списка |

`Stop`: sequence index, station `{source,code,name}`, arrival/departure `{state: reported|not-provided|unavailable, value: offset timestamp|null, reason: string|null}`, dwellSeconds по той же state-схеме с integer|null, platform по той же схеме с string|null, reasons. SourceUpdatedAt/IANA не изобретаются. `reported` требует value и reason=null; остальные требуют value=null и конкретную reason. Для первой arrival/последней departure используем not-provided/terminal-not-provided; null у иных timestamps — not-provided/missing-time. Missing dwell/platform (включая пустую platform) — not-provided и само по себе не понижает route; malformed optional value или missing ожидаемый timestamp (всё, кроме первого arrival/последнего departure) — limited, если не нарушена обязательная selected-occurrence проверка.

400 — malformed/selection invalid/stale catalogue;413 — body;429 — process admission;5xx — platform failure. Supplier failure остаётся200 typed envelope. Допустимое получение пустого thread не является complete-empty. UI timeout10s включает серверный8s budget.

#### 3.1. Согласованность reply и decoder

| Исход | Обязательная форма |
|---|---|
| Admission skipped | verification=null, routeReceivedAt/expiresAt=null, stops=[], indices=null, attribution=null |
| Verification failed/skipped/no-match/locator-unavailable | verification задана, stage=verification, routeReceivedAt/expiresAt=null,0stops; kind и reason соответствуют причине, а verification.kind сохраняет фактический outcome search |
| Route failed/skipped | stage=route, verification.kind complete/limited с receivedAt;0stops/null indices/expiry/attribution. routeReceivedAt только если весь bounded body был получен; отсутствие ответа не получает выдуманную дату |
| Route complete/limited | stage=route, verification usable и receivedAt задан, routeReceivedAt≤completedAt<expiresAt≤routeReceivedAt+60s; expiry≤reference expiry. Все binding/stop/index invariants выполнены |
| Profile changed в любой момент | skipped/profile-changed, stage=admission, текущие profile/epoch,0stops, verification/routeReceivedAt/expiresAt/attribution=null; старые source facts отброшены |

Для usable reply: verification.transport совпадает с selection.transport; verification.receivedAt≤routeReceivedAt; stops[boardingIndex].station и departure совпадают с request selection, stops[alightingIndex] — station и arrival; indices — целые, sequence ровно0…n−1. Каждая station.code соответствует `^s[0-9]+$`, source=yandex-rasp. Нет повторной нормализации key. reported fields имеют value и reason=null; non-reported — value=null и допустимую safe reason. Complete не содержит route-level defects; limited имеет хотя бы одну actual field/metadata причину. Отдельные verification limited reasons не превращают полный route в неполный.

Client сначала проверяет минимальный schema/profile binding; любой несовпадающий profile/epoch вызывает существующий `Rail.Profile.Changed` путь очистки, после чего данные не применяются. Обычная contract error не должна оставлять старый source state при profile notice. Для совпавшего profile строго проверяются request, весь envelope, время/nullable evidence, refs/indices/chronology/selection и размер; нарушение отклоняет весь reply. Reasons — bounded unique safe codes (≤64/128bytes на набор), optional metadata/stop reasons не несут raw supplier text. Сервер обеспечивает512KiB до отправки; decoder проверяет нормализованный размер, но это не заявление о streaming limit до JSON parsing в HttpClient.

### 4. Проверка маршрута и времени

Парсер Infrastructure читает только ограниченный JSON, проверяет duplicate критические поля и выдаёт нормализованные значения. Core отвечает за допустимый порядок/selected occurrence и независимость фактов.

- Required: uid/type, stops array2–200, station.code `^s[0-9]+$` каждой строки, непустое корректное name. Если station.type предоставлен, он должен быть station, не settlement; station_type не подменяет type. Station code не сопоставляется с именем или каталогом autocomplete: новые промежуточные станции не становятся user-selectable refs.
- `uid` и `transport_type` равны перепроверенному варианту; interval-only отклоняется. Optional top-level from/to при наличии обязаны соответствовать запросу. departure_date/arrival_date — календарные echoes: строгая YYYY-MM-DD сравнивается с локальной датой выбранного конца. При полноценном offset timestamp поддерживается только календарная часть в его собственном offset, не дополнительное доказательство instant. Missing/null не меняет route outcome; иной непустой формат — limited/echo-date-unavailable; распознанная противоречащая дата — failed/route-mismatch. Точные instants/offsets доказывают только stops, optional start_date не подменяет эту проверку.
- Найти все pairs source refs с boarding<alighting и точными endpoint times/offsets; должна остаться одна. Повторные посещения станций допустимы; отсутствие/неоднозначность дают failed/route-mismatch. Все stops возвращаются, участок выделяется индексами.
- Проверять хронологию всех известных instants по source order (arrival затем departure); равенство разрешено, отрицательный ход времени нет. Отсутствующие промежуточные timestamps не восстанавливаются. Offsetless/невалидный optional timestamp становится unavailable; его raw значение не отправляется клиенту. Если такой timestamp нужен для выбранной посадки/высадки — fail closed.
- Scheduled arrival/departure поддерживают ту же строгую форму целых секунд с Z/±HH:mm, что M1 ParseTime/TS decoder. Fractional/offsetless/невозможное время не округляется: optional field unavailable/limited, необходимый selected timestamp — failed. Server/TS одинаково проверяют реальную дату, HH:mm:ss и offset≤14:00; Z и+00:00 эквивалентны по instant+offset.
- Source `stop_time`: математически exact integer≥0 в диапазоне JS safe integer, проверенный до numeric conversion/rounding (60.0=60 допустимо,60.00000000000000000001 не становится60); при двух валидных timestamps должен равняться departure-arrival. Несогласованный dwell — unavailable/invalid-dwell, limited; валидные времена остаются. Не вычислять отсутствующий dwell из соседних/собственных timestamps. `duration` из thread не используется для реконструкции времён.
- Обязательная identity/label за лимитом — failed; optional platform за лимитом — unavailable с reason, limited. Unknown extra fields не исполняются. Дубли structural/identity/echo fields дают failed; duplicate optional stop fact — unavailable/limited, кроме selected endpoint timestamp, который обязателен. Container duplicate station/stops нельзя понизить до ошибки одной подписи.

Такой выбор сохраняет пригодные остановки при локальном пробеле и не обещает достоверный момент без offset. Альтернатива угадывать часовой пояс по РФ/соседней станции исключена. Полностью строгий отказ на любой null ухудшил бы обычный маршрут с терминальными остановками.

### 5. Bounds, lifecycle и cache

Небольшой `RailReadAdmission` внутри Application заменяет private semaphore поиска и обслуживает оба outer coordinators: всего2 operations/process. Не добавлять general-purpose scheduler. Search сохраняет свой parallel≤2 flow; detail выполняет ≤2 sequential children, каждый≤5s, aggregate≤8s. Между этапами нет gap, допускающего новый child после deadline/revocation. Autocomplete сохраняет собственные прежние bounds; общий source quota охватывает все методы.

Permit нельзя освобождать только по WaitAsync timeout: caller прекращает ожидание вовремя, а bounded cleanup удерживает lease до фактического завершения запущенного child без поздней публикации. Две active operations означают≤4 одновременных children для двух all-mode search; detail имеет≤1 фактически активный child. Detail не вызывает другой admitted coordinator и не занимает второй permit. При «зависшем» первом child thread не запускается. Deadline покрывает headers, body, mapping и publication; bounded parser проверяет cancellation между остановками, а final authority check обязателен даже после синхронного parse. Тестировать удержание permits и их освобождение после controlled completion, включая stalled body и fault после отмены (exception наблюдается cleanup).

| Объект | Предложенный предел |
|---|---|
| Detail input / child headers | 8KiB /16KiB |
| Thread decoded JSON /depth/token | 2MiB /32 /16KiB |
| Stops /public reply | 200 /512KiB |
| Ref /UID /name или optional label | 128UTF-8 bytes /100characters /1024UTF-8 bytes |
| Detail UI lifetime | min(routeReceivedAt+60s, reference expiry, исходный search expiry) |

Это инженерные limits предложения, не тарифы/разрешённые TTL поставщика. Server не кэширует details/verification/locator и не архивирует raw response; разрешена только условная eviction прежнего search snapshot из§2.2. Серверная routeReceivedAt фиксируется при получении полного bounded thread body до parser, даже если parser затем отклонит его; если полного body нет, она null. expiresAt=min(routeReceivedAt+60s,reference expiry); UI дополнительно применяет исходный search expiry. Любое уже истёкшее usable response отклоняется; TTL не sliding.

Каждый dispatch дебетует текущий RailQuotaLedger; не резервировать/возвращать «неиспользованные попытки» после uncertain outcome. Local quota до этапа — skipped, remote429 — failed с сохранённым Retry-After cooldown. Profile/generation revalidation до каждого dispatch и final publication; current profile mismatch отдаёт sanitized skipped/profile-changed по§3.1 и очищает старый state. Если bundle истёк во время работы, вернуть400 Rail.StationSelection.Expired без данных. Никакого automatic catalogue refresh из route request.

### 6. UI и границы модулей

Используем текущий `/rail`. Controls перед результатами; одна стабильная inline region с маршрутом выбранной карточки, без новой страницы/deeplink и без URL с UID/route. Она не пересоздаётся при перестановке rows. Общая область source attribution покрывает список и подробности, остаётся mounted при sort/filter и не создаёт второй iframe: локальное изменение представления не должно скрыто перезагружать внешний banner. Последовательность остановок читабельна на мобильном; boarding/alighting обозначены текстом. CSS/точный layout остаются исполнителю в существующих Angular паттернах; отдельный design system не нужен.

Detail epoch отделён от search epoch; reply принимается только при совпадении обоих epochs, выбранного key, profile/generation и действующем исходном expiry. Проверка свежести делается перед open/apply и при возобновлении view, а не только callback timer: background tabs могут задержать таймер. Первый исполняемый UI turn после expiry очищает данные и отменяет request; мгновенное выполнение JS в suspended tab не обещается. Закрытие/hidden row/new criteria/new search/dispose/reference attribution failure очищают detail object, отменяют request и timer. Sort не меняет выбор; повторное explicit open после закрытия всегда новый bounded request. Нет многокарточного накопления.

Core → Application → Infrastructure → Api ownership сохраняется. Внешние DTO/mapping/метод thread только Infrastructure; новый normalized port — Application; правила времени/occurrence — Core. Host подключает уже существующий Api.Composition, не импортирует internals. Host OpenAPI snapshot дополняется только новым Rail path/schema. Добавляем uid в redaction contributor и тестируем emitted telemetry для нового body, включая key и endpoint times.

Не редактируем Trips source/specs, его worktree, contracts или UI. Если общий TS index либо Host snapshot изменится параллельно, интеграция должна сохранить чужие additions; не заменять файлы старой копией. Реализация позже — отдельный `gpt-6.1-sol` и личный review основного агента согласно текущему контракту; это предложение не запускает эту фазу.

## Risks / Trade-offs

- Два source reads на открытие → явный клик, одна панель, общий budget и отсутствие prefetch; это цена свежего UID.
- UID rotation/изменённое расписание → selection-not-confirmed и ручной поиск; никаких heuristics по номеру.
- Locator/echo/time shape не подтверждён реальным ответом → explicit supported forms, source-supplied date, fictional cases и отдельный real compatibility gate; не лечить guessed timezone/датами посадки/несколькими запросами.
- Полная нитка слишком длинна → bounded failure без truncation; лимит можно пересматривать только отдельным решением, не скрытно во время тестов.
- Общий admission увеличивает конкуренцию пользователя с route read → предсказуемый429, регрессии M1 mixed/cancellation tests; допуск не расширяется молча.
- Условия использования могут ограничивать обработку данных → source/fictional закончены сами по себе; конкретный оператор проверяет применимость фильтров/подробностей/60s lifetime перед real use. BYO-key и MIT не заменяют разрешения.

## Migration Plan

Сейчас создаётся только proposal corpus. Schema/data migrations отсутствуют. После отдельного утверждения: добавить source/fictional контракт и UI, проверить targeted suites и обычный CI в разрешённом контуре, затем sync/archive только по поручению. Existing search contract остаётся wire-compatible; admission становится общим. Если потребуется откат source increment, возвращается прежний M1 UI/API без преобразования данных.

Actual Host/OpenAPI, combined memory/load и live deployment не запускаются по наличию tasks. Предлагаемый measured extension готовится до source review:3cycles при1 и2 одновременных outer operations, всего6cycles. Каждый cycle содержит ровно1000 заранее заданных useful-workload attempts:500uncached all-mode search и500detail. Search success требует HTTP200+kind=complete+ожидаемые непустые проверенные journeys; detail success — HTTP200+kind=complete+ровно200валидных stops+правильную выбранную пару. Failed/skipped/limited, malformed и wrong binding не successes, даже при200. Required verdict:1000/1000 useful,0failures в каждом cycle, отсутствие retry. Call counters полезного участка дают1000GET search для500all-mode и ещё500verification+500thread для500detail, всего2000; reference/pressure/saturation считают отдельно, не подменяя знаменатель.

Сохраняются M1 reference pressure40MiB/100000stations и отдельное cache pressure128entries; source bindings существующего M1 harness переводятся с private searches.admission на общий admission без ослабления thresholds. p95≤1s проверяется отдельно по search/detail для fictional terminal transport. Baseline — медиана≥5samples за30s; baseline/recovery имеют1действительный bundle,0search cache/active detail states/children и2свободных permits; предписанная очистка cache одинакова в обеих фазах, forced GC запрещён. После120s recovery working set≤baseline+max(16MiB,15%). Saturation/refusal/cancel имеют собственный полный attempts denominator. Профиль/hash/source identity фиксируются до запуска; запуск только при отдельном разрешении. Старый M1 passing profile не подтверждает M2 и не заменяется новой подписью. Real access/keys/account quotas/attribution/target deployment остаются самостоятельными необязательными для OSS этапами.

## Technical audit — 2026-10-21

Проверены публичные официальные Yandex docs, текущие M1 parser/decoder/runtime/cache/measurement bindings и весь proposal corpus. Исправлены следующие дефекты плана; это не результаты реализации.

| Находка | Последствие старого плана | Исправление / контроль |
|---|---|---|
| P1: thread lookup date угадывалась по посадке | Посадка не на начальной станции могла открыть другой день нитки либо постоянно получать route-mismatch | Source locator из свежего search, bounded parsing и fixed host; сценарий lookup D /boarding D+1; отсутствие locator явно обработано |
| P1: calendar echoes смешивались с timestamps | Валидный ответ с date-only мог отвергаться | Разделены calendar comparison и строгая selected stop occurrence; unsupported echo form даёт limited |
| P1: stale search cache не инвалидировался | Manual refresh мог снова вернуть старый UID до TTL | Conditional exact-entry eviction только после usable fresh no-match, late verification не удаляет concurrent fresh snapshot |
| P2: общая фраза «строгий decoder» не определяла cross-field guards | Можно было принять чужой участок, невозможный expiry или не очистить state при profile notice | Matrix§3.1, endpoint/index/time/state checks и sanitized revocation response; server/TS contract vectors |
| P2: deadline и numeric/optional parsing оставались неоднозначными | Headers могли считаться окончанием child; дробь могла округлиться; optional duplicate затронуть required selection | Whole-child bounds, observed late faults, exact number/second precision, structural vs optional duplicates и terminal rules |
| P2: полезная нагрузка была описана через HTTP success |200failed мог попасть в successes; перенос semaphore ломал старые reflection bindings | Exact useful predicate/call counters/full denominator и сохранение M1 measured source bindings/thresholds |
| P2: view lifecycle и carrier controls не имели ряда edge cases | Задержанный timer, attribution failure или literal unknown label могли дать некорректное отображение | Checks при open/apply/resume, отдельный notice/clear flow, полный snapshot options и discriminated unknown/exact |

Сохраняются внешние неопределённости: фактическая доступность/формы ответов, права конкретного экземпляра на обработку и его квоты. Документация не является provider integration run. Source/fictional проект остаётся самостоятельным допустимым результатом; новый реальный метод default-off. Следующий этап — review исправленных artifacts, не implementation.

После исправлений независимый read-only reviewer повторно проверил locator/eviction, reply matrix, время и measured predicate по этим документам, исходному M1 и двум официальным страницам API; существенных новых проблем не обнаружил. Это review proposal, без runtime/provider proof. Ранее выполненный builder check не выявил перечисленные выше ошибки и не заменяется задним числом этим результатом.
