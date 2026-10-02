# Flights M1 B5 Own Order Cancellation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans for native implementation task-by-task. Steps use checkbox (`- [ ]`) syntax. User-requested independent review follows implementation; this plan does not authorize implementation before approval.

**Goal:** Отмена своего демонстрационного Held/Confirmed заказа со страницы B3, честные command outcomes и сохранение обновлённой карточки/места возврата B4.

**Architecture:** Вариант A спецификации: полноценный offline UI/demo и строгий backend provider safety gate. Root Flights memory service координирует confirm/cancel в пределах вкладки и хранит terminal command overlay; feed service сохраняет позицию и патчит только существующую карточку. Реальная Duffel cancellation возвращает явный unsupported до HTTP.

**Tech Stack:** Angular 21 signals/RxJS, HttpClient runtime decoders, Node demo HTTP, Vitest/Playwright; .NET 10 ErrorOr, Marten/Wolverine, EF read-model; WireMock и существующие no-DB HTTP fixtures.

**Spec:** [B5 design](../specs/2026-10-01-flights-m1-b5-own-order-cancellation-design.md).

**Status:** Approved 2026-10-02, variant A, native execution and independent final review. Fresh execution base `0a54e53d46107f262614248b73b1af3e79be0552` confirmed by re-fetch. Execution evidence lives in the B5 result document; examples below are plan contracts.

## Global Constraints

- Реальных поставщиков, платежей, Anthropic и иных платных API не вызываем.
- Токены и клиентское состояние операций — только память вкладки.
- PII не записывается в URL, browser storage, history.state или логи.
- Backend JWT/owner/scope не ослабляются.
- Fake auth остаётся только file replacement `flights-demo` и test fixtures.
- Не выполняем локальные миграции, schema apply, запуск Host/AppHost или деплой; CI/CD-конфигурацию не меняем.
- Paid AI-evals пропускаем, `run_paid_ai_evals` не включаем.
- Возврат денег клиенту, сумма/срок/гарантия возврата, отмена Ticketed, новый payment flow и полноценное управление реальными заказами вне B5.

## Review Focus

- Retry после unknown получает 4xx: прежний неизвестный исход остаётся барьером, новый key не появляется (Task 3).
- Старый successful POST приходит после A → B → A: epoch guard не обновляет view/feed (Task 3).
- Destroy B2/B3 во время confirm: общий attempt сохраняется, cancel заблокирован на повторно открытом заказе (Tasks 3–4).
- Stale list DTO приходит через buffered/manual append после cancel: карточка не откатывается и anchor не теряется (Task 5).
- Legacy/system Cancelled/no-op: success wording не обещает подтверждение поставщика или возврат денег (Tasks 1, 4).

## Task 0: свежая база и изоляция после согласования

**Files:** только два B5 документа перенести в новый managed worktree; исходный checkout не переключать на произвольную старую ветку.

- [ ] Выполнить `git fetch origin dev`, записать `git rev-parse origin/dev`, проверить checkpoint ancestry. Если dev продвинулся, перечитать изменившиеся Flights/auth/UI contracts, поправить spec/plan при существенном изменении до кода.
- [ ] Проверить `list_artifacts`; не восстанавливать archived B4. Создать managed worktree через app tool с `ref` равным **точному fetched SHA**, именем `flights-b5-cancel`; создать `codex/flights-b5-own-order-cancellation` от него. Проверить worktree HEAD и `git merge-base --is-ancestor <base> HEAD`. Если нужная ветка существует с другой ancestry, создать новую чистую ветку, не force-move/rewrite.
- [ ] Перенести B5 docs; убрать только их исходные untracked копии после сравнения SHA256, чтобы local dev снова был чистым. Прочитать root/nested AGENTS и relevant implementation/testing skills. Зафиксировать local no-schema fixture allowlist; никакого Host startup.

## Task 1: backend прекращает ложную отмену

