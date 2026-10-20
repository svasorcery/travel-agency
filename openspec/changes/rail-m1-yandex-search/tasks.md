# Tasks

Статус: разрешённый source/offline срез реализован и проверен;24/25tasks выполнены. Личный review6.4 завершён: [personal-review.md](personal-review.md). Actual Host/OpenAPI4.3 принят отдельно: [runtime-verification.md](runtime-verification.md). Measured execution7.1 принят полным attempt02; ordinary CI часть6.2 остаётся pending. Команды, исходные RED/infra failures, counts и ограничения: [verification.md](verification.md). Источник контракта: [spec.md](specs/rail-schedule-search/spec.md); подход, численные bounds и внешние gates: [design.md](design.md).

## 1. Контракт и первый чистый срез

- [x] 1.1 После утверждения спецификации передать bounded implementation отдельному агенту `gpt-6.1-sol`, указав ownership Rail и согласованных Host/web/test consumers; сообщить, что он не один в codebase и чужие edits не откатываются. Проверить Git/актуальный base/ancestry до переключения ветки, сохранить exact base SHA и scope в receipt этого change. Не stage/commit/push без отдельного поручения. Проверка: receipt называет base, рабочую ветку, implementer и отдельно непройденные внешние gates.
- [x] 1.2 Добавить Rail Core immutable values/factories и Application ports для станций/search/result/evidence; expected outcomes через явный `ErrorOr<T>`, без DTO/HTTP. Unit cases: station≠settlement, source/thread/transport/endpoints/instants identity, optional start_date без выдуманной run-start семантики, duplicate conflict, offset/date/UTC chronology/exact duration, неизвестные IANA/sourceUpdatedAt и query match. Проверка: focused Rail Unit проходит соответствующие scenarios, без foreign module imports и booking/events/DB.
- [x] 1.3 Добавить независимые ticket marker/fare/price states и pure money tests: missing/null/partial price, indicator true/false/unknown/invalid, цена1234.05, explicit0/unknown, integral JSON1234.0, null type, invalid cents/overflow, RUB/USD/EUR и unsupported currency, mixed valid/invalid fares и32type limit. Одновременно описать contract в Rail README/комментариях. Проверка: valid marker/цены не стираются соседней ошибкой, schedule completeness независима, нет seats/sold-out/party-total/FX выводов.

## 2. Временный каталог и выбор станций

- [x] 2.1 Реализовать production streaming parser `stations_list` и временную RF passenger-station subset: закрытые canonical/localized mappings, source-qualified refs, limited coverage для неизвестных RF типов и unavailable при missing RF/0 пригодных станций. Fixtures: много стран,100000station boundary, duplicate conflicts,64MiB/depth32/token16KiB. Проверка: нет raw string/DOM/DB, structural overflow не публикует truncated snapshot, logical payload отдельно от RSS, unknown records не дают ложный полный no-match.
- [x] 2.2 Реализовать атомарный reference bundle catalogue+copyright с single-flight,≤2API GET,общим20s( copyright≤5s),30min absolute TTL от раннего receivedAt,30s failed cooldown. Ограничить весь autocomplete2 admitted callers, включая waiters/scans, без очереди; max2snapshots включает leased старые generations,48MiB catalogue payload. Controlled-time tests: waiter flood, отдельная/общая отмена, cancelled uncooperative loader без новых подписок, refresh/expiry/config epoch race, retired-memory cap. Проверка:0 startup/background calls, retained permits не освобождаются преждевременно, late candidate не публикуется и не накапливается.
- [x] 2.3 Добавить Application autocomplete по station/settlement name с q3–80,≤20suggestions и complete/limited(hasMore)/skipped/failed envelope. Проверить null generation при unavailable, no-match отдельно от cooldown/disabled/quota, strict generation validation и инвалидирование обоих refs после refresh. Проверка: pure/service cases всех station scenarios проходят; contract/documentation описывает400/429 и `Rail.StationSelection.Expired`, без supplier search при отказе.
- [x] 2.4 Добавить Infrastructure copyright DTO/parser и normalized attribution descriptor с body128KiB/descriptor4KiB bounds: официальный logo_vm/text/url, closed ASCII URL allowlist, без raw HTML в API/innerHTML. Fixtures проверяют missing/banner/timeout, homograph host, on*/srcdoc/extra elements/duplicate keys, корректный descriptor и0 arbitrary fetches. Проверка: unsafe attribution не публикует bundle/данные, parser tests не выдаются за live frame/CSP/sandbox acceptance; этот внешний gate документирован.

