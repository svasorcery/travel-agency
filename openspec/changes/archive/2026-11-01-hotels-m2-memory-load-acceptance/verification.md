# Проверка planning artifacts — 2026-10-29

## V5 выполнен:9/9jobs,3/3dumps — 2026-10-30

После явного разрешения владельца один frozen-v5 запуск завершился exit0 за381.964s, без ошибок/предупреждений. Весь набор снимков разобран в50commands/588.133s; cleanup проверен. Cache0→8→0, Duffel rates0→16→16, HBX0→8→8. Продуктовый patch не обоснован, original M1 RSS cause остаётся unknown. Tasks2.2/2.3 выполнены, прогресс5/8; M2 load не принят. Полные условия, root-analysis limitations и exact hashes — [diagnostic-v5-result](diagnostic-v5-result.md). Ниже сохранена история source preparation и прежних попыток.


## Подготовка v5 без нового эксперимента — 2026-10-30

По прямому поручению владельца проверен весь исполнитель на реальных API JSON и подготовлен frozen v5. Ошибки wire validation/lifecycle/terminal evidence/deadline исправлены. Полный replay43/43, standalone136/136, один независимый review и один fix-pass; все замечания устранены. Итоговые hashes и точная команда — в [v5-verification](v5-verification.md).

Default invocation v5 прошла в режиме проверки, executed=false. Нового приложения/collector/нагрузки/дампов не запускали. Реальные JSON дополнительно получены standalone TestServer contract test с fictional handlers и fake clock; это не runtime experiment. Старые v2/v3/v4 receipts и единственный baseline dump сохранены. Task2.2 остаётся unchecked;3/8, historical M1failed16/17/H4unknown не меняются.


## V4: один запуск и разбор доступного снимка — 2026-10-30

По явному поручению владельца «Да, запускай v4 один раз и разбери снимки памяти» проверены approved hashes и записан authorization-v4.json. Команда run-v4.ps1 -Execute вызвана ровно один раз. Старт2026-10-29T21:08:01.192Z (2026-10-30 00:08MSK); длительность8.628673s, exit1. Нового запуска после отказа не было.

**Результат acquisition неполный:**1/9public jobs отправлен,8not-run;1/3Heap snapshots получен. Snapshot A собран до поиска, B/C отсутствуют. Task2.2 не закрывается полным ownership evidence; общий прогресс остаётся3/8.

### Почему остановилось

Первый HTTP search вернул200,1group/2leaves и оба provider status:
- provider=duffel-stays/hotelbeds;
- kind="Complete" (System.String), attempted=true, reason=null, resultCount=1.

Runner проверял kind -ne1 и поэтому ошибочно выдал "Search was not complete." на строке157. Wire contract уже задаёт string enum через JsonStringEnumConverter<ExecutionKind> в StayValues.cs:94. Это третья обнаруженная ошибка самостоятельной PowerShell-обвязки; отказ не является failure Hotels search или memory gate. Сохранённый failed receipt не переписан в success. Read-only проверка реального job-0.json подтверждает oldGuardRejects=true и wireComplete=true для обоих sources.

V4 не менялся после запуска; v5 не создан. Перед следующим экспериментом нужна целостная проверка runner против actual serialized API contracts, включая orchestration/terminal paths. Одни parser/helper tests не проверяли enum contract. Это рекомендация source review, не разрешение на новые процессы или новую нагрузку.

### Что показал снимок A

Обработан именно сохранённый a-before.dmp,28938221bytes, до первого запроса. Pinned dotnet-dump/DAC10.0.9 использовали локальные файлы; symbol downloads отключены.28SOS commands за218.9125284s, в пределах50commands/600s; raw stdout/stderr и command/PID ledger сохранены.

| Наблюдение A | Факт |
|---|---:|
| Product cache entries / payload bytes |0 /0 |
| Duffel rates |0items |
| Hotelbeds rates |0items |
| Reachable graph Duffel rate dictionary |8objects /560bytes |
| Reachable graph Hotelbeds rate dictionary |8objects /560bytes |
| Reachable graph cache dictionary |1object /80bytes |
| dumpheap inventory |19755objects /1465938bytes |

Reachable graph включает служебные таблицы/locks/comparers; значения560bytes не являются exclusive retained size, shared objects не складываются. dumpheap inventory не является размером live heap после full GC и не равен RSS.