**Files:** Modify `modules/flights/Travel.Modules.Flights.Application/Handlers/Booking/CancelOrderHandler.cs`, `modules/flights/Travel.Modules.Flights.Core/Errors/FlightsErrors.cs`, `modules/flights/Travel.Modules.Flights.Core/Providers/IFlightBookingProvider.cs`, `modules/flights/Travel.Modules.Flights.Infrastructure/Providers/Duffel/DuffelFlightBookingProvider.cs`, `modules/flights/Travel.Modules.Flights.Api/Endpoints/{CancelOrderEndpoint,GetOrderEndpoint}.cs`, `modules/flights/Travel.Modules.Flights.Api/Middleware/IdempotencyKeyMiddleware.cs` (только no-store cancel/replay header). Test existing `CancelOrderHandlerTests`, `BookingTransitionDecisionTests`, `DuffelFlightBookingProviderTests`, `FlightsEndpointsHttpTests`, `IdempotencyKeyMiddlewareTests`.

**Interfaces:** `Task<ErrorOr<Success>> CancelOrderAsync(string providerOrderId, CancellationToken ct)` сохраняет сигнатуру; Success документируется как terminal cancellation. Новые Error.Conflict коды `Flights.ProviderCancellationNotSupported` и `Flights.ProviderOrderMissing`, статус 409. CancelOrderCommand/OrderResponse/public domain states не меняются.

**Notification files:** Modify `modules/flights/Travel.Modules.Flights.Application/Handlers/Notifications/SendOrderCancellationEmailHandler.cs`; Create `tests/flights/Travel.Modules.Flights.Tests.Unit/Notifications/CancellationEmailTextTests.cs`; Modify existing `tests/flights/Travel.Modules.Flights.Tests.Integration/Notifications/EmailNotificationTests.cs` (CI only: fixture применяет EnsureCreated); regression `NotificationVersionGateTests.cs` остаётся. Templates сохраняются: исправляется источник RefundText, не механизм доставки.

- [ ] Написать regression: ErrorOr provider failure → неизменный stream/event count, нет notification/reconcile/success; Held success capability, Confirmed success capability, missing provider ID, foreign owner для terminal no-op, Cancelled/Refunded no-op; конфликт сохранения не даёт 200. В DB tests использовать существующий disposable PostgreSQL, запуск **только CI**. Добавить WireMock regression, который ожидает unsupported и 0 requests без schema.

```csharp
var result = await _sut.CancelOrderAsync("ord_fixture", CancellationToken.None);
result.IsError.ShouldBeTrue();
result.FirstError.Code.ShouldBe("Flights.ProviderCancellationNotSupported");
_server.LogEntries.Count().ShouldBe(0);
```

- [ ] Запустить безопасный provider regression отдельно и увидеть RED на create-only реализации. Handler DB RED не запускать локально; сохранить expectation и затем доказать real handler в CI.
- [ ] Изменить Allowed branch: проверить непустой ProviderOrderId, при provider ErrorOr вернуть Errors до append. У Duffel убрать create call в cancel и вернуть unsupported до сети. Не менять confirm/payment компенсации или global transport. Добавить no-store до middleware replay и на GET/cancel endpoint.

```csharp
if (string.IsNullOrWhiteSpace(agg.ProviderOrderId))
    return FlightsErrors.ProviderOrderMissing;
var cancelResult = await bookingProviders.Single().CancelOrderAsync(agg.ProviderOrderId, ct);
if (cancelResult.IsError)
    return cancelResult.Errors;
// Existing event + reconcile + sibling notification commit follows confirmed Success only.
```

- [ ] Добавить HTTP regressions: bodyless owner mapping, missing/invalid key, missing scope, ambiguous subject, safe ProblemDetails errors, 409 unsupported/missing order, 200 Cancelled/Refunded snapshot, replay no-store. Fake bus tests не выдавать за real handler.
- [ ] RED cancellation email RU/EN: новый unit test вызывает internal ResolveLocalisedCancelTexts для User/Airline/System и проверяет нейтральную точную фразу, без обещания/срока. Implement «Отмена заказа не подтверждает возврат средств» / «Order cancellation does not confirm a refund». Existing real renderer/fake-sender fixture проверяет resulting body в CI; локально этот класс не запускать, поскольку InitializeAsync вызывает EnsureCreated. Readiness gate покрыт прежним unit suite, реальные письма не отправляются.

