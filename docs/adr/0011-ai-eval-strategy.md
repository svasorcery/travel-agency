# 0011. AI Evaluation Strategy — Own Minimal .NET Eval Framework

**Date:** 2026-05-04
**Status:** Accepted
**Deciders:** Foundation spec author

## Context

The Travel platform uses Claude (Anthropic) as its primary LLM for four production AI agents and one educational custom agent (Travel Advisor). Evaluating agent quality — response correctness, instruction following, latency, cost per task — requires a repeatable evaluation framework that runs against real or recorded model responses. Without evals, prompt changes are deployed on intuition; regressions are discovered in production rather than in CI.

The eval tooling landscape shifted materially in early 2026: **Promptfoo**, the dominant open-source LLM evaluation framework (YAML-driven test suites, multi-model comparison, red-teaming), was acquired by OpenAI in March 2026. After the acquisition, Promptfoo's roadmap prioritises OpenAI model evaluation and the project's claimed neutrality in cross-provider comparisons is no longer credible. A Claude-primary showcase that uses a post-acquisition OpenAI-owned eval tool to benchmark Claude versus GPT-4o sends a contradictory message. Additionally, the acquisition introduced uncertainty about whether the MIT license of the pre-acquisition codebase will be maintained for future versions.

Other commercial and SaaS eval platforms (Arize Phoenix, LangSmith, Weights & Biases Weave) introduce SaaS dependencies, require API keys, and add ongoing cost to an OSS project that must run fully offline.

## Decision

The platform builds and maintains a **minimal eval framework in .NET**, located at `tests/Travel.Tests.AiEvals/`. The framework is intentionally minimal: it wraps the Anthropic SDK (`IChatClient`), defines a typed `EvalCase` record (input, expected behaviour, grading rubric), runs cases against the configured model, records results to the `ai` schema eval tables (eval run ID, case name, model response, pass/fail, latency ms, token cost), and produces a human-readable summary. Grading is: deterministic string matching for factual assertions; LLM-as-judge (a second Claude call) for subjective quality assertions.

The framework lives in the test project rather than application code. It is not a general-purpose eval platform — it is purpose-built for the Travel platform's agents and can be extended as needed without version compatibility concerns. `Travel.Tests.AiEvals/` is provisioned as an empty project in Foundation; the first actual eval cases are written in the subproject that ships the first AI feature.

## Alternatives Considered

### Option A: Promptfoo (pre-acquisition)

Promptfoo was a well-designed YAML-driven eval framework with multi-model comparison, red-teaming support, and an active OSS community before March 2026.

Rejected because: Promptfoo was acquired by OpenAI in March 2026. For a showcase whose primary differentiating claim is Claude-quality agent implementation, using an OpenAI-owned tool for model evaluation creates a structural conflict of interest. Evals run post-acquisition could be perceived as biased toward OpenAI models in scoring rubrics, provider-specific extensions, or default configurations. The acquisition also raises license continuity questions for OSS consumers of future Promptfoo versions.

### Option B: Arize Phoenix or LangSmith

Production-grade LLM observability and eval platforms with trace capture, dataset management, and human annotation workflows.

Rejected because: both are SaaS platforms requiring API key registration and network connectivity. Running evals in CI against a SaaS eval backend introduces a network dependency, potential cost, and the risk of provider-side data retention for what may be sensitive travel query fixtures. The Travel platform is designed to run entirely locally and self-hosted; eval infrastructure must satisfy the same constraint.

### Option C: No Formal Eval Framework

Rely on manual testing of agent responses during development; add evals only if quality regressions are observed in production.

Rejected because: this is the approach that produces "vibes-driven" prompt engineering. Without reproducible evals, there is no signal for whether a prompt change improved or degraded quality across the evaluation set. For a portfolio demonstrating AI engineering discipline, having no eval framework actively undermines credibility.

## Consequences

### Positive
- The eval framework is .NET-native and runs in the same CI pipeline as application tests, using the same xUnit v3 runner. No separate tooling process or language context switch.
- Results are persisted to the `ai` schema in PostgreSQL alongside cost ledger entries, making eval history queryable and auditable.
- The framework is vendor-neutral by construction: it calls `IChatClient` (Microsoft.Extensions.AI abstraction), so swapping from Claude to another model is a configuration change, not a framework change.

### Negative / Trade-offs
- Building a custom eval framework, even a minimal one, is ongoing maintenance work. As the number of agents and eval cases grows, the framework will need to evolve (parallel execution, dataset versioning, regression diff views). This is work that Promptfoo's pre-acquisition version provided for free.
- LLM-as-judge grading (using Claude to evaluate Claude's output) has a known self-consistency bias: the judge and the judged share the same model family's preferences. This limits the reliability of subjective quality assertions and must be acknowledged in eval result interpretation.

### Neutral
- The `Travel.Tests.AiEvals/` project is a test project, not a library. It is not imported by application code. If a superior eval framework emerges post-Promptfoo that is MIT-licensed and provider-neutral, migrating the eval cases is straightforward because the framework surface is thin.

## Out of Scope

- Red-teaming and adversarial prompt injection evaluation — not in Foundation scope; deferred to the subproject where agent security is first addressed.
- Automated eval gating in CI (failing the build if eval pass rate drops below a threshold) — infrastructure concern, decided when the first eval cases are written.

## References

- Concept doc: `docs/superpowers/specs/2026-05-03-travel-platform-concept.md` § 6.3
- Foundation spec: `docs/superpowers/specs/2026-05-04-foundation-design.md` § 6 (row 0011), § 5.4
- ADR 0002: `docs/adr/0002-ai-as-extracted-service.md`
- Microsoft.Extensions.AI `IChatClient`: https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai
- Promptfoo acquisition announcement (March 2026) — referenced as context for the decision driver
