# 0012. Microsoft Agent Framework as Primary Agent Runtime

**Date:** 2026-05-04
**Status:** Accepted
**Deciders:** Foundation spec author

## Context

The Travel platform's AI service (`Travel.AI`) hosts five agent implementations: Trip Planning Assistant, Search Advisor, Price Prediction, a Booking Confirmation assistant, and the educational Travel Advisor. Building agent infrastructure from scratch — tool registry, conversation memory, multi-step planning, A2A (agent-to-agent) communication, telemetry hooks — is substantial engineering work that is orthogonal to the domain value the platform is trying to demonstrate.

Two framework options existed at project start: build a custom runtime for all five agents (demonstrating deep understanding of agent internals), or adopt a production-grade framework for most agents and build a minimal custom runtime for exactly one (demonstrating both: production-path judgement and internal understanding). The choice was influenced by a significant ecosystem event: **Microsoft Agent Framework (MAF) 1.0 reached General Availability between April 3 and 7, 2026**, consolidating the previously separate Semantic Kernel and AutoGen projects into a unified, stable agent framework with a first-party Anthropic Claude connector.

Before the MAF GA, the original plan considered a custom-primary / MAF-reference split (custom runtime for most agents, MAF as a reference implementation). After the GA, reversing this polarity became clearly correct: using a custom runtime as the production path for a project starting in May 2026 — when a mature, Microsoft-backed, Claude-integrated framework is available — reads as NIH (Not Invented Here) engineering without a concrete justification.

## Decision

**Microsoft Agent Framework 1.0** is the primary agent runtime for **four production agents** in `Travel.AI`: Trip Planning Assistant, Search Advisor, Price Prediction, and Booking Confirmation Assistant. These agents use MAF's stable APIs only — orchestration and workflow features marked as preview in the 1.0 release are explicitly excluded (see Out of Scope). Claude is integrated via MAF's first-party Anthropic connector; the `IChatClient` abstraction from Microsoft.Extensions.AI is used at integration boundaries to preserve provider-neutrality in shared infrastructure.

**Travel Advisor** (the fifth agent) is implemented on a **minimal custom runtime** built within `Travel.AI`. This custom runtime is intentionally small: it implements a basic tool-call loop, a simple conversation memory, and observability hooks. Its sole purpose is educational — a "deconstruction" artifact that shows contributors how agent framework capabilities are implemented, as a companion to the MAF-based agents. It is not a production alternative to MAF and is documented as such in its CLAUDE.md.

## Alternatives Considered

### Option A: Semantic Kernel Directly (pre-consolidation)

Semantic Kernel (SK) was the primary Microsoft agent library before its consolidation into MAF. It provided plugin-based tool registration, planner strategies, and memory connectors.

Rejected because: SK was merged into MAF as part of the 1.0 consolidation. Using SK directly post-consolidation means using a deprecated API surface. New projects starting after April 2026 should target MAF, not the pre-consolidation SK packages.

### Option B: Fully Custom Runtime for All Five Agents

Implement all agent orchestration, tool registration, memory, and A2A communication as project-specific code. No dependency on MAF.

Rejected because: a project starting in May 2026 with MAF 1.0 GA available has no concrete orchestration requirement that MAF cannot express. A fully custom multi-agent runtime is a significant engineering investment that crowds out the domain-modelling and integration work that represents the platform's primary portfolio value. This would be NIH at scale: reinventing agent infrastructure that is already MIT-adjacent and maintained by Microsoft.

### Option C: Fully MAF Including Travel Advisor

Implement all five agents on MAF; no custom runtime at all.

Rejected because: this removes the educational artifact that demonstrates agent framework internals. One of the explicit portfolio objectives is showing that the developer understands *what is inside* a framework, not just how to configure one. A minimal custom runtime for a single agent at the "deconstruction" scale costs modest engineering effort and produces a high-value teaching artifact (blog post, walkthrough video). Without it, the AI section of the portfolio demonstrates framework consumption but not framework comprehension.

## Consequences

### Positive
- Four production agents on MAF benefit from Microsoft's ongoing framework maintenance, the first-party Claude connector, built-in OpenTelemetry hooks, and compatibility with the wider Microsoft.Extensions.AI ecosystem.
- The custom Travel Advisor runtime is scope-bounded to one agent; it cannot grow into a shadow framework without an explicit architectural decision to promote it.
- MAF's first-party Anthropic connector eliminates the need for a custom `IChatClient` wrapper for Claude in the production agents.

### Negative / Trade-offs
- MAF 1.0 GA stabilises the core agent APIs, but several orchestration and multi-agent workflow features are marked **preview** in the 1.0 release. If those preview features are needed for a production agent, the project either waits for GA stabilisation or accepts a preview dependency that may change. This ADR explicitly restricts the four production agents to stable APIs only.
- MAF is a Microsoft product. If MAF's development direction diverges from the platform's needs (e.g., reduced investment in the Claude connector, architectural shifts in 2.0), migration costs are non-trivial for four agents.

### Neutral
- The "MAF primary, custom educational" split is a deliberate inversion of the originally planned "custom primary, MAF reference" layout. The inversion was driven by the April 2026 GA event. Future readers should understand this context: the architecture reflects the state of the ecosystem at project inception, not a permanent philosophical preference for either approach.

## Out of Scope

- MAF preview features (multi-agent workflow orchestration, stateful agent session persistence, advanced planner strategies) — explicitly excluded from the four production agents in Foundation; may be revisited in subprojects as features stabilise.
- A2A (agent-to-agent) communication protocol and routing — implementation detail, not an architecture decision at Foundation level.
- Which specific tools each production agent registers — deferred to the subproject where the agent is first implemented.

## References

- Concept doc: `docs/superpowers/specs/2026-05-03-travel-platform-concept.md` § 4.1, § 4.1.1, § 5.4
- Foundation spec: `docs/superpowers/specs/2026-05-04-foundation-design.md` § 6 (row 0012), § 5.4
- ADR 0002: `docs/adr/0002-ai-as-extracted-service.md`
- Microsoft Agent Framework 1.0 GA (April 3–7, 2026): https://devblogs.microsoft.com/semantic-kernel
- Microsoft.Extensions.AI `IChatClient`: https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai
