# Flights M1 B5: локальное доказательство варианта A

**Объём:** согласован 2026-10-02 ответом «a» на совместный запрос согласования варианта A, спецификации, плана и полного цикла до merge/уборки. Это локальный отчёт перед публикацией; remote CI и merge проверяются отдельно на опубликованном HEAD.

**База:** свежий fetch 2026-10-02 дал `origin/dev = 0a54e53d46107f262614248b73b1af3e79be0552`, checkpoint ancestry подтверждена. Managed worktree `C:\Users\Vladimir_sva\.codex\worktrees\flights-b5-cancel\travel-agency`; ветка `codex/flights-b5-own-order-cancellation` создана непосредственно от fetched SHA. Архив B4 не восстанавливался; два design документа перенесены после сравнения хешей, основной dev оставлен чистым.

## Поведение

- B3 требует отдельного согласия на отмену Held/Confirmed, переводит фокус после render, закрывает review при смене owner/order/effective status. Повторный клик синхронно блокируется. Ticketed отменять нельзя.
- Flights root memory service хранит confirm/cancel попытки без токенов и passenger body. Оба confirm входа B2/B3 используют эту память. Уход из компонента не удаляет pending/unknown и не считается отменой серверной команды. Ручной retry сохраняет UUID, canonical method/path и точные body bytes; cancel body пустой.
- Unknown, InFlight, payload/concurrency conflict и новый неизвестный 409 не разрешают replacement key. Known rejection определяется allowlist кода и HTTP status, после него требуется валидный owner GET перед новым consent. 24-часовой предел replay не превращается в разрешение нового ambiguous запроса.
- View, feed и command memory привязаны к owner и identity epoch. Logout инвалидирует синхронно; GET/POST 401/403 очищают видимые/приватные snapshots и синхронно quarantines minimal exact attempt. Даже coalesced same-owner auth recovery не принимает старый POST. Late Keycloak factory/refresh после logout не возвращает authentication.
- Известный cancel snapshot имеет приоритет над early 404/Held/Confirmed. Наблюдённый Refunded сохраняет продвижение terminal evidence. Receipt конкретной команды обрабатывается один раз; unrelated revision не перезапускает ожидание. Ticketed после известного cancel показывает противоречие. Polling имеет абсолютный предел 30 секунд и manual refresh.
- B4 patch обновляет только существующий ID и сохраняет index, другие rows, paging, anchor geometry и focus. Browser Back и явная ссылка восстанавливают loaded 40 rows, selected anchor с отклонением ≤4 px и focus. Background command после возврата тоже обновляет active feed; stale refresh/append не откатывает status.
- Handler больше не пишет OrderCancelled/outbox/notification после provider error или при отсутствии ProviderOrderId. Owner проверяется до provider и terminal no-op; Cancelled/Refunded no-op не вызывает provider/новые события.
- **Реальная Duffel cancellation сознательно недоступна:** 409 `Flights.ProviderCancellationNotSupported` до HTTP. Create cancellation quote не выдаётся за подтверждённую отмену. Demo Node самостоятельно моделирует fictional cancellation/replay/InFlight/state guards и не является production provider.
- Cancellation email RU/EN больше не обещает возврат за 5–10 дней. Отмена не запускает refund; wording описывает внутренний факт. GET/cancel/replay имеют no-store. Reload/direct URL требуют входа и чтения, без автоматической записи и восстановления ключа через storage/URL.

## Проверки и границы

| Gate | Локальное доказательство |
| --- | --- |
| Web unit | 227/227: consent/double click, both confirm paths, unknown/retry/conflict, denial privacy, epoch, late auth creation, projection deadline/Refunded, feed patch/background/restore |
| API client | 45/45: dedicated terminal cancel decoder, same-ID timestamps, canonical bodyless request and replay bytes |
| Flights unit | 483/483 Release, включая public-handler email test RU/EN с fake notification boundaries |
| WireMock booking provider | 18/18 Release; unsupported cancel делает 0 HTTP calls; реальные поставщики не вызываются |
| No-DB HTTP | 29/29 Release: ASP.NET TestServer с fake bus/store; owner mapping, safe rejected ProblemDetails, opt-in replay и no-store. Это не DB-store/handler proof |
| Architecture / contract | 167/167 и 9/9 Release |
| Demo | Node 24/24 и Chromium 11/11; loopback/proxy, вымышленные данные, loss/replay, selected row/anchor/focus, desktop/mobile и reload. JWT ownership не доказывается demo auth |
| Builds | .NET Release solution; Angular production и flights-demo SSR/prerender builds |
| Source gates | CSharpier, ESLint web/api-client, Biome без error-level diagnostics, diff check; harness 90/90, inventory 62 pass/1 intentional skip, README catalog 11/11 |

