# Design

Final checkpoint2026-11-01: local S/L v7 accepted under owner-approved version5,23/23each; all8taskscomplete. See [finalreport](load-sl-v7-result.md). Historical failures and external publication/target boundaries remain unchanged.

Current explicit owner decision2026-11-01: [acceptance-v5](acceptance-v5.md) makes the30s RSS trend diagnostic while retaining mandatory1GiB andbaseline+128MiB and all other version4checks. Historicalresults unchanged.

Current prospective amendment2026-11-01: [acceptance-v4](acceptance-v4.md) binds recovery cohorts to fixed dependency origins and reports carry-in separately. No workload, numeric limit or historical outcome changes. This supersedes only the rolling membership clause for futureversion4.

Current delegated correction2026-11-01: [acceptance-v3](acceptance-v3.md) and [implementation plan](l-fixture-queue-plan.md) govern the next packet. Compact load-fixture ownership and fail-closed required/redundant work classification preserve all numeric limits and all historical results.


Current amendment 2026-11-01: owner-approved [acceptance revision2](acceptance-v2.md) changes only the final five-second RSS comparison from blocking to diagnostic for future version2 M2 packets. The original numeric ceilings/recovery windows and historical receipts stay unchanged. Earlier sections/checkpoints describe their original revision.

## Context

[Research](research.md) проверил historical receipt и код: причина product memory defect не установлена, а старые L1 наблюдения не описывают обе M2 transport fixtures. Предыдущий planning-v1 смешивал диагностику и приёмку, добавлял обязательный idle Q3 и неподтверждённые требования к ресурсам. По аудиту и поручению владельца план упрощён.

## Goals / Non-Goals

**Goals:** исправить конкретные пробелы измерителя, получить причинное evidence по одной гипотезе памяти и отдельно проверить M2 нагрузкой с сохранением исходных gates.

**Non-Goals:** покупать/арендовать сервер, создавать новую инфраструктуру или performance engine, менять schema/API/UI/defaults, доказывать production capacity либо получать green изменением порогов, GC или очисткой состояния.

## Decisions

### 1. Минимальный source-срез

Переиспользовать `tools/demo/Travel.Hotels.Fixture` и `HotelsHostFixture.Multisupplier`. Driver уже отделён от измеряемого Host; заново строить process isolation не нужно. Добавить только:

- привязку наблюдений к фактически работающим DuffelWire/HotelbedsWire;
- отдельные source window/admission/actual-start/active/release counters вместо суммарного snapshot;
- полный planned/terminal ledger, bounded memory/retained-state counters и отдельный M2 evidence path.

Первичные targets: existing L1 runner/process helper, Host fixture, owned handler callbacks, `HotelCapacity` readonly snapshot при необходимости и `hotels-source-manifest.mjs`. Product admission/cache/booking logic не менять по предположению. Потраченный permit без HTTP (например, проигранный claim) отличается от supplier start.

Regression scope следует изменённым путям: неверный source count, omitted/cancelled job, early release/late publication, потерянный private handle или повторный effect. Existing green tests не называются новым RED. HTTP90/140s, application145s и completion10s проверяются existing controlled-clock cases, не новой длительной нагрузкой в ordinary CI.

### 2. Диагностика памяти — protocol H4-M2 v1

Подготовлен **один ownership-эксперимент** на existing standalone m2-memory, без actual Travel.Host/EF/PostgreSQL. Гипотеза по текущему source: **после обычного истечения search cache объекты ответов остаются достижимыми через словари обоих M2 owned handlers**. У original M1 другой HotelsFakeSupplier; этот эксперимент не доказывает причину historical failed RSS и не закрывает H1/H2/H3/H6. Он отделяет конкретное удержание fake от cache ownership; объяснение остального process RSS может остаться unknown.

