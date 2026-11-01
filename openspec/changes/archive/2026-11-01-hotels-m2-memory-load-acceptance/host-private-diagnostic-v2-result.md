# H5 v2 — результат единственной попытки

2026-10-31. После «го» и подтверждения владельца «принял» проверен EulaAccepted=true и выполнен один frozen H5. **Диагностика не завершена: сохранена исходная карта P0, затем исполнитель остановился на ошибке XML-reader. P1/P2 и workload не выполнялись.** Повтора не было.

## Исполнение

| Факт | Результат |
|---|---|
| Время | 09:05:36.024–09:06:20.265 UTC; 12:05:36–12:06:20 МСК |
| Driver / Host | 2336 / 22504 |
| Весь вызов / внутренний attempt | 44.240 s / 41.021 s |
| Exit / forced stop | −532462766 / false |
| Setup | 38 rows; seed starts Duffel40/HBX16; 420 cache puts; admission unspent |
| Public requests | 1 readonly preflight GET; 0/56 warmup jobs |
| IPC samples | 1 observed / 1 attempted, из planned35 |
| Карты | raw P0 есть, 1836949 bytes; P1/P2 not-run; reader accepted0/3 |
| Ledger | все56 NotRun; Response/Cancelled/Unfinished0 |
| Основной отказ | P0:InvalidDataException |
| Дополнительный отказ | finalization Identity:InvalidOperationException |
| Acquisition / cleanup | false / true; independent cleanup проверен |

Пакет `TestResults/hotels-m2-memory-load-acceptance/diagnostic-host-private-v2/` израсходовал `attempt-1.json`. Этот attempt остаётся failed. Последующая verify-only проверка hashes не разрешает повтор и не исправляет его результат.

## Почему остановился исполнитель

**1. XML-reader не соответствовал реальному VMMap3.4 export.** Все7891 Region содержат отдельные `PrivateWS`, `ShareableWS`, `SharedWS`; атрибутов `WS` и `WorkingSet` нет. Frozen reader упал на HotelsH5Map.cs:113. Кроме того, `Type` и `UseType` присутствуют одновременно: это текстовая категория и числовой код, а не взаимоисключающие aliases. После устранения только первой ошибки старый Field(Type,UseType) тоже отказал бы.

Это дефект моей source-подготовки: синтетический XML не воспроизводил реальный export. Ошибка воспроизведена offline вызовом именно frozen HotelsH5Map.Read на сохранённом P0, без Program/Host/VMMap. `analysis-v1/parser-replay.json` сохраняет тип, сообщение и stack. Сборщик сформировал XML; проблема возникла при чтении результата.

