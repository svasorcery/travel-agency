# План: Flights M1 booking frontend после поискового среза

**Статус:** план согласован 2026-09-29; B1 merged в PR #19; B2 реализован локально для review; B3 не начинался.

**База B2:** свежий `origin/dev` `334de7622808c64edce3c0b6c83e8864e9d1dda3` после merge PR #19; повторный fetch 2026-09-30 дал тот же SHA.

**Ветка B2:** `codex/flights-b2-audience` начинается ровно с указанного SHA.

**Спецификация:** [2026-09-29-flights-m1-booking-frontend-design.md](../specs/2026-09-29-flights-m1-booking-frontend-design.md).

## Решение перед первым изменением кода

Согласовать рекомендуемую границу: B1 quote/detail, затем B2 auth/passenger/hold/confirm, затем B3 order/status. В B1 нужен `FareConditionsDto` только в quote response, чтобы пользователь видел известные тарифные факты перед hold. Для B2/B3 принимается ограничение: после открытия Held на новом устройстве существующий `GET order` не сообщает `HeldUntil`; UI показывает состояние без точного таймера. Если точное восстановление срока и строгая idempotency quote обязательны для первой версии, сначала проектируется backend-контракт; не выдавать обход через фиктивный таймер или автоматический новый ключ.

Ниже — исполняемый порядок **после согласования**. Каждая задача заканчивается указанным доказательством, без реальных поставщиков, Anthropic, деплоя или применения миграций. Публикация/merge следующей реализации согласуются отдельно.

## B1 — анонимная проверка цены

### 1. Закрепить quote wire-контракт

- В `modules/flights/Travel.Modules.Flights.Api/Contracts/Contracts.cs` добавить узкий `FareConditionsDto` в `QuotedOfferResponse`; заполнять его в `QuoteOfferEndpoint` из `BookableOffer.FareConditions`. Null/отсутствующие значения трактовать без маркетинговых обещаний. Не менять домен или таблицы.
- В `tests/Travel.Host.Tests.Integration/Flights` добавить no-DB HTTP contract cases: quote request/response, `aggregateId`, nullable price delta, fare facts, ProblemDetails, anonymous access. Fake bus/provider остаются в тестовой границе; тест не претендует на real Host composition.
- Добавить один `tests/fixtures/flights-booking.json` с canonical quote, changed-price, expired/unavailable и ProblemDetails shapes. Связать .NET serialization и TS decoder точным структурным сравнением. Не строить второй параллельный набор фикстур.
- Gate: focused .NET no-DB test + `dotnet csharpier check .`; подтвердить, что не добавлены migration files и не менялись backend auth rules.

### 2. Добавить узкий booking client

- В `shared/ts/api-client/src` создать `flights-booking.types.ts`, `flights-booking.decoder.ts` и `flights-booking-api.service.ts`. B1 client реализует только anonymous quote; типы B2/B3 добавляются в соответствующих инкрементах. Runtime decoder проверяет используемые поля, допускает неизвестные JSON-поля и не повторяет backend domain validation.
- Quote не получает автоматический retry. Отличать HTTP ProblemDetails, malformed 2xx, timeout и сеть. Не добавлять Authorization к quote. Защитить публичный API `src/index.ts` минимальным экспортом.
- Gate: `npx.cmd nx test api-client`, `npx.cmd nx lint api-client`, тест canonical JSON и отдельных повреждённых форм.

### 3. Встроить quote/detail в поиск

- В `apps/web/src/app/flights` добавить bookable action и отдельное небольшое представление quote внутри `/flights`; partner route не меняет тип. Открытие quote не очищает параметры поиска. Повторный click пока pending блокируется; изменение/уход отменяет клиентское ожидание без утверждения, что серверный quote отменён.
- Сравнить первый quote с search offer по сумме, валюте и значимому маршруту независимо от `priceChanged`; явно запросить принятие изменившегося результата. Для re-quote использовать тот же `aggregateId` и ref, но только до hold. Expiry показывать как серверное время с часовым поясом; при истечении убрать продолжение и предложить re-quote.
- Ошибки: unavailable/validation/unknown outcome/timeout/malformed; ручной новый quote только после решения пользователя. Никаких PII и login в B1.
- В `apps/web/src/app/app.routes.server.ts` сохранить Client render mode на интерактивных маршрутах; прямой reload не делает POST.
- Gate: Angular component/HTTP tests, keyboard/labels/focus, narrow mobile view, `npx.cmd nx test web`, `npx.cmd nx lint web`, `npx.cmd nx build web`.

