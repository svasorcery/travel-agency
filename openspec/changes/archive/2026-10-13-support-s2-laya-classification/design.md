# Design

## Context

Мотивация — [proposal](proposal.md); текущие факты и официальные источники — [audit](audit.md). Base новой ветки: свежий `origin/dev=7fd26fecfd996751c22b7b370a4fcbf6d085b9f6` после PR48; ancestry исходного163e3d9и принятого S1 проверена.

S1 хранит защищённый текст, ручные Category/Priority и Operator receipts. Существующий Flights leaf доказывает только владельца/версию: состояния поездки и времени до вылета в нём нет. Очередь — общий список с фильтрами, не распределение сотрудников. Новый [контракт](specs/support-classification-suggestions/spec.md) добавляет отдельную рекомендацию; обычные операции S1 продолжают работать без Laya.

## Goals / Non-Goals

**Goals:** один синхронный явный operator request; минимальный реальный HTTP ACL; ручное принятие/исправление; наблюдаемая версия/достоверность входа; независимая детерминированная срочность; ограниченный compute; совместимость S1.

**Non-Goals:** automatic routing, agent tools/runtime, Anthropic, model training, новые таблицы/миграции, durable inference jobs, сотрудник-assignment, автоматические ответы, изменение заказа или денег. Реализация, download/install/start, ключи/issuer grants, target DB и включение модели не разрешены этим планом.

## Decisions

### D1. Support-owned прямой HTTP ACL

Выбран отдельный self-hosted Laya service внутри доверенного Support perimeter. Support Application задаёт `ISupportClassificationProvider` и результат без wire DTO; `LayaSupportClassificationProvider` в Support.Infrastructure реализует реальный HTTP протокол. Core владеет допустимыми категориями/queue mapping и urgency policy. Api facade владеет регистрацией/options/operator endpoint, Host — существующей общей security/composition.

Варианты: (a) прямой ACL — один нужный retained consumer, короткий путь protected text; (b) прокси через Travel.AI — добавляет process contract/ещё один PII hop без существующего Laya consumer; (c) embedded Python/SDK или agent framework — добавляет runtime/lifecycle без необходимости. Выбрано(a). Travel.AI и его Anthropic cost ledger не подходят для учёта self-hosted CPU; их source не меняется.

### D2. Приватный запрос и согласованное чтение

Предложенный API: `POST /api/support/operator/cases/{caseId}/classification-suggestion`, body ровно `{ requestId: UUIDv4, expectedRevision: positiveInt32 }`. Body≤1KiB, strict UTF-8/object/duplicate+unknown-field rejection. Никаких browser-supplied text, owner, model, language, options, URL или thresholds. Новую POST форму покрывает Support body guard; все ответы остаются no-store с фиксированным ProblemDetails.

Существующие policy `support:operator` + `azp=travel-support-operator` и nonempty validated subject проверяются до processing; новый shared Application access helper переиспользует S1 guard/expiry semantics. Load case проверяет R, state Open/WaitingForCustomer, expiry; Disabled возвращается до decrypt/order/model. Enabled запрос читает максимум5immutable messages existing store limit5 (четыре+overflow sentinel), проверяет firstMessageId, уникальность, порядок и writtenRevision≤R. Пятый message означает manual fallback без decrypt. Для допустимой полной переписки decrypt сохраняет owner/case/message/writtenRevision binding; после чтения и перед inference case снова проверяется на ту же R/state/expiry.

После внешней работы и перед выдачей повторно Load case: R изменилась→409review required, resolved→409, отсутствует/expired→concealed404, store/protection failure→fixed503. Существующие ручные операции не ждут inference и могут выиграть гонку. Нет блокировки DB на время HTTP.

Response200: `status=Disabled|ManualRequired|Candidate`, caseId, requestId, capturedRevision, generatedAt, expiresAt, fixed reason; nullable category/queue and отдельная urgency recommendation. Candidate означает пригодную категорию, а не правильный ответ. Disabled/manual не означают сохранённое назначение. Admission429 и request400/401/403/404/409/503 обрабатываются явно.

### D3. Input и граница персональных данных

Одна versioned схема `support-category-v1`: title + **все**1–4messages в chronological order, с меткой Customer/Operator без IDs/timestamps. Не отправляются case/owner/order/message IDs, order facts, passenger snapshots, payment references, токены и credentials. Общий serialized state≤8000Unicode scalars и24KiBUTF-8; HTTP request≤32KiB. Любой overflow, неполнота, malformed row/text или decrypt failure — manual outcome, без усечения и частичного запроса.

