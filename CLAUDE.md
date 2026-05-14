# Travel Platform — Foundation

Travel Platform is a modular-monolith travel-agency backend (Subproject 0) built on .NET 10 + Aspire, exposing bookable flights, hotels, rail schedules, and AI-assisted trip planning through a single deployable host while keeping module boundaries strictly isolated.

---

## Architecture Map

```
                         ┌─────────────────────────────────────────┐
                         │           Travel.AppHost (Aspire)        │
                         │  orchestrates all services + resources   │
                         └────────────────┬────────────────────────┘
                                          │
               ┌──────────────────────────▼────────────────────────────┐
               │                   Travel.Host                         │
               │              (modular monolith, .NET 10)              │
               │                                                       │
               │  ┌──────────┐  ┌──────────┐  ┌────────┐  ┌───────┐  │
               │  │ Flights  │  │  Hotels  │  │  Rail  │  │ Trips │  │
               │  └────┬─────┘  └────┬─────┘  └───┬────┘  └───┬───┘  │
               │       └─────────────┴─────────────┴───────────┘      │
               │                     │ Wolverine / NATS                │
               │              ┌──────▼──────┐                         │
               │              │  Identity   │  (Keycloak OIDC)         │
               │              └─────────────┘                         │
               └───────────────────────────────────────────────────────┘
                                          │ Wolverine / NATS
                         ┌────────────────▼────────────────┐
                         │           Travel.AI              │
                         │  (.NET 10, MAF agents, Semantic  │
                         │   Kernel, AI evaluation)         │
                         └─────────────────────────────────┘

  Angular 21 SPA (apps/web) ──── REST / WolverineFx.Http ──── Travel.Host
```

---

## Stack Quick Reference

| Layer | Technology |
|---|---|
| Runtime | .NET 10 |
| Orchestration | .NET Aspire (AppHost) |
| Messaging / CQRS | WolverineFx (in-process + NATS) |
| Event store | Marten (Walsingham + PostgreSQL) |
| Relational ORM | EF Core 10 (non-event tables) |
| Identity | Keycloak 25 (OIDC, JWT bearer) |
| Result type | ErrorOr v2 |
| Frontend | Angular 21 + NX 22 |
| Frontend test | Vitest + Playwright |
| .NET test | xUnit v3 + Testcontainers |
| Arch tests | ArchUnitNET |
| Formatter (.cs) | CSharpier |
| Formatter (ts/js) | Biome |
| Containerisation | Docker / devcontainer |
| CI | GitHub Actions |

---

## Module Map

| Module | Bounded Context | Status |
|---|---|---|
| `flights` | Search & booking of air tickets (Duffel + Travelpayouts) | scaffold — production in Subproject 1 |
| `hotels` | Hotel search & booking (multi-supplier) | scaffold — production in Subproject 2 |
| `rail` | Read-only rail schedules (Yandex.Rasp + DB open data) | scaffold — production in Subproject 3 |
| `trips` | Trip planning composite + AI itineraries | scaffold — production in Subproject 4 |
| `identity` | Authentication/authorization via Keycloak OIDC | Foundation-implemented |
| `shared` | Shared abstractions, domain primitives, test infra | Foundation-implemented |

Per-module details: `modules/{name}/CLAUDE.md`

---

## Code Conventions

### Namespaces
```
Travel.Modules.{Name}.Core            — aggregates, value objects, domain events, provider interfaces
Travel.Modules.{Name}.Application     — Wolverine handlers (commands + queries)
Travel.Modules.{Name}.Infrastructure  — EF Core DbContext, Marten config, provider adapters, migrations
Travel.Modules.{Name}.Api             — WolverineFx.Http endpoints, request/response DTOs
```

### Handlers
- One class per file, class name ends with `Handler`
- File path mirrors namespace: `Application/Handlers/FooCommandHandler.cs`
- Decorated with `[WolverineHandler]` or implements `ICommandHandler<T>` / `IQueryHandler<T, R>`

