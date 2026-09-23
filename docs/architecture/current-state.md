# Travel Platform current architecture

This page records the code and tests at the WS5 source review. It describes a local code demo. The [remediation design](../superpowers/specs/2026-08-11-ai-harness-architecture-remediation-design.md) defines the intended boundaries; the source links below show which parts are implemented.

## Processes and module graph

    Travel.AppHost
      ├── Travel.Host
      │     ├── Identity.Api.Composition -> JWT/Keycloak infrastructure
      │     └── Flights.Api.Composition -> search, booking, webhook, projection
      └── Travel.AI -> Flights NL-search -> Anthropic IChatClient

[AppHost](../../apps/Travel.AppHost/Program.cs) orchestrates resources and processes. [Travel.Host](../../apps/Travel.Host/Program.cs) owns the only process-wide Marten builder, Wolverine builder, NATS/durability configuration, global authentication/authorization order and Wolverine endpoint mapping. It calls the enabled [Flights facade](../../modules/flights/Travel.Modules.Flights.Api/Composition/FlightsModule.cs) and [Identity facade](../../modules/identity/Travel.Modules.Identity.Api/Composition/IdentityModule.cs). The Host project and type guards enforce that boundary.

| Module | Current state | Code evidence |
|---|---|---|
| Flights | M1 backend: search, quote/hold/confirm/cancel, webhook, read model and notifications | [Api facade](../../modules/flights/Travel.Modules.Flights.Api/Composition/FlightsModule.cs), [booking aggregate](../../modules/flights/Travel.Modules.Flights.Core/Aggregates/BookingAggregate.cs) |
| Identity | Thin JWT/Keycloak integration; no full identity domain | [Api facade](../../modules/identity/Travel.Modules.Identity.Api/Composition/IdentityModule.cs), [infrastructure](../../modules/identity/Travel.Modules.Identity.Infrastructure/IdentityServiceCollectionExtensions.cs) |
| Hotels | Four scaffold projects; no runtime composition | [module instructions](../../modules/hotels/AGENTS.md) |
| Rail | Four scaffold projects; no runtime composition | [module instructions](../../modules/rail/AGENTS.md) |
| Trips | Four scaffold projects; no runtime composition or TripAggregate | [module instructions](../../modules/trips/AGENTS.md) |

The [architecture suite](../../tests/Travel.Tests.Architecture/) checks the exact 20-project inventory, all ordered module pairs, both evaluated MSBuild configurations, type references including method bodies, layer direction, the exact Api.Composition exception, Shared isolation and approved AI-contract consumers. Flights and the implemented Identity areas require nonempty selectors. Scaffold projects remain in the project graph checks even when their assemblies have only markers.

## Data and messaging

A booking command applies the [typed Core transition](../../modules/flights/Travel.Modules.Flights.Core/Aggregates/BookingAggregate.cs), then the application writer commits the Marten event, a ReconcileOrderReadModel message and sibling notifications in one enrolled outbox. The [durable reconciler](../../modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/OrderReadModelReconciler.cs) applies stream events after the persisted EF ProjectedStreamVersion. GET/List can briefly lag a successful command. Retry, dead-letter diagnostics, exclusive maintenance and rebuild/validation are described in [the recovery runbook](../operations/booking-read-model-recovery.md) and [ADR 0016](../adr/0016-booking-saga-via-marten-es.md). No mixed old projector is permitted.

[FlightsDbContext](../../modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/FlightsDbContext.cs) owns the flights read model, idempotency and webhook inbox. [AiDbContext](../../apps/Travel.AI/Persistence/AiDbContext.cs) currently owns the AI cost ledger. Shared Infrastructure supplies ordered initialization primitives; Host and AI register actual initializers. Development/Testing apply checked-in schema changes before readiness; Production startup validates compatibility and does not apply migrations. See [ADR 0007](../adr/0007-storage-strategy-marten-ef-coexistence.md).

Interactive NL-search sends the versioned [shared contract](../../shared/dotnet/Travel.IntegrationContracts.AI/NlSearch/NlSearchContracts.cs) through bounded Core NATS request/reply. [Travel.AI](../../apps/Travel.AI/Program.cs) registers AnthropicClient.AsIChatClient directly; it has no Microsoft Agent Framework or Semantic Kernel runtime. Wolverine/PostgreSQL supplies durable local queues and the booking outbox. JetStream availability on the broker does not make this NL-search request durable. [ADR 0020](../adr/0020-nl-search-cross-service-contract.md) owns that transport decision; [ADR 0012](../adr/0012-maf-as-primary-agent-runtime.md) defers a future MAF product milestone.

## HTTP, health and demo proof

