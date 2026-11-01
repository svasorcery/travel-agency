# Proposal

Final checkpoint2026-11-01: local S/L v7 accepted under owner-approved version5,23/23each; all8taskscomplete. See [finalreport](load-sl-v7-result.md). Historical failures and external publication/target boundaries remain unchanged.

Current explicit owner decision2026-11-01: [acceptance-v5](acceptance-v5.md) makes the30s RSS trend diagnostic while retaining mandatory1GiB andbaseline+128MiB and all other version4checks. Historicalresults unchanged.

Current prospective amendment2026-11-01: [acceptance-v4](acceptance-v4.md) binds recovery cohorts to fixed dependency origins and reports carry-in separately. No workload, numeric limit or historical outcome changes. This supersedes only the rolling membership clause for futureversion4.

Current delegated correction2026-11-01: [acceptance-v3](acceptance-v3.md) and [implementation plan](l-fixture-queue-plan.md) govern the next packet. Compact load-fixture ownership and fail-closed required/redundant work classification preserve all numeric limits and all historical results.


Current amendment 2026-11-01: owner-approved [acceptance revision2](acceptance-v2.md) changes only the final five-second RSS comparison from blocking to diagnostic for future version2 M2 packets. The original numeric ceilings/recovery windows and historical receipts stay unchanged. Earlier sections/checkpoints describe their original revision.

## Why

Hotels M1 сохранил failed L1 **16/17**: строгий working-set recovery не пройден. Для реализованного M2 отдельного нагрузочного доказательства пока нет; ordinary CI и принятое владельцем ограничение M1 его не заменяют.

## What Changes

1. Минимально доработать существующий Hotels измеритель: видеть оба M2 handler, отдельные provider budgets, память Host и полный учёт запросов.
2. Отдельно проверить конкретную гипотезу причины роста памяти. Диагностика должна дать новое evidence, а не ещё один общий RSS-график.
3. После этого отдельно согласовать нагрузочную приёмку M2 на существующем локальном ПК: малый и предельный набор данных, исходные пороги и сохранение всех результатов. Оба профиля используют существующий areaKey="fictional"; их Hotelbeds request/catalog явно согласуются через bounded fictional-only список IDs.

Обязательный idle-профиль Q3 удалён. Новое железо, специальная VM и разработка общего performance framework не требуются. [Исследование](research.md) и исходные failed receipts сохраняются; [план проверок](load-plan.md) разделяет диагностику и приёмку.

## Capabilities

### New Capabilities

Нет.

### Modified Capabilities

- `hotel-stay-search`: уточнить **Useful bounded single-process resilience** — независимая M2 приёмка, полный denominator, честные границы memory evidence и отдельное разрешение на запуск.

Booking, matching, ranking и webhook contracts остаются обязательной поверхностью проверки без изменения бизнес-семантики.

## Impact

Выполненный первый source-срез ограничен existing Hotels CLI/Host fixture, минимальными readonly counters и manifest output routing. Существующие owned supplier handlers переиспользуются. Для будущего task3.1 отдельно предусмотрена узкая правка HotelbedsOptions/HotelbedsStayProvider: fictional-only список1–100hotel IDs вместо literal[900001], с прежним default[900001], без изменения external branch/guards. Это подготовка достоверного fictional профиля, не исправление памяти. Product memory fix — лишь после подтверждения причины и meaningful RED.

База: `c3fd3a6b3e8965ad8cf7850e160f22edfa12dedf`, свежий origin/dev на 2026-10-29; ветка `codex/hotels-m2-memory-load-acceptance`. Упрощение не меняет ветку или ancestry.

После отдельного поручения «окей, давай дальше» выполнены instrumentation tasks1.1–1.2 с builds/standalone checks: [source-verification](source-verification.md). Diagnostic protocol2.1 подготовлен: standalone H4-M2 ownership-проверка,8search+1expired-rates/3dumps, frozen packet с явным runtime и отдельной execute-командой. После прежних v2/v3 отказов отдельно разрешённый v4 вызван один раз:1успешный search/1baseline dump, затем ошибочный отказ runner из-за string enum Complete вместо ожидаемого numeric1. Доступный dump разобран; cache/handler rates пусты до нагрузки, post-load/expiry evidence отсутствует. Все процессы завершены, v5 подготовлен после полной проверки actual wire contracts/orchestration,43replay/136standalone и независимого review; новый runtime не выполнялся. См. v5-verification.md. Original M1 RSS cause остаётся unknown. Запуск диагностики, source task3.1, Host/Docker/DB/load, миграции, публикация и deploy не считаются разрешёнными результатом этих source проверок. Внешний API budget **$0**; credentials/billing, сохранённые demo/human-review и Trips M3 вне scope. Исходные пороги и failed16/17 не меняются.

[Design](design.md), [tasks](tasks.md) и [verification](verification.md) описывают текущую редакцию. Согласование упрощения не разрешает выполнение будущих задач.


Текущий runtime checkpoint2026-10-30: один v5 завершён9/9jobs/3dumps, owner graphs сопоставлены; product memory patch не обоснован. Tasks2.2–2.3 закрыты,5/8; см. [diagnostic-v5-result](diagnostic-v5-result.md). Подготовка/запуск S/L и внешние gates отдельно.

