# H6 — подготовка атрибуции native heap

2026-10-31. После успешного H5 v4 владелец сказал «го» на подготовку следующей диагностики. **Текущий результат — исследованный протокол и пакет входов; execution-ready=false.** Нового Host/DB/VMMap/ETW recording, heap snapshot, установки или повышения прав не было. Нельзя запускать H5 v4 как замену H6.

## Задача и доказательная граница

H5 v4 локализовал610.45MiB/99.72% прироста XML private в Heap (Private Data), почти весь — вrawId131073 (13→130regions). [H5 result](host-private-diagnostic-v4-result.md) и все189rawartifacts истории сохраняются. RawId и адреса нельзя переносить в новый процесс как heap handle или code owner.

H6 должен показать outstanding tracked allocations и их стеки в растущих heap/ranges. Он может установить, какие call sites удерживают большую часть нового объёма. Он **не обещает точное число свободных байтов всего heap**: startup allocations до включения tracking и служебная память могут остаться вне учёта. Commit минус tracked Size не называется free. Если большой остаток не объяснён, вывод — unknown; отдельная busy/free heap walk/debugger диагностика остаётся следующим вариантом.

## Выбор метода исполнителем

Выбран встроенный **WPR HeapSnapshot по PID**, с двумя snapshots в одном ETL и существующим VMMap для committed-memory bounds. Microsoft документирует PID-mode и стеки выделений, появившихся после включения tracking: [Record a Heap Snapshot](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/record-heap-snapshot). Это метод исследования, не новая система нагрузочного тестирования.

| Вариант | Решение |
|---|---|
| WPR HeapSnapshot -pid | выбран: существующий Windows tool, конкретный owned PID, snapshot allocation stacks |
| WPR Heap / UMDH с IFEO по dotnet.exe | не выбран: name-wide настройки и restart, UMDH/GFlags отсутствуют; не включать для всех dotnet |
| Native dump + WinDbg/!heap | отложен: debugger отсутствует, большие/private dumps; стеков прежних alloc без tracking может не быть |
| Собственный HeapWalk/PInvoke profiler | не выбран: heap locking/interop и риск влияния на target; стандартный recorder сначала |
| Самописный ETL decoder | не выбран; анализатор должен поддерживать официальный HeapSnapshot format |

