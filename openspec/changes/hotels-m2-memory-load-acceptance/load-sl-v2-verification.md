# S/L v2 — source verification

2026-10-30. Timer/error handling исправлены и проверены **без нагрузки**. Branch/base прежние:codex/hotels-m2-memory-load-acceptance,c3fd3a6b3e8965ad8cf7850e160f22edfa12dedf. Stage/commit/push не выполнялись.

[Protocol](load-sl-v2-protocol.md) описывает новую execution boundary; dataset/schedule/business rules/численныеgates/старыйfrozenload-plan неизменны.

## Проверки

Receipts:`TestResults/hotels-m2-memory-load-acceptance/source-sl-v2/`.

| Проверка | Результат |
|---|---|
| Timer наactualv1single-delay seam |RED5/5;timer-red.trx |
| Execution orchestration |RED5failed/6;execution-red-v2.trx |
| Review integration cases |RED6/6;review-red-v3.trx |
| Final focused |22/22;review-green.trx |
| Full permitted standalone |200/200,0skips;standalone-final.trx |
| Manifest |4/4;manifest-final.log |
| Wrapper |compile-only,0errors/1knownVerifywarning;wrapper-final.log |
| Pure evaluator |18/18,HTTP0/Host0/DBfalse;evaluator-final.json |

Покрыты80real sub-ms deadlines,early-wake recheck,полные3152-job S/L черезactualexecution/virtualtime,terminal sampler,user cancellation,late immutability,80concurrentfaults→64entries/overflow,expected429,actualbadJSON/transport path. Compile/analyzer setup errors сохранены отдельно и не считаются RED.

Полный solution/DB-backed HotelHttpTests и HotelDurablePurchaseTests не запускались. Product/unit/architecture code не менялся; прежние79/243checks не выдаются за новый прогон.

## Один независимый review

0Critical/3Important/0Minor; все6regressionsRED→GREEN, затем200/200suite. Без второгоreview.

1. Linked cancellation lifetime:CTS вынесен заtry/drain; sharedstop admissionflag проверяется независимо. Regression блокирует propagation callback,dispose linkedCTS и проверяет отсутствие futuredispatch.
2. Observer wrapper:исходнаяerror сохраняется доCancelAsync. Blocked/throwingcallbacks не скрывают и не заменяют IOException; actualM2 rawsampler/database подключены кэтомуboundary.
3. Swallowed dispatcher exceptions:explicitDriverFailure останавливает supervisor сsafe metadata; HTTP200/Response сохраняются приbadJSON,transport доheaders остаётсяFailedBeforeResponse.

Declined runtime/Windowsload/Host/DB/cleanup,RSS/capacity/leak/supplier/target вопросы остаются отдельнымиgates. Freeze проверяется исполнителем послеreview; reviewer tests не запускал. Дельта доv1не переоценивалась.

## Решения и ограничения

- Small named helpers обслуживают existingrunner; новый workloadframework не создаётся.
- Delay rounding допустим, timestamp clamping нет; возможныйoversleep проверяется прежнимиlatencygates.
- Expected refusals/cancellations не смешиваются сunexpecteddriver/observerfault.
- Metadata64×4callsites исключаютprivate messages; exactstage/time/type/HTTPstatus остаются.
- Fixeddata/dates/gates сохранены; memoryfix и runtimegreen не заявляются.
- Workingdiff/ledger сохраняются безcommit/cleanup. Tasks3.2–3.3остаютсяpending,6/8.

## Сохранность и freeze

V1source/build ZIP сохранён до правок:55741675B,SHA256 `34AA1AFFB29D6689767BA176AF75C10FE746512FF72D0EA70395BB01A33860DE`.
V1Sfailed/invalid,Lnot-run,26rawfiles иM1/v5evidence сохраняются. V2data-preservation.json фиксирует16побайтноидентичныхspecimens.

Freeze завершён2026-10-30T15:36:20UTC:
1972source/input entries,996build files,335runtime files+dotnet host,24explicit inputs.
Verify-only exit0:`prepared=true,executed=false,profiles=[S,L],planned=6304`.
run/ отсутствует,attempt files0. Команда проверки — в protocol; `--execute` не вызывался.

| Artifact | SHA256 |
|---|---|
| profile-S.json |BE5D2522C96710B74E00339FE6DE84DB9E14B9316A850F7A023C9FAE759377C2|
| profile-L.json |8E4886C0C7C128F0876102423829F2FDAA2382FF93E1A9B0428B660FDCB0D335|
| build-inventory.json |6517384710145FBB2F67B49968DBCF8056FD2E9B4332FAC3C4AF210BA58BDE9B|
| source-manifest.json |B6D115831D3A1D48E8F917AA4574D82A63CD6CED1002AD27B6D8125E823F3DFA|
| freeze.json |525DE7B668251304C9A5001D6A6AB2515385F659564B6DEF97C3CF4F04460C07|

Read-only проверен тот же local imageID `sha256:7ae6051efd0e60444282c27c7e141af07f322ce033300e727a49c3dd11075e38`; pull/settings/container actions не выполнялись.

Historical offline replay вновь16/17,workingSetRecovery=false; пять evaluator sections unchanged. Test helper распознаёт изменившийся маркер next method (`private Task WaitUntil` вместоasync), не меняя сравниваемое телоPhase или predicates. V1raw26hashes совпали; oldfrozenload-plan не изменён.

Один timeout автоматического permission review остановил первоначальную запись документов до создания файлов. Разрешённый инструментом однократный retry успешно завершён; scope не расширялся, незавершённых approval действий нет.

Нагрузка не выполнена, RSS/memory fix не заявляется. Tasks3.2–3.3pending,6/8; новый runtime требует отдельного разрешения.


Final checks: 40 historical hashes unchanged (7 M1 + 7 diagnostic-v5 + 26 load-v1); CSharpier 10 files; strict OpenSpec valid with no issues; index empty; base ancestry preserved. Final verify-only exit 0, executed=false; v2 run absent, attempt count 0. Receipts: historical-integrity.json, format-check.log, openspec-validation.json, packet-verify-final.log.
