# Tasks

Реализация одного change поручена в текущем checkout от894c40f895b9e864263686f814ac0993bdbd3e8b; полномочия записаны в proposal. Source работу выполняет отдельный gpt-6.1-sol с владением затронутыми файлами; implementer не один в codebase и не откатывает чужие изменения. Пункты закрываются только по verification evidence; основной агент лично завершает5.4. Явно запрошенная fresh base никогда не заменяется текущим HEAD.

Порядок — пять сквозных срезов ниже. Каждый приносит наблюдаемый результат, свои тесты и нужные notes; не создавать второй implementation-plan. A1–A14 — evidence из traceability D8, S/B/W — requirements трёх specs.

## 1. Контракты, управляемые провайдеры и поиск в UI

Результат: пользователь может выбрать stay и понять complete-empty, skipped, failed и limited ответы в одном search UI, пока без внешних вызовов.

- [x] 1.1 Добавить Hotels VOs и компактные Search/Purchase/optional Reader contracts D1–D2 с формами support/results/prepared context/local quote; A1–A3/A6 unit/contract tests проверяют даты, occupancy, whole-stay money, capabilities без buy у search-only и отсутствие supplier DTO/HTTP/funding vocabulary в общем сценарии.
- [x] 1.2 В том же test/demo harness добавить controlled baseline и небольшой альтернативный fake с иным supported request, без supplier quote ID/webhook и с synchronous outcome; A2/A4/A5 tests доказывают offline/direct support agreement, full result snapshot round-trip и целые cache entries/byte bounds. Это не runtime fault API или вторая simulation platform.
- [x] 1.3 Добавить anonymous search/rates API contract и Angular destination/search/property/rate selection UI на controlled providers; A1/A3/A4 component/HTTP/browser tests покрывают curated areas, scopes/unknowns, loading/empty/skipped/failure/limited/expired, stale completion, keyboard/mobile. Короткие demo notes описывают первый наблюдаемый результат.
- [x] 1.4 Прогнать одинаковый supported local quote/purchase application contract на обоих semantic adapters с controlled persistence boundary; A6 tests доказывают отсутствие if-provider branch и source quote/webhook requirement, а unsupported fake не даёт empty success. Дополнить module glossary обычным вызовом и существенным edge case, без обещания real multi-provider readiness.

## 2. Duffel read mapping и ограничения с fake HTTP

Результат: тот же экран показывает нормализованные Duffel search/rates/prepared terms из fictional HTTP fixtures, source protocol остаётся в Infrastructure.

- [x] 2.1 Добавить provider-named Duffel read DTO/decoder/mapping и offline support rule; A1–A3/A4 fake-HTTP tests проверяют source date/occupancy limits, complete-empty/failed/limited, scoped room/board/cancellation, exact totals и expiry, сохраняя контракт и UI из среза1.
- [x] 2.2 Настроить validated configurable request/response/concurrency/admission/cache bounds D6 и Test-mode/default-off/credential/redaction guards; A5 tests проверяют refusal до HTTP, отсутствие redirects/disk spill, finite materialization, protected recovery share и status/ACK вне search permits. Новые dependencies/limiter framework не вводятся.
- [x] 2.3 Добавить Duffel Prepare mapping в локальные принимаемые terms и private versioned context; A3/A6 fake-HTTP+UI tests проверяют source quote/expiry внутри adapter, unknown mandatory amounts/eligibility, safe reasons и согласованность общей quote без обязательного supplier resource. Source notes фиксируют поддержанный subset и limits без реального ключа/API.

## 3. Quote, согласие, PII, долговечная покупка и private UI

Результат: полный путь 1/2 adults завершается приватной фактической бронью через реальный application/store flow и fake supplier; reload не отправляет покупку снова.

- [x] 3.1 Добавить Hotels EF reservation model D3 и owned quote/context persistence с conditional revision/unique single attempt; source migration generation лишь при отдельном разрешении, с additive SQL review. A6/A7 disposable-store tests проверяют локальную revision без source quote ID, context после restart, owner/expiry и racing quote replacement. Нет обязательного набора отдельных receipt/result/journal/tombstone таблиц.
- [x] 3.2 Добавить bound guest protection, key lifecycle без bootstrap и quote/review/guest UI; A3/A7/A9 tests проверяют minimal names/contacts, missing keys/tamper/binding, no plaintext before persistence, explicit consent и same-price material changes. Notes описывают необходимое будущее key provisioning, не выполняя его.
- [x] 3.3 Добавить provider permit до нового atomic purchase/attempt/claim/outbox и один bounded sender, Duffel Create mapping/correlation D3/D5. A8/A10 real-store+fake-HTTP tests: capacity для всех50 валидных contenders -> ровно1create/1retained attempt/result; отдельное exhaustion ->0create/0new claims; failed commit/losing race/completion освобождают permit; existing replay не берёт новый permit. Проверить new key/abort/crash, 201/200/202/provider503 versus proxy503, loss/save failure без retry/hedge/compensation/reset claim.
- [x] 3.4 Добавить private local list/detail и UI строго по D4, включая receipt replay/differences/cancelled не как active; A7/A10/A13 tests покрывают exact versus changed content, keyless zero-supplier GET, direct success/unknown/reload/login/logout/scope loss/stale responses. Purchase recovery не создаёт новый resource автоматически.
- [x] 3.5 Добавить bounded private-content cleanup и сохранение minimal reservation identities/claim/correlation; A9 persistence tests проверяют configured deadlines, physical removal без decrypt, unresolved retention и отсутствие позднего replay revival. Обосновать каждое самостоятельное storage разделение конкретным invariant и обновить короткие privacy/retention notes.
- [x] 3.6 Добавить реальную Hotels.Api.Composition, scope fixture/HTTP guards/no-store и Host source wiring только с implemented facade; A5/A7/A13 architecture/real HTTP/OpenAPI tests сохраняют один builder/map, module boundaries и disabled/Production behavior, полный keyboard/mobile flow без provider internals. Обновить current-state/module docs по реально реализованному, не по намерению.

