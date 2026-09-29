# Flights M1: первый демонстрируемый frontend-срез

**Статус:** первый срез реализован и проверен локально в указанной границе; результаты и ограничения зафиксированы в [отчёте до публикации](../results/2026-09-28-flights-m1-search-frontend-local.md). Результаты удалённого CI относятся к отдельной интеграционной проверке.
**База:** `origin/dev` после fetch 2026-09-28: `8b0eef927b8f39a22f7726ea1277bf0925f1b41e`.
**Ветка:** `codex/flights-m1-search-design`, создана от указанного SHA в отдельном worktree; ancestry проверен повторно при аудите.
**Цель:** первый работающий пользовательский путь Flights: анонимная форма one-way/round-trip, понятная выдача и ошибки, воспроизводимый локальный показ без внешних сервисов.

## 1. Как этот срез служит цели проекта

Концепт определяет Travel как публичный инженерный showcase с Flights в роли флагмана полного бронирования. Этот документ описывает первый frontend-инкремент, не объявляет Flights M1 целиком завершённым и не отменяет последующий booking milestone.

| Вариант | Полезный результат | Цена и ограничения |
|---|---|---|
| **Поиск и результаты сейчас, рекомендуется** | Законченный анонимный путь; демонстрация Angular, смешанной выдачи и проверяемого HTTP-контракта | Не доказывает работу настоящего поставщика, покупку и выдачу билета |
| Полный booking flow сразу | Пользователь проходит quote/hold/confirm/order | Требует frontend auth, PII, повторов записей, срока hold, согласия на новую цену и eventual read model; это несколько самостоятельных решений |
| Статический экран | Быстрый визуальный материал | Не проверяет запросы, сериализацию, отмену и ошибки |

Выбор поиска остаётся обоснованным. Его самостоятельный результат: человек может задать условия, понять оба типа найденных предложений и отличить отсутствие результатов от сбоя. Демо использует те же реальные варианты wire DTO, что backend. Успешный показ не называется интеграцией с живым поставщиком.

Действуют актуальный код, current-state и принятые amendments ADR. Старый ADR 0014 описывает `offer_type` и две CTA, но текущий `OfferDto` discriminator не содержит. В этом инкременте варианты распознаются по существующим полям, а обе CTA отложены. После согласования это отклонение первого frontend-инкремента фиксируется кратким amendment ADR 0014; новый wire discriminator не добавляется. ADR 0005 позволяет Reactive Forms для обычных форм; обязательство показать Signal Forms остаётся отдельным будущим примером. ADR 0022 определяет существующий `@travel/ui-kit` как границу UI-примитивов.

## 2. Что проверено в исходниках

| Область | Подтверждённое состояние |
|---|---|
| Angular | Только `/status`, root redirect, standalone/zoneless, `provideHttpClient(withFetch())`; frontend login и Flights client отсутствуют |
| SSR | `/status` имеет `RenderMode.Client`, wildcard имеет Prerender; `server.ts` не проксирует API |
| API client | Status-сервис и Nx-заглушка; Vitest библиотеки работает в Node без Angular TestBed setup |
| Search | Анонимный POST, currency в query, locale из Accept-Language; one passenger |
| Offer | Два варианта в `OfferDto.From`: bookable с ref/expiry и partner с URL/name |
| OpenAPI | Search request описан; search 200 ссылается на общий `IResult`, не на SearchResponse |
| Провайдеры | Валидный поиск обычного Host может вызвать Duffel/Travelpayouts; отсутствие ключа не создаёт fake inventory |
| Partner-данные | Travelpayouts mapper всегда создаёт один slice/segment; не переносит return_at/transfers, arrival вычисляет, неизвестную duration заменяет 60 минутами |
| Ошибки/кэш | Все провайдеры недоступны: 500 ProblemDetails. Кэш хранит offers, при hit partialFailures становится пустым. FX fallback сохраняет исходную валюту без ProviderFailure |
| Проверки | Есть лёгкий FlightsApiFixture без БД и real-Host fixture с PostgreSQL/миграциями; это разные уровни доказательства |

Основные источники: `modules/flights/Travel.Modules.Flights.Api/Contracts/Contracts.cs`, `Endpoints/SearchEndpoint.cs`, `Application/Handlers/Search/SearchFlightsHandler.cs`, `Infrastructure/Providers/Travelpayouts/TravelpayoutsOfferMapper.cs`, `tests/Travel.Host.Tests.Integration/Flights/FlightsApiFixture.cs`, `Web/HostWebContractTests.cs`, `apps/web`, `shared/ts/api-client`.

