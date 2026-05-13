# 0007. Storage Strategy — Marten and EF Core Coexistence on PostgreSQL

**Date:** 2026-05-04
**Status:** Accepted
**Deciders:** Foundation spec author

## Context

The Travel platform spans multiple bounded contexts with fundamentally different persistence requirements. The booking lifecycle for Flights and Trips is history-centric: every state transition (OfferQuoted → Held → Confirmed → Ticketed → Refunded → Cancelled) is a business fact, disputes are settled by replaying events, and temporal queries ("what was the booking state at 14:32 on the 5th?") are a real-world requirement for airline integrations. This is the canonical use case for event sourcing. Other contexts — station registries, hotel supplier metadata, user preferences, search result caches, identity tokens — are simple CRUD with relational queries. Forcing all of them through an event-sourced model would be over-engineering; forcing all of them through a relational ORM would lose the history semantics where they matter.

The challenge is combining both persistence models without introducing either two separate database servers (operational overhead, eventual consistency, connection pool fragmentation) or one ORM that tries to do both poorly.

## Decision

The platform uses a **polyglot persistence strategy on a single PostgreSQL 17 instance**:

- **Marten** (MIT, JasperFx) owns all **event-sourced aggregates**: `BookingAggregate` in Flights, `TripAggregate` in Trips. Marten manages its own schema namespace (`mt_*` tables: `mt_events`, `mt_streams`, `mt_doc_*` projections). It applies migrations at application startup via `AddMarten().ApplyAllPendingMigrationsOnStartup()`.
- **EF Core 10** owns all **relational models** in every module: saved travellers, supplier metadata, idempotency keys, outbox records, search audit, station registries, hotel ranking weights, prompt versions, cost ledger, conversation history, users, tokens, feature flags. EF Core migrations are versioned in each module's `Migrations/` folder and applied via `dotnet ef database update` (or programmatically at startup in development).
- **No schema overlap**: Marten tables are exclusively in the `mt_*` namespace; EF Core tables are in module-specific schemas (e.g., `flights`, `hotels`, `identity`). Neither ORM reads or writes the other's tables.
- The design message is explicit: "event sourcing where history is the domain; relational storage where storage is infrastructure."

## Alternatives Considered

### Option A: Separate Databases per ORM

Marten uses a dedicated PostgreSQL instance; EF Core uses another. Full isolation with no risk of schema interference.

Rejected because: two database servers double the connection pool configuration, Aspire resource definition, Docker Compose complexity, and operational surface area. For a showcase application, the added isolation is not worth the infra cost. Schema-level ownership separation within one Postgres instance provides the same logical isolation with a fraction of the operational overhead.

### Option B: Single ORM — EF Core for Everything (No Event Sourcing)

Use EF Core change tracking and an audit-log table to simulate event history. Simple, single mental model.

Rejected because: this loses a core portfolio demonstration objective. The booking lifecycle is the domain where event sourcing is genuinely the right model — the state machine has 6 states, each transition is a distinct business event, refund and dispute resolution depend on event replay. Using EF Core audit tables to simulate this hides the pattern rather than demonstrating it. Selective event sourcing (only where history is the domain) is itself a design insight worth showing explicitly.

### Option C: Marten for Everything (Full Event Sourcing)

Eliminate EF Core; use Marten document store and projections for all persistence including catalogs, metadata, and configuration tables.

Rejected because: forcing a document/event-sourced model onto relational data (e.g., station registry, hotel supplier config) is the opposite mistake — over-engineering simple storage to demonstrate a pattern. EF Core's schema migrations, LINQ-based querying, and relational constraint support are genuinely better for the CRUD-heavy contexts. The portfolio value is showing *when* to use each tool, not maximising usage of the most exotic one.

## Consequences

### Positive
- The separation is self-documenting: seeing `Marten` in a context tells a reader "this is event-sourced; history is meaningful here." Seeing `DbContext` tells them "this is relational CRUD."
- Wolverine's built-in Marten integration (transactional outbox, saga persistence, event subscriptions) works seamlessly because both share the JasperFx ownership.
- EF Core migrations for module schemas are independent of each other and of Marten; each module can evolve its schema without coordinating with others.

### Negative / Trade-offs
- Contributors must understand two persistence paradigms and two migration mechanisms. The "which one do I use?" question requires understanding the domain before choosing a tool, which is a non-trivial onboarding cost.
- Cross-model queries — e.g., joining a Marten projection with an EF Core table — are not directly possible via LINQ. They must be expressed as separate queries or raw SQL. This is an intentional boundary but can be inconvenient.

### Neutral
- pgvector is added as a PostgreSQL extension to the same instance for semantic search in AI features. It is accessed via `Pgvector.EntityFrameworkCore` (community binding, pre-1.0, treated as preview-grade). This does not change the Marten/EF ownership boundary.

## Out of Scope

- Whether specific modules not listed here should adopt Marten in future — deferred to the subproject where the storage requirement is first understood.
- Read model projection strategy for Marten (inline vs. async projections) — implementation detail, not an architecture decision.

## References

- Concept doc: `docs/superpowers/specs/2026-05-03-travel-platform-concept.md` § 4.5
- Foundation spec: `docs/superpowers/specs/2026-05-04-foundation-design.md` § 6 (row 0007), § 5.1
- ADR 0003: `docs/adr/0003-wolverine-marten-stack.md`
- Marten documentation on schema ownership: https://martendb.io/schema
- EF Core migrations: https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations
