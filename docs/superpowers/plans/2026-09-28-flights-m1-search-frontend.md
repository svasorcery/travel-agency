# Flights M1 Search Frontend Implementation Plan

> **For agentic workers:** выполнение начинается после согласования пользователя. Для последовательного исполнения применять superpowers:executing-plans; способ делегирования согласуется отдельно. Checkbox означает ещё не выполненную работу. Документ не разрешает commit/push/PR, миграции, деплой, настоящих поставщиков или Anthropic.

**Goal:** анонимный one-way/round-trip поиск с достоверным представлением смешанных результатов и воспроизводимым локальным demo.

**Architecture:** Angular standalone route + signals для page state; узкий HttpClient в api-client; pure decoder/formatting; общий JSON-каталог, проверяемый .NET/TS/Node; отдельный Node stub и Nx demo configuration. Backend runtime не изменяется.

**Tech Stack:** существующие Angular 21, Reactive Forms, RxJS, Tailwind, TravelButton, Node 22, Vitest, Playwright и .NET 10/xUnit.

**Spec:** [Спецификация](../specs/2026-09-28-flights-m1-search-frontend-design.md).

**Execution status:** все шесть задач реализованы и проверены локально. Точные результаты и ограничения — в [отчёте до публикации](../results/2026-09-28-flights-m1-search-frontend-local.md). Удалённый CI и публикация проверяются отдельно.

**База:** `8b0eef927b8f39a22f7726ea1277bf0925f1b41e`, зафиксированный свежий origin/dev; `codex/flights-m1-search-design`. Продолжать в существующем worktree после проверки HEAD/status/ancestry. При расхождении не force-move/rebase и не подменять базу текущим HEAD.

## Global constraints

- Поиск anonymous; один взрослый/economy, currency query RUB, Accept-Language ru, DateOnly strings и явный returnDate:null для one-way.
- Сохранять порядок backend и валюту каждой цены. Reported partialFailures и currency mismatch являются разными признаками.
- Bookable и partner имеют существующие wire shapes. Никаких all-null demo-offers, offer_type из старого ADR или выдуманного обратного сегмента.
- Для Travelpayouts показывать ограниченную сводку; сегменты этого mapper не доказывают пересадки, реальное arrival или полный round-trip.
- До submit отсутствует HTTP-поиск. Flights имеет RenderMode.Client. Отмена/редактирование не создают ошибку и не оставляют старую выдачу.
- Demo API полностью маршрутизируется в loopback stub без fallback к Host. Banner существует в idle/loading/empty/error/success.
- Не запускать real Host/AppHost, Docker-backed tests или MigrateAsync под текущим запретом миграций. Обязательная сериализация проверяется no-DB FlightsApiFixture.
- Не обновлять framework, не вводить генератор клиента/NgRx/новую UI library. Спорные backend contract changes требуют отдельного решения.
- Корневые dependencies и lockfile остаются зафиксированными. Объявление peer metadata локальной Nx-библиотеки само по себе не требует изменения root lock: npm workspaces для неё сейчас не объявлены.

## File map и зависимости задач