## 3. Пользовательский путь и точная граница

1. `/` ведёт на `/flights`. В обычном режиме навигация сохраняет `/status`. В demo виден постоянный текст «Демонстрационные данные. Поиск не обращается к авиакомпаниям» во всех состояниях, включая загрузку, ошибку и пустую выдачу.
2. Форма содержит выбор «В одну сторону / Туда и обратно», два IATA-поля, дату вылета и условную дату возврата. Подсказки дают понятные примеры «Санкт-Петербург — LED», «Москва — DME». Кнопка «Подставить пример» заполняет LED/DME и даты, но не запускает поиск. Один взрослый, эконом, валюта запроса RUB, язык ru фиксированы и видны.
3. Trim/uppercase применяются до проверки. Нужны три буквы A–Z, разные аэропорты, валидные календарные даты, вылет не раньше локального сегодняшнего дня и возврат не раньше вылета. «Сегодня или позже» является UX-ограничением этого экрана: backend SearchCriteria сам не запрещает прошлые даты. DateOnly хранится строкой без преобразования через UTC Date. One-way всегда отправляет `returnDate:null`.
4. Валидный submit запускает один запрос. Изменение формы отменяет текущую подписку и очищает прежнюю выдачу; повторный submit во время загрузки не создаёт дубль. Отмена не показывается как ошибка. Поздний ответ отменённого поиска не меняет экран.
5. Bookable-предложение показывает цену, валюту, фактические slices/segments, даты и времена, пересадки, длительность и пояснение «Оформление появится в следующем этапе». Partner-предложение имеет отдельное компактное представление: партнёр, цена/валюта, направление и пояснение «Детали маршрута уточняются у партнёра». Оно не обещает отсутствие пересадок, точное прибытие или полный обратный маршрут.
6. Никаких переходов к поставщикам, quote, hold, confirm, PII или CTA покупки в первом инкременте. Фиктивные данные не образуют отдельный доменный тип предложения.

Нет фильтров, сортировки на клиенте, autocomplete аэропортов, NL-поиска, saved searches, multi-passenger или новых UI-библиотек. Один законченный экран реализуется на текущих Angular/Tailwind/TravelButton, с native inputs и radiogroup.

## 4. Контракт HTTP и варианты ответа

```http
POST /api/flights/search?currency=RUB
Content-Type: application/json
Accept-Language: ru
```

```json
{
  "origin": "LED",
  "destination": "DME",
  "departureDate": "2030-06-10",
  "returnDate": null,
  "passengerCount": 1,
  "cabinClass": "economy"
}
```

Дата выше является фиксированным примером для проверки. В интерактивном обычном режиме пользователь выбирает актуальную дату. Currency/locale не добавляются в body. Каталог `docs/examples/flights-requests.json` остаётся источником документационных запросов и не используется как вымышленный ответ.

`SearchResponse` содержит `offers: OfferDto[]` и `partialFailures: {provider,errorCode,elapsedMs}[]`.
OfferDto: `id, provider, totalAmount, currency, itinerary, fetchedAt, expiresAt, providerOfferRef, deeplinkUrl, partnerName`.
Itinerary: `slices, totalDuration, isRoundTrip`; slice: `origin,destination,segments,duration`; segment: `origin,destination,departAt,arriveAt,carrierCode,flightNumber,cabinClass`.

| Вариант | Поля, по которым его распознаём | Представление |
|---|---|---|
| Bookable | Непустой providerOfferRef, валидный expiresAt; deeplinkUrl/partnerName null | Полный маршрут из полученных slices, без действия бронирования |
| Partner | Непустые deeplinkUrl/partnerName; providerOfferRef/expiresAt null | Ограниченная партнёрская сводка |
| Противоречие | Оба ref/URL заполнены или оба отсутствуют; обязательные поля повреждены | Ошибка контракта; не показывать выдуманный тип |

Provider остаётся строкой, распознавание не основано на имени компании. URL валидируется как http(s), но не превращается в активную ссылку. Runtime guard допускает новые неизвестные JSON-поля и проверяет используемые поля/вариант; он не повторяет backend business rules. Все карточки сохраняют порядок backend.

Длительности имеют формат .NET TimeSpan, включая дни и дробную секунду. Для отображения минуты округляются вниз, исходное значение остаётся в wire model. DateTimeOffset сохраняет offset: bookable-время показывается как календарные дата/время из ответа плюс UTC offset, без неподтверждённой подписи «местное время аэропорта». Денежные значения используются только для отображения, без арифметики и пересчёта валют на клиенте. При нечисловой/бесконечной сумме или повреждённом itinerary выдача становится contract error.