```csharp
var (_, refundText) = SendOrderCancellationEmailHandler.ResolveLocalisedCancelTexts(
    CancelReason.User, CultureInfo.GetCultureInfo("ru"));
refundText.ShouldBe("Отмена заказа не подтверждает возврат средств");
```
- [ ] GREEN локально: domain/unit, WireMock provider, no-DB HTTP/middleware fixtures после source audit. .NET build; DB regression и existing real wiring/commit/projection tests остаются mandatory CI gate.

## Task 2: точный cancel client и decoder

**Files:** Modify `shared/ts/api-client/src/{flights-booking-api.service,flights-booking.types,flights-booking.decoder,index}.ts`; Tests `shared/ts/api-client/src/flights-booking.decoder.spec.ts`, `apps/web/src/app/flights/flights-booking-api.service.spec.ts`.

**Interfaces:** `CancelledFlightOrderResponse` — `FlightOrderResponse` со status Cancelled | Refunded; `decodeCancelledOrderResponse(value: unknown, expectedAggregateId: string): CancelledFlightOrderResponse`; API `cancel(aggregateId: string, idempotencyKey: string, accessToken: string | null): Observable<CancelledFlightOrderResponse>`.

- [ ] RED: decoder допускает только same-ID terminal snapshot с обязательным соответствующим timestamp. Cases null/empty/wrong ID/status/malformed itinerary/invalid timestamp/204; заголовки exact key и optional bearer, no query/body; retry одинаковых path/body.

```typescript
api.cancel(id, key, token).subscribe();
const req = http.expectOne(`/api/flights/orders/${id.toLowerCase()}/cancel`);
expect(req.request.method).toBe('POST');
expect(req.request.body).toBeNull();
expect(req.request.headers.get('Idempotency-Key')).toBe(key);
```

- [ ] Implement validated canonical ID, UUID v4 header, `.post<unknown>(path, null, { headers })`, timeout 15_000 и dedicated decoder. Не менять GET semantics для legacy states; не передавать owner/body/refund request.
- [ ] GREEN: `npx.cmd nx test api-client --skipNxCache`, затем focused/full `web` API-client consumer tests. Проверить exports и production/demo TypeScript builds позже в Task 6.

## Task 3: память confirm/cancel и owner epoch

**Files:** Create `apps/web/src/app/flights/flight-order-operations.service.ts` и `.spec.ts`; Modify `flights-auth.service.ts`, `flights-auth.service.demo.ts` только для synchronous logout/invalidation contract и corresponding auth specs. Service uses existing booking API; никаких новых dependencies/persistent stores.

**Interfaces:** `OrderOperationKind = 'confirm' | 'cancel'`; `OrderOperationState = 'pending' | 'success' | 'rejected' | 'conflict' | 'unknown'`; private immutable Attempt (ownerUserId, identityEpoch, aggregateId, kind, key, method/path/body, dispatchedAt, hadUnknown). Public methods:

```typescript
startConfirm(aggregateId: string, ownerUserId: string): boolean;
startCancel(aggregateId: string, ownerUserId: string): boolean;
retry(aggregateId: string, ownerUserId: string): boolean;
operation(aggregateId: string, ownerUserId: string): OrderOperationView | null;
blocksWrite(aggregateId: string, ownerUserId: string): boolean;
overlay(order: FlightOrderResponse, ownerUserId: string): FlightOrderResponse;
clear(): void;
```

`OrderOperationView` содержит kind/state/safe code и command result; service выдаёт immutable/cloned view. Вход start возвращает false при конкурирующей попытке/unknown, true при synchronous reservation. Fresh consent/component guards остаются у вызывающего; service повторно проверяет owner. Для confirm result использовать существующий ConfirmedFlightOrderResponse, для cancel — новый snapshot. Overlay Confirmed над Held использует существующее отображение B3; cancel overlay возвращает полный command DTO. Root service владеет POST, user intent и status observe доступны signals; lifetime root, не component.

