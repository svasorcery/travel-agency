# Foundation (Subproject 0) — Design Spec

**Дата:** 2026-05-04  
**Статус:** approved, ready for implementation plan  
**North star:** `docs/superpowers/specs/2026-05-03-travel-platform-concept.md`  
**Скоуп:** полный Foundation — NX workspace, Aspire, CI, devcontainer, ADR-набор, AI-harness, тестовая инфраструктура, один E2E вертикальный срез.

---

## 1. Отправная точка

Существующий репозиторий (`Viajante.sln`, частичный Rail-модуль на .NET 6) **полностью сносится** — мигрировать нечего. История git сохраняется, весь код удаляется, Foundation начинается с чистого листа.

Корневой неймспейс: `Travel` (вместо старого `Viajante`).

---

## 2. Структура репозитория

Domain-oriented layout: верхний уровень по бизнес-области, внутри — технология.

```
travel-agency/
├── .claude/
│   ├── settings.json
│   ├── agents/
│   │   ├── domain-modeler.md
│   │   ├── adr-writer.md
│   │   ├── test-author.md
│   │   ├── migration-author.md
│   │   └── integration-mapper.md
│   └── commands/
│       ├── spec.md
│       ├── adr.md
│       ├── explore-domain.md
│       ├── test-this.md
│       └── integration-from-openapi.md
│
├── apps/
│   ├── Travel.Host/                   .NET 10, модульный монолит
│   ├── Travel.AI/                     .NET 10
│   ├── Travel.AppHost/                Aspire orchestrator
│   ├── Travel.ServiceDefaults/        OTel, health checks, service discovery
│   └── web/                           Angular 21
│
├── modules/
│   ├── flights/
│   │   ├── Travel.Modules.Flights.Api/
│   │   ├── Travel.Modules.Flights.Application/
│   │   ├── Travel.Modules.Flights.Core/
│   │   ├── Travel.Modules.Flights.Infrastructure/
│   │   ├── ui/                        Angular feature lib (flights)
│   │   └── CLAUDE.md
│   ├── hotels/     (каркас, аналогичная структура)
│   ├── rail/       (каркас)
│   ├── trips/      (каркас)
│   └── identity/
│       ├── Travel.Modules.Identity.Api/
│       ├── Travel.Modules.Identity.Application/
│       ├── Travel.Modules.Identity.Core/
│       ├── Travel.Modules.Identity.Infrastructure/
│       └── CLAUDE.md
│
├── shared/
│   ├── dotnet/
│   │   ├── Travel.Shared.Abstractions/
│   │   ├── Travel.Shared.Infrastructure/
│   │   ├── Travel.Shared.Domain/
│   │   └── Travel.Shared.TestInfrastructure/   IntegrationTestBase + Testcontainers fixtures (только test projects)
│   └── ts/
│       ├── ui-kit/                    Angular shared UI
│       └── api-client/                heyAPI-генерированный TS клиент (настройка в Foundation, генерация — с Flights)
│
├── infra/
│   ├── docker/
│   │   └── docker-compose.production.yml   оверлей к aspire publish output
│   └── keycloak/
│       └── travel-realm.json               минимальный realm для Foundation
│
├── docs/
│   ├── adr/
│   ├── superpowers/specs/
│   ├── ai-conversations/
│   └── blog-template.md
│
├── prompts/
│   └── v1/
│
├── tests/
│   ├── flights/
│   │   ├── Travel.Modules.Flights.Tests.Unit/
│   │   └── Travel.Modules.Flights.Tests.Integration/
│   ├── hotels/
│   ├── rail/
│   ├── trips/
│   ├── identity/
│   ├── Travel.Tests.Architecture/
│   ├── Travel.Tests.Contract/
│   ├── Travel.Tests.AiEvals/
│   └── travel-e2e/
│
├── CLAUDE.md
├── nx.json
├── package.json
├── biome.json
├── .editorconfig
└── Travel.sln
```

**Решения:**
- Модули `hotels`, `rail`, `trips` создаются как каркасы (пустые `.csproj`, per-module CLAUDE.md) — решение и NX знают о них с первого дня.
- `shared/ts/api-client/` — lib создаётся, heyAPI target настраивается, реальная генерация запускается начиная с Flights M1.
- `infra/docker/docker-compose.production.yml` — оверлей с прод-настройками поверх `aspire publish` output.

---

## 3. Aspire AppHost и инфраструктура

`apps/Travel.AppHost/` — оркестратор, `apps/Travel.ServiceDefaults/` — стандартный Aspire шаблон: OpenTelemetry (трейсы, метрики, логи), health checks, service discovery. ServiceDefaults добавляется как reference во все .NET сервисы через `builder.AddServiceDefaults()`.

