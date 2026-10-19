# Tasks

Архивировано2026-10-19:25/25 задач завершены. Спецификация, M1 baseline sync, реализация отдельным gpt-6.1-sol, личная проверка, автономные и разрешённые relational/Host/privacy проверки выполнены. PR55 слит в dev, PR CI17/17 и post-merge CI16/16 прошли. После этого владелец поручил завершить OpenSpec: M1 архивирован без повторного sync, все4 M2 deltas применены к main и M2 архивирован. [Archive verification](archive-verification.md) и [completion review](../../../../docs/architecture/hotels-completion-review.md) фиксируют текущий итог. Следующие task descriptions и промежуточные receipts сохраняют историю выполнения; архив не закрывает load/supplier/target/deploy gates и не меняет failed16/17 M1.

## 1. Первый полный Hotelbeds slice

- [x] 1.1 Зафиксировать точный HEAD/base, согласованный Net/AT_WEB subset, используемые official operation fields/revisions и fixture provenance в M2 verification; проверить required/nullability/scope используемых DTO по официальным docs/Postman, не выполнять supplier requests. Неподтверждённое обязательное поле явно блокирует соответствующий шаг. Проверка: source evidence table и diff только согласованной области; baseline после M1 sync содержит все прежние требования/сценарии и исходный failed16/17.
- [x] 1.2 В Hotels Core/Application добавить минимальные normalized source identity/price-basis/condition metadata и persisted AdmissionBinding v2 вне accepted terms, не затрагивая M1 semantics; проверить unit-тестами1-/2-adult scope, unknown≠zero, namespaces, Net/retail и сериализацию profile/account/config/approval/mapping validity в существующем StateJson. Документировать поля/legacy meaning; текущая identity источника доступна через normalized Hotels port, без импорта Infrastructure options.
- [x] 1.3 Добавить Hotelbeds Infrastructure client/DTO/auth/mTLS options с Test-only/default-off composition, bounded fictional HTTP transport и versioned opaque context; проверить key/signature shape с TimeProvider, закрытый rateKey, redaction, no redirects/retry и fail-closed options без настоящих credentials/certificates. External-use guard без approvals даёт RightsNotCleared/attempted=false и0 dispatch; повторить проверку после отзыва approval/смены account/profile между Quote и Purchase:0 new claims/creates, replay admitted attempt сохраняется. Fictional context никогда не уходит actual transport; isolated M1 и допустимый admitted recovery сохраняются. Disabled startup не требует schema/keys.
- [x] 1.4 Реализовать Availability/CheckRate/полные conditions и bounded module-owned content для одного fictional отеля; проверить wire-shape integration tests RECHECK/BOOKABLE, content absence, partial/empty/errors, Net/AT_WEB eligibility, timezone/board/tax unknowns. Prepare не делает Book; неподдержанный профиль даёт fixed reason. Сохранить documented field-to-domain table.
- [x] 1.5 Реализовать один Hotelbeds Booking и optional known-reference details reader через existing application ports; проверить confirmed/details-pending/no-creation/unknown, semantic accepted-to-actual mismatch и correlation при разных stage IDs. Controlled-clock cases различают Hotelbeds HTTP90s, Duffel HTTP140s, общий application145s и completion-store10s без retry; таймаут не доказывает no-creation. Lost response без reference вызывает0 reads/list scans/resends; документация recovery объясняет предел.
- [x] 1.6 Протянуть один полный Hotelbeds search→rates→quote→consent→book→private-result/reload через существующие API/Angular routes; вместе добавить fictional HTTP/owner browser acceptance после разрешения harness. Проверка: exactly1 Hotelbeds Book,0 real requests, отдельный Duffel happy path продолжает работать, один-/два взрослых и private reload показывают исходные source/money/status.

## 2. Ограниченный поиск двух источников

