# H5 — точечная диагностика private memory actual Host

2026-10-30. Подготовка по поручению владельца; **новый runtime не разрешён и не выполнялся**. Это отдельный diagnostic packet, не повтор S/L acceptance. Текущая стадия: protocol/tool/workload prepared; короткий исполнитель ещё должен пройти source preparation и тесты. Нельзя запускать существующий m2-load вместо него.

## Вопрос и основание

S-v2 полностью измерен:3152jobs,361samples,22/24gates; Lnot-run. Отказали RSS ceiling1GiB и strict5s stability(+2457.6B). До timed phase private296710144B, на0.132s537387008B, на9.037s1260204032B. Assemblies389→397, JIT methods46598→64811, managed на9s≈102MB,Gen2count66. Это локализует интересующий рост в первых запросах/наблюдениях, но не доказывает конкретного владельца.

Вопрос H5: **в какой категории committed memory и на каком этапе появляется основной прирост private**, а не «падает ли RSS после ожидания». Возможные ответы: CLR/managed/native heaps, runtime/loader/JIT, stack, other private data, mapped/image COW либо unclassified. VMMap category сама по себе не называет allocating call stack.

PrivateUsage — process commit charge; working set — resident pages. MEM_PRIVATE не равен всей private commit: modified image/mapped COW pages сохраняют свой mapped/image type. Estimated managed, last-GC committed и private usage нельзя просто вычитать и объявлять остаток native leak. [Microsoft counters](https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-process_memory_counters_ex), [VirtualQueryEx](https://learn.microsoft.com/en-us/windows/win32/api/memoryapi/nf-memoryapi-virtualqueryex).

## Выбранный метод и альтернативы

Выбран **portable VMMap3.4, три XML memory maps одного owned Host**, плюс existing BCL/process/source counters. VMMap разбивает committed memory иworking set по типам; snapshot экспортируется дляпоследующегоразбора. [Microsoft VMMap](https://learn.microsoft.com/en-us/sysinternals/downloads/vmmap).

Это сохраняет небольшой metadata-only evidence budget: полные .dmp,allocation tracing,UST/PageHeap,forcedGC,cache/fake clearing,новыйserver/VM не нужны в этом первом протоколе.

Существующий pinned dotnet-dump10.0.750501 остаётся для managed/SOS; native debugger он не заменяет. Проверен точный upstream MAddressCommand: maddress требует windbg/cdb service. Нельзя обещать native map командой dotnet-dump analyze maddress. [dotnet-dump](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-dump), [pinned source](https://github.com/dotnet/diagnostics/blob/v10.0.750501/src/Microsoft.Diagnostics.ExtensionCommands/MAddressCommand.cs).

WinDbg/CDB с !address/!heap полезен следующим шагом для неизвестной private/native heap категории, но локально не найден и сейчас не устанавливается. Собственный VirtualQueryEx/CLRMD profiler отклонён: он расширил бы source/interop поверхность до выяснения, достаточно ли стандартной карты.

## Один будущий процесс, три карты

Переиспользовать existing HotelsL1HostProcess/HotelsHostFixture сS-v2 configuration:Testing,2CPU affinity,serverGC/current runtime,owned PostgreSQL,fictional typed Duffel/HBX,те же38rows и420cache puts. Не менять keys/permissions/real suppliers. Dates иcatalog revision берутся изнового frozen packet до runtime approval; исходный S-v2 никогда не переписывается.

| Этап | Действие | Evidence |
|---|---|---|
| Setup≤120s | Same owned startup,identity checks,readonly preflight,prepareS | Ready PID/starttime/root/build/image/typed-handler proof;seed40Duffel/16HBXsteps;before/after seed/cache counters |
| P0≤30s | Снять первую VMMap XML сразу после prepare, до первого post-seed sample | Native map+parent process counters before/after+capture start/end |
| Control20s |20раз вызвать existing sample черезIPC,один раз в секунду;безpublic workload |20rawsamples;source starts/effects/capacity deltas;OS/private/RSS/JIT/assemblies |
| P1≤30s | Вторая карта после control,до begin/public jobs | Та же карта/counters;source counters послеmap |
| Warmup10s | begin и original S jobs с0≤Due<10 черезexistingdispatcher/execution helper |56jobs и fixed prerequisite IDs,полныйterminal ledger;sample0..12 |
| Settle2s | Новыхpublicjobs нет; bounded наблюдение завершения фактических запросов | Active sources/dueWork/pendingInbox;13 warmup/settle samples |
| P2≤30s | Третья карта послеwarmup+settle;не начинатьновую работу | Native map/counters,latest owned source retained vector |
| Cleanup≤60s | stop толькоowned child/tool/DB/temp,confirm exact identities | Acquisition/cleanup receipt,remaining/missing work,raw hashes |

Existing preflight дополнительно делает **один** owner GET/list доprepare и один pre-seed sample. Это раскрытыйsetup warmup, нечастьcontrol. Поэтому total public API jobs:1preflight+56prefix=57. Supplier setup calls56 учитываются отдельно, не выдаются заpublic throughput.

56prefix jobs:2search(Due0,6),1rates(1.2),1prepare(3),1create(4.5,HBX),1callback(8,Duffel),50status(Due0..9.8). Exact IDs/roles/dependencies экспортированы изv2 data/S/schedule.json вwarmup-jobs.json. Не добавлятьdegraded/burst/recovery profiles. Нет нового S/L retry.

ПослеP1, непосредственно передbegin, выполнить **один дополнительный boundary sample**. Он закрывает интервал самой картыP1: source starts/effects должны совпасть сpost-prepare baseline, runtimecapacity остаётся0. Итого ровно35 IPCsample commands:1preflight+20control+1boundary+13warmup/settle. Prepare возвращаетstage counters, но не добавляетsample. Parent OS counters до/после трёхкарт считаютсяотдельно.

Вcontrol childElapsed ещё0; каждое наблюдение имеетотдельныйparent monotonic phase timestamp. Нельзя трактоватьэтинуликакzero-duration.

Ровнонаwarmup t=12 требуется56terminal expected outcomes,active sources0,pendingInbox0,dueWork0. Еслипроверканепройдена, **P2 не снимать**: acquisitionincomplete, затемcancel/ownedcleanup. Не ждатьдольше,неотменятьзапросыпередснимком,нефорсироватьquiescence ине делатьчетвёртуюкарту. Будущаяscheduledwork,котораяещёнедолжнавыполняться, учитываетсяотдельнои не обнуляется.

Control samples доbegin не должны расходоватьruntime source permits. Если исходящие provider starts/effects меняются безpublicjobs, сохранить background-work observation и остановить переход кwarmup: это уже другая причинная ветка, которую нельзя скрыть.

Не читать final facts, требующиеElapsed≥360; их отсутствие здесь задано протоколом. Короткий child заканчивается completed=false/expected exit1 после штатногоstop. Diagnostic acquisitionComplete отдельно отM2 completed/passed; не обходить360sguard радиgreen.

## Сопоставление и допустимые выводы

Сохранить processPrivate/processRSS/threads/handles иnative map totals вкаждойточке; committed,reserved,working set,privateWS иmapped/image поля держать раздельно. Полезны top allocation bases/ranges,allocation/protection/type,module labels,не содержимое страниц.

| Наблюдение | Вывод |
|---|---|
| Прирост преимущественноP0→P1 | Связан по времени сobserver/background/cold infrastructure; нельзя приписатьpublicrequests |
| Прирост преимущественноP1→P2 | Связан сfirst-request warmup послеконтроля; JIT/loader/handler correlation,не доказанныйallocator |
| РастётManagedHeap/nativeCLR category | Кандидат CLR owner; next managed/native analysis поконкретнымranges |
| РастётHeap/PrivateData при небольшомmanaged | Native/custom allocator candidate; категории/addresses,не automatic leak |
| Растётmapped/image privateWS | Разделить resident/COW иcommit;не приписывать всюimage size private |
| Карты/CLR category unsupported либо эффектне повторился | Unknown/not-reproduced;неподменятьcategory zero ине повторятьавтоматически |

Порядокcontrol-before-workload сам меняетcold timing. Поэтому это temporal localization/категоризация, **не causal A/B** ипнеповторнаяприёмка. VMMap можетприостанавливать/читатьстраницы: bracketed counters иdurations обязательны, этиRSS/latency не сравниваются какновыеS/Lgates.

Еслитип/owner остаётсяunknown, следующий эксперимент согласуется отдельно. Вэтомпакете нетавтоматическогоstack tracing,снятияdump,установкиsymbols иливторогоHost.

## Неизменные пороги, безопасность, privacy

Acceptance values S/L остаются byte-for-byte прежними:RSS≤1073741824B,baseline+134217728B идвеstrictnon-growth comparisons,исходныеlatency/success/capacitygates. H5их невычисляет инеменяет22/24v2 или16/17M1.

Сохраняются source safety floors:availableRAM≥4GiB,freeDisk≥5GiB,evidence≤512MiB. H5total≤600s (уже прежнего1200s),setup≤120s,каждыйmap≤30s,cleanup≤60s. Timeout/overflow/неверныйPID/sourcehash/transport/collectorerror прекращаютработу; missing maps остаютсяmissing. Не расширятьbudget послеошибки.

Runtime output:новыйуникальный diagnostic-host-private-v1/run-01;attemptcreate-only доэффектов. ТолькоPID+starttime+root+binary,полученныеотсобственногоchild; числовой PIDизстарогоreceipt запрещён. Tool ProcessStartInfo hidden,файлвыхода внутриownedroot;никакогоattach поимени,glob/prune/kill-all.

Карты остаются локально. Они могут содержатьадреса,module/mapped filenames иlocalpaths,которые нельзяпубликоватьбезпросмотра. Нетпроизвольныхstrings/dumpobj/env/credential/billing/certstore reads,uploads илиrealuserdata. Reader XML запретитDTD/externalentities; unsupported schema не дастcomplete=true.

## Подготовленные инструменты и точные команды

Portablefiles скачаны толькоизофициальногоURL;неустановлены инеисполнялись:
`https://download.sysinternals.com/files/VMMap.zip`.
ZipSHA256 `71C9BB73F1BA63F69096CC5AB11AAAD4F1969B060931F19C97F7CA6458CA1C1E`.
x64exe3.4SHA256 `0B3731C524E6BA716F15087D85EAE7E6225B6B51D4AE2FA6C142FF1523F57046`.
ЭтоURL+hash provenance; отдельная Authenticode trust validation незаявляется.

Синтаксисподтверждёнembeddedhelpбеззапускаexe:
`vmmap64.exe -p <owned-live-PID> <owned-run/P0.xml>`,аналогичноP1/P2.
Никакого-accepteula. HKCU VMMap EulaAccepted неустановлен; лицензиюMicrosoft должен принятьсампользователь передпервымисполнением. [Поставляемая EULA](../../../TestResults/hotels-m2-memory-load-acceptance/diagnostic-host-private-v1/tools/vmmap/Eula.txt).

## Короткий source-срез перед исполняемым freeze

Существующий m2-load всегдаисполняет360s; m2-host имеетframed IPC,аfinalfacts требует360s. ГотовойкомандыH5нет. **Этотпакетprotocol-ready,неexecution-ready**; запускатьстаруюкомандукакзаменунельзя.

Нужен один явныйdiagnostic entrypoint наexistingfixture:
1. Defaultverify-only:plan/schema,источник/build/runtime/tool hashes,dates/image/path/ownership/ресурсы,наличиелицензии без еёпринятия;незапускатьпроцессы.
2. Явныйexecute сatomicattempt;reuseHostProcess.StartAsync/PreflightAsync/prepare/sample/begin/stop,HotelsM2LoadDispatch/Execution/Timing. Новыйgeneralengineненужен.
3. ТриVMMap processes тольконаownedPID,bracketedmetadata,30sdeadline,ограниченныеoutputs/XMLsize,kill толькоownedcollector.
4. Ошибкиclosed/readiness/cleanup не даютacquisitionComplete;сохранить56jobdenominator,35 IPC samples:1preflight+20control+1post-P1boundary+13warmup/settle и3capturestates.
5. Source regressions:prefix56/exactbody/dependencies;delay/stop;wrongPID/hash/EULAfalse→0launch;collector timeout/badXML/missingfile/partialstdout;cleanup/error/deadline;privatecommit≠WS. Затемfullpermittedstandalone иодинreview.
6. Freezeновогоsource/build/profile/commands ипредъявитьдоотдельногоruntimeapproval.

Эти implementation пункты ещёневыполнены. Никакая установка/acceptEULA/sourceimplementation/runtime неследуетизпрочтенияэтогодокумента. Текущийзапросподготовки фиксируетметод,inputs иследующийограниченныйсрез;executionapproval отсутствует.



## Проверка реализуемости

Независимый builder check выявил два существенных вопроса. Оба решены до handoff: зафиксированы35sample calls сpost-P1boundary и отказ отP2 приnon-quiescent t=12. Выбор abort сделан исполнителем: он может оставитьнеполнуюдиагностику, но не подменитпамятьожиданием/отменой. Других существенныхвопросов reviewer неоставил. Review неисполнялHost/VMMap и неявляетсяruntimeproof.
