# S/L v3 — результат после исправления metadata lifetime

2026-11-01. Выполнен один разрешённый пакет на исправленной сборке. **S полностью измерен, но не принят: 23/24 проверки. L — not-run по stop-on-failure.** Пик RSS снизился с 1320.23 до 453.49 МиБ; прежнее превышение 1 ГиБ больше не наблюдалось. Единственный отказ — строгий рост среднего RSS на 4096 байт между последними двумя окнами.

Повторов, изменения порогов, forced GC, очистки cache, WPR/VMMap/ETW, дампов и UI automation не было. Программные исходники и сборка совпадают с проверенным metadata lifetime fix; исторические результаты сохранены. Полная M2-приёмка остаётся открытой.

## Исполнение и полнота

| Факт | Результат |
|---|---|
| Пакет | TestResults/hotels-m2-memory-load-acceptance/load-sl-v3/ |
| Driver / Host | 64764 / 38540 |
| Время вызова | 10:39:13.655–10:46:44.243 UTC; 13:39–13:46 Москва |
| Весь вызов / campaign | 450.580 / 443.766 секунды |
| S timed / с finalization и cleanup | 360 / 368.344 секунды |
| Exit / остановка извне | −532462766 / forcedStop=false |
| S jobs | 3152 planned, 3152 observed |
| Terminal accounting | 3141 Response + 1 Cancelled + 10 NotDispatched |
| NotRun / Unfinished / ранние старты S | 0 / 0 / 0 |
| Samples | 361/361, 0.0757…360.0510 секунды |
| Максимальная задержка parent sample | 0.32465 секунды при лимите 1 секунда |
| Evidence / ошибки исполнителя | completeEvidence=true; failure=null, failureDetail=null, execution failures=[] |
| L | 3152 planned not-run; каталога run/L нет |
| Весь denominator | 6304 = 3152 S terminal + 3152 L not-run |

NotDispatched сохраняет десять недостигнутых prerequisites; замещающих покупок не было. Отрицательный exit отражает непройденный gate, а не новый сбой диспетчера или sampler. Finalization errors отсутствуют, authoritative final facts сохранены.

## Проверки и единственный отказ

**Исходные families: 16/17. Дополнения M2: 7/7. Всего: 23/24.**

Прошли useful search/purchase/status/callback, burst responsiveness, source budgets/concurrency, cache bounds, fixed cardinality, абсолютный RSS ceiling, workingSetRecovery, восстановление в 75 секунд, settled inbox/due work, actual HTTP permit windows, 11 first-ID reads, bounded purchase outcomes, lost reply и все семь per-source/semantic/resource M2 additions. Все 24 значения сохранены в run/S/receipt.json.

Не прошёл finalSameRetainedRowsMemoryStable:

| Окно | Samples | Сумма RSS, байт | Средний RSS, байт |
|---|---:|---:|---:|
| [350,355) | 5 | 2344734720 | 468946944 |
| [355,360) | 5 | 2344755200 | 468951040 |
| Разница | | 20480 | **4096** |

Допуск остаётся нулевым. Offline-пересчёт всех трёх memory gates совпал с runtime; analysis-v1/memory-gate-replay.json. Последняя точка 360 включена в recovery mean [330,last], но исключена из [355,360), как в исходном evaluator.

Число reservation/inbox rows в обоих окнах одинаковое: 38/41. **Полное удерживаемое состояние различается**: после предусмотренного search на 354-й секунде cache bytes изменились с 1861296 до 1959002 (+97706), Duffel retained rates — с 1296 до 1314, Hotelbeds — с 822 до 831. Это сопоставление по числу строк, а не по всему retained dataset.

На всех десяти точках private memory равна 366264320 байтам; last-GC committed — 205479936 байтам, Gen2 count — 69. RSS меняется на 4096 байт ровно между окнами. Эти данные **не доказывают новый product memory defect или утечку** и не устанавливают владельца дополнительной resident page. Связь с поиском по времени не является причинной атрибуцией. Отрицательный gate сохраняется.

## Измеренная память

| Метрика | S v2 до исправления | S v3 после исправления |
|---|---:|---:|
| Peak RSS | 1384357888 B / 1320.23 МиБ | 475516928 B / 453.49 МиБ |
| Peak private | 1290289152 B / 1230.52 МиБ | 371916800 B / 354.69 МиБ |
| Final RSS | 1384321024 B | 468951040 B / 447.23 МиБ |
| Final private | 1289039872 B | 366264320 B / 349.30 МиБ |
| Peak estimated managed | 196016368 B | 181447680 B |
| Final estimated managed | 149363064 B | 117659120 B |
| Last-GC committed at final sample | 234622976 B | 205479936 B |
| Timed Gen2 count | 66→66 | 67→69 |

Peak RSS ниже на **65.65%**, peak private — на **71.18%**. Prescribed dataset/schedule/thresholds одинаковы; это два самостоятельных runtime-наблюдения, не полностью идентичное состояние и тайминг. В v3 есть один дополнительный Hotelbeds availability step и другие естественные GC. Сравнение не переносится на historical M1 и не доказывает отсутствие всех утечек.

RSS baseline на 29.022 секунды — 424861696 B; final−baseline = 44089344 B, меньше 128 МиБ. Mean [300,330) = 468832665.6 B; mean [330,last] = 468777158.194 B — ниже на 55507.406 B. Поэтому workingSetRecovery=true независимо от отдельного отказа последних 5-секундных окон.

Cache максимум 128 entries / 4258921 B, final 1959002 B. Before-timed RSS/private — 382328832 / 265289728 B. Host включает runtime, EF/Wolverine, observers и fictional handlers; managed/cache/serialized fixture values не суммируются как независимые owners.

