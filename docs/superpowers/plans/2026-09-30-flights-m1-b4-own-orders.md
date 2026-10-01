# Flights M1 B4 Own Orders Implementation Plan

> **Authorization:** 2026-10-01 пользователь разрешил после проработки концепции реализовать B4 последовательно или параллельно, затем отдельно разрешил commit/push/PR и уборку. Backend/client, demo/docs и memory service выделены в независимые задачи; лента и browser navigation проверяются вместе.

**Goal:** показать авторизованному пользователю ленту только своих спроецированных заказов с подгрузкой и точным возвратом после B3-страницы заказа.

**Architecture:** Сохранить owner-scoped GET, добавить tie-breaker и no-store. Angular запрашивает 21 запись для порции из 20 и дополняет ленту, автоматически около конца или по кнопке «Показать ещё». Owner-bound service хранит snapshot только в памяти и восстанавливает DOM, anchor и фокус на возврате из B3; observer в этот момент приостановлен.

**Tech Stack:** .NET/Wolverine/EF Core, Angular signals/RxJS, Node demo stub, xUnit/Vitest/Playwright.

**Spec:** `docs/superpowers/specs/2026-09-30-flights-m1-b4-own-orders-design.md` (уточнена по выбору пользователя 2026-10-01).

## Global Constraints

- Fresh fetch 2026-10-01: `origin/dev = 53df375368c1d4fe5b53fe1d3514d2754059e7ed`; managed worktree `C:\Users\Vladimir_sva\.codex\worktrees\flights-b4-own-orders\travel-agency`, branch `codex/flights-b4-own-orders` создана от этого SHA, ancestry проверена. Текущий HEAD не заменял fresh base.
- Только owner-scoped GET; client user ID не передавать серверу. Токены исключительно в памяти; PII, provider ref и токены не писать в URL/storage/логи. В URL допускается только существующий B3 `aggregateId`.
- Никакого cancel UI, реального supplier/payment/Anthropic, применения миграций и деплоя. Демо работает на вымышленных данных, owner/JWT в нём не доказаны.
- Запрос порции: `limit=21`, offset продвигается по 20 сырым позициям. Добавлять до 20 карточек; дубли ID обновляют существующую карточку без перестановки. Не заявлять total/snapshot consistency или устранение пропусков при offset drift.
- Сохранять DOM/anchor/focus в памяти той же вкладки на возврате; не делать первый GET вместо restore. Reload/новая вкладка требуют свежей загрузки и входа. Auth смена очищает cache и видимые данные.

## Review Focus

1. Два заказа с одним `BookedAt`: порядок между страницами стабилен; DB-тест задачи 1.
2. Поздний ответ после ухода, reset или смены owner не отображается; Angular-тест задачи 3.
3. Смена пользователя после начала GET: чужой ответ не попадает на экран; Angular-тест задачи 3.
4. Hold завершён, projection ещё пуста: нет синтетической карточки или ложной окончательной пустоты; тест задачи 4.
5. Демо после restart: список пуст, fixture owner/JWT не выдается за live proof; Node/Playwright тест задачи 5.
6. Browser Back и явный возврат после заказа №32 в ленте минимум 40: те же карточки, координата ±4px, фокус на той же ссылке, без GET offset=0; browser задача 5. Snapshot нельзя перезаписывать нулевыми координатами уже detached DOM при destroy.

---

### Task 1: зафиксировать порядок и HTTP list contract

**Files:** `modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/OrderReadModelQueries.cs`, `modules/flights/Travel.Modules.Flights.Api/Endpoints/ListOrdersEndpoint.cs`; `tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/OrderQueriesTests.cs`; `tests/Travel.Host.Tests.Integration/Flights/FlightsEndpointsHttpTests.cs`.

**Interfaces:** `IOrderReadModelQueries.ListAsync(Guid userId, int limit, int offset, CancellationToken ct)` и `OrderListResponse(items, limit, offset)` сохраняются. Порядок: `BookedAt DESC, AggregateId DESC`.

```csharp
.Orders.Where(o => o.UserId == userId)
.OrderByDescending(o => o.BookedAt)
.ThenByDescending(o => o.AggregateId)
.Skip(safeOffset).Take(clampedLimit)
```