Оба owned handlers и cache найдены в heap; ссылки на соответствующие dictionaries проверены. gcroot -nostacks не нашёл roots для трёх владельцев. Дополнительный stack-inclusive gcroot для Duffel показал8paths через Main/WaitForShutdown/WebApplication/ServiceProvider/provider/typed client, включая timer path. Stack-root вывод может содержать conservative references и не доказывает leak. Наличие singleton-handler на этапе ожидания работы ожидаемо.

**H4-M2 остаётся unknown:** начальный снимок подтверждает пустое состояние, но отсутствуют сравнения после8search и послеTTL. Нельзя подтвердить/опровергнуть рост или удержание fake graphs, product leak либо original M1 RSS cause. Product memory patch не обоснован. Исходный failed16/17 и отсутствие M2 load acceptance сохраняются.

### Cleanup и provenance

Fixture PID53588 и collector PID47692 остановлены штатным finally. Три offline analyzer процесса59920/67896/18980 завершились exit0; все5PID проверены отсутствующими. Новых Host/DB/AppHost/AI/provider/model операций не было. Единственный публичный запрос направлен на owned loopback fixture. Нет forced GC, trimming, state resets или удаления evidence.

Все артефакты находятся в TestResults/hotels-m2-memory-load-acceptance/diagnostic-h4-m2-v1/run-v4/; исходные v2/v3 failures сохранены отдельно.

| Артефакт | SHA-256 |
|---|---|
| receipt.json | 5009B29B2160A9A1EDFBD6F1CF0BE790A4357720C9FC79B5F130BC3420FA74D2 |
| a-before.dmp | 8221A71E6DEBD8CFF9D6428AD5A05B9B4E7F23A2F1F6D5BA50BEE0FD7FCCB7C5 |
| job-0.json | EB96811475D310B457CE5F558E500C80A6DF506406A082D383EE99DC34FD2B5C |
| analysis/ledger.json | 1DFDA9C68A4B9276AEAD8A6723A090FAD4779877B15FB44045ADC6FCB09640EC |
| analysis/result.json | 9E62A6DBC69E39D4396FA5AE30E43D418AEF0BE0DC7505D05F36DDE9E26C56B1 |

[Design](design.md) остаётся неизменённым frozen pre-run protocol v4; его statements о подготовке относятся к моменту freeze. Актуальный outcome — этот раздел и raw acquisition/analysis receipts. Публикация/commit/merge/deploy не выполнялись.



## Повтор2.2 на freeze v3: остановка до ready — 2026-10-29

По прямому разрешению владельца «Да, повтори один запуск исправленного пакета и разбери снимки памяти» hashes v3 сверены и run-v3.ps1 -Execute вызван ровно один раз. В20:39:23.296UTC создан собственный dotnet PID1676. Через0.2652737s runner завершился exit1 с PropertyNotFoundException **до получения ready, HTTP или запуска collector**. Все9jobs сохранены not-run;0dumps/0offline analysis commands. Cleanup receipt stopped=true; отдельная проверка подтвердила отсутствие PID1676. No retry v4.

Ошибка принадлежит диагностическому скрипту: Check-Budget читал .Sum у результата пустого Get-ChildItem | Measure-Object при StrictMode Latest. На первом ожидании каталог evidence ещё не содержал файлов, pipeline возвращал0objects. Точное выражение воспроизвело тот же PropertyNotFoundException/Sum без приложения. Это не memory failure Hotels; снимков для requested анализа нет, H4-M2/original M1 root cause остаются unknown.

Сохранены run/receipt.json и fixture.err, authorization-v3.json, execution-attempt-2.json, frozen v3 runner/manifest и design-freeze-v3.md. Нет удаления/перезаписи прошлых попыток.

| Артефакт | SHA-256 |
|---|---|
| run/receipt.json | 9E22D70C3F735B0FF8BF9927AA68609666693204F2132201E21848B37D2C448B |
| execution-attempt-2.json | 1771EBDCF0A4D5200C8EF0BF0B7FF404CC4566404DE933F213058E31ADED25A6 |
| run-v4.ps1 | 2114CF1915B545A64451F8605E8F52962B0DA8E428CA4997FCAE47A611E04603 |
| integrity-v4.json | 9B63BBBBCFF26E96CA33AC3A8919D784B891DC624471F2920D173A6BC096E1AE |
| source-manifest-v4.json | 3F63F0B2BF7FFF57D9FD2B94D96D8BA21FBF3BC6656BC7CBE99C864408D894D4 |

Подготовленный v4 суммирует Length начиная с long0; пустой каталог корректен. Ошибка дополнительно сохраняет message/line для следующей диагностики. Для будущей отдельно разрешённой попытки используется новый run-v4/, исходный run/ сохраняется. Profile/dates/workload/budgets не меняются.

