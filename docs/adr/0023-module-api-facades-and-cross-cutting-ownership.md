# 0023. Module API Facades and Cross-Cutting Ownership

**Date:** 2026-08-22
**Status:** Accepted
**Deciders:** Travel platform owner

## Context

ADR 0001 established `Travel.Host` as a modular monolith, and ADR 0009 selected WolverineFx.Http for HTTP endpoints. As Flights grew, however, `Travel.Host` accumulated registrations and imports from module Application and Infrastructure projects. Scaffold modules were also reachable from the Host project graph before they had a runtime milestone. The result weakened the claim that a module has one public integration surface and made process-wide Marten, Wolverine, HTTP, observability, and resilience policy ownership ambiguous.

The application remains one HTTP host. A separate composition assembly for every module would add projects and public surface without adding a distinct runtime role. At the same time, allowing a module facade to create another Marten or Wolverine builder would duplicate process-global policy and make middleware and endpoint mapping order dependent on registration details.

The platform also needs an explicit boundary for cross-cutting concerns. Platform defaults, Host process policy, module policy, provider-specific integration policy, and reusable Shared primitives have different owners. Without a written ownership model, a convenient shared extension or global HTTP policy can silently stack retries, expose provider secrets in telemetry, or pull module/hosting policy into Shared assemblies.

Finally, ADR 0018 requires the Duffel inbox write and Wolverine outbox publication to remain atomic. Hiding provider and persistence details from the HTTP endpoint must preserve that behavior rather than moving the transaction into the API layer.

## Decision

### Module facade and enabled runtime graph

`Travel.Modules.{Name}.Api.Composition` is the sole Host-visible surface of an enabled module. The Host references module projects only through their Api projects and imports only their Composition namespaces. A module without a real composition implementation and an enabled product milestone is not included in the Host runtime graph.

For the current runtime, Flights exposes:

```csharp
public static IHostApplicationBuilder AddFlightsModule(
    this IHostApplicationBuilder builder);

public static void ConfigureMarten(StoreOptions options);

public static void ConfigureWolverine(WolverineOptions options);

public static WebApplication UseFlightsModule(
    this WebApplication app);
```

Identity exposes its registration facade through `AddIdentityModule`. Module registration helpers behind these facades may remain `internal` when tooling permits. The facade may reference its module's Infrastructure project to compose the module.

That exception is limited to Composition. Types in `Api.Endpoints`, `Api.Contracts`, `Api.Middleware`, and every other non-Composition Api namespace must not depend on module Infrastructure. Transport code calls Application commands or ports and maps transport-neutral results to HTTP.

### Process-global builders and HTTP pipeline

`Travel.Host` owns enabled-module selection, creates the Marten and Wolverine builders exactly once, and calls each enabled module's contributions inside those builders. Module contributions add only module mappings, handlers, discovery, routes, and messages. They do not create a second builder or change process-wide stream identity, durability storage, transport guarantees, or failure policy.

The Host also owns the single HTTP pipeline and the single `MapWolverineEndpoints` call. Its security-sensitive order is:

```text
global exception handling
-> authentication
-> authorization
-> enabled module middleware
-> one MapWolverineEndpoints call
```

A module middleware hook contributes only inside the Host-selected module phase. A facade must not map Wolverine endpoints itself.

### Cross-cutting ownership

| Owner | Responsibility |
|---|---|
| `Travel.ServiceDefaults` / platform hosting | Logging, OpenTelemetry/exporter wiring, service discovery, `TimeProvider`, the standard RFC7807 exception/ProblemDetails policy, OpenAPI services, health endpoint policy, and an opt-in internal HTTP resilience profile. |
| `Travel.Host` | Enabled-module selection, physical resource names, transport guarantees, the global authentication fallback, process-level Marten/Wolverine policy, and build/map/run. |
| Identity Api facade | JWT/Keycloak options, claim normalization, scope requirements, and fail-closed user identity. |
| Flights Api facade | Complete Flights registration, module policies and middleware, metrics, health contributors, handlers, and provider adapters composed through Infrastructure. |
| Integration-contract assemblies | Stable request/reply/event identities and wire versions between processes. |
| `Travel.Shared.Infrastructure` | Reusable non-web primitives and initialization abstractions with real consumers; it does not own module wiring. |
| `Travel.Shared.Web` | HTTP-specific result, error, and identity helpers without module-specific or general hosting/startup policy. |

A Shared primitive must have at least two real consumers or be justified as a platform contract. Shared projects must not become an indirect module-composition path.

### External HTTP resilience and telemetry redaction

External provider resilience belongs to the integration adapter. Duffel, Travelpayouts, Frankfurter, Keycloak Admin, and health-probe clients own their timeout, retry, and circuit-breaker behavior. Platform hosting does not apply `AddStandardResilienceHandler` globally; an internal service-to-service client must explicitly opt in through the platform profile. A client must not receive both platform and provider retry pipelines, and production code must not remove a global handler as a suppression workaround.

Secret redaction remains a generic, configurable platform mechanism. Provider-specific sensitive query-parameter names are supplied by integration-owned contributors; the platform does not hard-code provider vocabulary.

### Duffel webhook boundary

ADR 0018 remains the authority for webhook atomicity and duplicate semantics. The API endpoint owns only HTTP concerns: it captures the raw request body and transport headers, creates a provider-neutral ingestion request, calls the Application ingestion service, and maps the typed outcome to RFC7807 or success.