## 4. Общий webhook, потери ответа, restart и поздние факты

Результат: один существующий route изолированно принимает Flights/Hotels, поздние наблюдения уточняют UI, исторические Flights entries остаются читаемыми.

- [x] 4.1 Добавить узкий BCL raw Probe/Accept seam с двумя реальными Infrastructure receivers и сохранить /webhooks/duffel в Host transport; A11 real-route/contract tests проверяют exact bytes/single signature/RAM bound, один owner до writes, compatible errors, wrong mode/account и required receiver set независимо от BookingEnabled.
- [x] 4.2 Исправить network v2 data.object/current event mapping и отделить historical saved root-object reader/codec D5; A11 fixtures/store tests: saved history читается после restart, valid v2 duplicate проходит validation до dedup и не переписывает envelope, old root-object network replay со знакомым ID отклоняется до dedup. Сохранить original Source/purpose, atomic own inbox/outbox/ACK loss, zero foreign payload rows и unrelated PII/store outage; не расширять до всех Flights APIs/events.
- [x] 4.3 Добавить маленькие existing-outbox handlers escalation/read-known-reservation и durable one-identity counters/window D4; A8/A11/A12 replacement-host/fake-clock tests покрывают early callbacks,200->10m->late first booking ID, максимум3reads, no-ID zero GET, duplicate/restart/exhaustion без reset/list/create, storage failure отдельно от business result.
- [x] 4.4 Свести Core transitions, TS decoder и UI к одной D4 таблице; A10/A12/A13 tests проверяют позднее matching confirmation со снятием resolved waiting reasons, actual cancellation без active resurrection, no-creation versus late positive conflict, sticky material mismatch/conflict и потерю response/reload без resend.
- [x] 4.5 Добавить короткий safe diagnostic runbook и receiver/subscription/rollback notes; A11 tests/receipt проверяют необходимые attempt/provider/environment/request/source refs/time/classification без raw errors/PII, receiving/drain после writer-disable и отсутствие manual resolver/DB reset. Operational notes не выполняют target provisioning.

## 5. Сквозная проверка, нагрузка и демонстрация

Результат: один воспроизводимый flow и короткий измеренный resilience proof с честной границей source/fake/sandbox evidence.

- [x] 5.1 Выполнить whole-flow 1/2-adult браузерные проверки и appropriate aggregate seven-layer checks: Hotels/Flights compatibility/Host/architecture, TS/UI, formatting, harness и strict OpenSpec. A1–A13 receipts содержат реальные команды/exit codes, full flow/mobile/keyboard и не смешивают test-fixture failure с product failure. .NET aggregate serial; paid AI categories исключены.
- [x] 5.2 До выполнения зафиксировать D6 L1 profile/env/seed/settings/gates, затем провести короткий disposable fake-supplier прогон normal/degraded/burst/recovery; A14 evidence содержит полный denominator, useful throughput,p95,429/5xx,provider starts/active,lag,RSS/heap/CPU/cache и recovery. Проверить status/ACK под search burst и bounded queue/memory; отдельные A8 proofs ровно1create и0claims при exhausted capacity остаются в3.3, а не подменяются отказом всем в L1. Failed gates не маскировать изменением порогов. Закрыто с явно принятым пользователем ограничением от2026-10-17 (D6/verification): M1 завершён без новых прогонов, исходный16/17/`workingSetRecovery=false`/`passed=false`/exit1 сохранён; непройденная проверка не объявлена успешной.
- [x] 5.3 Обновить demo instructions и traceability receipts A1–A14 с ссылками на реальные checks/limitations; воспроизводятся normal path, changed terms, reserved-awaiting-details/unknown, late callback и external cancellation, с fictional маркировкой/owned assets. PR/postmerge/sandbox/provisioning факты записываются только если реально отдельно выполнены; real multi-provider/production claims отсутствуют.
- [x] 5.4 Основной агент лично проверяет весь diff, contract substitutions, privacy/single sender/state table, current/historical webhook paths и реальные verification/load receipts после implementer gpt-6.1-sol; исправляет замечания и представляет результат с точным Git/source scope и оставшимися gates. Проверка — собственный review и evidence, не пересказ implementer; публикация или rollout автоматически не начинаются.