Произвольный Support text может содержать PII: исключение структурированных полей не является анонимизацией. Полный текст допускается только после отдельного подтверждения self-hosted privacy perimeter; до этого feature off и тесты используют демофикстуры. В периметр входят только Host и выделенный Laya service; TLS/private authenticated origin, отсутствие body/access-log capture, prompt/response tracing, hooks и telemetry exporters с содержимым, ограниченный доступ compute. Сервер не должен пересылать input наружу. Отдельное разрешение требуется для работы с настоящим потоком.

URL/credentials задаются server-side, secrets вне Git. BaseUri — заранее утверждённый HTTPS origin без userinfo/query/fragment; redirects выключены, certificate validation штатная. Loopback HTTP допустим только в disposable protocol tests. Browser с Laya напрямую не соединяется. Текст сериализуется как data; фиксированные instructions/criteria не строятся из его указаний.

### D4. Реальный протокол и версии

Read-only сверены Laya release0.4.6, sourceSHA `c73908d96ff73c0062c340a11bf7c54ec7d8bc4d`. План использует full protocol (`LAYA_JEV_STRICT=0`), `POST /v1/systemone`, одну named choice question `support_category`, фиксированный `model=multilingual`, без auto routing, batch, tournament или supplied language hints. Request question содержит type=choice, instructions и criteria map ровно Payment/Ticketing/Cancellation/Refund/FareConditions/Ancillaries/Technical/Other. Unclassified — ручной fallback, не forced model option. Краткие определения: проблема оплаты; получение билета; отмена; клиентский возврат; ограничения тарифа; багаж/места; ошибка приложения; прочее. Текст не означает признание факта из названия категории.

Router штатно читает bundled multilingual checkpoint: ожидаемый `routing.repo=convaiinnovations/laya/multilingual`; bundle revision `55cf4c4ebb4ebe31b2550e8bdf3bd21b99753851` подтверждён в pinned upstream `laya/revisions.py`. Не подставлять standalone multilingual SHA в bundle. Request `max_len=512, head_max_len=512` — стартовый ограниченный профиль; long text может не поместиться в токены и уйдёт manual.

Serving profile включает serverSHA/release, approved immutable image/config identity, bundleSHA, model alias, question+input version, budgets, два guard thresholds и device/precision expectation. ProfileId — hash canonical nonsecret configuration, не PII. `Support:Classification:Enabled=false` по умолчанию; enabled без полного profile/secret/URL не начинает HTTP и не ломает S1. Никакого sample URL/key/автоматического model download из startup.

План будущего размещения: dedicated immutable deployment, explicit bundle revision, preload multilingual, offline runtime после отдельно разрешённой подготовки, Bearer auth и full health details. До/после одного inference authenticated health проверяет resident multilingual revision, checkpoint_devices и cpu_fallbacks.multilingual.count. Для pinned version требуется конечный неотрицательный integer counter; missing/malformed/decrease означает identity evidence unavailable, рост при неразрешённом CPU fallback → manual. Scoped CUDA→CPU→CUDA fallback может оставить одинаковые device до/после, поэтому device alone недостаточен. Concurrent fallback также консервативно отклоняет кандидат; счётчик не доказывает, чей именно request выполнялся на CPU. Precision не публикуется в health: её подтверждает approved immutable deployment configuration, а не несуществующее wire поле. Preload не является запретом иных Router models: клиент всегда принудительно задаёт multilingual. Health без credentials/loaded revisions не доказывает готовность. Protocol response не содержит per-response checkpoint SHA: provenance опирается на доверенный immutable deployment + authenticated observations, не на криптографическую подпись ответа. Identity drift снимает enablement до нового receipt.

### D5. Валидация кандидата

Infrastructure читает response≤64KiB, bounded JSON depth и rejects duplicate fields. Требуются одна заданная answer, matching discriminator/label, ровно восемь probability keys, конечные значения[0,1], сумма1±0.001 (допуск для округлённого upstream JSON), choice является argmax (ties reject), answer_confidence согласован с выбранной probability±0.001; confidence/answer_confidence — конечные[0,1]. Неизвестные структуры pinned required protocol — manual, raw response/errors не покидают ACL.

