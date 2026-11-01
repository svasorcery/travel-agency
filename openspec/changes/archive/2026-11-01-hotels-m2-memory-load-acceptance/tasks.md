# Tasks

**8/8 задач выполнено. Локальная fictional M2 нагрузочная приёмка принята по согласованной acceptanceVersion5.** Один полный S/L v7: **S23/23,L23/23**,6304outcomes/722samples,exit0,независимый replay совпал. [Итоговый отчёт](load-sl-v7-result.md).

PeakRSS488.129/507.277МиБ;final−baseline64.949/58.426МиБ<128. SpositiveRSStrend остаётсяfalse/diagnostic, безwaiver исторических failures. Source/build,485priorraw/52newraw иownedcleanup проверены. M1failed16/17 неизменён. Публикация принята через PR86/PR87 с финальными20/19PR/post-merge jobs; main spec синхронизирован, change архивирован2026-11-01. Real suppliers иtarget остаются отдельными. [Финализация и точные receipts](archive-verification.md).

## 1. Минимальный измеритель

- [x] 1.1 После разрешения source-среза связать existing runner с обоими M2 typed handlers и отдельными source capacity/start/release counters. Verification: focused regression ловит прежнее single-Supplier/summed-window наблюдение, затем показывает оба источника, точные actual HTTP starts и exact-once release без изменения admission rules. Записать scope и receipts.
- [x] 1.2 Добавить полный denominator, необходимые memory/retained-state counters и уникальный M2 manifest/evidence path в существующие helpers; raw facts использовать раздельно в PhasePurchaseSuccess и WindowPurchaseSuccess. Verification: missing/cancelled/refused jobs не теряются; due284.5/Confirmed301 неуспешен для phaseEnd300, но успешен для windowEnd305; Confirmed310 не засчитывается в window305. Old M1 path не перезаписывается, offline historical evaluator остаётся16/17; lifecycle/overflow tests только в разрешённой source границе. Документировать observation overhead и разделение Host/driver.

## 2. Диагностика и условное исправление

- [x] 2.1 Подготовить один конкретный diagnostic protocol по research H4 либо более сильному source evidence: новое различающее наблюдение, неизменяемые inputs, один изучаемый фактор, точные data/concurrency/duration/cycles и локальные safety budgets. Verification: protocol позволяет подтвердить или опровергнуть указанную ownership/retention гипотезу и не повторяет уже неинформативный общий RSS run; команду и hashes предъявить до разрешения запуска.
- [x] 2.2 Только после отдельного разрешения выполнить этот frozen diagnostic experiment, сохранить raw facts и cleanup receipt. Verification: число частей/прогонов совпадает с разрешением, failed/cancelled остаются видны, historical receipts неизменны; вывод разделяет code/method/environment и допускает unknown. Приёмка M2 не является условием запуска диагностики.
- [x] 2.3 Зафиксировать решение по root cause: при подтверждённом дефекте предложить и после разрешения выполнить минимальный patch с meaningful RED/GREEN; иначе явно записать, что patch не обоснован. Verification: причинное evidence и сохранённые business/privacy/claim invariants; ни fixture cleanup, ни forced GC, ни ослабление gates не используются как исправление. Новый runtime требует нового разрешения.

## 3. Отдельная нагрузочная приёмка M2

- [x] 3.1 После разрешения именно этого source-среза подготовить S/L на existing Host fixture/handlers с areaKey="fictional" и явно расширить fictional request/catalog в HotelbedsOptions/HotelbedsStayProvider: FictionalHotelIds1–100unique positive IDs, default[900001], S=900001..900009, L=900001..900100. Verification: captured-request RED на старом literal→GREEN9/100IDs; default1ID и external request shape прежние; invalid catalog и London+Fictional=true дают0HTTP. Owned handler проверяет request scope, mapping/group counts равны10/18 и100/200; сохраняются1/2adults, private contexts/bounds/deadlines и concurrency safety. Frozen packet включает catalog revision, exact bytes/seed/source-step counts, hashes и локальный budget. Нет обязательного Q3, нового engine, ослабления guards или скрытого изменения ordinary demo.
- [x] 3.2 После отдельного разрешения выполнить каждый из двух360s профилей один раз с6304planned public jobs на весь пакет, stop-on-failure и own cleanup. Verification: для текущей acceptanceVersion5 обязательные13 original +10M2 checks и четыре legacy diagnostics представлены раздельно по [acceptance-v5](acceptance-v5.md); прежние boolean/версии сохранены без waiver исторических результатов; all failed/cancelled/not-run учтены, late HBX confirmation не улучшает прошлое recovery window, thresholds/source unchanged. Не запускать диагностику или retry автоматически.
- [x] 3.3 Выпустить итоговый report по двум профилям и отдельному diagnostic result. Verification: исходный M1 всё ещё failed16/17; M2 accepted только при полном прохождении обоих нагрузочных профилей и проверенной evidence/cleanup; пропущенный профиль — not-run, неизвестная причина — unknown, не supplier/target/hard2GiB/leak-free claim. Публикация и sync/archive не выполняются этим task.

