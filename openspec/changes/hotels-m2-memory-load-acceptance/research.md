# Исследование Hotels memory 16/17

Дата: 2026-10-29. Только статическое чтение кода, документов и сохранённых JSON; новых тестов, сборок или запусков не было. База исследования и нового worktree: `c3fd3a6b3e8965ad8cf7850e160f22edfa12dedf`. Hotels source/spec/archive paths совпадают с прочитанным исходным checkout `35953fd6c8feabf16acd7fb944201711e3d8c5fa` (read-only Git diff пуст); root AGENTS дополнен свежим статусом Trips, который вне scope.

## 1. Источники и сохранность

Прочитаны [root AGENTS](../../../AGENTS.md), [Hotels AGENTS](../../../modules/hotels/AGENTS.md), [README](../../../modules/hotels/README.md), [completion review](../../../docs/architecture/hotels-completion-review.md), пять main specs: [search](../../specs/hotel-stay-search/spec.md), [booking](../../specs/hotel-reservation-booking/spec.md), [matching](../../specs/hotel-property-matching/spec.md), [ranking](../../specs/hotel-offer-comparison-ranking/spec.md), [webhook](../../specs/duffel-webhook-routing/spec.md). История: [M1 design D6](../archive/2026-10-19-hotels-m1-duffel-booking/design.md), [M1 verification](../archive/2026-10-19-hotels-m1-duffel-booking/verification.md), [M2 design D3–D9](../archive/2026-10-19-hotels-m2-multisupplier-search-booking/design.md), [M2 tasks](../archive/2026-10-19-hotels-m2-multisupplier-search-booking/tasks.md), [M2 verification](../archive/2026-10-19-hotels-m2-multisupplier-search-booking/verification.md), [relational record](../archive/2026-10-19-hotels-m2-multisupplier-search-booking/relational-verification.md). Исторические промежуточные статусы не заменяют финальные записи archive/completion.

Локальные evidence находятся **в основном checkout** `D:/_Projects/_github/travel-agency/TestResults/hotels-m1/`, не в новом worktree и не в Git. Они не скопированы и не изменены. Хеши ниже вычислены по исходным байтам, без parse/reserialize.

| Файл относительно этой локальной папки | SHA256, фактически проверен |
|---|---|
| l1/receipt.json | a37df64485b4f585b846fb021c702585f90c178960bcac767f7965c9a08ace3e |
| l1/resources.json | 50d3c2c19cf41c76f180fce40146eee2da6fe166607d322e9edc9e6ea6d8c780 |
| l1-profile.md | cc3c53c80e8bba9fb9635c76747159d0998df2128b26840e155493861d5f4330 |
| l1-memory-diagnostic-profile.md | ca6821b5198c5fee8741740f3fe0e6ef8a6030f80e37501f0ff68d9848046248 |
| l1-memory-diagnostic/receipt.json | 140d0b8a1ba306fbfa8363742ddebde4ac28dbc167a34a4f9919917c020a0169 |
| l1-memory-diagnostic/memory-diagnostic.json | b97951b8b47fb4539a08b2c65c651e3eced638457af3a7ea57526fa3f5b9f170 |
| source-manifest.json | f12dc161661dedf677ec89d130140c580bf033ac24a20abe23f8f85310a177e3 |

Receipt hash совпал с ожидаемым completion review. Также доступны original planned-jobs/operations/supplier-starts/http-permit-windows/recovery-read-accounting/driver-resources/before-run/checkpoint/failure/runner-failure JSON, earlier attempt1 setup failure, attempt2 connectivity failure, attempt3 memory failure, pre-diagnostic source copies и allocation-probe receipt. Их наличие не означает повторной полной проверки всех прежних source/BIN inventories. Оригинальный M1 source был uncommitted поверх `894c40f895b9e864263686f814ac0993bdbd3e8b`; нынешний dev не является точным старым executable. Нынешний source-manifest уже относится к последующей диагностике; не приписывать его acceptance receipt без проверки embedded before-run inventory.

## 2. Что именно отказало