### 4. Расширить изолированное demo

- В существующем Node stub добавить только `POST /api/flights/orders/quote` с вымышленными offer/price/expiry и проверкой допустимого provider/ref. Demo ref должен однозначно кодировать one-way/round-trip и выбранные даты, иначе одинаковый ref прежних примеров делал quote неоднозначным. Детерминированный aggregate ID привязан к полному ref; чужой ID на re-quote отклоняется. Фиктивный срок должен предшествовать выбранному вылету и оставаться явно помеченным как demo. Это не требует состояния последнего поиска на сервере. Изменение цены и ошибка воспроизводятся тестами; неизвестные booking routes по-прежнему 404. Не импортировать fixture в обычную production web сборку.
- Browser smoke проходит поиск → quote через настоящий Angular demo proxy. Проверить demo-маркер, отсутствие запросов к Host/Keycloak/provider/Anthropic и неизменность partner.
- Gate: `npm.cmd run test:flights-demo`, проверка build replacement и demo баннера; отчёт называет это UX demo, не provider integration.

**Выход B1:** самостоятельный экран проверки настоящей формы HTTP quote на вымышленных данных, с контрактом и честным изменением цены. Следующий код B2 начинается только после просмотра B1 и проверки auth предпосылок.

## B2 — auth, пассажир, hold и confirm

### 5. Проверить локальный auth-контракт до UI

- Установить официальный `keycloak-js` из обычного registry с зафиксированной версией после проверки актуальной документации и текущего Angular build. Не добавлять второй Angular auth framework. В `apps/web` сделать browser-only инициализацию публичного `travel-web`, code flow/PKCE S256, in-memory tokens, controlled refresh, logout/error handling. Конфигурация issuer/clientId/redirect явна, без secret; обычный поиск остаётся анонимным.
- Отдельно проверить, что локальный Keycloak выдаёт для `travel-web` JWT с нужными `aud`, GUID `sub`, `scope=flights:book`. Не копировать dev credentials в код или журнал. Если claim-контракт расходится, остановить B2 и предъявить варианты: realm mapping, backend validation или другой approved auth design. Не делать fake login в обычной сборке.
- Существующий `FlightsApiFixture` использует `TestAuthHandler`: его 401/403 и policy tests доказывают HTTP intent, но не приём Keycloak JWT. Для auth integration добавить отдельный no-DB TestServer с настоящим JWT middleware и локальным Keycloak либо явно оставить это доказательство неполным. Запуск полного AppHost, который автоматически применяет checked-in migrations в Development, **не входит** в этот gate при действующем запрете миграций. Если для real Host/projection end-to-end потребуется отдельная БД и миграции, представить это как отдельную границу согласования.

### 6. Сохранить намерение через login без PII

- После принятия quote записать в `sessionStorage` только `provider`, непрозрачный `providerOfferRef`, `aggregateId`, короткий срок жизни и технический marker версии draft; очистить сразу после callback. Не считать ref публичным, не помещать его в URL/логи; не сохранять токены, пассажира, email, телефон или consent цены. При callback загрузить draft, выполнить re-quote, снова потребовать просмотр цены/маршрута. При отсутствии/ошибке draft вернуть в поиск с понятным сообщением.
- На B2 маршруты checkout/заказа добавить Client render mode и прямой reload без автоматических write requests. Не запускать OIDC при простом посещении `/flights`.
- Gate: auth-service tests с browser guard, redirect/re-quote/expired draft cases, production SSR build.

### 7. Сделать форму и записи безопасными при повторе

