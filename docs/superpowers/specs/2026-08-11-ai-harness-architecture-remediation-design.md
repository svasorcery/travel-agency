# Codex-first AI Harness and Architecture Remediation — Design Spec

**Дата:** 2026-08-11

**Статус:** концепт одобрен; письменный spec ожидает финального review владельца

**Аудитируемая база:** `dev` @ `0591459`

**Триггер:** переход разработки с Claude Code на Codex и репозиторный аудит AI-harness, composition root, cross-cutting concerns, persistence, messaging, CI и архитектурных guard rails.

---

## 1. Контекст

Travel Platform создаётся как демонстрация AI-first разработки и зрелого проектирования сложной модульной системы. Foundation и Flights M1 уже содержат сильные решения: физическое разделение Core/Application/Infrastructure, Marten event store, Wolverine outbox, EF inbox/outbox, provider ACL, `TimeProvider`, централизованные analyzers и большой Flights test corpus.

Аудит выявил разрыв между заявленной архитектурой и реально собираемой системой:

- tracked AI-harness остаётся Claude-first, а текущая Codex-адаптация целиком untracked и частично механически скопирована;
- `Travel.Host` и Flights Infrastructure делят владение composition root;
- platform defaults и module-specific policies конкурируют друг с другом;
- несколько зелёных тестов не проверяют реальные runtime-контракты;
- fresh database, CI для `dev`, межпроцессный NL-search и EF read-model recovery не имеют рабочего end-to-end контракта;
- ADR, README и agent instructions содержат устаревшие либо противоречивые факты.

Этот документ задаёт целевую архитектуру и декомпозирует remediation на независимые workstream-ы. Это не big-bang rewrite и не новый product milestone.

### 1.1. Опорные факты аудита

- Codex artifacts присутствуют только как untracked [`AGENTS.md`](../../../AGENTS.md) и [`.codex/`](../../../.codex/), а nested module `AGENTS.md` отсутствуют.
- Оба hook adapter-а читают Claude-specific environment variables вместо stdin event: [Claude settings](../../../.claude/settings.json), [Codex hooks](../../../.codex/hooks.json).
- Host напрямую владеет Flights persistence, Marten, telemetry, authorization и routing: [`Travel.Host/Program.cs`](../../../apps/Travel.Host/Program.cs).
- Flights registration одновременно объявляет себя composition root и оставляет часть module wiring Host-у: [`FlightsModuleServiceCollectionExtensions.cs`](../../../modules/flights/Travel.Modules.Flights.Infrastructure/FlightsModuleServiceCollectionExtensions.cs).
- Host и AI используют разные CLR types для NL-search: [Flights contracts](../../../modules/flights/Travel.Modules.Flights.Application/Contracts/NlSearchContracts.cs), [AI contracts](../../../apps/Travel.AI/NlSearch/Contracts/NlSearchContracts.cs).
- Contract test сравнивает shape, но не Wolverine identity: [`NlSearchContractShapeTests.cs`](../../../tests/Travel.Tests.Contract/Flights/NlSearchContractShapeTests.cs).
- Generic initializer запускается без production module initializers: [`AppInitializer.cs`](../../../shared/dotnet/Travel.Shared.Infrastructure/Initialization/AppInitializer.cs).
- Booking event/outbox commit и EF projection являются отдельными saves: [`ConfirmOrderHandler.cs`](../../../modules/flights/Travel.Modules.Flights.Application/Handlers/Booking/ConfirmOrderHandler.cs), [`OrderReadModelProjectorImpl.cs`](../../../modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/OrderReadModelProjectorImpl.cs).
- CONTRIBUTING направляет PR в `dev`, а CI слушает только `master`: [`CONTRIBUTING.md`](../../../CONTRIBUTING.md), [`ci.yml`](../../../.github/workflows/ci.yml).

---

## 2. Цели

1. Сделать Codex основным, воспроизводимым из Git AI-клиентом, сохранив Claude Code как тонкий совместимый adapter.
2. Устранить ложные и дублирующиеся инструкции, hooks и workflow definitions.
3. Сделать `Travel.Modules.{Name}.Api` публичным composition facade модуля без добавления отдельного `Composition.csproj`.
4. Оставить Host владельцем process-level решений, а module-specific wiring скрыть за module facade.
5. Исправить подтверждённые correctness gaps: Wolverine message identity, database initialization, projection recovery, configuration contracts и CI coverage.
6. Зафиксировать единый источник domain transition rules.
7. Усилить architecture, contract, configuration и fresh-volume tests так, чтобы заявленные границы действительно проверялись.
8. Синхронизировать текущую документацию с исполняемым кодом, не переписывая исторические design records задним числом.