The Application layer owns the ingestion service and port contract. Infrastructure implements that port and owns Duffel HMAC verification, supplier DTO parsing, PostgreSQL duplicate detection, the EF inbox write, and Wolverine outbox publication. The inbox write and outbox message are committed through the same Infrastructure transaction as required by ADR 0018.

### Condition for a separate composition assembly

No `Travel.Modules.{Name}.Composition` assembly is introduced for the current single HTTP host. A separate composition assembly requires both a new non-HTTP or multi-host runtime need, such as a worker, CLI, or dedicated consumer process, and a new ADR that defines the resulting public surface and policy ownership. Reorganizing files alone is not sufficient justification.

## Alternatives Considered

### Keep module-internal wiring in `Travel.Host`

Rejected because direct Host imports of module Application or Infrastructure types make the Host a second module composition root, expose transitive implementation details, and allow scaffold modules into the runtime graph before their milestone.

### Let each facade create Marten, Wolverine, or endpoint builders

Rejected because Marten stream identity, Wolverine durability and failure behavior, endpoint discovery, and pipeline order are process-global decisions. Multiple builders or mapping call sites can stack policy and create order-dependent behavior.

### Create a separate composition project for every module now

Rejected because there is one HTTP host and no independent runtime consumer for such assemblies. The extra project would add ceremony and another public dependency edge without isolating a real deployment or host type.

### Apply one resilience handler to every `HttpClient`

Rejected because provider rate limits, safe retry methods, timeout budgets, and circuit-breaker thresholds differ. A global retry can stack with an adapter pipeline and multiply attempts. Explicit platform opt-in remains available for internal clients.

## Consequences

### Positive

- The Host's module graph contains only enabled Api facades, while module implementation details remain behind composition boundaries.
- Marten, Wolverine, authorization fallback, middleware order, and endpoint mapping have one auditable process owner.
- Provider-specific retries and redaction vocabulary stay with the adapters that understand the provider contract.
- The Duffel endpoint is transport-only while the existing atomic inbox/outbox behavior remains in Infrastructure.
- Architecture tests can enforce project references, type dependencies, call-site counts, Api namespace isolation, resilience ownership, and Shared charters.

### Negative / Trade-offs

- Api projects intentionally reference their module's Infrastructure project for Composition, so namespace and architecture guards are required to prevent that dependency from leaking into transport code.
- Each new enabled module must provide a facade and process-builder contributions before Host integration; scaffold projects are not runtime placeholders.
- Process-wide policy changes require coordination in Host even when motivated by one module.

### Neutral

- This decision does not split the modular monolith, change the WolverineFx.Http endpoint model, or change the Duffel delivery guarantee.
- It does not define WS4 booking consistency, projection recovery, rebuild, retry/DLQ behavior, or WS5's complete all-module/layer matrix.

## Evidence and Validation Boundary

Repository evidence for this decision is present in:

- `apps/Travel.Host/Program.cs` and `apps/Travel.Host/Travel.Host.csproj` for enabled facades, single global builders/mapping, process policy, and pipeline order;
- `modules/flights/Travel.Modules.Flights.Api/Composition/FlightsModule.cs` and `modules/identity/Travel.Modules.Identity.Api/Composition/IdentityModule.cs` for facade contributions;
- the Flights Application webhook contracts and `Travel.Modules.Flights.Infrastructure/Webhooks/DuffelWebhookIngestionPort.cs` for the transport/Application/Infrastructure split;
- `apps/Travel.ServiceDefaults`, Flights Infrastructure registration, and `Travel.Shared.Infrastructure/Telemetry/IHttpUrlRedactionContributor.cs` for platform opt-in resilience and contributor-based redaction;
- architecture and integration tests under `tests/Travel.Tests.Architecture`, `tests/Travel.Host.Tests.Integration`, and `tests/flights` for executable ownership and behavior checks.

Repository evidence shows what the source implements; it is not by itself runtime or deployment proof. The acceptance states are separate:

| State | Acceptance boundary |
|---|---|
| Source-ready | Verified on 2026-08-22 by the successful Task 8 formatting, focused-test, solution build/test, inventory, harness, diff, migration, and ownership gates. The first full-test attempt hit a timing-sensitive PostgreSQL container cleanup timeout; the exact test and a complete solution rerun passed without a source change. |
| Integration-proven | Verified in disposable local Docker integration on 2026-08-22: all 181 Flights integration tests passed in the focused run and again in the clean full-suite rerun, including the real-port webhook/inbox/outbox duplicate and rollback coverage. This does not prove a shared or live environment. |
| Live/deployment-validated | Not attempted by this decision. No deployment, shared/live database operation, migration generation/application, CI dispatch, or external mutation is authorized or implied. |

Exact command results and residual warnings are recorded in the WS3 Task 8 report. User review of the complete WS3 diff is the next gate before any push, PR, or WS4 work.

## References

- ADR 0001: `docs/adr/0001-modular-monolith.md`
- ADR 0009: `docs/adr/0009-http-endpoints-wolverine.md`
- ADR 0018: `docs/adr/0018-duffel-webhook-inbox-outbox.md`
- Canonical remediation design: `docs/superpowers/specs/2026-08-11-ai-harness-architecture-remediation-design.md` (D4-D6, D11, WS3)
- WS3 implementation plan: `docs/superpowers/plans/2026-08-21-ws3-module-composition-cross-cutting-ownership.md`