Verification без Hotels/network:
- helper-regression на v3 воспроизвёл RED1/12 только для empty evidence; после fix12/12GREEN;
- расширенная проверка16/16: empty/nonempty inventory, disk/RAM/RSS/time guards, completed/faulted/unfinished tasks, HTTP ledger/ISO value/disposal, target metrics, collector command/receipt/error/no-overwrite;
- collection branches проверены **mock-процессом и кратким synthetic test file**, не реальным dotnet-dump. Все временные test files удалены из своего проверенного каталога; helper-expanded-final.json подтверждает tempCleaned=true. Первый expanded log содержал безвредный VoidTaskResult от тестовой проверки exception; final JSON очищен подавлением вывода проверяемого action, production collection behavior не изменён;
- date regression14/14, parser0errors; v4 default invocation exit0/executed=false,1112packet/build/tool +1917source +338runtime identities совпали.

Проверки helpers не заменяют actual lifecycle/heap proof. Task2.2 остаётся unchecked, прогресс3/8. Новый runtime не выполнялся: no-retry boundary требует отдельного решения владельца. Current command/protocol — [design](design.md). Product patch, приёмка M2 и вывод об утечке не обоснованы.



## Попытка2.2: отказ preflight, без runtime — 2026-10-29

Владелец явно разрешил: «Да, запускай один эксперимент и разбери снимки памяти.» Согласованные hashes freeze v2 проверены; команда run.ps1 -Execute вызвана **один раз**. Она завершилась exit1 на date guard, **до** создания run/, Process.Start и любого HTTP. Итог:0Hotels processes,0/9offered jobs, все9not-run,0/3dumps,0analysis commands. Cleanup runtime assets не требовался — они не создавались. Это failed-before-runtime, не результат memory experiment; task2.2 остаётся unchecked, общий прогресс3/8.

Причина установлена read-only воспроизведением. Host UTC был2026-10-29T20:30:49Z, deadline в profile2026-11-04T00:00:00Z: реального истечения/расхождения часов нет. PowerShell7.6.5 ConvertFrom-Json вернул DateTime; его implicit string был11/04/2026 00:00:00. Culture-dependent DateTimeOffset.Parse в ru-RU прочитал11апреля с local offset, получив2026-04-10T21:00:00Z. Та же ошибка затрагивала будущую обработку response expiry.

Это ошибка подготовленного диагностического скрипта, а не Hotels/GC/окружения. Она не устанавливает product memory defect; анализировать snapshots невозможно, потому что их нет. Historical M1 failed16/17 и M2 load acceptance не меняются.

Исправление подготовлено **в отдельном run-v3.ps1**, старый runner не переписан. JSON dates сохраняются строками через -DateKind String, обе ISO timestamps разбираются invariant/RoundtripKind. Frozen dates/workload/concurrency/duration/budgets/cleanup сохранены; новый -Execute не выполнялся.

Regression извлекает фактические JSON conversion/Parse expressions из runner, не вызывает его и не стартует Hotels. На старом runner13/14fail; на исправленном14/14pass: ru-RU/en-US, ambiguous month/day, day>12, explicit timezone, ещё допустимый deadline, точная граница и истёкший deadline. Parser0errors, default invocation нового packet exit0/executed=false;1111build/tool/packet +1917source +338runtime identities проверены.

Local evidence в TestResults/hotels-m2-memory-load-acceptance/diagnostic-h4-m2-v1/:

| Артефакт | SHA-256 / результат |
|---|---|
| execution-attempt-1.json | A8EEFC5391C1610333F895EA1C0E01457D0FBF6187C9F4312BFD0CCCF8FB9DF6 |
| run-v3.ps1 | FA55D05288EE8A1286995E636AD7D8E7E34D078E2386BADD1EC80991F660D88C |
| integrity-v3.json | 9183EDE6364F63B1F4F01AB860D4EFD20648579231A1BDFD1701BDA197F2B3FD |
| source-manifest-v3.json | 8AB51C8D50ECD1B33103AB1E302310EF65EC4C656587444C0291975250B91FA9 |
| profile.json | unchanged523530B53BBC50935242E5E6E1DE6665F04FF26FE34B012E4E248E2F9F8C0746 |
| authorization.json / date-parsing-red.json / date-parsing-green.json | Authorization, RED13fail/14, GREEN14/14 |
| design-freeze-v2.md / run.ps1 / integrity-v2.json | Исходные inputs отказавшей команды сохранены |

