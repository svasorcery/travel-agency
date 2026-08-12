# Travel.AI

Travel.AI is a separate Aspire-orchestrated ASP.NET process. Its current product capability is Flights NL-search request/reply over Wolverine/NATS.

## Current implementation

- The LLM path is direct Anthropic behind `Microsoft.Extensions.AI.IChatClient`; Microsoft Agent Framework and Semantic Kernel are not wired at runtime.
- `NlSearchExtractor` owns the prompt and structured extraction. The Wolverine handler and evaluation suite reuse it.
- `NlSearchAiHandler` records cost through `AiDbContext` in schema `ai`, uses `TimeProvider`, and emits OpenTelemetry activity and metrics.
- Cost-ledger migration source exists, but runtime startup migration application and fresh-database readiness are not wired.

## Contract boundary

Host and AI currently define different CLR message types with the same shape. Shape tests do not prove Wolverine message identity or live transport. This is a confirmed WS2 gap: do not create further contracts by copying those types.

Tests: [unit/integration](../../tests/Travel.AI.Tests), [AI evaluations](../../tests/Travel.Tests.AiEvals), and [Flights contract shapes](../../tests/Travel.Tests.Contract/Flights).

Paid Anthropic evaluations require a key and separate authorization. Keep model identifiers and pricing in code/configuration, not these instructions.
