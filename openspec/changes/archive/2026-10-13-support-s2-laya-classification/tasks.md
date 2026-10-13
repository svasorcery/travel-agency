# Tasks

Source/ordinary CI acceptance complete:23/23tasks, including actualCI/publication/postmerge gates. Earlier source checkpoints are retained in verification. D1–D8 относятся к [design](design.md); полный acceptance map — [verification](verification.md). Tasks не разрешают download/inference/Host/DB/keys/grants/deploy или publication.

## 1. Domain policy and published order context

- [x] 1.1 Добавить Core typed suggestion/queue/outcome и urgency-v1 policy с TimeProvider-supplied now; проверить fixed8categories, unknown≠Normal, exact24h/now/past, existingHigh и no-mutation unit tests.
- [x] 1.2 Добавить minimal published Flights triage leaf и сохранить existing ownership contract; проверить BCL/ErrorOr-only DTO, отсутствие financial/passenger types и единственный Support consumer в Contract/Architecture tests.
- [x] 1.3 Реализовать Flights Application authoritative owner-bound triage reader и facade registration; проверить ownerless/foreign/invalid stream, Held-before-EF, next segment/return/open-jaw, closed/unknown/missing itinerary, supplier-report barrier/dispatched unknown cancellation/creation-confirmation uncertainty/expired HeldUntil, UTC boundaries и ноль writes/provider/decrypt в unit + disposable CI integration.
- [x] 1.4 Уточнить architecture policy/документацию published leaf и примеры urgency без bank/supplier claims; проверить все module-pair rules и сохранение24assemblies/Host facade ownership.

## 2. Application request, privacy and bounded admission

- [x] 2.1 Добавить provider port/options-neutral result и disabled implementation; проверить disabled/incomplete profile даёт0decrypt/0order/0model calls и не затрагивает S1 creation/queue/actions.
- [x] 2.2 Выделить небольшой Application access helper из существующего guard и собрать revision-bound full1–4message input; проверить before-decrypt access/state/expiry, sentinel5, first ID/order/duplicates, binding, Unicode/24KiB/8000bounds и0HTTP при overflow/unreadable input.
- [x] 2.3 Оркестрировать one explicit request, pre-inference/post-result case rereads и urgency context; проверить reply/triage/resolve/retention races, staleR, missing/foreign/age30sec context, fixed errors и отсутствие case mutation/financial dispatch.
- [x] 2.4 Реализовать module-owned admission2active/3operator-minute/12process-minute/no waiting с bounded≤1000metadata partitions, same-case in-flight guard и deadline10seconds; проверить parallel requests, full partitions/eviction, canceled/timeout attempts consume budget и manual actions не блокируются.
- [x] 2.5 Сохранить request/outcome/limits/privacy примеры в canonical change и module operational reference; проверить согласование с [evaluation](evaluation.md), отсутствие PII diagnostics и ясное разделение source/real-model gates.

## 3. Infrastructure real Laya protocol

- [x] 3.1 Добавить LayaSupportClassificationProvider typed HTTP client, server-side approved BaseUri/Bearer/options/profile and off-default composition; fake-handler tests проверяют fixed POST path/body, explicit multilingual/bundle identity, no redirect/retry/body logging,0calls when disabled.
- [x] 3.2 Реализовать bounded authenticated health observations и full response decoder; protocol fixtures проверяют wrong/missing loadedrevision/device, scoped CPU fallback counter increase/reset/missing with CUDA restored, routing/model/choices/schema, duplicates/unknown fields/probabilities/NaN/oversize и no raw provider diagnostic leak.
- [x] 3.3 Реализовать input/token/options truncation и distinct confidence/gate guards; fixtures покрывают actual passed-with-absent-low_confidence Candidate, independent entropy/max-probability guards и missing/unevaluated/abstained/zero/wrong thresholds, collapsed options, Jev strict projection и manual fallback.
- [x] 3.4 Проверить transport timeout/401/422/503/cancellation и lost response через fake HTTP handler; счётчик подтверждает максимум1inference/no replay, bounded response/deadline и различие uncertain compute/NoEffect.
- [x] 3.5 Документировать отдельно разрешаемый self-hosted profile, preload/offline preparation, secrets/privacy/no content telemetry/device и rollback; synthetic source examples не содержат actual credentials/target defaults и не скачивают веса.

## 4. HTTP contracts and operator UI

- [x] 4.1 Добавить operator POST endpoint/DTO и route-specific1KiB body guard в существующую Api facade; HTTP tests покрывают authority before processing, wrong azp/customer/booking-only, duplicate/unknown/malformed input, expired/staleR/disabled/no-store и routes registration.
- [x] 4.2 Добавить strict shared TS request/result decoder/API method с fixed category/queue/lifetime/revision/profile/urgency guards; client tests отклоняют чужой case/request, stale/invalid expiry, inconsistent status/category и неполный typed result.
- [x] 4.3 Добавить explicit request and current-generation suggestion state в existing SupportStateService; Angular tests проверяют1call/click,0auto calls/reload, TTL/newerR/request/navigation/client/identity/permission401/403erase и manual fallback.
- [x] 4.4 Добавить operator panel, preview+prefill+existing manual Save, отдельный High choice/as-of explanation и manual overrides; component tests проверяют no auto-save, form invalidation, sourceOperator/CAS/receipt barriers, disabled/busy/errors/accessibility.
- [x] 4.5 Расширить существующий Support demo browser fixture и focused Playwright scenarios для кнопки/правки/unknown save/late response; browser tests на демофикстурах проходят без Laya процесса, и документация честно называет этот уровень доказательства.

## 5. Integrated source acceptance and later runtime handoff

- [x] 5.1 Обновить настоящий Host-generated OpenAPI snapshot/contract examples и проверить actual endpoint generation/architecture в disposable ordinary CI; public contracts S1/issuer permissions остаются совместимыми.
- [x] 5.2 После аудита .NET entrypoint/fixtures выполнить разрешённые pure tests последовательно и необходимые format/TypeScript/harness/strict OpenSpec checks; перед полноценным Host/DB/Aspire оставить исполнение существующему CI и записать точные результаты в verification.
- [x] 5.3 Получить independent implementation self/review, исправить Critical/Important, затем только по publication authorization пройти exact-head ordinary CI + mandatory PR E2E и postmerge; сохранить SHA/run IDs, fixture scope и separate target/model limits.
- [x] 5.4 Подготовить runtime/evaluation handoff по [evaluation](evaluation.md) с immutable profile/privacy approval, synthetic smoke/RU-EN baseline/calibration/holdout и численными критериями перед запуском; проверить что enablement/automatic routing не объявлены принятыми из source tests.
