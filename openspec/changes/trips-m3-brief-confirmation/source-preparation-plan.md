# Trips M3 evaluator: source/test preparation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` for native execution, or `superpowers:subagent-driven-development` only if the user chooses delegation. Steps use checkbox syntax. This document authorizes no implementation or execution by itself.

**Goal:** подготовить проверяемый standalone evaluator и устранить выявленные пробелы, доказав поведение только fake/offline тестами; реальные модели и target M3 не запускать.

**Architecture:** сохранить одну production extraction/validation pipeline и существующие evaluation helpers. Добавить узкий .NET console consumer с default `preflight`, строгую границу будущего разрешения, durable controlled evidence и монотонное расписание. Не создавать AI core, provider registry, agent framework, второй планировщик или новые HTTP endpoints.

**Tech Stack:** .NET10, текущие Anthropic12.20.0 / Microsoft.Extensions.AI10.5.2, существующий EF ledger; xUnit/Shouldly, fake clock/HTTP и уже закрепленный Testcontainers.PostgreSql4.11.0. Новых версий зависимостей не выбирать.

**Spec:** [варианты и readiness gaps](model-evaluation-options.md), [неизменный corpus/protocol](evaluation-plan.md), [current capability contract](../../specs/trip-brief-extraction/spec.md). Этот план задает **пять source/test prerequisites задачи6.3**, а не пять новых продуктовых milestones. Основной исторический счетчик остается22/23; выполнение P1–P5 не завершает6.3.

## Global Constraints

- Source/test P1–P5 одобрены пользователем2026-10-28 («го как рекомендуешь»): native исполнение и независимое итоговое review. Model/processor selection, цены, расходы, private-text processing и actual execution этим approval не подразумеваются.
- Source/test реализацию начинать от действительно свежего `origin/dev` в чистой отдельной `codex/` ветке с записью SHA; сохранять и переносить одобренные локальные planning artifacts, не переключать грязный worktree и не переписывать завершенные ветки. База этого планирования —9ff60e42bb553214a22ed71fb4fa44b9d846c3d2, не предписание использовать ее вместо будущего fresh dev.
- Не менять evaluation-plan/corpus/oracles/prompt/schema/normalizer,13requirements/52scenarios, квалификационные22/24 и11/12, safety thresholds, M1/CAS/catalogue1, городские пакеты, saved demo и human materials.
- Qualifications:48=D24+H24, обе части открыты; starts не ранее0/65/130/195/260/325min, ≥11s между фактическими provider attempts,6h overall. Поздний sealed24/3h остается отдельным gate; не создавать/открывать его здесь.
- Input≤1000scalars/4096UTF8 bytes, complete envelope≤4096tokens, output≤1024tokens/16KiB; inference3500ms/AI4500ms/component5000ms. Request/hour ceilings≤USD0.03/0.30; выбранные будущие caps должны быть ниже них и полностью совпадать между manifest/binding/options.
- No real calls, API/credentials/billing inspection, automatic retry/repair/resume/fallback, checked-in feature enablement, target/schema/key work. Source Docker tests — только NEW owned disposable state после соответствующего source/test approval; никакого подключения к retained demo/target.
- Core/Host/Web/NATS composition и публичные contracts не меняются. Commit/push/PR/merge, paid evaluation, deployment и cleanup вне owned disposable state требуют своих разрешений.

## Review Focus

1. Malformed/forged/stale manifest, dirty source или иной binary/prompt — отказ **до** чтения key, создания SDK/DB/container (P1/P4).
2. Process pause/UTC jump и неодинаковое время ledger admission — ≥11s между реальными SDK attempts, без catch-up, monotonic6h stop (P2/P3).
3. Crash/write failure/symlink/повтор authorization — ни повторного uncertain call, ни потери прежних результатов, ни выхода за approved receipt root (P2).
4. Задержавшаяся нормализация — duration включает весь component и повторный deadline check; receipt I/O не маскируется как inference (P3).
5. Rejected H09 proposal или normalizer-added issue — отдельно от normalized correctness; не превращать `UnsafeCases=0` в «модель не галлюцинирует» (P3).