Точная команда следующей попытки зафиксирована в [design](design.md): run-v3.ps1 -Execute. Согласованный protocol запрещает automatic retry после failure; требуется отдельное решение владельца о повторе. До него acquisition/ownership остаются not-run/unknown. Новых нагрузочных прогонов, dumps, product changes, stage/commit/push/PR/merge/deploy не было.



## Подготовка diagnostic protocol2.1 — 2026-10-29

По поручению «го» подготовлен конкретный H4-M2 v1 из design§2. Это отдельная проверка ownership current M2 handlers/cache на existing standalone fixture; она не устанавливает причинный вклад original M1 fake в historical RSS. Прогресс **3/8**:1.1,1.2,2.1; runtime task2.2 не выполнялся.

Подготовлен local ignored packet TestResults/hotels-m2-memory-load-acceptance/diagnostic-h4-m2-v1. Microsoft dotnet-dump10.0.750501 установлен туда из публичного NuGet через явный config без унаследованных package sources; проверены только version/help. Tool runtimeconfig ориентирован на net8 с major roll-forward; чтобы не брать установленную8.0.0, collector/analyzer закреплены на10.0.9 явным --fx-version. Initial preparation freeze сохранён как superseded; на том checkpoint окончательный freeze был v2. Позднейший preflight refusal и текущий v3 описаны выше.

| Frozen item | SHA-256 |
|---|---|
| integrity-v2.json | F7DDE89EECF2AEE8D36C92EE41AA5DEE453C0CB246BCACDDE1830B4AA068D136 |
| source-manifest-v2.json | BD9478302EA8625231123D8A35A687B946F9C80ECAD9D100D7A57CFCA4E3BD17 |
| run.ps1 | 6C54BDDD4D4DC5AB13D78CFAB04594F9B593E50F2C13B7ACD515AB248375D9CC |
| profile.json | 523530B53BBC50935242E5E6E1DE6665F04FF26FE34B012E4E248E2F9F8C0746 |
| Existing fixture DLL | C904396D20C2D02A1F7E99D9EAAFC5569D56F9182D9A8908DAABF4B18E332222 |

Source manifest содержит1917файлов, integrity дополнительно1110build/tool/packet files и338runtime/launcher files. Preparation check: PowerShell parser0errors; default invocation exit0, readyForAuthorization=true, executed=false; run/ отсутствует. Это syntax/integrity proof, а не lifecycle/performance proof. Скрипт одноразовый и fail-closed: -Execute отдельно; существующий run/, changed hashes/dates или занятый port не обходятся.

Один будущий запуск:8search+1expected409-rates,1process/3Heap dumps/≤720s, затем bounded offline analysis≤10min/50commands. Полный protocol, текущие ограничения, authority и cleanup — [design](design.md). Fixtures/product/gates текущим шагом не менялись; весь ранее выполненный source diff остаётся uncommitted.

Финальная проверка: pinned OpenSpec strict exit0/valid=true/issues=[], apply3/8; git diff --check прошёл, index пуст, HEAD/base c3fd3a6b3e8965ad8cf7850e160f22edfa12dedf сохранён.7/7historical M1 hashes совпали,0broken local links,0dump и нет run/. Новых product tests не запускали: изменены protocol/packet, runtime proof остаётся2.2.



**Предыдущий checkpoint: v3, исправлен по прямому поручению владельца после полного review.** Истории v2 и v1 ниже сохранены для provenance; их результаты проверки относятся к соответствующим прошлым редакциям.

После этой planning-проверки владелец поручил «окей, давай дальше». На том checkpoint source tasks1.1–1.2 были выполнены и проверены, прогресс2/8; отдельный [source-verification](source-verification.md) содержит RED/GREEN, independent review и исправления,135standalone/79unit/4manifest, compile-only и offline16/17. Нижние записи о0/8 и только planning описывают прошлый checkpoint; diagnostic/load/target acceptance ими и source-проверкой не установлены.

## Исправление трёх замечаний полного review — v3

Полный read-only review после упрощения выявил два несоответствия реальному provider code и неоднозначность evaluator. В план внесены:

