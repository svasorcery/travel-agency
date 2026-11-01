# H5 v4 — диагностика завершена, прирост локализован в Heap (Private Data)

2026-10-31. Выполнена ровно одна разрешённая попытка H5 v4. **Получены все три карты P0/P1/P2, все 56 запросов и 35 измерений. Ошибок, повторов и принудительной остановки нет; очистка подтверждена.**

Основной результат: между P1 и P2 категория VMMap `Heap (Private Data)` выросла на **640106496 B = 610.45 MiB**. Это **99.72% прироста XML top PrivateBytes** и 99.50% прироста OS private counter. Практически весь рост этой категории относится к регионам с одним raw ID `131073`. Код-владелец и наличие утечки пока не установлены.

## Исполнение и полнота

| Факт | Результат |
|---|---|
| Время | 10:37:39.907–10:38:56.535 UTC; 13:37:39–13:38:56 МСК |
| Внешний вызов / внутренний attempt | 76.627 s / 74.311 s |
| Driver / Host | 57572 / 53084 |
| Exit / forced stop | 0 / false |
| Карты | P0, P1, P2: 3/3, reader errors 0 |
| IPC | 35 observed / 35 attempted: 1 preflight + 20 control + 1 boundary + 13 warmup/settle |
| Public requests | 56/56 Response + 1 readonly preflight GET |
| Workload | 2 search, 1 rates, 1 prepare, 1 create, 1 callback, 50 status |
| Missing/cancelled/unfinished/early starts | 0 / 0 / 0 / 0 |
| Acquisition / cleanup / final complete | true / true / true |
| Execution/finalization errors | отсутствуют |

На t12 active sources и capacity lanes равны0, pendingInbox0, dueWork0. FutureWork17 сохранён, не обнулялся. Максимальная sample lateness0.609s при допуске1s. Сроки не продлевались, P2 снята только после проверки готовности. Capture durations: P0=1.240s, P1=1.160s, P2=1.392s.

Setup сохранил38rows,40Duffel/16HBXseed starts и420cacheputs. Во время public phase выполнено ещё4Duffel/6HBXsource starts. У Hotelbeds появился ровно один новый effect, все effects имеют count1, unknownEffects0. Create26 вернул HTTP200/ReservedAwaitingDetails/PurchaseAccepted=true на4.8482143s; в authoritative sample7(at7.0393213) та же бронь уже Confirmed=true/SourceMatched=true. Промежуточный ответ не переименовывался в Confirmed.

Это полная диагностическая acquisition, **не приёмка S/L**: attempt сохраняет diagnosticOnly=true и m2Accepted=false. Короткий child штатно завершён completed=false/expected exit1, без вызова запрещённого для короткого окна final-facts360s.

## Три карты памяти

