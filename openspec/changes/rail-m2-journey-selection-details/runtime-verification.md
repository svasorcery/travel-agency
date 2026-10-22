# Rail M2 — actual Host и вымышленный нагрузочный профиль

Дата: 2026-10-22. После source/offline приёмки пользователь отдельно разрешил actual Host с одноразовой PostgreSQL и один фиксированный mixed load/memory run. Оба этапа приняты. Реальные API, credentials, commit/push/PR и deployment не выполнялись.

## Идентичность и границы

- Worktree: `C:\Users\Vladimir_sva\.codex\worktrees\rail-m2-implementation\travel-agency`; ветка `codex/rail-m2-journey-selection-details`.
- Свежий исходный `origin/dev`, required base и неизменённый HEAD: `a8721effd9056459d217966083816455fbea599d`. Ancestry повторно проверена; staged diff пуст.
- Исполненный source digest: `41ef618fd29d2d5dce3272bf71f1cd9d8c67ad4adba170223bd78f0bb4d4a1bb`, 189 file hashes. Это сохранённый `TestResults/rail-m2/source-manifest-parent-final.json`.
- Профиль `rail-m2-host-memory-v1`: SHA256 `f7f81e4c8d9f9c63b241b6f0e6ae98da2d5fcbdb5fbf3f7d853202c5d0159206`.
- Корень локальных невёрсионируемых receipts: `TestResults/rail-m2-runtime-20261022/` относительно worktree. Все команды ниже запускались из корня worktree.
- Actual Host использует настоящий `Program.cs` и одноразовую PostgreSQL. Fixture очищает конфигурацию, исключает saved secrets и внешний transport; Rail получает самостоятельно созданные fictional ответы через production parser. Чужие модули не получают runtime acceptance от этой проверки.
- Local NuGet restore использовал только cache с очищенными package sources и `NuGetAudit=false`; Docker использовал уже имеющиеся images, без pull. Реальных вызовов поставщиков и платных/model вызовов — 0.

## Actual Host: 3/3

Один запуск, 3 passed, 0 failed, 0 skipped. Проверены default-off/zero dispatch, catalogue/search/explicit details через настоящий Host и OpenAPI с сохранением прежних схем/путей других модулей.

```text
dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --no-restore --maxcpucount:1 --filter FullyQualifiedName~RailHostContractTests -p:NuGetAudit=false -p:SolutionDir=C:/Users/Vladimir_sva/.codex/worktrees/rail-m2-implementation/travel-agency/ -p:SolutionName=Travel --logger 'trx;LogFileName=rail-m2-host-attempt01.trx' --results-directory TestResults/rail-m2-runtime-20261022/host-attempt01
```

В PowerShell значение `--logger` передавалось одним аргументом в кавычках. Receipts: `host-attempt01/invocation.json`, `console.log`, `completion.json`, `rail-m2-host-attempt01.trx`, `acceptance.json`, `source-and-binaries.json`. Последний связывает результат с 189 исходными файлами и 6 Debug DLL. Все 3 принадлежащие fixture PostgreSQL и временных каталога удалены; точные IDs, пути и test IDs сохранены в acceptance receipt.

## Mixed load/memory: 6/6

Один запуск `measured-attempt01`, без retry. Начало launcher: 09:38:30 MSK; завершение: 09:52:34 MSK. Guarded launcher сделал свежую Release build, затем actual Host run. Exit code build/run/wrapper — 0; `sourceUnchanged=true`, `binariesUnchanged=true`, `automaticRetry=false`. Сохранены 10 executed Release DLL hashes.

```text
node tools/rail/run-measured.mjs --m2 --run-authorized --actual-host-db-authorized --receipt-dir C:\Users\Vladimir_sva\.codex\worktrees\rail-m2-implementation\travel-agency\TestResults\rail-m2-runtime-20261022\measured-attempt01
```

Профиль неизменён: 3 цикла при outer concurrency 1 и 2; в каждом 500 uncached all-mode searches и 500 details по 200 stops. Полезный denominator — ровно 1000/1000, failures — 0; 1500 search GET + 500 thread GET на цикл. HTTP 200 с failed/skipped/limited, неверным binding или непригодным selected segment не засчитывается.

| Concurrency | Цикл | Полезные / попытки | Ошибки | Search p95, ms | Detail p95, ms | Working set после recovery, bytes |
|---|---|---|---|---|---|---|
| 1 | 1 | 1000/1000 | 0 | 1.2071 | 10.4692 | 376688640 |
| 1 | 2 | 1000/1000 | 0 | 0.5532 | 8.5300 | 382840832 |
| 1 | 3 | 1000/1000 | 0 | 0.5364 | 7.5264 | 378437632 |
| 2 | 1 | 1000/1000 | 0 | 0.8737 | 9.0854 | 392167424 |
| 2 | 2 | 1000/1000 | 0 | 1.1616 | 10.2489 | 391458816 |
| 2 | 3 | 1000/1000 | 0 | 1.0718 | 9.5476 | 396849152 |