### 2.1. Не входит в scope

- реализация Hotels, Rail, Trips и полноценного frontend product UI;
- deploy, публикация images, изменение live infrastructure, secrets или внешних provider accounts;
- автоматический production migration при старте application host;
- создание собственного универсального module framework или reflection-based module discovery;
- публикация внутренних integration contracts как отдельного NuGet package;
- добавление MCP только ради симметрии Claude/Codex;
- удаление legacy `src/` в одном коммите с архитектурным refactoring;
- изменение бизнес-правил Flights без отдельного domain decision.

---

## 3. Зафиксированные решения

### D1. `AGENTS.md` — канонический AI-контракт репозитория

Codex становится основным клиентом. Канонические repository и module instructions хранятся в:

```text
AGENTS.md
modules/<name>/AGENTS.md
shared/AGENTS.md
apps/Travel.AI/AGENTS.md
```

Tracked `CLAUDE.md` сохраняются, но импортируют соответствующий `AGENTS.md` через поддерживаемый Claude синтаксис `@AGENTS.md`. В них остаются только действительно Claude-specific дополнения. Полные параллельные копии запрещены.

Перед переносом исправляются факты, а не копируется текущее содержимое:

- `ErrorOr<T>` вместо устаревшего `Result<T>`;
- Flights M1 вместо статуса scaffold;
- актуальные ADR paths и отсутствие hard-coded ADR count;
- актуальные formatter commands;
- корректные Windows/PowerShell команды;
- отсутствие hard-coded dashboard URL: authoritative URL берётся из launch output `dotnet run --project apps/Travel.AppHost`;
- действительное устройство test infrastructure и provider naming;
- lowercase `.codex` на всех файловых системах.

### D2. Повторяемые AI-workflows становятся skills

Канонические процедуры располагаются в `.agents/skills/<name>/SKILL.md`. В первую миграцию входят:

- `spec`;
- `adr`;
- `explore-domain`;
- `test-this`;
- `integration-from-openapi`;
- общие инструкции для domain modeling, migration authoring и test authoring.

`.claude/skills` и legacy `.claude/commands` используются как минимальные compatibility adapters на время перехода. `.codex/agents/*.toml` и `.claude/agents/*.md` содержат роль, permissions/sandbox intent и ссылку на один canonical workflow, но не две независимые версии 40–60 строк instruction body.

AI workflow не получает commit, push, migration execution или external mutation authority по факту вызова. Эти действия остаются отдельными пользовательскими разрешениями.

Live Codex acceptance использует составное доказательство, потому что ни один публичный endpoint не аттестует одновременно Git provenance и загруженное содержимое. Clean-clone validator фиксирует tracked inventory и статический body contract; короткая App Server-сессия через `skills/list` подтверждает repo scope, enabled state, metadata и физический путь для root и nested cwd; typed `skill` input использует возвращённый сервером path без реконструкции и является поддерживаемым способом инъекции полных skill instructions. Verifier-owned thread и App Server закрываются до отдельных долгих literal `$skill` smokes, которые проверяют реальное поведение без перекрытия с этой сессией. `codex debug prompt-input` не используется как skill provenance: текущий CLI создаёт plain text input без typed skill selection. Custom-agent provenance тоже composite: tracked project TOML, отсутствие одноимённого personal agent в точном `codexHome` из `initialize`, request-scoped trust exact clean-clone root без записи user config, и успешный explicit `agent_type: domain-modeler` spawn. Для Codex 0.147 verifier включает experimental raw events и требует единственный raw `spawn_agent` call в namespace `collaboration` с четырьмя аргументами. `task_name: harness_identity`, `agent_type: domain-modeler` и `fork_turns: none` проверяются буквально. Message принимается только в одном из source-defined transport-вариантов: отсутствующий `encrypted_function_args` — наблюдаемая implicit opaque ветка, `['message']` — explicit encrypted ветка; обе требуют opaque payload, отличный от plaintext probe. `[]` — explicit plaintext ветка и требует exact probe. Отсутствие metadata само по себе не является криптографической аттестацией. Локальный публичный протокол не умеет расшифровать opaque payload и не аттестует его plaintext, поэтому формат ciphertext не распознаётся regex или длиной. Raw `call_id` связывается с MultiAgentV2 `subAgentActivity(kind: started)`, после чего требуется structured `thread_spawn` source с exact parent, task path и `agent_role: domain-modeler`. Child должен иметь ровно один собственный completed turn без tool use и ответить точно `travel-agency/domain-modeler`; этот ответ является behavioral correlation внутри составного доказательства, но не самостоятельным source/plaintext provenance. Model self-report, regex по transcript и произвольный recursive JSON walk доказательством не являются.