| Задача | Создать/изменить | Ответственность |
|---|---|---|
| 1 | `tests/fixtures/flights-search.json`; `tests/Travel.Host.Tests.Integration/Flights/FlightsSearchContractHttpTests.cs` | Общий wire-каталог и endpoint serialization proof |
| 2 | `shared/ts/api-client/src/flights-search.{types,decoder}.ts`, `flights-search.decoder.spec.ts`, `flights-search-api.service.ts`, `index.ts`; `package.json`, `vite.config.mts`, `project.json`, `tsconfig.spec.json` библиотеки | DTO/guard/HTTP, peers/external, чистые Node tests и cache inputs |
| 2 | `apps/web/src/app/flights/flights-search-api.service.spec.ts`; `apps/web/tsconfig.spec.json`, `apps/web/project.json` | HTTP tests в существующей Angular test environment |
| 3 | `apps/web/src/app/flights/flight-search-form.ts`, `flight-results.ts` и их `.spec.ts` | Чистая валидация и view mapping |
| 4 | `flights-page.component.ts`, `.html`, `.spec.ts`, `flight-offer.component.ts`, `.spec.ts` в той же папке; `flights-source-mode.ts` | UI, lifecycle, два вида представления |
| 4 | `apps/web/src/app/app.routes.ts`, `app.routes.server.ts`, `app.html`, `app.spec.ts`, `apps/web/proxy.conf.json`; `tests/travel-e2e/specs/health.spec.ts` | Routes, CSR, обычный proxy, сохранение status |
| 5 | `tools/demo/flights-search-fixtures.mjs`, `flights-search-api.mjs`, `flights-search-api.test.mjs`; `apps/web/proxy.flights-demo.conf.json`; `flights-source-mode.demo.ts`; `apps/web/project.json` | Детерминированное изолированное demo |
| 6 | `tests/travel-e2e/playwright.flights-demo.config.ts`, `tests/travel-e2e/demo/flights-search.spec.ts`; root `package.json`; `.github/workflows/ci.yml`; README, current-state, ADR 0014 amendment | Воспроизводимая browser/CI acceptance и честная документация |

Задачи последовательны: 1 → 2 → 3 → 4 → 5 → 6. Никакая промежуточная задача не добавляет import ещё не созданного компонента. В Angular HTTP-spec разрешены test-only JSON imports из общего каталога через resolveJsonModule; production code его не импортирует. Для каждого такого прямого импорта в двух spec-файлах допускается только точечный `eslint-disable-next-line @nx/enforce-module-boundaries` с причиной «shared canonical protocol fixture, test only». Общее правило Nx не отключается, новые исключения для application imports не добавляются. Pure decoder tests импортируют decoder напрямую, чтобы Node runner не загружал Angular barrel. Для web/api-client test inputs явно добавить `{workspaceRoot}/tests/fixtures/flights-search.json`, сохранив default/^production.

## Review focus

1. Round-trip → one-way после заполненной return date отправляет null; чистая form-проверка задачи 3.
2. Изменение формы отменяет запрос A, новый B побеждает; late A не меняет экран; page/browser проверки задач 4/6.
3. Foreign currency при partialFailures:[] и empty при partialFailures nonempty имеют отдельные сообщения; задачи 3/4/6.
4. Partner с одним slice после round-trip submit не превращается в «прямой перелёт туда-обратно»; задачи 1/3/6.
5. Мокнутый browser test не доказывает proxy. Хотя бы один успешный путь задачи 6 не перехватывает search, проверяет HTTP demo-marker и browser request events.

## Task 1. Связать wire-примеры с настоящим endpoint

**Interfaces:** один `tests/fixtures/flights-search.json` содержит именованные пары request/response: oneWay, roundTrip, partial, empty, emptyPartial, mixedCurrency, unavailable. Response соответствует SearchResponse/ProblemDetails из текущего кода. Один файл используют задачи 2, 5 и 6; отдельный manifest, генератор и универсальный fixture loader не нужны.

- [x] **1.1 — Написать no-DB HTTP contract test.** Новый trait-free `FlightsSearchContractHttpTests : IClassFixture<FlightsApiFixture>` использует тот же collection/reset, что `FlightsEndpointsHttpTests`. Через FakeMessageBus.On вернуть SearchResult из настоящих domain BookableOffer/DeeplinkOffer с фиксированными UUID/time/amount. Все provider-объекты являются данными, реальных клиентов нет. Проверить actual POST endpoint JSON через структурное равенство каноническому response.

  ```csharp
  fixture.Bus.On<SearchFlightsQuery>((ErrorOr<SearchResult>)expectedResult);
  using var response = await fixture.Client.PostAsJsonAsync(
      "/api/flights/search?currency=RUB", request, ct);
  response.StatusCode.ShouldBe(HttpStatusCode.OK);
  using var actual = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
  using var expected = JsonDocument.Parse(File.ReadAllText(fixturePath));
  JsonElement.DeepEquals(
      actual.RootElement, expected.RootElement.GetProperty("roundTrip").GetProperty("response")
  ).ShouldBeTrue();
  ```

