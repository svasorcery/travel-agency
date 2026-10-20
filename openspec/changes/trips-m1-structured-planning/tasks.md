# Tasks

Только M1 из [design](design.md); генератор M2 и текстовый ввод M3 сюда не входят. Пользователь утвердил план и поручил реализацию в отдельном worktree от свежего dev, независимое ревью и доработки. Дополнительно разрешены source migration/SQL и собственный одноразовый PostgreSQL с вымышленными данными и временными тестовыми ключами. Реальные среды/БД, full Host/Keycloak, модели/поставщики, Публикация ветки и PR в dev отдельно разрешена; merge и deployment остаются отдельными действиями. Checkbox отражают проверенный прогресс; исходные receipts и ограничения — в [implementation.md](implementation.md).

## 1. Core: пожелания и правила плана

- [x] 1.1 Реализовать Trips value objects, first-party city metadata и TripBrief с пределами design §2, ErrorOr и TimeProvider на use-case границе. Проверка: unit tests valid/historical dates,14-day/adult/enum/city/Unicode boundaries; metadata содержит только city IDs/names/IANA, без provider claims. Обновить module README разделом входных параметров.
- [x] 1.2 Реализовать TripPlan/day/block identity, add/edit/reorder/move/remove и non-destructive brief changes из design §3. Проверка: unit tests точного набора дней,duplicate IDs,8/112cap,time pair,overlap warning,shorten/extend,city/currency guards,сохранения правок. Domain events не вызывают broker/effects.
- [x] 1.3 Реализовать exact PlanningMoney/subtotal/unknown count и budget warnings. Проверка: unit tests decimal/zero/precision/max/overflow/currency/partial estimates,без FX/float и ложного within-budget. В module README привести пример неполного subtotal и ручного времени, которое не является расписанием.

## 2. Application: owner commands и сохранённое состояние

- [x] 2.1 Добавить create/list/get/replace/delete use cases и минимальные persistence/protection ports с trusted owner, server-owned metadata и типизированными outcome. Проверка: unit tests owner guards до payload access,foreign/unknown404,read-only GET,отсутствия upsert и любого provider/model/money вызова. Документировать port boundaries рядом с модулем.
- [x] 2.2 Задать revision/no-op/overflow/CAS/capacity contract и canonical snapshot sizing до commit. Проверка: application tests invalid/stale/all-or-nothing, no-op timestamp, body vs full detail size,near-limit Unicode и derived-field reserve; accepted snapshot остаётся читаемым. Codec не вводит обратных ссылок Application→Api/Infrastructure.
- [x] 2.3 Сформировать bounded summary/cursor contract и uncertain-write outcomes без generic receipts framework. Проверка: owner-scoped pagination,stable tie-break,20summary cap,no content in cursor,unavailable≠empty; контракт не обещает exactly-once POST или snapshot isolation списка.

## 3. Infrastructure: relational persistence и приватность

- [x] 3.1 Реализовать TripsDbContext/configuration/store: schema trips, protected payload, owner index, atomic CAS/update/delete и serialization последнего owner slot. Проверка: repository-focused tests lost CAS,update/delete race,100plan concurrent-create cap,atomic rollback/roundtrip; actual PostgreSQL запускать только в разрешённом disposable contour. Не импортировать Support internals.
- [x] 3.2 После отдельного применимого разрешения подготовить additive source migration, SQL review и rollback notes; не применять БД. Проверка: source diff создаёт только Trips-owned schema/tables/indexes, сохраняет чужие migrations и имеет описанный rollback без автоматического удаления данных. Без разрешения задача остаётся pending.
- [x] 3.3 Реализовать Trips-owned Data Protection configuration/purpose/binding и canonical JSON codec. Проверка: protection tests owner/TripId/revision/schema binding,tamper,missing key ring,20-row failure без partial/private leakage; bytes near-limit roundtrip и summary total bound. Записать конфигурацию/recovery без ключей,паролей и копирования чужих key rings.
- [x] 3.4 Реализовать default-off registration и enabled schema/protection readiness без ordinary-startup provisioning. Проверка: disabled не открывает DB/keys; enabled missing schema/key fail closed; проверки startup-policy исходников и разрешённые fixture tests. Документировать, что существующий Support auto-migrate pattern не копируется в Trips.

## 4. Api и модульная композиция

- [x] 4.1 Реализовать routes/strict write DTO whitelist/owner principal parsing/ETag/preconditions/RFC7807 по design §4/4.1. Проверка: lean HTTP tests201/200/204/401/403/404/400/409/412/428/503,forged fields,weak/wildcard tags,body limits,exact types и no-store; JSON examples→create/replace/detail vectors проверяют days/blocks nesting,nulls,stable IDs/array order. Зафиксировать API examples в module README.
- [x] 4.2 Включить реальную Trips.Api.Composition через Host facade и точечно обновить closure/inventory/CI selectors без ослабления layer policy. Проверка: architecture positive/negative tests,existing module routes/auth/gates,один global builder/map. Интегрировать на принятой базе совместно с владельцем Rail shared files,сохранив требуемую ancestry.
- [x] 4.3 Проверить canonical full response/summary sizes,privacy diagnostics и отсутствие внешних effects на реальном HTTP serialization path. Проверка:128KiB edge UTF8→save→GET,20max summaries,emitted log/span/error tests без private fields,disabled endpoint behavior; никакого позднего reject уже сохранённого допустимого плана.