App Server client следует опубликованному wire contract: newline-delimited JSON-RPC envelopes передаются без поля `jsonrpc`, request results обязаны иметь документированные `{turn}`, `{thread}` и `{data}` wrappers, а `thread/read(includeTurns: true)` принимается только с полной `itemsView: "full"` проекцией каждого turn. Literal `codex exec --json` принимается только как один ordered root lifecycle от `thread.started` и `turn.started` до финального `turn.completed` с полной usage schema и хотя бы одним completed non-empty `agent_message`. Любое расхождение response/result/error/notification/JSONL schema, неполная история, неоднозначный skill source, нарушение статического body contract, collision или невозможность безопасной очистки закрывает gate с ошибкой.

The live discovery boundary is the clean clone, not the complete personal Codex catalog. Schema-valid discovery errors outside the clone do not invalidate an exact enabled repo-scoped target; every clone-local discovery error, target-name conflict, canonical-path conflict, or malformed error record fails closed. Cleanup closes App Server stdin and waits briefly for authoritative wrapper-tree closure before force termination.

### D3. Сломанные edit/Stop hooks не сохраняются

Текущие formatter hooks удаляются как неработающие и дублирующие Lefthook. Они используют недокументированные environment variables, проглатывают ошибки и не читают JSON event из stdin.

Форматирование и verification выполняются детерминированно:

- явно агентом перед завершением изменения;
- Lefthook на developer lifecycle;
- CI как обязательный non-mutating check.

Если live post-edit hooks понадобятся позднее, они проектируются отдельным change set: client-specific stdin adapter, общий checked-in script, Windows path normalization, валидный client response и тестовые fixtures для обоих event schemas. Silent failure запрещён.

### D4. `Travel.Modules.{Name}.Api` — публичный module facade

Отдельные `Travel.Modules.{Name}.Composition` проекты не создаются. Для текущего single HTTP host это лишняя сборка без самостоятельной runtime-роли.

Каждый `Api` project имеет две явно разделённые области:

```text
Travel.Modules.Flights.Api
├── Composition
│   ├── FlightsModule
│   ├── service registrations
│   ├── Wolverine/Marten contributions
│   ├── authorization and telemetry contributions
│   └── middleware contribution / endpoint discovery
├── Endpoints
├── Contracts
└── Middleware
```

Публичная поверхность модуля для Host ограничена registration, process-builder contributions и middleware hook:

```csharp
public static IHostApplicationBuilder AddFlightsModule(
    this IHostApplicationBuilder builder);

public static void ConfigureMarten(StoreOptions options);

public static void ConfigureWolverine(WolverineOptions options);

public static WebApplication UseFlightsModule(
    this WebApplication app);
```

Host создаёт global Marten/Wolverine builders ровно один раз. Внутри этих единственных callbacks он вызывает facade contributions каждого enabled module:

```csharp
builder.Services.AddMarten(options =>
{
    FlightsModule.ConfigureMarten(options);
});

builder.Host.UseWolverine(options =>
{
    FlightsModule.ConfigureWolverine(options);
});
```

Module facade не вызывает второй `AddMarten`/`UseWolverine` и не меняет process-global policy. `ConfigureMarten` добавляет только module event/projection mappings; `ConfigureWolverine` добавляет только module discovery, routes и handlers. Stream identity, durability store, transport connection и process-wide failure policy остаются в Host.

