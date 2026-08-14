# 0007. Storage Strategy — Marten and EF Core Coexistence on PostgreSQL

**Date:** 2026-05-04
**Status:** Accepted
**Deciders:** Foundation spec author

## Context

The Travel platform spans multiple bounded contexts with fundamentally different persistence requirements. The booking lifecycle for Flights and Trips is history-centric: every state transition (OfferQuoted → Held → Confirmed → Ticketed → Refunded → Cancelled) is a business fact, disputes are settled by replaying events, and temporal queries ("what was the booking state at 14:32 on the 5th?") are a real-world requirement for airline integrations. This is the canonical use case for event sourcing. Other contexts — station registries, hotel supplier metadata, user preferences, search result caches, identity tokens — are simple CRUD with relational queries. Forcing all of them through an event-sourced model would be over-engineering; forcing all of them through a relational ORM would lose the history semantics where they matter.

The challenge is combining both persistence models without introducing either two separate database servers (operational overhead, eventual consistency, connection pool fragmentation) or one ORM that tries to do both poorly.

## Decision

The platform uses a **polyglot persistence strategy on a single PostgreSQL 17 instance**:

- **Marten** (MIT, JasperFx) owns all **event-sourced aggregates**: `BookingAggregate` in Flights, `TripAggregate` in Trips. Marten manages its own tables (`mt_events`, `mt_streams`, `mt_doc_*` projections). Schema changes follow the environment-specific initialization and deployment gates in the 2026-08-14 amendment below; Production does not auto-apply them at application startup.
- **EF Core 10** owns all **relational models** in every module: saved travellers, supplier metadata, idempotency keys, outbox records, search audit, station registries, hotel ranking weights, prompt versions, cost ledger, conversation history, users, tokens, feature flags. EF Core migrations are versioned in each module's `Migrations/` folder. Non-Production initialization and the explicit Production deployment gate are defined in the 2026-08-14 amendment below.
- **No schema overlap**: Marten tables are exclusively in the `mt_*` namespace; EF Core tables are in module-specific schemas (e.g., `flights`, `hotels`, `identity`). Neither ORM reads or writes the other's tables.
- The design message is explicit: "event sourcing where history is the domain; relational storage where storage is infrastructure."

## Amendment (2026-08-14): Environment-specific schema gates

The storage ownership decision above remains Accepted. This amendment replaces the stale startup-auto-migration policy with the initialization behavior implemented by WS2.3.

### Context configuration and migration ownership

- Each EF Core context has one owner-specific Npgsql configuration reused by runtime registration and its design-time factory. Provider selection, snake-case naming, default schema, migrations assembly, and migrations-history placement must not diverge between those paths.
- EF Core migration history is schema-qualified per owner: Flights uses `flights.__ef_migrations_history`; Travel.AI uses `ai.__ef_migrations_history`. A shared `public.__EFMigrationsHistory` table is not the platform contract.
- Module and process initializers run deterministically by phase, then by stable initializer type name: `Platform` -> `RelationalSchema` -> `EventStoreSchema` -> `DevelopmentSeed`. Wolverine durable-message storage is a Host-owned Platform concern, EF Core is relational-schema work, and Marten is event-store-schema work.

### Environment policy

- Outside Production, the owning initializers may apply schema changes to their explicitly configured database. Development and Testing are the environments proven by disposable/local tests: EF Core uses `MigrateAsync`; Marten and Wolverine/Weasel apply their configured changes. This convenience is not a Production deployment mechanism.
- In Production, application startup is validation-only. EF Core checks for pending migrations; Marten and Wolverine/Weasel call read-only `AssertDatabaseMatchesConfigurationAsync`. Production startup does not call `MigrateAsync`, `ApplyAllConfiguredChangesToDatabaseAsync`, or another automatic schema-apply API.
- An incompatibility marks initialization `Failed`. The process remains live for diagnostics, but `/health/ready` is unhealthy and traffic must not be switched to that instance. Initialization diagnostics expose initializer identity, phase, state, and safe error type, not connection strings or exception messages.

### Deployment and rollback consequences

- A migration committed to source is only **source-ready**; it is not evidence that any live database was changed. Production deployment requires a separate, explicit migration job to complete successfully before traffic is switched to the new application version.
- If that job is skipped, fails, or leaves Marten/Wolverine configuration incompatible, the application must remain not-ready. Operators must fix or complete the schema gate rather than bypass readiness or enable application-startup migration.
- Rollback is an application-and-schema compatibility decision. Before applying a migration, the deployment plan must establish whether the previous application version can run against the new schema and provide a migration-specific rollback or restore procedure when it cannot. Rolling back application binaries alone does not reverse a database change.
- WS2.3 supplies source and disposable-environment proof of these gates. It neither implements nor runs the Production migration job, changes a live database, or proves ingress/deployment behavior.

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