**2. Окружение коллектора было чрезмерно очищено.** Frozen StartInfo оставляет только SystemRoot/WINDIR/TEMP/TMP; SystemDrive и ProgramData отсутствуют, WorkingDirectory пустой и наследует корень driver. Во время P0,09:06:12.730–12.749UTC, появились6 служебных файлов под буквальным `%SystemDrive%/ProgramData/Microsoft/Windows/Caches/` в worktree. Поэтому source inventory вырос1975→1981, хотя hashes всех прежних1975 source/input файлов сохранились. Это объясняет отдельный identity refusal. Связь с окружением — вывод из точной конфигурации и совпадающих времени/пути; отдельного A/B запуска не было. Механизм наследования/замены environment описан в [Microsoft Windows documentation](https://learn.microsoft.com/en-us/windows/win32/procthread/environment-variables).

После проверки creation times внутри окна единственной попытки, точных абсолютных границ, отсутствия reparse points и SHA256 все6 файлов **перемещены без удаления** в `analysis-v1/collector-created-worktree-files/`. Их hashes сохранены. Source inventory снова1975, verify-only source/build/runtime/tool проверка прошла. Исходный failed attempt и finalization error не переписаны.

## Что показала P0

P0 снята после setup, до control и до warmup. Capture длился2.288 s. OS counters до/после: private274681856→274702336B (261.96→261.98MiB), RSS392458240→393850880B (374.28→375.61MiB). Изменение во время самой карты не является acceptance measurement.

Offline XML-анализ суммирует2420 верхних регионов;5471 вложенный регион не добавляется второй раз. Raw XML сохранён; SHA256 `D9AA96F887D7957180168FBC7EE3EDED1490478BA9881F958023AA24D418F491`.

| Raw category | PrivateBytes верхних регионов | MiB |
|---|---:|---:|
| Private Data |162484224|154.96|
| Heap (Private Data) |97062912|92.57|
| Image (ASLR) |5697536|5.43|
| Thread Stack |1777664|1.70|
| Shareable |8192|0.008|

Остальные категории имеют PrivateBytes0 в верхних записях. Их общий Commit не равен private memory: total Commit карты703844352B (671.24MiB), включая image/mapped/shareable категории. [VMMap](https://learn.microsoft.com/en-us/sysinternals/downloads/vmmap) разделяет committed memory и working set; [Windows PrivateUsage](https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-process_memory_counters_ex) — process commit charge. Эти величины не подменяются друг другом.

Есть ещё одна существенная особенность реального XML: у284 родителей сумма дочерних PrivateBytes отличается от агрегата родителя. Top total267030528B, leaf total268611584B; оба сохранены отдельно. Для Size/Commit/PrivateWS/ShareableWS/SharedWS top и leaf totals совпадают. Ни одну private сумму не выдаём за точный OS PrivateUsage и не превращаем расхождение в «native leak».

Перед P0 managed estimate был97802928B (93.27MiB), last-GC committed152514560B (145.45MiB), Gen2count64, assemblies389, JIT43491methods. Названия категорий VMMap не доказывают владельца CLR/JIT/allocator; Private Data не объявляется автоматически managed или native.

**Главный вопрос H5 остаётся unanswered:** P1/P2 отсутствуют, поэтому неизвестно, какая категория и какой этап дают прежний рост private до~1.2GiB. P0 описывает только baseline и не опровергает S-v2 memory failure.

## Cleanup и целостность

Host22504 и driver2336 отсутствуют. Owned container `980aa9fa1af6aafc2394d11b3be40fcd31350801912c73a3c46579b77217bc8d` отсутствует по независимому Docker read(exit0); временный каталог `travel-hotels-host-46a6fad1e1e343c6939b1d8b97ddfd41` отсутствует. Collector PID текущий runner не записал; дополнительно проверено отсутствие vmmap64 с точным tool path и creation time внутри этой попытки. Файлы, созданные коллектором, сохранены в evidence.

17 raw artifacts этой попытки зафиксированы дополнительным inventory;11 файлов исходного raw-integrity проверены без изменений. Все68 прежних M1/v5/S-L-v1/v2 raw artifacts сохранили hashes. Никаких full dumps, forced GC, очистки cache/fake, изменения порогов, real supplier/model calls или повторов не было. Код и build после запуска не менялись.

Основные offline receipts: `analysis-v1/P0-analysis.json`, `parser-replay.json`, `collector-environment.json`, `collector-created-files.json`, `source-identity.json`, `cleanup-verification.json`, `raw-integrity.json`, `integrity-check.json`. Анализатор `analysis-v1/analyze-P0.ps1` только читает сохранённый XML; его повторное offline вычисление не является повтором эксперимента.

## Следующий ограниченный шаг

Подготовить отдельный source-патч: reader по реальному P0 (оба type fields, отдельные WS components, иерархия и private aggregation), корректное минимальное Windows environment и owned collector cwd, сохранение collector identity. Replay actual XML и fault tests, новый frozen packet. Это предлагаемый следующий шаг, сейчас код не исправлялся. Отдельный runtime требует отдельного разрешения; consumed v2 не переиспользуется. Verify-only readiness также должна явно показывать consumed attempt вместо одного лишь EULA=true.

M1 failed16/17, S-v2 failed22/24, Lnot-run. План остаётся6/8; tasks3.2/3.3 открыты. Memory patch по продукту пока не обоснован.