1. Оба M2 профиля используют существующий areaKey="fictional"/Areas[3]. London оставлен только отрицательным guard-case: Fictional=true должен отказать ему доHTTP. Guard не ослабляется.
2. В future task3.1 явно добавлено ограниченное расширение fictional-only request/catalog в HotelbedsOptions/HotelbedsStayProvider:1–100unique positive IDs, прежний default[900001], S запрашивает900001..900009, L —900001..900100. Owned handler сверяет запрос с frozen inventory; default demo/external branch не меняются. Это заявленная будущая source-правка, а не уже реализованная возможность или исправление памяти.
3. PhasePurchaseSuccess и WindowPurchaseSuccess вычисляются независимо из raw timestamps/effect facts. Покупкаdue284.5 с подтверждением301 не проходит phaseEnd300, но может проходить windowEnd305; подтверждение310 в окне305 не засчитывается. Добавлены регрессии обоих направлений, без изменения150s/HTTP latency bounds.

Также уточнено распределение данных: currency/unknown-local альтернативы задаются по matched pairs, чтобы unmatched single-source properties сохраняли1rate/property и общий denominator18/200leaves.

Согласованы proposal, design, tasks, load-plan, delta и дополнительные repository references в research. В плане по-прежнему8future tasks,0completed; два360s профиля/6304public jobs; диагностика отдельная, без обязательного Q3 и без новых hardware caps.

После правок выполнены только документальные проверки:

| Проверка | Результат |
|---|---|
| Pinned strict validation | exit0, valid=true, issues=[],1/1passed |
| Исходный requirement с3old scenarios | Сохранён дословно |
| Delta scenarios / task traceability |12scenarios, все связаны с tasks |
| Seven historical raw hashes |7/7 совпали |
| Repository links |0broken |
| Future task count |0/8completed |
| Tracked diff/index / requested ancestry | Пусты / base c3fd3a6b3e8965ad8cf7850e160f22edfa12dedf сохранена |

Команда: `node tools/openspec/run.mjs validate hotels-m2-memory-load-acceptance --strict --json`, cwd — текущий managed worktree. Ни source code, ни main specs, ни saved demo/human-review/receipts не изменены. Builds/tests/benchmarks/Host/Docker/DB/migrations/paid APIs и publication не выполнялись; credentials/billing не читались. Исправлены **плановые** противоречия; executable behavior остаётся непроверенным до отдельно разрешённой реализации. Historical M1 всё ещё failed16/17.

## История упрощённой редакции v2 — до полного review

Первоначальная проверка v2 не обнаружила три замечания, перечисленные выше. Следующая запись сохраняет её фактические действия и прежний вывод, а не утверждает отсутствие замечаний в текущем runtime.

Изменены только существующие planning artifacts: proposal, design, tasks, load-plan, delta spec, planning-рекомендация H4 в research и эта запись. Base/branch сохранены: `c3fd3a6b3e8965ad8cf7850e160f22edfa12dedf`, `codex/hotels-m2-memory-load-acceptance`.

По результатам аудита:

- Вместо16future tasks — **8**, выполнено0. Порядок: минимальный измеритель → отдельная диагностика конкретной гипотезы → отдельная M2 приёмка.
- Обязательный idle Q3 удалён; диагностика не зависит от green нагрузочного профиля. Точный diagnostic protocol обязан дать новое evidence, иначе повтор прежних scalar measurements не запускается.
- Сохранены два360s M2 профиля S/L и исходные numeric gates:3152jobs каждый,6304planned public jobs вместе. Seed сокращён до38rows; burst использует одну seeded confirmed Duffel reservation.
- Удалены обязательные новые общие4CPU/6GiB/8GiB и private-commit/driver/DB/VM caps. Используется существующий ПК и имеющиеся fixtures; фактические ресурсы учитываются, а локальные time/disk/free-memory stop limits фиксируются до отдельного разрешения. Неподтверждённые hardware requirements не являются acceptance gate.
- Убрано обязательное проектирование таблицы job→response/preallocated pools; existing handlers дорабатываются адресно под конкретные данные и safety.
- Historical failed16/17, исходные thresholds и разделение source/diagnostic/load/external authority не изменены.

После правок выполнены:

| Проверка | Результат |
|---|---|
| Pinned strict OpenSpec validation | exit0, valid=true, issues=[],1/1passed |
| Полный прежний requirement и3old scenarios | Сохранены дословно в delta |
| Семь raw evidence hashes из research |7/7 совпали |
| Relative repository links |0broken |
| Future task count |0/8 completed |
| Tracked diff/index и ancestry | Tracked diff/index пусты, requested base сохранена |
| Read-only builder recheck v2 | Конкретных блокеров/регрессий от упрощения не обнаружено |