### Ресурсы AppHost

**Infrastructure resources:**
```
postgres   PostgreSQL 17 + pgvector (image: pgvector/pgvector:pg17)
redis      Redis 7
nats       NATS JetStream (Aspire.Hosting.Nats)
keycloak   Keycloak (Aspire.Hosting.Keycloak), realm из infra/keycloak/travel-realm.json
mailpit    axllent/mailpit (custom container)
```

**Application projects:**
```
host   Travel.Host  (refs: postgres, redis, nats, keycloak, mailpit)
ai     Travel.AI    (refs: postgres, redis, nats)
```

**Local-only observability stack (за флагом `ENABLE_OBSERVABILITY_STACK=true`):**
```
otel-col   OpenTelemetry Collector (fanout: Aspire dashboard + Grafana stack)
grafana    Grafana (provisioned dashboards)
loki       Loki (log backend)
tempo      Tempo (trace backend)
```

По умолчанию (`ENABLE_OBSERVABILITY_STACK` не задан) поднимается только встроенный Aspire OTel dashboard. Полный LGTM-стек — опциональный флаг для локальной разработки.

### Keycloak realm (Foundation минимум)
`infra/keycloak/travel-realm.json` содержит:
- Realm: `travel`
- Client: `travel-web` (public, PKCE, redirect: `http://localhost:4200/*`)
- Один тестовый пользователь: `dev@travel.local` / `dev123`
- Роли: `user`, `admin`

### Конфигурация сервисов
Все connection strings прокидываются через Aspire service discovery. В `appsettings.json` — только структура с плейсхолдерами; реальные значения приходят от Aspire в dev и из переменных окружения на проде.

---

## 4. CI Pipeline

**Триггеры:** push на любую ветку + PR → `master`.

### Jobs

```
Trigger
  │
  ▼
setup
  checkout, Node 22 + .NET 10 SDK
  restore caches: NX (.nx/cache), NuGet (~/.nuget), npm (~/.npm)
  nx affected --base=origin/master → affected project list
  │
  ├──────────────────────────────────────────────────────────┐
  ▼                    ▼                    ▼                ▼
lint                 build               test:unit        test:arch
Biome (ts/js/json)   nx affected:build   nx affected:test  dotnet test
CSharpier (.cs)      .NET + Angular       xUnit v3 + Vitest  --filter Arch
                     OpenAPI diff (Spectral,
                     если затронуты API-проекты)
  └──────────────────────────────────────────────────────────┘
  │  (все четыре параллельно)
  │  (все зелёные)
  │
  ▼  только PR → master
test:e2e
  Playwright против aspire run
  │
  ▼  только push → master
docker:publish
  build + push образов в GHCR
  теги: {sha}, latest
```

### Кэши (GitHub Actions cache)
| Ключ | Путь |
|---|---|
| `nx-{hash(nx.json, package-lock.json)}` | `.nx/cache` |
| `nuget-{hash(**/*.csproj)}` | `~/.nuget/packages` |
| `npm-{hash(package-lock.json)}` | `~/.npm` |

### Детали
- **NX affected base:** `origin/master` для feature-веток; `HEAD~1` для прямых пушей в `master`.
- **ArchUnitNET** выделен в отдельный job — нарушения границ модулей должны быть видны отдельно от юнит-тестов.
- **OpenAPI diff (Spectral)** запускается внутри `build` job, только когда NX affected включает API-проекты.
- **E2E только на PR → master** — требует поднять весь Aspire-стек; слишком дорого для каждой ветки.
- **docker:publish** пушит образы `ghcr.io/{owner}/travel-host` и `ghcr.io/{owner}/travel-ai`. SSH-деплой на VPS — отдельный workflow `deploy.yml`, триггерится вручную или по тегу.
- Без NX Cloud — только GitHub Actions cache.

---

## 5. Devcontainer

`.devcontainer/devcontainer.json`:

```jsonc
{
  "name": "Travel Platform",
  "image": "mcr.microsoft.com/devcontainers/base:ubuntu-24.04",
  "features": {
    "ghcr.io/devcontainers/features/dotnet:2":                    { "version": "10.0" },
    "ghcr.io/devcontainers/features/node:1":                      { "version": "22" },
    "ghcr.io/devcontainers/features/docker-outside-of-docker:1":  {},
    "ghcr.io/devcontainers/features/github-cli:1":                {}
  },
  "postCreateCommand": "npm ci && dotnet restore",
  "forwardPorts": [5000, 5001, 4200, 8080, 8025],
  "customizations": {
    "vscode": {
      "extensions": [
        "ms-dotnettools.csdevkit",
        "nrwl.angular-console",
        "ms-azuretools.vscode-docker",
        "biomejs.biome",
        "csharpier.csharpier-vscode",
        "saoudrizwan.claude-dev"
      ]
    }
  }
}
```