Для round-trip bookable показывает два реально полученных направления. Partner с одним slice в ответе на round-trip остаётся партнёрской сводкой; UI не достраивает обратную часть. `isRoundTrip` описывает полученный itinerary, а не гарантирует соответствие любому запросу.

## 5. Состояния и достоверность

| Состояние | Что видит пользователь |
|---|---|
| idle | Форма и приглашение задать условия |
| invalid | Связанные с полями сообщения, фокус на первом ошибочном поле после submit; HTTP отсутствует |
| loading | Текст «Ищем рейсы», aria-busy/aria-live, заблокированный повторный submit; форма доступна для изменения/отмены |
| offers | Bookable и partner представлены раздельно по смыслу; число отображённых предложений без обещания полного рынка |
| empty | «По этим условиям предложений не найдено»; в demo уточнение «В демонстрационном наборе» |
| partial | Дополнительное «Часть источников не ответила» при nonempty partialFailures, независимо от числа offers |
| currency mismatch | Отдельное «Часть цен указана в другой валюте» при currency != RUB, даже при partialFailures:[] |
| 400 | Проверить условия; известные errors[].code можно связать с полем, неизвестные получают общее сообщение |
| 500/502/503, network, 15-секундный client timeout | Недоступность поиска, сохранённая форма и ручной повтор |
| 401/403 | Ошибка доступа/конфигурации анонимного поиска; не открывать login автоматически |
| malformed 200 | «Не удалось прочитать результаты. Повторите поиск»; техническая причина остаётся диагностикой |

HttpClient сам выдаёт HttpErrorResponse на неуспешный HTTP и JSON parse failure; `response.ok` для него не вводится. Автоматических повторов нет. Таймаут отменяет ожидание браузера и не обещает завершение работы сервера.

Отсутствие partialFailures не означает полноту выдачи: cached ответ теряет историю ошибок. Отличная от RUB валюта не называется доказанным FX-сбоем, поскольку причину response не передаёт. Не использовать «самый дешёвый», «проверено только что», «есть в наличии». Полученная цена предварительна; свежесть и доступность проверяются будущим quote. Известный истёкший expiresAt можно отметить, но отсутствие истечения не гарантирует доступность.

## 6. OpenAPI и единая проверка wire-данных

У текущего OpenAPI response gap есть два жизнеспособных решения:

- Уточнить search response metadata и после проверки реального документа выбрать генерацию клиента. Это backend-изменение и отдельная проверка Wolverine/OpenAPI; надёжный способ не объявляется заранее доказанной однострочной правкой.
- **Для первого среза:** узкие ручные TS-типы и runtime guard, связанные с backend через общие канонические JSON-примеры. Генератор и новые библиотеки не требуются.

Выбран второй вариант, но прежнего «написать похожие примеры в двух тестах» недостаточно. Один файл `tests/fixtures/flights-search.json` содержит именованные request/response-примеры для one-way, round-trip, partial, empty, empty-partial и currency mismatch. В partial присутствуют offers только выжившего провайдера и failure другого; один источник не изображается одновременно успешным и упавшим. Лёгкий .NET HTTP-тест создаёт настоящие BookableOffer/DeeplinkOffer и SearchResult и структурно сравнивает ответ настоящего SearchEndpoint с соответствующими примерами этого файла. TS decoder tests читают этот же файл; Node builder при опорном запросе обязан вернуть структурно тот же JSON. Порядок массивов важен, порядок свойств нет. При drift падают проверки, fixture не обновляется автоматически.

Для ProblemDetails проверяются стабильные поля и errors[].code; динамический traceId проверяется отдельно, а не замораживается. Канонический all-provider-unavailable имеет HTTP 500. 503 проверяется дополнительно как возможная транспортная недоступность.

Обязательный .NET gate использует `FlightsApiFixture` без БД, NATS и миграций. Он доказывает model binding/endpoint/DTO serialization. Fake bus и вручную зарегистрированные маршруты не доказывают real Program/Wolverine composition. Real-Host/OpenAPI и полный браузер→Host→fake-provider запуск остаются явно отдельным gate после разрешения и подготовки изолированной БД. Запрет миграций сейчас действует и для запуска тестов.

## 7. Изолированное локальное демо

Отдельная Nx-конфигурация `flights-demo` включает только маркировку источника через build-time replacement `flights-source-mode.ts`. Обычный и production build имеют `host`; fixture и stub в application bundle не импортируются. Query parameter или localStorage не переключают источник.

