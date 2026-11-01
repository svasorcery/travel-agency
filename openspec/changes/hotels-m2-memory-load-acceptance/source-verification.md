# Source-срез1.1–1.2 — 2026-10-29

После поручения владельца «окей, давай дальше» выполнен первый source-срез: instrumentation и ограниченные builds/standalone checks. Реальные Host/AppHost/Travel.AI, Docker/PostgreSQL, memory diagnostic/load, suppliers/model/paid API, migrations и publication не запускались. База/HEAD остаётся `c3fd3a6b3e8965ad8cf7850e160f22edfa12dedf`, ветка `codex/hotels-m2-memory-load-acceptance`; нет stage/commit/push/PR/merge.

## Что сделано

- `HotelCapacity.GetSourceSnapshots` отдаёт отдельные окна, starts/active/max/release facts двух источников. Старые admission rules и aggregate snapshot сохранены.
- Оба owned typed handlers имеют finite measurements: cumulative counters,64event buffer со sticky overflow, retained counts и optional bytes вне timed interval. Для точного effect count фиксируется hash безопасной local-attempt correlation; HBX20hex prefix не объявляется supplier idempotency. Неоднозначное соответствие не даёт success.
- Запись timed events начинается только после quiescent seed; counters/effects/dictionaries не очищаются. Before/after detailed captures относятся к двум настоящим M2 wires, а не к неиспользуемому legacy fake.
- Child sampler получает ограниченную38row projection без decrypt/вывода StateJson; parent сохраняет первое время наблюдения durable Confirmed. Source/attempt matching и фактические effects поступают в отдельные phase/window predicates.
- Existing runner получает полный parent ledger и подключённый M2 evidence consumer. Normal/final phase summary и recovery-покупки используют свои cutoffs. Legacy M1 Schedule/Phase/Gates/RecoveryAt и четыре дополнительные проверки сохранены.
- Финальный source artifact пишется после terminal sample, включая failure/partial outcome; create-only запись сохраняет все361points и before/after payload bytes. История/checkpoints живут в parent, не в Host.
- Manifest writer получил explicit `--m2 --profile <relative-path> --output <relative-path>`, защиту старых receipts и physical containment. Reader принимает явные M2 profiles, проверяет inventory/hash, relative-root resolution и junction boundaries. Default legacy profile list сохранён.

M2 workload/CLI activation, S/L catalogue, seed/fault scheduling и actual target verification относятся к task3.1/3.2. Этот source-срез не запускает их и не является M2 performance acceptance. Добавленные callbacks/producer/evaluator paths проверены standalone, а actual Host/DB runtime/restart остаётся отдельным доказательством.

## RED/GREEN и результаты

Локальные receipts в `TestResults/hotels-m2-memory-load-acceptance/source/` этого worktree; они ignored, не являются опубликованной CI.

| Проверка | Результат |
|---|---|
| Manifest write preservation/path boundaries | RED4/4 → GREEN4/4; `manifest-red.log`, `manifest-final.log` |
| Independent source windows/releases | RED2/2 → GREEN; `capacity-red.trx`, покрыты финальным suite |
| Owned-wire observations | RED1/1 → GREEN; actual sends/caller cancellation/sticky overflow и privacy |
| Ledger/cutoff | RED3/3 → GREEN; missing/cancelled/not-run/unfinished и301/310 boundary, duplicate/missing-effect rejection |
| Actual M2 writer→reader | RED из-за hardcoded M1 profiles → GREEN; tamper, relative root, physical evidence-tree regressions |
| Consumer path после independent review | RED2/2 → GREEN; actual TestServer booking→known-reference read→parent report и361point file round-trip |
| Полный разрешённый standalone suite после review fixes | **135/135**,0skipped, exit0; `standalone-after-review.trx` |
| Hotels unit suite | **79/79**,0skipped, exit0; `unit-final.trx` |
| CLI wrapper compile-only после review fixes | exit0,0errors; `wrapper-after-review.log`.30existing RouteHandlerAnalyzer/Verify warnings не подавлялись |
| Formatting | CSharpier1.2.6:16changed C#/project files checked; Biome2.5.15 (lockfile match):2JS files passed |
| Historical saved-data replay | **16/17**, workingSetRecovery=false, historical passed=false, recoveryAt305; `historical-offline-replay.json` |