Существующие AD0001/Verify discovery/Node color/mock-timer и informational/non-null diagnostics не выдаются за новые failures; checks не отключались. Для нового worktree установлен требуемый Chromium: первая попытка E2E не дошла до кейсов из-за отсутствующего binary, затем весь suite прошёл.

**CI-only:** новые real Marten handler tests для Held/Confirmed provider failure, missing provider order, Held success, owner-before-Cancelled/Refunded-no-op, unchanged stream/outbox; existing EF rendering, store, real wiring, projection/commit/Host/Aspire lanes. Их fixtures применяют EnsureCreated/AutoCreate/Migrate, поэтому локально не запускались. Обязательный CI должен пройти до merge; focused mocks его не заменяют.

Ни Host/AppHost, ни local schema/migrations/deploy, ни реальные supplier/payment/Anthropic API не запускались. Paid AI-evals не включались, CI/CD и lockfile не менялись. Внешние письма не отправлялись.

## Independent review и устранение

Отдельный read-only reviewer проверил весь diff и новые untracked source/tests. Два Important: сохранённый command DTO после GET denial и typed unknown 409, разрешающий новый key. Оба воспроизведены до исправления. Три первоначально Minor повышены исполнителем до обязательных: invalidated consent при Held → Confirmed, ложное повторное updating после Refunded/unrelated operation, late auth factory после logout. По влиянию это нарушения согласованного поведения либо auth lifetime.

Один fix pass добавил allowlist rejected/status, synchronous denial quarantine, consent status binding, per-command receipt и factory epoch guard. Все reproductions стали GREEN, полный web/browser suites прошли после правок. Повторного независимого review не подменяли своим мнением: closure подтверждён регрессиями и полной проверкой. Конкретных оставленных замечаний нет.

## Решения исполнителя

1. Internal email helper недоступен unit assembly: тестирует public handler через fake boundaries; production visibility не расширяется. Цена ошибки — пропуск текста на renderer boundary, покрываемом CI.
2. B2 confirm tests ожидают async token gate; старый polling test ожидает отсутствие GET после absolute deadline. При восстановлении доступа fixture явно возвращает authenticated state, как делает реальный token gate. Это уточнение ожидаемого поведения, не обход failure.
3. Demo fixture переиспользовал один confirm UUID для разных DTO: независимый scenario получил отдельный key. Цена ошибки — потеря cross-scenario isolation; exact changed-request conflict остаётся отдельной проверкой.
4. PRODUCT/DESIGN отсутствуют: следуем согласованному B3/B4 стилю, не добавляем unrelated design/config files. Цена ошибки — визуальные различия, проверенные desktop/mobile screenshots.
5. Три review замечания исправлены как обязательные по фактическому эффекту. Цена ошибки этой оценки — дополнительная узкая реализация/tests; scope не расширен на provider/refund/durable workflow.
6. No-DB HTTP store остаётся no-op по умолчанию; конкретный replay response задаётся opt-in для middleware contract. Реальная atomic reservation/replay семантика проверяется обязательным DB CI.

## Ограничения доказательства

Вариант A не доказывает и не включает real supplier cancellation/refund, coordination между вкладками/устройствами, durable recovery после reload, deployed issuer или deployed projection convergence. Legacy/system Cancelled остаётся внутренним статусом без provenance внешней отмены. Offset feed не является стабильным snapshot при конкурентных вставках. Токен и operation key теряются при полном reload/redirect, эта граница показана человеку.

Снимки B5: `flights-b5-cancel-desktop.png` и `flights-b5-feed-mobile.png` просмотрены; перед уборкой сохраняются в разрешённой папке Codex visualizations вне временного worktree. Полный разрешённый цикл требует commit/push/PR, green mandatory CI, merge, local dev sync и удаления только своих temporary files/worktree/веток.
