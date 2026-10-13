# Design

## Context

Мотивация — [proposal](proposal.md). Исходная точка `af725448e6632708d5129971f8570c1d047f4233` содержит merged PR45/M3 review; source Flights готов, model/provider/deployment gates сохраняются. [Audit](audit.md) фиксирует реальные seams и необходимые изменения. Это архитектурный planning-only срез, не начало реализации.

## Goals / Non-Goals

**Goals:** завершённый S1-процесс без модели; минимальная relational модель; доказуемые owner/operator guards, protected text, commit/concurrency и retention. Один канонический OpenSpec corpus.

**Non-Goals:** Laya runtime/SDK/placeholder ports, model routing, multichannel, attachments, email/SSE для Support, полнофункциональный helpdesk, assignment/SLA engine, новый брокер, event-sourced support stream, изменение финансовых операций или глобального JWT/Keycloak протокола.

## Decisions

### D1. S1 перед Laya; варианты

1. Только классификатор поверх нынешних заказов — отсутствует реальное обращение, очередь и ручная корректировка; нет основания измерять пользу.
2. Сразу helpdesk + Laya + автоматические назначения — смешивает новый процесс, privacy, инфраструктуру модели и evaluation; слишком широкий первый срез.
3. **Рекомендуется S1 manual workflow**, затем отдельные S2 classifier/shadow и S3 evaluation/limited routing. Это согласованный порядок roadmap. S1 не создаёт unused `IClassifier` или неработающий AI endpoint; [проверенное Laya research](laya-research.md) сохраняет источники для S2.

### D2. Новый модуль и storage

`modules/support/Travel.Modules.Support.{Core,Application,Infrastructure,Api}`. Core — SupportCase, CaseId/MessageId, CaseState, ManualCategory/Priority, Revision, actor/action decisions и ErrorOr factories. Application — синхронный workflow service с явными transaction/repository/protection/order-access ports. Infrastructure — EF `SupportDbContext`, protected-text implementation, retention worker. Api — DTO/guards/endpoints и `Api.Composition.SupportModule`.

EF schema `support`, свой migration history/configuration/initializer, в той же PostgreSQL. Tables: cases (owner, state, revision, optional order proof, manual triage, timestamps, retention deadline, protected title), immutable messages (case/message/actor/kind/written-at revision/protected body), action receipts/audit metadata. Один commit сохраняет mutation, revision, message и минимальный receipt. No Marten support stream, relational edits не импортируют Flights EF/Marten.

Реализация расширит exact project inventory20→24и ordered module-pair guards; только Support.Api.Composition получает ту же явную boundary exception, что enabled facades. Host добавляет Support.Api и вызывает его facade, сохраняя единственные global builders, security order и endpoint mapping. После утверждения решения обновляется ADR0023 и current runtime instructions; не ослабляется selector coverage.

### D3. Собственный заказ через published read-only seam

Минимальный leaf `Travel.IntegrationContracts.Flights` публикует owner-bound `IFlightsSupportOrderAccess` и typed verified/not-found/unavailable result. Не переносить доменные объекты, wire DTO, passenger data, payment refs или service inventory в Shared. Flights-owned реализация читает authoritative booking stream: только действительно owned Held-or-later order, включая terminal history; ownerless quote или непрошедшее creation admission не являются owned order.

Support передаёт owner, полученного от principal, и optional OrderId. Результат успеха — OrderId, verified owner, stream version, verifiedAt; это **проверка ссылки на момент создания**, не текущий финансовый статус. Port зарегистрирован Flights facade; Support.Infrastructure/Application не импортируют Flights internals. Последующий operator view не получает owner HTTP token и не обходит обычную Flights authorization.

Без OrderId запрос создаётся без Flights calls. Foreign/missing — единая404; unavailable —503. При ошибке сервер не теряет присланную заявку через тихое удаление ссылки: клиент сохраняет draft в памяти и может явно отправить новый case ID без связи. Idempotent existing receipt проверяется после собственной authorization и до повторного order lookup; уже committed link не перепроверяется при replay.