## File map и общие интерфейсы

Ниже точные path prefixes: **E**=`tests/Travel.Tests.AiEvals/TripsBriefExtraction`, **C**=`tests/Travel.Tests.Contract/Trips`, **A**=`tests/Travel.AI.Tests/TripsBriefExtraction`, **T**=`tools/trips/Travel.Trips.Evaluation`. Все предлагаемые новые файлы перечислены в задачах; не переносить helpers в новый общий framework.

- Helpers E остаются canonical non-xUnit code. Contract project продолжает подключать их через `Compile Link`; console тоже подключает те же файлы, без копий и без ссылки на весь paid test assembly.
- Только T создает реальную composition. Contract tests не получают ProjectReference на T, чтобы не дублировать linked E types; AI tests могут ссылаться на T для NEW owned-ledger integration proof.
- Все fake helpers и clocks общие только внутри C/тестов. Реальный CLI жестко использует `TimeProvider.System`, approved immutable packet и audited factory; CLI не принимает произвольный текст, URL, fake clock или произвольный provider.

Продуцируемые имена и signatures (namespace helpers остается `Travel.Tests.AiEvals.TripsBriefExtraction`):

```csharp
TripsBriefEvaluationPreflight TripsBriefEvaluationAuthorization.Check(
    TripsBriefEvaluationManifest manifest, TripsBriefEvaluationArtifacts observed);
Task TripsBriefEvaluationFileJournal.RecordAttemptAsync(TripsBriefEvalAttempt attempt, CancellationToken ct);
Task TripsBriefEvaluationFileJournal.RecordResultAsync(TripsBriefEvalCaseReceipt result, CancellationToken ct);
Task TripsBriefEvaluationFileJournal.CompleteAsync(TripsBriefEvaluationReceipt receipt, CancellationToken ct);
void TripsBriefEvaluationSchedule.Begin(TimeSpan runLimit);
TimeSpan TripsBriefEvaluationSchedule.DelayBeforeCase(int originalOrdinal);
void TripsBriefEvaluationSchedule.RecordProviderStart();
Task<TripsBriefEvalCaseReceipt> TripsBriefEvaluationCaseProcessor.ProcessAsync(
    TripsBriefEvalCase @case, TripBriefExtractionInput input,
    ITripsBriefEvaluationPipeline pipeline, CancellationToken ct);
Task<int> TripsBriefEvaluationCommand.RunAsync(
    string[] args, ITripsBriefEvaluationEnvironment environment, TextWriter output, CancellationToken ct);
```

`ITripsBriefEvaluationJournal` содержит три journal methods выше; memory journal разрешен явно только mechanical tests и помечает receipt `mechanical-memory-only`. В `RunAuthorizedAsync(packetPath,binding,pipeline,time,journal,schedule,wait?,stopRequested?,ct)` journal и общий schedule становятся обязательными. Schedule создается один раз для runner и observed IChatClient, Begin вызывается runner после preflight/resource setup; re-Begin запрещен. Новые необязательные поля в существующих receipts добавляются с defaults для исторических mechanical fixtures; старые данные/denominators не переименовывать.

## P1. Зафиксировать проверяемую границу разрешения и source identity

**Files:** create `E/TripsBriefEvaluationAuthorization.cs`, `C/TripsBriefEvaluationAuthorizationTests.cs`, `C/TripsBriefEvaluationTestFixture.cs`; modify `E/TripsBriefEvaluationRunner.cs` (binding only), `apps/Travel.AI/TripsBriefExtraction/TripsBriefExtractor.cs` (только pure identity helper), `tests/Travel.Tests.Contract/Travel.Tests.Contract.csproj` (Compile Links).

