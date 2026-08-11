---
name: domain-modeling
description: Model Travel domain concepts using aggregates, value objects, state transitions, domain events, invariants, and ubiquitous language. Use for domain exploration or design before persistence and transport choices.
---

Canonical Travel workflow ID: travel-agency/domain-modeling.

# Domain Modeling

1. Read the module `AGENTS.md`, ADR 0001, and actual Core before proposing a model.
2. Produce aggregates and identities; state transitions and invariants; value objects; past-tense `IDomainEvent` events; ubiquitous language; and bounded-context notes.
3. Tie each claim to evidence, marking uncertainty and open questions explicitly.
4. Keep persistence and transport out of the model. Do not write code unless requested.

## Authority

Invoking or auto-loading this skill does not grant additional authority. Follow the user's requested scope. Do not infer permission to stage, commit, push, create or switch branches, generate or apply a migration, deploy, or mutate an external system. A design or review request does not authorize implementation-file edits.