**Решения:**
- **Docker-outside-of-Docker** (shared host socket) — достаточно для Testcontainers + Aspire.
- **Node 22 LTS** — минимум для Angular 21.
- `postCreateCommand` восстанавливает зависимости сразу — первый `aspire run` не ждёт.
- Порты: 5000/5001 (Travel.Host), 4200 (Angular), 8080 (Aspire dashboard), 8025 (Mailpit UI).
- GitHub Codespaces — opt-in, упоминается в README; primary путь — локальный devcontainer.

---

## 6. Стартовый набор ADR

Все ADR в `docs/adr/`, формат: `NNNN-kebab-title.md`.  
Шаблон: Context / Decision / Alternatives Considered / Consequences / References.  
Пробелы в нумерации — резерв для подпроектных ADR.

| # | Файл | Ключевое решение |
|---|---|---|
| 0001 | `modular-monolith.md` | `Travel.Host` — модульный монолит вместо микросервисов; split-readiness без операционной сложности |
| 0002 | `ai-as-extracted-service.md` | `Travel.AI` — отдельный процесс; разный профиль нагрузки, deploy cadence, secrets boundary |
| 0003 | `wolverine-marten-stack.md` | Wolverine + Marten вместо MediatR + Dapper; один автор, бесшовная интеграция, saga + ES из коробки |
| 0004 | `nx-monorepo-tooling.md` | NX 22 + `@nx/dotnet` для смешанного TS+.NET; `@nx-dotnet/core` deprecated с NX 22 |
| 0005 | `frontend-stack.md` | Angular 21 + Signals + httpResource + NgRx SignalStore + Tailwind v4 + PrimeNG unstyled |
| 0006 | `testing-strategy.md` | Семислойная стратегия: unit / integration / architecture / contract / ai-evals / E2E / visual |
| 0007 | `marten-ef-coexistence.md` | Marten и EF Core в одной PostgreSQL; Marten владеет `mt_*`, EF — схемой модуля, миграции независимые |
| 0010 | `keycloak-identity.md` | Keycloak self-hosted (OIDC); email+пароль + Google/GitHub; production-grade из коробки |
| 0011 | `notifications-channels.md` | Два канала: email (MailKit + Mailpit локально) + SSE (`/events/{userId}`) |
| 0012 | `payments-strategy.md` | Duffel test wallet через `IPaymentGateway`; sandbox-only, интерфейс расширяем до Stripe/CloudPayments |
| 0014 | `storage-strategy.md` | Marten для booking lifecycle (selective ES); EF Core для всего остального; polyglot на одной PostgreSQL |
| 0015 | `ui-library-selection.md` | PrimeNG unstyled mode + `tailwindcss-primeui`; comprehensive coverage, zoneless/signal поддержка |
| 0019 | `ai-eval-strategy.md` | Собственный eval framework в .NET вместо Promptfoo (покупка OpenAI, март 2026); живёт в `tests/Travel.Tests.AiEvals/` |
| 0020 | `maf-as-primary-agent-runtime.md` | MAF 1.0 GA — primary для 4 продуктовых агентов; custom runtime только для Travel Advisor (образовательно); фиксируем только стабильные MAF APIs |

---

## 7. AI-Harness

### 7.1 Root `CLAUDE.md`

Rich CLAUDE.md (~150-200 строк) с секциями:

1. **Что это** — одно предложение о проекте и цели
2. **Архитектурная карта** — ASCII-схема `Travel.Host` (модули) → Wolverine/NATS → `Travel.AI`; Aspire оркеструет оба
3. **Stack quick reference** — одна строка на технологию
4. **Карта модулей** — таблица: модуль / bounded context / статус (production-grade / каркас)
5. **Конвенции кода:**
   - Namespace: `Travel.Modules.{Name}.{Layer}` (Core / Application / Infrastructure / Api)
   - Handlers: один класс на файл, суффикс `Handler`
   - Value objects: валидация в конструкторе, `static Result<T> Create(...)` factory
   - Domain events: прошедшее время (`BookingConfirmed`, не `BookingConfirm`)
   - Exceptions: суффикс `Exception`, бросаются только из доменного слоя
