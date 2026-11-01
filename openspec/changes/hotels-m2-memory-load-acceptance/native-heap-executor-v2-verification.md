# H6 v2 — source-ready, без нового эксперимента

2026-10-31. Выполнено разрешение на установку WPA и подготовку исполнителя H6. **Исходники, tests, независимое review, исправления, freeze и verify-only готовы. Новый эксперимент не выполнен.** План остаётся6/8; задачи3.2/3.3 открыты.

## Что подготовлено

- Официальный Microsoft WPA установлен через Microsoft Store, package11.8.423.0/CLI11.8.423.12582; WPAExporter и native perf_heapsnapshot.dll проверены и включены в tool-hash inventory. WPR10.0.19041.6093/SHA8C3D49A83EBD8138FFA15DA93D25BA1FC44ACDEC3CB9D729B6F43B55A5EF7CA5 неизменен.
- Ordinary driver/Host и отдельный fixed-command elevated WPR helper, точная привязкаPID/start/parent/user/image/build, scoped instance, strict IPC, защищённый trace output и hash-acknowledged copy.
- Та же S-нагрузка56jobs: WPR A→P1, quiescent t12→B→P2, дополнительный post-capture sample. Totals57public/36IPC/3VMMap/2heap snapshots/1ETL. Изменение source/retained/future-work делает comparison contaminated/unknown.
- Один attempt, stop-on-error/no retry. Независимая очистка trace и Host. Необязательные memory counters не блокируют cleanup. Projected copy size и блоки1MiB сохраняют combined evidence bound.
- Pinned executable packet: TestResults/hotels-m2-memory-load-acceptance/diagnostic-h6-native-heap-v2. [Команды пакета](../../../TestResults/hotels-m2-memory-load-acceptance/diagnostic-h6-native-heap-v2/commands.md).

## Проверки и receipts

| Проверка | Результат |
|---|---|
| Permitted Hotels standalone |351/351,0skipped;63H6, прежние288 сохранены |
| H6 sequencing |5RED→GREEN; H5/H6 replay26/26, реальный сохранённый JSON без нового Host |
| Process/IPC boundary |14RED→GREEN, native own-process identity и in-process named pipe |
| Trace policy |23RED→GREEN |
| Protocol wire |7RED→GREEN |
| Native output/session |2RED→GREEN;4теста включая read-only ETW ABI query |
| Независимая очистка |3RED→GREEN |
| Review regressions |6RED+1existingGREEN→7/7GREEN |
| Final wrapper build |0errors,1прежний Verify SolutionDir warning |
| Pure evaluator |18/18 |
| Manifest tests |4/4 |
| Historical M1 replay |16/17, workingSetRecovery=false;5legacy evaluator sections unchanged |
| Historical raw integrity |189/189, H5v4archive/H6v1protocolfreeze неизменны |
| Final verify-only |sourceReady=true,executed=false,runtimeAuthorized=false,runtimeQualified=false,analysisQualified=false,EULA=true |
| OpenSpec |strict valid; tasks6/8 |

Logs/TRX/installation receipts: TestResults/hotels-m2-memory-load-acceptance/source-h6-v1. Итоговый test receipt standalone-final.trx; build-final.log; evaluator-proof.json; manifest-tests.log; historical-offline-replay.json; preserved-integrity.json. Первая попытка чтения Appx черезPowerShell7 была несовместима с модулем; metadata прочитаны штатнымWindowsPowerShell, установка не повторялась.

Независимое [review](native-heap-executor-v2-review.md):0Critical/2Important/1Minor. Два Important исправлены одним TDD проходом,351/351послеисправлений, повторное ревью не выполнялось. Отложенный Minor: при timeout/cancellation/output-bound WPR часть stdout/stderr может не попасть в result. Команды/identity/timestamps/error/raw сохраняются, cleanup не блокируется; автоматический повтор запрещён. ПрежнийH5XMLtest minor внеH6slice сохранён.

## Freeze