## 5. TS client и Angular workspace

- [x] 5.1 Добавить реально используемые Trips DTO/strict decoders/API methods в existing shared TS client. Проверка: decoder tests unknown/forged shapes,date/range/money/revision losslessness,byte/count bounds,cursor,ETag/statuses и общие JSON vectors design §4.1; write DTO строится по whitelist,без server-field spread;0automatic mutation retries. Обновить client usage docs.
- [x] 5.2 Добавить lazy /trips и /trips/:id,structured form,owner list/day editor,block operations,explicit save/delete и honest M1 labels. Проверка: component tests всех форм/лимитов/brief guards,partial estimates/warnings,keyboard/labels/focus/live-region,empty/unavailable/auth/conflict;нет generate button и supplier state.
- [x] 5.3 Реализовать saved baseline/dirty buffer,owner/plan/request epochs,submitted edit version и monotonic revision. Проверка: A→B→A,owner change,edit B during save A,oldGET after save/delete,dirty navigation,uncertain POST/PUT/DELETE и blocked double-submit; state не теряет edits и не восстанавливает удалённый план.
- [x] 5.4 Обеспечить private rendering/ephemeral browser state и feature-aware navigation. Проверка: logout clears dirty memory,late response discarded,0local/session/IndexedDB/SW/SSR retention,markup literal,disabled hides entry,enabled-unready distinguishable. В user-facing docs показать реальный M1 workflow и pending M2 без заглушки генерации.

## 6. Интеграционная проверка согласованного M1

- [x] 6.1 Добавить fictional browser flow create→edit→save→reload→stale conflict→delete и второй owner denial. Проверка: complete flow с0external model/provider/purchase calls,near-limit/late-response regressions,HTTP no-store; actual Host/DB/E2E запускать только при применимом допуске,иначе сохранять явный pending вместо fake green.
- [x] 6.2 Выполнить разрешённые targeted unit/adapter/HTTP/TS/Angular/architecture/format/harness/OpenSpec checks и ordinary CI без bypass/paid AI jobs. Проверка: exact base/result/run/attempt,commands/counts/outcomes/skips и полный scenario denominator; Docker-heavy suites последовательно на общем daemon,CI по существующим раздельным runner.

Локальная часть6.2 выполнена с сохранёнными receipts. Первоначально Git publication не входила в source scope; теперь пользователь отдельно разрешил commit/push/PR в dev и доведение CI до GREEN, без merge. Задача6.2 закрыта по [фактическому PR CI receipt](ci-verification.md): source head b39b6ce,run39455463089,attempt1,все19ordinary jobs GREEN,только paid AI skipped. Локальный GREEN и target acceptance учитываются отдельно.

## 7. Review и передача к следующему этапу

- [x] 7.1 Провести independent implementation review и личную проверку полного M1 diff с регрессиями для исправлений. Проверка: все15requirements и49scenarios traced,обе design-audit P2 защищены tests,source/CI/disposable/target gates разделены; commit/push/PR/merge/sync/archive только по отдельному поручению.
- [x] 7.2 Обновить фактическое состояние Trips/README/current-state после реализации и оформить вход M2: confirmed brief/candidate boundary,read-only source contracts,data rights/freshness и human/model quality gates. Проверка: документация не объявляет M1 генератором,не выбирает непроверенную модель/источник и не запускает M2; новый M2 change требует отдельного scope.

## Traceability

Каждая строка относится ко всем сценариям requirement, а не только happy path. Scenario names и нормативные требования находятся в [delta spec](specs/trip-plan-workspace/spec.md).

| trip-plan-workspace requirement | Tasks |
|---|---|
| Structured single destination brief | 1.1,2.1,4.1,5.1,5.2,6.1 |
| Honest planning status | 1.2,2.1,5.2,6.1,7.2 |
| Owner private workspace | 2.1,3.1,4.1,5.3,6.1 |
| Bounded editable day blocks | 1.2,2.2,4.1,5.2 |
| Non destructive brief changes | 1.2,2.2,5.2,6.1 |
| Exact optional planning money | 1.3,2.2,5.1,5.2 |
| Atomic revision checked changes | 2.2,3.1,4.1,5.3,6.1 |
| Bounded reads and owner capacity | 2.3,3.1,3.3,4.3,5.1 |
| Explicit handling of uncertain writes | 2.3,5.1,5.3,6.1 |
| Payload validation and private rendering | 1.1,2.2,3.3,4.1,4.3,5.1,5.4 |
| Private storage and diagnostics | 3.3,3.4,4.3,5.4 |
| Session and editor isolation | 5.3,5.4,6.1 |
| Owner deletion | 2.1,3.1,4.1,5.3,6.1 |
| Accessible explicit editing | 5.2,5.4,6.1 |
| Isolated opt in capability | 3.4,4.2,5.4,6.2,7.2 |