6. **Запреты (обязательно к соблюдению):**
   - Модули не импортируют внутренности друг друга — только через `Travel.Shared.Abstractions` или domain events
   - Бизнес-логика не в Infrastructure
   - `catch Exception` без re-throw запрещён
   - Внешние DTO не пересекают границу Infrastructure
   - Без `// TODO` без issue-ссылки
7. **Как запустить:** `aspire run` / `nx serve web` / тесты
8. **AI-harness:** список агентов и команд с однострочным описанием каждого
9. **Ссылки:** `docs/adr/`, `docs/superpowers/specs/`, per-module CLAUDE.md, concept doc

### 7.2 Per-module `CLAUDE.md` (шаблон)

Создаётся в `modules/{name}/CLAUDE.md` для: `flights`, `hotels`, `rail`, `trips`, `identity`, `shared`.

Секции:
1. **Назначение и bounded context** — что делает модуль, ubiquitous language
2. **Агрегаты** — список с state machine если есть
3. **Доменные события** — список
4. **Внешние интеграции** — провайдеры + ACL интерфейсы
5. **Особые конвенции модуля** — если есть отклонения от root CLAUDE.md
6. **Тесты** — пути к тестовым проектам

Foundation заполняет per-module CLAUDE.md для `identity` и `shared` содержательно. Для `flights`, `hotels`, `rail`, `trips` — создаёт файлы с пометкой "каркас, заполняется в подпроекте N".

### 7.3 Кастомные агенты

#### `.claude/agents/domain-modeler.md`

```markdown
---
name: domain-modeler
description: DDD artifact generator — given a feature request or domain description, produces aggregates, value objects, domain events, and ubiquitous language
---

You are a Domain-Driven Design expert working on the Travel platform (travel-agency repo).

## Your task
Given a feature description or user story, produce DDD artifacts for the relevant bounded context.

## Before you start
1. Read the per-module CLAUDE.md for the relevant module (modules/{name}/CLAUDE.md)
2. Read docs/adr/0001-modular-monolith.md for module boundary rules
3. Scan existing aggregates in the module's core/ directory to maintain consistency

## Output format

### Aggregates
For each aggregate:
- Name (PascalCase)
- Identity type (value object)
- State machine: states + valid transitions
- Invariants (business rules the aggregate enforces — not policies)
- Fields with types

### Value Objects
For each:
- Name
- Fields
- Validation rules (what makes it invalid — tested exhaustively)
- Example valid and invalid values

### Domain Events
For each:
- Name (past tense: BookingConfirmed, not ConfirmBooking)
- Trigger (what action raises it)
- Payload (fields)

### Ubiquitous Language
| Term | Definition | Notes |

### Bounded Context Notes
- What belongs in this module vs others
- Cross-module communication needed (events or shared contracts)

## Rules
- Use the module's ubiquitous language — no generic terms if a domain term exists
- Mark uncertain decisions with ⚠️ requires discussion
- Do NOT design database schemas — that belongs in Infrastructure
- Aggregates enforce invariants, not policies; if a rule can be violated by design it is a policy
- Keep aggregates small; prefer multiple small aggregates over one large one
```

#### `.claude/agents/adr-writer.md`

```markdown
---
name: adr-writer
description: Writes Architecture Decision Records in the project format
---

You are an architecture documentation expert working on the Travel platform.

## Your task
Write a complete ADR for an architectural decision.

## Before you start
1. Read existing ADRs in docs/adr/ to match the style and find the next available number
2. Read CLAUDE.md (root) for project context
3. Read the concept doc at docs/superpowers/specs/2026-05-03-travel-platform-concept.md if the decision touches the north star

## ADR format
File: docs/adr/NNNN-kebab-title.md

---
# NNNN. Title

**Date:** YYYY-MM-DD
**Status:** Accepted
**Deciders:** [who was involved]

## Context
[The problem and forces at play. What makes this decision necessary NOW.]

## Decision
[What we decided. One clear paragraph.]

## Alternatives Considered

### Option A: [name]
[Description + why rejected]

### Option B: [name]
[Description + why rejected]

## Consequences

### Positive
- [benefit]

### Negative / Trade-offs
- [cost or risk — every real decision has at least one]

### Neutral
- [observation]

## Out of Scope
[What this ADR explicitly does NOT decide]

## References
- [links to specs, concept doc, or external resources]
---

## Rules
- Be specific: "we chose X because Y" not "X was chosen"
- Every ADR must list at least one negative consequence
- The Out of Scope section prevents scope creep in future debates
- After writing the ADR, ask if it should be committed to git
```

#### `.claude/agents/test-author.md`