Из module projects Host directly references только Api facades **enabled modules**. Допустимые non-module references включают существующие `Travel.ServiceDefaults`, `Travel.Shared.Web`, `Travel.Shared.Infrastructure` и integration-contract assemblies. Новый `Platform.Hosting` project этим design не создаётся. Host не импортирует module DbContext, provider types, projections, metrics, handlers или internal message contracts.

Разрешённая project dependency `Api -> Infrastructure` используется только composition namespace. Архитектурные правила запрещают `Api.Endpoints`, `Api.Contracts` и обычному middleware зависеть от Infrastructure. Endpoint вызывает Application command/port; atomic persistence/outbox implementation остаётся в Infrastructure.

Duffel webhook endpoint после refactoring читает только raw body и transport headers и передаёт provider-neutral ingestion request. Infrastructure verifier/parser владеет HMAC protocol, supplier DTO, PostgreSQL duplicate semantics и atomic EF inbox/outbox. Application получает typed outcome и переводит его в transport-neutral success/error; API выполняет только HTTP mapping.

SDK-style `ProjectReference` транзитивен, поэтому одного удаления direct references из Host недостаточно. Facade boundary дополнительно обеспечивается минимальной public composition API, `internal` module registration helpers там, где это допускают EF/Wolverine tooling requirements, и architecture tests для Host/API namespaces. Публичный Infrastructure type не считается разрешённым Host dependency только потому, что assembly оказался транзитивно доступен.

`UseFlightsModule` подключает только module middleware в заранее определённой Host фазе. Host сохраняет единственный process pipeline и порядок:

```text
global exception handling
-> authentication
-> authorization
-> enabled module middleware
-> one MapWolverineEndpoints call
```

Facades не вызывают `MapWolverineEndpoints` самостоятельно. Это исключает повторное mapping и делает порядок security middleware явным.

Если в будущем один модуль будет подключаться к нескольким host types без HTTP (`worker`, `CLI`, отдельный consumer process), тогда composition может быть выделен в самостоятельную сборку отдельным ADR. До появления этого требования действует YAGNI.

### D5. Явная модель владения cross-cutting concerns

| Владелец | Ответственность |
|---|---|
| `Travel.ServiceDefaults` / platform hosting | logging, OTel/exporter wiring, service discovery, `TimeProvider`, standard ProblemDetails/exception policy, OpenAPI services, health endpoint policy, opt-in internal HTTP resilience profile |
| `Travel.Host` | выбор enabled modules, physical resource names, transport guarantees, global auth fallback, process-level Wolverine/Marten policy, build/map/run |
| Identity Api facade | JWT/Keycloak options, claim normalization, scope requirements, fail-closed user identity |
| Flights Api facade | полная регистрация Flights, module policies, middleware, metrics, health contributors, handlers и provider adapters через Infrastructure |
| Integration contracts | стабильные request/reply/event identities и wire versions между processes |
| `Travel.Shared.Infrastructure` | только переиспользуемые non-web primitives и initialization abstractions с реальными consumers |
| `Travel.Shared.Web` | HTTP-specific result/error/identity helpers без module-specific или hosting policy |

`Shared.Infrastructure` не становится dumping ground для module wiring. `Shared.Web` не содержит general startup policy. Любой shared primitive должен иметь минимум два реальных consumer-а либо обоснованный platform contract.

### D6. External HTTP resilience принадлежит integration adapter

Глобальный `AddStandardResilienceHandler()` больше не применяется ко всем `HttpClient` автоматически.

- internal service-to-service clients могут явно выбрать platform profile;
- Duffel, Travelpayouts, Frankfurter, Keycloak Admin и health probe clients владеют полным provider-specific timeout/retry/circuit-breaker contract;
- один client не может одновременно получить platform и provider retry pipeline;
- secret redaction является generic/configurable platform mechanism, а provider-specific sensitive names вносятся integration contributor-ом.

Composition test собирает полный Host registration graph и проверяет отсутствие stacked resilience handlers.

### D7. NL-search использует общий versioned integration contract

Mirrored CLR records в Flights Application и Travel.AI удаляются. Создаётся один внутренний project `Travel.IntegrationContracts.AI`, referenced обоими processes. Он содержит request/reply types и стабильные wire identities, не зависящие от будущего namespace refactoring.