## 3. Официальный adapter, поиск и безопасный cache

- [x] 3.1 Реализовать `YandexRailScheduleProvider`/typed client в Infrastructure: HTTPS host/Authorization/no redirects/cookies,2MiB decoded,16KiB headers,depth32/token16KiB. Terminal fixtures проверяют query parameters, omission result_timezone, обязательный search echo даже при segments=[], тип/date/refs, duplicate critical keys и optional start_date. Проверка: positive train/suburban,401/403/429,oversize/malformed/offsetless/route mismatch проходят production parser;0 arbitrary URL follows, packages/security backport не меняются.
- [x] 3.2 Реализовать train/suburban/all orchestration:≤2GET,1page50/mode,2admitted searches,5s/child,8s/search. Tests: empty+failed/skipped→limited-zero, all skipped/failed, truncation, interval-only, optional absent interval array, исходный count до dedup; не требовать неподтверждённого sum(interval+scheduled)=total. Проверка: публичный kind complete/limited/skipped/failed,100journeys,0paging/retry/thread/скрытых reference calls, стабильный порядок внутри полученной части.
- [x] 3.3 Реализовать cancel/admission и immutable cache128entries/16MiB/256KiB: expiry=min(ранний mode receivedAt+TTL,bundle expiry),300s normal/30s empty или damaged ticket components; no schedule limited/failed/cancelled writes. Runtime profile epoch меняется процессом при source/credential/permissions/config changes и проверяется до dispatch/publication/write. Tests: ignored cancellation/held permits, mid-flight disable,10s bundle life, receivedAt/completedAt/cache-hit, eviction/clone/retired bounds. Проверка:0late writes/старого profile reuse/лишних children или retries.
- [x] 3.4 Добавить default-off real/fixed fictional и общий quota ledger для copyright/catalogue/search: atomic dispatch debit, rolling60s/24h, unknown transport/cancel не возвращает debit. Retry-After не сокращать; missing/invalid→60s,overflow→closed admission; новый restart не получает full real allowance автоматически. Tests: skipped local quota, failed remote429, useful mode+skipped mode, разрешённый cache-hit0debit, no real credentials/fake fallback. Проверка: historical500/day не становится actual budget, live single-instance/restart gate остаётся явным.
- [x] 3.5 Добавить нормализационные limits: public4MiB,100journeys,32fares/journey,ref128UTF-8 bytes,UID100characters,label1024UTF-8 bytes. Проверить boundaries и много bounded wire fields, которые вместе превышают output cap; optional label/price loss сохраняет reason, critical identity не усекать. Проверка: bounded failed/normalization-limit с0items без cache write, необязательный fares limit отражается отдельно от schedule completeness.

## 4. HTTP composition и исполняемый consumer contract