- [x] Добавить DB-тест: три своих заказа с одним `BookedAt`, один чужой, `limit=2`, затем `offset=2`; конкатенация двух страниц равна трём своим ID в `AggregateId DESC`, без повторов и чужого заказа. Отдельно проверить `limit=21` и пустой `offset` выше конца.
- [x] Запустить только `dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration/Travel.Modules.Flights.Tests.Integration.csproj --maxcpucount:1 --filter "FullyQualifiedName~OrderQueriesTests"`; тест на равное время должен до правки сортировки показать недоказанный порядок. Он использует временный Testcontainers PostgreSQL/`EnsureCreated`, не применяет миграции к среде пользователя.
- [x] Добавить к EF query `.ThenByDescending(o => o.AggregateId)` после `.OrderByDescending(o => o.BookedAt)`; повторить тот же focused test. Если детерминизм не подтверждён, не компенсировать клиентской сортировкой.
- [x] В no-DB HTTP fixture через `FakeMessageBus.OnCapture<ListOrdersQuery>` проверить `GET /api/flights/orders?limit=21&offset=20`: query получает GUID из auth, 21/20; JSON содержит только `items/limit/offset` и не содержит пассажирских полей. Проверить `Cache-Control: no-store`; добавить заголовок в endpoint. Сохранить существующие 401 и malformed identity проверки. Запустить `dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --maxcpucount:1 --filter "FullyQualifiedName~FlightsEndpointsHttpTests"`.

### Task 2: типизированный list client

**Files:** `shared/ts/api-client/src/flights-booking.types.ts`, `flights-booking.decoder.ts`, `flights-booking.decoder.spec.ts`, `flights-booking-api.service.ts`, `index.ts`; `apps/web/src/app/flights/flights-booking-api.service.spec.ts`.

**Interfaces:**

```ts
export interface FlightOrderListResponse {
  items: FlightOrderResponse[];
  limit: number;
  offset: number;
}
// FlightsBookingApiService
listOrders(offset: number, accessToken: string | null): Observable<FlightOrderListResponse>;
```

Decoder принимает только `{items, limit: 21, offset: requestedOffset}` и для каждого `item` проверяет строковый `aggregateId` и вызывает `decodeFlightOrderResponse(item, item.aggregateId)`. Неизвестные дополнительные JSON-поля допустимы, как в B3.

- [x] Написать decoder-тест на пустую страницу и 21 валидный fictional item; проверить целые `limit=21`, `offset>=0`, `items.length<=21`, валидность каждого элемента через single-order decoder и уникальность ID в странице. Невалидный item, отсутствующее поле, искажённый itinerary, повтор ID, лишний item или неверные `limit/offset` должны вызвать `FlightBookingContractError` без частичного результата.
- [x] Написать HttpTestingController-тест: URL `/api/flights/orders?limit=21&offset=20`, Bearer только из параметра в обычном режиме, без bearer при `null` для demo; timeout 15 секунд. Проверить, что caller не передаёт `userId` и что ошибка декодирования доходит до UI.
- [x] Добавить тип/decoder и метод client. `listOrders` принимает только безопасный offset (`Number.isSafeInteger`, `>=0`, кратность 20); запросить ровно 21. Decoder сверяет отражённые `limit/offset` с запросом. Существующий `getOrder` и его decoder сохраняются без изменения семантики.
- [x] Запустить `npx.cmd nx test api-client --skipNxCache` и `npx.cmd nx test web --skipNxCache` для затронутого клиента; исправить только ошибки этого среза.

### Task 3: лента, direct route, login и восстановление позиции

**Files:** `apps/web/src/app/app.routes.ts`, `app.routes.server.ts`, `app.routes.spec.ts`; `apps/web/src/app/flights/flights-auth.service.ts`, `flights-auth.service.spec.ts`; новые `flight-orders-page.component.ts/.html/.scss/.spec.ts`, `flight-orders-feed.service.ts/.spec.ts`; `flights-page.component.ts/.html`, `flight-order-page.component.ts/.html/.spec.ts`.

**Interfaces:** маршрут `/flights/orders` идёт перед `/flights/orders/:aggregateId`; `FlightsAuthService.beginLogin('/flights/orders')` возвращает к списку. Новый компонент использует `FlightsBookingApiService.listOrders(offset, token)` и `FlightsAuthService.status`.

```ts
const PAGE_SIZE = 20;
const requestOffset = nextOffset;
const visibleItems = response.items.slice(0, PAGE_SIZE);
const hasNext = response.items.length === PAGE_SIZE + 1;
```

Return path принимает только exact `/flights/orders` или существующий `/flights/orders/{validGuid}`; остальные значения идут на настроенный `/flights` redirect.

