# Hotels M2: итоговая локальная нагрузочная приёмка

2026-11-01. **S и L прошли по23/23 обязательных проверок в одном полном S/L v7, acceptanceVersion5.** Независимый пересчёт подтверждает результат. Все6304исхода запросов и722замера сохранены, временные процессы/базы очищены. Приёмка ограничена описанным локальным сценарием с вымышленными ответами Duffel и Hotelbeds.

Версия5 применяет явное решение владельца: нулевой рост среднего RSS — диагностика; обязательны RSS<=1ГиБ, final<=baseline+128МиБ и остальные проверки. Положительный тренд S осталсяfalse в диагностике. Это не доказательство отсутствия утечки и не повторная классификация историческихfailedрезультатов.

## Выполнение

Одна попытка,15:09:11.327–15:22:59.543UTC (18:09–18:22 Москва),828.216s —13мин48сек. Innercampaign823.622s<1200s;exit0,forcedStop=false,cancelled=false,finalizationErrors=[].

| Проверка | S | L |
|---|---:|---:|
| Обязательные gates |23/23|23/23|
| Original / M2 families |13/13 +10/10|13/13 +10/10|
| Planned / observed |3152/3152|3152/3152|
| Response / Cancelled / NotDispatched |3141 /1 /10|3141 /1 /10|
| FailedBeforeResponse / NotRun / Unfinished |0 /0 /0|0 /0 /0|
| Samples |361/361|361/361|
| First / last actual sample,s |0.1233 /360.0310|0.0912 /360.0325|
| Max parent sample lateness,s |0.4611|0.3080|
| Evidence complete |true|true|

Полный denominator:6282Response +2Cancelled +20NotDispatched=6304. Недостигнутые prerequisites остаются неуспехами, замещающих покупок и повторов не было. Ранних стартов нет. S завершил owncleanup перед началом L.

## Память и честные диагностические результаты

| Метрика | S | L |
|---|---:|---:|
| RSS baseline до30s |418.078МиБ|433.406МиБ|
| Peak RSS |488.129МиБ|507.277МиБ|
| Final RSS |483.027МиБ|491.832МиБ|
| Final−baseline |64.949МиБ|58.426МиБ|
| Допуск прироста |128МиБ|128МиБ|
| Peak / final private |394.816 /389.137МиБ|411.141 /393.148МиБ|
| Peak / final estimated managed |189.260 /128.782МиБ|165.409 /149.482МиБ|
| Last-GC committed |235.703МиБ|235.305МиБ|
| Natural Gen2 count |66→66|65→83|
| Max cache payload |4258920B|16770516B|

Ни RSS1ГиБ, ни post-warm128МиБ, ни cache16МиБ/128entries не превышены. GC не форсировался, страницы/fixture history не очищались ради результата.

Тренд[300,330)→[330,last] сохранён без округления в решении: S **+380342.228B**, nonGrowth=false; L **−1904776.533B**, nonGrowth=true. Legacy combinedworkingSetRecovery поэтомуfalse дляS иtrue дляL. Это отдельная диагностика, а новый m2PostWarmMemoryBudget=true дляобоих.

Все четыре исходных дополнительных результата остаются видны:
| Legacy result | S | L |
|---|---:|---:|
| finalSameRetainedRowsMemoryStable |false|false|
| dueWorkAndInboxSettled |true|true|
| recoveryWithin75Seconds |true|true|
| workingSetRecovery |false|true|

Пятисекундные средние выросли на104857.6B вS и2820505.6B вL; false не заменялсяtrue. Равенство отдельных counters не объявляется равенством полного retained state.

## Полезность, очередь и восстановление

Normal/finalsteady прошли полностью в обоих профилях: search20/20 и10/10, rates8/8 и4/4, prepare/create6/6 и3/3, status600/600 и300/300, callback4/4 и2/2.

| HTTP completion p95 от due | S normal / steady | L normal / steady |
|---|---:|---:|
| Search |129.3 /129.4ms|241.2 /368.7ms|
| Rates |127.2 /126.8ms|165.0 /171.9ms|
| Prepare |137.7 /125.2ms|198.3 /121.6ms|
| Create |196.9 /141.9ms|187.6 /130.8ms|
| Status |17.5 /17.0ms|17.7 /17.7ms|
| Callback |46.4 /82.3ms|65.0 /76.2ms|

Burst status150/150 иcallback30/30 полезны в обоих; p95status/ACK18.0/44.3ms вS и22.3/60.2ms вL. Из1200burstsearch healthy только19S и9L; быстрыеотказы/limitedHTTP200 не выдаются за полезную выдачу.

Восстановление по fixed-origin cohorts: end310(S,70sпослеburst) и307(L,67s). Legacy own-due recovery в этом запуске такжеtrue. Все16окон,eligible/carry-in IDs,raw/useful counts и authoritative cutoffs пересчитаны независимо по frozenoriginmaps. Методика новых цепочек не утверждает успех carry-in или ещё не предложенных дочерних операций.

Queue361observations/profile,unknown0,allcomplete. Maxrawdue2 в обоих; maxrequired1, maxredundant2S/1L. Наterminal:raw/required/redundant/unknown/pendingInbox=0. Все метаданные due messages сохранены без тел. Подтверждённые no-op не скрывают обязательную или неизвестную работу.

Finalrows каждого профиля:26Confirmed,10Quoted,2Unknown. Ожидаемые lostreply/cancellation/LimitedTimeout сохранены; нет duplicateeffects,unknownextraeffects или sourceoverflow. Неизвестный исход предусмотренной потерянной покупки не превращается вConfirmed.