**Consumes:** существующие Binding/Packet/Options, фиксированные hashes и limits из spec. **Produces:** Manifest/Artifacts/Preflight types и `Authorization.Check`; без SDK, secret resolver, EF или process/container creation. Manifest v1: ProtocolVersion, Binding, ExpectedArtifacts, публичный OptionsSnapshot (без key), Endpoint, FictionalOnly и AuthorizationExpiresAtUtc; AuthorityReference — существующий Binding.AuthorizationId. OptionsSnapshot содержит Model/PricingModel/ProcessorId, rates/caps и policy identity, которые сверяются с Binding. Preflight содержит CanConstructExecutionResources и controlled Reasons; он не утверждает удаленную model readiness. Expiry при старте переводится в оставшийся monotonic budget и не продлевается UTC rollback.

- [x] **RED:** добавить tests `Missing_approval_and_pending_selection_are_not_executable`, `Unknown_duplicate_or_changed_binding_fails_before_resources`, `Prices_limits_policy_and_binary_must_match`. Assert `CredentialsReads=0`, `ClientCreations=0`, `ResourceCreations=0`; подменить каждый digest/model/policy/rate по одному. Запустить `dotnet test tests/Travel.Tests.Contract --filter FullyQualifiedName~TripsBriefEvaluationAuthorizationTests`; увидеть отсутствие новой проверки/типа либо конкретный провал assertion.
- [x] **Implement:** strict manifest v1: unknown/duplicate members запрещены; manifest несет существующий Binding плюс ProcessorId=`anthropic`, fixed endpoint, FictionalOnly, expiration/authority reference, per-request/hour limits и artifact hashes. В Binding добавить optional `MaxRequestCostUsd=.03m`, `MaxHourlyCostUsd=.30m`; не повышать старые ceilings. Проверять `PricingModel==Model`, policy digest, совпадение rates/caps с options, packet/prompt, максимум48calls и полные оставшиеся резервы. `Authorized=true` в файле не является человеческим разрешением: будущий execute требует отдельно одобренный artifact reference и явный confirm его SHA256, это защита от случайного запуска, не новая система подписи/идентификации.
- [x] **Implement identity:** `Artifacts` содержит clean Git commit, SHA256 raw `git ls-tree -rz --full-tree HEAD` bytes, corpus file, embedded prompt/schema, normalizer/lexicon sources, lock/package identity и build-closure digest. Последний — SHA256 sorted UTF8 relative-path + NUL + raw-file-SHA256 lines для DLL/deps/runtimeconfig собственной published tool closure. Для runtime schema identity добавить только pure static `TripsBriefExtractor.SchemaSha256()`: SHA256 UTF8 compact JsonSerializer serialization текущего Schema JSON (без изменения definition/order). Fake wire test сверяет ее с фактически переданной логической schema; unsupported SDK transformations требуют явного решения, не silent widening. Разрешенные paths фиксированы и находятся внутри проверенного repo/artifact root; не читать secret files или произвольные paths из manifest. Dirty/extraneous execution closure или mismatch — not executable. Preflight без выбранной модели возвращает controlled pending reasons и `ReadyForExecution=false`, не выбирает модель.
- [x] **GREEN/review:** выбранные tests проходят; семь frozen input/contract fingerprints остаются прежними; два разрешённых source fingerprints (extractor identity helper/runner) пересчитываются и требуют future rebinding. До следующей задачи review границы «никакой resource/key access до проверки». Никакой настройки `ExecutionAuthorized` в appsettings.

## P2. Durable evidence и безопасный dispatch bookkeeping

**Files:** create `E/TripsBriefEvaluationJournal.cs`, `E/TripsBriefEvaluationFileJournal.cs`, `E/TripsBriefEvaluationSchedule.cs`, `E/TripsBriefEvaluationObservedChatClient.cs`, `C/TripsBriefEvaluationJournalTests.cs`, `C/TripsBriefEvaluationScheduleTests.cs`; modify Runner и существующие RunnerTests; добавить Compile Links в Contract project.

**Consumes:** P1 validated binding, `TimeProvider`, original ordinal/IDs. **Produces:** mandatory journal, schedule, eval-only IChatClient decorator с constructor `(IChatClient inner, TripsBriefEvaluationSchedule sharedSchedule)`; ни новых supplier clients, ни production middleware.

