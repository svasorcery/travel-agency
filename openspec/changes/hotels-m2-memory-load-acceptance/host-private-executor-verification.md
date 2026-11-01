# H5: подготовка исполнителя завершена

2026-10-31. Реализован короткий диагностический режим `m2-h5`; новый эксперимент не запускался. Пакет: `TestResults/hotels-m2-memory-load-acceptance/diagnostic-host-private-v2/`. [Метод и реализация](host-private-executor.md), [точные команды](../../../TestResults/hotels-m2-memory-load-acceptance/diagnostic-host-private-v2/commands.md).

## Что проверено

- 252/252 разрешённых standalone-теста Hotels, включая 52 H5; пропусков нет. Docker/Host suites исключены.
- Независимое review: 0 Critical, 5 Important, 0 Minor. Все пять замечаний исправлены одним проходом; шесть воспроизводящих регрессий сначала RED, затем GREEN. Повторного review не было.
- CLI собран: 0 ошибок, 1 прежнее предупреждение Verify об обнаружении solution. Formatter: 14 файлов.
- Pure evaluator: 18/18, HTTP=0, Host=0, database=false. Manifest tests: 4/4.
- Offline M1 replay: прежние 16/17, workingSetRecovery=false; пять разделов evaluator не менялись.
- 68 исторических raw artifacts сохранили SHA256. Старый H5 preparation-v1 также проходит read-only verifier.
- OpenSpec strict valid; план остаётся 6/8, задачи 3.2 и 3.3 открыты.
- Новый verify-only вернул sourceReady=true, executed=false, executionReady=false, vmmapEulaAccepted=false. Ни attempt-1.json, ни run-01 не созданы.

Все receipts: `TestResults/hotels-m2-memory-load-acceptance/source-h5-v1/`. Основные: standalone-final.trx, review-red.trx, review.md, wrapper-final.log, packet-verify.json, evaluator-final.json, historical-integrity.json, historical-offline-replay.json.

## Зафиксированный пакет

1975 source/input файлов, 996 build файлов, 335 runtime файлов и dotnet host; 17 frozen inputs. 56 workload jobs, 57 public requests с preflight, 35 IPC samples, 3 карты. S dataset, даты и пороги сохранены. Архив содержит 2974 entries, 58098958 bytes.

| Артефакт | SHA256 |
|---|---|
| packet.json | 6055C070A7587688A3821B847BB40C34F042E25C05A7741E3B4925277773ABB7 |
| profile-S.json | 896FEE2B03007A85A5D13C17F3963DB68109E0FDF0437C33EF9086FC127AA887 |
| build-inventory.json | 2CAEB71A1659591686DF5CD51379AE019D08A7ABDE429D7B8D2159D54647018A |
| source-manifest.json | 6E8E5B67C3A5F91EDD742549A14DEE7F0FB7DE146451A7C3F0EF52F51520A2C5 |
| freeze.json | 5C228C534DE9EA1654CD871783F358DA5E02A5EFD8A5848401481ACF379EC057 |
| frozen-source-build.zip | 85FEB57DF57B143172381C1437AB6ABD159C718E259B3BE100673055F7339FE4 |

## Принятые решения и ограничения

1. Старые protocol/preparation/S/L receipts остаются неизменными; исполняемые inputs вынесены в v2. Запуск требует именно нового пакета, а не старого m2-load.
2. Source workflow остаётся в существующем isolated worktree без stage/commit/push и без удаления ledger. Подготовка не превращает прежние результаты в runtime acceptance.
3. Если старт был начат, но owned handle не возвращён, cleanup считается unknown/failed. Это может оставить консервативный отказ даже при успешной внутренней очистке, зато не скрывает потенциальные остатки; слепого повтора cleanup нет.
4. XML проверен на синтетических fixtures и экспортных именах из pinned VMMap binary. Реальная совместимость ещё не доказана. Неизвестный формат сохраняется и даёт incomplete; повтор автоматически не выполняется.
5. Windows timing, физические deadlines, actual Host/DB/collector cleanup, категории памяти/allocator/leak, supplier/target/rollout остаются отдельными runtime gates. Hash freeze проверен исполнителем после review. Deferred minors отсутствуют.

VMMap не исполнялся, EULA от имени владельца не принималась. Перед будущим запуском нужны личное принятие лицензии и отдельное разрешение эксперимента. До этого source preparation завершена, runtime readiness не заявляется.

M1 остаётся failed 16/17; S-v2 failed 22/24; L not-run. Никакого memory fix, ослабления порогов, forced GC или очистки cache/fake не было.
