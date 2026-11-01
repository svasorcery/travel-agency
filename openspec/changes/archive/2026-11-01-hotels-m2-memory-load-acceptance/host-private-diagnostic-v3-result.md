# H5 v3 — одна попытка, две карты и частичный warmup

2026-10-31. Владелец явно разрешил H5 v3 один раз, stop-on-error/no retries. **Попытка выполнена один раз и остановлена на4.875s warmup из-за неверного ожидания create outcome в H5. P0/P1 успешно получены и разобраны; P2 отсутствует.**

Полезный результат: в20s observer-only control большой рост private не воспроизводится; после перехода begin/public work sampled private вырос с270.79 до728.68MiB за первые4s. Это локализует этап, но без P2 не устанавливает категорию/allocator роста и не доказывает leak.

## Исполнение и остановка

| Факт | Результат |
|---|---|
| Время |09:48:54.107–09:50:04.589UTC;12:48:54–12:50:04МСК|
| Весь вызов / attempt |70.481s /67.168s|
| Driver / Host |4564 /61232|
| Exit / forced stop |−532462766 /false|
| Setup |S;38rows;40Duffel+16HBXseedstarts;420cacheputs;runtime admission unspent|
| Raw maps / accepted maps |P0,P1;2/3;reader errors0|
| IPC |27/35:1preflight+20control+1boundary+5warmup;27attempted/27observed|
| Public workload |29responses/56planned,27NotRun; отдельно1readonly preflightGET|
| Early starts / unfinished |0 /0|
| Terminal receipt |acquisitionComplete=false,cleanupComplete=true,complete=false|
| Finalization errors |empty;source/build/runtime/tool verification passed|

Ответили1search,1rates,1prepare,1create и25status; не запущены второйsearch,callback и25status. Наcreate26(due4.5) полученHTTP200/ReservedAwaitingDetails, sourcehotelbeds, PurchaseAccepted=true, DriverFailure=null. Response завершён4.8741991s; H5 record callback объявил InvalidDataException на4.87465s. Нагрузка остановилась, ledger сохранён и закрыт; повторов/дополнительных запросов не было.

**Ошибка в моём H5-предикате**, а не доказанный дефект продукта: HotelsH5Controller.Expected требует немедленного Outcome==Confirmed. Но HotelM2LoadControl.ReturnReferenceOnly намеренно возвращает Hotelbeds reference-only дляэтойпокупки, а [контракт](../../../specs/hotel-reservation-booking/spec.md) допускает получение недостающих деталей ограниченными последующими reads. Такой же job26 в прежнем S-v2 уже вернул ReservedAwaitingDetails/PurchaseAccepted=true. Этот ответ был доступен до H5; мой набор тестов не проверил его против H5 predicate. Результат сопоставления сохранён в analysis-v1/purchase-contract-mismatch.json.

Допустимость промежуточного состояния не превращает его в Confirmed и не закрывает purchase/load acceptance. Текущее отсутствие P2 нельзя компенсировать ожиданием, отменой или ещё одной картой после остановки. Исполнитель не правился во время/после попытки.

## P0 → P1: observer-only control

P0 после setup, capture1.352s; P1 после20sконтроля, capture1.131s. ОбаXML повторно прочитаны тем же frozen reader offline; totals совпали с capture receipts. Все21control/boundaryнаблюдение сохранили source starts40/16, effects и admission; active sources0, dueWork0, pendingInbox0. Public jobs ещё не выполнялись.

| Показатель | P0 | P1 |
|---|---:|---:|
| OS private до карты,B |286826496|283938816|
| OS private,MiB |273.54|270.79|
| OS RSS до карты,B |399974400|402685952|
| XML top PrivateBytes,B |279154688|276180992|
| XML Commit,B |715563008|717152256|
| XML WorkingSet,B |399896576|402116608|
| Top/all/leaf regions |2422/7915/6110|2462/8097/6254|
| Parent private mismatches |284|284|

Изменения категорий XML(top regions, дети не прибавляются повторно):

| Категория | Δ PrivateBytes | Δ Commit | Δ WorkingSet |
|---|---:|---:|---:|
| Heap (Private Data) |−2945024|−2945024|−2863104|
| Private Data |+57344|+57344|0|
| Thread Stack |−86016|−86016|−73728|
| Shareable |0|+4562944|+4673536|
| Image (ASLR) |0|0|+483328|
| Остальные |0|0|0|

