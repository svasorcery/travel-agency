# S/L v2 — результат одного запуска

2026-10-30. После поручения «го дальше» выполнен один frozen S/L v2 packet. **S корректно измерен, но не принят:22/24gates. L — not-run по stop-on-failure.** Повторов, source/build изменений, нового memory experiment, forced GC или изменения thresholds не было.

## Исполнение и полнота

| Факт | Результат |
|---|---|
| Driver |68024,16:08:01.089–16:14:48.342UTC|
| Host |67072|
| Весь вызов / campaign timer |407.268s /401.730s|
| S elapsed с finalization/cleanup |366.752s|
| Exit |−532462766; failed gates,forcedStop=false|
| S planned/observed |3152/3152|
| Samples |361/361,0.132..360.032s|
| Максимальная lateness parent sample |0.2741s при allowed1s|
| Early starts / NotRun / Unfinished |0/0/0|
| execution-failures / failureDetail |empty/null|
| completeEvidence / final facts |true/present|
| L |3152planned,not-run;каталогаrun/Lнет|
| Cleanup |cleaned=true,независимо подтверждён|

Это отличается от [v1](load-sl-v1-result.md): прежние269пропусков и отсутствиеterminal360 не повторились. Runtime работоспособность исправленного исполнителя подтверждена наS; L пока не проверен.

S denominator:3141Response+1Cancelled+10NotDispatched=3152. Весь packet:те же3152terminal outcomes+3152Lnot-run=6304. NotDispatched — явные недостигнутые prerequisites в degraded/recovery; запасных purchase/retry не было.

S receipt.failure=null,finalizationErrors=[],gatesEvaluated=true. Outer InvalidOperationException сообщает об отрицательной приёмке, а не о новом dispatcher/sampler exception.

## Gates

**Original15/17; M2 additions7/7; всего22/24.** Два отказа:

| Gate | Наблюдение |
|---|---|
| workingSetCeiling |peak1384357888B≈1.289GiB >1073741824B; превышение310616064B≈296.2MiB|
| finalSameRetainedRowsMemoryStable |mean[350,355)=1384318566.4B;mean[355,360)=1384321024B;рост2457.6B=2.4KiB|

Все остальные15original families прошли, включая workingSetRecovery, recoveryWithin75Seconds, actual permit windows,11Duffel first-ID reads, bounded outcomes и fast lost reply. Все7M2 source/semantic/resource additions прошли. Полные24bools — run/S/receipt.json и analysis-v2/summary.json.

Разница последнего окна мала, но по frozen правилу положительный рост остаётся отказом. Точный расчёт: разность сумм5samples=12288B;12288/5=2457.6B,epsilon0. Никакой допуск/округление перед сравнением не добавлялся.

## Полезность и сценарии

| Фаза / операция | Полезные / offered | HTTP p95 |
|---|---:|---:|
| Normal search |20/20|128.5ms|
| Normal rates / prepare / create |8/8;6/6;6/6|119.9/133.2/190.0ms|
| Normal status / callback |600/600;4/4|17.2/74.9ms|
| Burst status / callback |150/150;30/30|18.5/44.1ms|
| Final steady search |10/10|126.8ms|
| Final steady rates / prepare / create |4/4;3/3;3/3|121.8/121.3/124.1ms|
| Final steady status / callback |300/300;2/2|17.8/36.5ms|

Purchase success здесь использует authoritative durable observations и exactly1effect; HTTPp95 отдельно считается по response completion. Короткие fictional cohorts не являются statistical/external SLA.

Burst search:1200observed,8healthy; остальные быстрые отказ/limited outcomes не выдаются за полезные результаты. Полный denominator сохранён.

-154.5s:HBX lost reply выполнен;HTTP200/Unknown за0.1284s,один retained effect,безrepeat/bookingID.
-162s:предусмотренная caller cancellation учтена.
-186s:HTTP200/Limited за15.0164s,DuffelComplete/HBXTimeout.
-RecoveryAdmission purchases2/3;неудача этой фазы не переносится в rolling window. Purchase window reaches3/3 сend307;общий recoveryWithin75Seconds=true.
-Final durable rows38:26Confirmed,10Quoted,2Unknown;pendingInbox0,dueWork0.

## Source accounting

| Source | Seed / timed HTTP starts | Timed create / read | Final retained rates / quotes / bookings | Serialized value bytes |
|---|---:|---:|---:|---:|
| Duffel |40/114|6/11|1314/31/19|1268736|
| HBX |16/111|8/7|822/0/9|70996|

Cumulative deltas совпали с actual start events. Наterminal active0,overflowfalse,unknownEffects0,duplicateeffects0; каждый наблюдённый effect ровно1. Source maxima:foreground24starts/1active;recoveryDuffel3/HBX2starts,в пределах6.14timed creates имеют bounded outcomes; HBX known-reference outcomes, которые былиadmitted, действительно прочитаны.

## Память