Contract assembly остаётся leaf dependency: он не ссылается на apps, modules, persistence, ASP.NET, EF, Marten или domain assemblies. Разрешены BCL serialization types и минимальная Wolverine message-identity metadata. В нём нет behavior, service registration, `ErrorOr` или shared domain primitives. Прямые consumers ограничены allowlist: Flights Application, Travel.AI, Flights Api Composition и contract tests. Architecture test проверяет этот graph, чтобы contracts project не стал новым cross-process dumping ground.

`NlSearchRequested`/`NlSearchParsed` contract verification включает:

1. message identity/version;
2. JSON serialization shape;
3. correlation/reply metadata;
4. реальный Host → NATS → AI → reply integration test.

Для интерактивного NL-search выбирается **Core NATS request/reply** с bounded timeout: ответ после истечения пользовательского timeout уже не имеет ценности. JetStream резервируется для durable commands/events, которые должны пережить отсутствие consumer-а. Это различие явно фиксируется в ADR и architecture overview; наличие JetStream на broker-е не означает, что каждый subject durable.

AI cost ledger получает idempotency key на основе correlation/message identity, чтобы retry или потерянный reply не создавали дублирующий charge record.

### D8. Database initialization имеет environment-specific policy

Initialization framework сохраняется только после появления реальных module initializers и явного порядка фаз.

Для Aspire/dev/test:

1. применяется platform schema/bootstrap;
2. применяются module EF migrations с корректным migrations-history schema;
3. настраивается/проверяется Marten schema;
4. выполняются безопасные development seeds;
5. readiness открывается только после успешного завершения.

Для production application host не вызывает `MigrateAsync` автоматически. Deployment process должен выполнить отдельный migration gate/job. Application startup проверяет совместимость schema/checkpoints и остаётся not-ready при несовместимости.

Runtime и design-time DbContext configuration используют одну module-owned функцию конфигурации provider, naming convention и migrations history table.

Fresh-volume smoke обязан выполнить не только `SELECT server_version`, но и минимум один EF-backed Flights flow и запись AI cost ledger без внешнего платного LLM-вызова.

### D9. EF order read model становится durable replayable projection

`order_read_model` перестаёт обновляться отдельным post-commit вызовом из command handler.

- Marten stream остаётся source of truth;
- каждая booking transaction публикует internal `ReconcileOrderReadModel(AggregateId)` через тот же `IMartenOutbox`, что и event commit;
- durable Wolverine handler читает `ProjectedStreamVersion` из EF, загружает из Marten все stream events после этой версии и применяет их по порядку;
- EF projection хранит применённую stream version и идемпотентно игнорирует duplicate/older delivery;
- transient failure ретраится, terminal failure виден через DLQ/health/metrics;
- duplicate или out-of-order reconcile messages безопасны: handler всегда догоняет stream от persisted version до current version;
- rebuild command перечисляет booking streams и вызывает тот же reconcile service с reset/rebuild mode, не отдельную projection implementation;
- projection checkpoint участвует в readiness только там, где read-model freshness является обязательной;
- command response строится из результата команды, а query API честно допускает короткий eventual-consistency interval.

`ReconcileOrderReadModel` является internal application message и не пересекает bounded context/process boundary. Per-order `ProjectedStreamVersion` принадлежит EF read model; operational rebuild progress принадлежит rebuild runner. Retry и DLQ policy принадлежат module Wolverine contribution, а не command handler.

Fault-injection test обязан остановить EF update после event commit и доказать автоматическую convergence без повторного пользовательского command.

### D10. Domain transition rules имеют один источник истины

Handlers больше не дублируют state-machine проверки `BookingAggregate`.

Core policy возвращает typed transition decision:

- `Allowed`;
- `IdempotentNoOp`;
- `Rejected` с domain reason.

Application переводит decision в `ErrorOr`/HTTP semantics и оркестрирует providers/persistence. Семантика повторной отмены, terminal webhook events и expiry фиксируется domain tests, а не расходящимися `if` в нескольких handlers.

### D11. Web, health и configuration contracts стандартизируются

Platform Web baseline включает:

- единый RFC7807 ProblemDetails contract для expected и unhandled errors;
- fail-closed claim parsing вместо `Guid.Empty`;
- transport/configuration validation;
- OpenAPI document registration и проверяемый generation/diff path.

