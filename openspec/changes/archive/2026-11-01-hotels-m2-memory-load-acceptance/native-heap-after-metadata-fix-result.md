# H6 v3 после metadata fix — заметный эффект, неполный diagnostic run

2026-10-31. Выполнен один фактический H6 v3 на исправленной сборке. **На сопоставимой части нагрузки private memory снизилась примерно на71%; новый прирост от boundary доt12 — только10.23MiB. Но полный H6 не принят:** наt12 осталась одна due-задача, поэтомуB/P2 не выполнялись; дополнительно старый WPR не смог сохранить итоговую ETL. Пороги не менялись, повторного запуска не было. План6/8, S/L acceptance остаётся открытой.

## Что измерено

| Точка | H6v2 до исправления | H6v3 после исправления | Граница сравнения |
|---|---:|---:|---|
| t8 private memory |882.21MiB|253.34MiB|наблюдаемые business/source/retained counters совпали; −71.28% |
| t12 private memory |1231.26MiB|263.98MiB|конечное состояние не совпало из-за одной due-задачи; не acceptance comparison |
| v3 boundary→t12 | — |+10.23MiB|266080256→276803584B |
| v3 peak sampled private | — |266.13MiB|по13warmup samples |

Счётчики совпали вточкахt0…t8:обаисточника,starts/completions/active/effects counts,admission/releases,retained counts,inbox/due/future work. Cache payload отличается на несколько bytes, поэтому это не утверждение о byte-identical полном состоянии. В v3 была естественнаяGen2collection наt2 (66→67); вbaselineGen2оставалась66. ForcedGC не выполнялась; разбор сохраняет этот фактор, а не приписывает каждую разницу только Dispose.

Этот результат поддерживает эффект scoped metadata ownership на общей части workload. Он не доказывает полную native-allocation A/B картину, working-set recovery360s, долгосрочную leak absence или M2capacity.

## Выполнение и корректная остановка

- Driver69324,helper40260,Host25684;20:32:18.411–20:33:41.231UTC (23:32:18–23:33:41МСК),82.8188388s,forced=false.
-56/56public workload responses плюсpreflightGET; все56имеютHTTP200,нетNotRun/cancelled/unfinished. Public ledger complete.
-35IPC, P0/P1сохранены и повторно разобраны compiledreader; командаheap snapshotAуспешна. B/P2/postcapture не выполнялись.
- Наt12 active sources0,inbox0,ноdueWork1/futureWork16;baselineбылdue0/future17. Duffelstarts43 вместо44:одинrecovery read кгранице не наблюдался. Hotelbedsstarts22 совпали. Oldest due lag≈3.54s.
- Guard CheckQuiescent корректно завершил протокол: failure=warmup:InvalidDataException. Не ожидали сверхt12,не выполнялидополнительныхrequests и не ослаблялиусловия.
- Причина задержки конкретной фоновой задачи по сохранённым counters не установлена. Одна такая задержка не объявляется product regression и не заменяется предположением о таймере.

AcquisitionComplete=false. CleanupComplete=true относится только к освобождению owned ресурсов, не к успешному сбору. Driver exit−532462766 — явный отказ H6Runtime; Host exit1 ожидаем для короткого diagnostic,не360sacceptance.

## Отдельный отказ WPR

Во время scoped stop/merge WPR10.0.19041.6093 вернул0x80010106/RPC_E_CHANGED_MODE: «Cannot change thread mode after it is set». Это [документированный Microsoft отказ WPR-stop](https://devblogs.microsoft.com/performance-diagnostics/wpr-start-and-stop-commands/); Microsoft указывает более новый WPT recorder как способ устранения этого класса ошибок. Это не disk-resource stop; запас диска сохранялся.

Tracking иобаownedETWsessionsзакрыты. Передstop унихevents/logBuffers/realtimeBuffersLost=0,ноэто не компенсируетотсутствиеmergedETL. Сохранённый262144B WPR internal fragment содержит4служебныхevents и0HeapSnapshot events. Первоначальный WindowsEventAPI read не принял299-character path; byte-identical copy по188-character path успешно прочитана. Это только offline copy, не новая запись.

Итоговыйheap.etlотсутствует, поэтому allocation bytes afterfix/числоцелевыхсохранившихсяsnapshots/stackdelta не подставляются нулями. Валидных target snapshots в сохранённомfragment нет;nativeA/Banalysis unavailable. A command success отличается от сохранённого пригодного heap snapshot.

WPRнеперезапускался, Windowsнерестартовала, recording/cancelчужихsessions, сменыCOM/registrysettings и установкиновогоrecorder не было.

## Подготовка места и сохранность

Передrun диск был близок к5GiBfloor. ПрозрачнымNTFScompression сжаты15старых собственныхdiagnosticfiles; ниодного удаления/перемещения, SHA256 каждого до/после совпал. FreeC:5432295424→5637890048B, передruntime≈5.25GiB. Targetbuild/runtimefiles иcurrentrecorder не сжимались; затронутыеofflineevidence/toolfiles перечислены вsource-h6-v3/storage-compression.json. Порог5GiB не снижен.

Новыйpacket diagnostic-h6-native-heap-v3:
-2012source/input,998build,335runtime+dotnethost,25inputs.
-ManifestE09F9B966C280420423B792A8097CAD27297604B4961AE410AFA3ECB84D3D28E.
-FreezeA9AF5F4AFB64C92381BED7A2B4F756F45A945EB849989B46BE831427A16B539E.
-Archive3013entries/56048079B,SHA9C57E3C3D1A0B9A31D7BFEDBFF692F4248DC473AD8955187C493CEC21A5BABA6.

Проверены272priorraw,73новыхraw,66runtimehashes,2012source и998build. BaselineETLSHA3E364944CFC02FF6ECDEF9BF8B8B05D764B327F7E15886AE3EC49E4F2D056EAB неизменен. Данныеиисходники не корректировались послеrun.

Independently confirmed: driver/helper/Host завершены, container c43ed5d5f93261949c0e821657e38ef52eee933217809e5b2e6182432d9c6f10 отсутствует,ownHostTemp/traceTempотсутствуют,WPR not recording,dotnetIFEOотсутствует. Никаких оконWPA илиUIautomation.

## Evidence и следующий шаг

Вpacket/analysis-v1: memory-comparison.json (все13парточек ивекторы),native-map-replay.json,analysis-receipt.json,cleanup.json,integrity.json,wpr-fragment-analysis.json,wpr-internal-events.xml.txt. Исходныеreceipts attempt-1.json/run-01/trace не переписаны.

**Source-fix имеет положительный измеренный эффект на общей части сценария; полный H6 остаётся incomplete.** M1failed16/17,S-v2failed22/24,Lnot-run,3.2/3.3pending — безwaiver.

Рациональный следующий шаг — подготовка независимой S/Lприёмки на исправленнойсборке; для неё WPRне требуется иестьисходныйдлинныйsettle/recoveryконтракт. Еслинуженповторименноallocation-диагностики,сначалаподготовить/проверитьновыйофициальныйrecorder; не повторятьвслепуюстарыйWPR и не расширятьt12заднимчислом. Вэтомturn дополнительныеHost/loadrunsне выполнялись.