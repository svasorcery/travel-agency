# 0002. Travel.AI as an Extracted Service

**Date:** 2026-05-04
**Status:** Accepted
**Deciders:** Foundation spec author

## Context

The Travel platform includes AI-powered product features: a Trip Planning Assistant, a Search Advisor, a Price Prediction agent, and the educational Travel Advisor custom runtime. These features share common infrastructure (Microsoft.Extensions.AI abstraction layer, Anthropic SDK, MAF orchestration) but differ radically from the core booking domains in three dimensions: load profile (long-streaming LLM calls, expensive per-token cost, no hard latency SLA), deploy cadence (prompt engineering changes frequently without touching booking logic), and secrets boundary (Anthropic API keys, eval service credentials must not be co-located with booking-domain service credentials for least-privilege reasons).

Keeping AI workloads inside `Travel.Host` would require deploying the entire monolith every time a prompt is tuned, expose all modules to the memory and CPU spikes caused by large model inference, and co-mingle secrets that belong to different operational scopes. In the broader industry AI inference is almost universally extracted to a dedicated service — signalling that the designer understands where to draw process boundaries is itself a portfolio objective.

## Decision

`Travel.AI` is deployed as a **separate process**, orchestrated alongside `Travel.Host` by Aspire AppHost in a single `aspire run`. It hosts four production agents on Microsoft Agent Framework 1.0 and one educational custom-runtime agent (Travel Advisor). Communication with `Travel.Host` is primarily asynchronous via Wolverine over NATS JetStream; synchronous HTTP is available where needed.

**Data sharing (load-bearing):** `Travel.AI` connects to the **same PostgreSQL instance** as `Travel.Host` but accesses `Travel.Host` schema data in **read-only mode** only. `Travel.AI` owns its own schema (`ai`) with tables for prompt versions, eval run history, cost ledger, and conversation history. It never writes to `Travel.Host` module schemas. This pattern avoids a duplicated data store while preserving a clear ownership boundary: `Travel.Host` is the system of record for all booking and domain data; `Travel.AI` is a consumer of that record, not a co-owner. Any mutation that affects booking state flows through Wolverine messages back to `Travel.Host` handlers.

## Alternatives Considered

### Option A: AI Features Inside Travel.Host

AI agents and infrastructure run as additional modules within the monolith. No separate process, no NATS hop for AI-originated events.

Rejected because: this collapses the three divergence dimensions into a single deployment unit. Every prompt change triggers a full monolith release. Long-running LLM streaming calls share the thread pool with booking API handlers. Anthropic API keys live in the same configuration scope as booking provider credentials. The portfolio value of demonstrating *where* to extract is also lost.

### Option B: Fully Separate Repository with its Own Database

`Travel.AI` lives in a distinct git repository with its own PostgreSQL instance, maintaining domain data via event replication from `Travel.Host`.

Rejected because: event replication introduces eventual-consistency lag that complicates the read model, requires a CDC or outbox pipeline for Travel.Host → Travel.AI data, and adds substantial infra overhead for a solo OSS showcase. The shared PostgreSQL with schema-level ownership separation gives the same isolation guarantee at a fraction of the operational cost.

## Consequences

### Positive
- Prompt engineering and model configuration changes ship without touching booking logic; deploy cadence for `Travel.AI` is independent of `Travel.Host`.
- Memory and CPU spikes from LLM calls cannot starve booking API handlers; horizontal scaling of `Travel.AI` is independent.
- Anthropic API keys and eval service credentials are scoped to `Travel.AI` configuration only, satisfying least-privilege.

### Negative / Trade-offs
- Every AI-originated state mutation (e.g., an agent confirming a trip suggestion) requires an async Wolverine round-trip to `Travel.Host` rather than a direct method call. This adds latency and a failure mode (NATS unavailability) absent in the single-process design.
- Two process lifetimes to manage in development; contributors must understand the Aspire orchestration layer to run the full system.

### Neutral
- The read-only cross-schema Postgres access is an intentional simplification. A future hard extraction to separate databases would require introducing a read model sync mechanism; this ADR deliberately accepts that cost as a future-phase decision.

## Out of Scope

- Which specific AI features ship in which subproject — this ADR only establishes the process and data-sharing boundary.
- Whether `Travel.AI` will eventually use a vector database separate from PostgreSQL+pgvector — deferred to the subproject where semantic search first ships.

## References

- Concept doc: `docs/superpowers/specs/2026-05-03-travel-platform-concept.md` § 4.2, § 4.3, § 4.5
- Foundation spec: `docs/superpowers/specs/2026-05-04-foundation-design.md` § 6 (row 0002), § 5.4
- ADR 0001: `docs/adr/0001-modular-monolith.md`
- ADR 0012: `docs/adr/0012-maf-as-primary-agent-runtime.md`
