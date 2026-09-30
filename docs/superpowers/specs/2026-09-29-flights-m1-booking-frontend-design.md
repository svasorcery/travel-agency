# Flights M1: frontend бронирования одного пассажира

**Статус:** дизайн согласован 2026-09-29; B1 merged в PR #19; B2 реализован локально для review; B3 не начинался.

**База B2:** `origin/dev` после fetch 2026-09-30 — `334de7622808c64edce3c0b6c83e8864e9d1dda3` (merge PR #19).

**Ветка/worktree B2:** `codex/flights-b2-audience`, отдельный worktree, начинается ровно с указанной базы; ancestry проверен.

**Цель:** после анонимного поиска дать человеку проверить актуальное bookable-предложение, войти, оформить одного пассажира, подтвердить заказ и увидеть его состояние. Демо остаётся вымышленным; работа реального поставщика, платёж и выдача билета им не доказываются.

## 1. Выбор объёма

| Вариант | Польза | Цена |
| --- | --- | --- |
| **Последовательные B1 → B2 → B3, рекомендуется** | Каждый шаг даёт проверяемый пользовательский результат; auth и денежные побочные эффекты получают отдельные проверки | Весь путь появляется после трёх инкрементов, а не в первом PR |
| Один PR сразу до Ticketed | Быстрее выглядит как «полный путь» | Смешивает новый OIDC, PII, quote/hold/confirm, идемпотентность, eventual read model и демо; сложнее установить причину дефекта |
| Только статический checkout | Быстрый экран | Не проверяет цену, auth, запись и статус; не продвигает M1 |

**B1 — проверка предложения и цены.** На карточке bookable появляется действие «Проверить цену». Анонимный quote возвращает свежий маршрут, цену и срок предложения. Пользователь видит изменения и подтверждает именно новую версию. Этот инкремент не запрашивает персональные данные и не выполняет hold/confirm.

**B2 — вход, пассажир и подтверждение.** После принятия quote пользователь входит через Keycloak, вводит данные одного пассажира, выполняет hold, видит его срок, отдельно подтверждает заказ. Подтверждение в non-Production использует существующий test wallet; интерфейс никогда не подразумевает оплату реальными деньгами. Действия hold и confirm разделены и снабжены разными стабильными Idempotency-Key.

**B3 — статус и восстановление.** Страница конкретного заказа показывает Held/Confirmed/Ticketed/Cancelled/Refunded по данным backend. После confirm немедленный ответ показывается сразу, затем ограниченный GET polling ждёт проекцию. Обновление страницы и повторное открытие по сохранённой ссылке поддерживаются в пределах имеющегося контракта. Список и отмена заказов — отдельные последующие UX-срезы M1: backend endpoints существуют, но здесь действия не добавляются.

После одобрения этого дизайна реализация идёт в указанном порядке с проверкой каждого инкремента. Первый review реализации — B1; B2 и B3 не маскируются под готовые, пока их проверки не прошли.

## 2. Фактические границы системы

- В `apps/web` есть клиентская `/flights`, search client и demo stub; frontend-auth, booking client, passenger form и order UI отсутствуют. `/flights` использует `RenderMode.Client`; обычный dev proxy `/api/**` ведёт к Host, demo proxy изолирует `/api/**` и `/events/**` на локальную заглушку.
- `POST /api/flights/orders/quote` анонимен. Тело: `{ providerOfferRef, provider, aggregateId? }`. Результат: `{ aggregateId, offer, priceChanged, oldAmount?, oldCurrency?, newAmount?, newCurrency? }`. Первый quote создаёт Marten stream; re-quote допустим для того же ref только в состоянии OfferQuoted. У quote нет HTTP Idempotency-Key: его сетевую неопределённость нельзя автоматически лечить бесконечным повтором.
- `POST /api/flights/orders/hold` и `/confirm` требуют JWT с `flights:book` и GUID user id. Hold принимает ровно одного пассажира. Для обоих endpoint middleware требует GUID `Idempotency-Key`, кеширует успешный 2xx и возвращает 409 для in-flight или несовпадающего тела.
- `GET /api/flights/orders/{aggregateId}` и `GET /api/flights/orders` требуют JWT, читают owner-scoped EF projection. Проекция eventual; до hold анонимный quote там не виден. `OrderResponse` не содержит `HeldUntil`. `BookedAt` заполняется временем hold, поэтому UI не называет это время оплатой или выпиской билета.
- `GET /events/flights/orders/{id}` требует JWT и проверяет владельца, но нативный `EventSource` не передаёт Bearer header. В этом этапе выбран bounded GET polling; SSE не нужен для первого статуса.
- Realm fixture содержит публичный `travel-web` с standard flow и PKCE S256 для `http://localhost:4200/*`; backend Identity валидирует JWT, но не предоставляет frontend login. Конфигурацию приложения/issuer и реальную выдачу нужных claims нужно проверить в отдельном локальном auth smoke. Не переносить dev-учётные данные в UI, тесты или документацию.
- Bookable и partner различаются по текущему `OfferDto`: только bookable имеет `providerOfferRef` и `expiresAt`. Partner не отправляется в quote/hold/confirm. Существующий `.invalid` URL в поисковом demo не становится действием перехода.

Источники: `apps/web/src/app`, `shared/ts/api-client/src`, `modules/flights/Travel.Modules.Flights.Api/Contracts/Contracts.cs`, `Endpoints/{QuoteOffer,HoldOffer,ConfirmOrder,GetOrder,ListOrders,OrderEventsSse}Endpoint.cs`, `Middleware/IdempotencyKeyMiddleware.cs`, `Application/Handlers/Booking`, `Infrastructure/Persistence/OrderReadModelEventApplier.cs`, `modules/identity`, `infra/keycloak/travel-realm.json`, ADR 0010/0015/0016/0017 и текущий search-дизайн.

## 3. Контрактные пробелы и решения до реализации

| Пробел | Варианты | Предлагаемое решение |
| --- | --- | --- |
| Quote содержит `OfferDto`, но не `FareConditions`, хотя aggregate сохраняет условия и передаёт их в hold | Не показывать условия; расширить только quote response; расширить общий search `OfferDto` | **Добавить `FareConditionsDto` только в `QuotedOfferResponse`** с change/refund flags и количествами багажа. Это небольшое HTTP-изменение без схемы БД. Текущий Duffel mapper берёт максимум багажа по сегментам: UI обязан назвать его максимумом отдельного сегмента, не обещая такую норму на всём маршруте. Не обещать бесплатные изменения/возврат и не выдумывать сборы. |
| `OrderResponse` не имеет `HeldUntil` | Новый owner-scoped checkout GET из Marten; миграция EF projection; хранить deadline из hold response только в сессии | **Для первого B2/B3 не вводить новый backend read path/миграцию.** В текущей вкладке показать срок из `HeldOrderResponse`; при повторном открытии без него показать Held без таймера и дать backend решить допустимость confirm. Если требование «точный срок на любом устройстве» станет обязательным, отдельно выбрать Marten GET или миграцию проекции. |
| До hold quote не привязан к пользователю и не читается через order GET | Новое anonymous recovery API; session-scoped draft; отсутствие восстановления | **Хранить минимальный provider/ref/aggregateId в `sessionStorage` только на время OIDC redirect, с коротким сроком жизни и очисткой после использования.** Provider ref — непрозрачный идентификатор, его не считать публичным и не писать в URL/логи; PII и токены туда не писать. После возврата всегда re-quote и заново показать цену/маршрут. Если draft отсутствует или истёк, начать с поиска. |
| Положительный HTTP quote потерян в сети | Автоматический повтор может создать второй stream; новый quote idempotency contract; явный ручной restart | **Не делать автоматический retry.** Показать неопределённость и ручное начало проверки предложения заново; возможный незавершённый quote stream не называется заказом. Отдельный backend idempotency контракт нужен, если требуется строгая очистка/recovery. |
| OpenAPI для `IResult` недостаточно точен для генерации booking client | Править endpoint metadata и генерировать SDK; узкие TS-типы и общие fixtures | **Узкий типизированный client с runtime проверкой используемых полей и общей JSON-фикстурой**, как в поиске. Разрешить отдельную правку OpenAPI только при конкретном потребителе или drift. |
| ADR 0017 описывает канонический/HMAC hash и UUID v4, а текущий middleware хеширует raw body SHA-256 и принимает любой GUID | Следовать ADR буквально; следовать коду; исправить ADR в отдельной документационной правке | **Клиент посылает UUID v4 и повторяет точное тело.** Тесты привязываются к исполняемому middleware; расхождение ADR исправляется документально в B2, без переписывания middleware в этой frontend-задаче. |

Эти решения сохраняют существующие backend business rules. Если без точного `HeldUntil` после любого reload или без строгой quote-idempotency нельзя считать checkout приемлемым, это меняет контракт и объём: сначала нужен отдельный backend-срез, затем frontend. Такой выбор должен быть сделан до B2, а не скрыт в клиентском workaround.

## 4. Пользовательский путь и состояния

1. На bookable карточке человек выбирает «Проверить цену». Partner карточка остаётся отдельным типом; действие покупки ей не назначается. Повторный click во время quote не создаёт ещё один запрос.
2. B1 показывает quote как источник актуальной цены: точный маршрут, валюту, срок `expiresAt` и после согласованного DTO только фактически доступные условия. Сравнение с поисковой карточкой выполняется на клиенте для первого quote; `priceChanged` backend относится к re-quote того же aggregate. Предыдущая и новая суммы показаны рядом, а при изменении itinerary обе маршрутные карточки стоят в одной панели; при повторной проверке отображается и прежняя quoted-цена. Любое изменение цены, валюты или показанной длительности/маршрута требует явного подтверждения свежего quote. Устаревшая поисковая цена не подставляется в checkout.
3. В B2 вход запускается только по действию оформления. После OIDC redirect выполняется re-quote того же provider/ref/aggregateId. Если offer недоступен, истёк или изменился, человек возвращается к review; consent предыдущей версии не переносится. Неавторизованный пользователь не видит форму PII как готовую к отправке.
4. Форма имеет ровно одного пассажира и backend-поля `givenName`, `familyName`, `dateOfBirth`, `gender`, `email`, `phone`; ошибки поля доступны с клавиатуры и screen reader. PII остаётся в памяти компонента до отправки, не пишется в URL, localStorage, sessionStorage, аналитику или console. На уход со страницы данные очищаются.
5. Hold получает `aggregateId` и один элемент `passengers`; клиент создаёт UUID v4 до первой отправки. Пока исход неизвестен, кнопка не порождает новый ключ. Retry того же тела использует тот же ключ. После 2xx UI показывает `heldUntil` и последнюю подтверждённую цену; изменение тела требует нового намерения и нового ключа, но существующий Held не переоформляется автоматически.
6. Confirm — отдельное явное действие после hold. Перед ним пользователь видит итоговую цену, маршрут и предупреждение, что в demo используется тестовый кошелёк. Новый UUID v4 привязан к точному `{aggregateId}`. Двойной click блокируется. При timeout/обрыве сеть не объявляется ни успехом, ни провалом: сначала GET/повтор с тем же ключом и телом, затем ручной recovery. 409 InFlight предлагает подождать, BodyConflict считается ошибкой клиента; не выдавать новый ключ автоматически ради обхода конфликта.
7. Ответ `Confirmed` означает подтверждение, не выписку билета. Страница заказа показывает текущий command outcome, затем читает owner-scoped projection. Временный 404/старый Held сразу после 2xx — «обновляем статус», не «заказ потерян». Ticketed отображает номера билетов; Cancelled и Refunded имеют отдельные формулировки. Polling имеет предел по времени и ручное обновление после него.

Состояния отдельно для каждого запроса: idle, pending, success, known validation/expiry/price change, auth required/expired, 409 conflict, network/timeout unknown, server unavailable, malformed 2xx. Для неизвестного исхода записи нет оптимистического финального статуса. Ошибки ProblemDetails выводятся через безопасный код/публичное сообщение, без сырого stack trace.

## 5. Авторизация и SSR

Предпочтительный клиент — официальный `keycloak-js` в тонкой Angular-службе: browser-only code flow + PKCE S256, токены только в памяти, обновление перед защищённым запросом, Bearer для hold/confirm/order GET, но не для анонимного quote. Не нужен собственный OAuth-клиент, глобальный auth-store или guard на анонимный поиск. Официальная документация: <https://www.keycloak.org/securing-apps/javascript-adapter> (включая token storage, PKCE и ограничения silent SSO в современных браузерах).

Конфигурация issuer/clientId/redirect URI должна быть явной для локального окружения и не содержать секрет. `travel-web` fixture допускает `localhost:4200`, но поисковый demo работает на `4201`: использовать его как доказательство реального Keycloak login нельзя без отдельной допустимой redirect-конфигурации. Для B1 auth не нужен. Перед B2 проверяются login callback и фактически выданные `aud`, GUID `sub`, `scope=flights:book`; существующий no-DB HTTP fixture применяет тестовую auth-схему и сам по себе не доказывает приём реального JWT. Отдельный no-DB TestServer с настоящим JWT middleware или безопасный Host integration нужен для полного auth-доказательства. При отсутствии нужного claim UI показывает ошибку доступа, не подделывает JWT.

Checkout и order routes получают `RenderMode.Client` до wildcard. SSR не создаёт quote, hold, confirm или фонового login; прямой GET/reload не отправляет запись. Browser-only API (`sessionStorage`, `crypto.randomUUID`, Keycloak adapter) не вызываются на сервере. Production/standalone SSR build остаётся обязательной проверкой; фактический production reverse proxy/hosting — отдельный gate, как у поискового среза.

## 6. Локальное демо и проверка безопасности

B1 расширяет существующий детерминированный Node stub только для `POST /api/flights/orders/quote`: вымышленный provider/ref, JSON с текущим DTO, фиксированные цены, вычисляемый из даты рейса фиктивный срок и сценарий изменённой цены/недоступного предложения. Один и тот же ref в прежних one-way и round-trip примерах не позволял quote определить выбранный маршрут: quote принимает только provider/ref. Поэтому **только в demo** ref детерминированно кодирует тип поездки и даты, а stub восстанавливает соответствующий фиктивный itinerary без server-side состояния последнего поиска. Aggregate ID детерминированно привязан к полному ref; re-quote с ID другого ref отклоняется. Фиктивный expiry всегда предшествует вылету, fetchedAt предшествует expiry. Общие fixtures и .NET HTTP-тест обновлены синхронно; wire-форма ref остаётся строкой. Stub не обращается к Host, Keycloak, Anthropic, Duffel или платным сервисам. Search demo proxy по-прежнему перехватывает весь `/api/**` и `/events/**`; неизвестные маршруты получают 404. На quote-экране явно написано, что demo-цена и срок вымышлены.

Для B2/B3 UI demo может иметь явно подписанную имитацию входа и ответов hold/confirm/status в том же изолированном stub **только если** это не смешивается с проверкой реального JWT. Реальная граница авторизации проверяется отдельно HTTP-тестами Host с тестовой identity и локальным Keycloak smoke при наличии безопасного стенда. В итоговом отчёте нужны две строки доказательства: «демо UX с fake data» и «backend/auth integration». Нельзя объявить одно подтверждением другого. Платёж и provider booking не вызываются в браузерном демо.

Не добавлять новый универсальный mock framework, новый глобальный state manager, SSE клиент, persist PII, email-сервис или migration ради этого интерфейса.

## 7. Тесты и приёмка

| Инкремент | Приёмка |
| --- | --- |
| B1 | Bookable → quote/detail для one-way/round-trip; partner исключён; свежие цена/маршрут/expiry; изменение требует согласия; loading/error/unavailable/timeout/malformed; отсутствие auto retry и PII; общий fixture .NET endpoint ↔ TS decoder ↔ demo stub; browser smoke через настоящий demo proxy; SSR/client-route и production build. |
| B2 | Реальный local Keycloak/Host claim smoke до включения auth UI; возвращение к draft с обязательным re-quote; один пассажир; доступные validation/errors; bearer только на защищённых запросах; UUID v4 и same-key/same-body retry для hold и confirm; concurrent/unknown outcome/409/expiry; никакие реальные provider/payment вызовы в demo. |
| B3 | Имmediate Confirmed vs eventual projection; временный 404; Held без выдуманного deadline после потери draft; bounded polling и ручной refresh; Ticketed с номерами, Cancelled/Refunded отдельно; чужой заказ не раскрывается; прямой URL/reload; no PII persistence. |

Обязательные уровни: pure TS decoder/state tests, Angular component+HttpClient tests, Node stub tests, .NET no-DB wire/authorization tests, ограниченный Playwright browser path, затем отдельная проверка реального JWT и projection на безопасном стенде. Большая матрица ошибок не повторяется целиком в browser E2E. Тесты с БД не запускаются локально только ради этого дизайна и не применяют миграции без отдельной авторизации. Если реальный Host/projection gate потребует применения миграций, его результат остаётся непроверенным до отдельного решения; B2/B3 не называются production-ready. Текущий merge CI уже проверил предыдущий search-срез; он не является доказательством B1–B3.

## 8. Саморевью и упрощение

- Проектная цель M1 включает single-passenger booking и view/cancel; этот документ доводит до view конкретного заказа, а list/cancel оставляет явными следующими UX-срезами. M1 frontend не объявляется законченным после B1 или B3.
- Quote-first остаётся самостоятельным пользовательским результатом и даёт честную проверку цены без нового auth-кода. Две обязательные backend-доработки **не** закладываются молча: fare-conditions DTO рекомендован, а recovery deadline/quote idempotency вынесены как варианты с последствиями.
- Search card и quote могут различаться по цене/маршруту; `priceChanged=false` первого quote не доказывает совпадение с поиском. Проверка будет явно тестировать это.
- Тестовый wallet не означает билет: `Confirmed` и `Ticketed` различаются в UI и критериях. Projection lag и потерянный ответ на запись не превращаются в ложный успех.
- Не требуются BFF, NgRx, SSE, генератор SDK, новая БД или миграция. Если реальная Keycloak-конфигурация/claims не совпадут с ожиданием, этап B2 останавливается на зафиксированном контрактном решении, а не на fake-login workaround в обычной сборке.