## Traceability

| Delta scenario | Tasks |
|---|---|
| Short resilience run; Overload and evidence denominator |1.2,3.1–3.3 |
| M1 completion with an explicitly accepted memory limitation |1.2,2.2,3.3 |
| Independent M2 load acceptance |3.1–3.3 |
| Complete source and campaign accounting |1.1–1.2,3.2 |
| Protocol-faithful fictional inventory |3.1–3.2 |
| Fixed phase and recovery window have separate cutoffs |1.2,3.2 |
| Memory measurements retain their scope |1.2,2.1–2.3,3.3 |
| Frozen workload and whole-contour budget |2.1,3.1 |
| No result-driven tuning or repair claim |2.2–2.3,3.3 |
| Controlled stop and exact resource ownership |1.2,2.2,3.2 |
| Planning and external authority remain separate |1.1,2.2,3.2 |






2026-10-31: H5 executor source preparation завершена,52 H5 /252 permitted standalone tests; review fixes verified. Новый runtime не выполнялся. [Подготовка исполнителя](host-private-executor.md);6/8 и pending3.2/3.3 не изменены.

## H5 runtime checkpoint — 2026-10-31

Один H5 выполнен после «го» и принятия владельцем VMMap EULA. Получена raw P0; runner отказал из-за неверного XML shape, а post-run identity — из-за6 служебных файлов, созданных collector в worktree при отсутствующем SystemDrive. P1/P2 и56warmup jobs not-run;cleanup подтверждён, extra files сохранены сhashes вevidence. Baseline private≈262MiB; причина прежнего роста до≈1.2GiB остаётсяunknown. Повтора/source patch не было. [Результат и offline анализ](host-private-diagnostic-v2-result.md).6/8 неизменно.

## H6 preparation checkpoint — 2026-10-31

После H5v4 выбран proposed native-heap attribution protocol: PID-only WPR HeapSnapshot, два allocation/stack snapshots + VMMap, только owned Host. Проверены local WPR help/profile/status и disabled dotnet heap flags. WPA отсутствует, token неAdmin; новый recorder/controller ещё не реализован. [H6 protocol](native-heap-attribution-protocol.md) прошёл read-only builder check без существенных открытых вопросов для scope подготовки. Протокол готов,executionReady=false; installs/ETW/Host/DB/нагрузка не выполнялись,189priorraw неизменны. Приёмка S/L и runtime gates остаются открыты,6/8.

## H6 source checkpoint — 2026-10-31

WPA11.8.423 установлен; исполнительH6v2 подготовлен,351/351standalone(63H6),2Importantreviewfindingsисправлены,freeze/archive/verify-onlyготовы. НовыйHost/DB/UAC/ETWэкспериментне выполнялся;physicalruntime/ETLqualificationpending. ОдинMinorпотеричастиWPRstdout/stderrприtimeoutоставленявно. [H6 source verification](native-heap-executor-v2-verification.md).6/8 и3.2/3.3unchanged.

## H6 v2 invocation checkpoint — 2026-10-31

После разрешения сделан один CLI вызов H6 v2. Он отклонён до runtime: ассистент передал packet path со смешанными separators, frozen canonical-path guard сработал. Host/helper/UAC/ETW и снимки не запускались; повторов не было. Исправленные аргументы подготовлены и проверены offline, source/build/freeze и189raw сохранены. [Результат вызова](native-heap-diagnostic-v2-invocation-result.md). H6 memory diagnosis остаётся pending,6/8 неизменно.

