# Hotels M2 — планирование и проверка реализации

Дата:2026-10-19. Завершены25/25 source/local tasks, PR55 и post-merge CI; реализация слита в dev на5a0fb7a36de6101170c4b676415e616890b4bfa6. M1/M2 архивированы в разрешённом порядке, M2 перенесён в main. [Archive verification](archive-verification.md) содержит текущие CI/sync итоги; нижние разделы сохраняют историю планирования, красных проверок и принятых этапов. Нагрузка, реальные поставщики и target/deploy остаются отдельными непройденными этапами.

## Историческая база до реализации

Локальные `HEAD`, `dev`, `origin/dev` и фактический `git ls-remote origin refs/heads/dev` совпали: `75dfba532c0f321286630e3d04fbea70347903a7`. `git merge-base --is-ancestor fd94b9c91cd4345f1b5a684a3c8b73d2bc84e6b9 HEAD` завершился0. Более новая ветка не откатывалась и не переключалась.

До sync `git diff HEAD --stat` был пуст, `git status --short` показывал только новый untracked каталог M2. После sync добавлены три новых main specs, перечисленные ниже. Продуктовые исходники, исходный корпус M1, dependencies/locks и braces backport не изменялись; tracked diff по-прежнему пуст. Stage/commit/push не выполнялись.

## Документы и проверка

| Проверка | Результат / предел доказательства |
|---|---|
| OpenSpec status | proposal/design/specs/tasks существуют; `isPlanningComplete=true`. CLI `isComplete` означает наличие planning artifacts, не реализованные25 задач. |
| `node tools/openspec/run.mjs validate hotels-m2-multisupplier-search-booking --strict --json` | До sync: valid=true, exit0, два INFO об отсутствующих main specs. После разрешённого sync: valid=true, exit0, issues=[]; зависимость MODIFIED targets устранена. |
| Baseline dependency | Сначала M1 main отсутствовали, поэтому M2 нельзя было синхронизировать/архивировать. По разрешению владельца M1 перенесён; M2 сверён с main, но его требования в main не применялись. Архивирование не выполнялось. |
| Structural review | Четыре capability deltas,24 requirements,75 scenarios после саморевью (первоначально64); все MODIFIED titles существуют в pending M1, все14 прежних scenario titles этих требований сохранены. |
| Traceability |24/24 requirement titles связаны с задачами,25 уникальных unchecked tasks; ни одна implementation task не выполнена. |
| Local links/whitespace | Относительные links разрешаются; trailing whitespace отсутствует. URI schemes, включая mailto, проверкой файловых links не подменяются. |
| Fresh builder check | Независимый агент прочитал proposal/design/4 specs, затем tasks. Существенных неразрешённых проектных вопросов не обнаружил; потребовал явный task-level тест external-use guard. |
| Исправление review | В design D1 и task1.3 зафиксирован guard: без approval actual M2 даёт RightsNotCleared/attempted=false и0 supplier dispatch, с сохранением isolated M1/admitted recovery. |
| Личная проверка основным агентом | Сверены exact source binding, no cross-source resend, legacy accepted-state semantics, permit lifetime, mapping expiry, typed price basis/unknowns, API selector shape и M1 scenario preservation. |

Исследование и его источники сохранены в [research.md](research.md). Актуальный выбранный scope и предложенные решения — [proposal.md](proposal.md)/[design.md](design.md), executable acceptance requirements — в [specs](specs), будущие шаги — [tasks.md](tasks.md). Исследовательские альтернативы не являются дополнительными implementation commitments.

## Личное саморевью по поручению владельца

Основной агент повторно лично сверил все planning artifacts с существующим кодом, без делегирования или нового общего аудита. Первоначальная structural/builder проверка не выявила перечисленные ниже пробелы. Они исправлены в этом же change; это уточнения безопасности и определённости плана, не изменение выбранной пары или реализация.