- [x] **1.2 — Запустить новый class filter и убедиться в red.** Ожидаем отсутствие fixture или несовпадение данных, а не ошибку Docker. Команда:
  ```powershell
  dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --maxcpucount:1 --filter "FullyQualifiedName~FlightsSearchContractHttpTests"
  ```
  Этот class не наследует IntegrationTestBase и не стартует Program. Не заменять команду всем проектом/solution: там находятся DB/Aspire suites.

- [x] **1.3 — Заполнить каталог проверенными shapes.** One-way содержит bookable и partner. Round-trip содержит bookable с двумя mirrored slices и partner с одним slice, как реальный mapper. Partial содержит только bookable и failure Travelpayouts; не изображать один provider одновременно успешным и упавшим. Empty содержит пустые массивы; empty-partial содержит offers:[] и failure одного источника. Mixed-currency содержит фактическую EUR-цену при запросе RUB и partialFailures:[], что допускает FX fallback. Фиксированные опорные даты: departure 2030-06-10, return 2030-06-17, fetchedAt 2030-06-01T08:00:00Z, bookable expiresAt 2030-06-01T08:20:00Z. В partner URI использовать https://partner.invalid/fixture, а не реальный адрес. Ref/expiry/nullability должны удовлетворять Contracts.cs. Использовать положительные duration, в том числе day/fraction example, и ненулевой offset с переходом суток.

- [x] **1.4 — Проверить request binding и ошибки отдельно.** Через OnCapture проверить DateOnly/null, PassengerCount, Currency, Locale. Неизвестные свойства response не запрещать в TS, но каталог поддерживать точным для текущего endpoint. Для недоступности bus возвращает FlightsErrors.ProviderUnavailable("all"), фактический ответ обязан быть 500, errors[].code = Flights.ProviderUnavailable. Сравнить стабильные ProblemDetails поля; traceId только проверить на наличие/безопасность, не замораживать.

- [x] **1.5 — Повторить focused test до green и вручную просмотреть fixture.** Никакого JSON auto-accept. Это proof model binding/endpoint/DTO serialization с fake bus. Full Host discovery, кэш/FX/provider pipeline остаются вне этого теста и не заявляются доказанными.

**Готово:** один каталог, оба реальных варианта, оба вида поездки и error semantics проверяются без схемы БД.

## Task 2. Узкий API client и правильная тестовая среда

**Interfaces:** `FlightSearchRequest`, `FlightSearchResponse`, `FlightOffer`, `FlightPartialFailure`; `decodeFlightSearchResponse(value:unknown): FlightSearchResponse`; `FlightsSearchApiService.search(body): Observable<FlightSearchResponse>`; `FlightSearchContractError`. Все даты/duration остаются строками.

- [x] **2.1 — Написать pure Node decoder tests на каталог задачи 1.** Прямой импорт decoder, JSON через test-only import. Проверить extra JSON field tolerated, missing used field, неверные arrays/numbers/datetime/duration, all-null refs и both-nonnull refs rejected. Bookable и partner nullable-field combinations должны отличаться; provider остаётся строкой.

- [x] **2.2 — Реализовать узкие типы и guard.** Поля точно из спецификации §4; не придумывать offer_type. Проверять непустые slices/segments, положительные корректные duration, конечную неотрицательную цену, datetime с offset/Z, существующие два варианта. Не требовать от partner two-slice по типу запроса и не повторять backend business validation. Не выполнять currency arithmetic.