Health semantics разделяются:

- `live` — процесс способен обслуживать probe;
- `ready` — обязательные собственные stores/schema/checkpoints готовы;
- optional external dependencies — degraded diagnostics, но не автоматический readiness blocker;
- feature-disabled providers не регистрируют failing readiness check;
- production probe endpoints доступны только через согласованный internal exposure policy.

Configuration переводится на typed validated options. Исправляется `Smtp__Host` → `Flights__Smtp__Host`; production Keycloak authority, SMTP и обязательные provider settings не маскируются localhost fallback-ами.

### D12. CI и architecture tests доказывают заявленные границы

Немедленный CI baseline не зависит от того, видит ли Nx .NET graph:

- PR/push workflow покрывает реальный `dev` integration flow;
- выполняется явный `dotnet build Travel.slnx`;
- каждый test project включён ровно в одну основную lane либо явно allowlisted как paid/manual;
- Flights unit, AI unit, trait-free Host HTTP tests, architecture, contract, integration, Aspire smoke и E2E имеют явные jobs;
- frontend продолжает использовать Nx affected graph;
- отдельная проверка сравнивает solution projects, repository `.csproj` и CI inventory.

ADR 0004 пересматривается отдельно: `@nx/dotnet` не считается работающим до появления реально проверяемого .NET graph. Его можно подключить для affected optimization позднее, но он не заменяет полноту backend gate.

Architecture suite расширяется:

- all-module pair matrix вместо асимметричных handwritten rules;
- MSBuild `ProjectReference` graph плюс IL type dependencies;
- `Host -> module Api facade only`;
- `Api.Endpoints/Contracts/Middleware !-> Infrastructure`;
- исключение только для `Api.Composition`;
- `Application !-> Infrastructure/Api`, `Core !-> Application/Infrastructure/Api`;
- `Shared !-> modules`;
- `Travel.IntegrationContracts.AI !-> apps/modules/persistence`, кроме allowlisted Wolverine identity metadata;
- direct consumers integration contracts ограничены Flights Application, Travel.AI, Flights Api Composition и contract tests;
- implemented modules требуют non-empty selector results;
- domain events выбираются namespace/interface contract, а не суффиксом `Event`.

---

## 4. Data flow после remediation

### 4.1. Host composition

```text
Travel.Host
  ├── AddPlatformDefaults
  ├── AddIdentityModule          -> Identity.Api facade
  ├── AddFlightsModule           -> Flights.Api facade
  ├── configure process transport/durability
  └── build/map/run

Flights.Api.Composition
  ├── Application handlers
  ├── Infrastructure adapters and stores
  ├── API endpoints/middleware
  ├── Marten module contribution
  ├── Wolverine module contribution
  ├── telemetry/health contribution
  └── authorization contribution
```

### 4.2. NL-search

```text
HTTP request
  -> Flights Api endpoint
  -> Flights Application handler
  -> shared NlSearchRequested v1
  -> Core NATS request/reply
  -> Travel.AI handler
  -> idempotent AI cost ledger
  -> shared NlSearchParsed v1
  -> Flights search criteria
  -> provider search
```

Timeout, unavailable AI или invalid model response возвращают существующий typed fallback/error contract. Сообщение не переигрывается позднее через JetStream.

### 4.3. Booking projection

```text
booking command
  -> Core transition decision
  -> provider side effect
  -> append event + outbox ReconcileOrderReadModel(aggregateId) atomically
  -> durable Wolverine reconcile handler
  -> read Marten events after EF ProjectedStreamVersion
  -> idempotent EF read-model upsert + advance version
  -> checkpoint/metrics
```

Projection failure не откатывает уже подтверждённую domain transaction, но автоматически repair-ится через durable delivery/rebuild.

---

## 5. Workstreams и порядок

Remediation разбивается на отдельные implementation plans/change sets. Один гигантский PR запрещён.

### WS1 — Codex-first AI-harness

**Зависимости:** нет.

**Результат:** tracked, client-neutral, self-consistent agent environment.

- исправить canonical project/module facts;
- добавить tracked root/module/shared/AI `AGENTS.md`;
- перевести `CLAUDE.md` на imports;
- создать canonical `.agents/skills`;
- сократить agent adapters;
- удалить broken hooks и устранить ложные command paths;
- обновить README/CONTRIBUTING/devcontainer AI-client wording;
- добавить harness integrity checks.