Новое наблюдение — три Heap dump и ссылки от конкретных владельцев. Выбран Microsoft dotnet-dump10.0.750501, установленный только в локальный ignored packet. Проверены version/collect-help/analyze-help; подключения к процессу не было. По [Microsoft dotnet-dump](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/dotnet-dump) Heap содержит heap/стэки/handles, а collection может подгрузить страницы в working set. Поэтому RSS после snapshot не используется как recovery/acceptance measurement. dotnet-gcdump не применяется: он [вызывает Gen2 GC](https://devblogs.microsoft.com/dotnet/collecting-and-analyzing-memory-dumps/).

#### Входы и единственный фактор

| Параметр | H4-M2 v1 |
|---|---|
| Процесс | Existing Travel.Hotels.Fixture.dll m2-memory; .NET/ASP.NET10.0.9, Debug сборка принятого source-среза, affinity0x3 после старта |
| Данные | fictional,2026-11-05→2026-11-07,1adult/1room/0children; existing pair acc_fictional/900001, без task3.1 |
| Запросы |8 одинаковых POST search, затем1 POST rates к последнему expired search/property group; всего9 public jobs |
| Healthy output |1group/2leaves; оба source Complete/Attempted без reason: duffel-stays и hotelbeds |
| Concurrency |1public request, до1actual request/source, до2combined; следующий search через6s после ответа, без catch-up |
| Длительность |1цикл;302s monotonic от последнего search response, включая средний dump, затем expired rates |
| Фактор | Штатная доступность cache entry до/после TTL; source/data/options/fake history не меняются |
| Без покупки |0quote/0create/0reservation; нет faults/burst/status polling |
| Снимки | Ровно3: A доsearch, B после8search, C послеexpired rates |
| Срок packet | Запуск до2026-11-04T00:00:00Z; dates автоматически не сдвигать |

Здесь нет A/B разных сборок и matched-retained-state сравнения. Existing HotelSnapshotCache.Get в rates вызывает обычный lazy Purge по настоящему времени. Нет direct Purge/Clear, clock advance, сброса fake/claims, forced GC или trimming. Rates содержит непустой propertyGroupKey из ответа: иначе validation остановится **до** cache.Get. Ожидается HTTP409 / Hotels.Expired, не410 и не произвольная ошибка.

Ожидаемые числа — гипотеза, проверяемая по дампам после сбора, **не уже измеренный результат**:

| Snapshot | Cache entries | Duffel rates | HBX rates | Actual starts/source |
|---|---:|---:|---:|---:|
| A |0|0|0|0/0|
| B |8|16|8|8/8|
| C |0|16|8|8/8|

Quotes/bookings/store rows во всех точках0. Duffel сохраняет два DeepClone/result, HBX — один Scope/availability. В B все entries ещё не expired: packet проверяет первое expiry после среднего dump; пересечение делает сравнение invalid без повторения. В C starts не должны вырасти. Расхождения сохраняются, а не исправляются дополнительными запросами.

#### Packet, команды и provenance

Evidence root: TestResults/hotels-m2-memory-load-acceptance/diagnostic-h4-m2-v1/. В нём profile.json, run-v5.ps1, pinned tools/, source-manifest-v5.json, integrity-v5.json и подготовительные receipts. Одноразовый operational script входит в hash freeze; канонический протокол остаётся здесь.

Из физического корня worktree, проверка **без запуска приложения**:

~~~powershell
pwsh -NoProfile -File ./TestResults/hotels-m2-memory-load-acceptance/diagnostic-h4-m2-v1/run-v5.ps1
~~~

Точная команда эксперимента, **только после отдельного разрешения task2.2**:

~~~powershell
pwsh -NoProfile -File ./TestResults/hotels-m2-memory-load-acceptance/diagnostic-h4-m2-v1/run-v5.ps1 -Execute
~~~

Default invocation лишь сверяет hashes. Execute создаёт единственный новый run-v5/; существующий directory запрещает повтор. Повторно проверяются hashes/dates/free port5111/reserve; чужой owner порта не закрывается и не исследуется. Child запускается по absolute dotnet path с --fx-version10.0.9; PID/creation time известны из собственного Process object.

Окружение child/collector очищается; остаются только Windows/runtime/temp paths, diagnostics и opt-out telemetry. Cwd — свой run-v5/, config/token/key в m2-memory — existing fictional literals; real env secrets/config/UserSecrets не наследуются. Нет dotnet run/build внутри эксперимента, uploads или чтения чужих процессов.

~~~text
dotnet --fx-version 10.0.9 <tool-dll> collect --process-id <owned-child-pid> --type Heap --output <packet>/run-v5/<a-before|b-filled|c-expired>.dmp
~~~

Tool DLL: <packet>/tools/.store/dotnet-dump/10.0.750501/dotnet-dump/10.0.750501/tools/net8.0/any/dotnet-dump.dll. Fixture/collector/analyzer явно используют --fx-version10.0.9. История v2/v3/v4 и original failed16/17 сохранена в verification; v4 получил только baseline snapshot, H4-M2 остаётся unknown.

По запросу владельца от2026-10-30 подготовлен v5, без нового эксперимента. Исполнитель проверяет actual string enum Complete, типы flags/status, aggregate/source cardinality, исходный запрос, уникальные snapshot IDs и expiry. Один run-state связывает все функции. Process object регистрируется до чтения metadata, cleanup охватывает каждый созданный process. Все9jobs сохраняют состояние и время завершения, в том числе cancel/timeout. Receipt persistence, final deadline, неполный stderr и cleanup failure не дают complete=true.

Каждый вызов -Execute сначала атомарно занимает attempt-v5.json: даже preflight failure не допускает молчаливого повтора. Default invocation лишь проверяет hashes/profile и не создаёт attempt/run/process. Новый output — run-v5/, старые каталоги сохраняются. attempt-v5.json — окончательный результат всего вызова, run-v5/receipt.json — acquisition/cleanup receipt. Перед выводом об успехе требуются оба, отсутствие errors/warnings и согласованные counts; поздний отказ finalization нельзя скрывать ранней квитанцией. Поля acquisitionComplete/complete/ownershipConclusion разделены.

Source verification: реальный unmodified job-0.json из v4 плюс8search+expired409 JSON от existing standalone TestServer/owned handlers и fake clock. Это contract test без Kestrel/DB/real-time/dump run. Полный тот же orchestration выполнен в43offline cases с подменой OS process/HTTP/clock/resources; synthetic test files удалены и не являются heap evidence.136standalone tests passed; один независимый review и один fix-pass завершены. Windows collector timing/heap/memory acceptance этим не доказаны. Подробности — [v5 source verification](v5-verification.md). Runtime v5 ещё не выполнялся и требует отдельного разрешения.

Hash inventory фиксирует весь build output/fixture JSON/tool tree/runtime files и source manifest. Изменение требует нового freeze/review до разрешения; execute не скачивает и не чинит зависимости.

#### Предохранители и cleanup

Read-only preparation snapshot:8logical CPU/34318143488B RAM; фактические free RAM/disk сохранены в preparation-resources.json и перепроверяются перед execute. Это не новое hardware requirement.

- Общий deadline720s; startup30s, HTTP18s, каждый dump≤60s. Ровно9jobs/3dumps/1attempt; automatic retry/resume запрещён.
- Перед стартом и примерно каждую секунду ожидания: available RAM≥4GiB, free C:≥5GiB, evidence≤3GiB, target RSS≤1GiB. Превышение останавливает эксперимент, не устанавливает product regression. Polling допускает overshoot; это не hard quota.
- Snapshot pause/page-in/observer overhead раскрываются; resources.ndjson не является новой L1/M2 memory acceptance.
- При ошибке/stop dispatch прекращается; все9jobs остаются expected/response/unfinished/not-run. Частичные dumps сохраняются. Недостающий snapshot делает comparison incomplete.
- Finally завершает только созданные здесь Process objects и сохраняет PID/stop result. Нет DB/container/key assets, recursive deletion, prune или kill-by-name.
- Dumps сохраняются локально до решения владельца. Acquisition complete=true означает только9expected responses/3snapshots; ownershipConclusion отдельно not-analyzed, cleanup проверяется отдельно.

#### Offline analysis и правила вывода

После завершения child — один разбор≤10min/50SOS commands суммарно. Тот же pinned tool, local DAC10.0.9; symbol download не включать, environment/arbitrary strings/private payload не читать, чужие dumps не открывать. Недоступность DAC/heap walk означает unknown; новых snapshot/retry нет.

Базовый вызов для каждого из3снимков:

~~~text
dotnet --fx-version 10.0.9 <tool-dll> analyze <owned-dump> -c "setclrpath C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.9" -c "dumpheap -stat" -c "dumpheap -type HotelM2MemoryFixture+OwnedDuffelWire" -c "dumpheap -type HotelM2MemoryFixture+OwnedHotelbedsWire" -c "dumpheap -type HotelSnapshotCache" -c "exit"
~~~

По addresses **из того же snapshot** проследить поля rates обоих handlers и entries cache через dumpobj; objsize <dictionary-address> измеряет reachable graph. Для трёх владельцев в C проверить gcroot -nostacks <owner-address>, ссылки dictionary→values — по полям/ограниченному entry array. Addresses между dumps не переносить. Приоритет50commands: C counts/root path/cache ownership, затем A/B counts/sizes; не хватило evidence — unknown.

По [SOS documentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/sos-debugging-extension) ObjSize включает child objects, а gcroot -nostacks ограничивает поиск. Reachable bytes не exclusive/dominator size; shared subgraphs не складываются. Отсутствие non-stack root не доказывает отсутствие любых roots. dumpheap -stat включает несобранные объекты: один type count не устанавливает retention.

| Наблюдение | Вывод |
|---|---|
| C cache отпустил expired snapshots, handlers всё ещё владеют16/8values | H4-M2 подтверждена для этого пути: fake graphs удерживаются независимо от cache TTL |
| Fake graphs не сохранились/иной owner | H4-M2 опровергнута либо уточняется по raw facts; expected вектор задним числом не менять |
| Expired snapshots остались у cache после expected409 | Кандидат cache defect; отдельное подтверждение/RED до patch |
| Snapshots сохранил другой owner | Указать конкретный owner/path; это ещё не leak |
| Heap/root/expiry evidence неполно или budget stop | Unknown/incomplete, не отрицательное доказательство |
| RSS большой при меньшем cache graph | Причина RSS unknown; runtime/native residency остаётся гипотезой |

Bounded ownership result возможен без natural Gen2, поскольку проверяются сильные ссылки. Весь allocated heap «живым» не объявляется; освобождение страниц/leak-free не доказываются. Не выполнять product repair по RSS или сохранению fixture history. Historical M1 остаётся failed16/17, S/L acceptance — tasks3.1–3.3.


### 3. Приёмка M2 — существующий локальный запуск

Временный контур: actual Testing Host + собственная disposable PostgreSQL/inbox/outbox + owned fictional supplier handlers + отдельный driver. Он нужен, чтобы измерять реальные обработчики и хранение; in-memory CAS не заменяет relational результат. AppHost, Travel.AI, реальный Keycloak, suppliers и платные API не запускаются.

Сохранены два M2 профиля: small/1adult и maximum-cardinality/2adults. Каждый один раз по360s; detailed schedule, denominator и gates — в load-plan. Оба используют существующий areaKey="fictional"/Areas[3]. London+Fictional=true должен по-прежнему дать DestinationUnsupported/0HTTP: guard не ослабляется ради профиля.

В task3.1 явно включено ограниченное расширение fictional request/catalog в HotelbedsOptions/HotelbedsStayProvider: FictionalHotelIds default[900001],1–100unique positive IDs; request передаёт ровно объявленный список. S запрашивает900001..900009 (8matched+1unmatched HBX), L —900001..900100. Matching и ответы обоих owned handlers соответствуют этому scope. Empty/invalid catalog отказывается доHTTP; ordinary demo/default и external geolocation/rights/typed-transport guards сохраняются. Catalog/profile revision фиксируется при composition и входит в frozen hashes; новый registry/importer и hot reload не нужны.

Это отдельная подготовка будущего fictional профиля в source task3.1, не product memory repair и не неявное расширение первого instrumentation slice. Для принятия профиля captured-request regression должна показать9/100IDs; отдавать100отелей в ответ на старый запрос[900001] запрещено. Такая короткая проверка не устанавливает statistical SLA или отсутствие длительной утечки.

Нельзя переносить green нового fixture/profile на historical M1 или объявлять root cause по сравнению разных окружений. Максимальный набор служит отдельной M2 проверке; не нужно предварительно строить универсальную таблицу ответов job→response или систему pools. Использовать существующие handlers и только описанное расширение fictional request/data/синхронизации. Их собственная память остаётся в измерении и явно учитывается.

Evaluator хранит raw completion/authoritative-observation facts и отдельно вычисляет PhasePurchaseSuccess(p,phaseEnd) и WindowPurchaseSuccess(p,end). RecoveryAt не читает boolean успеха предыдущей фазы: покупкаdue284.5, подтверждённая301, неуспешна для фазы до300, но может войти в window ending305. Подтверждение310 не учитывается в том же окне305. Подробные predicates и контрольные примеры заданы в load-plan; HTTP p95 и150s bound не меняются.

### 4. Ресурсы — условия и безопасность запуска

Использовать существующий ПК и установленный Docker/runtime. Записать фактические CPU, RAM, .NET/GC/OS, Docker settings и нагрузку соседних процессов на уровне агрегированных метрик. Сохранить Host working-set≤1GiB, исходный recovery gate и2CPU affinity existing L1.

Удалены обязательные новые лимиты4CPU/6GiB общего контура,8GiB диска, private-commit2GiB и отдельные caps driver/DB/VM. Их необходимость не установлена. Для каждого конкретного run packet заранее задать time/disk/free-memory **предохранители**, исходя из доступного локального резерва и размера evidence. Они нужны для остановки эксперимента, не являются требованием купить машину или новым product performance threshold.

Host RSS/private/managed и driver/DB consumption показывать отдельно. VM/container memory не складывать с двойным счётом; system available-memory/commit delta — контекст возможного внешнего давления, не точный расход Hotels. Отсутствие общей физической изоляции ограничивает вывод; hard2GiBreference capacity остаётся unproven. Новую VM или изменение общих Docker settings автоматически не предпринимать.

### 5. Три границы разрешения

| Этап | Что нужно до работы |
|---|---|
| Source | Instrumentation1.1–1.2 выполнена после «окей, давай дальше» с ограниченными builds/standalone checks; receipts в source-verification.md. Подготовка профилей3.1 с fictional request/catalog extension остаётся отдельно разрешаемым source-срезом. |
| Диагностика | Конкретная гипотеза, новое ожидаемое evidence, frozen protocol/budgets/cleanup и отдельное разрешение ровно этого эксперимента. |
| M2 load | Reviewed source + source regression receipts + exact build/profile/data hashes и отдельное разрешение двух профилей/своего Host/DB/schema-init/keys/cleanup. |

Подготовка packet входит в соответствующий разрешённый source/planning scope и сама не запускает workload. После failure нет automatic retry/resume; сохранённый отказ не превращается в success. Публикация, реальные supplier rights/access/quotas, target и deploy остаются отдельными действиями.

## Risks / Trade-offs

- Строгий non-growth RSS может отказать без доказанной утечки → оставить отказ, показать retained state/GC/private evidence, не менять threshold.
- Existing handlers могут исчерпать свой test-only cap или иметь небезопасный concurrent доступ → проверить перед нагрузкой, внести минимальную адресную test-fixture правку; не выдавать fixture failure за capacity продукта.
- Новый M2 профиль меняет набор данных и источников → не использовать его как причинный A/B старого M1.
- На общей машине возможна внешняя нагрузка → записать её признаки и ограничить вывод, а не скрывать это новым неподтверждённым «6GiB» пределом.
- Короткая диагностика может не установить причину → unknown является допустимым исследовательским итогом, но не M2 memory acceptance.

## Migration Plan

Ни migrations, ни deployment в этом change не планируются. После отдельно разрешённого запуска удалять только собственные disposable assets по точным IDs/paths, сохраняя raw evidence и историю16/17. Main-spec sync/archive и публикация не входят в текущее упрощение.


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





