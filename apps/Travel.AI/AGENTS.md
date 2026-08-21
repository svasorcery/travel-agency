# Travel.AI

Travel.AI is a separate Aspire-orchestrated ASP.NET process. Its current product capability is Flights NL-search request/reply over Wolverine/NATS.

## Current implementation

- The LLM path is direct Anthropic behind `Microsoft.Extensions.AI.IChatClient`; Microsoft Agent Framework and Semantic Kernel are not wired at runtime.
- `NlSearchExtractor` owns the prompt and structured extraction. The Wolverine handler and evaluation suite reuse it.
- `NlSearchAiHandler` records cost through `AiDbContext` in schema `ai`, uses `TimeProvider`, and emits OpenTelemetry activity and metrics.
- Development and Testing apply the checked-in cost-ledger migrations during ordered startup; readiness remains unhealthy until initialization succeeds. Production never auto-migrates and instead requires the checked-in schema to match before readiness can become healthy.

## Contract boundary

Host/Flights and AI consume the same versioned messages from the leaf `Travel.IntegrationContracts.AI` project; neither side owns a mirrored contract. The v1 Wolverine identities are `travel.ai.nl-search.requested` and `travel.ai.nl-search.parsed`, both with `Version = 1`. Interactive NL-search uses Core NATS request/reply on `travel.ai.nl_search`; every compatible Travel.AI replica joins the stable queue group `travel.ai.nl_search.workers`, so one live replica handles each request and another can take later requests after a replica stops. Do not add instance, revision, or environment suffixes to that group. JetStream is reserved for commands and events whose value survives a missing consumer. Flights bounds the call at six seconds and maps timeout or transport failure to `Flights.NlSearchUnparseable`.

The first deployment of this queue-group listener must not overlap any older group-less Travel.AI replica. Drain and stop every old replica before starting the queue-group revision, or switch all replicas atomically; a plain subscriber and a queue group both receive the same publication. The repository proves the source and disposable topology only—it does not prove that this rollout gate has been executed in a deployed environment.

The cost-ledger unique key `(MessageIdentity, CorrelationId)` permits at most one row for that pair; it is not exactly-once processing, LLM-call deduplication, a response cache, or replay. In particular, it cannot suppress duplicate LLM calls before competing handlers reach the insert; the queue-group delivery guarantee is the protection for this live scale-out transport. A future incompatible v2 requires new message identities, a new explicit version, and a compatibility window; never mutate the v1 contract in place.

Tests: [unit/integration](../../tests/Travel.AI.Tests), [AI evaluations](../../tests/Travel.Tests.AiEvals), [contract identity and JSON shape](../../tests/Travel.Tests.Contract/Flights/NlSearchContractShapeTests.cs), [architecture boundaries](../../tests/Travel.Tests.Architecture/IntegrationContractArchitectureTests.cs), and [disposable Core NATS transport](../../tests/Travel.Host.Tests.Integration/NlSearch/NlSearchTransportTests.cs).

The transport proof boots the real Host and two AI `Program` hosts through Alba with disposable PostgreSQL and Core NATS; only each replica's `IChatClient` is faked. Before publishing, bounded NATS monitoring proves two distinct connections on the exact subject and queue group; the same barrier proves one member after the responder stops and zero after both stop. The test then proves one LLM call and one ledger row per request, service by the surviving replica, and the typed Host fallback after both stop. It does not prove separate operating-system processes, Aspire orchestration, live Anthropic, a shared/live database, migration application outside the test, or deployment.

Paid Anthropic evaluations require a key and separate authorization. Keep model identifiers and pricing in code/configuration, not these instructions.