OS private/RSS ниже — parent counter непосредственно перед соответствующим capture. XML totals сохраняются отдельно; они не подменяются друг другом. [Windows PrivateUsage](https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-process_memory_counters_ex) описывает process commit charge, а [VMMap](https://learn.microsoft.com/en-us/sysinternals/downloads/vmmap) отдельно показывает committed memory и working set по категориям.

| Этап | OS private, MiB | OS RSS, MiB | XML top PrivateBytes, B |
|---|---:|---:|---:|
| P0: после setup |267.01|378.93|272310272|
| P1: после20s observer-only control |262.20|379.60|267177984|
| P2: после56jobs и settle доt12 |875.75|1016.45|909078528|

Все21control/boundary наблюдение сохранили source starts40/16, effects и нулевые runtime admission/active. Public work в control отсутствовала. P0→P1 OS private уменьшилась примерно4.80MiB; крупного роста в этом интервале нет.

### P1 → P2 по категориям XML

| Категория | PrivateBytes P1 | PrivateBytes P2 | Прирост |
|---|---:|---:|---:|
| Heap (Private Data) |91885568|731992064|**640106496 B / 610.45 MiB**|
| Private Data |167915520|169553920|1638400 B / 1.56 MiB|
| Thread Stack |1671168|1822720|151552 B|
| Image (ASLR) |5697536|5701632|4096 B|
| Shareable |8192|8192|0|
| Остальные private categories |0|0|0|

Суммарный XML top private прирост641900544B (612.16MiB); OS private прирост643346432B (613.54MiB). Дополнительно Shareable Commit вырос21581824B, но его PrivateBytes не выросли. Это не ещё21.6MB private: показатели разделены.

Map working-set прирост в Heap (Private Data) составил643055616B. Большая часть изменения произошла до P2: OS private во время самого capture выросла только122880B, RSS824KiB. Порядок наблюдений всё же влияет на cold timing, поэтому это temporal localization, не строгий causal A/B.

## Где сосредоточен рост heap

В пределах `Heap (Private Data)` группа с raw XML `Id=131073`:

| Этап | Верхних регионов | PrivateBytes |
|---|---:|---:|
| P1 |13|91738112|
| P2 |130|731791360|

Прирост группы640053248B: 117 новых регионов дали580263936B; в13 прежних регионах private выросла ещё59789312B. Ни один из этих13 адресов не исчез. Остальные heap-ID группы объясняют только53248B прироста категории. Адреса и изменения каждого региона сохранены в `analysis-v1/heap-localization.json` и `memory-analysis.json`.

`131073` оставлен именно raw ID: он не декодирован в OS heap handle, номер default heap или имя DLL. Число регионов не является числом прикладных аллокаций. Поля Details этих регионов пусты; стеков выделений в картах нет.

Windows различает committed bytes heap и занятые блоки: committed size включает свободные блоки, занятые блоки и служебные структуры. Поэтому рост heap commit сам по себе ещё не доказывает live leak. Это ограничение подтверждает [PROCESS_HEAP_ENTRY](https://learn.microsoft.com/en-us/windows/win32/api/minwinbase/ns-minwinbase-process_heap_entry). Здесь не снимались allocation stacks и не выполнялся HeapWalk/UMDH/ETW.

## Сопоставление с managed и retained state

| Sample | Private, MiB | Managed estimate, MiB | Last-GC commit, MiB | Gen2 |
|---|---:|---:|---:|---:|
| boundary доbegin |262.20|104.61|149.64|64|
| 0 |499.08|118.99|149.64|64|
| 1 |502.66|145.37|149.64|64|
| 2 |374.28|48.52|141.86|65|
| 5 |608.65|90.34|141.86|65|
| 7 |726.00|106.69|141.86|65|
| 9 |871.52|83.43|153.07|65|
| 12 |875.75|85.31|153.07|65|

Это planned sample seconds; фактические parent observations занимают до0.609s. Между samples1/2 произошёл естественный Gen2, одновременно уменьшились managed estimate и private. Это корреляция, не доказательство того, что GC освободил конкретный native heap. Forced GC не вызывался.

JITmethods49301→72541, assemblies389→397. Cache entries остаются128; payload1488768→1667126B, всего+178358B. Reservations38; inbox1 processed row. Native map непосредственно показывает основной прирост в heap category, поэтому вывод не основан на простом вычитании managed из private. Однако ни JIT correlation, ни небольшой cache growth не идентифицируют конкретный allocator/call site.

P2 ниже прежнего~1.2GiB private из S-v2; это не исправление того результата. В H5 иной порядок прогрева и наблюдений и в этот раз есть естественный Gen2. Процесс наблюдался только до короткой P2, а не на полном360s S/L. Исторические memory failures не отменяются.

## Целостность и cleanup

Host53084 иdriver57572 отсутствуют. Container `83854881e14a80d043acc3e30be1acee58986d4043fa0df18d5319fd9322f37a` отсутствует по независимому Docker read(exit0); temporary directory `travel-hotels-host-d8325867edda4714a0db7abe53c0d561` отсутствует. Runtime дождался выхода коллектора каждой карты; его PID helper не сохраняет, поэтому независимая дополнительная проверка ограничена exacttoolpath+creationwindow — живых совпадений нет.

Новой буквальной папки %SystemDrive% в worktree нет. Два support-файла VMMap в ownedrun/SOS сохранены и совпадают с ранее установленными hashes; SOSdeps.zip ранее побайтно найден внутри pinned executable. Отдельных debugger/SOS commands, downloads, full dumps или четвёртой карты не было.

57recursive raw artifacts этой попытки зафиксированы,49runtime hash entries совпали. Все132предыдущихraw сохранилиSHA256. Post-run verify-only подтвердил frozen source/build/runtime/tool identity; это не повтор --execute. Суммарно run-01 содержит7280703B. Исходные thresholds/data/source не менялись, cache/fake не очищались.

| Raw map | SHA256 |
|---|---|
| P0.xml |A4942D06C200D1A3FC0C8D15795AA2D7974C0239C7C0D7E395256076B4C96ED1|
| P1.xml |CAB1E7C9143DD77116CC9B8019B44009F0216D2CB7FAACDBD254CF2E0B3774C2|
| P2.xml |EB43359B80D897343AE414E47BDF23036CE5D3824EF3F3F1DF2BAC61C3A5EED1|

Evidence: `TestResults/hotels-m2-memory-load-acceptance/diagnostic-host-private-v4/`. Offline analysis: `analysis-v1/memory-analysis.json`, `heap-localization.json`, `workload-accounting.json`, `summary.json`, `cleanup-verification.json`, `raw-integrity.json`, `integrity-check.json`. Повторное чтение raw тем же frozen parser совпало с capture totals всех трёх карт.

## Вывод и следующий шаг

H5 ответил на вопрос о категории и этапе: основной прирост связан с first-request phase и сосредоточен в группе heap regions, а observer-only control его не воспроизводит. **Кто выделяет/удерживает эту память и сколько в heap занято живыми блоками — ещё неизвестно.** Product memory patch пока не обоснован.

Следующий обоснованный шаг — подготовить ограниченную native-heap диагностику, которая различает busy/free/служебную память и связывает растущие выделения со стеками или компонентом. Она должна использовать уже найденную группу/диапазоны, а не повторять общий RSS-run. Сейчас новый протокол, инструменты или runtime не запускались.

M1 остаётся failed16/17; S-v2 failed22/24; Lnot-run. Общий план6/8, задачи3.2/3.3pending. Успешная H5 acquisition не является M2 load acceptance, supplier/target readiness или доказательством отсутствия утечки.