# S/L v1 — результат единственного разрешённого запуска

2026-10-30. Пакет вызван ровно один раз с `--execute`. **S failed/invalid, L not-run; M2 load acceptance не получена.** Повторов, нового diagnostic experiment, исправлений source или изменения порогов не было. Задачи3.2–3.3 остаются незавершёнными, общий статус6/8.

## Что произошло

| Факт | Результат |
|---|---|
| Frozen packet |load-sl-v1, source manifest F0E1A10820BE45301593BAFE0E683FED2A56A3448B9F29F565D08A4F6CD73C3F|
| Один вызов |2026-10-30 13:15:38.290–13:22:24.604 UTC|
| Общее время |406.316s; campaign timer400.466s|
| Exit |−532462766, unhandled .NET InvalidOperationException; forcedStop=false|
| S |planned3152, observed2883; gatesEvaluated=false|
| L |not-run,3152planned jobs; каталога run/L нет|
| Samples S |360/361:0.0956..359.0156s; terminal360 отсутствует|
| Final facts |явный available=false/FactsNotReached|
| Cleanup |cleaned=true; независимо проверен|

Исходники,21declared input,996build files,335runtime files и dotnet host прошли проверку до выполнения и после него. Проверка после запуска — verify-only, не второй execute. Старые7M1 и7diagnostic-v5 hashes не изменились.

## Полный denominator

| Терминал S | Количество |
|---|---:|
| Response |2868|
| Cancelled |1|
| NotDispatched |14|
| NotRun |269|
| FailedBeforeResponse / Unfinished |0 /0|
| Всего S |3152|

Весь campaign:2868Response+1Cancelled+14NotDispatched+3421NotRun=6304. L не исчезает из знаменателя.

В269NotRun входят118search,146status,2prepare,2create,1callback. Первый — create34.5s/job188; Started=null. По фазам: Normal58, Degraded35, Burst133, RecoveryAdmission22, RecoverySteady21. Записи сохранены в analysis-v1/not-run-jobs.json.

## Ошибка измерителя

Подтверждён дефект связки existing `HotelsL1Runner.WaitUntil` и `HotelsLoadLedger.Begin`:

- WaitUntil один раз вызывает Task.Delay(оставшееся время) и возвращается, не перепроверяя monotonic deadline.
- На том же .NET10.0.9 Delay(9999ticks=0.9999ms) возвращает общий уже завершённый Task.CompletedTask. Это доказано scalar probe, не зависит от скорости проверки IsCompleted.
- Frozen ledger отвергает due42/started41.9995 с InvalidOperationException; ровно42 принимает. Нельзя исправлять это округлением timestamps или ослаблением ledger.
- Ошибка Begin происходит до HTTP и не сохраняет Started/причину для конкретного job. Исключение отдельного dispatch task не отменяет остальные tasks: await Task.WhenAll обнаруживает его после завершения всей группы.
- В результате catch достигнут на359.816s, отменяет sampler перед точкой360; вызовы final facts и gates уже не достигаются.

Это объясняет возможный механизм ранних NotRun и согласуется с их распределением. **Точные отклонённые timestamps и исходный message/stack каждого runtime exception не сохранены**: receipt оставляет только InvalidOperationException, outer stderr — общую campaign exception. Поэтому нельзя утверждать точный ранний offset для каждого из269jobs.

Scalar proof: analysis-v1/scheduler-check-exact.json, frozen DLL, runtime10.0.9, HTTP0/Host0/DBfalse. Первоначальная аналогичная проверка PowerShell работала на его собственном10.0.11; она сохранена, но вывод привязан к последующей pinned10.0.9 проверке.

Stop-on-failure **между профилями** сработал: L не стартовал. Немедленная остановка **внутри S** при неожиданном исключении task не обеспечена. Это дефект подготовленного исполнителя и пробел его тестового покрытия;500source tests не проверяли эту реальную timer/scheduler связку.

## Какие сценарии достигнуты

- Normal search18/20 полезных; rates6/8; prepare5/6; create4/6 по записанным authoritative observations. Normal status545/600:55не стартовали. Эти доли включают полный offered denominator.
- В final steady search10/10, rates4/4, prepare3/3, create3/3; status279/300. Частичный успех этой фазы не исправляет весь результат.
- Search162 отменён через63.3ms от фактического dispatch. Search186 вернул HTTP200/Limited за15.012s: Duffel Complete/9, HBX Failed/Timeout/0.
- Lost-reply create154.5 **не был выполнен**: prepare153/job823 имеет NotRun; create832 получил PrerequisiteUnavailable. Lost-reply/Unknown protection в этом run не доказана.
- Normal rates46.2 зависел от search42/job228, который NotRun. Далее не состоялись prepare53/create54.5. В analysis-v1/prerequisite-failures.json сохранены все14отказов; запасной поиск/покупка не использовались.

Наблюдаемые p95 относятся только к имеющимся Response. Например, normal search157ms при18/20 и burst status17ms при131/150 не являются полноценным p95/95% acceptance: пропущенные запросы не имеют нулевой latency. Полные разрезы — analysis-v1/summary.json.

## Память и ресурсы