### WS2 — Correctness and delivery gates

**Зависимости:** WS1 только для корректной agent automation; runtime-изменения логически независимы.

**Результат:** базовые claims проекта подтверждаются clean environment и CI.

- общий NL-search integration contract и transport test;
- Core NATS/JetStream semantics и superseding ADR 0020 amendment в том же change set;
- `dev` CI и полный .NET build/unit inventory;
- SMTP/Keycloak/config validation;
- module initializers для dev/test, production schema gate и amendment ADR 0007;
- live/ready/dependency health contracts;
- fresh-volume functional smoke.

### WS3 — Module composition and cross-cutting ownership

**Зависимости:** WS2 contract/config decisions.

**Результат:** Host зависит только от Api facades enabled modules; policy stacking отсутствует.

- перенести Flights/Identity wiring в `Api.Composition`;
- удалить все Host references на Core/Application/Infrastructure Hotels, Rail и Trips; scaffold modules не включаются в runtime до своего milestone;
- убрать premature `Api -> Infrastructure` references из scaffold modules, пока у них нет реального composition implementation;
- оставить process-level decisions в Host;
- удалить Host imports внутренних module types;
- перенести webhook persistence за Application port;
- развести platform/internal и provider resilience;
- унифицировать runtime/design-time persistence configuration;
- привести Shared.Infrastructure/Shared.Web к зафиксированным charters;
- стандартизировать Web baseline и observability contribution;
- зафиксировать facade/process ownership новым ADR либо amendment ADR 0001/0009 в том же change set.

### WS4 — Booking consistency

**Зависимости:** WS3 composition и durable messaging registration.

**Результат:** state machine и read model восстанавливаются после сбоев.

- typed Core transition decisions;
- удалить handler-side duplicate guards;
- durable versioned EF projection;
- projection checkpoint, retry, DLQ diagnostics и rebuild command;
- fault-injection/convergence tests;
- amend/supersede ADR 0015 и ADR 0016 одновременно с изменением transition/projection semantics.

### WS5 — Enforcement and truthfulness

**Зависимости:** WS1–WS4.

**Результат:** документация и guards описывают финальный код.

- полная project/namespace architecture matrix;
- executable README examples/OpenAPI smoke;
- финально сверить ADR 0001, 0004, 0006, 0007, 0009, 0015, 0016, 0020 и architecture descriptions; semantic amendments выполняются в owning WS, а не откладываются до WS5;
- описать текущий runtime как direct `IChatClient`/Anthropic; ADR 0012 перевести в статус Deferred до отдельного MAF product milestone;
- dedicated review перед удалением legacy `src/`;
- удалить NX welcome в отдельном frontend foundation change, не смешивая с backend remediation.

---

## 6. Testing strategy

| Уровень | Обязательная проверка |
|---|---|
| AI harness | path existence, canonical/import parity, skill/agent inventory, no stale Claude env assumptions, Windows command fixtures |
| Unit | domain transition decisions, options validation, provider resilience policy, message identity |
| Composition | реальный DI graph, один resilience pipeline на client, Host imports only facades, registered middleware/policies/handlers |
| Contract | shared CLR/wire identity, serialization version, reply metadata |
| Integration | NATS request/reply, EF/Marten outbox, projection retry/convergence, module initialization |
| Architecture | project-reference graph, namespace dependencies, positive selector results, all-module isolation matrix |
| Fresh-volume smoke | clean Postgres/NATS/Redis startup, schema readiness, EF-backed Flights operation, AI ledger write без paid provider |
| E2E | `/api/status` плюс минимум один реальный module route; README examples generated or executable |

Paid AI evals и реальные external providers не входят в обязательный local gate. Они запускаются только в явной protected CI lane с отдельной авторизацией и budget policy.

---

## 7. Error handling и observability

