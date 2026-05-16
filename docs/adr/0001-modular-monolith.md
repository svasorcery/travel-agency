# 0001. Travel.Host as a Modular Monolith

**Date:** 2026-05-04
**Status:** Accepted
**Deciders:** Foundation spec author

## Context

The Travel platform is a showcase for production-grade .NET architecture covering six business domains: Flights, Hotels, Rail, Trip Planning, Identity, and Shared infrastructure. At the start of the project the team is a single developer building an OSS portfolio. The dominant alternative pattern for multi-domain systems — microservices — requires a level of operational overhead (multiple deployments, inter-service network calls, distributed tracing, independent CI pipelines) that is disproportionate to the project size and actively hinders rapid iteration during the foundational phase.

At the same time, an unstructured "big ball of mud" monolith would not demonstrate the domain isolation, bounded-context thinking, and Ports-and-Adapters discipline that make a portfolio project valuable to employers and contributors. The goal is to maximise architectural credibility while minimising operational complexity — and to keep the door open for genuine microservice extraction later if load, team size, or domain complexity justifies it.

## Decision

`Travel.Host` is structured as a **modular monolith**: a single deployable process containing six independently bounded modules (Flights, Hotels, Rail, Trips, Identity, Shared). Each module owns its own namespace (`Travel.Modules.{Name}.{Layer}`), its own EF Core `DbContext`, its own migration timeline, and its own test projects. Modules communicate exclusively through Wolverine message dispatch or via `Travel.Shared.Abstractions` interfaces — no module imports the internal namespaces of another. Module boundary enforcement is automated via ArchUnitNET architecture tests that run on every CI build. `Travel.AI` is extracted as a genuinely separate process (see ADR 0002) because its load profile, deploy cadence, and secrets boundary differ meaningfully from the rest of the host.

## Alternatives Considered

### Option A: Full Microservices

Each domain (Flights, Hotels, Rail, etc.) deployed as an independently runnable service with its own database. This pattern is common in large engineering organizations where team-per-service reduces coordination overhead and enables independent scale.

Rejected because: the project is solo at founding; the operational cost (kubernetes manifests, per-service CI, distributed trace correlation from day one, network call overhead, service discovery) would crowd out the domain-modelling and DDD-pattern work that is the actual portfolio value. Microservices are an organisational pattern first; the split-readiness property of the modular monolith delivers the architectural benefit without the cost.

### Option B: Single Unstructured Monolith

One project, one namespace, no enforced boundaries between domains — the fastest path to a working application.

Rejected because: it defeats the primary portfolio purpose. A reviewer cannot tell whether a developer understands bounded contexts, DDD, or the hexagonal pattern from a single undifferentiated codebase. Additionally, an unstructured monolith is genuinely harder to extract later than a modular one.

## Consequences

### Positive
- Single `aspire run` starts the entire system; local development and contributor onboarding require minimal infrastructure knowledge.
- Bounded-context discipline is visible and verifiable: ArchUnitNET tests enforce inter-module isolation on every PR.
- Modules can be extracted to independent services in future by promoting an in-process Wolverine message dispatch to a NATS-backed one — the messaging contract is already the public interface.

### Negative / Trade-offs
- Boundary discipline must be actively maintained. Without cultural and tooling enforcement, the modular structure degrades into a distributed monolith or ball of mud. ArchUnitNET tests mitigate this but require ongoing investment.
- A single deployable process means all modules share the same deployment lifecycle for non-AI concerns. A regression in one module blocks deployment of others until module-level canary releases are introduced.

### Neutral
- The `Travel.AI` extraction demonstrates that the project understands *when* to extract (genuine operational divergence) as well as when not to (organisational convenience that does not yet exist).

## Out of Scope

- Which specific modules will be extracted to microservices, and when — this is a runtime decision driven by observed load and team growth, not a Foundation-time decision.
- Cross-module synchronous HTTP calls — currently prohibited; this ADR does not decide whether they will ever be permitted.

## References

- Concept doc: `docs/superpowers/specs/2026-05-03-travel-platform-concept.md` § 4.1
- Foundation spec: `docs/superpowers/specs/2026-05-04-foundation-design.md` § 6
- Sam Newman, *Building Microservices* (2nd ed.) — monolith-first pattern
- Mauro Servienti, *All Our Aggregates Are Wrong* — bounded context decomposition