- [x] 2.1 Заменить последовательный search/rates fan-out на максимум два source executions с15s child/17s aggregate budget и module admission2 без очереди; проверить overlap, timeout, caller abort, late result и uncooperative transport с удержанием permits до actual child completion и ровно одним release. Multi-step Search/Rates/Prepare получает оставшийся15s срок без reset на каждом HTTP step; controlled-clock tests это различают. Документировать fixed cancellation/deadline reasons.
- [x] 2.2 Разделить foreground/recovery counters двух provider keys: каждый1active/24starts-min и отдельные1active/6starts-min; reads берут source lease внутри adapter, Create — в Application до claim, recovery — в runner; Create/reader не захватывают его повторно. Проверить isolated saturation в обе стороны, exactly1 Book при available-capacity race, failed commit/lost race release и responsive private reads. Отдельный test с одним slot подтверждает, что Create/recovery не отказывают сами себе; local starts не объявляются числом оплаченных HTTP requests.
- [x] 2.3 Ввести полные source statuses(kind/reason/attempted) и aggregate truth table; проверить table-driven cases для Complete0/Complete0, Complete0/Timeout, Limited0/Failed, Failed/Skipped, всех skipped, local refusal versus supplier429/auth/decode. HTTP/TS/UI fixture round-trip сохраняет каждое различие, без fake fallback.
- [x] 2.4 Выпустить schemaVersion2 public projection + server-only opaque-handle sidecar в одном immutable entry, local tokens и group/leaf selector по D3–D4. Проверить старую/новую request shapes и существующий PUT quote route, invalid selectors до HTTP, collisions, закрытый rateKey≤4096UTF8 bytes и ContextTooLarge без обрезания. Лимиты2MiB entry/128entries/16MiB включают public+private bytes; тесты eviction/expiry/byte reduction удаляют обе части без orphan dictionary. HTTP/TS/OpenAPI не сериализуют sidecar; admitted context от cache независим.

## 3. Сопоставление отелей и происхождение

- [x] 3.1 Добавить pure Hotels matching rules и versioned reviewed map≤100pairs/200properties с evidence/reviewer/check/expiry/use-scope; проверить fictional trusted pair, namespace identity, name/coordinates-only candidate, stale/ambiguous/conflicting pair и invalid duplicate/transitive membership. Не нужен Shared service, новый каталоговый importer или mapping UI; правило документировано в M2 evidence.
- [x] 3.2 Сформировать property groups со stable attributed representative и полным набором source offers; проверить differing stars/scales/benefits/board, exact same-source duplicates и conflicting-key quarantine с Limited/Conflict. Ни cross-source dedup rates, ни наследование чужих benefits не допускаются; API/UI snapshots сохраняют provenance и причины разделения.
- [x] 3.3 Связать selection/quote с mapping pair/revision/validity и material evidence; сохранить guard в AdmissionBinding/StateJson вне accepted terms и ограничить quote ValidUntil. Проверить cache loss/restart, изменение/expiry между Quote и Purchase и crossing deadline при capacity/protection:0 new claims/Book; после admission — сохранённые facts/barrier/recovery. Документировать refresh reason без переписывания истории.

## 4. Стоимость, условия и понятный порядок

- [x] 4.1 Нормализовать supplierPayable/optional travellerPrice, included/excluded local obligations, scoped board/cancellation без parsing display strings; проверить GBP200 incl20 + separate10→debit200/comparison210,2guests/2nights без умножения, GBP+EUR, Unknown≠0, Net≠retail и source offset/unknown coverage. Документировать pricing profile и material obligations, блокирующие покупку.
- [x] 4.2 Реализовать `hotel-cost-first-v1`: comparable basis/currency buckets, exact totals, ordinal semantic identity/fingerprint tie-break; проверить permutation/culture/new-token invariance, unknown/mixed-currency stable sections и отсутствие commission/brand/bookability бонусов. Golden cases фиксируют full reason/scope/version, без «лучший» при разных условиях.
- [x] 4.3 Рассчитать scoped group minima/order без одной цены от для разных currencies: primary bucket — ordinal-minimum comparable basis/currency, одна group — одна card, остальные minima видны на ней. Проверить EUR+GBP group рядом с GBP-only group, incomplete/unmatched visibility, ties и прежний policy snapshot. Документация/reason fixtures отличают навигационный primary bucket от экономического преимущества и supplier price от неизвестной retail.

## 5. Полная выдача и осознанный выбор в Angular

