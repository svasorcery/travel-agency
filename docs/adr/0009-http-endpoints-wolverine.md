# 0009. HTTP Endpoints via WolverineFx.Http

**Date:** 2026-05-04
**Status:** Accepted
**Deciders:** Foundation spec author

## Context

.NET Minimal APIs (introduced in .NET 6) removed the controller ceremony of ASP.NET MVC but left endpoint definition verbose when applied at scale: route registration, parameter binding, validation, ProblemDetails formatting, and response type documentation must all be wired manually per endpoint. Several third-party libraries emerged to address this, most prominently FastEndpoints and various custom `IEndpoint` patterns.

The choice of HTTP endpoint library intersects with the messaging and Wolverine adoption (ADR 0003): an endpoint library that integrates natively with Wolverine allows a handler to both respond to an HTTP request and cascade Wolverine messages (domain events, outbox entries) within a single transaction, without manual coordination. This is the REPR (Request-Endpoint-Response) pattern expressed through the Critter Stack.

A second concern shapes this ADR: the transitive dependency graph of `Travel.Shared.Web`. If the shared HTTP abstractions package references ASP.NET Core types (`HttpContext`, `IEndpointRouteBuilder`, middleware interfaces), that package cannot be used by domain and application layers without pulling ASP.NET Core into their transitive closure — violating the hexagonal architecture principle that the domain should not know about the delivery mechanism.

## Decision

**WolverineFx.Http** is the HTTP endpoint layer for all `Travel.Host` API endpoints. Endpoints are declared as plain C# classes annotated with Wolverine HTTP attributes (`[WolverineGet]`, `[WolverinePost]`, etc.); Wolverine source-generates the `IEndpointRouteBuilder` mapping. Handlers return typed C# objects (including `ErrorOr<T>`); WolverineFx.Http maps them to HTTP responses with ProblemDetails for errors. A handler may cascade Wolverine messages (events, commands) as tuple return values, integrating HTTP and messaging in one unit.

**Travel.Shared.Web vs. Travel.Shared.Infrastructure split:** HTTP-specific shared utilities (ProblemDetails conventions, middleware registrations, endpoint base types that reference `HttpContext`) live in **`Travel.Shared.Web`**. Infrastructure shared utilities that do not depend on ASP.NET Core (database conventions, Wolverine configuration helpers, shared value objects, integration abstractions) live in **`Travel.Shared.Infrastructure`**. Domain and Application layers may reference `Travel.Shared.Infrastructure` but must never reference `Travel.Shared.Web`. This keeps ASP.NET Core out of the domain/application transitive closure.

## Alternatives Considered

### Option A: FastEndpoints

FastEndpoints is a popular Minimal API wrapper that provides a strongly-typed endpoint class model, automatic validation, and OpenAPI integration.

Rejected because: FastEndpoints uses an async-void-style response model where the handler calls `SendAsync(response)` as a side effect rather than returning a typed value. This makes handler testing awkward (mock the `SendAsync` call rather than assert the return value) and does not compose naturally with Wolverine's tuple-cascading message model. Given that Wolverine is already the messaging library, WolverineFx.Http from the same vendor provides deeper integration with no additional learning curve.

### Option B: Custom `IEndpoint` Pattern (plain Minimal APIs wrapper)

A project-specific `IEndpoint` interface with a `MapEndpoints(IEndpointRouteBuilder)` method. Each endpoint class registers itself. Common in YouTube clean-architecture tutorials.

Rejected because: this is NIH in the presence of a mature, well-integrated alternative from the same vendor as the messaging library. A custom `IEndpoint` produces the same endpoint-as-class ergonomic benefit as WolverineFx.Http but without source generation, without automatic ProblemDetails integration, and without Wolverine message cascading. Maintaining a custom framework alongside Wolverine adds surface area with no architectural benefit.

### Option C: Plain Minimal API Lambdas

Use `app.MapGet(...)` inline lambdas directly in `Program.cs` or in per-module extension methods.

Rejected because: this scales poorly as the number of endpoints grows. OpenAPI metadata, validation integration, ProblemDetails consistency, and handler isolation all require progressively more boilerplate per endpoint. The resulting code is harder to navigate and test. WolverineFx.Http provides all of this as framework-level conventions.

## Consequences

### Positive
- Handlers are plain C# classes with typed return values; they can be unit-tested without an HTTP host by instantiating the class and calling the handler method directly.
- WolverineFx.Http source generation surfaces wiring errors at build time rather than at first request in production.
- The `Travel.Shared.Web` / `Travel.Shared.Infrastructure` split enforces hexagonal architecture at the package level: the compiler prevents domain projects from importing HTTP concerns.

### Negative / Trade-offs
- WolverineFx.Http is tied to the JasperFx ecosystem. A future decision to switch messaging libraries (e.g., back to MediatR if it re-opens its license) would require migrating the HTTP layer simultaneously, as they share the handler discovery pipeline.
- Source generation increases cold build time and requires the Wolverine source generator NuGet in every project that declares endpoints. For a large solution this adds measurable build overhead.

### Neutral
- The `Travel.Shared.Web` package is the single location where ASP.NET Core infrastructure enters the shared library surface. Keeping it isolated makes the separation auditable: any PR that adds an ASP.NET Core type to `Travel.Shared.Infrastructure` is immediately wrong.

## Out of Scope

- API versioning strategy — not a Foundation decision; deferred to the first subproject that requires a breaking API change.
- OpenAPI specification generation and TypeScript client generation via heyAPI — operational tooling, documented in `docs/conventions/`.

## References

- Concept doc: `docs/superpowers/specs/2026-05-03-travel-platform-concept.md` § 5.1
- Foundation spec: `docs/superpowers/specs/2026-05-04-foundation-design.md` § 6 (row 0009), § 5.1
- ADR 0003: `docs/adr/0003-wolverine-marten-stack.md`
- WolverineFx.Http documentation: https://wolverinefx.net/guide/http
