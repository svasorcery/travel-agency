# 0020. NL-Search Cross-Service Contract (Travel.Host ↔ Travel.AI)

**Date:** 2026-05-13
**Status:** Accepted
**Deciders:** M1 design author

## Context

The Flights M1 natural-language search feature requires parsing a free-form user query (e.g. "из Москвы в Санкт-Петербург 25 июня 2026") into structured flight-search criteria (`NlSearchParsed`). The parsing involves an LLM call, which is allocated to `Travel.AI` — a separately deployed process — per ADR 0002 (AI as Extracted Service).

This creates a cross-service message contract: `Travel.Host` publishes a `NlSearchRequested` message and consumes the `NlSearchParsed` reply. Both services must agree on the exact JSON wire shape of these two records, and any field rename or type change on either side must be caught before it reaches production.

Two questions must be answered:
1. How are the contract records shared between the two processes?
2. How is contract drift detected?

## Decision

**Transport:** `Travel.Host` invokes `Travel.AI` via Wolverine request/reply over NATS on subject `travel.ai.nl_search`. The call is fire-request / await-reply with a 6-second timeout; on timeout the host handler returns the `Flights.NlSearchUnparseable` error to the caller.

**Contract record placement:** The `NlSearchRequested` and `NlSearchParsed` records are duplicated as *mirrored records* on each side of the service boundary — no shared NuGet package in M1.

- `Travel.AI.NlSearch.Contracts.NlSearchRequested` / `NlSearchParsed` — authoritative definitions in `apps/Travel.AI/NlSearch/Contracts/NlSearchContracts.cs`
- `Travel.Modules.Flights.Application.NlSearch.NlSearchRequested` / `NlSearchParsed` — mirror definitions in the Flights module

**Contract test:** The wire shape of both messages is pinned by a Verify-snapshot test in `tests/Travel.Tests.Contract/Flights/NlSearchContractShapeTests.cs`. The test serialises representative instances to JSON and compares them to committed `.verified.txt` snapshots. Any drift causes a CI failure. Full bidirectional Pact message verification (PactNet 5.x `IMessagePactBuilderV4`) is deferred to M2 when more cross-service messages are expected to justify the Pact infrastructure.

**Extraction refactor:** The LLM prompt-and-parse logic is extracted from `NlSearchAiHandler` into a static `NlSearchExtractor.ExtractAsync(IChatClient, string, CancellationToken)` method. The Wolverine handler delegates to it, and the AI-eval suite calls it directly without needing Wolverine or a database.

## Alternatives Considered

### Option A: Run NL parsing in-process in Travel.Host

Embed the `IChatClient` call directly in the Flights module inside `Travel.Host`, eliminating the NATS round-trip.

**Rejected.** ADR 0002 places all LLM calls in the extracted `Travel.AI` service for independent deploy cadence, cost isolation, and separate horizontal scaling. Violating this boundary for the first real AI feature would undermine the architectural principle established for the entire platform.

### Option B: Shared `Travel.Contracts` NuGet package

Publish the `NlSearchRequested` / `NlSearchParsed` records to a shared package referenced by both `Travel.Host` and `Travel.AI`.

**Rejected for M1.** ADR 0002 (§4.3) explicitly defers shared contract packages until multiple cross-service message types justify the build-system overhead (versioning, publish pipeline, downstream upgrade coordination). With only one message pair in M1 the duplication cost is lower than the coupling cost. This decision will be revisited when a second distinct cross-service message pair appears.

## Consequences

**Positive:**
- AI prompt iteration and model upgrades can be deployed to `Travel.AI` without redeploying the monolith.
- LLM API key and cost tracking remain isolated in `Travel.AI` — `Travel.Host` has no Anthropic dependency.
- The 6-second timeout provides a bounded user-facing latency and a graceful degradation path.
- `NlSearchExtractor` is independently testable by the AI-eval suite without Wolverine infrastructure.

**Negative / trade-offs:**
- The duplicated contract records are a small maintenance cost: a field rename must be applied in two places. The Verify-snapshot test makes this visible immediately but does not prevent it automatically.
- The 6-second NATS round-trip adds latency relative to an in-process call; this is acceptable for a search-assist feature (not a booking transaction).
- Full Pact message verification is deferred, meaning the snapshot only pins shape — it does not verify that a `NlSearchRequested` consumed by `Travel.AI` actually produces a `NlSearchParsed` matching the contract. This gap is accepted for M1.

## M1 Ratification: JSON-Snapshot as the M1 Contract Mechanism

**Date:** 2026-05-14

The M1 remediation design (decision D7) ratifies the Verify-snapshot approach as the complete
contract mechanism for M1. Both contract sides (`Travel.Host` mirror and `Travel.AI` authoritative
record) live in the same monorepo. Drift is caught at build by the two-sided snapshot test in
`tests/Travel.Tests.Contract/Flights/NlSearchContractShapeTests.cs`, which serialises
representative instances of both sides and compares them to committed `.verified.txt` snapshots.
A field rename or type change on either side breaks the CI snapshot test before the branch merges.

Full bidirectional Pact message verification (PactNet 5.x `IMessagePactBuilderV4`) is deferred to
M2, when `Travel.AI` is expected to publish additional cross-service message types that justify the
Pact infrastructure investment (a Pact broker, consumer/provider test split, and publish pipeline).

The snapshot test is sufficient for M1 because both services share a single repository and
deployment cycle; there is no scenario in M1 where `Travel.Host` and `Travel.AI` could diverge
in production without a passing contract test.