- [x] 5.1 Показать grouped и unmatched cards, source-specific rates, attributed facts и отдельные состояния skipped/complete-empty/limited/failed/purchase-unsupported; проверить component/TS decoder tests и доступные mobile/keyboard cases. Человек видит область сравнения и неизвестные суммы, без внутренних workflow терминов.
- [x] 5.2 Связать явную смену provider с новым token/quote revision/consent при сохранённом reservation ID текущей purchase flow, в том числе при той же цене; проверить board/cancellation/source/profile changes, expired mapping/cache и response races. После Unknown продолжение flow открывает retained result, не создаёт новый resource/action ради обхода барьера; component/HTTP tests показывают0 effects старого выбора и отсутствие auto-failover. Не заявлять global dedup независимых бронирований.
- [x] 5.3 Проверить общий fictional browser сценарий: оба sources, verified matching, выбор Hotelbeds, свежие условия, одна покупка, lost reply/reload, Unknown без reference, иной supplier дешевле и logout. Отдельно подтвердить Duffel happy path; удержать guest/consent только в memory с epochs и no-store, проверив storage/URL/console/SSR отсутствие PII. Запуск harness требует своего разрешения.

## 6. Durable authority, совместимость и приватность

- [x] 6.1 Расширить durable integration cases для выбранного provider/account/environment/context: healthy contenders exactly1 create, capacity refusal0 claims, changed-content replay conflict, crash/abort/restart/other action ID без resend, cross-source replacement0. Сохранить source capacity/replay semantics и короткий read-only runbook Unknown без reset/list/new purchase.
- [x] 6.2 Проверить Hotelbeds known-reference3reads/5min, late identity, exhausted allowance/restart, actual mismatch/error и writer-disable с сохранением допустимого read/drain. При смене reader account/environment/profile относительно admitted binding —0 supplier reads и ObservationUnavailable/review, без сброса барьера. Host tests сохраняют общий Duffel ingress с раздельными inboxes и без Hotelbeds webhook; supplier/PII outage не делает local GET сетевым.
- [x] 6.3 Добавить legacy M1 serialized reservation/context fixtures без новых полей; проверить read/replay/actual matching, Unknown/Legacy display и отсутствие consent mismatch от defaults. Отдельно новая M2 quote без обязательного AdmissionBinding v2 должна отказать, не используя legacy fallback. Проверить owner-before-decrypt, key outage/tamper, retention/barrier/logout и Infrastructure/Api.Composition boundaries. Если потребуется relational schema — отдельный migration gate до генерации.

## 7. Интеграционная проверка и передача результата

**Историческая граница первого source-only receipt (22/25).** Тогда автономно были пройдены JSON-cloning in-memory CAS contenders/replay, сохранённый M1 row/context, source/account/map guards, Unknown без resend/read, known-reference3reads/5min и writer-off drain, HTTP owner checks и browser privacy/epochs. Это само по себе не доказывало relational CAS/commit/crash durability, ordinary Host ingress/inbox и physical crypto/privacy; `HotelHttpTests` и `HotelDurablePurchaseTests` в той фазе не запускались. Три checkbox были оставлены pending без waiver. Новое локальное разрешение и фактические relational/process/Host receipts закрывают эту границу отдельно, не переименовывая прежнюю source-only проверку в SQL proof и не перенося failed16/17 M1 memory waiver на M2.

- [x] 7.1 Выполнить разрешённые focused unit/integration/HTTP/architecture/TS/browser checks по изменённым consumers, без нового broad audit; зафиксировать команды, exact head, counts/results и fictional transport. Ориентиры: `tests/hotels/Travel.Modules.Hotels.Tests.Unit`, `tests/hotels/Travel.Modules.Hotels.Tests.Integration`, Hotels tests в `Travel.Host.Tests.Integration`, Angular Hotels specs и существующий travel-e2e harness. Более широкий required CI/format/harness check запускать только в применимом согласованном scope, Docker-heavy solution checks — последовательно.
- [x] 7.2 Обновить verification/traceability, runtime gate checklist, current-state/module scope и source-only run instructions по фактическому результату; проверить отсутствие claims о sandbox/mTLS handshake/target/live proof. Нагрузочный acceptance M2 остаётся отдельно согласуемым профилем: не запускать автоматически, не переносить waiver M1 и не называть controlled-clock tests performance proof; каждый незапущенный required gate явно pending, ни одна красная проверка не превращается в зелёную.
- [x] 7.3 Основной агент лично проверяет итог `gpt-6.1-sol`: diff, source boundaries, contract substitution, source authority/privacy/recovery, matched/unknown money и фактические receipts; исправления перепроверены targeted tests. Финальный статус отделяет source checks/CI/fictional E2E/load/external acceptance/target readiness; commit/push/PR/sync/archive/publication/deploy только по отдельному поручению.

