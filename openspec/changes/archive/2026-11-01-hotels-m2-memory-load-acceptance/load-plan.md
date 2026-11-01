# План диагностики и нагрузки — v5, S/L source packet

2026-10-30. Diagnostic v5 выполнен один раз и разобран; результат — [diagnostic-v5-result](diagnostic-v5-result.md). Task3.1 реализует S/L и проверяется source-only; exact packet, review fixes, тесты и хеши — в [source-3.1-verification](source-3.1-verification.md). Нагрузочные S/L не выполнялись, task3.2 требует отдельного разрешения.

Исторические v2/v3/v4 отказы и подготовка v5 сохранены в verification/v5-verification; исходный M1 всё ещё failed16/17. Q3 не добавляется, численные gates не меняются.

## 1. Что нужно запустить и зачем

Для полной нагрузочной приёмки использовать уже имеющийся Testing Host/Kestrel, собственную одноразовую PostgreSQL/inbox/outbox, owned fictional Duffel/Hotelbeds handlers и отдельный driver. Это временные процессы на существующем ПК; новый сервер, VM, AppHost, Travel.AI, real Keycloak и supplier sockets не нужны.

Host включает runtime, EF/Wolverine, cache, fake handlers и observer. Driver history/checkpoint serialization уже вынесены из него. PostgreSQL и driver измеряются отдельно; in-memory CAS пригоден для source regressions, но не заменяет real-store acceptance. Все данные вымышленные, внешний API budget **$0**, без saved credentials/billing.

## 2. Диагностика памяти выполняется отдельно

