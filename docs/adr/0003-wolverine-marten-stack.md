# 0003. Critter Stack — Wolverine + Marten + WolverineFx.Http

**Date:** 2026-05-04
**Status:** Accepted
**Deciders:** Foundation spec author

## Context

The Travel platform requires three distinct infrastructure capabilities that are conventionally solved by separate libraries: in-process CQRS message dispatch, event sourcing for the booking lifecycle, and a structured HTTP endpoint layer. Selecting a separate best-of-breed library for each means three independent mental models, three separate integration points, three different release cadences, and a higher probability of subtle incompatibilities at the seams.

Two libraries that historically dominated the .NET CQRS/messaging space changed their licensing terms in 2025–2026. MediatR moved to a commercial license under Lucky Penny Software in July 2025, and MassTransit (the predominant service-bus abstraction) moved to a commercial license under Massient in Q1 2026. Both changes affect OSS projects: a showcase repository that depends on commercially licensed core infrastructure is not a clean open-source reference. The .NET ecosystem was left with a gap — MIT-licensed, production-grade messaging — that the JasperFx Critter Stack fills directly.

## Decision

The platform adopts the **JasperFx Critter Stack**: **Wolverine** for in-process and cross-process CQRS/messaging and saga orchestration, **Marten** for event sourcing on PostgreSQL, and **WolverineFx.Http** for HTTP endpoint definition (see also ADR 0009). All three packages are MIT-licensed and maintained by Jeremy Miller and the JasperFx organization. Wolverine and Marten share deep integration: Wolverine handlers can directly produce and consume Marten events; outbox durability is built in. This creates a single unified mental model spanning messaging, event sourcing, and HTTP across the entire backend.

## Alternatives Considered

### Option A: MediatR + Dapper (pre-commercialization stack)

MediatR provides in-process request/handler dispatch, Dapper provides lightweight data access, and a custom or third-party outbox (e.g., Outbox.Core) handles durable messaging. This was the dominant .NET clean-architecture stack before July 2025.

Rejected because: MediatR 13+ requires a commercial license for non-trivial use, making it unsuitable for an OSS portfolio reference. Dapper provides no event sourcing; adding ES would require a third library. The stack produces three integration seams (MediatR ↔ outbox ↔ data access) where Wolverine provides one.

### Option B: MassTransit + EF Core Outbox

MassTransit provides both in-process mediation (via its mediator) and cross-service messaging; EF Core's outbox integration handles durability. This was the dominant service-bus stack for .NET before Q1 2026.

Rejected because: MassTransit moved to a commercial license under Massient in Q1 2026, same OSS-hygiene concern as MediatR. Additionally, MassTransit's in-process mediator is not as ergonomic as Wolverine's convention-based handler discovery for vertical-slice architectures.

## Consequences

### Positive
- Single MIT-licensed stack from one vendor covers dispatch, ES, outbox, HTTP, and saga — minimising context-switching for contributors.
- Wolverine's source-generated dispatch eliminates reflection overhead at runtime and surfaces handler wiring errors at build time.
- Marten and Wolverine are authored by the same team; the outbox/event-sourcing integration is first-class, not an afterthought.

### Negative / Trade-offs
- The Critter Stack community is smaller than the former MediatR + MassTransit ecosystem. Fewer blog posts, StackOverflow answers, and third-party integrations exist compared to the commercially-licensed incumbents. Contributors unfamiliar with JasperFx tools face a steeper initial learning curve.
- Wolverine's convention-based auto-wiring is powerful but can be surprising to developers accustomed to explicit DI registration. Misconfigured handlers fail silently unless tests cover the dispatch path.

### Neutral
- The switch from MediatR to Wolverine is a direct response to the 2025 ecosystem shift. Readers of this ADR should understand that the choice is not "Wolverine is always better than MediatR" — it is "Wolverine is the best MIT-licensed option after MediatR became commercial."

## Out of Scope

- The boundary between Marten (event sourcing) and EF Core (relational read models) — covered in ADR 0007.
- The specific HTTP endpoint conventions for WolverineFx.Http — covered in ADR 0009.
- Cross-process messaging transport selection (NATS JetStream) — this is an infrastructure configuration, not a library-selection decision.

## References

- Concept doc: `docs/superpowers/specs/2026-05-03-travel-platform-concept.md` § 5.1
- Foundation spec: `docs/superpowers/specs/2026-05-04-foundation-design.md` § 6 (row 0003), § 5.1
- JasperFx Wolverine documentation: https://wolverinefx.net
- JasperFx Marten documentation: https://martendb.io
- MediatR commercial license announcement: https://jimmybogard.com/mediatr-13-and-the-future (July 2025)