Обязательны корректные usage и routing: нет lost state tokens, truncated questions или collapsed options; отсутствующая usage также manual. Strict Jev projection без нужных данных отвергается. В модели нет возвращаемого произвольного объяснения: продукт использует свои category/queue labels и fixed reasons.

Примерный первый экспериментальный профиль: request `min_confidence=0.75` проверяет `answer_confidence=max(p)`; дополнительно клиент требует независимый entropy guard `confidence≥0.60`. Laya gate должен быть passed с echo threshold0.75; `low_confidence` допустим absent/false, но true отвергается. Passed ответы upstream обычно удаляют этот flag. Missing `answer_confidence` клиент отвергает независимо от fallback внутри upstream. Это **не откалиброванные** пороги и не обещание accuracy. Missing/unevaluated/abstained/mismatched gates не превращаются в pass. `confidence`, max-probability и compatibility `x_jev_confidence` не смешиваются. В S3 thresholds подбираются на calibration split, потом фиксируются до holdout. До отдельного runtime/evaluation разрешения этот профиль используется только для protocol fixtures.

Queue key детерминированно равен категории S1; UI переводит его в существующий queue category filter. Не добавляются отдельный queueId, таблица маршрутов или сотрудник assignment.

### D6. Независимая явная срочность

Добавить отдельный опубликованный `IFlightsSupportTriageContext` в существующий BCL/ErrorOr leaf assembly; прежний ownership method не меняется. Один owner+order-bound read возвращает source streamVersion/observedAt, `journey=Active|Closed|Unknown` и nullable nextScheduledDepartureUtc. Это минимальное schedule/context metadata; нет суммы, пассажиров, билетных/provider identifiers, customer-refund/bank состояния или commands.

Flights Application читает тот же authoritative BookingAggregate stream. Active = Held с известным ещё действующим HeldUntil (aggregate ExpiresAt), Confirmed/Ticketed, только при отсутствии lifecycle uncertainty. HasSupplierCancellationReportBarrier, HasUnresolvedCreation, HasConfirmationBarrier или nonterminal CurrentCancellation означают Unknown; missing/expired HeldUntil также Unknown. Подтверждённая ObservedSupplierCancellation либо Cancelled/Refunded означает Closed как состояние поездки, **не bank credit**; при конфликтующих/неподтверждённых lifecycle facts выбрать Unknown. None/OfferQuoted, inconsistent owner, invalid itinerary или missing facts не дают подтверждённую Active/date. Сначала проверяется owner/consistency, затем времена всех segments по UTC DateTimeOffset: выбирается ближайший будущий, включая следующий segment/возвратный/open-jaw leg. Отсутствующая/некорректная полная itinerary не заменяется датой из customer text, quote cache или EF projection. Никаких passenger decrypt/events/provider calls.

Support Application получает свежий triage context после model попытки, один раз внутри общего deadline; при Laya timeout может оставаться только manual fallback. Core receives mapped facts, не Flights types. При decision age>30секунд, observation>server-now, nonpositive version или чужом binding контекст Unknown. Rule `support-urgency-v1`: Active и `0 < departure-now ≤24h` → предложить High независимо от confidence/category; уже High сохраняется. Active дальше24h/Closed — KeepCurrent. Active без future time/unlinked/unavailable — Unknown/KeepCurrent, не Normal. Пример24h — предлагаемый business threshold для ревью.

Проверка Core выполняется после финального асинхронного перечитывания обращения; now для freshness/departure и generatedAt берётся одним наблюдением TimeProvider. Если до этого момента вылет уже наступил или факты устарели, urgency становится Unknown без потери независимо принятой категории. Disabled относится только к начальному пути без private processing; отзыв профиля после начала обработки даёт ManualRequired/IdentityMismatch.

UI показывает «Рекомендуем высокий приоритет: ближайший запланированный вылет …; данные на …». Это сохранённое расписание заказа на момент чтения, не обещание фактического вылета/нет задержки. Понижение High модель/правило не предлагает. Даже при ManualRequired категории отдельный проверенный urgency advice можно показать; сохранение остаётся operator decision.

### D7. Admission и тайминг