Итого topPrivate уменьшился2973696B(2.84MiB); Commit вырос1589248B(1.52MiB), главным образом вShareable. Эти разные показатели не складываются в «private leak». Определения scope: [VMMap](https://learn.microsoft.com/en-us/sysinternals/downloads/vmmap), [Windows PrivateUsage](https://learn.microsoft.com/en-us/windows/win32/api/psapi/ns-psapi-process_memory_counters_ex). Parent/leafPrivate различаются: P0leaf280735744B,P1leaf277762048B; обе величины сохранены, не уравнены сOScounter.

В control Gen2count64 не менялся, assemblies389; JITmethods вырос44698→49295, managed estimate70.6→79.2MB. Несмотря на это, private не выросла. Сам по себе рост JIT counter не доказывает владельца private memory.

## После begin: первые запросы

Таблица использует плановую секунду sample; фактическое чтение занимает время. Максимальное завершение относительно due0.633s, в пределах1s. OS counters взяты parent после IPC, а не выданы за одновременный native snapshot.

| Плановый sample | Private,MiB | RSS,MiB | Managed estimate,MiB | Last-GC commit,MiB |
|---|---:|---:|---:|---:|
| Boundary доbegin |270.79|384.05|75.92|158.92|
| 0 |500.62|615.32|86.50|158.92|
| 1 |501.28|617.93|117.09|158.92|
| 2 |626.49|745.03|135.92|158.92|
| 3 |728.66|856.23|138.06|158.92|
| 4 |728.68|857.82|81.57|169.95|

Boundary→последнийsample: private+480137216B(457.89MiB), RSS+496783360B. Gen2count остался64; JIT49319→59586, assemblies389→393. Cache payload1488768→1580367B, source starts40/16→42/19 кsample4; create26 случился позже этого sample. На первом sample private уже выросла при ещё прежних source starts40/16.

Следовательно, наблюдение связано по времени с переходом begin/first-request execution и не требует большого накопления новых supplier результатов. Нельзя приписать его одной операции, runtime subsystem или allocator: begin активирует работу, несколько действий пересекаются, а control-before-workload меняетcold timing. Это не causal A/B.

Managed estimate и last-GC commit не являются полным объяснением текущего OSprivate; вычитание их из private не доказывает native leak. P2 не получена, поэтому неизвестно, куда относятся дополнительные~458MiB. Прежние~1.2GiB S-v2 здесь полностью не воспроизведены: измерения закончились раньше, при~729MiB. Это не успешная M2 memory acceptance и не отсутствие проблемы.

## Предыдущие исправления и cleanup

Ошибки v2 XML/environment в этой попытке не повторились: P0/P1 parsed, source inventory/hash verification прошло, буквальная папка %SystemDrive% вworktree не появилась. Это runtime evidence именно этих путей; весь H5 остаётся incomplete из-за другого predicate.

Host61232 иdriver4564 отсутствуют. Owned container `bcc570a432af5bef12a6909384352fc88399f52d8691b57d6dee83f12600233b` отсутствует поDockerread(exit0), временныйкаталог `travel-hotels-host-7bbb1aa845df4798bd24d9990d467b9f` отсутствует. CollectorPID не записан текущимhelper; отдельно проверено отсутствиеvmmap64 сexacttoolpath иcreationtime внутри окна попытки.

Вownedrun-01/SOS сохранены два вспомогательныхфайлаVMMap, также включённые вrecursiveintegrity. SOSdeps.zip789676B побайтно найден внутри pinnedvmmap64.exe наoffset744736; отдельного скачивания этогоZIP или запускаdotnet-dump/SOSanalysis не выполнялось. Папкаevidence остаётся локальной;supportfiles не удалялись.

47rawartifacts этой попытки зафиксированы;39исходныхruntimehashes совпали. Все85предыдущихraw(M1/v5/S-Lv1/v2/H5v2) сохранилиSHA256. Source/build/data/thresholds не менялись; нет forcedGC/cacheclear/дополнительногоexperimenta/real supplier/paidmodel/publication.

| Native map | SHA256 |
|---|---|
| P0.xml |38CA7A861676002FA397AF8600DB1A1B82295CF39FFCAA40FAA33FE65DA22B61|
| P1.xml |27848D5E518A81A80B83B6483879E843543BFC99B8CA7D9D9DF6AB4DA6592625|

Evidence: `TestResults/hotels-m2-memory-load-acceptance/diagnostic-host-private-v3/`. Analysis: `analysis-v1/memory-analysis.json`, `analyze-memory.ps1`, `purchase-contract-mismatch.json`, `collector-support-files.json`, `cleanup-verification.json`, `raw-integrity.json`, `integrity-check.json`. Исходный failed attempt остаётся immutable.

## Следующий шаг

Предлагается source-исправление H5 purchase predicate с replay реальных ReservedAwaitingDetails/Confirmed/Unknown/ошибочныхsource outcomes и проверкой полного producer-to-consumer пути, сохраняя source binding, elapsed12,quiescence иfuture-work distinctions. Нельзя просто считатьлюбойHTTP200успехом или переноситьдиагностическийкритерий вS/Lacceptance. Новыйпакет/запуск требуютотдельногоразрешения; сейчаскод не менялся и retry не выполнялся.

M1 failed16/17; S-v2 failed22/24; Lnot-run. Общийплан6/8,3.2/3.3pending. Гипотеза о конкретном владельце роста памяти остаётся unknown.