```markdown
---
name: test-author
description: Writes comprehensive tests for .NET and TypeScript code following the project's seven-layer testing strategy
---

You are a test engineering expert working on the Travel platform.

## Your task
Write tests for the provided source file(s).

## Before you start
1. Read docs/adr/0006-testing-strategy.md
2. Identify test type: unit (no I/O) or integration (real infrastructure)
3. Read the module's CLAUDE.md for domain context
4. Check if tests already exist — extend, don't duplicate

## Test placement
- Unit tests:        tests/{module}/Travel.Modules.{X}.Tests.Unit/
- Integration tests: tests/{module}/Travel.Modules.{X}.Tests.Integration/

## .NET tests (xUnit v3)

### Unit tests
- One test class per class under test
- Naming: `{Method}_{Scenario}_{ExpectedResult}`
- Use Verify for complex object assertions (snapshot testing)
- Value objects: test all valid inputs + each invalid input separately
- Domain logic: test all paths, all invariants
- Add `[Trait("Category", "Unit")]` to every test class

### Integration tests
- Inherit `IntegrationTestBase` (shared PostgreSQL + Redis via Testcontainers)
- Test Wolverine handlers end-to-end: dispatch → verify DB side effects
- Test Marten projections: append events → verify read model
- Never mock the database
- Add `[Trait("Category", "Integration")]` to every test class

## TypeScript tests (Vitest)
- One spec file per source file
- Use describe/it blocks
- Test behavior, not implementation details
- Mock HTTP at the fetch/HttpClient boundary, not at the domain level

## Rules
- No mocking of domain objects
- No mocking of the database in integration tests
- External HTTP: mock at HttpClient level only
- Cover: happy path + every validation failure + every domain exception
- Never use `Thread.Sleep` or arbitrary delays — use proper async patterns
```

#### `.claude/agents/migration-author.md`

```markdown
---
name: migration-author
description: Writes safe EF Core migrations given model changes, with safety checks
---

You are a database migration expert working on the Travel platform.

## Your task
Write an EF Core migration for the provided model changes.

## Before you start
1. Read docs/adr/0007-marten-ef-coexistence.md
2. Identify which module's DbContext owns the changed entity
3. Read the current migration history for that module

## Safety checklist — verify ALL before writing

- NOT NULL column on existing table → requires DEFAULT value or two-step migration (add nullable → backfill → add NOT NULL constraint)
- Dropping column → verify no code references it first; consider soft-delete pattern
- Rename → prefer add+copy+drop in separate migrations to avoid data loss
- Large table index → use `CREATE INDEX CONCURRENTLY` via raw SQL (EF cannot generate this)
- Never touch `mt_*` tables — Marten owns those; modifying them will corrupt event store

## Migration commands
```
dotnet ef migrations add {Name} \
  --project modules/{name}/Travel.Modules.{Name}.Infrastructure \
  --startup-project apps/Travel.Host \
  --output-dir Migrations
```

## Output format
1. Show the generated Up() and Down() SQL
2. Flag any safety concerns explicitly
3. Provide the exact dotnet ef command to apply
4. Ask if you should run the migration or if the user will handle it

## Rules
- Always include a meaningful Down migration
- Add a SQL comment in the migration explaining WHY it exists
- One migration per logical change — don't bundle unrelated schema changes
```

#### `.claude/agents/integration-mapper.md`

```markdown
---
name: integration-mapper
description: Designs Anti-Corruption Layer (ACL) for external API integrations — interface, DTOs, adapter skeleton, mapping notes
---

You are an integration architecture expert working on the Travel platform.

## Your task
Given an external API spec or description, design the full ACL for integrating it into the correct module.

## Before you start
1. Read the relevant per-module CLAUDE.md to understand domain types
2. Read docs/adr/0001-modular-monolith.md for layer rules
3. Scan existing provider implementations for style reference:
   - modules/rail/Travel.Modules.Rail.Infrastructure/Providers/ (if exists)
   - modules/flights/Travel.Modules.Flights.Infrastructure/Providers/ (if exists)

## Output: four artifacts

### 1. Provider interface (Core layer)
Location: modules/{name}/Travel.Modules.{Name}.Core/Providers/I{ProviderName}Provider.cs

Rules:
- Use domain types only — no external DTO types in the interface signature
- Return Result<T> — no exceptions in interface contracts
- CancellationToken on every async method
- Name methods after domain intent, not HTTP verbs (SearchRoutesAsync, not GetV1ScheduleAsync)

### 2. External DTOs (Infrastructure layer)
Location: modules/{name}/Travel.Modules.{Name}.Infrastructure/Providers/{ProviderName}/Dto/

Rules:
- Mirror the external API's shape exactly
- No domain logic or validation here
- Naming: {ProviderName}{EntityName}Dto (e.g., YandexRaspStationDto)
- Annotate with [JsonPropertyName] if the API uses snake_case or non-standard casing

### 3. Adapter (Infrastructure layer)
Location: modules/{name}/Travel.Modules.{Name}.Infrastructure/Providers/{ProviderName}/{ProviderName}Adapter.cs

Rules:
- Implements the Core interface
- Maps external DTOs → domain types (private mapping methods)
- Translates HTTP errors → domain Result errors
- Applies TOS constraints (caching TTL, rate limit retry)
- Registered in DI as the implementation of the Core interface

### 4. Mapping notes
- List any lossy mappings (external field has no domain equivalent)
- Flag fields requiring business rules during mapping
- Document TOS constraints (Yandex.Rasp: no persistent caching of raw responses, attribution required)
- Note rate limits and caching strategy

## Rules
- External types NEVER cross into Core or Application layers
- All HTTP calls via IHttpClientFactory (typed client, registered in DI)
- Rate limit handling in the adapter, not the domain
- If the provider requires attribution in UI (e.g., Yandex.Rasp), document it in the mapping notes and flag it for the UI layer
```