Две команды: Node stub на `127.0.0.1:5100` и Angular demo на `127.0.0.1:4201`. Demo proxy направляет **все** `/api/**` и `/events/**` на stub. Он обслуживает POST search и простой GET / для готовности процесса; остальные API получают 404. Диагностического API, счётчиков и управления сценариями у него нет. Нет fallback к Host, supplier URL, секретов или сетевого клиента в stub. В demo навигация не обещает работающий статус инфраструктуры; обычный `/status` сохраняется.

Fixtures моделируют реальные варианты:
- Bookable: provider `duffel`, вымышленный ref с префиксом `off_fixture_`, фиксированный UUID, валидный expiry, null partner URL/name.
- Partner: provider `travelpayouts`, непустое вымышленное partnerName, HTTPS URL на зарезервированном `.invalid`, null bookable ref/expiry.
- Демо никогда не открывает этот URL и не отправляет ref на backend. Безопасность обеспечивают изоляция маршрутов и отсутствие действий, а не повреждение формы DTO.

В опорном JSON используются фиксированные даты/время. Небольшая функция сдвигает только даты сегментов от опорной даты к выбранной, для возврата использует выбранную returnDate. IDs, цены, порядок, fetchedAt и expiry остаются фиксированными. Это вымышленные времена: в demo карточка не интерпретирует их как «сейчас доступно» или реальный срок покупки. Общая система часов, генератор сценариев и seed/random не нужны.

LED→DME возвращает смешанную выдачу: one-way bookable+partner или round-trip bookable с двумя slices+partner с одним. Иные корректные пары дают явно демонстрационное empty. Повтор одинакового body даёт одинаковое содержимое. Ошибки, задержки и частичные ответы воспроизводятся в тестах; CLI-переключатель и отдельный механизм ручных сценариев в первый срез не входят.

Один обязательный browser smoke проходит через настоящий Angular dev proxy до Node **без перехвата search**. Он проверяет demo-marker HTTP-заголовка ответа и browser request events, поэтому дополнительный диагностический endpoint не нужен. Полная матрица ошибок/гонок проверяется на уровне компонента; браузер добавляет короткий error/retry сценарий с route interception. Неожиданные внешние и booking-запросы проваливают browser test.

## 8. SSR, UI и структура

`/flights` явно имеет `RenderMode.Client` до wildcard. Это интерактивный submit-only экран; SEO-страницы направлений и SSR результатов остаются следующими отдельными задачами. Прямой GET/reload не делает поиск. Проверки первого среза: production build, явный Client render mode и direct/reload страницы через dev server без автоматического POST. Специальный запускатель собранного Express и его smoke откладываются до задачи hosting: API reverse proxy там сейчас отсутствует. Работоспособность поиска на standalone built server или деплой не обещаются.

Page отвечает за форму/стейт, client за HTTP/guard, presentation helpers за форматирование. Signals держат view state; RxJS switchMap отменяет предыдущую операцию. Не добавляется NgRx/store ради одной страницы. Angular TestBed HTTP-spec располагается в `apps/web`, где уже есть Angular unit-test runner; чистый decoder остаётся в Node Vitest библиотеки.

Используются OnPush, standalone, `@travel/ui-kit` и явный `type="submit"` на TravelButton (его default — button). Поля имеют labels, ошибки связаны через aria-describedby, выбранный режим доступен как radiogroup. Проверяются видимый keyboard focus, отсутствие горизонтального скролла на 360px, читаемость на 1280px и 200% zoom. Новая visual-regression платформа не нужна.

## 9. Переход к бронированию

Каждый следующий шаг имеет самостоятельное согласование и доказательство:
1. **Quote/detail:** только настоящий bookable ref/provider; anonymous quote создаёт aggregate. Начальную цену сравнивать с поиском, повторный quote делать с тем же aggregateId и ref. Цена/валюта/expiry требуют явного согласия; partner не попадает в этот путь.
2. **Identity/passenger/hold:** выбрать frontend OIDC/PKCE и безопасную стратегию сессии. Проверить backend audience, GUID subject и flights:book, возврат к выбранному предложению после login. Если quote успел истечь, обновить его до hold. PII только после auth, без localStorage. Hold закрепляет владельца.
3. **Confirm:** non-Production test wallet, одна пользовательская операция и её idempotency key. Повтор того же запроса после timeout использует тот же ключ/тело; hold и confirm имеют разные ключи. Отдельно обработать 409, истечение hold и неизвестный исход сетевой ошибки. Confirmed не равно Ticketed.
4. **Order/status:** показывать успешный command response, учитывать задержку EF view и временный 404. Начать с bounded GET polling; затем решить transport SSE. Нативный EventSource не решает Bearer-header сам по себе. При reconnect сверять GET и streamVersion, не откатывать статус старым событием. Ticketed с номерами билетов, Cancelled и Refunded отображаются различимо; действие cancel требует своего UX-решения.