Общий enabled request deadline10секунд через TimeProvider/linked cancellation, включая private reads, health, one inference и Flights context; response TTL≤2минут. Model typed HttpClient без retry/resilience replay, redirects и body logging; timeout/401/422/503/oversize→fixed manual reason. Health также не повторяется автоматически. CT/timeout не означает остановку удалённого compute.

Малый module-owned bounded admission component: максимум2active запросов на Host process,3attempts/min/operator и12/min/process, очередь ожидания0. Эти proposed limits подтверждаются runtime измерением позже; не multi-node global quota и не SLA. Новый явный request потребляет budget до HTTP. Metadata-only in-flight same operator/case/revision защита не допускает параллельный повтор, не сохраняет input/output. Любые admission bookkeeping keys имеют bounded lifetime/capacity; fixed max1000operator partitions, full→reject. После timeout UI не повторяет; явная новая попытка после user review расходует бюджет заново и предупреждает, что предыдущая могла выполниться. Unknown model outcome не блокирует ручной S1, но **не снимает** его write barriers.

Observability: fixed outcome/category/profile/latency/request-byte/token counters и ожидаемое device, без caseId/userId labels/text/responses. Token counts и inference milliseconds — не currency cost; реальный compute/time/потребление host оценивается отдельно. Нет Anthropic ledger/no fictitious zero-cost claim.

### D8. UI и ручная фиксация

Отдельная «Предложить категорию» кнопка только на выбранной operator case в допустимом state/current R, вне pending/unknown S1 writes. UI хранит request generation/case/R/operator epoch и private suggestion только в памяти; navigation/logout/401/403/permission-client change/newer revision/newer request/TTL очищают её. Автозапроса на GET/reload нет. Простое refresh даже при том же R очищает старую suggestion; поздний callback сравнивает все guards. Пока значения лишь подставлены из suggestion, UI держит их local binding: expiry/invalidation сбрасывает их к текущим saved values; явно отредактированный manual draft требует нового operator review и не показывается как действующая рекомендация.

«Подставить рекомендацию» только заполняет existing manual form; «Сохранить» — отдельный existing SetTriage с fresh actionId/current expectedRevision. Priority по умолчанию остаётся текущим; High proposal отдельно выбирается оператором. При ручной correction виден выбранный operator вариант, не model certainty. Стандартные actor-bound receipts/CAS/unknown barriers работают без новых action kinds или source AI. Нет server-side accept-suggestion token/table: авторизованный оператор может независимо выбрать любую категорию, а suggestion expiry контролирует UI; endpoint сохранения остаётся manual action, не сертификатом модели.

## Risks / Trade-offs

- PII в arbitrary text → не обещаем анонимизацию; только отдельно принятый perimeter, off до approval, fixtures для source.
- Короткое окно input/token budgets часто отправляет длинные cases в manual → явно измерить coverage/latency в S3, не скрывать truncation.
- Health и route metadata не дают per-response SHA attestation → immutable dedicated profile/health evidence; drift выключает feature.
- Fixed24h/thresholds ещё не приняты и не откалиброваны → review concrete policy, затем отдельная calibration/holdout; отсутствие автоприменения.
- S2 не хранит dataset/correction pairs → полноценная S3 оценка/сбор размеченных примеров отдельно; агрегаты не доказывают accuracy/time savings.
- Source schedule может отстать от реального рейса → as-of scheduled context, Unknown при неполноте; manual override.
- Deadline может оставить compute running → без replay, bounded admission; real resource saturation отдельный runtime gate.

## Migration Plan

После письменного approval отдельный apply реализует source и tests; schema/data migrations не нужны. Publication/ordinary exact-head CI/PR E2E и postmerge подтверждаются отдельно. Target Laya image/weights/network/secrets/rights и Support readiness устанавливаются только по отдельному runtime approval; проверяются pinned identity и synthetic smoke, затем отдельный approved dataset эксперимент. До этого enabled=false.

Rollback: выключить Classification Enabled; штатный S1 остаётся работоспособным и manual selections сохраняются. Не менять их на Unclassified/Normal. Model deployment/cache cleanup имеет отдельный scope, не является rollback Support данных.

## Open Questions

Deferrable operational inputs: approved hostname/image digest, CPU capacity и measured latency, затем labels/quality targets и calibration guards в S3. Они не блокируют source-ready план с off default и protocol fixtures; отсутствие этих receipts блокирует target enablement. Никаких placeholders в runtime defaults; неизвестное остаётся Disabled.