Acceptance `l1/receipt.json`: 3152 offered/completed, elapsed 360.0269987s, 16 true из 17 gates, `workingSetRecovery=false`, `passed=false`. Failure receipt фиксирует InvalidOperationException, 3152 recorded, без automatic rerun; archive фиксирует exit1. Исторический диагностический receipt тоже 16/17, но не acceptance replacement.

Точная формула в [HotelsL1Runner.cs](../../../tools/demo/Travel.Hotels.Fixture/HotelsL1Runner.cs), `Gates`, строки 919–987:

- baseline = последняя telemetry sample с At < 30.
- final = последняя sample.
- firstSteady = mean RSS при 300 ≤ At < 330.
- lastSteady = mean RSS при At ≥ 330, **включая terminal sample после 360**, если она присутствует.
- workingSetRecovery = final ≤ baseline + 134217728 bytes **AND** lastSteady ≤ firstSteady.
- Отдельный дополнительный gate сравнивает [350,355) и [355,360), минимум по пять samples, одинаковые reservation/inbox row counts и bounded cache; mean второго окна ≤ первого.

Офлайн пересчёт сохранённых resources (не новый experiment):

| Показатель | Байты |
|---|---:|
| Peak child working set | 1026928640 (979.355469 MiB), ниже 1073741824 |
| Baseline At≈29.03 | 989679616 |
| Final At≈360.018 | 856109056; baseline +128 MiB выполнен |
| Mean [300,330), 30 samples | 855800900.266667 |
| Mean [330,last], 31 samples — **код gate** | 856074966.709677 |
| Прирост strict gate | 274066.443010 = примерно 0.261370 MiB |
| Mean [330,360), 30 samples — исторический текст | 856073830.4 |
| Mean [350,355) / [355,360) | 856150016 / 856146739.2, дополнительный gate выполнен |

В архивном narrative приведён прирост 272930.13 bytes / 0.260286 MiB для окна без terminal sample. Это небольшое расхождение **описания окон**, не другой исход: обе формулы дают failure. Старый документ не переписывается; будущий evaluator должен явно сериализовать индексы/число samples и включение endpoint.

Остальные 16 gates: denominator; useful search; rates/prepare/create; private status/ACK; burst status/ACK; capacity lanes; cache bounds; RSS ceiling; fixed reservation count; final same-row stability; useful recovery≤75s; settled due/inbox; 11 реальных first-ID reads; actual HTTP starts/permit windows; bounded admitted outcomes; ровно одна быстрая потеря ответа с удержанным effect. Purchase concurrency A8 — отдельное доказательство, не один из этих 17.

## 3. Исходный профиль и границы измерения

`l1-profile.md`, [M1 D6](../archive/2026-10-19-hotels-m1-duffel-booking/design.md), runner `Schedule` (529–559):

| Stage | Секунды | Нагрузка |
|---|---|---|
| Warmup | 0–30 | normal mix, исключён только из latency/success gates |
| Normal | 30–150 | search10/min, rates4/min, prepare3/min, create3/min; status5/s; callbacks2/min; read work от этих callbacks; handler100ms |
| Degraded | 150–210 | тот же mix; deterministic30% slow3s/10% safe failure; один create теряет reply≤1s после effect |
| Burst | 210–240 | search40/s, status5/s, callback1/s; handler500ms |
| RecoveryAdmission | 240–300 | normal mix, все отказы сохраняются |
| RecoverySteady | 300–360 | normal mix, заранее фиксированное окно |

Denominator: search1255, rates22, prepare17, create17, status1800, callbacks41 =3152. Derived recovery GETs считаются отдельно и входят в provider budgets, не подменяют offered HTTP jobs. 38 reservation rows созданы до таймера: 24 drafts, 2 confirmed status и12 spent no-ID. 41 inbox row /18696 ciphertext bytes добавлены в процессе. Timed creates14; seed creates14; actual timed HTTP starts133; 11 first-ID reads; useful recovery t305, т.е.65s после burst.

Исходные gates: normal/final steady ≥95% useful каждого этапа, search p95≤1000ms, status/ACK≤500ms; rates/prepare≤20000ms, create≤150000ms в runner; burst status/ACK≥95% и≤1000ms; foreground24/min/1active, recovery6/min/1active, combined2active; cache128/16MiB; RSS≤1GiB; final baseline tolerance128MiB и строгое отсутствие роста средних; recovery≤75s. p95 использует Completed−Due, а не только service time успешных запросов. Полный140s timeout проверялся controlled-clock тестами отдельно.

