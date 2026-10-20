# Rail M1 — локальный Host/OpenAPI и вымышленные измерения

## Разрешение и исходная точка

2026-10-20 пользователь явно разрешил: «локальный Host/OpenAPI с временной БД и нагрузочные проверки на вымышленных данных». Реальный Яндекс не включать, без коммита и публикации. Это новое разрешение на временный локальный Host/DB/schema fixture и fixed measured profile; оно не переносит допуски Hotels и не разрешает реальные supplier/model/key/account/target действия.

Ветка `codex/rail-m1-yandex-search`, base/HEAD `799e31a6b31ff256ba017861cd911e547e4e9896`. Предыдущий принятый source manifest v4: `1293943a24c864bd1ab27af1b6ee1f4386b6eb1d9c5ce4cfe7dd4965a3541412` (145 entries). Исходники не закоммичены. Новые actual-Host проверки выполняет тот же отдельный `gpt-6.1-sol`, основной агент лично проверяет изменения и результаты и владеет запуском measured profile.

Согласованный профиль `rail-m1-host-memory-v1`: SHA256 `802e60aee97b248c242382672c5e4e5e7a0c2137006f025fae5223c3a30473ed`. Пороги/числа/retained states не меняются. Исходные отказы/RED/ошибки и результаты каждого attempt сохраняются; автоматических повторов нет.

## Окружение перед запуском

- Windows 10 Pro10.0.19045,33513812KiB visible RAM,16896160KiB free при preflight.
- Docker29.8.2,8CPUs/16751718400bytes daemon memory.
- Cached `pgvector/pgvector:pg17`: image ID `sha256:7ae6051efd0e60444282c27c7e141af07f322ce033300e727a49c3dd11075e38`.
- Единственный исходный контейнер: `692f04d16796` (`buildx_buildkit_desktop-linux`, buildkit). Он не принадлежит проверке и не изменяется.
- Используется существующий isolated `HotelsHostFixture` только для настоящего Program.cs и временного PostgreSQL/синтетического keyring lifecycle. Configuration memory-only/Testing, настоящие ключи не читаются; Rail transport вымышленный, real admission закрыт. Это повторное использование тестового lifecycle, не Hotel supplier integration и не waiver.

## Host/OpenAPI (4.3)

Подготовка/проверки выполняются последовательно относительно Docker-heavy runners. План: реальные anonymous Rail GET/POST через Program.cs, no-store/validation/outcome/точные цены, execution examples, actual OpenAPI schema/query/request metadata и reviewed full Host snapshot. Lean fixtures не подменяют этот gate.

Принят: implementer Release3/3,0skips38s; личный повтор основного агента Debug3/3,0skips47s. Exact command основного агента: `dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --no-restore --filter FullyQualifiedName~RailHostContractTests --logger "trx;LogFileName=rail-host-parent.trx" --results-directory TestResults/rail-actual-host`. TRX/log: `TestResults/rail-actual-host/rail-host-parent.trx`, `parent.log`; worker: `rail-host-final.trx`, `final.log`.

Первый actual Host дал0/3, diagnostic0/2: Wolverine трактовал POST service как body. Исправление `[FromServices]` на двух Rail services; строгий bounded JSON reader сохранён. Исходные RED, metadata/schema/compiler failures и старый OpenAPI сохранены в `TestResults/rail-actual-host`. Public input schema теперь принадлежит Api.Contracts: required/writable/nonnullable поля, закрытые unknown fields, station grammar/date/mode/generation. Использованы существующие central pins AspNetCore.OpenApi10.0.0 и Microsoft.OpenApi2.12.0; первоначальный NU1903 transitive2.0.0 не обходился выключением аудита.

Лично прочитаны binding/metadata/input DTO/composition/package refs/3Host cases/execution catalogue. Независимый semantic diff golden: добавлены только2Rail paths,11normalized schemas и RailEndpoints tag;0изменений/удалений прежних paths/schemas. Реальные anonymous GET/POST подтвердили exact1234.05, generation/cache echo, disabled/skipped, strict extra/duplicate400, expired selection400,8193bytes413,no-store и0real transport. Log canaries key/query/body не опубликованы; non-Rail control сохранён. Host analyzer AD0001 warnings остались видимыми, не подавлялись.

## Measured profile (7.1)

Первый attempt прерван извне после4/6успешных циклов; полная приёмка НЕ засчитана. Исходный attempt `TestResults/rail-measured/attempt-20261020-01`. Exact command: `node tools/rail/run-measured.mjs --run-authorized --actual-host-db-authorized --receipt-dir D:/_Projects/_github/travel-agency/TestResults/rail-measured/attempt-20261020-01`. Fresh Release build exit0;150sorted source files SHA256 `7c5c7bca06287ce97d7076a066a5ee059133ef1781107b1ecb85a811cb558ec7`,10executed DLL hashes в sibling `.source.json`. HEAD/base прежние; dirty source явно записан. С этого freeze исходники не меняются до окончания attempt.

Личные targeted regressions перед freeze: Rail unit35/35,adapter/lean75/75,architecture182/182,0skips; `unit-parent.trx`,`adapter-parent.trx`,`architecture-parent.trx` в `TestResults/rail-actual-host`. `git diff --check` exit0; OpenSpec apply подтвердил23/25 после закрытия4.3. Первый sandbox-only launcher OpenSpec вернул spawnEPERM до дочернего процесса; разрешённый local запуск подтвердил статус без внешних действий.