- [x] Написать auth тесты: exact `/flights/orders` допустим как return path, `/flights/orders/anything-else`, query, fragment и внешний URL отклоняются; callback очищает OIDC query и после валидации claims загружает список. Demo `beginLogin` остаётся фиктивным и не создаёт токен.
- [x] Написать component tests: anonymous/direct reload показывает login без GET; успешный login отправляет один GET с `offset=0`; loading/empty/error/timeout/malformed/401/403 различимы; 401 и смена `status.userId` немедленно убирают старые карточки. Отдельно проверить, что late response после page change, logout или destroy отменён/проигнорирован.
- [x] Написать batch tests для 0, 1, 20, 21+ item: 20 отображаются, lookahead включает следующую загрузку +20; append/dedup сохраняют предыдущие карточки, ошибка повторяет тот же offset, пустая порция завершает ленту. IntersectionObserver вызывает один loader, auto не двигает фокус, явная кнопка фокусирует первую новую ссылку. Если auto response застал фокус на кнопке, ответ буферизуется до её активации без повторного GET. Карточка содержит route/status/price/hold time, без PII/ticket/cancel.
- [x] Реализовать allowlist login, Client route и семантический ol, observer с rootMargin 400px и постоянную кнопку. Сохранить owner/late-response guards. При next-page ошибке сохранять предыдущие карточки и остановить auto retry; 401/403 очищают всё. Полный refresh только по действию пользователя сбрасывает ленту к началу.
- [x] Реализовать `FlightOrdersFeedService.save/restore/clear/returningFromOrder`: один deep-copied owner-bound snapshot с items/nextOffset/hasMore/anchorId/anchorTop/scrollY/focusId. NavigationStart определяет возврат B3→list и управляет native scrollRestoration только в corridor. Сохранять координаты при link click/NavigationStart, пока DOM существует; не повторять запись на destroy. Проверить смену auth до Angular effect, logout, cloned DTO и native setting lifecycle.
- [x] Restore сначала проверяет accessToken, потом возвращает cached DOM без GET и через afterNextRender восстанавливает anchor relative top и focus({preventScroll:true}). Observer не грузит до первого user scroll/manual action. B3 начинает сверху с фокусом h1. Все list/detail ссылки RouterLink; direct B3 без cache ведёт в начало списка. Unit-tests проверяют cached restore, отсутствие initial GET, недоступность cache после auth expiry и regression detached DOM.
- [x] Запустить `npx.cmd nx test web --skipNxCache`, `npx.cmd nx build web --configuration=production` и demo build. Проверить keyboard navigation и 360px без горизонтального скролла в browser task задачи 5.

### Task 4: ограниченно ждать новый hold в eventual projection

**Files:** `apps/web/src/app/flights/flight-order-handoff.service.ts`, `flight-orders-page.component.ts`, `flight-orders-page.component.spec.ts`, `flights-page.component.spec.ts`.

**Interfaces:** `rememberHeld(id, ownerId)` и `takeOutcome(id, ownerId)` сохраняют B3-семантику. `recentHeld(ownerId): string | null` и `recentHeldDeadline(ownerId): number | null` owner-scoped, deadline recordedAt+30s; чтение не разрушает B3 outcome. Auth effect и синхронные guards очищают hint/outcome при logout/смене owner даже вне списка.

```ts
// Отдельное поле service, не очищаемое takeOutcome().
private recentHold: { aggregateId: string; ownerUserId: string; recordedAt: number } | null = null;
```

- [x] Написать тесты: после `rememberHeld` и последующего B3 `takeOutcome` hint ещё доступен тому же owner; другому owner недоступен; по истечении 30 секунд исчезает; после logout/смены identity старый hint не вызывает GET и не рисует карточку.
- [x] Написать UI тесты: при раннем пустом списке или странице без hinted ID `role=status` сообщает о задержке, GET повторяется раз в 2 секунды не дольше 30 секунд; когда ID появился — таймер прекращается; после 30 секунд есть ручное обновление. При direct reload без hint пустой ответ не запускает polling. Не создавать локальную `FlightOrderResponse` из hint.
- [x] Закрепить абсолютный deadline: list открыт на29s не начинает GET на31s, in-flight GET на29s отменяется на30s, переход к следующей порции завершает waiting banner. Expiry/cancel не очищают предыдущие валидные строки и не переводят UI в auth.
- [x] Реализовать отдельный недеструктивный recent-hold hint в service и bounded refresh только для первой страницы. Проверить, что компонент чистит timer/GET на destroy, навигации и смене owner. Подтверждение старого заказа не запускает бесконечный поиск ID на первой странице.
- [x] Запустить `npx.cmd nx test web --skipNxCache`, включая B3 spec: изменение handoff не должно испортить удержание/подтверждение на странице одного заказа.

