# Shared infrastructure

**Status:** Foundation-implemented

## What lives here
- `Travel.Shared.Abstractions`       — `IDomainEvent`, `IModuleAssemblyMarker` (no package deps)
- `Travel.Shared.Domain`             — `AggregateRoot<TId>`, `Entity<TId>` (identity equality via `class`)
- `Travel.Shared.Infrastructure`     — `IInitializer` module bootstrap pattern (no AspNetCore deps)
- `Travel.Shared.Web`                — `ErrorOrExtensions` (ErrorOr → ProblemDetails). Only `apps/*` and `Modules.*.Api` may reference this.
- `Travel.Shared.TestInfrastructure` — `IntegrationTestBase` with Testcontainers PostgreSQL fixture

## Conventions
- **No business logic.** Only abstractions and infrastructure primitives.
- **No module dependencies.** Any module may depend on shared; shared may not depend on any module.
- **Value objects are plain C# `record` types.** No base class — `record` already provides structural equality. If an architecture test ever needs to enforce "VO lives in `*/ValueObjects/*` namespace", add a marker interface then; do not pre-introduce inheritance.
- **Entities use `class`, not `record`.** Identity equality (by `Id`) is fundamentally different from value equality — `record` is the wrong primitive for entities.
- **Time is always injected via `TimeProvider`.** No `DateTime.UtcNow` / `DateTimeOffset.UtcNow` calls in production code. Tests use `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`.
- **Result type is `ErrorOr<T>`.** Imported locally (`using ErrorOr;`), not via global using — keeps the coupling visible.