### 7.4 Slash Commands

#### `.claude/commands/spec.md`

```markdown
When this command is invoked, run a brainstorming cycle for a new feature or subproject spec.

Steps:
1. Ask: "What feature or subproject do you want to spec?" — one sentence answer
2. Read: CLAUDE.md (root), relevant per-module CLAUDE.md, concept doc at docs/superpowers/specs/2026-05-03-travel-platform-concept.md
3. Ask clarifying questions ONE AT A TIME: purpose, scope, constraints, success criteria
4. Propose 2-3 implementation approaches with trade-offs and a recommendation
5. Present the design section by section, asking "ok?" after each
6. Write the spec to docs/superpowers/specs/YYYY-MM-DD-{topic}-design.md
7. Run spec self-review: placeholders, contradictions, ambiguity, scope
8. Commit: git add docs/superpowers/specs/... && git commit -m "docs: add spec {topic}"
9. Ask user to review before transitioning to implementation

Rules:
- One question at a time — never ask multiple questions in one message
- No implementation until spec is written and user approves
- Every spec must reference the concept doc and not contradict it; if it does, flag the contradiction
```

#### `.claude/commands/adr.md`

```markdown
When this command is invoked, create an ADR for a recent or specified architectural decision.

Steps:
1. If the user provided a topic: go to step 3
2. Run: git diff HEAD~5 --name-only — identify what architectural change was made; propose a topic
3. Confirm the topic with the user in one sentence: "I'll write an ADR about X — does that sound right?"
4. Invoke the adr-writer agent with the confirmed topic
5. Present the drafted ADR to the user
6. On approval: write the file to docs/adr/NNNN-kebab-title.md and commit:
   git add docs/adr/NNNN-*.md && git commit -m "docs: add ADR NNNN - {title}"
```

#### `.claude/commands/explore-domain.md`

```markdown
When this command is invoked, explore the current state of a domain module and produce a summary card.

Steps:
1. Identify the module: use argument if provided, otherwise ask "Which module? (flights / hotels / rail / trips / identity)"
2. Read: modules/{name}/CLAUDE.md
3. Scan and summarize:
   - Aggregates in modules/{name}/Travel.Modules.{Name}.Core/ — list with state machines
   - Value Objects in modules/{name}/Travel.Modules.{Name}.Core/
   - Domain Events in modules/{name}/Travel.Modules.{Name}.Core/
   - Wolverine Handlers in modules/{name}/Travel.Modules.{Name}.Application/
   - Provider interfaces in modules/{name}/Travel.Modules.{Name}.Core/Providers/
4. Check test coverage:
   - Unit tests in tests/{name}/...Tests.Unit/ — which handlers/aggregates are covered?
   - Integration tests in tests/{name}/...Tests.Integration/
5. Output:

---
## {ModuleName} — Current State

### Aggregates
[list with states]

### Value Objects
[list]

### Domain Events
[list]

### Handlers
| Handler | Type | Tests |
|---------|------|-------|
| FooHandler | Command | ✅ |
| BarHandler | Query   | ❌ |

### Providers
[list]

### TODO
[handlers without tests, stubs not yet implemented, missing value objects]
---
```

#### `.claude/commands/test-this.md`

