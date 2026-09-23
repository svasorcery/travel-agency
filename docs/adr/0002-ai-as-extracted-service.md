# 0002. Travel.AI as an Extracted Service

**Date:** 2026-05-04
**Status:** Accepted
**Deciders:** Foundation spec author

## Accepted amendment — implemented AI boundary (2026-09-23)

The process boundary from the original decision remains: [Travel.AppHost](../../apps/Travel.AppHost/Program.cs) starts Travel.AI separately from Travel.Host, and only Travel.AI registers Anthropic credentials. Repository evidence covers source and disposable local tests, not a production deployment or independent release cadence.

The implemented cross-process capability is Flights NL-search. [Flights](../../modules/flights/Travel.Modules.Flights.Application/Handlers/NlSearch/NlSearchHandler.cs) and [Travel.AI](../../apps/Travel.AI/NlSearch/NlSearchAiHandler.cs) use the same versioned contract over bounded **Core NATS request/reply**, as decided in [ADR 0020](0020-nl-search-cross-service-contract.md). This request does not use JetStream persistence. [Travel.AI Program](../../apps/Travel.AI/Program.cs) registers AnthropicClient.AsIChatClient directly; [ADR 0012](0012-maf-as-primary-agent-runtime.md) is Deferred. No MAF agents, custom Travel Advisor, AI-originated booking mutations or asynchronous AI event workflow are implemented.

[AI persistence](../../apps/Travel.AI/Persistence/AiDbContext.cs) currently contains the cost ledger. The original proposal for prompt/eval/conversation tables and read-only access to Host module schemas is future design. Add those uses only after their product and data contracts are specified. The original Foundation prose below records the broader intent and its alternatives; this amendment is authoritative for current runtime claims.

## Original Foundation context

The original Foundation concept proposed AI-powered features: a Trip Planning Assistant, a Search Advisor, a Price Prediction agent, and the educational Travel Advisor custom runtime. These features share common infrastructure (Microsoft.Extensions.AI abstraction layer, Anthropic SDK, MAF orchestration) but differ radically from the core booking domains in three dimensions: load profile (long-streaming LLM calls, expensive per-token cost, no hard latency SLA), deploy cadence (prompt engineering changes frequently without touching booking logic), and secrets boundary (Anthropic API keys, eval service credentials must not be co-located with booking-domain service credentials for least-privilege reasons).

Keeping AI workloads inside `Travel.Host` would require deploying the entire monolith every time a prompt is tuned, expose all modules to the memory and CPU spikes caused by large model inference, and co-mingle secrets that belong to different operational scopes. In the broader industry AI inference is almost universally extracted to a dedicated service — signalling that the designer understands where to draw process boundaries is itself a portfolio objective.

## Original Foundation decision (runtime details superseded above)

The original decision selected a separate Travel.AI process. It also proposed four MAF agents, a custom Travel Advisor, asynchronous JetStream communication and read-only access to Host module schemas. Those wider runtime and data-sharing claims were design intent, not delivered capabilities. The accepted amendment above states the implemented process, transport and persistence boundary.

## Original alternatives considered

### Option A: AI Features Inside Travel.Host

AI agents and infrastructure run as additional modules within the monolith. No separate process, no NATS hop for AI-originated events.

Rejected because: this collapses the three divergence dimensions into a single deployment unit. Every prompt change triggers a full monolith release. Long-running LLM streaming calls share the thread pool with booking API handlers. Anthropic API keys live in the same configuration scope as booking provider credentials. The portfolio value of demonstrating *where* to extract is also lost.

### Option B: Fully Separate Repository with its Own Database

`Travel.AI` lives in a distinct git repository with its own PostgreSQL instance, maintaining domain data via event replication from `Travel.Host`.

Rejected because: event replication introduces eventual-consistency lag that complicates the read model, requires a CDC or outbox pipeline for Travel.Host → Travel.AI data, and adds substantial infra overhead for a solo OSS showcase. The shared PostgreSQL with schema-level ownership separation gives the same isolation guarantee at a fraction of the operational cost.

## Original anticipated consequences

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
