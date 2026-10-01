# Flights M1 B4: локальный результат

**Статус:** согласованный объём B4 завершён; финальные локальные gates зелёные, независимое source review не выявило оставшихся блокеров. Пользователь разрешил commit/push/PR и уборку 2026-10-01. Этот отчёт фиксирует локальное доказательство перед публикацией; remote CI проверяется на опубликованном PR. Локальные миграции и деплой не выполнялись.

**База:** повторный `git fetch origin dev` 2026-10-01 дал `53df375368c1d4fe5b53fe1d3514d2754059e7ed` (merge PR #21). Managed worktree: `C:\Users\Vladimir_sva\.codex\worktrees\flights-b4-own-orders\travel-agency`, branch `codex/flights-b4-own-orders` создана непосредственно от этого SHA; перед публикацией ancestry снова проверена. Коммиты B4 публикуются поверх этой базы.

## Пользовательский результат

- `/flights/orders` показывает только серверную owner-scoped выдачу. Новые заказы сверху; порции по 20, lookahead 21 даёт признак продолжения. Около конца ленты следующая порция загружается автоматически. Кнопка «Показать ещё» доступна постоянно и работает с клавиатуры либо без IntersectionObserver.
- Автоподгрузка сохраняет фокус. Явная кнопка переводит его на первую новую ссылку. Автоматический ответ, пришедший при фокусе на кнопке, ждёт её активации в памяти без второго GET. При ошибке подгрузки предыдущие карточки остаются, retry повторяет тот же offset; 401/403 и смена identity удаляют видимые данные и cache.
- Ссылки открывают существующую страницу B3 через RouterLink. Browser Back и «К списку заказов» восстанавливают загруженную ленту, выбранный заказ на прежней высоте viewport и фокус на его ссылке. Перед показом cache проверяется accessToken; GET первой порции при возврате не нужен. Во время restore observer не загружает новую порцию. При изменении ширины экранов восстановление использует anchor карточки, а не только прежний scrollY.
- Snapshot привязан к owner и хранится только в памяти этой вкладки. Logout/смена пользователя очищают его. Полный reload/новая вкладка начинаются с первой порции и входа; токены, DTO, PII и состояние ленты не записываются в URL/storage/history.state/логи.
- Пустой результат, первая загрузка, ошибка, недоступный вход, malformed DTO и задержка проекции различимы. После известного hold ожидаем появление заказа не более 30 секунд от получения hold: deadline отменяет незавершённый polling GET. Переход к следующим порциям прекращает это ожидание. Синтетического заказа нет; cancel UI не добавлен.
- Backend получает owner из JWT, сортирует `BookedAt DESC, AggregateId DESC`, возвращает `Cache-Control: no-store`. List client проверяет всю страницу и отражённые параметры; повреждённый элемент отклоняет страницу целиком.

## Проверки

| Проверка | Результат и граница |
| --- | --- |
| `npx.cmd nx test web --skipNxCache --watch=false` | 200/200, включая B3 regression, memory owner guards, append/retry/focus/restore, абсолютный deadline и передачу loader при отмене polling/token refresh |
| `npx.cmd nx test api-client --skipNxCache` | 37/37, runtime list decoder и старые contracts |
| `OrderQueriesTests` | 12/12, реальный EF/Npgsql запрос к временному PostgreSQL с EnsureCreated, без применения миграций |
| `FlightsEndpointsHttpTests` / `ReadmeRequestExamplesTests` | 25/25 и 11/11, no-DB ASP.NET HTTP fixtures с fake message bus; не live JWT/Host proof |
| Demo Node / README catalog | 22/22 и 11/11, вымышленные данные, чистый list GET, paging, no-store, bearer rejection, restart и каталог восьми запросов |
| `npm.cmd run test:flights-demo` | 22 Node и 9 Chromium browser tests, локальные listeners/proxy; 45 fictional holds, auto 20+20, Browser Back/явный возврат ±4px, viewport 360px, reload, keyboard error/retry и отсутствие persistence |
| Keyboard browser case отдельно на новом stub | 1/1; тест сам создаёт данные и не зависит от предыдущего case |
| Production и flights-demo Angular build | обе SSR/prerender сборки прошли; новый список Client-rendered |
| Web/API-client ESLint, Biome error-level CI, CSharpier, diff check | прошли; известные AD0001, Node MockTimers и informational tooling diagnostics не являются новыми gate failures |

Desktop и mobile screenshots просмотрены. Demo browser suite выполняется одним worker: все cases используют один in-memory stub, поэтому concurrent hold из другого файла сдвигал offset в тесте. Это изоляция fixture, не изменение product contract.

## Исправления по проверкам и review

Браузерный RED выявил повторную запись anchor при уничтожении уже detached DOM: сохранённая координата становилась нулём, и возврат смещался на 276px. Snapshot теперь записывается только при переходе/клике до удаления DOM; unit regression и browser return прошли.

Независимое source review нашло три P2, все исправлены: ожидание проекции могло выйти за deadline или сохранить banner после подгрузки; recent-hold hint мог пережить auth-смену без открытого списка; keyboard case зависел от seed предыдущего test. Новые deadline/auth regression tests воспроизвели ошибки до правки и прошли после. Самостоятельный keyboard browser case и общий browser suite также прошли. Существенных оставленных замечаний нет.

## Финальный аудит готовности

По запросу пользователя 2026-10-01 заново выполнен fetch: `origin/dev` всё ещё `53df375368c1d4fe5b53fe1d3514d2754059e7ed`, ancestry верна, расхождения базы нет. Полный `dotnet build Travel.slnx --configuration Release --maxcpucount:1` прошёл (0 errors, известный AD0001 warning). В Release также прошли Flights unit 480/480, architecture 167/167, contract 9/9 и focused no-DB HTTP 36/36. CI validators: harness 90/90, inventory 62 passed/1 intentional skip, README 11/11. После дополнительных frontend fixes повторно прошли полный web 200/200, demo Node22/browser9, обе Angular builds, web lint, Biome и diff check.

Аудит нашёл ещё две гонки: токен мог разрешиться уже после polling deadline и привести к новому GET; отмена polling могла оставить loader занятым до microtask и потерять auto-trigger следующей порции. Введены `requestVersion`/`activeMode`: отмена сохраняется через await токена, после него повторно проверяется deadline, polling синхронно освобождает loader, старые catch/finally не меняют новый запрос. Три регрессии воспроизвели оба дефекта и ручную замену polling (RED), затем прошли (GREEN). Closure source review подтвердило исправления без оставшихся конкретных blockers.

Незавершённого implementation scope нет. Публикация авторизована; до merge требуется зелёный CI опубликованного PR. Полные CI lanes, запускающие Host/AppHost и schema initialization, локально не исполнялись под запретом применения миграций. Они не подменяются demo или no-DB доказательством.

## Ограничения доказательства

Offset-пагинация не даёт стабильного снимка при появлении/перепроекции заказов между GET. Слияние одинаковых ID убирает повторные карточки, но не восстанавливает пропуски; полный refresh начинается только по действию пользователя. Возврат сохраняет старую проверенную ленту, поэтому статус может быть устаревшим до refresh или открытия B3.

Demo auth не проверяет JWT/владельца и не доказывает реальные booking/payment/ticketing, live issuer callback `/flights/orders` или deployed EF projection convergence. Эти gates не запускались; supplier/payment/Anthropic не вызывались. Production auth требует текущий web contract (`aud=travel-web`, GUID sub, `flights:book`); backend read policy остаётся `[Authorize]`.

Снимки интерфейса: `flights-orders-top.png`, `flights-orders-desktop.png`, `flights-orders-mobile.png`; перед уборкой они сохраняются в Codex visualizations вне временного worktree. Эти снимки можно воспроизвести browser suite. Live acceptance остаётся отдельным этапом.