### Value Objects
- Plain C# `record` (structural equality built-in, no base class)
- Validation in private constructor; expose via `static Result<T> Create(...)`
- Live in `Core/ValueObjects/`

### Domain Events
- Past tense: `BookingConfirmed`, not `ConfirmBooking` or `BookingConfirm`
- Implement `IDomainEvent` from `Travel.Shared.Abstractions`
- Live in `Core/DomainEvents/`

### Exceptions
- Suffix `Exception` (e.g., `InvalidBookingStateException`)
- Thrown only from the Core (domain) layer
- Infrastructure and Application layers translate exceptions to `ErrorOr` errors

### Time
- Always injected via `TimeProvider` — never call `DateTime.UtcNow` / `DateTimeOffset.UtcNow` in production code
- Tests use `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`

---

## Forbidden Practices

1. **No cross-module internal imports** — modules communicate only through `Travel.Shared.Abstractions` types or by publishing/consuming domain events via Wolverine. Never reference `Travel.Modules.X.*` from module Y.
2. **No business logic in Infrastructure** — Infrastructure holds persistence adapters; all invariants and rules belong in Core.
3. **No `catch Exception` without re-throw** — swallowing exceptions silently is forbidden. Either re-throw, log + re-throw, or convert to a typed `Error`.
4. **No external DTOs crossing the Infrastructure boundary** — provider response shapes must be mapped to domain types inside the adapter.
5. **No `// TODO` without an issue link** — every unresolved TODO must reference a GitHub issue (`// TODO: #123 ...`).

---

## How to Run

### Full stack (Aspire)
```bash
dotnet run --project apps/Travel.AppHost
```
Aspire dashboard: `http://localhost:15888`

### Frontend only
```bash
npx nx serve web
```
App: `http://localhost:4200`

### .NET tests
```bash
# All tests
dotnet test Travel.slnx

# Architecture tests only
dotnet test tests/Travel.Tests.Architecture --no-build

# Integration tests only (requires Docker for Testcontainers)
dotnet test Travel.slnx --filter "Category=Integration"
```

### Frontend tests
```bash
npx nx run-many -t test          # Vitest unit tests
npx nx run travel-e2e:e2e        # Playwright E2E
```

### Format
```bash
dotnet csharpier .               # format all .cs files
npx biome format --write .       # format all .ts/.js/.json files
```

---

## AI-Harness

### Custom Agents (`.claude/agents/`)

| Agent | Purpose |
|---|---|
| `domain-modeler` | Given a feature or user story, produces aggregates, value objects, domain events, ubiquitous language |
| `adr-writer` | Writes a complete Architecture Decision Record in the project format |
| `test-author` | Writes .NET and TypeScript tests following the 7-layer testing strategy |
| `migration-author` | Writes safe EF Core migrations with a safety checklist |
| `integration-mapper` | Designs ACL (interface + DTOs + adapter) for an external API |

### Slash Commands (`.claude/commands/`)

| Command | Purpose |
|---|---|
| `/spec` | Brainstorms and writes a new feature/subproject design spec |
| `/adr` | Creates an ADR for a recent or specified architectural decision |
| `/explore-domain` | Produces a current-state summary card for a domain module |
| `/test-this` | Writes tests for the current or specified file |
| `/integration-from-openapi` | Designs ACL for an external API from an OpenAPI spec or description |

---

## Links

| Resource | Path |
|---|---|
| Architecture Decision Records | `docs/adr/` (12 ADRs) |
| Foundation design spec | `docs/superpowers/specs/2026-05-04-foundation-design.md` |
| Travel Platform concept | `docs/superpowers/specs/2026-05-03-travel-platform-concept.md` |
| Foundation plan | `docs/superpowers/plans/2026-05-04-foundation.md` |
| Per-module CLAUDE.md files | `modules/flights/CLAUDE.md`, `modules/hotels/CLAUDE.md`, `modules/rail/CLAUDE.md`, `modules/trips/CLAUDE.md`, `modules/identity/CLAUDE.md`, `modules/shared/CLAUDE.md` |
| Shared infrastructure | `shared/CLAUDE.md` |