- [x] **2.3 — Написать Angular HTTP tests в apps/web.** Здесь runner уже инициализирует TestBed. Использовать service через `@travel/api-client`, provideHttpClient/provideHttpClientTesting/HttpTestingController; afterEach verify. Проверить exact query/body/headers, отсутствие Authorization, один success, передачу HTTP error и client timeout/cancellation. Полная семантика ответов уже покрывается decoder tests; тексты ошибок и остальные status codes проверяются на уровне page, без повторения той же матрицы здесь. Content-Type задаём явно, чтобы тест и transport совпадали.

  ```ts
  const req = http.expectOne('/api/flights/search?currency=RUB');
  expect(req.request.method).toBe('POST');
  expect(req.request.body.returnDate).toBeNull();
  expect(req.request.headers.get('Content-Type')).toBe('application/json');
  expect(req.request.headers.get('Accept-Language')).toBe('ru');
  expect(req.request.headers.has('Authorization')).toBe(false);
  ```

- [x] **2.4 — Реализовать сервис.** Только error channel HttpClient, никакого response.ok, automatic retry, token storage или fallback endpoint. Применить 15 секунд client timeout и decoder:

  ```ts
  search(body: FlightSearchRequest): Observable<FlightSearchResponse> {
    return this.http.post<unknown>('/api/flights/search?currency=RUB', body, {
      headers: { 'Accept-Language': 'ru', 'Content-Type': 'application/json' },
    }).pipe(timeout(15_000), map(decodeFlightSearchResponse));
  }
  ```