Validation выполнялась только через `node tools/openspec/run.mjs validate hotels-m2-memory-load-acceptance --strict --json` из физического корня текущего managed worktree. Runtime/build/tests/Host/Docker/DB/migrations/providers/paid API не запускались. Credentials/billing не читались; stage/commit/push/PR/merge/deploy не выполнялись. Saved demo/human-review и raw receipts не менялись.

Planning готово к рассмотрению. Run packet пока не готов к исполнению: executable hashes, конкретный diagnostic protocol и локальные предохранители относятся к будущей разрешённой работе. Упрощение не является разрешением на реализацию или запуск.

## История первоначальной проверки v1 — до последующего аудита

Следующие записи описывают прежнюю редакцию. Поздний аудит выявил методические недостатки, которые первоначальный builder check и синтаксическая strict validation не выявили; они исправлены в v2 выше.

### Scope и provenance

Только research/planning. Fresh `origin/dev` после fetch: `c3fd3a6b3e8965ad8cf7850e160f22edfa12dedf`; HEAD новой `codex/hotels-m2-memory-load-acceptance` совпадает с этой базой, ancestry проверена. Managed worktree: `C:/Users/Vladimir_sva/.codex/worktrees/hotels-m2-memory-load-acceptance/travel-agency`.

У основного checkout был более старый HEAD35953fd6…, он не использован для ветки. Исторические TestResults прочитаны в основном checkout по точным путям. Семь raw hashes из [research](research.md) перепроверены после правок,7/7 совпали. Сохранённые demo/human-review файлы, архивы, main specs и source не изменены. Index пуст; tracked diff пуст; новые файлы только в этом change.

В fresh worktree отсутствовал pinned CLI runtime: скопирована существующая локальная `tools/openspec/node_modules`, без network install. Launcher проверил pin/runtime digest; CLI запускался исключительно через `node tools/openspec/run.mjs` из этого worktree. Sandbox spawn EPERM разрешён scoped запуском той же planning-команды, pin checks не обходились. OpenSpec/generated metadata не является разрешением на implementation.

### Саморевью и независимый builder check

Исправлены содержательные ошибки первого черновика:

- Различены raw HBX rateKey и полный private context: healthy Q2 key2048, full context≤4096;4096raw с oversized wrapper остаётся отдельной негативной regression.
- Lost-reply job154.5 имеет frozen reachable prerequisite chain и100ms overrides;3s fault не может превратить его незаметно в отказ до отправки.
- Q3 имеет status5/s на всех120s:600jobs, campaign6904.128small snapshots содержат по1group/2leaves, не128полных200-leaf ответов сверх16MiB.
- Seed64rows теперь содержит ровно2confirmed Duffel burst targets. HBX recovery следует timed direct known reference, без invented webhook или продления давно начатого read allowance.
- Stable fixture scope keys и shared immutable catalog делают512scope-record bound проверяемым; overhead/bytes сохраняются в Host cost.
- RecoveryAt учитывает отдельный authoritativeObservedAt: Confirmedt310 не улучшает окноt300 задним числом; raw HTTP p95 сохранён.
- Historical numeric gate уточнён по коду: mean[330,last] включает terminal sample. Новый planned sampler не расширяет это окно drain/cleanup.
- Полный прежний requirement **Useful bounded single-process resilience**, включая все3 старых scenarios/M1 exception, сохранён дословно; добавлены7 новых scenarios.

Fresh read-only builder check после двух раундов исправлений: существенных блокеров не осталось. Это review планирования, не review реализованного harness и не runtime proof.

### Выполненные проверки документов

Команды из корня указанного worktree:

```text
node tools/openspec/run.mjs validate hotels-m2-memory-load-acceptance --strict --json
node tools/openspec/run.mjs status --change hotels-m2-memory-load-acceptance
git merge-base --is-ancestor c3fd3a6b3e8965ad8cf7850e160f22edfa12dedf HEAD
git diff --exit-code
git diff --cached --exit-code
```

Финальный strict result: **exit0, valid=true, issues=[],1/1passed**. Status:4/4planning artifacts complete. Не путать с выполнением implementation tasks: **0/16**. Дополнительно статически проверены repository Markdown links (0broken до добавления этой записи), сохранность исходного requirement block и7 raw hashes; никаких product tests для этого не запускалось.

Выполнены только permitted Git operations, чтение файлов/JSON, локальная арифметика сохранённых metrics и planning CLI/static-document checks. Не было сборок, tests, benchmarks, Host/AppHost/Travel.AI/Docker/DB/migrations, provider/model/paid calls, credentials/billing read, stage/commit/push/PR/merge/deploy.

### Итог и незакрытые gates