- [ ] RED: repeated start, confirm→cancel и cancel→confirm races; async token до dispatch; route destroy; 15s timeout; same-key retry; InFlight vs BodyConflict/ConcurrencyConflict; unknown→4xx retains barrier; 24h cutoff; refreshed same owner vs different owner; A→B→A; quarantined same-owner reauth; explicit logout clears before redirect promise settles.

```typescript
expect(operations.startConfirm(id, owner)).toBe(true);
expect(operations.startCancel(id, owner)).toBe(false);
// Resolve confirm transport with timeout, destroy initiating view, reopen B3.
expect(operations.operation(id, owner)?.state).toBe('unknown');
expect(operations.startCancel(id, owner)).toBe(false);
operations.retry(id, owner);
expect(confirmRequests[1].key).toBe(confirmRequests[0].key);
expect(confirmRequests[1].serializedBody).toBe(confirmRequests[0].serializedBody);
```

- [ ] Implement memory map scoped owner+epoch+aggregate; one writable operation per order; exact immutable body/path/key. Token не входит в attempt. Final decode success записывает command evidence; generic 4xx после unknown не стирает hadUnknown. Graceful private-data purge on auth error, minimal attempt quarantine, explicit logout/identity switch full purge. Service ignores all old-epoch completions and doesn't emit navigation on destroyed views.
- [ ] Связать successful cancel с `feed.patchKnownOutcome`, определённым Task 5; зависимость operations → feed односторонняя, feed не импортирует operations. List/page применяют overlay явно. Не делать циклическое DI. Для отсутствующего feed snapshot patch no-op.
- [ ] GREEN web service/auth tests; тесты не читают реальные tokens, не используют storage/real Keycloak. Зафиксировать ограничение reload/multi-tab в публичной UI-копии, а не маскировать backend serialization клиентским map.

## Task 4: B2/B3 подтверждение, отмена и command/projection UI

**Files:** Modify `flight-booking-panel.component.ts` и `.spec.ts` (только confirm), `flight-order-page.component.{ts,html,scss,spec.ts}`, при необходимости `flight-order-handoff.service.ts` и `.spec.ts` только для согласования existing Held/Confirmed handoff. Не переносить passenger hold в operations.

**Interfaces:** оба confirm entrypoints вызывают Task 3 `startConfirm/retry/operation`. B3 `requestCancellation`, `dismissCancellation`, `acceptCancellation`, `retryCancellation`; template читает effective order через operation overlay. Existing routes/API remain.

- [ ] RED: no POST до consent, Escape/оставить заказ focus return, double click, pending confirm из B2 при переходе на B3, owner change в confirmation section, demo direct B3 без login, pending cancel блокирует confirm, Ticketed отсутствует action; statusChanged while review закрывает review.
- [ ] Заменить только component-local confirm execution/attempt ownership общим service; component-local GET generations сохранить. B2 output/handoff emitted только активному view той же identity. B3 re-entry читает operation; visible order auth guard действует и в demo, где synthetic owner `demo-only` известен только после demo login.
- [ ] Добавить inline review и safe state copy по spec, role=status/alert, accessible labels/focus. В API-режиме объяснить unsupported real capability. Success/no-op wording описывает внутренний факт, не внешнее подтверждение/refund.
- [ ] RED→GREEN projection cases: success → 404/Held/Confirmed, success → old late GET, success → malformed/error, matched terminal GET → later stale GET, Cancelled → Refunded, Cancelled → Ticketed discrepancy, absolute 30s limit across reopen, token resolves after deadline. Command overlay сохраняется после deadline; unknown не становится command success по stale GET.
- [ ] Проверить direct URL/invalid GUID, session expiry before/after dispatch, same-owner reauth, reload. Не отправлять writes автоматически после login. Run full web unit чтобы проверить B1–B4 regression.

## Task 5: patch B4 без потери позиции

**Files:** Modify `flight-orders-feed.service.ts` и `.spec.ts`, `flight-orders-page.component.ts` и `.spec.ts`; HTML/SCSS только если нужен truthful label/poll message или focus fallback.

**Interfaces:** `patchKnownOutcome(ownerUserId: string, order: FlightOrderResponse): void`. Patch owner-checked, same-ID only; no snapshot creation/insertion. Page применяет `operations.overlay` ко всем входным DTO. Position clear и command overlay clear не связываются.