- [x] **RED:** tests `Intent_write_failure_has_zero_pipeline_calls`, `Crash_between_intent_and_result_is_uncertain`, `Duplicate_authority_and_outside_or_linked_paths_fail`, `Late_wait_cannot_catch_up`, `Ledger_delay_cannot_shorten_provider_spacing`, `Utc_rollback_cannot_extend_run`. Использовать memory journal/fake clock, faulting filesystem seam и fake HTTP; проверить sequence `[Intent,ProviderStart,Result]`, отсутствие второго call и отсутствие marker text в файлах/errors. Journal tests — `dotnet test tests/Travel.Tests.Contract --filter FullyQualifiedName~TripsBriefEvaluationJournalTests`; schedule tests — аналогично `TripsBriefEvaluationScheduleTests`.
- [x] **Implement journal:** stable approved root с anti-junction/symlink/path-traversal checks; authority claim сохраняется после success/failure/crash. Создавать bounded immutable per-case intent/result и final receipt через new temp file + flush-to-disk + no-overwrite atomic rename на том же volume. Разрешенные имена — RunId/CaseId из bound packet; максимум64KiB на запись, общий approved data cap256MiB. `CompleteAsync` пишет bounded summary/index с case-file references+SHA256 и aggregate/binding, не дублирует все48Cases bodies в64KiB; reader восстанавливает полный typed receipt из проверенных immutable files. Проверить max-size48-case output и отсутствие потерянных cases. Intent: RunId, caseID, correlationID, manifest hash, full request reservation и timestamps; result: только controlled normalized fictional fields/issues, outcome, diagnostics, known/unknown usage/cost/duration. Никакого raw prompt/text/completion/exception. Receipt read/validate классифицирует незавершенный intent как uncertain; не возобновляет и не создает новый вызов. Ошибка записи останавливает run до следующего dispatch; прошлые receipts/claims не удаляются.
- [x] **Implement pacing:** deadline run — `min(protocolDuration, authorizationRemaining)` по monotonic elapsed: не более6h (sealed packet3h) и не позже expiry approved authorization; истечение означает stop. Внутренние rolling-hour reservations runner также стареют по monotonic elapsed, UTC jump не освобождает бюджет. UTC — для аудита/fixture dates. Earliest case start — максимум original planned window/ordinal и last **actual SDK call start** +11s. Decorator вызывает `RecordProviderStart` непосредственно перед единственным inner `GetResponseAsync`, без I/O или ожидания внутри inference budget. Runner ждет **до** следующего input/deadline/admission; неодинаковое ledger admission время не сокращает provider spacing. Сначала durable intent с заранее выбранным correlationID, затем input с тем же ID и runtime deadline; после записи повторно проверять run budget. Запрет catch-up/retry/streaming/bypass; превышение/UTC jump не обновляет время разрешения.
- [x] **GREEN/review:** fake full48 сохраняет исходный порядок/6окон/48denominator; oversleep и ledger delay1000ms→0ms дают все provider gaps≥11s, а UTC rollback не позволяет выйти за6h. Раздельно учитывать pipeline attempts, actual provider starts и crash uncertainty; историческое `Dispatched` не выдавать за доказательство LLM call. Без claim reuse, auto-resume или восстановления uncertain calls. Существующие mechanical tests перенести на explicit test journal, сохранив assertions.

## P3. Полное component timing и честная атрибуция качества

**Files:** create `E/TripsBriefEvaluationCaseProcessor.cs`, `E/TripsBriefEvaluationDiagnostics.cs`, `C/TripsBriefEvaluationCaseProcessorTests.cs`; modify Runner/CaseReceipt/aggregate reporting и Compile Links. Production `TripBriefCandidateValidator` не менять.

**Consumes:** P1 binding, P2 journal/schedule, существующий extractor/validator. **Produces:** `CaseProcessor.ProcessAsync`; constructor получает Binding, TimeProvider и optional typed validator delegate (`TripBriefCandidateValidator.Validate` по умолчанию, fake delay только в tests).