[HotelsL1HostProcess.cs](../../../tools/demo/Travel.Hotels.Fixture/HotelsL1HostProcess.cs), 229–288, 423–499, 741–839: actual Testing Host/Kestrel и bounded fake/observer находятся в child PID33188; HTTP driver/history/checkpoint serialization в parent PID51420. Child affinity0x3 устанавливается после CLR startup. 1Hz telemetry, private stdin/stdout frames64KiB/queue64; parent снимает child WorkingSet64/CPU, child передаёт GetTotalMemory(false), cache/capacity/DB/work facts. Parent metrics отдельные. Host RSS включает managed/native/runtime/JIT/loaded assemblies/thread stacks/observer/fake и resident shared pages, но не PostgreSQL/Docker VM/driver. Сумма RSS процессов может двойным счётом учитывать shared pages.

Windows10Pro19045, .NET10.0.301,8 logical CPU, RAM34318143488 bytes; DockerDesktop29.8.2,8 CPU/RAM16751714304. Child affinity ограничивала CPU, hard2GiB memory cap отсутствовал. `hard2GiBEnvironmentProven=false`. Весь контур не измерен единым ресурсным envelope. AppHost/AI/real Keycloak/provider не являются измеренными компонентами.

Pre-timer cache proof в текущем `FillAndBoundCache` (1031–1105) делает **140 small →140 large →140 small** inserts; это значимые transient allocations до baseline. Сокращать их или удлинять warmup для green нельзя. Count/PayloadBytes вызывают Purge: observer тоже выполняет lazy TTL housekeeping. 1Hz query материализует incoming/outgoing Wolverine envelopes; это не бесплатная метрика.

## 4. Сохранённая диагностика и предел вывода

Raw diagnostic hashes также совпали с M1 verification. `initialFake/finalFake`: rates76→220, quotes38→53, bookings14→28, preparation38→53; serialized values122738→288823 bytes, без managed node/key/dictionary overhead. В финальные350–360s rates218→220. Равные38+41 DB rows не означают равный полный retained graph.

Последняя минута diagnostic: RSS рос, private bytes снижались (archive: +0.136807 /−0.117360 MiB); natural Gen2count оставался4 во всём timed run. GetTotalMemory(false) включает недособранный garbage; last-GC fields не являются текущим live heap; committed GC memory не равна private commit и RSS. Нет сопоставимых natural full-GC snapshots, heap root analysis или native page attribution. Thread/timer/assembly snapshots не показали монотонного роста, но не доказывают отсутствие утечки. Зафиксированная потеря RSS gate не превращается в product defect или доказанную неисправность GC.

Ранний attempt3 измерял driver и Host вместе. Allocation probe установил observer allocations от checkpoint serialization, после чего driver был вынесен. **Этот дефект границы уже исправлен до финального16/17**; повторное предложение вынести driver не исправит финальный отказ.

## 5. Source trace: факты и гипотезы

