# 0020. NL-Search Cross-Service Contract (Travel.Host ↔ Travel.AI)

**Date:** 2026-05-13
**Status:** Accepted
**Deciders:** M1 design author

> **Current decision for the implemented Flights NL-search:** see the
> [Accepted amendment dated 2026-08-14](#accepted-amendment-shared-versioned-contract-and-core-nats-proof).
> For this capability, that amendment supersedes only the mirrored-record,
> JSON-snapshot-only, and transport-semantics claims identified there. The
> original sections below are retained unchanged as decision history.

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

## Accepted Amendment: Shared Versioned Contract and Core NATS Proof

**Date:** 2026-08-14
**Status:** Accepted

### Context

The implemented Flights NL-search crosses a Wolverine transport boundary. Matching JSON fields
in two unrelated CLR records do not establish that Wolverine assigns the same message identity,
and a JSON snapshot does not execute the real request/reply route. The original mirrored-record
decision and its 2026-05-14 ratification therefore leave failures that matter to this boundary
unproved.

No production NL-search messages are guaranteed to be in flight at this replacement point, so the
two mirrors can be replaced atomically. This exception does not establish a general policy of
breaking in-place contract changes.

### Amended Decision

**Contract ownership and identity:** `NlSearchRequested` and `NlSearchParsed` have one production
definition in the leaf project
[`Travel.IntegrationContracts.AI`](../../shared/dotnet/Travel.IntegrationContracts.AI/Travel.IntegrationContracts.AI.csproj),
under `Travel.IntegrationContracts.AI.NlSearch`. The project contains transport contracts and their
Wolverine metadata, not application, web, persistence, or domain behavior. Host/Flights and
`Travel.AI` consume this same assembly; neither side owns a mirror.

Version 1 uses explicit wire identities rather than CLR type names:

- `NlSearchRequested`: `travel.ai.nl-search.requested`, `Version = 1`;
- `NlSearchParsed`: `travel.ai.nl-search.parsed`, `Version = 1`.

**Transport semantics:** interactive NL-search uses Core NATS request/reply on subject
`travel.ai.nl_search`. Its reply has value only within the user request, so Flights waits at most
six seconds and maps a timeout or transport failure to `Flights.NlSearchUnparseable`. JetStream is
reserved for durable commands and events whose value survives a missing consumer. JetStream being
available on the broker does not make this subject durable.

**Scale-out delivery:** every compatible `Travel.AI` replica listens in the stable Core NATS queue
group `travel.ai.nl_search.workers`. The group name has no instance, revision, or environment suffix,
so live replicas compete for each request instead of every replica invoking the LLM. If the replica
that handled one request stops, a later request can be handled once by a surviving member. This is
live-subscriber load balancing, not durable storage, replay, or exactly-once processing.

The first rollout from a group-less listener to this queue-group revision is a non-overlap gate.
Operators must drain and stop every old group-less `Travel.AI` replica before starting the new
revision, or switch all replicas atomically. A plain subscription and a queue group are separate
delivery interests, so overlap would send one copy to the old subscriber and another copy to one
queue member. This ADR records the required rollout behavior; no deployment or completed drain is
proved by the repository change.

**Ledger uniqueness:** the `ai.cost_ledger` unique key on
`(MessageIdentity, CorrelationId)` permits at most one persisted row for that pair. It does not
provide exactly-once processing, suppress a repeated LLM call, or implement a response cache or
replay protocol. In particular, the unique insert happens after extraction and cannot suppress
duplicate pre-insert LLM calls if competing handlers receive the same request outside the queue-group
delivery guarantee.

### Repository Evidence and Its Boundary

- [`NlSearchContractShapeTests`](../../tests/Travel.Tests.Contract/Flights/NlSearchContractShapeTests.cs)
  pin the single shared CLR types, explicit identities and version, exact web-JSON shapes, defaults,
  and correlation-id round trips.
- [`IntegrationContractArchitectureTests`](../../tests/Travel.Tests.Architecture/IntegrationContractArchitectureTests.cs)
  enforce the leaf dependency boundary, approved direct consumers, and one production declaration
  for each contract type.
- [`NlSearchTransportTests`](../../tests/Travel.Host.Tests.Integration/NlSearch/NlSearchTransportTests.cs)
  boot the actual `Travel.Host` and two `Travel.AI` `Program` hosts through Alba against disposable
  PostgreSQL and Core NATS containers. Only the replicas' `IChatClient` implementations are replaced
  with deterministic, replica-aware fakes. The test observes the subject, typed reply, correlation,
  and a bounded NATS-monitoring snapshot with two distinct connections on the exact queue group
  before publishing. It proves one total LLM call and one ledger row while both replicas are live;
  stops the replica identified by the reply and observes one remaining queue member before proving
  one call by that survivor for the next request; then observes zero queue members and proves the
  Host fallback after both AI hosts stop.

This evidence is source and disposable-test proof. It does not prove separate operating-system
processes, Aspire orchestration, a live Anthropic call, a shared or live database, migration
application outside the disposable test, or deployment behavior.

### Precedence and Compatibility

For the implemented Flights NL-search only, this amendment takes precedence over:

- this ADR's mirrored-record decision and JSON-snapshot-only M1 ratification;
- [ADR 0002](0002-ai-as-extracted-service.md) only where its general asynchronous/JetStream wording
  could be read as requiring durable delivery for this bounded interactive request/reply; its
  process, data-ownership, secrets, and scaling decisions remain unchanged;
- [ADR 0003](0003-wolverine-marten-stack.md) only where its general cross-process messaging wording
  could be read as selecting one delivery semantic for every Wolverine route; its Critter Stack
  selection and outbox decisions remain unchanged; and
- [ADR 0006](0006-testing-strategy.md) only where it defers Host-to-AI contract coverage or implies
  that snapshots alone are sufficient for this boundary; its seven-layer testing strategy remains
  unchanged.

Any future breaking v2 change requires new message identities, an explicit new version, and a
defined compatibility window in which producers and consumers can be upgraded safely. It must not
mutate the v1 fields or metadata in place. This amendment is not evidence of production rollout;
deployment and compatibility validation remain separate gates.
