# Flights M1 B5: отмена своего заказа — проект спецификации

**Статус:** вариант A и план согласованы пользователем 2026-10-02 ответом «a» на совместный запрос согласования. Разрешён полный цикл до merge в dev и уборки; реализация и проверка ведутся в managed worktree. Ограничение реальной отмены — согласованная часть решения.

**База реализации:** повторный fetch 2026-10-02 подтвердил `origin/dev = 0a54e53d46107f262614248b73b1af3e79be0552` и checkpoint ancestry. Managed worktree `C:\Users\Vladimir_sva\.codex\worktrees\flights-b5-cancel\travel-agency`, ветка `codex/flights-b5-own-order-cancellation` создана непосредственно от fetched SHA. Историческая запись исследования ниже сохраняется.

**База исследования:** `git fetch origin dev` выполнен 2026-10-01. `origin/dev` и `FETCH_HEAD`: `0a54e53d46107f262614248b73b1af3e79be0552`. После fetch HEAD локального `dev` совпал с этой базой; `git merge-base --is-ancestor 0a54e53d46107f262614248b73b1af3e79be0552 origin/dev` и `git merge-base --is-ancestor origin/dev HEAD` вернули 0. Совпадение checkpoint установлено после fetch, не использовано вместо него. Checkout был чистым. Worktree/ветка не создавались, архив B4 не восстанавливался. Перед реализацией требуется новый fetch и отдельный managed worktree от его точного SHA.