- [ ] RED: patch меняет только один existing DTO и не трогает paging/position; unknown/direct URL/foreign-owner patch no-op. old DTO из first/append/buffer/poll не откатывает status. Explicit refresh очищает позицию, но не known command evidence.

```typescript
const before = feed.restore(owner)!;
feed.patchKnownOutcome(owner, cancelled);
const after = feed.restore(owner)!;
expect(after.items.find(x => x.aggregateId === id)?.status).toBe('Cancelled');
expect(after.anchorId).toBe(before.anchorId);
expect(after.anchorTop).toBe(before.anchorTop);
expect(after.focusId).toBe(before.focusId);
expect(after.nextOffset).toBe(before.nextOffset);
expect(after.hasMore).toBe(before.hasMore);
```

- [ ] Implement clone-safe same-ID replacement. Сохранить order index и existing snapshot coordinates. Restore применяет overlay перед render; page incoming data тоже merge through overlay. Anchor fallback scrollY, missing link fallback focus heading. Не refetch list first-page и не сортировать после cancellation.
- [ ] GREEN full feed/list unit, затем Browser Back и explicit-link proofs Task 6; сохранить existing auto-load/buffer/keyboard semantics.

## Task 6: offline demo, E2E и документированная верификация

**Files:** Modify `tools/demo/flights-search-api.mjs` и `.test.mjs`, `tests/travel-e2e/demo/flights-orders.spec.ts`, `tests/fixtures/flights-booking.json` если нужен отдельный fictional terminal example; Create `tests/travel-e2e/demo/flights-cancellation.spec.ts`; Modify README только current booking limitations/B5; Create `docs/superpowers/results/2026-10-01-flights-m1-b5-own-order-cancellation-local.md`. Существующий CI/config не менять; suite уже запускает demo testDir одним worker.

**Interfaces:** `createDemoServer(options = {})` может принимать test-only scenario factory hooks; default CLI остаётся loopback 5100, без новых URL controls/auth mechanism. Server принимает cancel с empty body и валидным key, сохраняет exact request digest/replay/in-flight в памяти. Confirm replay и transition guards sufficient для uncertainty tests; existing hold/search fixtures остаются.

- [ ] RED Node: bodyless cancellation Held/Confirmed success, Ticketed rejected, Cancelled/Refunded no-op timestamps stable, same-key replay/no repeated mutation, body conflict/InFlight, malformed key, confirm retry после terminal, credentials rejected, process restart clears state. Обновить старый тест «cancel unimplemented»: сохранить NL/неподдержанные routes rejection, cancel теперь отдельный contract.
- [ ] Implement fictional cancellation/projection scenarios; no refund/ticket creation on cancel, no token/PII log. `readJson` не применять к bodyless cancel; hash actual bytes и path. Test-only scenario управляет delay отдельно от command state, не меняет default confirm progression.
- [ ] Browser E2E seed своих fictional orders в каждом case, внешний network allowlist как у B4. Cases consent/no-op/double click, lost success response → same-key retry, empty/malformed 2xx unknown, InFlight/conflict/rejected, B2/B3 unknown-confirm barrier, departure/re-entry, expired demo auth/read denial через interception; где demo не имеет JWT owner, не называть это owner proof.
- [ ] Browser test с 45+ seeded rows/loaded 40: cancel выбранный row и проверить updated status, loaded length/order, focus link, anchor ±4px на Back и явной ссылке; mobile 360px, viewport resize, stale list append/refresh не откатывает. Direct reload/auth и absence storage/token/PII tested. Не полагаться на seed предыдущего case.
- [ ] Запустить безопасные gates (перед новыми .NET классами audit fixture):