| Приоритет / находка | Основание / исправление / будущая проверка |
|---|---|
| P1 — approval guard заканчивался на Quote; после смены account/profile можно было по-разному истолковать допустимость Purchase | [Текущий service](../../../../modules/hotels/Travel.Modules.Hotels.Application/HotelReservationService.cs) сохраняет quote, а Purchase не зависит от search cache. D1/D8 теперь требуют persisted AdmissionBinding v2 в StateJson и повторную проверку перед новым claim, включая revoked approval/Fictional→External/account/mapping/validity. Existing replay обрабатывается первым. Reader не обращается в другой namespace. Tasks1.2/1.3/3.3/6.2/6.3 проверяют0 effects и legacy boundaries. |
| P1 — opaque rateKey до Prepare был назван закрытым, но его server-only storage и byte accounting не определены | [Текущий search cache](../../../../modules/hotels/Travel.Modules.Hotels.Application/HotelSearchService.cs) хранит сериализуемые snapshots, а [HotelOffer/StayTerms](../../../../modules/hotels/Travel.Modules.Hotels.Core/StayValues.cs) имеют публичные IDs. D2/D4 задают один public+private entry, finite4096-byte handle, accounting обеих частей, explicit HTTP projection и совместный expiry/eviction. Никакого необъяснённого глобального словаря или rateKey в RateId. Tasks1.3/2.4 и новые scenarios проверяют это. |
| P2 — смешаны HTTP и application deadlines, неявно определены владельцы Create/recovery leases | В коде HTTP create140s, application cancellation145s, completion10s; [runner](../../../../modules/hotels/Travel.Modules.Hotels.Infrastructure/Observations/HotelObservationRunner.cs) уже держит recovery lease. D3/specs/tasks различают эти budgets, HBX HTTP90s, общий15s execution для multi-step reads и единственного владельца каждого lease; timeout не доказывает no-creation. Tasks1.5/2.1/2.2 проверяют deadline/lease separation. |
| P2 — одна multi-currency property group могла получить разные трактовки primary section и повториться карточками | D7 явно задаёт одну card на group, primary ordinal (basis,currency), scoped minima на ней и stable order без cross-currency winner. Новый EUR+GBP/GBP-only scenario и task4.3 исключают неоднозначность. |
| P2 — route Quote ошибочно назван POST, а фактический contract использует PUT | D4 исправлен на существующий `PUT /api/hotels/reservations/{id}/quote`; task2.4 сохраняет route и совместимость request shapes. Endpoint не менялся. |

Добавлены11 проверяемых scenarios, все25 implementation tasks остаются unchecked. Также явно ограничено обещание защиты от повторов: один purchase flow сохраняет свой reservation ID; Unknown не создаёт новый ресурс как recovery. Это не глобальная дедупликация любых независимых покупок разных ресурсов. Supplier stage IDs сравниваются по доказанной correlation и material scope, не по буквальному равенству rateKey.

Повторная structural/link/whitespace и strict OpenSpec validation подтверждают только согласованность документов. Новые продуктовые test cases ещё не написаны и не запускались. Net/supplier-cost как проектный профиль теперь утверждён владельцем; реальные supplier права и применимый account profile остаются отдельным будущим этапом.

## Утверждение спецификации и разрешённый sync M1

Владелец2026-10-19 подтвердил оба обсуждавшихся решения: итоговую спецификацию M2 и перенос принятых M1 contracts в основные спецификации. Это включает Net/supplier-cost без retail markup, reviewed matching, comparator/budgets и исправления личного саморевью. Повторно запрашивать эти же решения без изменения scope не требуется. Отдельное поручение начать реализацию этим подтверждением не давалось.

Применён `openspec-sync-specs` только к трём `artifactPaths.specs.existingOutputPaths` из status M1. Перед записью получен успешный specs-instruction snapshot закреплённого launcher; root подтверждён как `D:\_Projects\_github\travel-agency`. Все три main targets отсутствовали. Purpose перенесён дословно, все ADDED requirement blocks перенесены без смысловых изменений под единую `## Requirements`; delta operation headers в main отсутствуют.

| Созданный main contract | Requirements / scenarios | SHA256 |
|---|---|---|
| [duffel-webhook-routing](../../../specs/duffel-webhook-routing/spec.md) |7 /15 | `1ebfa80f1c0a1b46795ecebcd5c4a4708db86ed1befb6fab9c76578d3dc53d40` |
| [hotel-reservation-booking](../../../specs/hotel-reservation-booking/spec.md) |17 /38 | `871265ea3f02562c0ed22f7614332c3d581d4a553cba1086acdbd4edbb67c5cb` |
| [hotel-stay-search](../../../specs/hotel-stay-search/spec.md) |9 /22 | `cdc3aae8fa5cda59eaff0b2e7ce904b07a019d5e960761cbe238ed35baf00893` |

Проверено полное равенство Purpose/requirement blocks с исходными M1 deltas:33 requirements/75 scenarios. Все8 файлов исходного M1 побайтно сохранились; SHA256 нормализованного manifest `{relativePath: fileSHA256}` с сортировкой keys: `0d3ba5fb53fbd50a15f4d13fffb94b62fe7780064fd3ae9b7310c370f9f9ca85`. Повторный расчёт того же main format даёт тот же текст, без дополнительных изменений. M1 остаётся в active changes, не архивирован; исходные reports не редактировались. Failed memory limitation дословно присутствует и в main.

M2 reconciliation выполнен без применения M2 к main: каждый MODIFIED requirement найден, все14 его прежних scenario titles сохранены, ADDED не конфликтуют с baseline; остальные baseline blocks в in-memory merge неизменны. Две новые M2 capabilities существуют только как deltas. В M2 остаются24 requirements/75 scenarios и0/25 implementation tasks. Статус утверждения и выполненного sync согласован в proposal/design/tasks/двух зависимых deltas.