| Область / repository reference | Статический факт | Что ещё требуется доказать |
|---|---|---|
| [HotelSearchService.cs](../../../modules/hotels/Travel.Modules.Hotels.Application/HotelSearchService.cs): cache27–215, search/rates/Bound | Singleton cache128/16MiB, TTL≤300s, private context≤4096B в той же entry; serializes для size и Freeze, deep copy на Get. Aggregate byte reduction повторяет Put с удалением whole group. Purge только при обращении. | H1: transient serialization/LOH pressure и object overhead могут объяснять часть footprint; bound сериализованных bytes не bound всего managed graph. Root retention после eviction не установлен. |
| [HotelReadCoordinator.cs](../../../modules/hotels/Travel.Modules.Hotels.Application/HotelReadCoordinator.cs):8–70 | shared2 slots;15/17s CTS; uncooperative task удерживает slot/CTS до actual completion, затем dispose/release; late result не публикуется. | H2: длительно живущие child tasks при cancellation удерживают bounded state; это предусмотренная защита, а не установленный leak. Проверить отсутствие накопления после реального завершения. |
| [HotelCapacity.cs](../../../modules/hotels/Travel.Modules.Hotels.Infrastructure/Providers/Duffel/HotelCapacity.cs):21–121 | фиксированные два provider keys, leases idempotent dispose; snapshot **суммирует** providers и берёт max WindowUntil. | Из общего snapshot нельзя восстановить M2 per-source window/active. Использование старого M1 gate даст ложную оценку; нужен минимальный per-source numeric observation. |
| [DuffelStaysProvider.cs](../../../modules/hotels/Travel.Modules.Hotels.Infrastructure/Providers/Duffel/DuffelStaysProvider.cs):705–775; [HotelbedsStayProvider.cs](../../../modules/hotels/Travel.Modules.Hotels.Infrastructure/Providers/Hotelbeds/HotelbedsStayProvider.cs):635–679 | request/response/stream/MemoryStream/JsonDocument имеют scoped disposal, ResponseHeadersRead,2MiB bound; buffering/ToArray создают временные копии. HBX rates повторяет Availability, Prepare делает CheckRate. | H3: allocation pressure при максимальных ответах; нет найденного missing-dispose на этих путях. Fictional terminal handlers не упражняют supplier sockets/TLS/pool. |
| [HotelsModule.cs](../../../modules/hotels/Travel.Modules.Hotels.Api/Composition/HotelsModule.cs):64–190 | cache/capacity/providers singleton; services/DbContext scoped; real HTTP pool2connections, lifetime2min; fictional typed handlers. | Singleton lifetime объясняет сохранение handler state до конца процесса, но не устанавливает рост живых production objects. Не включать real pool в conclusions fictional run. |
| [HotelsFakeSupplier.cs](../../../tests/Travel.Host.Tests.Integration/Hotels/HotelsFakeSupplier.cs):42–83,158–213,278 | rates хранит два DeepClone дерева на search; quote/book/preparation dictionaries растут; Dispose override пустой для reuse fixture; Starts/ReadCompletions drain в L1 sampler. | H4: исторически измеренный fake retention, без TTL; влияние на RSS неизвестно. Не удалять fake history, чтобы пройти threshold. |
| [HotelM2MemoryFixture.cs](../../../tests/hotels/Travel.Modules.Hotels.Tests.Integration/HotelM2MemoryFixture.cs):281–421 | M2 имеет другие owned handlers: Duffel dictionaries, HBX Dictionary scopes/bookings; HBX guard128rates/64bookings. | Повторять старый L1 seed/runs без расчёта fixture cardinality нельзя. Concurrent reads/writes в owned Dictionary требуют явной проверки load safety; функциональная fixture не готовый load server. |
| [HotelsHostFixture.cs](../../../tests/Travel.Host.Tests.Integration/Hotels/HotelsHostFixture.cs):28–54,284–333 | Multisupplier подключает DuffelWire/HotelbedsWire вместо наблюдаемого старым harness Supplier. Настоящий Program/SQL/outbox доступны. | H5: M2 instrumentation gap подтверждён source: старые phase hooks, create/read counts и telemetry не описывают обе реальные подмены. |
| [HotelReservationStore.cs](../../../modules/hotels/Travel.Modules.Hotels.Infrastructure/Persistence/HotelReservationStore.cs):19–62; [HotelRetentionWorker.cs](../../../modules/hotels/Travel.Modules.Hotels.Infrastructure/Persistence/HotelRetentionWorker.cs):23–57 | no-tracking bounded owner reads; scoped DB; disposed5min timer/scope; cleanup100 inbox rows,90days. | H6: query/observer/runtime/native allocations и environment residency остаются возможными, без доказанной retention chain. Retention нельзя запускать вне штатного времени для green. |

[HotelReservationService.cs](../../../modules/hotels/Travel.Modules.Hotels.Application/HotelReservationService.cs):159–284 сохраняет replay-before-admission, capacity-before-claim,145s sender и10s completion budgets. Browser cancellation не отменяет уже admitted authority; нет основания менять это из-за RSS.

## 6. Рекомендуемый минимальный срез и будущие проверки

Первым исправлять **доказанные пробелы измерительного harness** H5/per-source denominator, не объявлять изменение product memory management. После отдельно разрешённой source работы нужны осмысленные RED/GREEN:

