# Shared layers

## Ownership

- `Travel.Shared.Abstractions`: `IDomainEvent`, `IModuleAssemblyMarker`, `EquatableArray<T>`, and `[TestOnly]`; it has no package dependencies.
- `Travel.Shared.Domain`: `AggregateRoot<TId>` and `Entity<TId>`. Do not claim Flights currently derives from them.
- `Travel.Shared.Infrastructure`: generic initialization primitives only. No real module initializer is currently registered, so do not promise automatic bootstrap or migrations.
- `Travel.Shared.Web`: ErrorOr/ProblemDetails, claims/request helpers, and `TestOnlyGuard`; no business logic.
- `Travel.Shared.TestInfrastructure`: PostgreSQL-only `IntegrationTestBase` and `StubOptionsMonitor<T>`; it does not provide Redis, NATS, or Keycloak.

Shared never imports modules and does not own domain rules. `ErrorOr` is not a global using.