- [x] **2.5 — Привести package metadata в соответствие.** api-client peerDependencies: existing core + common ^21.2.0 и rxjs ^7.8.0. Vite external исключает @angular/* и rxjs/subpaths. Root dependencies и lockfile не менять для этих peer declarations. Добавить exports, resolveJsonModule в test tsconfig и общие fixture inputs в targets.test.inputs. Канонические JSON imports получают только описанные выше точечные test-only lint comments; проверить lint обоих проектов. Не удалять unrelated Nx stub ради уборки.

- [x] **2.6 — Проверить library build/lint и два runner.**
  ```powershell
  npx.cmd nx test api-client --watch=false
  npx.cmd nx build api-client
  npx.cmd nx lint api-client
  npx.cmd nx test web --watch=false --include=src/app/flights/flights-search-api.service.spec.ts
  ```
  Если AOT/declaration pipeline потребует новую конфигурацию, сначала разобрать причину; не переносить всю библиотеку на новый build system скрытно.

**Готово:** клиент и wire tests исполнимы на текущем tooling; Angular framework не встраивается в library bundle.

## Task 3. Валидация и семантика результатов

**Interfaces:** `createFlightSearchForm(today: () => string)`, `toFlightSearchRequest(form)`; `toFlightOfferView(offer)` возвращает discriminated UI model bookable/partner; `summarizeSearchResponse(response)` отдельно вычисляет reportedPartial и currencyMismatch.

- [x] **3.1 — Сначала form tests.** today передаётся через функцию с фиксированным значением в тесте. Проверить trim/uppercase, IATA format, same airport, невозможную дату, past departure, return before departure, round-trip missing return, one-way null после переключения. Отдельно изменить today callback через полночь и убедиться, что новый submit отклоняет вчерашнюю дату. Today/return same date разрешены. Сериализация не проходит через Date.toISOString.

  ```ts
  expect(toFlightSearchRequest(form)).toEqual({
    origin: 'LED', destination: 'DME', departureDate: '2030-06-10',
    returnDate: null, passengerCount: 1, cabinClass: 'economy',
  });
  ```

- [x] **3.2 — Реализовать typed Reactive Forms и функции.** Cross-field validators возвращают стабильные ключи для UX и вызывают today getter. Перед сериализацией submit вызывает updateValueAndValidity, поэтому current local today не фиксируется на время открытия вкладки. Невалидная форма не сериализуется. Кнопка примера задаёт today+30 дней и return+7 при round-trip, без HTTP. Test clock фиксирован.

- [x] **3.3 — Сначала view tests.** Bookable one-way/round-trip, реальные connecting segments, Z/ненулевой offset/переход суток, duration с днями/дробями. Partner always summary, особенно один slice после round-trip. Foreign currency при empty partialFailures даёт currency warning, но не утверждает причину FX. Empty+failures даёт два сообщения. Порядок offers сохранён, нет «cheapest» и client sort.

- [x] **3.4 — Реализовать форматирование.** Время с явным исходным offset без конвертации в browser zone. Duration .NET constant format переводится в дни/часы/минуты; дробные секунды не округляют вверх заявленную минуту. Money только display с фактической валютой. Partner не использует inferred arrival/duration/stops для потребительских утверждений. Source mode не меняет доменный offer type.

- [x] **3.5 — Запустить focused tests, затем web unit suite.**
  ```powershell
  npx.cmd nx test web --watch=false --include=src/app/flights/flight-search-form.spec.ts
  npx.cmd nx test web --watch=false --include=src/app/flights/flight-results.spec.ts
  ```
  Routes ещё не изменены, отсутствующих imports нет.

## Task 4. Собрать страницу и route

**Interfaces:** page state = idle/loading/ready/error; ready содержит offers и независимые warnings, empty выводится из offers.length. `flights-source-mode.ts` экспортирует host-mode constant; demo replacement появится в задаче 5.

- [x] **4.1 — Написать page/presentation tests.** Все состояния AC3, validation без запроса, отсутствие запроса при конструировании, подавление повторного submit. Для race: начать A, изменить форму (reset), запустить B, вернуть B, попытаться вернуть A; экран остаётся B. Destroy unsubscribes. Изменение формы очищает старую выдачу; retry выполняет новую валидацию текущей формы.

- [x] **4.2 — Реализовать одну модель отмены через RxJS.** Subject принимает Request либо null для reset. SwitchMap отменяет предыдущий HttpClient; startWith loading и catchError расположены внутри внутренней цепочки, чтобы ошибка не завершала дальнейшие поиски. takeUntilDestroyed закрывает lifecycle. Не добавлять одновременно ручную subscription и sequence-counter без отдельного обоснования.

  ```ts
  commands.pipe(
    switchMap(request => request === null
      ? of({ kind: 'idle' } as const)
      : api.search(request).pipe(
          map(toReadyState),
          catchError(error => of(toErrorState(error))),
          startWith({ kind: 'loading' } as const),
        )),
    takeUntilDestroyed(),
  ).subscribe(value => state.set(value));
  ```

- [x] **4.3 — Реализовать шаблоны.** OnPush standalone, TravelButton из @travel/ui-kit с явным type=submit. Labels, radiogroup, field errors/aria-describedby, first-invalid focus, aria-busy/live. Два вида offer presentation, no clickable supplier URL, no booking handlers. Demo banner зависит от build-time source mode и виден вне results branch. На странице не показывать raw stack/ProblemDetails detail как основной UX.

- [x] **4.4 — После компонента добавить lazy route/navigation.** Root → flights, status сохранён; flights RenderMode.Client перед wildcard. App spec и root test в health.spec.ts обновляются на поиск, status test по-прежнему отдельно проверяет /status. Normal-host proxy сделать явно `/api/**`, тестировать currency query. Это устранение неоднозначности конфигурации, не заявление о доказанном баге старого `/api`.

  ```ts
  { path: 'flights', loadComponent: () =>
      import('./flights/flights-page.component').then(m => m.FlightsPageComponent) },
  { path: '', redirectTo: 'flights', pathMatch: 'full' },
  ```

- [x] **4.5 — Проверить unit/lint/build.**
  ```powershell
  npx.cmd nx test web --watch=false
  npx.cmd nx lint web
  npx.cmd nx build web
  ```
  Проверить в тесте server route configuration, что flights имеет RenderMode.Client. Direct/reload и отсутствие автоматического поиска проверяются через dev browser suite. Отдельный built Express smoke и API routing относятся к последующей задаче hosting; wrapper для них здесь не создаётся.

## Task 5. Сделать маленькое изолированное demo

**Interfaces:** `buildDemoSearchResponse(request)` читает общий JSON, выбирает oneWay/roundTrip, подставляет даты сегментов и возвращает body. Это функция одного сценария поиска, а не общий движок fixtures.

- [x] **5.1 — Написать Node tests.** Опорные one-way/round-trip структурно равны соответствующим response общего файла; повторные body дают равный JSON; выбранные outbound/return dates правильно попадают в сегменты. Partner не получает выдуманный второй slice. Unknown valid route даёт empty. Проверить HTTP query/Content-Type, malformed body400 и 404 на неизвестных API.

- [x] **5.2 — Реализовать builder и stub.** Фиксированные IDs/цены/refs/fetchedAt/expiry, сдвигаются только segment dates. Без Date.now/Math.random/outbound clients. Bind 127.0.0.1:5100; URL разбирается через pathname/searchParams. Разрешить POST /api/flights/search с currency=RUB и JSON, ограничить body 16KiB. GET / возвращает простую readiness-строку. Другие API дают404. Search response получает `X-Travel-Demo: fixtures`. Нет диагностического API, счётчиков, CLI-сценариев, forwarding или fallback. SIGINT/SIGTERM закрывают server.

- [x] **5.3 — Добавить явный Nx demo launch mode.** Source-mode.demo.ts экспортирует demo вместо host; fileReplacements используются только в build configuration flights-demo. Serve использует этот buildTarget, host127.0.0.1, port4201 и proxy.flights-demo.conf.json. Все `/api/**` и `/events/**` указывают на stub5100. Default/production build не использует replacement и не импортирует fixtures.

  ```powershell
  node tools/demo/flights-search-api.mjs
  npx.cmd nx serve web --configuration=flights-demo
  ```

- [x] **5.4 — Запустить `node --test tools/demo/flights-search-api.test.mjs`.** Для HTTP-тестов использовать ephemeral loopback port и тот же handler. Занятый demo-порт должен дать ошибку, а не переключение на существующий процесс. Slow/partial/unavailable воспроизводятся тестами клиента/страницы и не расширяют этот server.

## Task 6. Browser acceptance, CI и документация

**Interfaces:** отдельный Playwright config с testDir ./demo, baseURL http://127.0.0.1:4201 и Chromium. Existing config ./specs и Host status suite не подменяются демо.

- [x] **6.1 — Написать Playwright config с двумя webServer processes.** Первый запускает Node stub и ждёт GET / на его порту; второй запускает Nx serve web --configuration=flights-demo. reuseExistingServer:false, bounded startup timeout и обычное завершение процессов. Windows выбирает npm/npx.cmd, POSIX unsuffixed; не использовать видимые shell-окна. Занятый порт даёт явную ошибку, а не переиспользование неизвестного Host.

- [x] **6.2 — Добавить один целый путь без interception search.** Открыть root/direct flights/reload, проверить demo banner до submit. Заполнить canonical dates, submit one-way, затем round-trip. Проверить actual request Content-Type/body, card types, две bookable slices и partner summary. Проверить X-Travel-Demo: fixtures в реальном response и browser request events: ровно один поиск на submit. Search request проходит реальный dev proxy. Разрешён только application loopback origin; неожиданная API операция проваливает тест, внешние origins блокируются. Никаких вызовов Host:5099.

- [x] **6.3 — Добавить короткий browser error/retry сценарий.** Один route.fulfill500, затем успех проверяют видимую ошибку, сохранённую форму, повтор и demo banner. Проверить невалидный submit без HTTP и empty для неизвестной demo-пары. Полная матрица partial/currency/401/403/503/network/malformed/timeout/race остаётся в unit/component tests задачи 4. Не запускать весь existing health suite как часть isolated demo.

- [x] **6.4 — Провести usability и rendering review.** На 360px/1280px проверить отсутствие горизонтального скролла, видимый focus/keyboard submit, связанные ошибки и 200% zoom. Production build и явный Client server route — обязательные проверки; direct/reload/no automatic POST уже проверены задачей 6.2. Отдельный built-server wrapper или hosting smoke в этом срезе не создаётся.

- [x] **6.5 — Использовать существующий CI.** Root script `test:flights-demo` запускает новый Playwright config. В уже существующий job frontend-affected после его Nx-проверок добавить Chromium install, Node stub tests и этот browser command. Не добавлять новый job, job dependencies или собственную систему change detection. Этот шаг не стартует AppHost и не требует provider credentials. No-DB contract tests входят в существующую trait-free Host HTTP lane. Публикация workflow отдельно не разрешена текущим review.

  ```powershell
  npx.cmd playwright test -c tests/travel-e2e/playwright.flights-demo.config.ts
  ```

- [x] **6.6 — Обновить README/current-state и ADR 0014 amendment.** Две команды demo, точный loopback URL, поддерживаемый пример маршрута, источник вымышленных данных, границы доказательства. Не изменять generated README request region вручную. В ADR уточнить отсутствие wire discriminator и поэтапное включение CTA. Сохранить будущую цель полноценного booking. Existing status smoke остаётся Host-dependent; его результаты не объявлять полученными в demo.

- [x] **6.7 — Финальная проверка изменённого объёма.** Повторить tests задач 1–5, новый Playwright suite, build/lint web и api-client, `npm.cmd run check:readme-examples`, Biome на затронутых TS/JSON/MJS, CSharpier check нового C# файла, git diff --check. Проверить scope file list и отсутствие product fixture imports. Не запускать весь solution/Host integration/AppHost для расширения доказательства под запретом миграций. Ошибки baseline/tooling отражать отдельно; если runtime-contract отличается от каталога, исправлять основание после объяснения, не маскировать parser fallback.

## Проверяемое соответствие спецификации

| Требование | Задачи / доказательство |
|---|---|
| AC1 форма/anonymous/root/direct | 2, 3, 4, 6; HttpTestingController + browser |
| AC2 единый контракт/два offer | 1, 2, 5; structural equality + decoder |
| AC3 состояния/гонки/currency | 2, 3, 4, 6 |
| AC4 изолированное demo/proxy | 5, 6; browser без search interception + demo response header |
| AC5 CSR и production bundle | 4, 5, 6; production build + Client mode/direct route |
| AC6 accessibility/status | 4, 6; UI tests/review, source preservation of Host status suite |
| AC7 проверки без БД/CI | 1, 6; focused no-DB filter, шаг существующего frontend job |

## Саморевью после исправлений

- Устранены две несовместимости исполнения: Angular TestBed в plain Node runner и route, ссылающийся на отсутствующий component.
- Устранены противоречия ограничений: автоматические миграции обязательного Host test и fallback demo-запросов к настоящему Host.
- Базовые success/empty-примеры проходят машинно связанную цепочку .NET endpoint → общий JSON → TS decoder/Node builder. Error/partial/currency cases проверяются соответствующим HTTP/decoder/page уровнем и не требуют реализации сценариев в stub. Это не full Host discovery или supplier proof.
- Partner limitations, кеширование partialFailures, валютное несоответствие, ошибки500/503 и режим demo отражены отдельными критериями.
- Все команды выше являются будущими проверками реализации. В ходе design audit они не запускались; node_modules сейчас отсутствует.
- Новые production runtime зависимости, auth decisions, миграции и booking endpoints не скрыты внутри первого среза. OpenAPI metadata и полный backend-connected browser gate остаются отдельными будущими решениями.

## Проверка сложности и объёма

- Один общий JSON заменяет набор отдельных файлов и manifest; это всё ещё единый проверяемый HTTP-контракт.
- Stub обслуживает успешный поиск и пустой набор. Error/partial/slow управление остаётся внутри тестов, а не становится отдельным demo-продуктом.
- Для proxy proof достаточно настоящего ответа с demo-header и событий браузера; диагностический endpoint и счётчики удалены.
- Существующий frontend CI принимает новый шаг. Отдельный CI job и built-server wrapper удалены.
- .NET проверяет HTTP serialization, decoder — структуру, service — транспорт, page — состояния, browser — целый пользовательский путь. Полная матрица не дублируется на каждом уровне.
- Универсальные расширяемые механизмы вводятся при реальной повторяющейся потребности; один экран не получает repository/facade/store framework. Специализированные локальный state, пара pure helpers и один API service достаточны.