**Историческая CI-проверка:** [run 36875553928](https://github.com/svasorcery/travel-agency/actions/runs/36875553928) прочитан через GitHub CLI: `event=push`, `status=completed`, `conclusion=success`, `headSha=0a54e53d46107f262614248b73b1af3e79be0552`. Это доказательство B4, не B5.

## Цель и границы

Пользователь со страницы B3 `/flights/orders/:aggregateId` явно подтверждает отмену только своего Held/Confirmed заказа. После известного успешного результата детальная страница и уже загруженная лента B4 сохраняют актуальное внутреннее состояние, даже если EF-проекция ещё отстаёт. Возврат через Browser Back или «К списку заказов» сохраняет выбранную карточку на прежней высоте viewport и фокус на ссылке.

Бесплатное доказательство строится на вымышленных данных, offline fixtures и изолированной demo-сборке. Реальных поставщиков, платежей, Anthropic и иных платных API не вызываем. Токены и клиентское состояние операций — только память вкладки. PII не записывается в URL, browser storage, history.state или логи. Backend JWT/owner/scope не ослабляются. Fake auth остаётся только file replacement `flights-demo` и test fixtures. Не выполняем локальные миграции, schema apply, запуск Host/AppHost или деплой; CI/CD-конфигурацию не меняем. Paid AI-evals пропускаем, `run_paid_ai_evals` не включаем.

Возврат денег клиенту, сумма/срок/гарантия возврата, отмена Ticketed, новый payment flow и полноценное управление реальными заказами вне B5. Статус Refunded можно показать как существующий факт; cancel не создаёт возврат и не вызывает `IPaymentGateway.RefundAsync`.

## Проверенные исходники и фактический контракт

| Область | Проверенное поведение |
| --- | --- |
| HTTP | `CancelOrderEndpoint.Post`: `POST /api/flights/orders/{aggregateId:guid}/cancel`, `[Authorize("flights:book")]`. DTO тела нет; owner извлекается из claims, затем `CancelOrderCommand(aggregateId, userId)`. Ответ 200 — полный `OrderResponse` из command snapshot, без follow-up query. |
| Auth | Host: authentication → authorization → Flights idempotency middleware. JWT audience конфигурируется Identity, default `travel-web`, `MapInboundClaims=false`; scope нормализуется из `scope/scp`. `TryGetUserId` требует один непротиворечивый непустой GUID. Web дополнительно проверяет `aud=travel-web`, GUID `sub`, `flights:book`. GET/list требуют `[Authorize]`; cancel требует booking scope. Live issuer не проверялся. |
| Owner | Handler вызывает `DecideOwner(Cancel, userId)` до state response/no-op/provider. Нет stream, owner отсутствует или чужой → `Flights.OfferNotFound`/404. Чужому пользователю не раскрывается статус, провайдер не вызывается. |
| Domain | `BookingAggregate.DecideCancel`: Held/Confirmed Allowed; Ticketed → `Flights.OrderNotCancellable`/409; Cancelled/Refunded IdempotentNoOp, возвращающий текущий статус; None/OfferQuoted rejected. TTL hold не используется для разрешения отмены. |
| Provider | `CancelOrderHandler` сейчас игнорирует ErrorOr-ошибку провайдера, логирует предупреждение и продолжает append `OrderCancelled`. `DuffelFlightBookingProvider.CancelOrderAsync` делает только create `/air/order_cancellations`, не проверяет body и возвращает Success на любой HTTP 2xx. |
| Commit | Сначала provider side effect, затем optimistic Marten commit. `OrderCancelled`, durable reconciliation и notification сохраняются через enrolled outbox; конфликт сохранения → `Flights.ConcurrencyConflict`/409. HTTP snapshot применяется локально после commit; EF запись eventual. Outbox не делает внешнюю операцию атомарной с Marten. |
| Idempotency | UUID header нормализуется в N; lookup связан с key/user/path, hash содержит method/path/body bytes. Started/Replayed/InFlight/BodyConflict. Только 2xx сохраняются на 24 часа; прочие ответы и исключения abandon. Ответ копируется клиенту перед Complete. Crash/disconnect/ошибка Complete могут потерять replay после commit. Внешний cancel не получает stable provider key. |
| Projection | GET/list фильтруют owner; список сортируется `BookedAt DESC, AggregateId DESC`, B4 запрашивает limit 21 и добавляет 20. EF имеет `ProjectedStreamVersion`, публичный DTO версии не отдаёт. Не обещаем snapshot consistency или доказательство завершения операции по старому GET. |
| B3 | Component-local confirm attempt и key; уход/смена route сбрасывают их. Command Confirmed защищён от early 404/Held только пока жив соответствующий view. Cancel client/action отсутствует. В demo B3 `sameOwner` сейчас всегда true: для новых write guards это недостаточно. |
| B2 | Booking panel тоже держит confirm attempt локально и теряет его на destroy. Это второй вход в confirm; подключить к общей памяти только confirm, не переносить passenger/hold body в новую cancellation-службу. |
| B4 | Root feed service хранит owner-bound snapshot, offset/hasMore, anchorId/anchorTop/scrollY/focusId. Restore после проверки accessToken не делает first-page GET. Сейчас нет patch по результату команды; append может заменить существующий DTO старой проекцией. |
| Demo | Loopback Node API, память процесса, rejects любой Authorization header. Cancel route отсутствует. UUID для confirm проверяется, но replay/in-flight не реализованы; повтор confirm сбрасывает progression. GET после Confirmed имитирует 404, Held, затем Ticketed. Owner/JWT не моделируются. |
| Email | `SendOrderCancellationEmailHandler.ResolveLocalisedCancelTexts` безусловно обещает возврат за 5–10 рабочих дней в RU/EN. Это неподтверждённое обещание независимо от provider cancellation и должно быть удалено в B5. |

Источники: `modules/flights/AGENTS.md`, `modules/identity/AGENTS.md`, `shared/AGENTS.md`; `Api/Endpoints/{CancelOrderEndpoint,GetOrderEndpoint,ListOrdersEndpoint}.cs`; `Api/Middleware/IdempotencyKeyMiddleware.cs`; `Application/Handlers/Booking/{CancelOrderHandler,ConfirmOrderHandler}.cs`; `Core/Aggregates/BookingAggregate.cs`; `Core/Providers/IFlightBookingProvider.cs`; `Application/Persistence/DocumentSessionExtensions.cs`; `Infrastructure/Providers/Duffel/DuffelFlightBookingProvider.cs`; `Infrastructure/Persistence/{OrderReadModelQueries,OrderReadModelReconciler,Repositories/IdempotencyStore}.cs`; `shared/dotnet/Travel.Shared.Web/{ClaimsPrincipalExtensions,ErrorOrExtensions}.cs`; Identity authentication sources; `apps/Travel.Host/Program.cs`; B2/B3/B4, auth and handoff components/services; `shared/ts/api-client/src/flights-booking.*`; `tools/demo/flights-search-api.mjs`.

Существующие тесты: `CancelOrderHandlerTests` покрывают Confirmed success, ownerless/foreign rejection, Ticketed rejection, Cancelled no-op, но не запрет commit после provider failure. `DuffelFlightBookingProviderTests.CancelOrderAsync_HappyPath_ReturnsSuccess` закрепляет ошибочное create-only success. `FlightsEndpointsHttpTests.Cancel_builds_response_from_command_snapshot_without_follow_up_query` проверяет форму HTTP/owner mapping с fake bus. Domain transition unit, B3 unknown-confirm/owner/polling tests, B4 anchor/keyboard/demo tests полезны как regression; B5 acceptance ими не доказана.

Дополнительный idempotency gap: `IdempotencyKeyConfig` имеет primary key только по `Key`, хотя lookup фильтрует ещё user/route. Повтор того же UUID другим owner/path не означает независимый namespace и может попасть в InFlight при insert collision. В A генерировать случайный UUID v4 для каждой новой операции и никогда не переиспользовать key другой операции; same-key retry строго привязан к исходным owner/path. Схему и middleware namespace в B5 не менять, её исправление требует отдельного решения. Cancel email templates содержат `RefundText`; поменять его источник на «Отмена заказа не подтверждает возврат средств» / «Order cancellation does not confirm a refund», без срока или обещания денежной операции.

По [официальному Duffel API](https://duffel.com/docs/api/order-cancellations) создание cancellation не завершает отмену: нужен отдельный confirm, а `confirmed_at` указывает подтверждение. Этот шаг связан и с provider-side refund. Поэтому добавление confirm-call нельзя молча считать чистой UI-правкой без денежных последствий.

## Варианты и рекомендуемое решение

| Вариант | Результат, цена и ограничение |
| --- | --- |
| **A. Demo B5 + строгий backend safety gate — рекомендуется** | Полный B5 UI, same-key retry, memory confirm/cancel coordination, B4 patch/return и бесплатное доказательство. Handler прекращает отмену при любой provider error. Текущая Duffel cancellation явно возвращает `Flights.ProviderCancellationNotSupported`/409 **до сетевого обращения**, вместо ложного create-only success. Production/provider cancellation не объявляется работающей. Нет новых схем/денежных действий. |
| B. Полноценная provider cancellation + recovery | Расширить capability outcome, проверять create/confirm/status, сохранять operation identity и cancellation ID, reconcile внешний успех после commit failure, сериализовать confirm/cancel между клиентами, классифицировать definitive rejection и unknown. Нужны отдельные решения о provider-side refund, durable operation storage/схеме и recovery; offline тесты не заменят live acceptance. Не укладывается в выбранные границы B5 без отдельного согласования. |
| C. Только UI поверх текущего handler | Меньше изменений, но выдаёт ложный успех при provider error/create-only. Противоречит требованию; не рекомендуется и не предлагается к реализации. |

Для A реальный backend сможет отменить заказ только через capability, чей Success означает окончательно подтверждённую отмену. В текущей композиции единственная реальная capability — Duffel — её пока не обеспечивает, поэтому нормальный Held/Confirmed с provider order получит понятный rejected. Demo Node API не является подменным production provider. Не добавляем fake booking provider в Host и не включаем fake auth вне demo.

Это сознательное изменение текущего create-only backend поведения: оно перестаёт возвращать ложный успех. Если требуется работающая отмена Duffel уже в B5, выбрать B и пересогласовать спецификацию до реализации.

## Backend и HTTP решения варианта A

1. Owner и domain decisions остаются первыми, включая проверку owner до idempotent no-op. Ticketed продолжает отклоняться. Cancelled/Refunded возвращают текущий snapshot без нового event/provider/notification.
2. Для Allowed требуется непустой ProviderOrderId; отсутствие → новый `Flights.ProviderOrderMissing`/409, без append и сообщения. Ошибка/exception провайдера не превращается в Cancelled; ожидаемые ошибки возвращаются, неопределённое исключение остаётся неуспехом. Success порта документируется как terminal cancellation, не «принят запрос».
3. Duffel метод варианта A возвращает `Flights.ProviderCancellationNotSupported`/409 без HTTP. Удалить raw-body логирование из этого cancel path; существующую unrelated payment logging remediation не включать.
4. На известный Success capability commit сохраняет прежнюю атомарность event/reconcile/notification и command snapshot. Не добавлять refund, новые domain states, таблицы, миграции, общий operation engine или изменение idempotency TTL/middleware.
5. На cancel и single-order GET добавить `Cache-Control: no-store`, включая replay cancel через targeted middleware path. List уже имеет этот header. Body клиента cancel — **ноль байтов** (`null` HttpClient body), не `{}`; path GUID канонизируется один раз, query отсутствует. Retry повторяет тот же method/path/body/key; Authorization можно обновить только для того же owner/session.
6. Client decoder принимает только валидный полный OrderResponse того же aggregateId и status Cancelled/Refunded. Для Cancelled обязателен валидный ненулевой cancelledAt, для Refunded — refundedAt. 204, пустой/null body, malformed JSON, несовпадающий ID, Held/Confirmed/Ticketed в 2xx → unknown, без patch. Это отдельный cancel decoder, не глобальная правка legacy GET decoder.
7. Cancelled/Refunded no-op отражает внутреннее состояние. Legacy `OrderCancelled` мог возникнуть из старого best-effort cancel либо compensation confirm; он не является доказательством внешней отмены. Ни HTTP 200 no-op, ни GET terminal нельзя описывать как подтверждение поставщика. Новых полей external-confirmation не добавляем.
8. Убрать безусловное обещание refund из RU/EN cancellation email model, сохранив шаблоны/доставку и readiness version gate. Покрыть текст локальным fake-sender/renderer test; письма реальным адресатам не отправлять. Источник денежных фактов не добавлять.

## UX и state machine

Кнопка «Отменить заказ» доступна на owner-checked B3 при ready Held/Confirmed, без projection lag и незавершённой confirm/cancel. Ticketed: «Отмена выписанного билета здесь недоступна». Cancelled/Refunded не предлагают новое действие. В demo вход требуется и для direct B3; убрать unconditional demo owner bypass для writes. Provider limitation описывается явно в API-режиме, а его ответ показывается как недоступная возможность.

Первый клик открывает inline confirmation section: «Отменить этот заказ?», маршрут и безопасный идентификатор заказа; «Отмена не оформляет возврат денег. Отмена выписанных билетов здесь недоступна». Кнопки «Оставить заказ» и «Да, отменить заказ». До второй кнопки POST/key отсутствуют. Фокус переводится на «Оставить заказ», Escape закрывает, закрытие возвращает фокус на trigger. Любое изменение owner/order/effective status закрывает подтверждение. При второй кнопке guard синхронно фиксирует attempt и блокирует обе записи до await token; double click создаёт ровно один POST.

| Состояние | Поведение |
| --- | --- |
| idle/review | Никакой записи; review закрывается при auth/status/route change. |
| pending | Одна попытка, кнопки confirm/cancel/retry disabled. «Отменяем заказ…». |
| success | Только после decode известного 200: «Демо-заказ отменён» в demo; «Внутреннее состояние заказа: отменён» в API. DTO сохраняется в памяти, B4 patch немедленно. Refunded: «Заказ уже в состоянии возврата» без обещания нового refund. |
| rejected | Первичная известная pre-effect ошибка: unsupported capability, missing provider order, domain invalid/Ticketed, validated command/key error, owner 404. Не показывать успех; требовать свежий owner GET перед новым review. 404: «Заказ не найден или недоступен». |
| conflict | InFlight 409 сохраняет exact attempt; ручной retry тем же ключом. IdempotencyConflict → blocked integrity conflict, никакого нового key. ConcurrencyConflict/неизвестный 409 → unresolved conflict: GET для информации, никаких новых write attempts; внешний эффект не считается исключённым. |
| unknown | Transport/status 0/timeout, 5xx, malformed/empty 2xx или прекращение ожидания отправленного запроса: результат неизвестен. Сохранять attempt; только ручной same-key retry или GET. Никакого оптимистического Cancelled и автоматического POST retry. |
| auth required | Ошибка токена до первой отправки означает «не отправлено», а не unknown. После отправки 401/403/ошибка токена не стирает прежнюю неизвестность; приватный view скрывается, late responses отвергаются. |

Ошибки показывать по allowlist кода ProblemDetails и собственной безопасной строке; сырой detail/provider body/PII не выводить и не логировать. Не любой 409 означает retryable InFlight. Не любой 4xx после неизвестного прошлого запроса доказывает, что прошлый запрос не прошёл: rejected response относится к текущему retry, предыдущий unknown barrier остаётся до известного command success или проверки, допускающей только чтение.

## Память операции, confirm и жизненный цикл

Добавить Flights-specific root memory service для confirm/cancel. Entry содержит ownerUserId, identity epoch, aggregateId, kind, key, immutable exact request, dispatched/hadUnknown, state и безопасный command snapshot; не содержит токен, passenger/hold body или provider payload. Ключ создаётся только на финальном consent/new confirm и не меняется при retry. Это координация вкладки, не durable workflow.

- Подключить оба существующих confirm entrypoints B2/B3. Пока confirm pending/unknown/InFlight/unresolved conflict, cancel запрещён независимо от stale Held GET, route destruction или возврата через список. Пока cancel unresolved, confirm запрещён. Не менять hold flow и payment business rules.
- Root service владеет ожиданием POST с существующим timeout 15 секунд. Уход со страницы отменяет GET/poll и view subscription, но **не трактуется как отмена серверного POST**. POST может завершиться в службе и обновить память/feed для того же owner epoch. При повторном открытии B3 показывает pending/unknown/success из этой памяти. Завершившийся component не пишет DOM и не emits navigation.
- При refresh токена с тем же owner операция сохраняется. При auth error после dispatch entry quarantined: приватные DTO/feed скрыты/очищены, late results прежней epoch не применяются; хранится только identity/key/exact non-PII request и неопределённость. Повторный вход того же owner без полной перезагрузки позволяет ручной retry прежнего key. Другая identity, explicit logout и destroy root уничтожают entry/overlays; новые поздние ответы не должны воскреснуть даже при A → B → A. Для logout нужны синхронная invalidation и очистка auth status до ожидания external redirect, узко покрытые auth regression.
- При initial expiry без отправленного запроса нет ambiguous attempt. Login только по пользовательскому действию. Полный OIDC redirect/reload уничтожает память и ключ: невозможность same-key retry честно отражается как ограничение, без записи ключа в URL/storage.
- Через 24 часа от первой dispatch клиента автоматическое разрешение повторять старый key прекращается: backend replay истекает. Показать требование проверки, не создавать replacement key для unknown. Это консервативная граница, не серверный operation-status контракт.
- Reload/direct URL/new tab: токен/attempt/feed не восстанавливаются; сначала вход, затем GET. Никакого автоматического cancel/confirm. Cancelled/Refunded/Ticketed — отображение проекции, без заявления о provider result. Held/Confirmed допускают новый explicit review с заметкой «После перезагрузки исход прежних действий здесь неизвестен; сначала проверьте заказ, если действие уже отправлялось». Старый GET не доказывает отсутствие внешней операции. Нельзя гарантировать координацию между вкладками/устройствами или после reload без варианта B.

## Command outcome и задержка проекции

Известный cancel snapshot имеет приоритет над EF Held/Confirmed и early 404 в B3 и B4. Overlay привязан к owner/epoch/aggregateId и держится до logout/owner switch/reload; истечение polling window **не снимает его**. Даже после первого согласованного GET поздний старый ответ не откатывает статус. Если после известного Cancelled GET возвращает Ticketed, показать discrepancy и оставить известный исход с запретом действий; это не нормальное «более новое» состояние. Refunded может продвинуть отображение вперёд, но не обратно.

B3 polling: каждые 2 секунды, абсолютный предел 30 секунд от command success, не от каждого re-entry. Deadline прекращает in-flight GET и повторно проверяется после await token. Уход/auth change останавливают polling. После предела: «Отмена принята системой; отображение проекции ещё обновляется», ручной GET доступен. GET error/malformed не уничтожает известный snapshot; auth denial скрывает его. Unknown cancel не получает success overlay только из старого Held/Confirmed; terminal GET показывается как наблюдение внутреннего статуса, без присвоения command success или доказательства exact request.

Публичной версии проекции нет; A использует узкие правила сохранения terminal command outcome. Общую сортировку статусов в числовой rank не вводить: Confirmed/Ticketed/Cancelled не линейная цепочка.

## Обновление B4 и место возврата

`FlightOrdersFeedService.patchKnownOutcome(ownerUserId, order)` заменяет только уже существующий элемент по case-insensitive aggregateId в owner-bound snapshot. Сохраняются index, BookedAt ordering, все остальные items, nextOffset, hasMore, anchorId/anchorTop/scrollY/focusId. Не вставлять direct-link заказ в неизвестную позицию ленты, не удалять отменённую карточку, не refetch первую страницу при возврате.

Корневая memory service также хранит terminal overlay; B4 применяет его на restore, first-page refresh, auto/manual append, buffered append и recent-hold poll. Повторное DTO старого Held/Confirmed не заменяет известный Cancelled. Patch snapshot и overlay — разные обязанности: даже очистка position snapshot при explicit refresh не теряет command evidence. При несовпадении owner обе памяти очищаются/невидимы.

Return использует существующий anchor geometry algorithm; изменение высоты статуса компенсируется anchorTop, фокус восстанавливается на link с preventScroll. Observer не догружает во время restore. Если anchor отсутствует, fallback scrollY; если focus link отсутствует, фокус на heading списка, без запроса восстановления несуществующей записи. После direct URL без snapshot обычная первая загрузка B4 с overlay для найденной карточки; anchor не выдумывается.

## Демо и проверяемое доказательство

Node stub добавляет bodyless cancel, честные Held/Confirmed → Cancelled, Ticketed → 409, Cancelled/Refunded no-op. CancelledAt сохраняется, confirm после Cancelled запрещён, cancel не создаёт билеты/возврат. Response тот же OrderResponse, без PII. Key/method/path/body hash, replay, in-flight и body conflict для cancel моделируются в памяти; confirm получает достаточную replay/state-guard семантику для B2/B3 uncertainty сценариев, не сбрасывает статус на retry. Сеть наружу запрещена browser test и loopback fixtures; bearer по-прежнему rejected.

Детерминированные delay/failure сценарии задаются test-only factory options/HTTP interception, а не production query-параметрами: потерянный успешный ответ, empty/malformed 2xx, InFlight, concurrency conflict, 404/stale projection, initial/retry auth expiry. Confirmed fixture не должен автоматически перейти в Ticketed до cancellation case; управлять этим только тестовой fixture, существующий default progression оставить regression.

Demo не доказывает owner/JWT/реальное внешнее cancellation, платежи или persistence/recovery. Owner acceptance доказывается domain/handler/HTTP tests с двумя различными test identities; no-DB HTTP fixture проверяет pipeline с fake bus, а не реальный Marten handler. Реальные handler/outbox/projection проверки требуют PostgreSQL fixture в CI.

## Критерии приёмки

1. Backend foreign/ownerless cancellation rejected до provider/no-op; Held/Confirmed allowed, Ticketed rejected, Cancelled/Refunded no new events/provider/notifications. Provider error/unsupported/missing order не создаёт OrderCancelled, success response, reconcile или cancellation email. Duffel cancel не делает HTTP и не возвращает create-only Success в A.
2. Review cancel без consent не вызывает POST; двойной click один POST; pending/unknown confirm из B2 и B3 блокируют cancel и переживают route destruction. Pending/unknown cancel блокирует confirm.
3. Same-key retry совпадает method/path/body bytes; token обновляется только для того же owner. InFlight сохраняет key; payload conflict не допускает replacement key; timeout/malformed/empty 2xx не дают успеха. Unknown → rejected retry не снимает прошлую неопределённость.
4. Смена пользователя/explicit logout немедленно скрывают данные и инвалидируют результаты; A → B → A не принимает старый POST. Same-owner token refresh сохраняет entry. Auth expiry до send отличается от expiry после send. Полный redirect/reload честно теряет replay key.
5. Валидный command Cancelled/Refunded немедленно отражается в B3 и loaded B4 row. Early 404/Held/Confirmed, delayed response, refresh, append/buffer/poll и истечение 30 секунд не откатывают terminal overlay. Unknown response не патчит ленту в success.
6. Loaded 40+ rows → открыть выбранный заказ → cancel → Back/явная ссылка: число/порядок rows и paging metadata сохранены, обновлена только нужная карточка, фокус на её link, anchor deviation ≤4 px при том же viewport. На 360px после изменения геометрии восстанавливается anchor, observer не стартует догрузку во время restore.
7. Direct GUID URL, invalid/empty GUID, owner 404, GET error/malformed, 401/403, logout, reload имеют явное безопасное поведение. Нет автоматической записи после reload/login. Ticketed недоступен; нет обещания refund ни в consent, ни в success, ни в cancellation email RU/EN.
8. Нет real API calls, secret/token/PII persistence или новых backend fake-auth путей; demo маркирован и изолирован от production build. Изменения CI/CD отсутствуют, платная lane skipped. Independent review не оставляет конкретных blocking findings, все mandatory PR checks green перед merge.

## Безопасность локальных проверок и дальнейший gate

`FlightsEfInitializer` вызывает MigrateAsync вне Production, `FlightsMartenInitializer` применяет AutoCreate.All, `WolverineMessageStoreInitializer` применяет schema changes. Host/AppHost локально не запускать. Не запускать aggregate solution test локально под этим ограничением. `CancelOrderHandlerTests` создаёт disposable PostgreSQL, Marten AutoCreate.All и EF EnsureCreated: даже без MigrateAsync это schema apply, локально отложить. Полные DB/Host/Aspire lanes остаются обязательными в CI, их не обходить.

`EmailNotificationTests` также применяет EnsureCreated и локально исключён; новый pure unit test текстов письма входит в безопасный local suite, rendering с real EF lookup проверяется существующей CI lane.

Локально после согласования: TypeScript unit, demo Node/Chromium, production/demo build, .NET build, audited unit/architecture/contract suites, no-DB FlightsApiFixture HTTP, WireMock provider tests (loopback, без schema). Перед каждым новым классом fixture заново проверить startup, migrations и outbound clients. Невозможность выполнить проверку сообщать; demo/source проверкой её не заменять.

**Самопроверка:** варианты отделены от выбранной рекомендации; state/response/projection не смешаны; bodyless retry точен; owner/session и оба confirm entrypoints учтены; no-op не заявляет external proof; отсутствие public version и reload/multi-client coordination обозначены; локальные schema-changing tests исключены; refund/Ticketed и CI изменения не включены. Нет implementation или live acceptance claim.

После согласования варианта и плана пользователь разрешает полный цикл implementation → подходящие tests → independent review → commit/push → PR в dev → mandatory green CI → merge → local dev sync → cleanup только собственных worktree/веток/temp. До согласования этот gate закрыт.
