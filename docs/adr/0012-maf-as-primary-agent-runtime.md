# 0012. Microsoft Agent Framework as Primary Agent Runtime

**Date:** 2026-05-04
**Reviewed:** 2026-09-23
**Status:** Deferred
**Deciders:** Foundation spec author; WS5 status follows the [approved remediation design](../superpowers/specs/2026-08-11-ai-harness-architecture-remediation-design.md)

## Context

The Foundation proposal considered Microsoft Agent Framework (MAF) for a future group of travel agents and a small educational custom runtime. Microsoft [announced Agent Framework 1.0 on April 3, 2026](https://devblogs.microsoft.com/agent-framework/microsoft-agent-framework-version-1-0/). Framework availability alone does not establish a product need or an implementation in this repository.

The current [Travel.AI program](../../apps/Travel.AI/Program.cs) registers AnthropicClient.AsIChatClient through Microsoft.Extensions.AI.IChatClient. [NlSearchExtractor](../../apps/Travel.AI/NlSearch/NlSearchExtractor.cs) performs Flights natural-language extraction. There is no MAF or Semantic Kernel runtime registration, no four-agent implementation and no custom Travel Advisor runtime. The original wording that described those agents as hosted/implemented was an unrealized proposal.

## Decision

Defer MAF as a primary runtime until a separate product milestone defines an agent capability, its orchestration requirements, evaluation and failure contracts, and the reason an agent framework is needed. That milestone must review the then-current MAF APIs and provider support, write a new implementation specification, and amend or supersede this ADR before adding runtime packages.

Flights NL-search continues to use direct Anthropic integration behind IChatClient and the accepted shared Core NATS request/reply contract in [ADR 0020](0020-nl-search-cross-service-contract.md). This status change does not alter that runtime path.

## Alternatives for the future milestone

- Keep direct IChatClient calls for a bounded extraction step when no tool loop, session or orchestration is required.
- Adopt MAF when a specified agent workflow needs its supported runtime semantics and the team can test them.
- Build a small educational custom loop only if a concrete teaching deliverable is agreed; it must not be presented as a production travel agent before implementation and tests.

## Consequences

- Current architecture descriptions can name the one implemented AI capability without implying five agents or framework wiring.
- The project does not take on MAF runtime migration or a custom agent engine in WS5.
- A future MAF choice remains open to fresh product requirements and dependency evidence.

## Historical note

The May 2026 proposal preferred four MAF agents plus one custom Travel Advisor and discussed Semantic Kernel and a fully custom runtime as alternatives. Those alternatives remain useful design history, but their original present-tense implementation and connector claims were not supported by the repository at this review.

## Evidence

- [Travel.AI composition](../../apps/Travel.AI/Program.cs) and [NlSearchExtractor](../../apps/Travel.AI/NlSearch/NlSearchExtractor.cs)
- [AI unit tests](../../tests/Travel.AI.Tests/NlSearch/NlSearchExtractorTests.cs)
- [Shared NL-search contract](../../shared/dotnet/Travel.IntegrationContracts.AI/NlSearch/NlSearchContracts.cs) and [ADR 0020](0020-nl-search-cross-service-contract.md)
- [Current architecture overview](../architecture/current-state.md)
