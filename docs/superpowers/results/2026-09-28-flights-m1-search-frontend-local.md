# Flights M1 search frontend: локальная проверка

**Контрольная точка:** локальная проверка до публикации. На момент записи отчёта реализация была в рабочем дереве без коммитов. База, полученная `git fetch origin dev` до создания worktree: `8b0eef927b8f39a22f7726ea1277bf0925f1b41e`. Ветка `codex/flights-m1-search-design` оставалась на этом SHA, ancestry проверен. Основной checkout не изменялся.

## Что готово

- `/flights` — анонимный поиск one-way/round-trip на одного пассажира в экономе, результаты двух существующих wire-вариантов, состояния загрузки/ошибки/пустой и частичной выдачи, независимое предупреждение о цене в другой валюте. `/status` остаётся в обычном режиме.
- API client проверяет используемые поля ответа и отменяет прежний запрос при изменении формы. Партнёрская карточка показывает только доступную сводку, bookable-карточка показывает каждый сегмент с его временным offset.
- Демо запускается отдельной Angular-конфигурацией и loopback Node-заглушкой. Все `/api/**` и `/events/**` маршрутизируются в неё без перехода к Host. Демо помечено во всех состояниях, а фиктивный partner URL не активен.
- Общий `tests/fixtures/flights-search.json` связывает no-DB HTTP-сериализацию endpoint, TypeScript decoder и демо-ответы. README, current-state, ADR 0014 и исходный план обновлены.

## Свежие локальные проверки

| Проверка | Результат и граница |
|---|---|
| `dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --maxcpucount:1 --filter "FullyQualifiedName~FlightsSearchContractHttpTests" --no-restore` | 9/9; реальный `SearchEndpoint` в `FlightsApiFixture` с fake bus, без БД/миграций |
| `nx test api-client --watch=false` | 8/8; decoder по общему JSON и сборка библиотеки |
| `nx test web --watch=false --skip-nx-cache` | 38/38; форма, HTTP transport, страница, сегменты, ошибки, отмена и timeout |
| `npm run test:flights-demo` | Node 6/6 и Playwright 6/6; браузерный one-way/round-trip через настоящий dev proxy, empty/error/retry, direct reload, отсутствие внешних/booking-запросов, узкий экран и фокус |
| `nx build web --skip-nx-cache` | Production Angular/SSR build успешен; маршрут `/flights` имеет Client render mode |
| `nx lint web`, `nx lint api-client` | Успешны |
| `npm run check:readme-examples` | 9/9 и соответствие каталогу README |
| Biome CI по изменённым TS/HTML/SCSS/JSON/MJS; CSharpier check нового C# теста; `git diff --check` | Успешны. Biome сообщает только 46 информационных `useLiteralKeys`, небезопасное преобразование не применялось |

Для локальных frontend-проверок использован установленный Node 24.19: системный Node 22.18 ниже заявленного проектом минимума. NuGet restore и `npm ci --ignore-scripts` выполнены только для этого worktree. .NET компилятор выводит уже известное предупреждение AD0001 анализатора endpoint fixture; тесты завершаются успешно.

## Исправления по финальному ревью

- Первоначально потерянные промежуточные сегменты теперь отображаются с аэропортами и временем; тест фиксирует маршрут через DXB, разные offsets и переход суток.
- Поля IATA принимают вставку с пробелами, после чего нормализуют код; browser test проверяет реальный ввод.
- В demo скрыта ссылка на статус инфраструктуры, который не запускается; browser test проверяет навигацию.
- Невалидный JSON при HTTP 200 теперь описан как нечитаемый результат. Browser test сначала воспроизвёл неверное сообщение, затем прошёл после исправления.

## Исправления повторного саморевью

- Источник предложения теперь виден рядом с названием карточки. Web-тест воспроизвёл отсутствие подписи и прошёл после исправления.
- Прерванная во время отправки POST-запроса клиентская связь больше не вызывает необработанный rejected promise в demo-сервере. Node-тест воспроизвёл сбой, затем подтвердил, что сервер принимает следующий запрос.
- Demo-stub больше не принимает посторонний MIME, начинающийся с `application/json`, например `application/jsonp`; Node-тест сначала получил ошибочный HTTP 200, затем подтвердил HTTP 400.
- Decoder больше не принимает невозможные даты вроде 30 февраля, время `24:00` и offset за пределами .NET `DateTimeOffset`. Тесты сначала воспроизвели принятие таких ответов, затем подтвердили контрактную ошибку.

## Пределы и следующий gate

Нынешний OpenAPI для поиска всё ещё публикует `IResult` вместо schema SearchResponse. Типы клиента ручные и проверяются общим JSON и endpoint-тестом; исправление metadata — отдельное backend-решение. Не запускались real Host/AppHost, Docker/миграции, поставщики, Anthropic, полный Host-dependent status E2E или развёрнутый production server. Локальный demo и no-DB HTTP fixture не доказывают supplier search, booking или live-доставку.

На момент этой локальной контрольной точки CI workflow был обновлён только как исходный файл; удалённый CI, коммит, push, PR, деплой и миграции ещё не выполнялись. Их результаты относятся к последующей интеграционной проверке, а не к этому отчёту.