Branch codex/hotels-m2-memory-load-acceptance; base/HEAD c3fd3a6b3e8965ad8cf7850e160f22edfa12dedf, ancestry подтверждена, index пуст.

-2002source/input files;998build files;335runtime files+1dotnet host;19explicit inputs.
- Source manifest SHA256:715B0DB7A8C4510DD4F549D209559406E9731CD03EBD54BF207022FA77A7582B.
- Freeze SHA256:0E3DCEF29C5DF9DBCA575C88A567477E528A8810BC14B21E0B9DA805FD0C428B.
- ZIP:3003entries,56010635bytes; каждый entry перепроверен.
- Archive SHA256:54AC52CE7CA6784EF7A294025E679953FB57C479AE898A1B14370E074F6DD713.
- H5v4 archive SHA256:AF8564069027FB88E331E7EFEA7ACCCD891D53AF34B6DE5CEA2F07D1FF51CAE1.
- H6v1 protocol freeze SHA256:56988EF4825630958E848A189459094B0F89853BF3CCA1DD53C8F5F77B821EB9.

Final verify-only использовал именно frozen source/build/runtime/tool bytes. Выполненная команда не содержала --execute. На проверке свободно≈19.2GiB physicalRAM/≈10.1GiB disk; это наблюдение подготовки, не гарантия будущего запуска. Даты2026-11-06→08 не сдвигались; пакет применим для старта до2026-11-04UTC включительно.

## Решения и незакрытые границы

1. Windows CurrentUserOnly не переносится на mixed-elevation pipe: применены явныйACL, OS peerPID и Identification-level elevated client. Protected temp принадлежитAdmin/System, ordinarydriver только читает; cost — физическая cross-elevation/ACL работа ещё требует первого отдельно разрешённого runtime.
2. При неизвестном PID/session/schema/elevation/cleanup — отказ/incomplete, без retry и без действия на чужойsession/новыйPID. Cost — даже зелёный source package может остановиться в первом реальном запуске.
3. Acquisition и analysis раздельны: ETL не анализировался; exit0/непустойфайл не доказывают два корректныхsnapshots или отсутствиеloss. [WPR PIDHeapSnapshot](https://learn.microsoft.com/en-us/windows-hardware/test/wpt/record-heap-snapshot) задаёт метод, а [QueryAllTraces](https://learn.microsoft.com/en-us/windows/win32/api/evntrace/nf-evntrace-queryalltracesw) возвращает sessions в пределах правcaller; ordinary query не подтверждает elevated isolation.
4. Все declined-to-judge runtime случаи полностью перечислены вreview:UAC/pipe/ACL/PIDraces/driverdeath/ETWisolation, реальныеtiming/resource/mergepeaks,ETLloss/completeness/coverage,WPAexport/symbolnetwork иnativeowner/busy-free. Эти проверки не заменены mocks/source success. Cost — причина роста private memory покаunknown,productpatch не обоснован.
5. Сохранены историческиеraw/ошибки/пороги. M1failed16/17,S-v2failed22/24,Lnot-run. Старые полныеaudits,CI,suppliers,M2load,target/rollout не принимаются этим source slice.

Не выполнялись новый Host/DB/VMMap/WPR recording, heaptracking enable/snapshot/disable/stop/cancel, UAC/helper execution, symbols/download/upload, forcedGC/cacheclear, provider/model calls, stage/commit/push. Read-onlyWPR-status: not recording; normal token;dotnetIFEOabsent;H6attempt/runabsent. WPA installation — единственное явно разрешённое системное изменение.

Следующий технический шаг — отдельно разрешённый **один H6 v2 runtime** с UAC толькодляWPRhelper и последующим анализом двухheap snapshots. BuiltinETL содержит общесистемныеProcessThread/Loader/CpuConfigmetadata,не толькоцелевойPID; остаётсялокально. Приошибкеостановкабезповторов. Это не S/Lacceptance и не закрытиеплана.
