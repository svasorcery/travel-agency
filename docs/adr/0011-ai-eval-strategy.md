# 0011. AI Evaluation Strategy — Own Minimal .NET Eval Framework

**Date:** 2026-05-04
**Reviewed:** 2026-09-23
**Status:** Accepted
**Deciders:** Foundation spec author

> Current accepted scope: the implemented Flights NL-search evaluation runner. A broader persisted or multi-agent eval framework remains future work.

## Context

The implemented AI capability is Flights natural-language extraction, not a portfolio of production travel agents. Prompt changes need repeatable cases that exercise the actual .NET extraction method. Paid model calls require an explicit credential and CI authorization.

The original Foundation rationale inferred that OpenAI ownership would make Promptfoo provider-biased or end its open-source license. Those inferences are withdrawn. [Promptfoo says it remains open source and model-agnostic](https://www.promptfoo.dev/about/), and its [2026 event page](https://www.promptfoo.dev/events/defcon-2026/) identifies the scanner as MIT-licensed. [OpenAI announced the acquisition agreement in March 2026](https://openai.com/index/openai-to-acquire-promptfoo/). No claim about Promptfoo's bias or future licensing is needed for this decision.

## Decision

Keep the first evaluation suite close to the [NlSearchExtractor](../../apps/Travel.AI/NlSearch/NlSearchExtractor.cs) it exercises. The current [NlSearchEvalRunner](../../tests/Travel.Tests.AiEvals/Flights/NlSearchEvalRunner.cs) reads versioned JSON cases, calls an Anthropic-backed IChatClient only when ANTHROPIC_API_KEY is present, asserts deterministic destination/date/shape outcomes, and computes an aggregate pass-rate threshold. The [paid CI lane](../../.github/workflows/ci.yml) is separately gated.

This is an executable Flights eval runner, not the broader persisted evaluation framework proposed in Foundation. It does not write eval-run history to PostgreSQL, use an LLM-as-judge, grade four production agents, or support a vendor switch through configuration alone. The [AiDbContext](../../apps/Travel.AI/Persistence/AiDbContext.cs) currently stores the NL-search cost ledger only.

A future multi-agent or persisted eval product is a separate decision. It can compare the then-current .NET test approach with Promptfoo and other tools using actual requirements and cost.

## Alternatives considered

- Promptfoo: a capable open-source, multi-provider eval tool. The first NL-search suite stays in .NET because it can call the exact extraction method and reuse repository test conventions; no claim of inherent Promptfoo bias is part of the choice.
- SaaS eval services: may provide dashboards and annotations, but would add an account and external data path to the mandatory local demo.
- No repeatable eval cases: rejected because prompt/model changes would have no controlled regression signal.

## Consequences

- Existing cases and the pass-rate gate are inspectable in the test project.
- A local solution test must exclude Category=AiEval and Category=AiEvals unless the paid run is separately authorized; the runner calls Anthropic whenever a key is available.
- Results remain test output. Persisted history, subjective judging, visual review and broader agent evaluation are future work.
- IChatClient keeps the extractor boundary narrow, while the current eval runner still constructs an Anthropic client directly.

## Evidence

- [Parameterized eval runner](../../tests/Travel.Tests.AiEvals/Flights/NlSearchEvalRunner.cs) and [cases](../../tests/Travel.Tests.AiEvals/Flights/nl-search-cases.json)
- [Current AI database model](../../apps/Travel.AI/Persistence/AiDbContext.cs)
- [CI paid lane](../../.github/workflows/ci.yml)
- [Current architecture overview](../architecture/current-state.md)
