# H6 v2 — выполнен; рост связан с DLL-размерными native allocations

2026-10-31. Один фактический H6 эксперимент завершён успешно. **Tracked outstanding allocations объясняют99.72% прироста native heap. Две allocation-цепочки дают920.40MiB прироста и имеют одинаковые наборы размеров блоков, совпадающих с DLL из frozen пакета. Основной кандидат — повторное создание metadata references в JasperFx/Roslyn runtime compilation; точные имена двух managed методов пока не доказаны.** Product patch не внесён. S/L acceptance остаётся открытой, план6/8.

Первый CLI вызов был отклонён до runtime из-за ошибки команды ассистента; он сохранён в [отдельном отчёте](native-heap-diagnostic-v2-invocation-result.md). После прямого указания владельца исправлять собственные ошибки без повторного согласования выполнен corrected invocation02. Это второй CLI вызов, но **первый и единственный actual Host/WPR experiment**. Старые логи не перезаписаны.

## Выполнение

-31октября2026,15:54:44.077–15:56:09.416UTC (18:54:44–18:56:09МСК),85.3407513s; driver62656 exit0,forced=false. Internal protocol82.984675s.
- Helper47416 elevated; Host30192 normal user. Start identity ticks639290588902026254, parent62656, root/build/source hashes проверялись исполнителем.
-56/56Response,57public requests включаяpreflight,36/36IPC,3VMMap,2WPRsnapshots,1ETL. Failed/cancelled/not-run/unfinished0. Create26 сохранил реальный ReservedAwaitingDetails; это не переписано в Confirmed.
- Control source/effects/admission неизменны; наt12 иpostcapture active/due/inbox0,futureWork17. Postcapture comparison=true,changed=[]. Максимальная lateness базовых samples≈0.103s.
- Tracing enable/start/A/B/disable/stop:exit0. Scoped instance TravelHotelsH6-85ebd8096ba44a29a536166f30959c39. Trace receipt:trackingClosed/sessionClosed=true,snapshots2,etlSaved=true,errors=[].
- Host cleanup подтверждён. Его exit1 ожидаем для short diagnostic: completed=false запрещает выдавать его за360s M2 acceptance. Driver exit0 означает успешный диагностический протокол.

Owned PostgreSQL container64b23aef4fc139a15a9207b047f1f4d14335afa70b9390221a9457f0b7a92e64 отсутствует; Host/driver/helper завершены, own host-temp иprotected trace-temp отсутствуют;WPR not recording,dotnetIFEO отсутствует. Проверены independently read-only. Окно WPA, открытое ассистентом, закрыто; после просьбы владельца экран больше не управлялся, оставшийся разбор выполнялся фоновыми процессами с пониженным приоритетом.

## Память

| Наблюдение | До нагрузки / A | После / B | Прирост |
|---|---:|---:|---:|
| OS private, P1/P2 brackets |288395264B|1293869056B|958.89MiB|
| VMMap Heap (Private Data) | — | — |935.72MiB|
| Outstanding tracked allocation bytes |59367977B /56.62MiB|1037839395B /989.76MiB|933.14MiB|
| Количество tracked allocations |72774|133948|61174|

Все allocation starts и полные allocation spans попали в соответствующие новые VMMap heap ranges:unmapped0,cross-range0. Основной native heap handle0x24EBED40000 сопоставился по адресам с VMMaprawId131073; вторичный0x24EBED30000 — сrawId131075. Это фактическое сопоставление в новом Host, а не декодирование rawId как heap handle и не перенос адресов H5.

Tracked delta978471418B / heap-private delta981176320B=99.7243%. Значит, почти весь наблюдаемый прирост соответствует outstanding allocations в моментB; объяснение только свободными/резервными страницами heap для этого прироста не подходит. Startup allocations до tracking, allocator metadata/fragmentation и влияние измерителя остаются отдельными; commit-minus-tracked не объявляется free.