В этом срезе auth/quote/PII/payment/SSE не реализуются. Перечень выше не заменяет отдельную спецификацию booking UI и безопасного fake-provider стенда.

## 10. Приёмка и пределы доказательства

- **AC1:** root/direct/reload flights, форма one-way/round-trip, validation, example fill, один passenger/economy; нет auto-search и Authorization.
- **AC2:** общие wire-fixtures проверены production endpoint serialization, TS decoder и Node; оба вида offers, nullable, durations/offsets, round-trip partner limitation.
- **AC3:** loading/empty/error/retry/cancel/race, partial с offers и без, foreign currency при partialFailures:[], 401/403, 500/503/network/timeout/malformed.
- **AC4:** реальный browser→dev proxy→stub smoke без Host/ключей/БД, demo banner во всех состояниях, запрет booking/external requests.
- **AC5:** production build проходит; Client render mode и direct/reload проверены, до submit поиск отсутствует; production bundle не содержит fixture и demo-mode replacement. Built Express smoke/API routing остаётся задачей hosting.
- **AC6:** keyboard/labels/focus/mobile/zoom, existing status route сохраняется в обычном режиме.
- **AC7:** focused TS/.NET/Node/browser checks и README check; demo-проверка добавляется шагом в существующую CI-задачу frontend-affected. Никаких миграций или полного AppHost запуска ради данного локального gate.

Готовность первого среза означает UI demo + проверенную сериализацию границы. Real Host composition, реальные suppliers, booking, deployment и live readiness этим не подтверждаются. Текущий аудит не запускал ни тестов, ни сервисов: это проверка решения по исходникам.

## 11. Исправления глубокого аудита

| Находка в первоначальном плане | Исправление |
|---|---|
| Real-Host test неявно применял миграции | Обязательный gate заменён на no-DB HTTP fixture с честным пределом доказательства |
| Demo имел невозможный третий offer | Используются настоящие bookable/partner shapes и отдельная маркировка режима |
| Partner segment принимался за полный маршрут | Отдельная сводка без выдуманных пересадок/возврата/arrival |
| Backend, TS и stub могли тестировать разные примеры | Общие JSON и точное структурное сравнение |
| FX/cache неполнота трактовалась слишком уверенно | Разделены reported partial, currency mismatch и неизвестная полнота |
| TestBed и зависимости библиотеки предполагались готовыми | Angular HTTP tests в web, pure tests в api-client, peer/external план |
| Прокси и SSR считались взаимозаменяемыми | Явные dev globs, real proxy smoke; production build/Client mode проверяются отдельно, built Express smoke отложен до hosting |
| Demo мог попасть в Host, а empty не сообщал источник | Полная маршрутизация demo API в stub и постоянный banner |
| Task 2 ссылался на ещё не созданный component | Исполняемый порядок задач перестроен |
| Booking roadmap упускал повтор ключа и статус билета | Добавлены re-quote, same-key retry, auth decisions, Confirmed/Ticketed и projection lag |

Саморевью сверяет каждое AC с задачей плана. Открытые backend-пробелы (OpenAPI metadata, partner completeness, cache failure history) описаны; runtime backend-код в этой редакции не меняется. Все изменения сейчас ограничены спецификацией и планом.

## 12. Проверка сложности

Сохраняем то, что нужно для первого среза: одну страницу, один API-сервис, небольшие pure-функции валидации/форматирования, один общий JSON-файл, маленькую HTTP-заглушку и явный demo launch mode. Генерация API-клиента, универсальная схема валидации, новый store, backend demo framework и auth в этот объём не входят.

Убраны диагностический endpoint со счётчиком, CLI-матрица сценариев, отдельный CI job, специальный built-server wrapper и повторение полной матрицы ошибок в browser E2E. Они не дают этому экрану соразмерной дополнительной пользы. Каждый тестовый уровень проверяет свою границу; одинаковые случаи не размножаются по всем уровням. Сценарии ошибок остаются воспроизводимыми в тестах, основной интерактивный demo остаётся воспроизводимым через настоящий HTTP/proxy.