```markdown
When this command is invoked, write tests for the current file.

Steps:
1. Identify target:
   - If a file path is provided as argument: use it
   - Otherwise: use the most recently edited .cs or .ts file (git diff HEAD --name-only | head -1)
   - If still unclear: ask "Which file should I write tests for?"
2. Read the file
3. Determine test type:
   - Value object, aggregate, domain service → unit test
   - Wolverine handler, EF query, Marten projection, provider adapter → integration test
   - Angular component, service → Vitest unit test
4. Invoke the test-author agent with the file path and determined test type
5. Show the generated test file content to the user before writing
6. On approval: write to the correct test project path
```

#### `.claude/commands/integration-from-openapi.md`

```markdown
When this command is invoked, design an ACL for an external API provider.

Steps:
1. Ask: "Which module and provider?" (e.g., "Rail module, Yandex.Rasp API")
2. Ask: "Do you have an OpenAPI spec file path, or should I work from a description?"
   - If spec file path: read the file
   - If description: ask "Describe the key endpoints we need (method, path, request/response shape)"
3. Invoke the integration-mapper agent with: module name, provider name, gathered spec/description
4. Present the four artifacts for review:
   - Provider interface
   - External DTOs
   - Adapter skeleton
   - Mapping notes (including TOS constraints)
5. On approval: write files to the correct locations:
   - modules/{name}/Travel.Modules.{Name}.Core/Providers/I{Provider}Provider.cs
   - modules/{name}/Travel.Modules.{Name}.Infrastructure/Providers/{Provider}/Dto/*.cs
   - modules/{name}/Travel.Modules.{Name}.Infrastructure/Providers/{Provider}/{Provider}Adapter.cs
```

### 7.5 Hooks (`.claude/settings.json`)

```jsonc
{
  "hooks": {
    "PostToolUse": [
      {
        "matcher": "Write|Edit",
        "hooks": [
          {
            "type": "command",
            "command": "node -e \"const f=process.env.CLAUDE_TOOL_INPUT_FILE_PATH||''; if(f.endsWith('.cs')) { try { require('child_process').execSync('dotnet csharpier '+JSON.stringify(f), {stdio:'ignore'}) } catch(e){} }\""
          },
          {
            "type": "command",
            "command": "node -e \"const f=process.env.CLAUDE_TOOL_INPUT_FILE_PATH||''; if(/\\.(ts|tsx|js|json)$/.test(f)) { try { require('child_process').execSync('npx biome format --write '+JSON.stringify(f), {stdio:'ignore'}) } catch(e){} }\""
          }
        ]
      }
    ],
    "Stop": [
      {
        "matcher": ".*",
        "hooks": [
          {
            "type": "command",
            "command": "node -e \"const edits=process.env.CLAUDE_RECENT_EDITS||''; if(edits.includes('/modules/') && edits.includes('.cs')) process.stdout.write('\\n⚠️  Изменены файлы в modules/ — запусти arch-тесты: dotnet test tests/Travel.Tests.Architecture --no-build\\n');\""
          }
        ]
      }
    ]
  }
}
```

Три хука:
1. **CSharpier on save** — форматирует `.cs` файлы сразу после Write/Edit (silent fail если CSharpier не установлен)
2. **Biome on save** — форматирует `.ts/.tsx/.js/.json` файлы
3. **Arch reminder on stop** — если в сессии изменялись `.cs` в `modules/`, напоминает запустить ArchUnitNET

---

## 8. Тестовая инфраструктура

### Структура

```
tests/
  flights/
    Travel.Modules.Flights.Tests.Unit/
    Travel.Modules.Flights.Tests.Integration/
  hotels/
    Travel.Modules.Hotels.Tests.Unit/
    Travel.Modules.Hotels.Tests.Integration/
  rail/
    Travel.Modules.Rail.Tests.Unit/
    Travel.Modules.Rail.Tests.Integration/
  trips/
    Travel.Modules.Trips.Tests.Unit/
    Travel.Modules.Trips.Tests.Integration/
  identity/
    Travel.Modules.Identity.Tests.Unit/
    Travel.Modules.Identity.Tests.Integration/
  Travel.Tests.Architecture/
  Travel.Tests.Contract/
  Travel.Tests.AiEvals/
  travel-e2e/
```

### Что Foundation создаёт содержательно

**`Travel.Tests.Architecture/`** — три файла с реальными правилами (работают даже на пустых модулях):

- `ModuleBoundaryTests.cs` — ни один модуль не импортирует внутренности другого; единственная разрешённая зависимость между модулями — `Travel.Shared.Abstractions`
- `DependencyDirectionTests.cs` — `Core` не зависит от `Infrastructure`; `Application` не зависит от `Api`; `Infrastructure` не зависит от `Api`
- `NamingConventionTests.cs` — классы в `/Handlers/` заканчиваются на `Handler`; классы в `/Exceptions/` заканчиваются на `Exception`; value objects имеют статический `Create` метод

