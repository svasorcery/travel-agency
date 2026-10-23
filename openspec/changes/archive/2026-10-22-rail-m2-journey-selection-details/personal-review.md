# Rail M2 — личная проверка основного агента

Дата: 2026-10-21. Утверждённый scope реализовал отдельный агент `gpt-6.1-sol`; основной агент лично проверил итог и повторил offline-наборы. Source/offline результат принят. Открытых существенных замечаний этого review нет.

Дополнение 2026-10-22: отдельно разрешённые actual Host/OpenAPI и fictional measured gates приняты, см. [runtime verification](runtime-verification.md). Ниже сохранена source/offline приёмка на дату 2026-10-21.

Последующая публикация, CI и sync/archive приняты отдельно: [archive verification](archive-verification.md).

## База и область изменений

- Worktree: `C:\Users\Vladimir_sva\.codex\worktrees\rail-m2-implementation\travel-agency`.
- Ветка: `codex/rail-m2-journey-selection-details`.
- Запрошенный свежий `origin/dev` получен через `git fetch origin dev`: `a8721effd9056459d217966083816455fbea599d`. Ветка создана именно от него; ancestry проверена до/после переключения и при финальном review. HEAD остался тем же: коммитов нет.
- Утверждённый change перенесён из primary checkout с совпадением SHA256 всех6файлов. Основной `dev` не переключался; Trips worktrees не менялись.
- Worker v1: source digest `021e8e4167ace08e10d9f552a9ce1f0ed33d49fe4fad14bf86780f95091f0350`. После дополнительного исправления классификации ошибок создан отдельный worker v2: `01a8082718476a3a56ecb4129d4775e7ef3abb21cc122027f1946a93d33f0242`. Старый snapshot сохранён, не переписан новым результатом.
- Основной агент повторно проверил все188file hashes v2:0расхождений. Между v1/v2 изменились только новый endpoint, detail coordinator, два соответствующих test files и verification receipt.
- Inventory до финализации документов:72пути,0изменений Trips, dependencies/lockfiles или workflows. Staged diff пуст. Родительская финализация ниже меняет только документы review/статус задачи; product source v2 сохраняется.

## Что проверено лично

Проверены Core fact/occurrence rules, свежая verification и locator parsing, fixed-host transport, shared admission/late cleanup, conditional cache eviction, strict HTTP body и разделение400/413/429/200typed/500, TS reply matrix, UI epochs/expiry/filtering и source attribution. Source-only measured predicates и source manifest проверены отдельно от runtime.

По сравнению с исходной базой прежние OpenAPI paths, schemas и прочие root/component fields семантически сохранены. Добавлены только `/api/rail/journey-details` и9Rail schemas. Это structural source check плюс lean Rail OpenAPI, не исполнение настоящего Host.

В review исправлены и подтверждены регрессиями:

1. Недостающие Core platform/source invariants; несогласованная стоянка остаётся unavailable, не стирает пригодные остановки.
2. `thread.from/to` разобраны как строковые коды, без копирования object shape из search API.
3. Положительные limited/skipped decoder cases, request/profile binding и раздельные reference/search/detail сроки.
4. Панель получает фокус и видна при длинной выдаче; detail-only expiry оставляет видимый live-region status без старых route facts.
5. Exception completion соблюдает caller cancellation, profile, reference expiry и deadline; поздняя работа держит permit до settlement.
6. Thread header/token/depth limits дают output-limit, включая `HttpRequestError.ConfigurationLimitExceeded` до создания response; M1 search classification не менялась.
7. Measured useful predicate отвергает чужой request/profile, отсутствующую verification, неверный TTL и200failed; новая detail fixture включена в source provenance.
8. Новый endpoint ловит ошибки только чтения/парсинга input. Неожиданные profile/cache/platform ошибки дают sanitized500; только ошибки вызова/ожидания provider child становятся typed provider failure. Existing M1 endpoint bodies сохранены.

Дополнительный независимый read-only reviewer подтвердил закрытие своих findings по backend/decoder/state и последнему header-limit пути. Затем основной агент лично обнаружил и проверил исправление platform boundary; исходный RED2/2 и последующий GREEN сохранены отдельно.

## Повторные offline-проверки основного агента

Все команды выполнены из указанного worktree. .NET использовал `--no-restore --maxcpucount:1 -p:NuGetAudit=false -p:SolutionName=Travel` и явный `-p:SolutionDir=C:/Users/Vladimir_sva/.codex/worktrees/rail-m2-implementation/travel-agency/`. Пакеты/браузер не скачивались.

| Набор | Результат | Локальное доказательство |
|---|---|---|
| `dotnet test tests/rail/Travel.Modules.Rail.Tests.Unit/Travel.Modules.Rail.Tests.Unit.csproj` |74/74,0failed/skipped|`TestResults/rail-m2/parent-unit-v2.trx`|
| `dotnet test tests/rail/Travel.Modules.Rail.Tests.Integration/Travel.Modules.Rail.Tests.Integration.csproj` |136/136,0failed/skipped; terminal/TestServer only|`TestResults/rail-m2/parent-integration-v2.trx`|
| `node node_modules/vitest/vitest.mjs run --config shared/ts/api-client/vite.config.mts shared/ts/api-client/src/rail.decoder.spec.ts shared/ts/api-client/src/rail-details.decoder.spec.ts` |39/39|Прямой вывод parent tool call; эти TS файлы не менялись в v2|
| `node node_modules/vitest/vitest.mjs run --config tools/rail/vitest.config.mts` |35/35|Прямой вывод parent tool call; TS не менялся в v2|
| Frozen hashes / scope / исходный OpenAPI snapshot |188hashes совпали;0scope violations;0changed existing fields|worker-v2 inventory и независимое сравнение с точным base|
| Visual QA mobile route |Прочитаны screenshot и template/state; длинные названия переносятся, посадка/высадка различимы, времена/unknown facts не теряются|`test-results/rail-search-local-view-con-38b3e-erves-exact-fares-on-mobile-chromium/rail-route-mobile.png`|

Проверены receipts исполнителя: architecture183/183, Angular22/22, fictional browser15/15 плюс отдельный focus/viewport1/1, pure measured-source5/5, lean Rail OpenAPI1/1, Angular compiler, dependency-free harness134/134, scoped formatting и strict OpenSpec. Не выдаются за повторно выполненные родителем или за actual Host acceptance. Полные команды, RED/исправления и53scenario mappings: [verification](verification.md).

## Ограничения и финализация

Сохранены предупреждения ASP.NET RouteHandlerAnalyzer AD0001 и advisory Biome; анализаторы/проверки не отключались, warning-free build не заявляется. Исходный telemetry RED133/134 остаётся в отчёте; исправлена общая test-fixture конкуренция, assertions и product deadlines сохранены.

21/21задач завершены в утверждённой source/offline области. 7.1 означает готовый test/snapshot source, а actual Host/БД/OpenAPI execution остаётся pending. 7.2 означает готовый профиль/runner/predicates, а нагрузка/memory measurements не запускались. Ordinary CI, реальные API/keys/rights/quota/banner/target, commit/push/PR/sync/archive/deploy не выполнялись. Второго источника, пересадок, покупки и интеграции с Trips нет.

После записи этого review/task status формируется отдельный `TestResults/rail-m2/source-manifest-parent-final.json`. Он отражает финальные документы, не заменяет сохранённый worker-v2 snapshot, на котором выполнены повторные .NET проверки. Production/test source относительно v2 не изменён.