- [x] **RED:** tests `Normalization_time_and_final_deadline_are_included`, `Rejected_H09_is_not_raw_safe_zero`, `Recovered_issue_is_attributed_to_normalizer`, `Existing_stricter_aggregate_guards_are_preserved`. Fake pipeline занимает100ms, validator advances clock5100ms: assert component≥5200ms, Exact=false, controlled deadline failure; повторный provider call отсутствует. Для correct proposal с пустыми Issues и source hazard сравнить added issue codes; для rejected H09 assert no accepted candidate, Exact=false и rejected/not-assessed diagnostics, а не «raw safety passed».
- [x] **Implement:** перенос текущей case extraction/accounting/normalization последовательности в processor. Timestamp до pipeline, duration после normalization, повторный deadline check после нее; preserve inference3500/handler4500 и independent whole5000 checks. Pacing waits/receipt I/O видны отдельно, wall-clock включает их; provider/EF work не вычитать. Unknown/mismatched usage/model/cost — stop с conservative reservation и без candidate; обычные учтенные refusal/invalid cases остаются failed rows, не repair calls.
- [x] **Implement diagnostics:** optional controlled receipt fields: observation available/not-assessed, rejected proposal, allowed `NormalizerAddedIssueCodes` и `DerivedSlots`; никакого произвольного model text. Использовать transient structured reply **до** validator и сравнение с его нормализованным результатом; если parse уже скрыл данные, observation=not-assessed. `Exact/Useful` остаются про normalized pipeline; `UnsafeCases` сохраняет текущую консервативную семантику D+H/all-slots. Не вводить новые скрытые quality thresholds и не выдавать normalized score за model-only accuracy.
- [x] **GREEN/review:** `dotnet test tests/Travel.Tests.Contract --filter FullyQualifiedName~TripsBriefEvaluation` проходит полностью. Full48/partial/NotRun/uncertain receipts сохраняют D/H totals,22/24 и11/12, текущие all-sample≤5000ms/unsafe guards и raw durations. Все rejected/fallback cases остаются в denominator; output diagnostics не создают model-quality claim.

## P4. Узкий standalone CLI и owned composition

**Files:** create `T/Travel.Trips.Evaluation.csproj`, `T/ToolProgram.cs`, `T/TripsBriefEvaluationCommand.cs`, `T/TripsBriefEvaluationEnvironment.cs`, `T/README.md`, `C/TripsBriefEvaluationCommandTests.cs`, `A/TripsBriefEvaluationEnvironmentTests.cs`; extend `A/TripsBriefSdkTests.cs`; modify `Travel.slnx`, `tools/ci/dotnet-inventory.json`, `.github/workflows/ci.yml` (safe preflight step only), Contract/AI test csproj.

**Consumes:** P1–P3 helpers. **Produces:** default preflight entry point и explicit future execute composition; `ITripsBriefEvaluationEnvironment` имеет отдельные `InspectArtifacts` (no credentials/resources), `CreateOwnedLedgerAsync`, `CreateChatClient`/Dispose boundaries. Интерфейс `ITripsBriefEvaluationEnvironment : IAsyncDisposable`: `TimeProvider Clock { get; }`, `TripsBriefEvaluationArtifacts InspectArtifacts(string repoRoot)`, `Task<ITripsBriefLedger> CreateOwnedLedgerAsync(TripsBriefEvaluationManifest manifest, CancellationToken ct)`, `IChatClient CreateChatClient(TripsBriefEvaluationManifest manifest)`. Реализация владеет своими container/services/client и их dispose; journal/claims хранятся отдельно и не удаляются. Test implementations считают обращения и возвращают fake HTTP/ledger; production CLI их не выбирает по аргументам.