## Полезность и ошибки поставщиков

p95 ниже — время от запланированного due до HTTP response; authoritative purchase success проверяется отдельно по сохранённым фактам.

| Фаза / операция | Полезные / offered | p95, мс |
|---|---:|---:|
| Normal search | 20/20 | 133.4 |
| Normal rates / prepare / create | 8/8; 6/6; 6/6 | 153.9 / 195.5 / 176.1 |
| Normal status / callback | 600/600; 4/4 | 18.3 / 59.9 |
| Burst status / callback | 150/150; 30/30 | 19.1 / 50.5 |
| Final steady search | 10/10 | 131.2 |
| Final steady rates / prepare / create | 4/4; 3/3; 3/3 | 125.0 / 192.9 / 142.9 |
| Final steady status / callback | 300/300; 2/2 | 17.7 / 24.2 |

Burst search: 1200 offered/observed, только 5 healthy; 918 HTTP200 включают limited/failed source results, 282 HTTP429. Быстрые отказы не выдаются за полезную выдачу.

Lost reply HBX на 154.5s: accepted HTTP200/Unknown за 0.1416s от due, один retained effect, без resend/failover. Search162 cancellation учтена. Search186 завершился HTTP200/Limited на 201.032s с truthful Duffel Complete / HBX Timeout.

RecoveryAdmission purchases 2/3, RecoverySteady 3/3; позднее подтверждение не улучшает прошлую фазу. Общий recoveryWithin75Seconds прошёл. Final rows: 26 Confirmed, 10 Quoted, 2 Unknown; dueWork=0, pendingInbox=0.

## Source и ресурсы

| Источник | Seed / timed HTTP starts | Timed create / read | Final rates / quotes / bookings | Serialized values |
|---|---:|---:|---:|---:|
| Duffel | 40 / 114 | 6 / 11 | 1314 / 31 / 19 | 1268698 B |
| Hotelbeds | 16 / 112 | 8 / 7 | 831 / 0 / 9 | 71707 B |

Start events совпадают с cumulative deltas; active=0, overflow=false, unknownEffects=0, duplicate effects=0. Foreground maxima 24 starts/1 active; recovery starts Duffel3/HBX2, в исходных bounds. HBX admitted known-reference cases получили actual reads.

.NET/ASP.NET 10.0.9, server GC, cachedProcessorCount8, affinity0x3. Driver peak RSS 198479872 B, owned DB peak cgroup memory 192921600 B. Эти величины не складываются с Docker VM.

До подготовки свободно около 6 ГиБ. В сохранённых runtime observations минимум available RAM 16086671360 B, free disk 13090451456 B, максимум profile evidence 15481298 B: все frozen safety limits соблюдены. Изменение общесистемного free disk не приписывается Hotels; shared-machine окружение не изолировано. Настройки и очистка чужих ресурсов не выполнялись.

## Provenance и очистка

16 data specimens побайтно совпадают с v2. Freeze включает 2013 source/input files, 998 build files, 335 runtime files, dotnet host и 26 explicit inputs. Перед запуском и после него verify-only прошёл; проверка evaluator 18/18 без Host/DB прошла. Изменений исходников после H7 не было; прежние 13 compiler / 243 architecture / 351 Hotels standalone tests и независимое review сохранены в [source verification](compiler-metadata-lifetime-verification.md), заново нагрузочными тестами не называются.

| Артефакт | SHA256 |
|---|---|
| source-manifest.json | CBF4911D60827CA48340515595DBB7AD286E62CB3E46FE4722A9E250F8C0E330 |
| freeze.json | 9826E2376DACC9A82280A28632670D549188BD7073A0529D74FE49CEFA936EF3 |
| frozen-source-build.zip | 12F16901D9D12E654D7F54618ABB3DCC176644217645C6A541B9E7AAD9100775 |

Архив: 3014 entries, 56141233 B, каждый entry проверен. analysis-v1/integrity.json фиксирует 28 новых raw artifacts и проверку 345 прежних: исходные M1 failed16/17, S-v2 failed22/24 и H6-v3 partial не изменены.

Независимая cleanup-проверка подтвердила отсутствие driver64764, Host38540, контейнера 01c380c2948f3ff4fabcfd758901912f30d78410e7d7f7eab593c56efe6d9201 и own temporary directory travel-hotels-host-cb075b248181457d80dd84aa80002a56. Cleanup runtime completed/cleaned=true. L ресурсов не создавал. Evidence сохранено; prune/delete чужих файлов не выполнялись.

## Вывод и следующий шаг

Исправление metadata lifetime подтверждено существенно меньшей памятью на полном S-профиле; absolute ceiling и recovery прошли. Полная M2-приёмка **не получена** из-за strict final RSS gate и not-run L. План остаётся 6/8, tasks3.2/3.3 открыты.

Следующий обоснованный шаг — отдельно пересмотреть методику оставшегося критерия по уже сохранённым данным: он сравнивает одинаковые durable row counts при изменившемся cache/fixture state и реагирует на 4 КиБ RSS без роста private/GC committed. Это не основание молча добавить epsilon, изменить профиль или объявить green. Если критерий остаётся прежним, текущий результат остаётся failed; если его назначение и метод меняются, требуется явное изменение приёмочного контракта и новая независимая проверка. Новый повтор того же пакета ради удачного RSS не выполняется.

[H6 v2 allocation diagnosis](native-heap-diagnostic-v2-result.md), [metadata lifetime source fix](compiler-metadata-lifetime-verification.md) и [H6 v3 partial comparison](native-heap-after-metadata-fix-result.md) остаются отдельными доказательствами. L, real suppliers, target/rollout и leak-free утверждение не приняты. Stage/commit/push/sync/archive не выполнялись.