Standalone integration command:

```text
dotnet test tests/hotels/Travel.Modules.Hotels.Tests.Integration/Travel.Modules.Hotels.Tests.Integration.csproj --no-restore --maxcpucount:1 /nodeReuse:false --disable-build-servers --filter "FullyQualifiedName!~HotelHttpTests&FullyQualifiedName!~HotelDurablePurchaseTests"
```

Исключены только две PostgreSQL-fixture classes. In-memory TestServer cases включены; они не запускают `Travel.Host`, внешние supplier sockets или БД. Full solution, actual Host/Aspire/relational/browser/CI не выдаются за выполненные.

Restore выполнялся с отдельным source-empty NuGet config и локальным package cache; новых зависимостей/lock изменений нет. NuGet advisory audit не выполнялся в этом offline restore и не заявляется как пройденный security gate.

Промежуточные ошибки тестового setup сохранены: nullable-double InlineData, неверное имя outcome enum и пропущенный auth header в новом test helper. Они исправлены до meaningful RED и не считаются product failures. Первый junction test столкнулся с Windows cleanup exception; остаток состоял из3проверенных пустых директорий и удалён точечно без recursive/glob cleanup. Fixture теперь удаляет собственный junction до parent directory, пишет точный owned root; receipt `manifest-reader-boundary-cleanup.json`.

## Независимое review и исправления

Один fresh-context reviewer `gpt-6-astra/high` проверил working diff и все новые files. Нашёл3Important,0Critical,0Minor:

1. Purchase predicates были доступны лишь helper-тестам, без фактов и evaluator wiring.
2. Periodic checkpoint до sampling терял terminal source frame на успешном завершении.
3. Detailed byte capture обеих M2 wires не доходил до boundary/runtime consumer.

Все3 исправлены одной TDD fix pass. Consumer regression выполняет actual owned purchase/read через TestServer, получает source matching/effects и доказывает разные phase/window результаты. File round-trip читает все361points, последние events и final bytes. После исправлений полный разрешённый suite135/135 прошёл. Повторное independent review не проводилось; не приписывать reviewer последующее одобрение исправленного diff.

Reviewer оставил за рамками task3.1activation/catalogue/seed/faults; actual Host/DB/cleanup runtime; самостоятельный повтор тестов; performance/root cause/supplier/target; parent historical replay; external/publication actions. Эти области не закрыты текущим source результатом.

## Принятые решения и пределы

- Existing owned dictionaries заменены на ConcurrentDictionary для безопасного наблюдения retained values. Wire semantics/caps сохранены, но memory overhead изменился и входит в будущий Host footprint; это не memory optimization.
- Использован canonical OpenSpec и working-diff ledger; commits и второй task corpus не создавались. Временные Superpowers task/commit scripts не подменяют OpenSpec.
- Source проверка не запускает запрещённые Host/DB suites. Цена границы — отсутствие runtime acceptance, которое честно остаётся pending.
- Отложенных review minors нет. Непринятых Critical/Important findings нет; остаются перечисленные будущие gates.

## Сохранение M1

Офлайн PowerShell replay использует **существующие** operations/resources/checkpoint/permit/read/before-run JSON, не новый эксперимент. Все17вычисленных predicates совпали с исходным receipt; только workingSetRecovery=false. Дополнительно проверены неизменные source sections Schedule, Phase, Gates, RecoveryAt и четыре дополнительных gates относительно базы.

Семь original evidence/profile hashes из research совпали, в том числе receiptSHA `a37df64485b4f585b846fb021c702585f90c178960bcac767f7965c9a08ace3e`. Saved demo/human-review материалы не изменены. Новая instrumentation не исправляет прошлый failed результат и не доказывает отсутствие утечки.