- [x] **RED:** command tests `Default_preflight_never_resolves_secrets_or_resources`, `Execute_requires_explicit_bound_approval_before_factories`, `Cancellation_does_not_publish_passed`, `No_implicit_model_price_or_provider`. Assert default preflight exits0 с `ReadyForExecution=false`, ProviderCalls=0; bad execute exits2 с controlled code и всеми factories=0. Approved **fictional mechanical** manifest в tests использует только injected fakes; CLI execute не запускается с настоящими settings/keys. SDK test fake HTTP проверяет фактические model/temperature0/max_tokens1024/schema/no-thinking/no-tools/no-prompt-cache-control/zero-retries и returned model/usage mapping; не менять schema ради API.
- [x] **Implement CLI:** `OutputType=Exe`, `IsTestProject=false`, FrameworkReference=`Microsoft.AspNetCore.App` как у existing LocalStack tool, unique namespaced `ToolProgram`; ProjectReferences только AI и Trips.Application, Compile Links на E helpers, не на весь paid test project. Default `preflight [--repo-root PATH] [--manifest PATH]` только читает allowed artifact/manifest data и пишет bounded JSON status без content/secrets. `execute --manifest PATH --confirm-manifest-sha256 HASH` проверяет P1 повторно **до** key resolver, SDK/EF/container creation; fixed direct Anthropic factory, no AppHost/Host/UI/NATS startup, no UserSecrets/autoload/global configuration probing. Prepared code может содержать future execute branch, но source/test этап доказывает его только fakes и не вызывает реальную модель.
- [x] **Implement owned resources/integration:** реальная execute factory после gate создает только NEW labeled Testcontainers PG и `AiDbContext`/существующий metadata-only ledger; использовать pinned4.11.0 и точный digest образа из текущего Trips local-stack accepted configuration, не брать floating latest. CPU≤2/RAM≤2GiB суммарно, writable data≤256MiB и loopback-only; если caps/ownership/source readiness не доказаны — отказ без dispatch. Disposable schema initialization допустима только на созданном ею ledger, не target migration. Credentials resolver вызывается последним после повторных checks; fake IChatClient integration test доказывает1ledger admission/result, rollback/cancellation и удаление только своих container/volume при сохранении evidence/claims. Tests не подключаются к demo/target и не читают реальный key. Если environment не может доказать применение ресурсных caps, возвращать controlled not-ready до SDK. Наблюденный размер данных не выдавать за enforced disk quota; при недоступной quota не обходить gate. AI tests получают ProjectReference на T; Contract tests link только Command/interface code, без ProjectReference T и duplicate E types.
- [x] **GREEN/CI wiring:** включить tool в solution/inventory как `tool`, AI environment test — `Category=Integration` в существующем test-ai lane. В build-dotnet после solution build добавить только `dotnet run --project tools/trips/Travel.Trips.Evaluation --configuration Release --no-build -- preflight --repo-root .` (без manifest — pending, exit0, Calls=0). Paid workflow остается как есть; никаких новых scheduled/paid jobs. Пройти tool build, command fake tests, `dotnet test tests/Travel.AI.Tests --filter FullyQualifiedName~TripsBrief`, `npm.cmd run check:dotnet-inventory` и dependency-artifact checks. Restore/build packages не являются разрешением на API или provider metadata probes.

## P5. Приёмка source-only подготовки и документация

**Files:** modify `E/README.md`, `model-evaluation-options.md`, этот план и `tasks.md` (ссылки/статусы prerequisites); create `source-preparation-verification.md` **при выполнении**, не сейчас. Исходный evaluation-plan и protected corpus не изменять.