Итого: 6000/6000 полезных операций, из них 3000 searches и 3000 details; 12000 fictional source reads полезной нагрузки. Отдельная saturation-фаза: 24 попытки, 12 cancellations и 12 capacity refusals, 24 source dispatches; они не включены в 6000 successes. Во всех шести циклах отказ/отмена проверены и in-flight после useful workload равен 0.

Baseline — median пяти samples за 30 секунд: 470704128 bytes. Предел восстановления — 541309747 bytes, прежняя формула baseline + max(16 MiB, 15%). После каждого фиксированного 120-second recovery без forced GC предел соблюдён; максимум 396849152 bytes. Каталог 40 MiB/100000 stations и cache pressure 128 сохранены. Sampled warm peak — максимум 512356352 bytes; process-lifetime peak — 512409600 bytes, включая cold catalogue и предыдущие циклы.

Baseline и все recovery receipts показывают 1 reference bundle, 0 search entries/active children/reference loaders, 2 свободных shared permits. Отдельный счётчик `activeDetailStates` не инструментирован: нельзя выдавать отсутствие такого поля за независимо измеренный ноль. Server detail cache отсутствует по source contract; окончания HTTP, children и освобождение shared permits проверены.

Receipts: `measured-attempt01.source.json`, `.launcher.json`, `.completion.json`, `.process.log`; внутри `measured-attempt01/` — `source-identity.json`, `baseline.json`, все 6 cycle receipts, все 6 полных denominator receipts с 1000 latency samples каждый и `verdict.json`. Основной агент отдельно сверил каждый denominator, p50/p95 по raw samples, counts/bounds/state, 189 source hashes и 10 DLL hashes: `parent-runtime-acceptance.json`.

## Ресурсы, диагностика и финализация

- Docker Engine 29.8.2. Cached PostgreSQL image `pgvector/pgvector:pg17`: `sha256:7ae6051efd0e60444282c27c7e141af07f322ce033300e727a49c3dd11075e38`; Ryuk 0.14.0: `sha256:7c1a8a9a47c780ed0f983770a662f80deb115d95cce3e2daa3d12115b8cd28f0`.
- Measured PostgreSQL `663767a4288c0be33d673cbb56dd12db4f67bf05a398cd59ddc4ffb84b512b74` и Ryuk `c1c855bd4c52ffa30a24c4e0872abd79e6ea0b7f696903058d2891d9c11f062a` удалены автоматически. Наблюдавшийся временный каталог `travel-hotels-host-459514cf1a034c0687c1abf304e51c23` также отсутствует. Точные результаты — `measured-attempt01.cleanup.json`. Preexisting Pulsell/buildx containers сохранили исходные состояния; ручной prune не использовался.
- Release build сохранила 30 warnings: 29 прежних ASP.NET RouteHandlerAnalyzer AD0001 и Verify solution-autodiscovery warning для standalone fixture; ошибок 0. Проверки/анализаторы не отключены; warning-free результат не заявляется.
- В старой текстовой таблице `verification.md` оказался устаревший profile hash `d90d428...`; actual bytes и сохранённый dry describe уже совпадали с `f7f81e...`. Исправлена только таблица после завершения run, профиль не менялся.
- Первый вспомогательный `verify-receipts.mjs` ожидал неверный регистр `HotelsWaiver`; официальный JSON содержит `hotelsWaiver`. Сохранены исходный helper и исправленный `verify-receipts-v2.mjs`; повторно прочитаны готовые receipts. Это не повтор measured run и не изменение его verdict.
- После run обновлены только документы и сформирован отдельный post-documentation manifest; его digest не подставляется вместо исполненного `41ef618...`. Runtime source/DLL receipts сохранены.

Rail M2 source/offline, личный review, actual Host/OpenAPI и фиксированный fictional load/memory приняты. Ordinary CI/PR, реальные Yandex shape/rights/quota/banner/target и rollout остаются отдельными этапами. Оба Rail switches сохраняют default-off. Trips, второй supplier, пересадки, inventory и покупка не затронуты. Commit/push/PR/sync/archive/deploy не выполнялись.

Дополнение перед публикацией: после этого run исправлены только границы импортов в Rail web-тестах, добавлена локальная test fixture и её manifest entry. Production-код и профиль нагрузки не изменены; повторены lint/TS/Angular. Подробности и исходный RED — в [verification](verification.md#pre-publication-test-boundaries--2026-10-22). Runtime digest выше остаётся идентичностью единственного исполненного measured run.