| Метрика S | Наблюдение |
|---|---:|
| RSS перед timed phase |402030592B ≈383.4MiB|
| Первое превышение1GiB |6.017s|
| Пик RSS |1445728256B ≈1.346GiB,196.018s|
| RSS последнего sample359 |490827776B ≈468.1MiB|
| Пик private / последний |1292431360B /333168640B|
| Пик estimated managed / последний |182781720B /83546976B|
| Максимум cache |128entries /3948147B ≈3.77MiB|
| Reservations во всех samples |38|
| Driver peak RSS |193507328B ≈184.5MiB|
| Own DB peak cgroup memory |194465792B ≈185.5MiB|
| Min available physical RAM |15807340544B; stop≥4GiB|
| Min free disk |10259644416B; stop≥5GiB|
| Max observed evidence |14786227B; stop≤512MiB|

Пик Host RSS — реальное наблюдаемое превышение acceptance ceiling. Он не был самостоятельным live safety stop в frozen executor; live stops были для available RAM/free disk/evidence. Они не сработали.

Baseline29s=1402408960B. По **доступным** samples средний RSS300–330=492417160.53B,330–last=492078967.47B;350–355=490955571.2B,355–360=490927718.4B. Обе observed-only non-growth формулы истинны, но terminal360 и полный корректный workload отсутствуют: workingSetRecovery не объявляется passed.

На196s private≈1.20GiB, estimated managed≈174MiB, GC committed at last GC≈203MiB. К210s private снизился до329973760B, одновременно Gen2 count вырос65→66; cache/fake entries насильно не очищались. Это корреляция, не установленный owner/root cause: RSS/private/managed/last-GC counters нельзя смешивать. Новых heap dumps не делали; источник пика и original M1 RSS cause остаются unknown.

Runtime10.0.9, serverGC=true, cachedProcessorCount8, Host affinity0x3. Эти факты не доказывают изоляцию всего процесса/VM на2CPU или hard2GiB capacity.

## Source accounting и gates

| Source | Seed starts | Timed actual HTTP starts | Timed create/read | Latest retained rates/quotes/bookings |
|---|---:|---:|---:|---:|
| Duffel |40|106|4/11|1260/30/17|
| HBX |16|103|6/6|783/0/7|

Actual start events совпали с cumulative deltas. В последнем sample оба active0; maxima foreground starts24, recovery2, foreground active1. Все наблюдаемые effects ровно1, unknownEffects0, overflowfalse. Конечные detailed valueBytes и per-row read allowance/sender-finished недоступны, потому что final facts не получены. Поэтому эти наблюдения не заменяют полные M2 gates.

Original17families сохранены **отдельно** в analysis-v1/summary.json/gates. Runtime не вычислил ни одну: receipt.gates=null. Из raw фактов уже достоверны offered/completed failure и RSS ceiling violation; lost-reply not-triggered. Остальные acceptance результаты unavailable/not-evaluated, без присвоения partial green.

M2 additions отдельно: source accounting имеет наблюдаемые106/103starts, SourceStatuses186правильные, duplicate effects не видны; completeEvidence=false, источник данных после timed boundary отсутствует, Normal purchase cohort4/6. Это failed/invalid measurement, не принятие capacity Hotels.

## Cleanup, integrity и следующий шаг

Driver57296 и Host2884 отсутствуют. Контейнер
`08f8762b2919c446891c443ec90da22a7f5e65b40b764762bae69487925001f7`
отсутствует в docker ps с exit0; own temp
`travel-hotels-host-3b97e9cd50cc41b28ef1d5b46c339c59`
удалён. Cleanup elapsed входит в362.684s profile receipt. Broad cleanup/prune/kill-all не выполнялись.

Raw output: `TestResults/hotels-m2-memory-load-acceptance/load-sl-v1/`;26raw files перечислены с hashes в analysis-v1/raw-integrity.json. После run сохранён frozen-source-build.zip:1964source/input+996build+2metadata entries,55741675B,SHA256 `34AA1AFFB29D6689767BA176AF75C10FE746512FF72D0EA70395BB01A33860DE`. Это provenance archive, не новая нагрузка и не часть timed resource measurements.

| Артефакт | SHA256 |
|---|---|
| execution-result.json |87D38829143936CE4ED56F5FBB24AA031C03E9C00084671F19A7BC4CFA0DDE65|
| run/campaign.json |1C2DE9369484A005B3B75EDF9271B3E1C245C9502D196342DAF96BE48CA64447|
| run/S/receipt.json |FAE6DB5EAAB5AE58FBD6EB2C92B4E8B721CD2974E250F32331F05C4FEDD1EEA9|
| run/S/cleanup.json |6A028370594D27547F778D3B792594ED75689E6706F1027AA7F2AA20CF6205EC|
| run/S/ledger.json |3F0DE378DB73B3293CA4210DCD1F3D593AE5D6588009E9194BC5E7CA3621FE35|
| run/S/resources.json |AB13ADF7C798B097634E7D34523A9A14AF39EB8A20FAF0A2EC9546FFCECCA4F8|

До нового runtime нужны source-only исправления: monotonic recheck в ожидании срока; остановка остальных dispatches на первой неожиданной ошибке; bounded job-level exception evidence и проверка полного orchestration на этой границе. Затем новый frozen packet и отдельное разрешение повторного запуска. Не менять numeric gates, не маскировать ранние timestamps округлением, не «чинить» RSS forced GC/fixture clearing.

Разрешение текущего единственного пакета использовано. Source/build/старый frozen load-plan не менялись. Strict OpenSpec valid=true, apply6/8, diff --check clean, indexempty; никакой публикации. [Diagnostic v5](diagnostic-v5-result.md) остаётся отдельным bounded owner-graph результатом; original M1 по-прежнему failed16/17. Ни real suppliers, ни target/rollout, ни память без утечек этим run не доказаны.