```powershell
npx.cmd nx test web --skipNxCache --watch=false
npx.cmd nx test api-client --skipNxCache
npm.cmd run test:flights-demo
npx.cmd nx build web --configuration=production --skipNxCache
npx.cmd nx build web --configuration=flights-demo --skipNxCache
dotnet build Travel.slnx --configuration Release --maxcpucount:1
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit/Travel.Modules.Flights.Tests.Unit.csproj --configuration Release --no-build
dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --configuration Release --no-build --filter 'FullyQualifiedName~FlightsEndpointsHttpTests'
dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration/Travel.Modules.Flights.Tests.Integration.csproj --configuration Release --no-build --filter 'FullyQualifiedName~DuffelFlightBookingProviderTests'
dotnet test tests/Travel.Tests.Architecture/Travel.Tests.Architecture.csproj --configuration Release --no-build
dotnet test tests/Travel.Tests.Contract/Travel.Tests.Contract.csproj --configuration Release --no-build
npx.cmd nx lint web
npx.cmd nx lint api-client
npx.cmd biome ci .
dotnet csharpier check .
npm.cmd run check:ai-harness
npm.cmd run check:dotnet-inventory
npm.cmd run check:readme-examples
git diff --check
```

Не запускать aggregate solution test, DB handlers/Host/Aspire locally. Build не является запуском schema initialization. Source/CI-only handler assertions перечислить в local result с точным пределом, затем добавить результат CI. Отдельно посмотреть desktop/mobile demo screenshots, доступность кнопок клавиатурой и тексты unknown/refund limitation.

## Task 7: independent review и полный разрешённый цикл закрытия

**Interfaces:** independent reviewer получает spec, exact base/HEAD, diff, test evidence и границы запретов; reviewer read-only, отдельный агент по пользовательскому требованию независимого review. Правки делает implementer, повторяет только связанные tests.

- [ ] Перед review self-check against acceptance 1–8. Независимый review: provider truth, owner-before-no-op, operation replay/epoch, confirm/cancel races, projection overlay, feed anchor и no schema/paid/config changes. Все конкретные blocking findings исправить, показать regression evidence; неизвестную проблему объяснить и согласовать изменённое решение, не скрывать.
- [ ] В пределах согласованного пользователем цикла stage только собственные files, commit; проверить fresh dev/base ancestry, собственный diff и чистоту worktree. Push без force; создать PR в `dev` с truthful scope (demo B5 + disabled incomplete Duffel cancel) и validation limitations. Обязательно attach PR artifact. Review-link для пользователя: `/pull/<number>/changes`.
- [ ] Дождаться mandatory CI именно текущего PR HEAD; paid lane intentional skipped. Требуются существующие DB handler/projection/outbox/real wiring и прочие mandatory lanes, не только focused local tests. Не включать `run_paid_ai_evals`, не менять checks/config и не обходить failing gate. Unexpected failure — diagnosis/report, правка только в scope или пересогласование.
- [ ] После green checks и review merge PR в dev в рамках уже согласованного полного цикла. Verify PR merged/merge SHA, update local clean dev через fetch + fast-forward; при чужих правках остановить переключение и сохранить их. Record final source/CI/merge evidence. Post-merge CI проверить, если workflow запускается; PR не оставлять открытым как завершённую работу.
- [ ] Сохранить нужные result screenshots вне временного worktree, проверить точные пути своих артефактов. Archive managed worktree app tool, удалить только свою merged branch/local temp и remote branch если не auto-deleted. Не убирать чужие worktree/files/jobs. Verify local dev HEAD совпадает с fetched dev, status чистый, PR merged и свои active worktree/ветки убраны.

## Самопроверка покрытия

Spec acceptance 1 → Task 1 + real CI; 2 → Tasks 3–4/6; 3 → Tasks 2–3/6; 4 → Tasks 3–4/6; 5 → Tasks 3–5/6; 6 → Tasks 5–6; 7 → Tasks 2/4/6; 8 → Tasks 0/6/7. Все пять Review Focus conditions имеют named regressions в owning tasks. Operation → feed DI односторонний; decoder/API/service используют одинаковые cancellation types; confirmed result не подменён полным cancel DTO. Forbidden local schema tests вынесены в обязательный CI, без изменения pipeline.

План разрешает native implementation после подтверждения варианта A и самого плана, с независимым review в конце. Если пользователь выбирает B, этот план не исполняется: сначала пересогласовать provider/refund/recovery объём.