Анализатор по умолчанию — **Microsoft WPA**, локально, без автоматического symbol download. Он сейчас отсутствует. Официальный альтернативный API умеет [IHeapSnapshot](https://learn.microsoft.com/en-us/dotnet/api/microsoft.windows.eventtracing.memory.iheapsnapshot?view=trace-processor-dotnet-1.0) и [IHeapAllocation](https://learn.microsoft.com/en-us/dotnet/api/microsoft.windows.eventtracing.memory.iheapallocation?view=trace-processor-dotnet-1.0), но новый custom parser/dependency сейчас не создаётся. WPA install/source integration — отдельный следующий шаг; первый runtime требует отдельного разрешения.

## Проверено локально без recording

- Windows10.0.19045; WPR executable10.0.19041.6093, CLI reports10.0.19041CoreSystem.
- Path C:/Windows/System32/wpr.exe; SHA2568C3D49A83EBD8138FFA15DA93D25BA1FC44ACDEC3CB9D729B6F43B55A5EF7CA5.
- -help подтверждает snapshotconfig heap -pid, singlesnapshot heap PID, -instancename последним параметром.
- Profile HeapSnapshot.Verbose.File существует. Два collectors, каждый20×1024KiB buffers: около40MiB суммарно.
- System provider keywords: CpuConfig,Loader,ProcessThread; event provider: Microsoft-Windows-Heap-Snapshot.
- Default WPR и проверенный отдельный instance сейчас не записывают.
- Query-only snapshotconfig/heaptracingconfig dotnet.exe:disabled; IFEO dotnet.exe key отсутствует, GlobalFlag/TracingFlags/PageHeapFlags не найдены.
- Текущий token не Administrator. WPA/xperf/UMDH/GFlags/CDB/WinDbg не найдены вPATH/стандартныхSDKдиректориях; WPA/WinDbgAppx не найдены. Ничего не установлено.
- Первый read-only profiledetails без-filemode вернул0xc5584017; повторный metadata query с-filemode успешно подтвердилпрофиль. Recording ниразуневызывался.

## Будущий один bounded run

Сохраняются S dataset,56originaljobs(Due<10),одинHost/однаownedDB,предыдущиеAPI/claim/source/privacyguards. Доfreezeданные/даты должныпроверяться; текущаяSдата2026-11-06→08 применима только до2026-11-04UTC, автоматическогосдвига нет.

Обычный driver/Host запускаются с обычным user token. Отдельный **одноразовый elevated WPR helper** допускает только fixed state machine ниже: один nonce, один owned PID/starttime/root/build identity, один named instance, два snapshot triggers,disable/stop/cleanup. Не generic admin command server; shell command text/произвольныеPID/paths отcaller запрещены. Helper должен пройти source tests/review передготовымfreeze. Elevation целого Host не является fallback.

Elevated helper получает разрешение ОС до создания Host; пользовательский UAC входит в начальный30s этап attempt. Atomic marker создаётся до helper/effects. Отказ/тайм-аут elevation завершаетпопытку безHost. No retry/resume.

| Этап | Действие | Данные |
|---|---|---|
| Preflight/elevation≤30s | hashes,даты,ресурсы,x64/adminhelper,unusedattempt,read-only WPR/default+named status и ETW session inventory | нет чужого WPR recording; unknown state — abort, не cancel чужого |
| Setup≤120s | ordinaryownedHost,readonlypreflight; после ready PID identity и до prepare включить PID heap tracking и start named HeapSnapshot trace; затем прежнийprepareS | preseed+seed+cachefacts; точный tracking-start timestamp |
| P0≤30s | прежняя VMMap baseline послеprepare | rawXML+bracketedOS counters |
| Control20s | прежние20IPC samples,безpublicwork | starts/effects/admissionнеизменны |
| Snapshot A≤30s | WPR singlesnapshot heap OwnedPid | baseline tracked allocations; собственныеbefore/after counters |
| P1≤30s | VMMap послеsnapshotA; затем boundary sample | baseline включает воздействияA; sourcevector/controlguard |
| Warmup/settle | теже56jobs/timers, samples0..12 | полныйdenominator;0active/0due/0inboxнаt12;futureworkсохранён |
| Snapshot B≤30s | только при успешнойt12quiescence, второй WPR heap snapshot | outstanding allocation/stack dataset |
| P2≤30s | VMMap; затем **один дополнительный IPC sample** | текущие ranges, послеcaptures source/retained vector |
| Cleanup≤120s | disable tracking длятого жеживогоPID;stop/merge толькосвоегоWPRinstance;ownedHost cleanup | trace/config/process/DB/temp receipts |

H6 totals:56workload jobs+1preflightGET=57public; **36IPC samples** (прежние35+1post-capture),3VMMap XML,2WPR snapshots,1ETL,1attempt. T12 иlateness1s H5 не ослабляются. Heap tracking можетзамедлитьсценарий: при нарушенииguard — incomplete, без расширенияwindow. Это заранее объявленный риск метода, не повод повторить автоматически.

Post-capture sample не добавляетpublicwork. Если междуt12 иконцомB/P2 изменились source starts/effects, появилисьactive/due/inbox или изменилсяfuture-work/retained vector, raw acquisition сохраняется, но comparison помечается contaminated/unknown. Нельзя выдавать этот интервал за тотже workload или перезапрашивать ещёснимок. Заранее известных background workers не выключать.

## Точные command forms — НЕ готовая execute-команда

Эти строки содержат placeholders. PID берётся толькоизновогоownedchild;старый53084 запрещён. Названиеinstance уникально:TravelHotelsH6-<runNonce>. Полныйsource/build/helper/WPAfreeze ещётребуется.

```text
wpr.exe -snapshotconfig heap -pid <OwnedPid> enable
wpr.exe -start HeapSnapshot.Verbose -filemode -recordtempto <OwnedTraceTemp> -instancename <UniqueInstance>
wpr.exe -singlesnapshot heap <OwnedPid> -instancename <UniqueInstance>   # A
wpr.exe -singlesnapshot heap <OwnedPid> -instancename <UniqueInstance>   # B
wpr.exe -snapshotconfig heap -pid <OwnedPid> disable
wpr.exe -stop <OwnedRun>/heap.etl -skipPdbGen -instancename <UniqueInstance>
```

Синтаксисподтверждёнлокальнымhelp; успешное физическоеисполнение/изоляцияinstance не заявляются. Параметр-instancename всегдапоследний. Registry name-mode,heaptracingconfig enable,boottrace,periodic snapshots,markerflush,disablepagingexecutive иглобальныйcancel запрещены. [WPR options](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/wpr-command-line-options).

## Safety, ownership и privacy

- Общийattempt≤600s; setup120s включаетattach/start(каждаякоманда≤15s),каждыйsnapshot/map≤30s. Cleanup120s:PIDdisable≤15,WPRstop/merge≤45,Hostcleanup≤60. При исчерпанииобщегосрокановыеизмеренияне начнутся;failure receiptbest-effortbounded.
- AvailableRAM≥4GiB,freeDisk≥5GiB,весьownedrun+trace-temp≤512MiB;проверкидоэффектов/каждуюсекундуожидания. Tracebuffers40MiBучитываютсякакobserveroverhead. Полныеdump/UST/PageHeap/forcedGC/cacheclearнеиспользуются.
- ПередкаждымPIDдействиемсверяютсяPID/starttime/owner/root/binary/parentmarker. НаPIDexit/reuseнеприменятьdisableкновомупроцессу;длястарогозафиксироватьexit,ownedETWsessionзакрытьотдельно.
- Cleanupне зависитотуспехаJSON/ETLwrites. Приошибкеstop разрешёнтолькоодинboundedcancel **своего**instance,еслиегоактивностьподтверждена. ЧастичныйETLможетоказатьсянедоступен;acquisitionостаётсяfailed. Никакогоcancelбезinstancename/удалениячужихsessions/всегоTEMP.
- PID-mode ограничиваетheaptrackingTarget. Но builtinprofileтакжепишетобщесистемныйProcessThread/Loader/CpuConfigcontext:таммогутбытьимена/пути/commandlinesдругихпроцессов. Этоотдельнораскрытаяповерхностьбудущегоразрешения, **не обещание target-only ETL**. RawETLостаётсялокально,непубликуется/неотправляется;аналитическиеэкспортыфильтруютсяпоownedPID/starttime.
- Current system state/read-only checks не изменялись. Вfuturepreflight непустыеIFEO flagsиличужойWPRrecording даютabort;ничегоне«чистить»дляготовности.

## Анализ без догадок о схеме

Доrun установить/зафиксироватьофициальныйWPA,проверитьнастоящиеversion/help/capability;невыдумыватьJSON/CSVschema. NativeETL остаётсяпервоисточником. Анализобычнымпользователем,неadmin. Автоматическаязагрузкасистемныхсимволоввыключена: сначалаmodule+offset/stackIP. Еслиименнехватает, отдельносогласоватьboundedMicrosoftpublic-symbolcache, неотправляяETL/privatePDB. [WPA](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/windows-performance-analyzer),[symbol settings](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/load-symbols-or-configure-symbol-paths).

Required analysis fields: snapshotIndex/timestamp/ProcessId/bitness; allocationAddress,heapHandle,Size,stackIP/module+offset/TraceUniqueStackId. SnapshotUniqueStackId не переноситьмеждуснимками; Size/ReferenceCount semantics сверитьсactualexport/официальнымAPI,немножитьвслепую. Snapshotidentity —Index+PID+bitness. ПерваяпроверкаETL:ровно2целевыхснимка,правильныйPID/starttime,нетevent/bufferloss/partialsnapshot,явнаядоляunknownstacks. Missing/unsupported не0.

Сопоставлять AllocationAddress с **новыми**VMMapP1/P2диапазонами,группироватьпоnativeHeapHandle/trace-stack иотдельносохранятьнесопоставленныеbytes. СтарыеH5адреса/rawID131073—толькогипотеза,нефильтрзаписи. Live-at-snapshot trackedSize/count сравнивать A→B; rawETL/table сохранены. Трассаодна,чтобыTraceUniqueStackIdимелодинаковыйscope.

Возможныевыводы:
1. Значимыйростoutstandingtrackedbytesсогласуетсясheapgrowth иуказываетнаразрешённыестеки→кандидаткомпонента-владельца;этоещёнеleakбезlifetimeexpectation.
2. Heapcommitрастёт,аtrackedliveмало→необъявлятьостатокfree:untrackedstartup/metadata/unsupported/loststackcoverageотдельно;при недостаточном покрытии вывод остаётся unknown.
3. Ростнеповторилсяилиинструментациясломалабюджеты→not-reproduced/incomplete;безавтоповтораилиthresholdwaiver.

## Что подготовлено и что требуется дальше

Подготовленыметод,read-onlytoolproof,scope/commands/budgets/analysiscontract. **Нет H6 executable controller/elevatedhelper/WPAinstallation/ETLqualification.** Следующийsource-срездолженреализоватьтолькозафиксированнуюsingle-runstate machineнаexistingH5helpers, testsfakeWPR/clock/processes,ownedPID/instancecleanup иsourcefreeze. Failure cases:wrongPID/hash/admin/analyzer→0start;enable/startambiguity;two snapshots exact;late/activephase→noB;stop/disable/writefailures;thirdsnapshot/retryforbidden;ETLloss/unknownschemaнеуспех.

Передбудущимruntimeнужны: WPA, source-ready H6packet, явноеразрешениеодногозапуска сelevatedcollector ираскрытымsystemmetadata. Никакаяпометкаprotocol-readyне заменяетэтиgates. H5v4остаётсяcomplete diagnostic;M1failed16/17,S-v2failed22/24,Lnot-run,план6/8.