Все тесты имеют `[Trait("Category", "Architecture")]`.

**Shared integration test infrastructure** в `Travel.Tests.Integration` (базовый класс, на который ссылаются все модульные integration-проекты):

```csharp
// Travel.Shared.TestInfrastructure (отдельный проект в shared/dotnet/)
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected PostgreSqlContainer Postgres { get; }
    protected IServiceProvider Services { get; }
    // Setup: поднять контейнер, применить EF миграции, настроить DI
    // TearDown: остановить контейнер
}
```

**`travel-e2e/`** — один тест `health.spec.ts` для вертикального среза (см. секцию 9).

### Что остаётся пустым до подпроектов
- `flights/`, `hotels/`, `rail/`, `trips/`, `identity/` тестовые проекты — структура папок создана, `.csproj` есть, тестов нет
- `Travel.Tests.Contract/` — пустой проект, Pact подключается в подпроекте 5
- `Travel.Tests.AiEvals/` — пустой проект, eval framework подключается в подпроекте 1

---

## 9. Вертикальный срез (E2E baseline)

Один маршрут `GET /api/status` — доказывает что весь стек срастается.

### Путь запроса

```
Angular StatusPageComponent
  httpResource(() => api.getStatus())
    → HTTP GET /api/status
      → Travel.Host REPR endpoint
        → Wolverine dispatch: GetStatusQuery
          → GetStatusQueryHandler
            → EF Core: SELECT current_setting('server_version'), NOW()
              → PostgreSQL
            ← { version, db: "ok", timestamp }
          ← StatusResponse
        ← 200 OK { version: "1.0.0", db: "ok", timestamp: "..." }
      ← JSON
    ← StatusResponse
  ← отображает "DB: ok, v1.0.0"
```

### Создаваемые артефакты

**Backend (`apps/Travel.Host/`):**
- `GetStatusQuery.cs` + `GetStatusQueryHandler.cs` — Wolverine query, проверяет доступность PostgreSQL через EF
- `StatusEndpoint.cs` — REPR endpoint, маппит на `GET /api/status`
- `StatusResponse.cs` — record `{ string Version, string Db, DateTimeOffset Timestamp }`

**Shared Infrastructure (`shared/dotnet/Travel.Shared.Infrastructure/Endpoints/`):**
- `IEndpoint.cs` — интерфейс с `void MapEndpoint(IEndpointRouteBuilder app)`
- `EndpointExtensions.cs` — `MapEndpoints()` extension, сканирует сборку и регистрирует все `IEndpoint`

**Frontend (`apps/web/src/app/status/`):**
- `status-page.component.ts` — standalone компонент, использует `httpResource(() => this.api.getStatus())`
- Маршрут `/status` добавляется в app routes

**API client (`shared/ts/api-client/`):**
- `status.client.ts` — написан вручную (один метод `getStatus(): Promise<StatusResponse>`)

**E2E (`tests/travel-e2e/specs/health.spec.ts`):**
```typescript
test('status page shows db ok', async ({ page }) => {
  await page.goto('/status');
  await expect(page.getByText('db: ok')).toBeVisible();
  await expect(page.getByText('1.0.0')).toBeVisible();
});
```

### Почему именно так
- Wolverine handler без Marten — `SELECT` достаточен, не усложняем
- REPR endpoint framework пишется минимально (ровно для одного endpoint), расширяется в Flights
- `httpResource` — тот же паттерн что будет везде в проекте

---

## 10. Implementation Notes

- **Порядок реализации:** структура репозитория → Travel.sln и NX workspace → Aspire AppHost → shared инфраструктура → модули-каркасы → CI → devcontainer → ADR-набор → AI-harness → вертикальный срез → E2E тест
- **NX targets для .NET:** `build`, `test`, `lint` (CSharpier check) — настраиваются в `project.json` каждого .NET проекта через `@nx/dotnet`
- **`Travel.sln`** включает все `.csproj` из `apps/`, `modules/`, `shared/dotnet/`, `tests/` — один solution для IDE
- **Aspire ServiceDefaults** добавляется как `<ProjectReference>` в `Travel.Host` и `Travel.AI`; не добавляется в модульные проекты напрямую
- **pgvector расширение:** при старте `Travel.AppHost` PostgreSQL контейнер поднимается с образом `pgvector/pgvector:pg17`; расширение активируется через `CREATE EXTENSION IF NOT EXISTS vector` в начальной миграции
- **Keycloak realm import:** `travel-realm.json` монтируется в контейнер через Aspire volume; Keycloak импортирует realm при старте если он ещё не существует