Первый личный review2026-10-19 завершился в code/autonomous scope:22/25 задач; его команды, counts и исходный красный Architecture receipt сохранены в [verification.md](verification.md). Последующий отдельный локальный этап закрыл6.1–6.3: module SQL/HTTP11/11; независимый полный Host44/45 (единственный RED — устаревший expected OpenAPI snapshot), после согласованного исправления только snapshot отдельный snapshot/privacy повтор2/2; standalone регрессия114/114. Все25 новых M2 relational cases в полном Host прогоне прошли. Подробные receipts и исходные RED сохранены в [relational-verification.md](relational-verification.md).25/25 фиксирует source/fictional/local relational приёмку задач, а не CI/load/supplier/target/rollout acceptance.

## Traceability

Каждая строка ссылается на requirement title в соответствующем M2 delta; inherited M1 invariants проверяются также в6.1–6.3. Все сценарии конкретного requirement входят в указанные проверки, а не заменяются одним happy path.

| Capability / Requirement | Tasks |
|---|---|
| hotel-stay-search / Explicit supplier availability and failure | 2.3,5.1 |
| hotel-stay-search / Current selection and indicative availability | 2.4,5.2 |
| hotel-stay-search / Bounded safe search interaction | 2.1,2.4 |
| hotel-stay-search / Bounded parallel read execution | 2.1,2.2 |
| hotel-stay-search / Autonomous source profile and external-use gate | 1.1,1.3,7.2 |
| hotel-property-matching / Separate property room rate and offer identity | 1.2,3.2 |
| hotel-property-matching / Reviewed bounded matching evidence | 3.1 |
| hotel-property-matching / Conservative doubt and conflict handling | 3.1,3.2 |
| hotel-property-matching / Attributed facts and preserved offer variants | 3.2,5.1 |
| hotel-property-matching / Mapping revisions cannot rewrite purchase history | 3.3,5.2 |
| hotel-offer-comparison-ranking / Explicit price basis and completeness | 1.2,1.4,4.1 |
| hotel-offer-comparison-ranking / Whole-stay comparison without implicit conversion | 4.1,4.2 |
| hotel-offer-comparison-ranking / Structured and scoped condition comparison | 1.4,4.1 |
| hotel-offer-comparison-ranking / Deterministic offer order | 4.2 |
| hotel-offer-comparison-ranking / Group prices retain comparison scope | 4.3 |
| hotel-offer-comparison-ranking / Explainable versioned ranking snapshot | 4.2,4.3,5.1 |
| hotel-reservation-booking / Exact current purchase consent | 1.2,1.3,3.3,5.2,6.1 |
| hotel-reservation-booking / Supported test purchase and complete obligations | 1.4,1.5,4.1 |
| hotel-reservation-booking / Complete fictional UI and separate acceptance | 1.6,5.1,5.3,7.2 |
| hotel-reservation-booking / Hotelbeds protocol stays behind Infrastructure | 1.1,1.3,1.4,1.5,6.3 |
| hotel-reservation-booking / Source-specific dispatch capacity | 2.2,6.1 |
| hotel-reservation-booking / No cross-source replacement after uncertain purchase | 5.2,5.3,6.1 |
| hotel-reservation-booking / Hotelbeds bounded known-reference observation | 1.5,6.2 |
| hotel-reservation-booking / Preserve legacy accepted state and private boundaries | 5.3,6.2,6.3 |