- [x] 4.1 Добавить Rail Api endpoints/contracts для station GET/search POST и module request/response policy: anonymous read-only,8KiB search body,strict fields/дубликаты,400/413/429/5xx отдельно от200 typed outcomes,no-store. Одновременно lean HTTP tests исполняют реальные routing/binding/cancellation/metadata с controlled services; неизвестный client input не отражается в ошибках. Проверка: public contracts не содержат supplier DTO/key/raw error/method URL, все outcomes/пустые списки и generation failures различимы.
- [x] 4.2 Добавить `Rail.Api.Composition` и минимальное Host подключение по ADR0023 после реализации реального milestone. Обновить только Rail participation в architecture inventory/positive selectors и approved facade dependencies; Trips и прочие guards сохранить. Проверка: evaluated project closure Debug/Release, type/method-body matrix и positive selectors проходят; Host владеет единственным global builder/map; default-off startup не вызывает supplier и не требует новой БД.
- [x] 4.3 Сверить нормализованный API с actual Host OpenAPI в отдельно разрешённом локальном/обычном CI fixture; обновить только соответствующий snapshot/request catalogue и выполнить реальные HTTP queries с terminal fictional supplier. Проверка: exact head/run/commands/test counts, declared no-store/outcome schemas и ключ0 в telemetry; lean fixture не выдаётся за этот результат. Ни disposable Host/DB startup, ни schema apply не запускать по одному наличию checkbox без соответствующего разрешения.
- [x] 4.4 Подключить существующий outbound redaction contributor для from/to/date и scoped inbound telemetry policy для q; добавить actual emitted log/span/exception tests на ключи/body/query при success/failure/cancel, сохраняя метрики без route text. Проверка: запрет работает не только в HttpClient, нормализованный attribution не содержит search/key и frame Referer исключён; общая platform policy других модулей не ослабляется.

## 5. Собственный Rail интерфейс

- [x] 5.1 Добавить Rail API service/types/strict decoders в существующий shared TS client; tests envelopes/money/offset/evidence, независимых ticket states/attribution/output bounds, exact mode set/request/source/profile/generation/counts и null freshness только без source data. Проверить строковую цену9007199254740993.05 без JS Number/render precision loss. Проверка: no provider HTML/DTO acceptance, безопасный URL только своего profile, no regression чужих consumers; README объясняет partial tickets/unknown inventory и source failures.
- [x] 5.2 Добавить `/rail`: форма,20station suggestions,300ms debounce, отдельные origin/destination/search epochs,25s autocomplete/10s search timeout и очистка rows/цен/refs при expiry/generation/полученном profile notice (без обещания instant push-revocation). UI показывает независимые ticket states, даты/offsets и banner→text→link через безопасный descriptor/sandbox/CSP/no-referrer, сохраняя существующие auth/self CSP directives. Tests: parallel fields,A→B→A,late replies,keyboard/live-region,ticket partial,clock expiry без auto query,typed failures и0browser/SSR retention/innerHTML/buy calls.
- [x] 5.3 Добавить fictional browser flow: выбор станций→date→оба modes→reported fare/unknown inventory; empty,interval-only/limited-zero,failure,partial ticket,stale generation,clock expiry,cancel/A→B→A и controlled local banner. Проверка:0actual external network/0purchase calls, видимая fictional метка, source notice/CSP/sandbox/Referer assertions; tests не заявляют live official banner acceptance. Full local browser/Host contour требует применимого разрешения.

## 6. Профиль измерений, интеграционная проверка и личный review