- Expected domain/application failures остаются typed `ErrorOr` errors и переводятся в единый ProblemDetails shape.
- Unhandled exceptions проходят global exception handler, логируются со trace/correlation identifiers и не раскрывают secrets/PII.
- Startup initialization failure не скрывается localhost fallback-ом и блокирует readiness.
- Projection lag, retry count, DLQ и last successful checkpoint имеют metrics и health diagnostics.
- AI request correlation одновременно связывает trace, request/reply и idempotent cost entry.
- Secret query/header names редактируются generic механизмом плюс module contribution; provider tokens не перечисляются вручную в platform defaults.
- LGTM containers не считаются observability stack, пока exporter/collector/data-source path не подтверждён smoke test-ом.

---

## 8. Migration и compatibility

- Изменения AI-harness сначала учитывают pre-existing untracked `.codex/` и `AGENTS.md`; они не перезаписываются вслепую.
- Claude compatibility сохраняется через imports/adapters и проверяется статически без необходимости доступа к Claude account.
- Межпроцессный contract получает стабильную v1 identity; старые mirrored types удаляются в одном atomic source change до появления production messages in flight.
- Schema-affecting projection/config changes получают отдельные EF migrations и rollback/rebuild notes в implementation plan.
- Production migration, deploy и external dispatch не являются частью source-ready remediation.
- Legacy `src/` удаляется только после отдельной проверки отсутствия полезных fixtures/provider knowledge и отдельного review, поскольку удаление необратимо для рабочего дерева без Git recovery.

---

## 9. Acceptance criteria

Remediation считается завершённой только когда:

1. Fresh clone получает рабочие Codex instructions, skills и custom agents из Git.
2. Claude instructions импортируют тот же canonical content без дублирования.
3. Ни один tracked AI hook не зависит от undocumented environment variables и не проглатывает ошибки.
4. Host project directly references только Api facades enabled modules; scaffold module assemblies отсутствуют в runtime graph.
5. Host создаёт по одному global Marten builder, Wolverine builder и endpoint mapping; modules вносят только facade contributions.
6. Только `Api.Composition` может зависеть от module Infrastructure; endpoints/contracts не могут.
7. Global authentication/authorization выполняются до module middleware, а `MapWolverineEndpoints` вызывается один раз.
8. Integration-contract assembly является leaf dependency с allowlisted consumers.
9. NL-search проходит реальный NATS request/reply test и обе стороны используют одну message identity.
10. Clean Aspire/dev database создаёт требуемые schemas/tables до readiness.
11. Production host не применяет migrations автоматически и fail-closed проверяет schema compatibility.
12. Fault после Marten commit автоматически repair-ится через durable `ReconcileOrderReadModel` и stream-version catch-up.
13. Booking transition semantics определены одним Core policy и покрыты domain tests.
14. `dev` PR запускает полный обязательный CI graph, включая .NET unit и trait-free Host tests.
15. Health endpoints различают live, ready и optional dependency degradation.
16. SMTP/Keycloak/provider configuration валидируется и не маскируется неверными localhost defaults.
17. Architecture tests проверяют project references, all-module isolation и non-empty implemented selectors.
18. README quick-start examples соответствуют OpenAPI/runtime contracts и выполняются smoke test-ом.
19. Source-ready, integration-proven и live-proven статусы сообщаются раздельно.

---

## 10. Traceability аудита

| Подтверждённая проблема | Решение |
|---|---|
| Untracked/mеханически скопированный Codex harness | D1–D3, WS1 |
| Broken Claude/Codex hooks | D3 |
| Split Flights composition root | D4–D5, WS3 |
| Global resilience конфликтует с provider pipelines | D6 |
| Разные Wolverine CLR identities | D7, WS2 |
| JetStream включён на broker, но semantics не выбраны | D7 |
| Нет runtime initializers/fresh DB readiness | D8, WS2 |
| Post-commit EF dual write | D9, WS4 |
| Core guards обходятся handlers | D10, WS4 |
| SMTP/health/Web/configuration gaps | D11, WS2–WS3 |
| `dev` без CI и .NET без unit lane | D12, WS2 |
| Неполные architecture tests | D12, WS5 |
| Legacy `src`, stale ADR/README, NX starter | WS5 |

---

## 11. Implementation planning boundary

После письменного approval этого spec первым создаётся детальный implementation plan только для **WS1 — Codex-first AI-harness**. WS2–WS5 получают отдельные plans после завершения и проверки предыдущих архитектурных gates. Это ограничивает blast radius, сохраняет reviewable diffs и не смешивает developer tooling с runtime correctness и schema changes.