Planning artifacts готовы к рассмотрению: [proposal](proposal.md), [design](design.md), [tasks](tasks.md), [delta](specs/hotel-stay-search/spec.md), [research](research.md), [load plan](load-plan.md).

Исходный M1 остаётся failed16/17, workingSetRecovery=false, passed=false, exit1. Product root cause и отсутствие утечки не установлены; M2 performance, hard2GiBreference и target не приняты. Следующий предмет согласования — G1 измерительный slice; перед каким-либо runtime — frozen packet, согласование новых P-values и отдельный G3. Ни один из них этой записью не разрешён.


### H6 executable preparation — 2026-10-31

После отдельного разрешения установленыофициальныйWPA11.8.423 иподготовленH6v2:fixedhelper,PID/pipe/ACLguards,2heap snapshots+3VMMap+36IPC,independentcleanup.351/351tests,2ImportantreviewfixesRED/GREEN,1Minorотложен;2002source/998build/335runtimefreeze,3003-entryarchiveпроверен,verify-onlysourceReady=true/executed=false. Все189historicalrawсохранены;M1failed16/17,Sfailed22/24,Lnot-run. Новогоэксперимента/повышенияправ/ETWrecordingнебыло. [Полный receipt](native-heap-executor-v2-verification.md);6/8неизменно.

### H6 v2 first CLI invocation — 2026-10-31

Один разрешённый вызов driver53868 завершился за1.114s с ArgumentException до HotelsH6Runtime: mixed-separator packet argument нарушил exact canonical-path guard. Это ошибка команды ассистента, не runtime Hotels. Внутренниеattempt/run и всеHost/helper/ETW/captures отсутствуют; WPR not recording, driver завершён. No retry; исправленный ProcessStartInfo построен и проверен безStart. 189historicalraw/2002source/998build/335runtime+host1 неизменны. [Подробный разбор](native-heap-diagnostic-v2-invocation-result.md); H6 diagnosis pending,6/8 прежние.

### H6 v2 successful acquisition and offline analysis — 2026-10-31

Владелец прямо разрешил исправлять ошибки ассистента без повторных вопросов. Corrected invocation02:oneactualrun,driver62656/helper47416/Host30192,85.34s,exit0,56Response/36IPC/3maps/2snapshots,postcomparisontrue,cleanupverified. ETL40894464B,lostevents0;официальныйTraceProcessor1.12.10подтвердил2x64snapshots и185/280непрерывныхchunks,72774→133948allocations,tracked59.37→1037.84MB. Tracked+933.14MiB=99.72%heapgrowth;двеLocalAllocгруппы+920.40MiB/DLL-size signature указываютнаJasperFx/Roslynmetadatareferences,ноanonymousmanagedIP/lifetimeостаётсяunresolved. БезPDB/download/новойнагрузки/sourcepatch.78currentraw+5firstCLI+189historicalverified;frozen2002source/998build/335runtimeunchanged. [Полный result](native-heap-diagnostic-v2-result.md).6/8/M2loadgatesбезwaiver.

### Metadata lifetime source fix — 2026-10-31

После продолжения lifetime-среза реализован Host scoped metadata compiler через существующий IAssemblyGenerator, без cache/version change/GC. Roslyn real-owner contract3/3;9RED→GREEN;final13/13,architecture243/243,Hotels351/351,pure18/18;wrapper0errors. Freshreview0Critical/0Important/1Minor(concurrency test overlap not guaranteed). Все272raw+168H6analysis verified;oldfreeze/archive unchanged. ActualHost/load/RSS/M2 acceptance не выполнялись. [Source verification](compiler-metadata-lifetime-verification.md),6/8 unchanged.

### H6 v3 comparative run — 2026-10-31

Послеgo-furtherподготовленотдельныйfrozenv3иодинactualrunнаmetadatafix.2012source/998build/25inputs,freezeA9AF5F4A,archive9C57E3C3.56Response/35IPC/P0P1;guardt12dueWork1остановилB/P2. Commonobservedcountersmatcht0…8,t8private882.21→253.34MiB;newboundary→t12+10.23MiB. Полныйрезультатincomplete,неwaiver. WPRstop0x80010106knownMicrosoftbug,retainedinternalfragment4events/noheapsnapshot. Cleanupownedresourcesverified,272prior+73newraw/66runtimehashespreserved. Передrun15oldfilesNTFScompressedwithsamehashes,nothingdeleted/moved. [Result](native-heap-after-metadata-fix-result.md);6/8unchanged,noadditionalrun.

## 2026-11-01 — S/L v3 after scoped metadata fix