Конкретный protocol H4-M2 v1 зафиксирован в [design, раздел2](design.md#2-диагностика-памяти--protocol-h4-m2-v1): один standalone m2-memory процесс,8search+1expired-rates,3heap snapshots и штатный TTL302s. Проверяется graph ownership обоих current M2 fake handlers после expiry product cache. Это не повтор original M1 и не причинная атрибуция его RSS; old H4 для другого M1 fake остаётся отдельно не доказана.

Локальный пакет TestResults/hotels-m2-memory-load-acceptance/diagnostic-h4-m2-v1 содержит точную execute-команду, pinned tool, source/build/tool/runtime hashes и bounded safety/cleanup. Default invocation только проверяет hash inventory. V2/v3 failures сохранены. Отдельно разрешённый v4 остановился после первого успешного search из-за неверного numeric enum guard;1/3baseline snapshot разобран, B/C отсутствуют, H4-M2 unknown. Исходный frozen design v4 сохранён отдельно; текущий design задаёт проверенный v5, фактические прошлые результаты — в verification. Новый запуск не выполняется автоматически. Подготовка не заменяет experiment/analysis.

Дампы отделены от load acceptance: collection меняет page residency. Нет forced GC, clock advance, direct cache/fake clearing, повторов или подмены failed16/17. Результат может быть confirmed/refuted/unknown для конкретной ownership-гипотезы. H4-M2 не устанавливает product leak, original M1 root cause, M2 performance или hard2GiB capacity. S/L ниже не меняются.

## 3. Два отдельных профиля M2

Ниже **P** — замороженные параметры подготовленного профиля, не выполненный прогон. Численные product bounds/gates **E** наследуются из Hotels contracts и L1.

| Профиль | Fixed fictional данные | Timed цикл |
|---|---|---|
| S — малый |1adult,8 reviewed pairs +2 unmatched single-source properties:10groups/18leaves,1rate/property | Один360s |
| L — cardinality boundary |2adults,100 reviewed pairs/200source properties:100groups/200leaves,1rate/property | Один360s |

Для обоих: **areaKey="fictional"**, существующий HotelSearchService.Areas[3] с Fictional=true; check-in = UTC date captured before packet +7days, checkout+2days; seed20261029. Абсолютные dates фиксируются в packet. London не используется: при owned fictional Hotelbeds он даёт DestinationUnsupported до HTTP. Guard совпадения destination/profile сохраняется и получает отрицательную regression на London. One-room, Hotelbeds Net/AT_WEB; exact source selection, consent, private result/reload и no-reference Unknown без resend/discovery/failover сохраняются.

Fixed data: GBP120.25/130.25 и explicit0local; каждая пятая **matched pair** имеет EUR альтернативу, каждая седьмая matched pair — unknown local obligation у одного leaf. У single-source unmatched properties альтернативу не добавлять:1rate/property сохраняется. Для purchase выбираются только заведомо eligible leaves с известными obligations/cancellation. Public ranking сохраняет scope/unknowns, HTTP200 не превращает partial result в healthy success.

### Запрос и fictional-каталог должны совпадать

До task3.1 Hotelbeds provider отправлял hotels.hotel=[900001]. Task3.1 добавляет в **fictional branch** HotelbedsOptions/HotelbedsStayProvider: bounded конфигурационный список FictionalHotelIds, default=[900001],1–100 положительных уникальных integer IDs. Empty/duplicate/nonpositive/>100 отказываются до HTTP, без молчаливого dedup/truncation. Request содержит ровно этот список в стабильном ascending порядке. Поле не поступает из пользовательского API; external geolocation branch, typed-handler/destination/rights guards и Production exclusion остаются прежними.

| Профиль | HBX request/catalog | Duffel fixture properties | Reviewed pairs / результат |
|---|---|---|---|
| S |900001..900009, ровно9 IDs |acc_load_001..acc_load_009, ровно9 |Пары001..008; девятая property каждого source unmatched:10groups/18leaves |
| L |900001..900100, ровно100 IDs |acc_load_001..acc_load_100, ровно100 |Пары001..100:100groups/200leaves |

Catalog/list и matching evidence фиксируются при composition каждого профиля, входят в dataset hash и его существующий ConfigurationRevision; изменения требуют нового profile revision/packet, hot reload не добавляется. Ordinary demo без новой настройки продолжает отправлять [900001]. Healthy owned handler проверяет фактический список запроса и возвращает только его properties; unexpected list/property даёт harness failure. Нельзя моделировать hundred-property success ответом на single-ID request.

Выполненные source regressions: captured request9/100IDs (старый literal[900001] даёт RED), default1ID остаётся прежним, invalid catalogs дают0HTTP, London+Fictional=true по-прежнему Unsupported/0HTTP, actual external request shape не меняется. Они проверяют protocol-faithful fictional scope, а не доступ к поставщику. Число HTTP starts не меняется: каждый такой search/rates остаётся одной Availability operation.

HBX raw opaque key:64UTF8 bytes вS,2048 вL (P); **весь** serialized SelectionContext вместе с wrapper≤4096bytes (E). Raw key4096 с oversized wrapper проверяется отдельной source regression, не healthy нагрузкой. Exact raw/public/private payload bytes/hash каждого fixture фиксируются до запуска; raw body/full entry≤2MiB. L максимален по cardinality, не одновременно по всем byte bounds.

### Existing fixtures, без нового engine

Переиспользовать существующие owned handlers и опубликованные fictional shapes. Синхронизацию overlapping foreground/recovery и нужный набор properties добавить адресно, если их нет. Не строить общую registry, таблицу всех job→response objects или preallocated pools.

До freeze рассчитать maximum retained keys/quotes/bookings для выбранного schedule и seed; явный test-only bound должен вместить план, а unexpected overflow даёт harness failure. Ordinary demo limits/behavior сохраняются. Если существующий128-key cap мешает заявленному профилю, согласовать и проверить узкое расширение **fixture**, не product budget. Count/bytes всех удержанных fixture данных входят в evidence; runtime cleanup ради green запрещён. Source-level overlap/shape/bound tests предшествуют реальной нагрузке.

Seed упрощён до **38 reservation rows (P)**:24drafts (12/source),2confirmed private-status (1/source),12Duffel spent-no-ID с known fixture booking identity для11 first-ID callbacks. IDs = seed/profile/role/index. Burst использует одну уже confirmed Duffel reservation с разными event IDs; identity/read allowance не сбрасывается. Нет дополнительных25terminal rows только ради размера списка.

17timed prepares/creates чередуются по source: S начинает HBX, L Duffel. Один draft используется не более чем одним create; сохранённый Unknown не получает новый resource. Все quote/admission/protection facts готовятся обычными типами и связаны с source. Fixture seed — отдельные counts, не timed throughput; no direct DB seed/reset во время нагрузки.

Pre-timer cache proof сохраняет420direct fixture Put:140small→140large→140small. Small=1group/2leaves; large=100groups/200leaves с valid handles; это отдельный synthetic cache dataset, не публичная выдача S. Проверить128entry eviction и16MiB byte eviction, complete entry≤2MiB; final Count128. Exact cache seed bytes/cardinality замораживаются до запуска. Если large не достигает byte bound — исправить dataset на source этапе, не добавлять padding после load результата. BeforeSeed/afterSeed/afterCacheProof/beforeTimed snapshots раскрывают cold allocations; cache/GC после seed не сбрасываются.

## 4. Schedule и полный denominator

Сохранить existing L1 monotonic schedule. Intervals start/duration: (0,30),(30,120),(150,60),(210,30burst),(240,120). Status offset0/step0.2s; search offset0/step6s, burst step0.025s; callbacks offset8/step30s, burst0/1s; вне burst rates1.2/15s, prepare3/20s, create4.5/20s. Все jobs и prerequisite/source IDs записываются заранее; tie-break = kind ordinal + original ID.

| Phase | Seconds | Работа |
|---|---|---|
| Warmup |0–30|normal mix/handler100ms; исключён только из latency/success gates |
| Normal |30–150|search10/min,group rates4/min,prepare3/min,create3/min;status5/s,callbacks2/min |
| Degraded |150–210|тот же mix + fixed faults ниже |
| Burst |210–240|search40/s,status5/s,callback1/s;handler500ms |
| RecoveryAdmission |240–300|normal mix/100ms, все отказы видны |
| RecoverySteady |300–360|normal mix и заранее заданные gates |

Per profile:1255search+22rates+17prepare+17create+1800status+41callback=**3152jobs**; два профиля=**6304planned public jobs**. Seed/preflight, actual supplier HTTP steps, derived reads, cache-fill и cleanup учитываются отдельно. Прерванные/not-run jobs не исчезают.

E: module2aggregate/no queue; каждый source foreground1active/24starts60s и recovery1active/6starts60s; оба source вместе≤4active. HBX Rates вызывает Availability, Prepare — CheckRate; все HTTP steps входят в свой budget. Nominal non-burst foreground≤18starts/source/min, но actual windows/seed/dependencies проверяются до freeze, без counter reset. Driver P:64concurrent dispatch tasks/no queue; переполнение остаётся driverCapacity/not-dispatched в ledger, не supplier refusal.

### Fault cases

- Degraded read/prepare ordinal (zero-based, заранее по source/job/step): mod10=0/1/2 →3s delay; mod10=9 →safe502, остальные100ms; не зависеть от порядка completion.
- Один lost-reply create154.5s: HBX вS, Duffel вL. Для его chain search150→group rates151.2→prepare153 обе source reads и selected prepare имеют successful100ms override выше fault mapping. Create удерживает fictional effect и теряет reply≤1s; Unknown/spent claim,0repeat/failover. Недостигнутый prerequisite — failed assertion, не запасная покупка.
- Search162s: caller abort50ms, один child cooperative, другой ignores cancellation до18s; bounded permit удерживается до actual completion,0late snapshot. Search186s:100ms/16s sources, truthful Limited/Timeout под15/17s budgets.
-11non-burst Duffel first-ID callbacks должны дать actual known-ID reads. Burst events идут к seeded confirmed Duffel. HBX timed creates, кроме lost reply вS, возвращают known reference без details; bounded reader завершает Confirmed. Это8planned HBX reference outcomes/profile; ledger хранит not-triggered при failed admission, каждый реально admitted known reference требует actual GET под3reads/5min/delays0,30,120s. HBX webhook не добавляется.
- HTTP90/140s, application145s, completion10s — existing controlled-clock regressions, не отдельные waits короткого load. Their green не является throughput/p95 evidence.

## 5. Gates не ослабляются

E для каждого S/L: normal и final steady≥95% useful search/rates/prepare/create; searchp95≤1000ms, rates/prepare≤20000ms, create≤150000ms; status/ACK≥95% иp95≤500ms; burst status/ACK≥95% иp95≤1000ms; useful recovery≤75s; final pendingInbox/dueWork=0; fixed38reservation rows; cache≤128/16MiB, entry≤2MiB; Host RSS≤1GiB.

Memory formula неизменна: baseline=last At<30; final≤baseline+128MiB **AND** mean[330,last]≤mean[300,330). Дополнительная original stability: mean[355,360)≤mean[350,355), минимум5samples/window, одинаковые reservation/inbox counts. Terminal inclusion раскрывается; никаких epsilon или округления перед сравнением. Equal DB rows не означают equal full retained dataset.

17historical gate families показывать отдельно от M2 additions; capacity24/6 и1/1 применять **к каждому source**, не к сумме. Historical receipt не редактировать. Original11first-ID-read check остаётся Duffel; HBX reference/read checks добавляются отдельно. Historical evaluator для сохранённых M1 данных остаётся неизменным; M2 asynchronous business success рассчитывается явно по двум определениям ниже, без общего mutable Useful flag.

P для healthy M2: nonempty aggregate, оба sources Complete, expected counts/ranking/unknown scope сохранены; Failed/Limited/Unsupported/Complete0 не получают healthy success credit. Degraded semantic correctness считается отдельно. По каждому source healthy purchase cohort≥95% PhasePurchaseSuccess иexactly1effect; Unknown не продажа. Report n: короткая серия не статистическая SLA.

Latency nearest-rank p95 поCompleted−Due всех terminal observations, отдельно service time/dispatch lag; raw429/5xx также входят. Missing/uncompleted не0ms: latency unavailable/censored и completion gate false. Planned = response + cancelled + failed-before-response + not-dispatched + unfinished/not-run; useful — subset, вычисленный для названной фазы/окна, reasons/source statuses отдельные разрезы.

HBX durable Confirmed может появиться после HTTP response. Сохранить неизменяемые facts: due, HTTPCompleted, source/resource, первый authoritativeObservedAt и exact effect count. authoritativeObservedAt — первое консервативное monotonic наблюдение parent, что durable row уже Confirmed, через bounded1Hz metadata projection всех38rows (наружу ID/outcome, без decrypt/StateJson); не время fake effect и не timestamp, восстановленный из финального receipt. Стоимость observer включена.

Для purchase с нормальным planned исходом общий predicate B = успешный terminal HTTP response, корректный source-bound durable Confirmed, exactly1effect, присутствующие HTTPCompleted и authoritativeObservedAt. Значения None/missing не заменяются нулём. Для каждого cutoff C проверять оба времени <=min(due+150s,C):

- **PhasePurchaseSuccess(p,phaseEnd)** = B и этот cutoff с C=phaseEnd; cohort выбирается по due в фиксированной фазе. Используется только для её success rate/throughput.
- **WindowPurchaseSuccess(p,end)** = B и этот cutoff с C=end; cohort выбирается по due в [end−60,end). Не читает PhasePurchaseSuccess и не ограничивается phaseEnd исходной операции.

RecoveryAt — earliest end из300..315, при котором полное60s offered window проходит95% gates четырёх stages/searchp95. Для create используется WindowPurchaseSuccess; для других stages — их semantic success и Completed<=end, также без предварительного отбрасывания по фазе. Raw HTTP p95 остаётся поCompleted−Due всех terminal observations. Fixed final60s проверяется отдельно с PhasePurchaseSuccess и phaseEnd=360.

Контрольные примеры для будущей evaluator regression (остальные facts B выполнены):

| due / HTTPCompleted / authoritativeObservedAt | Fixed phase | Recovery window |
|---|---|---|
|284.5 /285 /301 |RecoveryAdmission заканчивается300: false |end305, cohort[245,305): true |
|284.5 /285 /310 |RecoveryAdmission: false |end305: false; end315: true |
|304.5 /305 /310 |RecoverySteady заканчивается360: true |end305: false; end315: true |

Ни позднее подтверждение не улучшает раннее окно, ни неуспех прежней фазы не запрещает последующее восстановление. Bound150s и остальные численные gates не расширяются.

P sampling:361planned1Hz points0..360inclusive; actual timestamps сохраняются. Missing point илиlateness>1s делает measurement invalid; late observations не расширяют окно. Terminal sample заранее назначен, drain/cleanup observations не входят в recovery mean. Native/private/managed/GC/retained fake/cache/DB/work facts выводятся отдельно, без вывода об отсутствии утечки.

## 6. Обычный ПК: условия, учёт и предохранители

Новых требований к покупке CPU/RAM/диска нет. Сохраняются existing Host2CPU affinity иRSS1GiB gate; записать реальный processor count at CLR startup, GC mode, OS/.NET, RAM и Docker configuration. Это условия воспроизводимости, не доказательство capacity всей системы на2GiB.

| Ресурс | Что фиксировать до разрешения и во время запуска |
|---|---|
| CPU | Host affinity2; фактические CPU/CPU time Host,driver,DB. Driver и DB не требуют новых dedicated cores или global CPU pinning. |
| RAM | HostRSS/private/managed отдельно; driver и own DB consumption отдельно. Доступная системная память/commit до/после — признаки внешнего давления, не расход Hotels. |
| Disk | Exact baseline own DB/evidence/temp sizes и свободное место; packet задаёт output/file-size ceilings и минимальный свободный остаток, достаточный для failure receipt. |
| Time | P: M2 packet≤20min целиком:2×(setup/seed≤120s +timed360s +cleanup≤60s)=18min и≤2min metadata reserve. Нагрузочные/recovery окна не продлеваются. Диагностика имеет свой отдельный заранее заданный time budget. |

Численные free-memory/disk stop limits выбираются **до runtime approval** по доступному локальному резерву и планируемым evidence; сейчас не выдаются за измеренные требования продукта. Packet без конкретных значений не готов к запуску. Не требуется разработка отдельного Windows/Docker resource governor, установка новой VM или изменение shared Docker limits.

Не складывать VM и её container memory дважды; недоступный показатель остаётся unavailable. Полный отчёт перечисляет включённые компоненты и пробелы; нельзя утверждать whole-contour/hard2GiB capacity, которую не измерили. Если для выбранного эксперимента данных недостаточно или машина под внешней нагрузкой, остановиться и предъявить ограничение. Не менять hardware/settings скрыто и не называть environment mismatch product fix.

## 7. Packet, evidence и остановка

До first HTTP: exact source SHA/dirty inventory/build DLL+deps+runtimeconfig/profile/data/schedule hashes; runtime/image identity; parent/child PID+starttime/root/port; own DB/container/temp assets; Testing memory-only config-source metadata и **оба фактических owned typed handlers**. Flags сами по себе не гарантируют отсутствие supplier socket fallback. Никаких credentials/billing/system certstore reads или private content в логах.

Новый уникальный output `TestResults/hotels-m2-memory-load-acceptance/<run-id>/<purpose-profile>/`, nonempty fail-closed. Существующий source-manifest writer не должен перезаписать historical M1 path. Сохранять planned ledger до dispatch, terminal outcomes, source starts/admissions/read budgets,1Hz resources/retained vectors, phase gates, failure/cleanup receipts; hashes проверить после. Seed/fixture allocations и observer overhead раскрыть.

Manual stop, safety budget, wrong identity/source hash, unexpected external transport, evidence overflow/missing samples, child death или unplanned fixture work прекращают новые dispatches. Remaining jobs получают not-run/unfinished; claim/state не reset и purchase не resend. Cleanup bounded60s и не продлевает acceptance: если sender ещё жив, остановить только owned child, записать aborted/Unknown/nonzero, затем own DB cleanup. Не glob/prune/kill-all; только проверенные IDs/contained paths. Residual resources показать, evidence сохранить.

S и L исполняются по одному разу в отдельно разрешённом пакете. Failure/invalid/cancel останавливает этот load packet; второй профиль явно not-run. Диагностика памяти имеет **другой** protocol/approval и не находится после условного green gate. Ни retry/resume, ни новый experiment автоматически не начинаются.

## 8. Что означает результат

M2 bounded load accepted только если S и L полностью прошли исходные gates и M2 additions, evidence/source identity достоверны, safety budgets соблюдены и own cleanup подтверждён. Нет обязательного Q3. Failed/cancelled/invalid/not-run видны; частичный green не принимается.

Диагностический вывод отдельно: подтверждённая причина, отклонённая гипотеза или unknown. Он не меняет исходный failed16/17 и не заменяет нагрузочную приёмку. Ни один итог не означает real Duffel/Hotelbeds performance, отсутствие любых утечек, target readiness или разрешение на publication/deploy.



## 9. Подготовленный локальный пакет S/L v1

Пакет: `TestResults/hotels-m2-memory-load-acceptance/load-sl-v1/`. Dates2026-11-06..08, revision `load-s/l-20261029-v1-2026-11-06`; seed20261029. Fixture setup40Duffel+16HBX actual steps,14effects,38rows; runtime capacity после setup0. Caps rates: S3060/1658, L34000/17128 (Duffel/HBX), quotes/bookings64. Overflow — sticky harness failure, не product429.

Canonical raw/public/private JSON specimens и exact schedule сохранены в data/S и data/L. Их GUID/expiry/key ordinals/ciphertexts относятся к образцу; будущие wire bytes измеряются отдельно. Cache proof: small11631B, large1289817B, byte eviction13entries/16767621B, final128small/1488768B. Отдельные synthetic cache aliases непокупаемые; это storage proof, actual S/L purchase paths проверяются отдельно.

Предзаданные локальные stop limits: available RAM≥4GiB, free disk≥5GiB, own profile evidence≤512MiB; setup120s +timed360s +cleanup60s, campaign1200s. Это safety reserve текущего ПК, не требования Hotels. Read-only preparation snapshot сохранён в local-budget.json; перед фактическим запуском и во время него значения снимаются заново.

Сохраняются pre-start/before/timed/after/after-cleanup physical memory, commit, free disk/evidence size; own DB cgroup CPU/memory и apparent PostgreSQL directory bytes, own temp bytes. PostgreSQL directory bytes включают WAL и storage overhead, не являются размером Hotels rows. Нельзя суммировать эти величины с VM usage как независимые расходы.

Default CLI `m2-load <profile-S.json> <profile-L.json>` проверяет source/input/build/runtime inventory и не стартует Host/DB. Только явный `--execute` включает два последовательных профиля; его выполнение сейчас не разрешено. Любое изменение source/build/profile требует нового freeze до runtime. Отмена/deadline во время финализации запрещают passed; errors evidence не пропускают cleanup, final-facts.json сохраняет bounded durable rows для воспроизведения gates.