`node tools/openspec/run.mjs validate --specs --strict --json`:10/10 main specs valid, exit0. Только уже существующие пять INFO о длинных текстах в Flights cancellation; эти тексты не менялись. M2 strict validation проходит без INFO об отсутствующих targets. Structural/traceability/local-link/whitespace checks прошли. Product tests/builds/Host/DB/load и supplier calls не выполнялись.

## Начало разрешённой реализации

Владелец2026-10-19 отдельно поручил реализовать Hotels M2 здесь агентом `gpt-6.1-sol`, затем лично проверить результат основным агентом. Разрешены код и автономные тесты; настоящие API calls, migration generation/application и deploy исключены. Implementation agent `/root/hotels_m2_implementation` запущен на согласованном checkout `75dfba532c0f321286630e3d04fbea70347903a7`, без branch switch/stage/commit/push. Его промежуточные результаты — в [implementation-report.md](implementation-report.md); task7.3 принимает только основной агент.

Автономные проверки используют owned fictional HTTP transport, in-memory store/fake authentication и локальный browser fixture. Старые `HotelStoreFixture`/обычный Hotels Host fixture запускают PostgreSQL/schema/migrations и не выполняются в этом scope. Полный solution-wide test, настоящая supplier acceptance и L1 нагрузка не запускаются автоматически. Миграции не генерируются даже для disposable fixtures.

Исходный Angular Hotels baseline: `NX_DAEMON=false npx.cmd nx test web --watch=false --include=apps/web/src/app/hotels/**/*.spec.ts`,2 files/11 tests passed, exit0. Initial sandbox Nx `spawn EPERM` потребовал того же запуска с escalation. Sandbox .NET baseline без single-worker завис и был прерван; он не является product failure или passing baseline. Проверенные осиротевшие MSBuild nodes только двух наших parent processes были точечно завершены; дальнейшие .NET проверки запускаются с `-m:1 /nodeReuse:false --disable-build-servers`. Независимые результаты после source изменений фиксируются отдельно.

### Уточнение официального wire input