| Метрика | Значение |
|---|---:|
| Before timed RSS |418799616B≈399.4MiB|
| Baseline29s RSS |1329496064B≈1.238GiB|
| Первое превышение1GiB |6.020s|
| Peak / final RSS |1384357888B /1384321024B|
| Peak / final private |1290289152B /1289039872B|
| Peak / final estimated managed |196016368B /149363064B|
| Final GC committed at last GC |234622976B|
| Maximum cache |128entries/4212289B≈4.02MiB|
| Final cache |128entries/1958999B|

workingSetRecovery=true не означает малую абсолютную память: baseline уже выше1GiB. Final−baseline54824960B<128MiB;mean[330,last]=1384234743.742B≤mean[300,330)=1384256716.8B. Эти относительные условия прошли, absolute ceiling — нет.

Вtimed phase Gen2count оставался66. Вv1 между196и210s он вырос65→66 иprivate резко снизился; это только межзапусковая корреляция. V1имел неполный offered workload, поэтому его нельзя считать причинным A/B.

Окна350–355/355–360 имеют одинаковые38reservation/41inbox rows, но не весь retained dataset: после search354 cache bytes выросли1861293→1958999,Duffelrates1296→1314,HBXrates813→822. Это поясняет границы strict same-rows критерия, **не отменяет его отрицательный результат**.

Текущие estimated managed/GC committed/cache/fake serialized bytes не объясняют весьprivate≈1.20GiB; складывать/вычитать эти разные измерения как точные owners нельзя. Причина большого private/RSS остаётся unknown. Новых heap/native snapshots не собирали, memory repair/leak-free claim отсутствует.

Runtime10.0.9,serverGC=true,cachedProcessorCount8,affinity0x3. Driver peakRSS216719360B;own DB peakcgroup194719744B. VM/container не суммируются. Available RAM минимум16141926400B,free disk10139262976B,profile evidence max15498872B:заданные safety stops4GiB/5GiB/512MiB не сработали.

## Cleanup и сохранность

Driver68024/Host67072 отсутствуют. Контейнер
`4a8155fadff2a7497b689ab2e9a740c6b44bf04f5ff4d115a5d366ee02af1126`
отсутствует вdocker ps сexit0. Own temp
`travel-hotels-host-1f3c0077b0854236847e195c97523453`
удалён. Broad cleanup/prune не выполнялись.

Output:`TestResults/hotels-m2-memory-load-acceptance/load-sl-v2/`.
Raw hashes — analysis-v2/raw-integrity.json; cleanup-verification.json,summary.json,stability-exact.json,memory-landmarks.json сохраняют разбор. Post-run identity verification выполнена безexecute.40предыдущихM1/v5/v1rawhashes неизменны.

| Артефакт | SHA256 |
|---|---|
| execution-result.json |E1D2ADF421E611B9528436106EF20EAAADCBD79135514F4D6264DE40AA43B360|
| run/campaign.json |F2747A7BB83210A1C1362D09C8FFAA74DC9973EE2B5023A3149CB596B46549B2|
| run/S/receipt.json |11F5244D14440F563B6F595E7580F0878D2F46DBBDF02B54EC5471FD461D40F8|
| run/S/cleanup.json |023E958ECD84B2D27833898A5D4B03270D1D154AA89414CD1581030C09EDB675|
| run/S/execution-failures.json |20976FC4F3A11189A69F86C748C54AA3488D93DEBD1DC31342737682DF5CE6AD|
| run/S/final-facts.json |1528B0EB6D34A4FC3ACF3DA3B20A1A388CB2B26AFE2BA6E0819C448A8D9FAED3|

Frozen source/build сохранены вfrozen-source-build.zip:1972source/input+996build+2metadata entries,55763295B,
SHA256 `160BA8D43FECB0F9FC3CA44C54BC882EA355A2AA596762902AE548657F0BCB2E`.
Source manifest `B6D115831D3A1D48E8F917AA4574D82A63CD6CED1002AD27B6D8125E823F3DFA` иfreeze `525DE7B668251304C9A5001D6A6AB2515385F659564B6DEF97C3CF4F04460C07` прежние.

## Вывод и следующий шаг

Исполнитель наS теперь дал полный проверяемый результат; приёмка остановлена по двум memory gates. Lне проверен,полнаяM2acceptance не получена;tasks3.2–3.3остаютсяpending,6/8.

Следующий обоснованный шаг — отдельно подготовить диагностику владельца большогоprivate/RSS actualHost, с различающими before/after наблюдениями. Ещё один такой же S/L run сам по себе причину не установит. Не увеличивать лимиты,не вводитьepsilon,не чиститьfixture инефорсироватьGC радиgreen.

[Diagnosticv5](diagnostic-v5-result.md) отдельно подтвердил удержание fake rate graphs после очистки product cache; он не установил originalM1 RSS cause и не заменяет currentHost memory diagnosis. HistoricalM1остаётсяfailed16/17. Реальные suppliers/target/rollout не проверялись. Разрешённая попыткаv2использована; новые runtime/изменения требуют следующего поручения.