## H5 preparation — 2026-10-30

После S-v2 (22/24, два memory отказа, L not-run) подготовлен [точечный H5 protocol](host-private-diagnostic-protocol.md): три VMMap карты одного Host, observer-only control и 56-job warmup prefix. Это protocol/tool/workload preparation, не готовый исполняемый режим. Короткий source controller, его regression checks и runtime approval ещё требуются; VMMap EULA не принималась. Текущие результаты, frozen S/L inputs и thresholds не изменяются. Подготовка не разрешает Host/DB/collector запуск.

## H5 executor source checkpoint — 2026-10-31

Исполнитель H5 реализован по отдельному поручению без нового запуска. Основной сценарий, failure accounting, native collector guards и finalization проверены52H5/252full permitted standalone tests; независимое review0Critical/5Important/0Minor исправлено одним RED/GREEN pass. Новый исполняемый source packet — diagnostic-host-private-v2; старая preparation-v1 и S/L результаты сохранены. См. [H5 executor](host-private-executor.md). Runtime/VMMap XML proof и EULA владельца остаются отдельными; tasks3.2/3.3 открыты,6/8.

## H5 runtime checkpoint — 2026-10-31

Один H5 выполнен после «го» и принятия владельцем VMMap EULA. Получена raw P0; runner отказал из-за неверного XML shape, а post-run identity — из-за6 служебных файлов, созданных collector в worktree при отсутствующем SystemDrive. P1/P2 и56warmup jobs not-run;cleanup подтверждён, extra files сохранены сhashes вevidence. Baseline private≈262MiB; причина прежнего роста до≈1.2GiB остаётсяunknown. Повтора/source patch не было. [Результат и offline анализ](host-private-diagnostic-v2-result.md).6/8 неизменно.

## H5 v3 source checkpoint — 2026-10-31

XML-reader и Windows environment исправлены по реальному P0;267/267 standalone tests,67H5,raw XML replay и сборка прошли. Независимое review0Critical/1Important/1Minor: Important закрыт по3RED→GREEN regressions; Minor старого empty-regions теста отложен, runtime guard сохранён. Новый пакет diagnostic-host-private-v3 подготовлен без запуска. Все прежние raw receipts/пороги сохраняются,6/8 и pending3.2/3.3 неизменны. [Детали исправления](host-private-executor-v3.md).

## H5 v3 runtime checkpoint — 2026-10-31

Одна разрешённая попытка получила P0/P1 и27samples, затем остановилась на create26/4.875s из-за ошибочного требования immediateConfirmed при предусмотренном ReservedAwaitingDetails.29responses/27NotRun;P2нет. Control private stable/decreased; первые4sпослеbegin private270.79→728.68MiB, категория приростаunknown. Cleanup и frozenidentity подтверждены;85priorrawhashes unchanged;retry/sourcepatch не выполнялись. [Полный разбор](host-private-diagnostic-v3-result.md). План6/8,loadacceptancepending.

## H5 v4 source checkpoint — 2026-10-31

Исправлен только H5 purchase predicate: accepted HotelbedsReservedAwaitingDetails допустим при правильных source/reservationId/admission/noerror/deadline,непревращаетсявConfirmed. S/Lconfirmationgates/productнеизменены.288/288standalone(88H5),realrecordedfullcontrollerreplay иactiveTestServerAPIпрошли;freshreview0Critical/0Important/0Minor. Положительныйt12unitboundaryмоделируетсяявно;originalS-v2t12active остаётсяnegativecase. Новыйпакетdiagnostic-host-private-v4,безruntime. [Source repair](host-private-executor-v4.md).6/8план,всепрежниерезультатысохранены.

## H5 v4 runtime checkpoint — 2026-10-31

Один H5v4 завершён:56/56responses,35/35samples,3/3maps,exit0,cleanup/sourceidentity подтверждены. Heap (Private Data)+640106496B(610.45MiB),99.72%XMLprivategrowth P1→P2; почтивесьприрост вrawID131073(13→130regions). Observer-only controlнерастёт;native code owner иbusy/freeunknown. [Полный разбор](host-private-diagnostic-v4-result.md). No retry/productpatch/thresholdrelaxation;132priorrawunchanged. M1/S-v2failuresсохранены,6/8иloadacceptancepending.

## H6 preparation checkpoint — 2026-10-31

После H5v4 выбран proposed native-heap attribution protocol: PID-only WPR HeapSnapshot, два allocation/stack snapshots + VMMap, только owned Host. Проверены local WPR help/profile/status и disabled dotnet heap flags. WPA отсутствует, token неAdmin; новый recorder/controller ещё не реализован. [H6 protocol](native-heap-attribution-protocol.md) прошёл read-only builder check без существенных открытых вопросов для scope подготовки. Протокол готов,executionReady=false; installs/ETW/Host/DB/нагрузка не выполнялись,189priorraw неизменны. Приёмка S/L и runtime gates остаются открыты,6/8.