Raw HTML официальной [API reference](https://developer.hotelbeds.com/documentation/hotels/booking-api/api-reference/) ссылается через `Redoc.init` на полную [OpenAPI Hotel Booking API](https://d2gsjv9lq0esbh.cloudfront.net/open-api-documentation/OpenAPI-Hotel-BookingAPI-3.0.yaml). Документ публично прочитан без supplier business requests:370370 bytes, SHA256 `d566f132a95be1b8e9642d85b33438f9ec39a104030d842e8cd6623e46210de6`, Last-Modified2026-10-16T08:06:03Z, OpenAPI3.0.1/API1.0, declared Apache2.0. Это закрывает прежнее ограничение исследования «полный Swagger ещё не проверен»; account/runtime acceptance не устанавливает.

Для bounded JSON subset официальные поля сверяются с этой schema, [workflow](https://developer.hotelbeds.com/documentation/hotels/booking-api/workflow/) и официальной [Postman collection](https://www.postman.com/clientapi/apitude-by-hotelbeds/documentation/h58zuu9/bookingapi). Два publisher inconsistencies сохраняются явно: required `room` при declared/sample JSON `rooms` array; старый test server URL в schema при отдельной актуальной mTLS policy. Это не основания отправлять придуманные поля или отключать mTLS. `clientReference` максимум20 символов; holder/pax names максимум50; взрослым age не required; `net` string и booking `totalNet` number читаются точно без binary floating-point.

Наличие taxes не ограничивается Swagger properties: в ApiRateCheckRate этот optional field пропущен, однако официальный [Tax Breakdown](https://developer.hotelbeds.com/documentation/hotels/knowledge-base/tax-breakdown/) прямо распространяет rate-level structure на Availability, CheckRates и Booking. Поэтому документированный CheckRate taxes extension разрешён в fictional wire fixtures; RECHECK не исключается целиком из scope. Отсутствующие tax/amount facts остаются unknown и блокируют purchase, без наследования старых недоказанных obligations. Реальная activation этой feature аккаунту не выполнялась.

## Историческая очередь проверок перед реализацией

Спецификация утверждена, M1 sync/reconciliation выполнены, разрешение code/autonomous implementation получено. После работы `gpt-6.1-sol` необходимы личный review основным агентом и фактические test receipts. M1 archive, migrations, load, supplier/target acceptance, publication/CI/deploy остаются отдельными действиями. Ни approval, ни запуск агента не закрывают implementation tasks без доказательств.

До начала разрешённой реализации build/product unit/integration/DB/Host/browser/load tests не запускались. После начала реализации фактические автономные receipts фиксируются ниже и в implementation-report; они не являются DB/ordinary Host/load или external acceptance. Supplier API, secrets, настоящие certificates, migration generation/application, аккаунты/переписка, publication/CI/deploy не выполнялись. Fictional protocol proof не доказывает права, supplier mTLS handshake, account limits, certification или target readiness.

Исторический M1 L1 остаётся **16/17**, `workingSetRecovery=false`, `passed=false`, exit1. M1 owner-accepted limitation не исправлена документами и не переносится на M2.

## Личная проверка реализации — промежуточные результаты

Реализацию выполняет `/root/hotels_m2_implementation` на `gpt-6.1-sol`; основной агент проверяет diff, пишет собственные adversarial tests и повторяет проверки. Это ещё не окончательный source acceptance и не выполненный task7.3. Финальные прогоны должны пройти после исправлений и freeze исходников.

Проверены границы Core/Application/Infrastructure/Api.Composition, private opaque context, whole-entry cache accounting, exact money, источник принятой покупки и сохранение M1 contracts. Существенные находки переданы исполнителю вместе с воспроизводящими тестами:

| Область | Найденная проблема / требуемое исправление |
|---|---|
| Supplier facts | Чужие даты, недокументированный booking status и противоречивый clientReference не должны превращаться в принятые или положительные факты брони. |
| Цена и ограничения | Некорректные boolean flags, повторяющиеся JSON money properties и противоречивый tax completeness не авторизуют покупку. Разный decimal scale не создаёт ложный mismatch, fingerprint или конфликт дубликатов. |
| Mapping | Непустые поля и revision недостаточны: сохранить digest всей принятой reviewed evidence и membership, затем сравнить перед новой покупкой. Reviewer/evidence не выходят в публичный JSON. |
| Account/transport | Public alias не заменяет account binding. Private credential identity фиксируется отдельно; Duffel OrganisationId остаётся прежним для webhook correlation. Fictional context нельзя отправить ordinary transport, включая M2-off/restart. |
| Quote authority | Старый selected token нельзя подготовить через новый account/profile/config. Проверка сохранённого normalized selection binding выполняется до Prepare, а свежего срока — перед claim. |
| Bounded aggregation | Caps и combined byte overflow не теряют всю корректную выдачу без явного Limited; expired source не удаляет свежий другой источник. Один opaque source key с разными фактами изолируется, одинаковые факты сохраняются один раз. |
| UI и recovery | Смена источника сохраняет reservation ID и сбрасывает согласие; Unknown не создаёт новый action/resource как recovery. Local refusal, empty success, source error и partial остаются разными состояниями. |

Собственные тесты основного агента: `HotelbedsReviewTests`, `HotelMappingReviewTests`, `HotelAggregationReviewTests`. Их assertions не ослаблялись для получения зелёного результата. Исполнитель может форматировать эти файлы; изменение binding helper для нового evidence digest отдельно просмотрено и принято.

Лично выполненные промежуточные прогоны: Hotels Unit58/58; standalone wire/capacity/HTTP bounds65/65; Angular Hotels13/13; API-client233/233, включая Hotels4/4; headless browser4/4 с4 purchase POST, exactly1 выбранным supplier Create на сценарий и0 альтернативных Create. Browser включает HBX1/2 adults, source replacement в том же resource, потерянный ответ/reload, Unknown без reference и Duffel/mobile390/keyboard/logout. Просмотрены desktop Unknown и mobile Duffel screenshots. Это результаты промежуточного кода; последующие изменения требуют финального повторения.

`check:ai-harness`:134/134, exit0. Strict M2 OpenSpec validation: valid=true, issues=[], exit0; это проверка корпуса, а не продукта. Первичный `git diff --check` нашёл три лишних пустых строки на EOF; окончательная format-проверка ещё требуется.

### Красный архитектурный receipt

Личный полный Architecture прогон дал181/182,1 failed,0 skipped, exit1. Исходный receipt сохранён в `TestResults/hotels-m2/root-architecture.trx` и не подменяется будущим зелёным файлом. Сбой — `NullReferenceException` в `LayerArchitecturePolicy.ParseModuleType`, а не заключение об отсутствии нарушений. Новый compiler-generated generic dependency имеет одновременно Assembly=null и Namespace=null; проверка безусловно обращалась к Assembly.Name. Исполнитель добавил regression и точечную null-safe обработку с прежним namespace fallback; запрещающие matrix/IL rules не удаляются. Полный повторный прогон основным агентом обязателен.

### Hotelbeds: предел positive no-creation

Основной агент лично сверил pinned `/bookings`400 response в OpenAPI и официальный [Hotels Best practices](https://developer.hotelbeds.com/documentation/hotels/knowledge-base/best-practices/). INVALID_REQUEST/INVALID_DATA описывают неправильный запрос/поля, но не дают достаточно явной гарантии отсутствия брони и финансового эффекта. Отсутствие booking/reference также не является таким доказательством. Поэтому Hotelbeds non-2xx, включая400 validation, остаются Unknown после dispatch. Автоматический resend/restart/failover запрещён; общий совет документации начинать процесс заново после500 не применяется к неопределённой покупке. Positive no-creation для Hotelbeds потребует отдельно подтверждённого supplier evidence. Общая уже принятая M1 семантика положительного no-creation этим не меняется.

## Итог личной проверки основным агентом

Разрешённый code/autonomous этап завершён2026-10-19. Реализацию выполнил отдельный `gpt-6.1-sol`; основной агент лично просмотрел код и исправления, написал28 adversarial cases в трёх своих test files, проверил их неизменённые assertions и самостоятельно повторил финальные проверки. Существенных открытых source findings в проверенном объёме не осталось. Task7.3 закрыт основным агентом:22/25, с открытыми6.1–6.3.

### Зафиксированные исходники

HEAD остаётся `75dfba532c0f321286630e3d04fbea70347903a7`, ветка `dev`; branch switch/stage/commit/push не выполнялись. Код находится в рабочем дереве. Финальный manifest66 изменённых/новых code/fixture files: `TestResults/hotels-m2/root-source-final.json`, SHA256 canonical sorted `{repoRelativePath:fileSHA256}` — `e714f916e33b336ad31794ecad87b5c28d92d751527269955f3a60b1f24eab85`. Документы OpenSpec и Markdown в этот code manifest не входят.

После первого freeze допущены только один новый owned request-contract test и style-only изменения шести файлов: JSON whitespace, JS formatting и порядок TS imports. Остальные исходники не менялись. Финальные Integration/browser/Biome и повторные TS/Angular проверки выполнены после этих изменений; Unit/Architecture/OpenAPI consumers не менялись. Manifest проверен на отсутствие иных отклонений.

`git diff --exit-code HEAD -- openspec/changes/hotels-m1-duffel-booking` —0; исходный M1 corpus не редактировался и не архивирован. SHA256 трёх перенесённых main specs по-прежнему совпадают с таблицей sync выше. M2 в main не применён. Dependencies/locks/braces backport и relational schema/migrations не изменялись.

### Финальные проверки, выполненные лично

Все команды запускались из корня репозитория. Для .NET использовались `--no-restore -m:1 /nodeReuse:false --disable-build-servers --verbosity minimal`; DB-heavy solution run не выполнялся. Каждый перечисленный ниже прогон завершился exit0, без failed/skipped tests.

| Проверка | Фактический результат / receipt |
|---|---|
| `dotnet test tests/hotels/Travel.Modules.Hotels.Tests.Unit/Travel.Modules.Hotels.Tests.Unit.csproj` |79/79,0 skipped; `TestResults/hotels-m2/root-unit-final.trx` |
| `dotnet test tests/hotels/Travel.Modules.Hotels.Tests.Integration/Travel.Modules.Hotels.Tests.Integration.csproj --filter "FullyQualifiedName!~HotelHttpTests&FullyQualifiedName!~HotelDurablePurchaseTests"` |114/114,0 skipped; `root-standalone-final.trx`. Только два DB fixture класса исключены заранее; среди выполненных — captured auth/epoch/geolocation, оба adapters, HTTP/CAS/legacy/drain, clocks/bounds и28 независимых review cases вместе с Unit. |
| `dotnet test tests/Travel.Tests.Architecture/Travel.Tests.Architecture.csproj` |183/183,0 skipped; `root-architecture-final.trx`. Новый null-dependency regression и прежние forbidden-fixture checks пройдены. Исходный красный `root-architecture.trx` сохранён отдельно. |
| `dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --filter FullyQualifiedName~HotelsWolverineOpenApiTests` |1/1; `root-openapi-final.trx`. Actual Wolverine discovery/OpenAPI, standalone TestServer без Host Program/AddHotelsModule/DB/provider transport. |
| `NX_DAEMON=false npx.cmd nx test api-client --run --skip-nx-cache` |233/233,14 files, включая Hotels4/4; build также exit0. Повтор после style-only imports, cache skipped. |
| `NX_DAEMON=false npx.cmd nx test web --watch=false --include='apps/web/src/app/hotels/**/*.spec.ts' --skip-nx-cache` |14/14,2 files; повтор после style-only imports, cache skipped. |
| `node tools/demo/hotels-browser.mjs --m2-memory` |4/4,4 purchase POST,0 page errors; `TestResults/hotels-m2/browser/receipt.json`. Ровно один выбранный Create и0 другого на сценарий. |
| `dotnet csharpier check` с точным списком изменённых C# files |53 files, exit0. |
| `npx.cmd biome ci --reporter=summary --colors=off` с точным списком изменённых TS/MJS/JSON |11 files,0 errors,7 warnings/162 infos сохранены. Первые6 style/import errors исправлены без изменения lint configuration/unsafe fixes. |
| `npm.cmd run check:ai-harness` |134/134 и validation passed. Authenticated Codex verifier не запускался. |
| `node tools/openspec/run.mjs validate hotels-m2-multisupplier-search-booking --strict --json` |valid=true, issues=[], exit0; подтверждает corpus, не runtime acceptance. |
| `git diff --check` |exit0. |

TRX files находятся в `TestResults/hotels-m2/`. Обычное предупреждение Verify о поиске SolutionDir у вложенных test projects не является failed test; оно не скрывалось. Результат624 product/architecture/client test cases и4 browser scenarios не включает134 harness cases и не заменяет CI/DB/load/supplier gates.

Browser заново проверил HBX1adult с заменой source в том же reservation, HBX2adults с потерей ответа и reload, HBX Unknown без reference и Duffel/mobile390/keyboard/logout. Проверены25 console entries и270 request URLs, storage values и SSR body: guest/contact/rateKey не обнаружены. Основной агент лично просмотрел финальные grouped-search и lost-reply screenshots; ранее также mobile/Unknown. Использовался actual application flow с двумя настоящими по исходному протоколу adapters и owned terminal HTTP handlers, а не подмена бизнес-API готовыми JSON. In-memory CAS/AES/fake auth являются явными test boundaries.

### Исторические gates первого source-only этапа

- **6.1:** настоящие relational CAS/commit/crash/process-restart proofs не выполнены. Memory CAS/serialization/replay/one-effect tests не объявляются доказательством работы PostgreSQL.
- **6.2:** ordinary Host/shared Duffel ingress/inboxes с настоящим store не запускались. Known-reference3reads/5min, account mismatch и M2-off draft refusal/admitted drain проверены автономно для обоих sources.
- **6.3:** physical persistence/encryption/key-outage/retention на relational store и прежние ordinary Host privacy fixtures не запускались. Исторический JSON, owner/epoch, lost-reply barrier и browser privacy проверены в разрешённом автономном контуре.

Source checks и fictional browser acceptance пройдены. Полная CI, M2 load/performance, actual supplier sandbox/account/rights/mTLS/certification, target schema/keys и rollout остаются pending и требуют соответствующего отдельного разрешения. Никакие migrations не генерировались и не применялись. Настоящие supplier endpoints, credentials/certificates и платные сервисы не использовались. Публикация/PR/deploy и M1/M2 archive не выполнялись. M1 L1 по-прежнему16/17, workingSetRecovery=false, passed=false, exit1; waiver на M2 не переносится.

Own helper sessions Angular33784 и in-memory fixture84166 остановлены; основной агент подтвердил отсутствие listeners на4202/5111. Собственный временный публичный OpenAPI YAML удалён только после проверки его точного SHA256. Исходные M1 reports и чужие процессы/ресурсы не затронуты.

## Отдельно разрешённый локальный relational/Host этап

После read-only аудита владелец2026-10-19 разрешил описанный одноразовый локальный тестовый контур: Docker PostgreSQL, применение существующих миграций/служебных схем только в своей пустой тестовой БД, isolated actual Host и его перезапуски/сбои, собственные временные ключи и сертификаты, синтетические данные и фиктивные supplier HTTP handlers, очистка только своих ресурсов. Это новое разрешение снимает прежний запрет local Host/DB/migrations только для этого контура; stale CLI context такого разрешения не отменяет.

Задачи6.1–6.3 выполняет отдельный `/root/hotels_m2_relational_acceptance` на `gpt-6.1-sol`; затем основной агент лично проверит diff и повторит covering tests. Исходное состояние22/25 не меняется до фактических receipts. Подробный журнал этого этапа — `relational-verification.md`; этот verification и окончательный личный acceptance принадлежат основному агенту.

Перед началом повторно сверены `HEAD`, `dev`, `origin/dev` и фактический `git ls-remote origin refs/heads/dev`: `75dfba532c0f321286630e3d04fbea70347903a7`. Исходный code manifest66 files побайтно совпал с предыдущим receipt; рабочее дерево и approved M1/main corpus сохраняются. Docker context — локальный `desktop-linux`, `npipe:////./pipe/dockerDesktopLinuxEngine`. Исполнитель зафиксировал исходный чужой buildkit container `692f04d16796` и локальный image `pgvector/pgvector:pg17`, image SHA256 `7ae6051efd0e60444282c27c7e141af07f322ce033300e727a49c3dd11075e38`; чужие ресурсы не используются и не очищаются.

Реальные supplier/payment/model/paid API, реальные credentials/certificates/системный certstore, рабочие базы, новые migration files, публикация/CI/PR/commit/push, нагрузочный L1/soak/performance, sync/archive и deploy остаются вне разрешения. M1 memory16/17 остаётся failed без переноса waiver. Старые source-only результаты выше сохранены как исторический receipt, а не доказательство ещё не проведённых relational/Host проверок.

## Итог личной PostgreSQL/Host проверки — 25/25 задач

Задачи6.1–6.3 выполнил отдельный `gpt-6.1-sol`; основной агент лично прочитал изменения и усилил доказательства: оба contender доходят до одной expectedVersion, проигравший save освобождает permit; fault атомарности срабатывает только после фактической вставки durable work; crash marker содержит реальный счётчик supplier effect; физический protector проверяется также на подмену purpose. Все эти исправления внесены исполнителем и перепроверены. Production-код этого этапа не менялся.

| Финальная проверка | Фактический результат / receipt |
|---|---|
| Новые M2 cases в личном полном Host прогоне |25/25 passed: real SQL/outbox, source-bound admission/replay,2 фактических process kills, request abort, known-reference allowance/drain, shared signed ingress, legacy и физические keys/retention. |
| Полный `Travel.Host.Tests.Integration.Hotels` namespace, основной агент |44/45 passed,1 failed,0 skipped; `TestResults/hotels-m2-relational/root-host-final.trx`. Единственный failed — прежний expected OpenAPI snapshot. Этот прогон остаётся красным и не переписан. |
| Повтор snapshot и отдельной public-schema/privacy проверки, основной агент |2/2 passed,0 skipped; `root-openapi-final.trx`. Изменён только ожидаемый snapshot после structural review, без ослабления assertions или изменения API. |
| Прежние module relational/HTTP cases, исполнитель |11/11 passed,0 skipped; `module-relational-final.trx`:10 durable и1 HTTP, включая stale cleanup/admission race. |
| Standalone Hotels regression после изменения общей wire fixture, основной агент |114/114 passed,0 skipped; `root-standalone-final.trx`. |
| `dotnet csharpier check` всех7 изменённых code/project files |exit0; `root-csharpier-final.log`. |
| `npm.cmd run check:ai-harness` |134/134, validation passed, exit0; `root-harness-elevated.log`. Первый sandbox launch дал Windows spawn EPERM и сохранён в `root-harness-final.log`; scoped запуск вне этого ограничения прошёл. Authenticated Codex verifier не запускался. |
| Pinned OpenSpec apply/status и strict validation, основной агент |25/25, remaining0, state `all_done`; valid=true, issues=[], exit0. `root-openspec-apply-final.json` / `root-openspec-validate-final.json`. Это corpus evidence, runtime доказан отдельными тестами выше. |

Основной прогон запускался после финального build, последовательно с остальными Docker/.NET проверками:

```powershell
dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --no-restore -m:1 /nodeReuse:false --disable-build-servers --filter 'FullyQualifiedName~Travel.Host.Tests.Integration.Hotels' --logger 'trx;LogFileName=root-host-final.trx' --results-directory TestResults/hotels-m2-relational
dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --no-build --no-restore -m:1 /nodeReuse:false --disable-build-servers --filter 'FullyQualifiedName~HotelsHostTests.Isolated_Testing_Host_preserves_the_public_OpenAPI_contract|FullyQualifiedName~HotelsWolverineOpenApiTests' --logger 'trx;LogFileName=root-openapi-final.trx' --results-directory TestResults/hotels-m2-relational
dotnet test tests/hotels/Travel.Modules.Hotels.Tests.Integration/Travel.Modules.Hotels.Tests.Integration.csproj --no-restore -m:1 /nodeReuse:false --disable-build-servers --filter 'FullyQualifiedName!~HotelHttpTests&FullyQualifiedName!~HotelDurablePurchaseTests' --logger 'trx;LogFileName=root-standalone-final.trx' --results-directory TestResults/hotels-m2-relational
```

Snapshot review установил: прежние6 Hotels200 responses ссылались на общий `IResult`; утверждённые typed response annotations делают видимыми31 public schema. Selector принимает прежний selectionKey либо новый propertyGroupKey, как предусмотрено D4. Path keys, non-Hotels paths и securitySchemes не изменились. Private handles, rateKey, admission/credential binding, protected guest/context и mapping audit не появились. Structural diff и исходный received сохранены в `openapi-structural-diff.json` / `openapi-received-before-acceptance.txt`. Повтор двух относящихся к исправлению тестов прошёл; остальные44 assertions/cases и production source не менялись.

Первоначальный7-file freeze сохранён: `source-freeze.json`, file SHA256 `b40a78e56d0dd35729aea3953725155363f35efcb2ed807371d1bf9eddc3de20`. После единственного snapshot update —8-artifact `source-freeze-after-snapshot.json`, file SHA256 `b382e5c70d4b4e59cacac3bddfa9a4b804315ab2c6521f61a8ba859ff6e14ae7`. Основной агент повторно сверил все8 hashes. Прежний66-file source manifest вне оговорённых test/demo изменений совпал; новые migrations и dependency/lock changes отсутствуют. HEAD/origin/dev остались `75dfba532c0f321286630e3d04fbea70347903a7`; fd94b9 остаётся ancestor. Работа не staged/committed/pushed.

Личная cleanup-проверка по TRX зафиксировала71 собственный container/temporary-directory receipt и4 crash-child PID: живых собственных контейнеров/процессов и оставшихся зафиксированных каталогов нет. Дополнительное чтение Temp не нашло ни одного `travel-hotels-host-*` каталога. В Docker остался только исходный чужой BuildKit `692f04d16796`, без вмешательства в него. Receipt — `root-cleanup-final.json`; ручная массовая очистка не применялась.

В допустимом source/local scope6.1–6.3 закрыты, итог25/25. Остались отдельные непройденные этапы: ordinary CI, согласованный M2 load/performance, supplier account/rights/mTLS/certification и target/deployment. Реальные supplier/paid API и рабочие базы не использовались; применялись только существующие миграции в одноразовых пустых локальных БД. M1 corpus/main contracts и исходные failed memory reports сохранены; M1 L1 остаётся16/17 (`workingSetRecovery=false`, `passed=false`, exit1). M1/M2 не архивированы, M2 main sync не выполнялся.

## Отдельно разрешённые ветка, коммит и PR в dev

Владелец отдельно поручил оформить Hotels M2 отдельной веткой, сделать коммит/PR в `dev`, дождаться CI и проверить результат. Реальные supplier/paid API и deploy остаются запрещены; merge, M1/M2 archive и M2 main sync не поручены. Перед созданием ветки повторно прочитан фактический origin/dev, выполнен fetch: base `75dfba532c0f321286630e3d04fbea70347903a7` совпал с локальными HEAD/dev. `feat/hotels-m2-multisupplier` создана явно от `origin/dev`; ancestry проверена, более новая база не подменялась HEAD. Рабочий M2 diff и approved M1 spec-sync prerequisite сохранены.

Личная предпубликационная проверка обнаружила CI portability defect: новый crash test искал только `bin/Debug`, тогда как отдельный Host integration runner собирает `Release` и прежде не собирал child helper. Прежний исполнитель `gpt-6.1-sol` исправил ровно test helper path и две команды существующего CI job: configuration берётся из фактически запущенной test assembly, без fallback; existing helper restore/build выполняется в том же Release job. Production, topology, filters, timeouts и paid-eval gate не менялись. Основной агент лично проверил diff и повторил оба crash boundary cases в каждой конфигурации.

| Предпубликационная проверка основного агента | Receipt в `TestResults/hotels-m2-pr/` |
|---|---|
| Hotels unit79/79,0 skipped | `root-unit-pr.trx` |
| Architecture183/183,0 skipped | `root-architecture-pr.trx` |
| Debug helper build и process-crash2/2 | `root-helper-Debug.log`, `root-process-Debug.trx` |
| Release helper build и process-crash2/2 | `root-helper-Release.log`, `root-process-Release.trx` |
| CSharpier full repository,1046 files, exit0 | `root-csharpier-pr.log` |
| Biome full repository,312 files,0 errors, exit0 | `root-biome-pr.log`; existing62 warnings/972 infos сохранены |
| AI harness134/134 и validation passed | `root-harness-pr.log` |
| .NET/CI inventory valid, exit0 | `root-inventory-pr.log`; validator tests62 passed/1 skipped |

Результат GitHub CI фиксируется отдельно по точному commit SHA, PR base/head и workflow run/jobs в PR и локальном publication receipt. Локальные результаты не подменяют ещё не завершённый PR run. Обычные17 jobs, включая обязательный PR E2E, должны завершиться успешно; единственный ожидаемый skipped job — отдельно разрешаемый платный AI evaluation. Deploy workflow остаётся manual-only и не запускается. Исторические local freezes, failed OpenAPI/Architecture attempts и исходный M1 failed16/17 сохранены; новая CSharpier/CI portability правка их не переписывает.

## Публикация, merge и архивирование

PR55 source a7f67b0070b194e02b85c68d5d4a6370da05f9b6 слит в dev, merge5a0fb7a36de6101170c4b676415e616890b4bfa6. [PR CI39257646397](https://github.com/svasorcery/travel-agency/actions/runs/39257646397) прошёл17 обязательных jobs, включая E2E; [post-merge CI39260364791](https://github.com/svasorcery/travel-agency/actions/runs/39260364791) прошёл16 обычных jobs. Ожидаемые skips соответствуют event guards, без waiver или ручного rerun. Полные counts и текущие main contracts — в [archive verification](archive-verification.md) и [completion review](../../../../docs/architecture/hotels-completion-review.md).

По последующему прямому поручению владельца выполнено закрытие корпуса: сначала M1 без повторного sync, затем проверенный sync M2 и его archive. Относительные ссылки исправлены; source metadata, исходные нормативные deltas и failed memory evidence сохранены. Новый документационный PR получает собственный exact-head CI; приведённые implementation runs не подменяют его.