## Источники и компактное хранение фикстур

| Profile/source | Seed / timed starts | Create / read | Final rates / quotes / bookings |
|---|---:|---:|---:|
| S/Duffel |40/114|6/11|1314/31/19|
| S/Hotelbeds |16/112|8/7|831/0/9|
| L/Duffel |40/118|8/11|14600/33/21|
| L/Hotelbeds |16/106|6/6|8726/0/7|

Actualevents совпадают с cumulative counters; конечнаяactive0, foreground<=24starts/1active иrecovery<=6starts/1active соблюдены.

На L все14600Duffelaliases сохранены; их uniqueimmutableUTF8ratepayload=6932933B, keyUTF16payload=481758B. Hotelbeds8726rateentries сохранены; actualstoredkeyUTF16payload сbookingkeys=1117012B. Исходные opaque wirekeys и lookup/response semantics прежние. ValueBytes остаётся логическимиserializedvalues и не объявляетсяRAM.

В предшествующем L v4 пикRSS был665.313МиБ иfinal−baseline190.707МиБ; теперь507.277 и58.426МиБ. Это разные исполнения, не идентичный A/B: фактические timing/sourcecounts/GC различаются. Измеренное улучшение согласуется с проверенным уменьшением fixture ownership; размер всех owners или отсутствие всех утечек не доказаны.

## Ресурсы, provenance и cleanup

Windows/local.NET/ASP.NET10.0.9,serverGC,cachedProcessorCount8,Hostaffinity0x3. DriverpeakRSS190.539МиБ(S)/228.813МиБ(L); ownedDBpeakcgroup190324736B/98197504B — отдельно отHost иDockerVM.

Минимум availableRAM15188930560B/15035940864B,freeDisk12398702592B/12383416320B. Максимум profileevidence15860648B/15822959B. Все исходные safety reserves4ГиБRAM/5ГиБdisk/512МиБevidence соблюдены. Whole-systemhard2ГиБcapacity не измерялась.

Независимо подтверждено отсутствие driver16644,Host52524/36532, exactcontainers:
- S:ce969784c2b9de0049aa3ae55d1bb20161d59c700ac56c0a40bb09f1567c3089;
- L:f35550c3c0c979cc7df1e28b9f4b0bdba5ee1724977edf09729c23895e6d3841.
Оба собственных tempdirectories удалены; runtimecleanupcompleted/cleaned/exited=true. Чужие ресурсы не затрагивались.

Packet:TestResults/hotels-m2-memory-load-acceptance/load-sl-v7/. Source2030/input32/build1001/runtime335+dotnethost проверены послеrun.485priorraw неизменны;52newraw закреплены в analysis-v1/integrity.json. Archive3034entries,56273186B,каждыйentry проверен.

| Artifact | SHA256 |
|---|---|
| source-manifest.json |A26C41151C0868C5AC265E807C3362D63D6AD9A343C8AEA08AA24B18146632EB|
| freeze.json |B8DD326ED2EDCDF3AF4BC1A1D8DC1849D765AF84EC3EDF1DF3D80BA0B3DD6A02|
| frozen-source-build.zip |E3CFEC75A7EF6C2C47C3D3672E68B98B4D49691C8BE5D0A593D6C04A3C0BFEF7|

analysis-v1/acceptance-decision.json accepted=true; independent cohort-queue-replay.json совпал с runtime дляbudget/trend,16окон и361queueframes каждого профиля. Старыеfailedreceipts не редактировались.

## Диагностика отдельно от приёмки

- [H4 v5](diagnostic-v5-result.md): подтверждено удержание fake rategraphs независимо от истечения productcache. Это не доказательство productcacheleak.
- [H5 v4](host-private-diagnostic-v4-result.md): основной прирост private пришёлся на nativeheap.
- [H6 v2](native-heap-diagnostic-v2-result.md): allocation evidence связало основную часть прироста с DLL-size metadata allocations; точные managedcall sites оставались ограничены evidence.
- [Metadata lifetime correction](compiler-metadata-lifetime-verification.md): проверен Roslyn ownershipcontract и внедрено детерминированное освобождение. [H6 v3](native-heap-after-metadata-fix-result.md) дал частичное сравнение; полныйH6 не был принят и не заменял S/L.
- [Compact fixtures/queue plan](l-fixture-queue-plan.md), [cohort review](recovery-cohort-review.md) и [trend decision](acceptance-v5.md) раскрывают дальнейшие изменения измерителя/фикстур и методики.

Sourcevalidation последней версии:425standalone/41focused,160actualevaluatorcases+3rejectedartifactproofs, historicalM1replay16/17 и5legacycode sections unchanged. [Последнийreview](rss-trend-review.md):0Critical/0Important/0Minor. В предшествующем cohortreview остаётся один необязательный пункт покрытия: same-kindfutureparent/duplicate-plan-ID regressions; guards проверены, исключений вruntimeacceptance он не даёт.

## Итог и границы завершения

Локальная fictional M2приёмка **пройдена по явно согласованной acceptanceVersion5**. Финальная цель этого плана достигнута: исходный M1failed16/17 и все промежуточные отказы сохранены; диагностика отделена от нагрузочной приёмки; S/L прошли независимый новый полныйcampaign с достоверным evidence иcleanup.

Это не принятие strict zero-growth, realproviderperformance, leak-free поведения, productioncapacity или targetrollout. L — граница по количеству properties, не одновременный максимум всех payload sizes. Изменения остаются локальными и не закоммичены; CI/publication/main-specsync/archive/deploy этим этапом не выполнялись. Следующий отдельный этап — оформление и CI изменений.
