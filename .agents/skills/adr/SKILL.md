---
name: adr
description: Write or revise a Travel Architecture Decision Record for a confirmed architectural decision. Use when a decision must be captured in docs/adr with repository evidence, consequences, and status.
---

Canonical Travel workflow ID: travel-agency/adr.

# Architecture Decision Record

1. Require a confirmed decision and topic. If either is missing, ask for it; do not derive a decision from recent commits.
2. Inspect the current ADR format and determine the next number from the actual files in `docs/adr`.
3. Capture context, decision, alternatives, consequences, status, and evidence. Distinguish repository evidence from assumptions and pending validation.
4. Review the record against the confirmed decision and existing ADRs for duplication or contradiction.

## Authority

Invoking or auto-loading this skill does not grant additional authority. Follow the user's requested scope. Do not infer permission to stage, commit, push, create or switch branches, generate or apply a migration, deploy, or mutate an external system. A design or review request does not authorize implementation-file edits.