Owner «го» continued to independent S/L acceptance. Prepared a new create-only packet with unchanged 16 data specimens, 26 inputs, 2013 source/input and 998 build files. Source/build matched the reviewed H7/H6-v3 state; verify-only and pure evaluator 18/18 passed. One actual campaign: 450.580s, S 3152/3152 jobs and 361/361 samples, original16/17 + M2 7/7 = 23/24; L not-run under stop-on-failure. RSS ceiling/recovery passed. Strict final 5-second windows grew by exactly4096B; private/GC committed stayed constant, but cache/fixture state changed across scheduled search354. This does not prove a leak or waive the failed gate.

Driver64764/Host38540, exact owned container/temp absent;345 prior raw hashes unchanged;28 new raw files retained. No retry, forcedGC, threshold change, WPR/UI, source edit, publication or archive. Overall6/8 and tasks3.2/3.3 pending. [Result and method limitation](load-sl-v3-result.md).

## 2026-11-01 — future acceptance revision2 source-ready

The owner accepted the explicit audit recommendation: final5s RSS comparison becomes nonblocking diagnostic only for newversion2M2 packets. All numeric mandatory memory/recovery/cache/source/business/evidence/safety gates remain unchanged; legacyversion1/M1 classification and failed receipts remain intact.

Implemented20focused/371standalone/48pure-evaluator checks, meaningful RED→GREEN and fresh review0Critical/0Important/0Minor. Historical M1replay16/17 and5legacy evaluator sections unchanged. New S/Lv4 prepared:2018source/input/999build/16unchangeddata/373priorraw; freeze/archive/verify-only passed. Runtime not executed or authorized. [Full source verification](load-sl-v4-verification.md). Overall6/8, tasks3.2/3.3pending.

## 2026-11-01 — one S/L v4 campaign completed, acceptance failed

Owner «Го» authorized one prepared version2 campaign. S passed23/23, L failed21/23;6304/6304outcomes and722/722samples, no executor/sampler/finalization error. L failed both memory recovery predicates (final−baseline190.707MiB vs128; latter30s mean+4.837MiB) and terminaldueWork1/lag1.266s,pendingInbox0. Final-phase purchases allconfirmed; exact pending-envelope identity unavailable, no post-window observation used. Final5s diagnostic remains separate: Sfalse/+12KiB,Ltrue/−437.6KiB.

Driver18212 and Host52396/34484, exactownedDB/temp cleaned and independently absent.2018source/input/999build unchanged,373priorraw and52newraw pinned. Runtime820.730s, exit−532462766, no retries/forcedGC/newdiagnostic/sourceorpolicychange. [Report](load-sl-v4-result.md). Plan6/8; fullM2acceptance remains false pending investigation of L memory and scheduled-work semantics.

## 2026-11-01 — v6 complete, strict mean decision pending

Version4onecampaign460.690s: S22/23,Lnot-run;full3152/361,noruntimeerror. Both rawlegacy and fixed-origin useful recovery pass(end308), queue settled,1GiB andbaseline+128MiB pass. Only strict30s mean grows665.049462B (exactcrossproductdifference618496/930), henceworkingSetRecoveryfalse. Allownresourcesabsent;455priorraw/30newraw/source2027/build1000verified. [v6report](load-sl-v6-result.md).

Owner asked for autonomy; routine source/tests/review/newpackets were completed without per-step approval. The remaining proposal would change the explicit zero-growth acceptance requirement, so an asynchronous concrete policy question is pending; no silent threshold/policy change or repeat was performed.

## 2026-11-01 — accepted S/L v7, acceptanceVersion5

Explicit owner trend decision implemented and reviewed.425standalone/160actualevaluator+3rejectedartifactcases,M1replay16/17unchanged. Onefresh v7campaign passed bothS/L23/23,6304outcomes/722samples,exit0,828.216s. PeakRSS488.129/507.277MiB;final−baseline64.949/58.426MiB. PositiveSmean trend staysfalse/diagnostic;Ltrendnegative. Newandlegacyusefulrecovery310/307;rawterminalqueue0both.

Independent rawbudget/trend/cohort/queue replay matched runtime. Allownedprocesses/DB/tempabsent;source2030/build1001 and485priorrawverified,52newrawpinned. [Finalreport](load-sl-v7-result.md) preserves diagnostic/historyscope,ownerpolicy and rollout/publication boundaries. No forcedGC/cacheclear/unchanged-package retry. Local boundedacceptance achieved;publication/CI/target remain separate.
