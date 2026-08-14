# Travel.AI

Travel.AI is a separate Aspire-orchestrated ASP.NET process. Its current product capability is Flights NL-search request/reply over Wolverine/NATS.

## Current implementation

- The LLM path is direct Anthropic behind `Microsoft.Extensions.AI.IChatClient`; Microsoft Agent Framework and Semantic Kernel are not wired at runtime.
- `NlSearchExtractor` owns the prompt and structured extraction. The Wolverine handler and evaluation suite reuse it.
- `NlSearchAiHandler` records cost through `AiDbContext` in schema `ai`, uses `TimeProvider`, and emits OpenTelemetry activity and metrics.
- Cost-ledger migration source exists, but runtime startup migration application and fresh-database readiness are not wired.

## Contract boundary

Host/Flights and AI consume the same versioned messages from the leaf `Travel.IntegrationContracts.AI` project; neither side owns a mirrored contract. The v1 Wolverine identities are `travel.ai.nl-search.requested` and `travel.ai.nl-search.parsed`, both with `Version = 1`. Interactive NL-search uses Core NATS request/reply on `travel.ai.nl_search`; JetStream is reserved for commands and events whose value survives a missing consumer. Flights bounds the call at six seconds and maps timeout or transport failure to `Flights.NlSearchUnparseable`.

The cost-ledger unique key `(MessageIdentity, CorrelationId)` permits at most one row for that pair; it is not exactly-once processing, LLM-call deduplication, a response cache, or replay. A future incompatible v2 requires new message identities, a new explicit version, and a compatibility window; never mutate the v1 contract in place.

Tests: [unit/integration](../../tests/Travel.AI.Tests), [AI evaluations](../../tests/Travel.Tests.AiEvals), [contract identity and JSON shape](../../tests/Travel.Tests.Contract/Flights/NlSearchContractShapeTests.cs), [architecture boundaries](../../tests/Travel.Tests.Architecture/IntegrationContractArchitectureTests.cs), and [disposable Core NATS transport](../../tests/Travel.Host.Tests.Integration/NlSearch/NlSearchTransportTests.cs).

The transport proof boots the real Host and AI `Program` entry points through Alba with disposable PostgreSQL and Core NATS; only `IChatClient` is faked. It does not prove separate operating-system processes, Aspire orchestration, live Anthropic, a shared/live database, migration application outside the test, or deployment.

Paid Anthropic evaluations require a key and separate authorization. Keep model identifiers and pricing in code/configuration, not these instructions.