- В `apps/web/src/app/flights` добавить форму одного пассажира по фактическому `PassengerInfoDto`, серверные field errors, запрет PII persistence/logging. Данные уходят только в защищённый hold после проверенного quote. Дата рождения — `DateOnly` строка, без UTC сдвига.
- В booking client добавить hold и confirm с Bearer и `Idempotency-Key`. Для каждого пользовательского действия `crypto.randomUUID()` один раз до первого POST; ключ/тело сохраняются в памяти пока исход неизвестен. Повтор сети использует те же байты тела и тот же ключ; изменённое тело не переиспользует ключ. Не делать автоматического нового hold/confirm после 409.
- После hold показать `HeldUntil` из ответа, но не выводить вымышленный deadline при восстановлении через GET. Confirm допускается только после явного review суммы/маршрута; test-wallet маркировка видна в demo и non-Production. `Confirmed` не называется `Ticketed`.
- Gate: TS/Angular tests на double click, in-flight 409, body conflict, timeout до/после ответа, token refresh/401, expiry и отсутствие PII в storage/URL/console; focused backend middleware contract tests. Провайдер и payment не вызываются в demo.

### 8. Доказать границу demo отдельно

- Для browser UX test расширить изолированный stub ответами hold/confirm с однозначной надписью «имитация входа/заказа». Любой demo auth mode выбирается только build-time replacement; обычная сборка не может включить его query/localStorage. Stub не претендует на JWT validation.
- Реальные правила owner/scope/idempotency проверять в .NET HTTP tests; не считать fake browser auth достаточной проверкой. Если безопасный local Keycloak/Host integration без миграций недоступен, отчёт явно оставляет его непроверенным.

**Выход B2:** пользовательский путь до `Confirmed` на вымышленном demo, плюс отдельные доказательства backend policy и OIDC claims в доступной без миграций границе.

## B3 — состояние заказа

### 9. Добавить owner-scoped чтение и ограниченный polling

- В client добавить GET одного заказа, runtime decode нужных полей, 401/403/404 и malformed response. Для `Confirmed` сразу использовать command response, затем polling с ограниченным временем и manual refresh. Дедуплицировать запросы при смене маршрута; не выводить чужой заказ даже из локального draft.
- Страница `/flights/orders/:aggregateId` (Client render) показывает Held, Confirmed, Ticketed, Cancelled, Refunded. `Ticketed` выводит ticket numbers; 404 сразу после 2xx не равен пропаже заказа. `BookedAt` обозначать как время создания/удержания, не как момент оплаты. Список заказов и cancel action остаются следующими UX-срезами.
- Если пользователь вошёл после reload и увидел Held без session deadline, интерфейс честно скрывает таймер и допускает confirm с серверной проверкой. Ошибка expiry возвращает к поиску; никакого второго hold на том же stream.
- Gate: decoder/component tests для delayed projection, stale Held после Confirmed, terminal statuses, direct reload, owner 404 и bounded polling; один browser path до Ticketed на deterministic stub.

### 10. Закрыть документацию и доказательства

- Обновить `README.md`, `docs/architecture/current-state.md`, `docs/examples/flights-requests.json` и нужные ADR amendments по фактически выполненному объёму. Устранить расхождение ADR 0017 с исполняемым raw SHA-256 middleware; не переписывать middleware ради документа.
- Для каждого B1/B2/B3 зафиксировать локальные команды и результат, где использовались fake данные, где проверен настоящий JWT, какие DB/Host gates остались непроверенными. Сверить спецификацию с implementation diff и критериями приёмки.
- Перед публикацией: `git diff --check`, focused frontend tests/build/lint, no-DB contract tests, demo browser test и CI там, где среда позволяет. Не расширять тестовую матрицу без конкретного оставшегося риска.

## Саморевью порядка и сложности

1. B1 не зависит от Keycloak и даёт честный прирост к уже слитому поиску. B2 зависит от quote-контракта и проверки claims; B3 зависит от подтверждения и eventual projection. Обратных зависимостей нет.
2. В плане не спрятаны миграция, реальный provider, платёж или деплой. `OrderResponse.HeldUntil` и quote-idempotency названы отдельными решениями; первое B2/B3 может работать без них с явным UX-ограничением.
3. Один booking client, одна page-level state machine и существующий demo stub достаточны. NgRx, BFF, SSE, генератор SDK и новый mock framework не добавляют нужной проверки на этом этапе.
4. Unit, HTTP и browser tests проверяют разные границы; один и тот же полный набор ошибок не дублируется во всех слоях.
5. После B3 остаются отдельные M1 UX вопросы: cancel action, реальное подтверждение поставщика/платежа, production hosting и наблюдение Ticketed. Это не называется готовностью к реальным продажам.