- [x] Финальный audit: после await token повторно проверять deadline/current request. Отмена polling синхронно освобождает loader и меняет requestVersion; late catch/finally старого запроса не трогают новый. Регрессии slow token после deadline, active poll→auto append и manual append во время token await прошли RED→GREEN; полный web suite 200/200.

### Task 5: demo, browser proof и документация

**Files:** `tools/demo/flights-search-api.mjs`, `tools/demo/flights-search-api.test.mjs`; `tests/travel-e2e/demo/flights-search.spec.ts`; `README.md`, `docs/architecture/current-state.md`, `docs/examples/flights-requests.json`, `tools/docs/readme-examples.mjs`, `tools/docs/readme-examples.test.mjs`, `tools/docs/readme-smoke.test.mjs`, `tests/Travel.Host.Tests.Integration/Documentation/ReadmeExamples.cs`, `tests/Travel.Host.Tests.Integration/Documentation/ReadmeRequestExamplesTests.cs`.

**Interfaces:** demo `GET /api/flights/orders?limit=21&offset=N` отдаёт `{items, limit, offset}` из локального `Map` в порядке `(bookedAt DESC, aggregateId DESC)`, с `Cache-Control: no-store`, `X-Travel-Demo: fixtures`; default 50/0 и clamp 1–200/offset≥0 повторяют backend. Bearer отклоняется. В demo нет настоящего owner scope.

```js
const ordered = [...orders.entries()].sort((a, b) =>
  b[1].bookedAt.localeCompare(a[1].bookedAt) || b[0].localeCompare(a[0]),
);
const items = ordered.slice(offset, offset + limit).map(([id, order]) => orderResponse(id, order));
```

- [x] Написать Node tests: пустой список до hold; 1/21+ вымышленных записей после hold; paging без пассажира; list GET не меняет progression single-order GET; bearer отклонён; новый server instance пуст. Для 21+ генерация идёт через существующие фиктивные quote/hold helpers, без внешнего HTTP.
- [x] Реализовать list route в demo с теми же параметрами и DTO; вынести общий pure `orderResponse` builder для list и single GET, чтобы статусы и поля не расходились. Не добавлять fake user ID в URL и не обещать owner enforcement.
- [x] Разрешить GET list в Playwright network allowlist. Через реальный локальный proxy создать 45 вымышленных hold, получить 20+20 автоматической подгрузкой, открыть заказ №32, вернуть Browser Back и явной ссылкой B3: 40 строк, та же координата ±4px/фокус и нет нового list GET. Проверить 360px, reload→login→первая порция, отсутствие PII/token/cache в URL/storage/console. Отдельный browser case без IntersectionObserver проверяет Enter на кнопке, сохранение20 строк после503, ручной retry и фокус на первой новой ссылке.
- [x] Обновить README/current-state и request catalog новым `listOrders` GET с `Authorization: Bearer {{jwt}}` и серверными default `limit=50&offset=0`; отдельно описать UI lookahead `limit=21`. Расширить точный список ID/route/title/число записей в `readme-examples.mjs` и `ReadmeExamples.cs`, добавить проверки list GET в Node и C# docs tests, а list route — в оба mock OpenAPI объекта `readme-smoke.test.mjs`. Различить demo proof, no-DB HTTP proof и отсутствующее live issuer/EF acceptance.
- [x] Запустить `node --test tools/demo/flights-search-api.test.mjs`, `npm.cmd run test:flights-demo`, `npm.cmd run check:readme-examples`, `npx.cmd biome ci .`, `npx.cmd nx lint api-client`, `npx.cmd nx lint web`, `dotnet csharpier check .`, `git diff --check`. Зафиксировать точные результаты и любые непроверенные live gates. Не применять миграции, не обращаться к реальным поставщикам/платежам/Anthropic, не деплоить.

## Самопроверка плана

- Каждый критерий спецификации покрыт задачами 1–5; owner scope проверяется DB/HTTP, stale identity — UI, demo граница — Node/browser.
- Типы `FlightOrderListResponse`, `listOrders`, `recentHeld`, route и paging-значения совпадают между задачами. `limit=21` — lookahead для 20 видимых карточек.
- План не утверждает snapshot consistency, истинность demo auth или готовность real Host; для них оставлены явные ограничения.
- Реализация и публикация разрешены; окончательные команды/результаты и исправления фиксируются в B4 local result. Remote CI проверяется на опубликованном PR; после завершения работы managed worktree архивируется по просьбе пользователя.