- [x] **Verify:** полный Contract suite (не paid AiEvals), AI M3 fake/NEW disposable-ledger tests, relevant architecture/inventory и actual CLI preflight subprocess. Для mechanical tests сохранять controlled local output; `preflight` ничего не создает и не вызывает модель. Не запускать весь `Travel.Tests.AiEvals`, `execute`, target Host или retained local demo.
- [x] **Review:** независимый whole-source review после source/test approval и реализации; исправить замечания и сделать scoped recheck. Особое внимание пяти Review Focus cases, unknown accounting и price/options mismatch. Появившаяся необходимость менять provider/public contract/corpus или ceilings — остановка с отдельным решением, не скрытый workaround.
- [x] **Document/check:** записать fresh base/result identity, red→green outcomes, exact commands/counts, resource ownership/cleanup, сравнение всех9baseline fingerprints:7unchanged,2approved source changes, observed gate/no-secret/no-call proof и ограничения. Обновить README exact CLI usage; original main task6.3 остается unchecked даже после готовности всех prerequisites. Выполнить pinned `node tools/openspec/run.mjs validate trips-m3-brief-confirmation --strict`, `validate --specs --strict`, `npm.cmd run check:ai-harness`, README/inventory checks и релевантные formatter checks.
- [x] **Handoff:** source ready означает только P1–P5 выполнены с нулем real provider calls и off/unset checked-in flags. Реальная48qualification, later24holdout, выбор/условия провайдера, бюджет, private-text/target/rollout остаются отдельными разрешениями. Publication при отдельном согласовании удобно разбить на2–3осмысленных commits (guards/evidence; processor/CLI; verification), без auto-push/PR/merge.

## Критерии завершения этого этапа

Все пять задач и их assertions подтверждены; source/test defects устранены, а не только перечислены; fake clocks не используются в production execute. Нулевая инференс-активность доказана для всех source checks/CI preflight; runtime M3 по-прежнему default-off. Corpus/prompt/oracles/schema/acceptance thresholds неизменны, неизвестный spend/uncertain attempts не переименованы в нулевые. Полный known-corpus/holdout quality и rollout **не приняты**. Главная task6.3 не отмечается выполненной.

## Самопроверка плана / handoff

P1 покрывает binding/authorization, P2 — evidence и actual-start clock boundaries, P3 — полный measurement/attribution, P4 — executable consumer и ownership, P5 — source acceptance без model execution. Cross-task signatures выше являются едиными; выбранные имена не оставлены исполнителю как догадки. Самопроверка исправила shared-schedule injection, manifest/observed-artifact границу, schema identity helper и bounded final-summary index (без дублирования48case bodies в одной записи); пять Review Focus cases имеют именованные RED tests. P1–P3 реализованы и проверяются повторно; P4/P5 проверяются после отдельно согласованных wire/resource уточнений; отдельный baseline dependency gate остаётся failed. Подготовка не использует новые pricing facts и не утверждает доступность/качество конкретной модели.

Рекомендация способа выполнения после approval: native исполнение в этой сессии с независимым итоговым review — задачи связаны общими типами/journal/schedule, поэтому параллельные edits здесь мало помогают. Source/test execution начато от fresh9ff60e4 в отдельной ветке codex/trips-m3-evaluator-preparation. Новые owned disposable containers используются только тестами; real models/target/demo не запускались.

Проверки планирования2026-10-28: pinned strict change/spec validation прошла (20/20current specs), AI harness134/134; links, arithmetic,9frozen hashes и неизменные23main task lines проверены. Это историческая проверка стадии планирования; актуальный source/test статус ниже и в source-preparation-verification.md.

## Уточнение source/test scope2026-10-28

Пользователь явно согласовал wire-проекцию со строгой локальной валидацией и продолжение общего ресурсного бюджета. Исходные schema/prompt/corpus/normalizer остаются прежними. Новые source files: AI TripsBriefWireProjection; E TripsBriefEvaluationArchiveJournal/ResourcePolicy; T TripsBriefEvaluationEnvelope, TripsBriefEnvelopeWorker/Budget, Envelope.Dockerfile/envelope.sh; A TripsBriefEnvelopeTests и C TripsBriefEvaluationArchiveTests.