- [x] 6.1 Подготовить до финального source review воспроизводимый fictional load/memory профиль и harness/команды по design §8, без запуска load:3 cycles,1/2 admitted profiles и отдельная saturation,40MiB catalogue,100000station/128entry pressure,≥1000useful uncached successes/profile. Проверка: одинаковый warm business state baseline/recovery(1bundle,0search cache/children),≥5samples/30s baseline,120s recovery≤baseline+max(16MiB,15%),p95≤1s,полный denominator и отдельные refusals/cancel. Настройки/harness входят в итоговый diff, не добавляются после personal review.
- [ ] 6.2 Собрать и запустить разрешённые targeted source checks: Rail unit/adapter/lean HTTP,TS/Angular,architecture,format,dependency-free harness,strict OpenSpec/links/traceability; existing ordinary CI без bypass/новых paid jobs. Проверка: exact base/result head/run/attempt,команды,counts/outcomes/skips/profile; Docker-heavy aggregate при разрешении последовательно. Source/CI не объявляются measured/live приёмкой, профиль6.1 не запускается автоматически.
- [x] 6.3 Обновить Rail/current-state документы и исторические предположения concept: source date не run-start, temporary storage, actual quotas unknown, copyright banner+text+link, tickets не inventory. Сохранить Hotels RED/braces proof, оформить fictional run instructions и pending внешние gates: rights/access/budget/account restart/live metadata+banner/target. Проверка: ссылки/API/README согласованы, source implementation и внешняя приёмка различаются, ключи/регистрации/переписка не выполнялись.
- [x] 6.4 Основной агент лично проверяет полный результат gpt-6.1-sol, включая профиль6.1: все scenarios, source echo/identity/time/independent tickets, cancel/epochs/cache/bundle/waiter bounds, attribution security и receipts. Исправления требуют targeted regressions. Проверка: отдельный personal-review receipt с residual gates; commit/push/PR/sync/archive/publication/deploy только по отдельному поручению.

## 7. Отдельная измеренная приёмка и внешний допуск

- [x] 7.1 Только после отдельного разрешения запустить неизменённый профиль6.1, сохранить исходные receipts/RED, не подменять warm baseline/denominator и не расширять thresholds. Проверка: measured verdict/commands/counts/attempt/profile hash; до разрешения pending, source receipt не выдаёт этот gate за пройденный и не заимствует Hotels waiver.
- [x] 7.2 Сверить итоговый admission checklist из6.3 с фактически оставшимися внешними gates; все непроверенные access/rights/quotas/restart, actual source shape/official banner и target rollout оставить pending. Проверка: отдельный статус каждого gate без новых внешних действий. Реальные calls/keys/регистрация/переписка/платежи/миграции/деплой не входят в source implementation и требуют нового поручения.

## Traceability

Все scenarios каждого requirement входят в указанную задачу/набор проверок, а не заменяются одним happy path.

| rail-schedule-search requirement | Tasks |
|---|---|
| Bounded RF direct rail scope | 1.2,3.1,3.2,5.2 |
| Explicit RF station selection | 2.1,2.3,4.1,5.2,5.3 |
| Explicit search date and request validation | 1.2,3.1,4.1,5.2 |
| Source qualified journey identity | 1.2,3.1,3.2 |
| Faithful timetable timestamps and provenance | 1.2,3.1,5.1,5.2 |
| Match the requested railway journey | 1.2,3.1,3.2 |
| Source ticket facts without invented inventory | 1.3,3.1,3.5,5.1,5.2,5.3 |
| Exact source money and unknown price scope | 1.3,3.1,5.1,5.2 |
| Explicit completeness and failure outcomes | 3.2,4.1,5.1,5.2,5.3 |
| Bounded pagination and interval coverage | 3.1,3.2,5.1,5.3 |
| Bounded execution and cancellation | 2.2,3.3,4.1,5.2 |
| Temporary bounded catalogue and cache | 2.1,2.2,2.3,3.3,6.1,7.1 |
| Controlled official source access | 2.4,3.1,3.4,4.2,7.2 |
| Public HTTP and client contract | 4.1,4.3,5.1 |
| Bounded public data size | 3.5,4.1,5.1 |
| Search telemetry minimization | 3.1,4.4,5.2,5.3 |
| Reference bundle attribution readiness | 2.2,2.4,5.1,5.2,5.3,7.2 |
| Current profile and temporal data authority | 2.2,3.3,4.1,5.1,5.2,5.3 |
| Own interface with current request authority | 5.2,5.3 |
| Attribution and restricted retention | 2.1,2.4,3.1,3.3,5.2,5.3,7.2 |
| Module composition and isolation | 1.2,4.2,6.2 |
| Autonomous fictional proof | 3.1,3.2,4.1,5.3,6.2 |
| Separate measured and external acceptance | 1.1,4.3,6.1,6.2,6.4,7.1,7.2 |