Наt12 managed estimate111487680B, last-GCcommit187969536B,Gen2=66,JITcompiledMethods72402,assemblies397. Cache128entries/1667127B и38reservation rows не объясняют почти1GiB native growth. ForcedGC/cacheclear не выполнялись.

## Две доминирующие цепочки

Trace stack241: прирост482552096B/1208allocations; stack2120: тот же прирост482552096B/1208allocations. Вместе965104192B=920.3951MiB:98.6339% tracked delta и98.3620% heap-private delta.

-241:0x00007FFE0F037F0E → KernelBase.dll+0x31BCD → ntdll.dll+0x2B621.
-2120:0x00007FFE1070864E → те же системные кадры.

Локальная PE export table располагает эти системные RVA после LocalAlloc+0x6D и RtlAllocateHeap+0xC81; PDB не загружались. Первый кадр каждой цепочки находится в безымянном executable Shareable mapping (PAGE_EXECUTE_READ), отсутствует в image list. Это совместимо с JIT-кодом, но **прямого method/module mapping этих двух IP нет**. Непустые стеки есть у всех allocations; это не означает, что все вызывающие методы разрешены. ВB объём с неразрешённым ближайшим вызывающим кодом —965694278B.

У двух доминирующих групп совпали гистограммы размеров:1208добавленных блоков в каждой,200разных размеров. Все их дополнительные bytes имеют точное совпадение с размером одного или нескольких файлов frozen build/runtime. Примеры на группу:4×16017232B(System.Private.CoreLib.dll),4×7788368B(System.Private.Xml.dll),4×6839608B(Microsoft.CodeAnalysis.CSharp.dll),4×3059000B(Microsoft.CodeAnalysis.dll). Некоторые DLL имеют одинаковый размер, поэтому совпадение размеров не доказывает содержимое каждого блока или ровно четыре копии каждого имениDLL; кратности отдельных размеров выше4 сохранены вraw-derived report.

## Кандидат компонента и предел вывода