P4 использует один Linux container для .NET worker и PG, общий cgroup CPU2/RAM2GiB/swap0. Только host admission/control остаётся снаружи; это не cap Docker daemon/сборки/всего Windows. Сумма writable quotas256MiB: PG112+socket4+tmp12+scratch32+shm32 tmpfs и один persistent64MiB archive. Kernel RLIMIT_FSIZE64MiB, read-only root/app/parent, UID999/capdrop/no-new-privileges; никаких published ports/logs/reaper. Source probe network=none. Supervisor6h + <=5s teardown grace. Ранее proposed file-rename journal остаётся mechanical fixture; runtime использует fixed mmap slots, commit marker и hash, без дублирования evidence. Это необходимое уточнение для единой kernel квоты и сохранения evidence после container loss.

Wire policy trips-anthropic-wire-v1 снимает только maxItems, сохраняет defs/refs. Native request projection проверяется actual pinned SDK fake HTTP на полном48 corpus; full local schema и oversized-array rejection неизменны. Full actual serialized envelope проверяется против консервативной границы4096. Эта механика не доказывает удалённую provider acceptance/tokenizer/quality.

Manifest дополнительно связывает WireSchema, ResourcePolicy, WorkerClosure (все published files), WorkerImage (immutable digest). Явный prepare-source публикует worker/image без ключей; default preflight не создаёт execution resources. После будущего clean commit нужна новая preparation/binding. До worker key handshake проверяются cgroup/PG membership/actual quota и host image+bind ownership; остаток первоначального monotonic authorization переносится после setup. Late/error/cancelled worker не получает повторных calls. Immutable index остаётся FinalizationPending, returned verdict связан hash.

Source RED/GREEN, независимое review, актуальные counts и baseline dependency failure — в [verification](source-preparation-verification.md). Ранние4P2 review исправлены и сохранены там как исторический этап. P5 handoff не закрывается одним kernel test: требуются весь source check, новое независимое review и явный учёт failed baseline. Модель/target/publication не разрешены; main6.3 остаётся pending.

Final source status: Contract98/98, ordinary AI261/261 including combined kernel
budget/loopback/crash probes and the5P2 fix regressions. P4 implementation was verified before the dependency extension; at that stage
the combined GREEN/CI checklist and P5 Handoff remained unchecked on the failed
baseline advisory. That historical limitation is resolved by the separately
authorized dependency completion below. Actual publication/CI, model/provider/prices/quality and target remain
separate. Final architecture/preflight/harness receipts are in verification.

## Separately authorized dependency completion2026-10-28

User explicitly approved investigating/fixing the baseline dependency next. The
[exact local backport receipt](../../../docs/operations/2026-10-28-deepmerge-security-backport.md)
supersedes the earlier failed-baseline limitation: unchanged upstream4.3.1 has a
locally verified revision1 repair, official/patched archives, strict artifact and
installed proof, visible raw4advisories and GREEN local gates/Storybook build.
No broad version updates or blanket waiver. Independent security review is clean.
P4 GREEN/CI wiring and P5 source-only Handoff are now checked on that evidence;
remote CI/publication and all real-model/quality/target gates remain pending.
Original milestone22/23 and main6.3 are unchanged. Package/lock modifications are
explicitly authorized new execution inputs; future model binding must be fresh.

## Current publication and run-packet status

P1–P5 source prerequisites are accepted through merged PR83 and its final ordinary PR/post-merge CI; earlier pending-publication statements retain phase-bound history. See [publication](publication.md#pr82-documentary-closure-and-pr83-evaluator-preparation) and the [no-model run review packet](model-evaluation-options.md#11-пакет-первого-запуска-подготовка-без-исполнения). The clean-base preflight is denied as intended. Initial worker/image preparation failed while Linux Docker was unavailable; the separately requested source-only retry succeeded on clean d318cb49, recorded in model-evaluation-options.md section12. Final execution authority and runtime handshake remain pending. No model call, new quality result or task6.3 completion follows. No checkbox is changed by this reconciliation.

## Paid model evaluation deferred

User decision2026-10-28: skip the paid API path, external model API budget USD0. P1–P5 source preparation remains accepted; task6.3 is deferred/not run. The current choice is manual-only/defer option F; model selection, alternate/local execution and rollout are not the next authorized step. See model-evaluation-options.md section13.