Эта возможность относится только к create responses с reserved fixed types `Support.OrderLinkNotFound`/`Support.OrderLinkUnavailable`, возвращённым до protection/store/commit при проверке новой ссылки. Source tests доказывают zero writes для этих branches. Только наблюдаемый typed pre-write rejection завершает этот rejected create; UI сохраняет draft и предлагает явный fresh CaseId/MessageId без OrderId. Generic404/503, receipt lookup и transport loss не доказывают отсутствие эффекта и не снимают unknown barrier. Нельзя применять reserved types к storage/commit errors.

### D4. API, actors и frontend identity

Customer — valid authenticated GUID subject, без backend-требования `flights:book` для обращения без заказа. Operator — validated actor плюс dedicated `support:operator` и `azp=travel-support-operator`. Scope/client identity в body не принимаются; сервер проверяет claims валидированного JWT на каждом read/write/replay до decryption. Role membership проверяет issuer при выдаче/refresh, не новый realm lookup на каждом Support запросе; немедленный отзыв уже выданного JWT не обещается. JWT issuer/audience/middleware и Flights book/review guards остаются прежними. Внешнее назначение scope не выполняется реализацией.

Текущий realm `travel-web` выдаёт `flights:book` как defaultClientScope всем входящим, а FlightsAuthService явно его запрашивает. Отдельный frontend validator один не ограничивает настоящий token. Выбран issuance путь: новый public PKCE-S256 client `travel-support-operator`, прежний issuer и `aud=travel-web`, `basic` subject mapping, без `flights:book` среди default/optional scopes. Optional `support:operator` scope имеет role-scope mapping только к новой non-default operator role; frontend запрашивает `openid support:operator`. По [официальному Keycloak scope contract](https://www.keycloak.org/docs/26.5.7/server_admin/index.html), отсутствие membership исключает scope из token. Scope не hardcoded для всех пользователей; composite/default roles не должны включать operator role.

Альтернатива — role-gate существующий default booking scope; она меняет onboarding/current Flights issuance и отклонена для S1. Обычный customer flow продолжает использовать неизменённый `travel-web`; Support permission не меняет его права. Человек с отдельно полученными customer правами может действовать как customer, но operator token и operator API не получают эти права автоматически.

Для frontend нужен narrow общий Keycloak factory/subject-audience validation seam с отдельными customer/operator session contexts; Flights wrapper сохраняет прежнюю проверку scope. `travel-support-operator` получает отдельный validated runtime config/allowlisted callback, не переиспользует flights token или неподтверждённый clientId. Identity generation включает client/session mode, поэтому одинаковый GUID в другом контексте не принимает старую completion. Используется существующий SDK, без нового auth framework.

Инициализируется только явно выбранный route/session context; operator navigation не запускает eager customer sign-in/token acquisition. Смена client mode очищает private view и отменяет его callbacks, но не является финансовой reconciliation или автоматическим сбросом existing unknown booking barrier для того же owner. Проверка same-owner/new-client mode обязана сохранять этот барьер.

Generation также включает effective Support capabilities, а не только GUID/client mode. При потере необходимых claims на refresh либо401/403из актуального Support request приватные title/body/drafts очищаются, generation повышается и более ранние completions больше не принимаются — даже при том же subject/client. Permission failure от уже устаревшей generation не очищает новую сессию. Неавторизованный экран не продолжает показывать ранее загруженную переписку. Minimal unknown action IDs/receipts metadata остаются отдельным owner-bound барьером; потеря доступа не доказывает отсутствие эффекта старого write и не разрешает его повтор.

Source realm fixture/client-scope/role mapping changes входят в согласуемый S1 scope. Existing disposable CI должен проверить actual authorization-code+PKCE issuance: authorized operator scope present/book absent, non-operator cannot obtain operator scope даже при explicit request, invalid audience/azp rejected, current customer booking token unchanged. Signed/fake JWT tests одни не доказывают issuance. Actual target client/role grants/redirect URIs provisioning остаётся отдельным разрешением.

| Route (proposed) | Behavior |
|---|---|
| `PUT /api/support/cases/{caseId}` | Create-only, first MessageId/title/body/optional OrderId; client UUIDv4 IDs known before send;201initial/200matching receipt/409conflict. |
| `GET /api/support/cases` / `/{caseId}` / `/{caseId}/messages` | Owner-filtered list/detail/conversation;404foreign/missing; pages≤20, stable metadata cursor. |
| `PUT /api/support/cases/{caseId}/messages/{messageId}` | Customer Reply / ReplyAndReopen, expectedRevision. |
| `GET /api/support/operator/cases` / `/{caseId}` / `/{caseId}/messages` | Dedicated operator permission; filters state/category/priority, no owner IDs from caller needed. |
| `PUT /api/support/operator/cases/{caseId}/messages/{messageId}` | Operator reply, expectedRevision. |
| `PUT /api/support/operator/cases/{caseId}/actions/{actionId}` | Resolve or SetTriage, exact typed kind/fields/expectedRevision. |
| `GET /api/support/cases/{caseId}/actions/{actionId}` | Owner + original customer actor/kind bound retained receipt lookup; no pagination or text decryption. |
| `GET /api/support/operator/cases/{caseId}/actions/{actionId}` | Current operator scope/azp + original operator actor/kind bound receipt lookup; another operator's receipt is concealed. |

No-store header policy применяется к Support resource prefixes до authentication, чтобы включать401/403; bounded body guard выполняется после authorization внутри Host-selected module phase. Endpoint calls синхронный Application service; plain text не проходит Wolverine durable commands/outbox/idempotency response cache. Fixed errors400/401/403/404/409/413/503 не включают input. Runtime OpenAPI и catalog/contract fixtures отражают actual routes.

UI: `/support`, `/support/cases/:id`, `/support/operator`, `/support/operator/cases/:id`; existing order page получает простую owner create link. OrderId/caseId в resource route разрешены; text/token не попадают в URL. Queue/detail errors видимы; никаких fake empty states при HTTP failures. Keyboard/mobile actions и clear operator provenance обязательны. Сессия и private text только в RAM, без Travel browser storage.

Все `/support/**` routes явно `RenderMode.Client` в `app.routes.server.ts` перед Prerender wildcard, включая root `/support`; private session/content не пререндерятся и не сериализуются в SSR/transfer state. Customer/operator callback/return paths отдельно allowlisted по своим route prefixes/resource GUIDs; external, protocol-relative и encoded foreign returns отвергаются. Existing Flights whitelist/SDK protocol state handling сохраняется.

Pagination uses keyset: case lists ordered by immutable CreatedAt/CaseId within selected filters; conversation ordered by retained WrittenAtRevision/MessageId. Cursor contains only bounded ordering/filter metadata and is validated after actor guards. Owner is never taken from a cursor. Manual triage/state changes are reflected on an explicit refreshed generation; continuation does not promise one transaction snapshot across requests. Do not page on mutable LastActivityAt or use an offset that repeats/skips messages after new replies.

### D5. Lifecycle, concurrency и uncertainty

New→Open. OperatorReply: Open/WaitingForCustomer→WaitingForCustomer. CustomerReply: Open/WaitingForCustomer→Open. Resolve: Open/WaitingForCustomer→Resolved. Customer ReplyAndReopen: Resolved-before-expiry→Open плюс сообщение в одном commit; обычный reply в Resolved —409. Reopen исключён после retention deadline. Сообщения не редактируются; исправление — новое сообщение.

Create ID и вложенные action/message IDs уникальны в своей области. Owner/operator authorization предшествует receipt lookup. Matching replay возвращает прежний минимальный receipt и не повышает revision; changed text/link/kind/actor/expectedRevision —409. Для проверки exact content protected original читается только после guards; нет plaintext fingerprint/cache. Receipt содержит applied revision/state/IDs, но не текст. Старый receipt не заменяет UI snapshot с большей revision.

Для receipt namespace creation использует first MessageId, reply — свой MessageId, Resolve/SetTriage — ActionId. Все эти mutation IDs уникальны в пределах CaseId через один receipt unique key `(CaseId,ActionId)`: один ID нельзя переиспользовать для другого kind/actor. Это сохраняет однозначный metadata GET; отдельный operation registry не вводится. Case/first message/receipt creation и каждый последующий message/action/receipt сохраняются в одном transaction.

После lost response UI читает receipt именно по известным CaseId/ActionId. DTO содержит CaseId/ActionId/kind/AppliedRevision/AppliedState/AppliedAt и MessageId при наличии; server фильтрует case ownership, original actor и допустимый caller kind до disclosure. Текст/другие request fields в DTO не входят. Positive receipt подтверждает сохранённое действие; свежий case GET нужен отдельно для текущего статуса. Receipt может подтвердить старое действие с меньшей revision, сохранив более новый case snapshot.

Receipt404, отсутствие сообщения на одной странице, отсутствие receipt после expiry/purge и401/403/503не доказывают NoEffect: первый запрос мог ещё выполняться или история уже недоступна. Это остаётся explicit unknown/unavailable с запретом automatic resubmit. Reload делает authorized reads по resource IDs/истории, не reconstruct/resend body. Нельзя выдавать чтение snapshot за подтверждение того, что конкретный pending write завершён.

Update uses atomic expectedRevision condition, mutation/message/receipt в одном transaction. При конфликте UI refetch + явный новый выбор; при transport uncertainty не resubmit. Support uncertainty не объявляется financial exactly-once и никогда не освобождает Flights uncertainty. Idempotency ограничена lifetime retained case/receipts; после физического удаления история не восстанавливается и UI не повторяет старый запрос.

### D6. Manual triage

New: Unclassified / Normal / Default. Operator categories: Payment, Ticketing, Cancellation, Refund, FareConditions, Ancillaries, Technical, Other. Priority: Normal/High. Source всегда Operator после ручной смены; audit сохраняет old/new enum values, actor/time/action ID. Один общий operator queue; отдельные departments/assignment не вводятся до фактического consumer.

S1 не выводит срочность из client prose и не запрашивает финансовые факты. В S2 явные правила по подтверждённому состоянию/будущим departure instants проектируются отдельно; текст остаётся claim клиента. Этот план не задаёт фиктивные universal confidence/urgency thresholds.

### D7. Free text и privacy

Title≤120Unicode scalars; each body≤4000scalars и16KiBUTF-8; request≤32KiB. Invalid UTF/JSON, unknown/duplicate fields, invalid UUID/revision/enums, empty/whitespace text отвергаются fixed errors. Bounded UTF-8 RAM reader до binder diagnostics. Output uses interpolation/plain text, без html renderer, active link parser, prompt execution или submitted-error echo.

Support-owned certificate-protected Data Protection ring по проверенному Flights pattern; отдельные ApplicationName/purposes и configuration. Не импортировать private Flights protector и не использовать его ring по скрытой договорённости. Source пока не делает common cryptography framework; duplicate small module-specific boundary предпочтительнее несогласованного shared key lifecycle.

Protected title/body binds format, case owner, case ID, MessageId or title marker и **retained written-at revision**, не mutable current case revision. Plaintext не сохраняется в EF/history/outbox/cache/logs. Authorized list decrypts only≤20titles; message pages≤20bodies. Missing keys/new protection unavailable→503 до write; unreadable historical data→503, не empty list/placeholder plaintext. Metadata cleanup не требует decrypt. No automatic empty-ring bootstrap; actual certificates/key provisioning и recovery rehearsal — отдельно разрешаемые операции.

Fake-protector tests подтверждают только routing/guards/purpose arguments. Disposable CI отдельно использует настоящий Support Data Protection provider и свои временные certificates/ring: ciphertext без plaintext sentinels, wrong owner/case/message/written-at revision/tampering rejected, чтение после restart и certificate rotation с retained read key, отказ без нужного ключа, ordinary write не bootstrap empty ring. Реальные persisted protected title/body перечитываются из Support EF в этом CI. Генерация этих disposable test keys не выполняется локально и не является target provisioning.

### D8. Retention

Предлагаемая продуктовая политика для review: resolved content90days с ResolutionAt; open/waiting cases сохраняются до явного resolution, без автоматического закрытия. Reopen до deadline атомарно сбрасывает purge deadline. На deadline reads/writes concealed404, независимо от наличия физической строки. До deadline operator/customer lifecycle остаётся обычным.

Support-owned hourly retention worker starts only after own schema initialization; injectable TimeProvider, batch≤50cases, pass≤1000cases. Conditional deletion requires still-Resolved and exact observed revision/deadline; transaction removes messages/receipts/free-text/links и case together. Competing reopen wins only before expiry. Multiple workers/restart repeat safely без distributed lock или нового scheduler. Failures остаются явными diagnostics/readiness/metrics; expiry-on-read не считается физическим deletion proof. Cleanup never traverses Flights history; non-support backup retention и actual production purge delivery не заявляются.

### D9. Evidence and acceptance

Pure Core/validation/protection-boundary/clock tests локально только после startup/assembly-fixture audit; .NET Windows последовательно. Real EF transaction/unique IDs/races/purge/restart/schema/readiness — disposable existing CI. New Support Unit/Integration projects need explicit test inventory/ordinary CI lane extension; это обязательная часть будущего source approval, без изменения deployment or paid-eval policy. Не оставлять новые tests вне выполняемого CI.

HTTP/JWT tests prove owner/operator scope/foreign concealment/32KiB/no-store/actual DTOs and OpenAPI. Shared TS strict decoders preserve errors and revisions. Browser fixtures prove separate client/operator workflow, keyboard/mobile, reply/resolution race, lost responses and epoch guards with zero external/model/financial calls. Existing actual PR E2E remains mandatory; source acceptance отдельно от keys/schema/operator provisioning/rollout/Laya.

## Risks / Trade-offs

- Новый module увеличивает inventory и host integration surface → exact guards+all ordered module pairs, approved facade only.
- Free text может содержать PII → protected purpose, RAM-only drafts, bounded no-echo reader, authorization before decrypt, retention; нельзя считать простую редактировку текста гарантией удаления PII.
- Operator reply может конфликтовать с resolution → atomic revision and receipt, explicit refetch; не automatic retry.
- Separate key lifecycle требует эксплуатации → fail-closed source and CI fixtures; actual provisioning/recovery отдельный gate.
- Manual priority не обеспечивает автоматическое обнаружение срочных случаев → честный S1 scope, операторский triage; S2/S3 later measured rules, не обещание SLA.

## Migration Plan

После согласования written plan: source implementation отдельно; generation/review Support source migration отдельно разрешаются, SQL Up/Down inspected offline, DB не применяется локально. Required future scope also includes new auth seam/policy, architecture/CI test inventory extensions and key-lifecycle source. No runtime grants/provisioning/deploy inferred.

CI disposable initialization applies only its own fixture schema. Production follows ADR0007 validation-only; checked migration must be applied externally before enabling Support facade/UI in the actual target. Keys/operator claims precede runtime acceptance. Rollback to pre-Support revision stops Support routes/worker and retains schema/ciphertext for reviewed recovery; destructive Down deletes support history and requires explicit approval. Existing Flights source/storage remains compatible.

Планирование завершается written review. Source S1, затем отдельные S2 и S3; ни Laya package/model download, ни реальный inference данным планом не запускаются.