| Гипотеза / минимальная область | Будущая проверка и expected RED→GREEN | Подтверждение / опровержение |
|---|---|---|
| H5: L1 не наблюдает M2 handlers | Harness protocol tests: две разные source lanes/windows, реальные callbacks/reads/creates, false→nonzero source evidence; golden failed A37 raw fields всё ещё16/17. Files: existing L1 CLI, Host fixture, owned handlers, manifest output routing. | GREEN означает корректный измеритель, не memory fix. Если handler identity/counts не совпали — run invalid. |
| H4: fake state / unsafe load dictionaries | Сначала отдельный diagnostic protocol: определить owner/retained graph и новое различающее наблюдение сверх уже сохранённых counters. Для M2 адресно проверить existing handlers: overlapping foreground/recovery, expected key/book count, declared fixture bound; корректировать только найденный дефект. Новая система pools/таблиц ответов не обязательна, очистки ради green нет. | Counts/bytes/root evidence могут подтвердить удержание fake; стабильные fake metrics при росте памяти опровергают fake как единственную причину. Диагностика не зависит от green M2. |
| H1: snapshot serialization | Existing HotelPrivateSelectionTests/HotelSearchTests: expiry/eviction вместе с private handle, mutation isolation, rejected-large entry. Дополнять только отсутствующий сценарий; уже зелёное не называть RED. Future allocation attribution на том же fixture payload. | Optimization только если профилирование укажет этот путь; RED по сохранённому behavioral invariant + воспроизводимый allocation regression с заранее согласованным пределом. Не вводить произвольный allocation threshold сейчас. |
| H2/H3: cancellation/HTTP lifetime | Existing HotelParallelSearchTests, HotelAggregateDeadlineTests, HotelReadExecutionBoundsTests, HotelDeadlineTests; добавить disposal-counting owned streams и repeated completed cancellation, если покрытия нет. RED leak/early release/late publication → GREEN exact-once disposal и bounded active до completion. | Если underlying operations завершаются и leases/CTS/streams освобождаются без роста retained roots, гипотеза этого leak отклоняется; рост RSS сам по себе недостаточен. |
| H6: observer/native/GC/environment | Сохранить1Hz counters, отдельные process identities, full retained vector, natural-GC indices; при необходимости отдельный согласованный diagnostic trace, без forced GC. | Стабильные сопоставимые post-natural-full-GC heap values поддерживают иной вклад; отсутствие такой пары означает unknown. Нужда в native trace — новый scope, не скрытый fallback. |

Root cause окончательно **не установлен**. Нет основания прямо сейчас выбирать cache rewrite, forced GC, TTL reset, runtime GC tuning или ослабление gate. Полный M2 workload и resource accounting — в [load-plan.md](load-plan.md).

## 7. Уточнения M2 profile по полному review — 2026-10-29

Эти факты относятся к подготовке нагрузки, не являются найденной причиной исторического memory failure:

- [HotelbedsStayProvider.GetSupport](../../../modules/hotels/Travel.Modules.Hotels.Infrastructure/Providers/Hotelbeds/HotelbedsStayProvider.cs), строки73–85, требует совпадения request.Area.Fictional с options.Fictional. При owned fictional profile London отклоняется доHTTP; использовать существующий areaKey="fictional"/HotelSearchService.Areas[3], не ослаблять guard.
- В этом же provider строка135 пока отправляет hotels.hotel=[900001]. Поэтому S/L с9/100HBXproperties требуют явного узкого расширения **fictional request/catalog**, а не только увеличенного ответа handler. Оно описано как будущий source task3.1, default и external branch сохраняются. В текущем коде такого расширения ещё нет.
- Existing [RecoveryAt](../../../tools/demo/Travel.Hotels.Fixture/HotelsL1Runner.cs), строки993–1011, оценивает offered jobs относительно конца скользящего окна. Новому M2 evaluator нельзя передавать заранее вычисленный phase-success boolean: он либо засчитает позднее подтверждение в прошлом, либо навсегда отбросит покупку, завершённую после прежней phase boundary. Raw timestamps и два отдельных predicates определены в load-plan v3; historical M1 evaluator/result не меняются.

