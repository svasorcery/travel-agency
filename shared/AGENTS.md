# Shared layers

## Ownership

- `Travel.Shared.Abstractions`: `IDomainEvent`, `IModuleAssemblyMarker`, `EquatableArray<T>`, and `[TestOnly]`; it has no package dependencies.
- `Travel.Shared.Domain`: `AggregateRoot<TId>` and `Entity<TId>`. Do not claim Flights currently derives from them.
- `Travel.Shared.Infrastructure`: generic ordered initialization and readiness primitives. Host and Travel.AI register real schema initializers; Development/Testing can apply checked-in migrations, while Production startup validates schema compatibility.
- `Travel.Shared.Web`: ErrorOr/ProblemDetails and claims/request helpers; no business logic. `TestOnlyGuard` belongs to `Travel.ServiceDefaults.Hosting`.
- `Travel.Shared.TestInfrastructure`: PostgreSQL-only `IntegrationTestBase` and `StubOptionsMonitor<T>`; it does not provide Redis, NATS, or Keycloak.

Shared never imports modules and does not own domain rules. `ErrorOr` is not a global using.

`Travel.IntegrationContracts.Flights` publishes only `IFlightsSupportOrderAccess` and metadata `FlightOrderOwnershipProof` for the retained Support consumer. The leaf depends on BCL and ErrorOr; Flights Application produces source-stream ownership/version proof, Flights Composition registers it, Support Application consumes it. Core, Host, Shared and non-Composition Api may not consume module internals through this seam. No passenger, provider credential, payment/refund or projection DTO belongs to this contract.
