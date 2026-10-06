---
name: spec
description: Design and write a Travel feature or subproject specification. Use when a request needs requirements clarification, alternatives, architecture decisions, scope boundaries, acceptance criteria, and approval before implementation.
---

Canonical Travel workflow ID: travel-agency/spec.

# Specification

1. Read the root and relevant nested `AGENTS.md`, current code, and applicable concept, specification, and ADR evidence.
2. Resolve only material ambiguity. Offer two or three viable approaches with trade-offs, then present the proposed design for review.
3. Define scope, boundaries, decisions, acceptance criteria, risks, and verification. Write `docs/superpowers/specs/YYYY-MM-DD-topic-design.md` only when that documentation is within the approved scope.
4. Self-review for contradictions, unsupported claims, and placeholders. Do not begin implementation before design approval.

## Authority

Invoking or auto-loading this skill does not grant additional authority. Follow the user's requested scope. Do not infer permission to stage, commit, push, create or switch branches, generate or apply a migration, deploy, or mutate an external system. A design or review request does not authorize implementation-file edits.

## OpenSpec pilot routing

For Flights M3 whole-order cancellation, read and update the existing canonical corpus at `openspec/changes/flights-m3-cancellation`; do not create a second feature specification or plan under `docs/superpowers`. Use the pinned `node tools/openspec/run.mjs` from the physical repository root. Existing ADRs remain global decisions. Follow the current execution scope in the change's process log; tooling approval does not authorize product implementation or publication.