## H6 v2 result — 2026-10-31

После исправления ошибки CLI без нового согласования выполнен один actual H6:85.34s,56/56responses,36IPC,3VMMap,2heap snapshots,cleanup/comparisontrue. Tracked outstanding+933.14MiB объясняет99.72%heap-private growth; двеLocalAllocцепочки+920.40MiB имеют одинаковые DLL-size histograms. Сильный кандидат — повторные metadata references JasperFx/Roslyn; точные managed call sites/lifetime defect не доказаны,productpatch не внесён. [H6 result](native-heap-diagnostic-v2-result.md).6/8,S/L acceptancepending; новыйнагрузочныйrun не начат.

## Metadata lifetime source-fix — 2026-10-31

Проверен реальный Roslyn ownership contract; Host использует existing IAssemblyGenerator со scoped AssemblyMetadata и детерминированным Dispose после Emit. Source-fix готов:13compiler tests,243architecture,351Hotels standalone,18pure;review0Critical/0Important/1Minor.272raw и168H6analysis сохранены. Нового Host/DB/load/UI/ETW не запускали, RSS effect не принят. [Verification](compiler-metadata-lifetime-verification.md).6/8 и3.2/3.3 неизменны.

## H6 v3 after fix — 2026-10-31

Один runtime82.82s:56/56responses,35IPC,P0/P1,ноt12dueWork1/future16 запретилB/P2. Наобщихobserved-counterточкахt0…8 private memoryсущественнониже; t8:882→253MiB(−71.28%). WPRstop отдельно отказалс0x80010106,полногоETLнет. Cleanupподтверждён;новыйrunнеповторялся. [Частичный result](native-heap-after-metadata-fix-result.md). ПолныйH6/M2loadнепринят,6/8. СледующаяприёмкаS/Lне требуетWPR.


## S/L v3 after metadata fix — 2026-11-01

Один пакет: S полностью измерен (3152/3152 jobs, 361/361 samples), 23/24 gates; L not-run. Peak RSS 1320.23→453.49 МиБ (−65.65%), peak private 1230.52→354.69 МиБ (−71.18%). Единственный отказ — strict final RSS +4096 B при стабильных private/GC committed; полный retained state окон различается из-за планового search354. Cleanup/source/build/345priorraw подтверждены, 28 новых raw artifacts сохранены. Retry/threshold change/WPR/UI отсутствуют. [Полный разбор](load-sl-v3-result.md). План6/8, M2 acceptance pending.

2026-11-01: Owner approved future acceptanceVersion2 preparation; source/test/freeze in progress, runtime not authorized. See [revision](acceptance-v2.md).


## Acceptance revision2 source checkpoint — 2026-11-01

Owner-approved future classification implemented: version2 has23mandatory gates and separate unchanged false/true final5s RSS diagnostic. Version1/M1 remain unchanged.20focused/371standalone/48actualevaluator; meaningfulRED→GREEN; freshreview0Critical/0Important/0Minor. New load-sl-v4 freezes2018source/input+999build,16unchangeddatafiles,373priorraw; archive3020entries verified. NoHost/DB/load/WPR/UI. [Verification](load-sl-v4-verification.md).6/8 and runtime3.2/3.3pending.


## S/L v4 runtime checkpoint — 2026-11-01

Один разрешённый campaign820.730s, S23/23 иL21/23, полный6304job/722sampledenominator. LfailedworkingSetRecovery иdueWorkAndInboxSettled; version2diagnostic Sfalse/+12KiB,Ltrue/−437.6KiB сохранена отдельно. Все373priorraw и52newraw сохранены, source/buildidentity иownedcleanup подтверждены. No retries/sourcechanges/thresholdchanges/WPR/UI. [Полный результат](load-sl-v4-result.md). FullM2acceptancefalse,6/8.







## Final acceptance — 2026-11-01

Owner-approved version5 full S/Lv7 accepted:23/23each,6304terminaloutcomes,722samples,exit0. Independentbudget/trend/cohort/queue replay and allownedcleanup/sourceidentity verified. Tasks3.2/3.3complete;8/8. Historical failures remain immutable; source changes local/uncommitted, no publication/sync/archive/rollout. [Finalreport](load-sl-v7-result.md).