Ресурсы measured attempt из Testcontainers log: Ryuk `9c1e743d574e`,PostgreSQL `991011513a50`; исходный buildkit `692f04d16796` не принадлежит attempt.

В receipt должны остаться fresh Release build/source/DLL hashes, profile hash, baseline5samples/30s,3cycles×profiles1/2,1000useful uncached successes/cycle, отдельные cancellation/refusal counts,128entry pressure, одинаковый warm retained state,120s recovery и неизменённые p95/RSS thresholds. Подготовка/describe/генератор не являются measured verdict.

## Прерывание attempt01 и отдельное разрешение attempt02

Turn interruption остановило launcher/fixture до пятого cycle receipt:4completed cycles по1000useful,0failures,все4passed;2недостающих cycle denominators/terminal verdict/launcher receipt отсутствуют. Число незаписанных попыток пятого цикла неизвестно и не восстанавливается предположением. Это incomplete acceptance, не RED threshold и не6/6GREEN. Parent observation `.interruption-observation.json` сохранён отдельно от исходных receipts; source/DLL hashes при наблюдении совпали. Свои процессы/PG991011513a50/Ryuk9c1e743d574e отсутствовали; оставшийся единственный own fictional temp directory `travel-hotels-host-197dae6f07d041579fb941844b8a7233` удалён после абсолютного path/reparse guard. Buildkit692f04d16796 сохранён.

Пользователь отдельно ответил «Да, новый полный прогон» на вопрос о полном новом attempt с теми же порогами и сохранением первого. Attempt02 запускается в новом `TestResults/rail-measured/attempt-20261020-02`; automatic retry по-прежнему отсутствует. Результат будет записан после завершения.

## Итог полного attempt02

Принят: `attempt-20261020-02/verdict.json` passed=true; sibling `.launcher.json`: buildExitCode=0,runExitCode=0,sourceUnchanged=true,binariesUnchanged=true,automaticRetry=false. Команда совпадает с attempt01 кроме нового receipt-dir `D:/_Projects/_github/travel-agency/TestResults/rail-measured/attempt-20261020-02`. Source SHA `7c5c7bca06287ce97d7076a066a5ee059133ef1781107b1ecb85a811cb558ec7`,150files/10DLLs,тот же base/HEAD/profile hash. Parent independently проверил root+6cycle+6denominator receipts; результат `.parent-verification.json`.

Baseline5samples:464576512,469766144,468762624,469131264,467988480bytes; median468762624bytes,threshold539077017bytes. Forced GC отсутствует. Восстановление после120s проверялось при1bundle/0search entries/0active children/0reference loaders/2free search permits.

| Concurrent searches | Cycle | Useful / attempts | p95 ms | Recovered bytes | Verdict |
|---|---|---|---|---|---|
|1|1|1000/1000|1.3602|425725952|passed|
|1|2|1000/1000|0.6903|424755200|passed|
|1|3|1000/1000|0.6698|362598400|passed|
|2|1|1000/1000|0.6997|363900928|passed|
|2|2|1000/1000|1.1479|364269568|passed|
|2|3|1000/1000|2.1073|449933312|passed|

Итого6000attempts/6000useful,0failures/0cancel/0inflight в полезной выборке; все6denominators complete/interrupted=false,1000latency samples each. Cache pressure128each; uncached=true,2000healthy mode dispatches+4saturation dispatches и4reference dispatches/cycle. Отдельно saturation:12cancellations+12capacity refusals,24source children dispatches,0useful; все6saturation checks passed. WorkingSetRecovery6/6,performance6/6 при неизменённом p95≤1000ms. Max lifetime peak510791680bytes имеет process-lifetime scope, не называется чистым warm peak.

Очистка подтверждена после exit0: процессы launcher/fixture отсутствуют; own PostgreSQL `fb3f0ea3690a`,Ryuk `2fa5247423cb` и temporary keyring directory отсутствуют. Docker ps-a показывает только исходный untouched buildkit692f04d16796. Реальные supplier endpoints не вызывались: terminal Wire реализует все ответы и отвергает неизвестные destinations/methods;realSupplier=false,hotelsWaiver=false.

## Оставшиеся границы

Pinned OpenSpec после закрытия4.3/7.1:24/25complete, единственная pending6.2(ordinary CI); strict validate valid=true/issues=[]; `git diff --check` exit0, staged diff пуст. После измерений изменены только два включённых в manifest документа: `docs/architecture/current-state.md` и `modules/rail/README.md`. Runtime/test/config/profile/DLL изменений нет. Post-document manifest `TestResults/rail-measured/source-manifest-after-runtime-docs.json`:150files,SHA256 `6eae42e36ebe15dd06be60ae8691bbae824ebffa2437ee02ab7cd7e3eac0d9b7`; measured execution остаётся привязан к исходному `7c5c7bca…`, не переименован в новый hash.

Ordinary CI6.2 требует отдельного опубликованного CI контекста; публикация не разрешена. Real Yandex/access/rights/account quota/restart/live banner и target deployment остаются отдельными pending gates. Hotels original L1 RED16/17,workingSetRecovery=false не меняется; Laya default-off.

После выполнения проверяются завершение собственного процесса, удаление временной БД/keyring и сохранность исходного buildkit container. Существующие чужие ресурсы не очищаются.