Flights HTTP routes and request DTOs live in [Api Endpoints](../../modules/flights/Travel.Modules.Flights.Api/Endpoints/) and [Contracts](../../modules/flights/Travel.Modules.Flights.Api/Contracts/Contracts.cs). The actual Host publishes a Development OpenAPI document at /openapi/v1.json. [Web contract tests](../../tests/Travel.Host.Tests.Integration/Web/HostWebContractTests.cs) snapshot the document and check the [README request catalog](../../docs/examples/flights-requests.json) against its route and request schemas. The catalog's bodies are also executed against the lightweight HTTP pipeline; a real Host integration test exercises a valid search with a fake supplier. The [local README smoke](../../tools/docs/readme-smoke.mjs) exercises status, OpenAPI and invalid-search ProblemDetails through the running stack.

Health probes are on internal listeners: Host 5098 and AI 5159. Live means the process can answer; ready includes required own stores/schema/initialization; optional dependency health is diagnostic. Public health concealment and ProblemDetails are covered by [Host web tests](../../tests/Travel.Host.Tests.Integration/Web/HostWebContractTests.cs). The [Angular app](../../apps/web/src/app/) currently exposes a system status foundation, with [Playwright](../../tests/travel-e2e/specs/health.spec.ts) checking that page; it is not a booking UI.

These tests prove the stated source and local conditions only. A fake supplier is not a Duffel booking; a disposable Aspire stack is not a deployed environment. Paid Anthropic evals are separate from mandatory local checks. No production migration, shared-data cutover, external ingress or live customer delivery is established by this repository review.

## ADR audit for WS5

| ADR | Review result and current evidence |
|---|---|
| [0001](../adr/0001-modular-monolith.md) | Modular-monolith decision stands; [Host composition](../../apps/Travel.Host/Program.cs) and [project graph test](../../tests/Travel.Tests.Architecture/HostModuleProjectReferenceTests.cs) show only Flights and Identity enabled. The original six-domain wording is historical. |
| [0004](../adr/0004-nx-monorepo-tooling.md) | Accepted CI amendment makes explicit .NET build/inventory authoritative; [workflow](../../.github/workflows/ci.yml), [inventory](../../tools/ci/dotnet-inventory.json) and [package manifest](../../package.json) show current Nx 23. |
| [0006](../adr/0006-testing-strategy.md) | Strategy stands; [Host HTTP tests](../../tests/Travel.Host.Tests.Integration/), [contract tests](../../tests/Travel.Tests.Contract/) and [status-only browser E2E](../../tests/travel-e2e/specs/health.spec.ts) replace Foundation-era examples. |
| [0007](../adr/0007-storage-strategy-marten-ef-coexistence.md) | Storage ownership and environment gate stand; [FlightsDbContext](../../modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/FlightsDbContext.cs), [AiDbContext](../../apps/Travel.AI/Persistence/AiDbContext.cs) and [Host registration](../../apps/Travel.Host/Program.cs) delimit current persistence. |
| [0009](../adr/0009-http-endpoints-wolverine.md) | ADR 0023 amendment already owns one [Host endpoint mapping](../../apps/Travel.Host/Program.cs) and [Flights Api facade](../../modules/flights/Travel.Modules.Flights.Api/Composition/FlightsModule.cs); no new semantic decision in WS5. |
| [0015](../adr/0015-booking-aggregate-event-model.md) | WS4 amendment owns typed transitions in [BookingAggregate](../../modules/flights/Travel.Modules.Flights.Core/Aggregates/BookingAggregate.cs); no new semantic decision. |
| [0016](../adr/0016-booking-saga-via-marten-es.md) | WS4 amendments own the [versioned reconciler](../../modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/OrderReadModelReconciler.cs) and [recovery runbook](../operations/booking-read-model-recovery.md); no new semantic decision. |
| [0020](../adr/0020-nl-search-cross-service-contract.md) | Accepted amendment owns [shared v1 message identity](../../shared/dotnet/Travel.IntegrationContracts.AI/NlSearch/NlSearchContracts.cs) and [Core NATS transport test](../../tests/Travel.Host.Tests.Integration/NlSearch/NlSearchTransportTests.cs); no new semantic decision. |
| [0002](../adr/0002-ai-as-extracted-service.md), [0011](../adr/0011-ai-eval-strategy.md), [0012](../adr/0012-maf-as-primary-agent-runtime.md) | Their WS5 amendments reflect [Travel.AI composition](../../apps/Travel.AI/Program.cs), the [current eval runner](../../tests/Travel.Tests.AiEvals/Flights/NlSearchEvalRunner.cs) and deferred MAF runtime. |
The [legacy root src/ review](../operations/2026-09-23-legacy-src-review.md) records the already-completed historical deletions and the remaining RZD DTO provenance limit. No source deletion is part of WS5.

The source-ready, integration-proven and live-proven states for a particular run belong in its verification report with exact commands and results; this page does not turn an earlier local pass into a permanent live claim.