Frozen JasperFx.RuntimeCompiler5.0.0 бинарно совпадает с локальнымNuGet package. Его nuspec указывает commit4f08f1dfeab987660bc392e3efa4ad4a42d9a3d8. В [коде именно этого commit](https://github.com/JasperFx/jasperfx/blob/4f08f1dfeab987660bc392e3efa4ad4a42d9a3d8/src/JasperFx.RuntimeCompiler/AssemblyGenerator.cs) ReferenceAssembly создаёт MetadataReference.CreateFromFile и рекурсивно добавляет зависимости; Compile создаёт ещё один AssemblyGenerator и заново добавляет наборы references.

**Вывод-инференс:** повторное чтение/удержание образов сборок при runtime compilation хорошо согласуется с измеренными DLL-размерными LocalAlloc группами. Это гораздо более конкретный кандидат, чем общий JIT/GC или hotel cache. Но H6 не связывает anonymous IP с именами методов, не читает содержимое выделенных блоков и не доказывает неправильный lifetime references. Нельзя называть это окончательно доказанной утечкой JasperFx либо уже исправленным дефектом.

Следующий bounded source шаг: проследить ownership/lifetime и повторное создание MetadataReference в используемом compiler path; подготовить минимальный тест/исправление только при подтверждении. Не начинать новый общий Host/S/L run и не менять пороги ради проверки предположения.

## Качество ETL и анализатор

ETL40894464B,SHA2563E364944CFC02FF6ECDEF9BF8B8B05D764B327F7E15886AE3EC49E4F2D056EAB. Tracerpt:39buffers,248106events,eventsLost0,44s; два owned WPR sessions передstop:events/logBuffers/realtimeBuffers lost0.

Windows Event API прочитал465HeapSnapshot chunk events и4cumulative-statistics events дляPID30192. Instance1:185chunks/3006804payload bytes;instance2:280chunks/4570712bytes. Sequences непрерывны с0 длякаждогоinstance, фактическая длинаpayload соответствуетBufferLen, обе промежуточные и обе финальныеTotalData совпали с cumulative sums. Binary payload самостоятельно не декодировался.

WPA UI оказался недоступен для надёжного capture, а WPAExporter с подтверждёнными table/column metadata не выдал native table; отсутствие CSV не трактовалось как пустая куча. Причина поведения экспортёра не установлена. Установленный WPA/его source packet не изменялся.

Рабочий fallback — официальный [Microsoft.Windows.EventTracing.Processing.All1.12.10](https://www.nuget.org/packages/Microsoft.Windows.EventTracing.Processing.All/1.12.10), отдельный offline reader под TestResults. Это стандартный Microsoft decoder, не custom ETL parser; product dependencies не менялись. AllowLostEvents=false/AllowTimeInversion=false, parsing завершён безошибок. Ровно2x64snapshots,ownedPID30192,parent62656. Два offline прохода дали byte-identical exports allocations;Size суммируется один раз,SnapshotReferenceCount не умножается.

Timestamp API IsPartial относится к точности контекста timestamp доnanoseconds (XML docs ITraceTimestampContext.IsPartial), а не к incomplete heap payload. Snapshot UTCtimestamps совпадают с команднымиbrackets. ETL process.CreateTime=null, потому что tracking начался после запускаHost; StartUtcTicks проверен runtime identity/held process и связан сPID/parent/timestamp, но exact process start не заявляется самостоятельно извлечённым изETL.

LoadSymbolsAsync не вызывался, PDB/symbol server downloads отсутствовали. UseSymbols во втором offline проходе запросил только уже имеющиеся вETL данные; два неизвестных call sites не разрешились. Никаких полных dumps, uploads, новых recordings или provider/modelcalls.

## Evidence и неизменность

Root:TestResults/hotels-m2-memory-load-acceptance/diagnostic-h6-native-heap-v2. Runtime:run-01 иinvocation-02. Разбор:analysis-runtime-v1.

- memory-analysis.json: compiled reader replay трёхVMMap, categories, rawId/ranges, sample timeline.
- decoded-v1/summary.json,allocations-1/2.jsonl,stacks-1/2.json,images-1/2.json: официальный export; decoded-v2 подтверждает тежеallocations. В исходном exploratory summary processStartMatches=false получен сравнением отсутствующего CreateTime; канонический analysis-receipt.json сохраняет etlStartMatch=null (unknown), а не доказанное несовпадение.
- allocation-attribution.json,mapping-and-coverage.json,allocation-size-signatures.json: суммы/адреса/гистограммы; module grouping впервом exploratory report не заменяет точное правило nearest caller вmapping-and-coverage.
- heap-stats-events.xml.txt,chunk-completeness.json,etl-summary.txt,etl-report.xml: raw-derived качество/полнота.
- compiler-source-evidence.json,upstream-AssemblyGenerator-4f08f1d.cs: exact-version upstream evidence играница inference.
- workload-accounting.json,cleanup.json,integrity.json: accounting,independent cleanup,hashes.

Сверены78raw files текущегоactualrun,72внутреннихruntime hashes,5файлов первогоCLIотказа и189priorraw.2002source/input,998build,335runtime+dotnethost неизменны. Freeze0E3DCEF29C5DF9DBCA575C88A567477E528A8810BC14B21E0B9DA805FD0C428B иsource/build archive54AC52CE7CA6784EF7A294025E679953FB57C479AE898A1B14370E074F6DD713 сохранены.

Immutable capture receipt сохраняет analysisComplete=false — он написан до offline анализа. Результат настоящего разбора записан отдельно; исторический receipt не переписан. Source/runtime acquisition/analysis/causal fix/M2load/target acceptance остаются разными стадиями. M1failed16/17,S-v2failed22/24,Lnot-run,6/8taskstatus безwaiver. Stage/commit/push/merge не выполнялись.