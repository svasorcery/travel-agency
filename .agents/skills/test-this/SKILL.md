---
name: test-this
description: Select and orchestrate the right tests for an explicit or recently changed file. Use when asked to test the current change and the target or test layer may need discovery.
---

Canonical Travel workflow ID: travel-agency/test-this.

# Test This

1. Prefer the explicit target. Otherwise run `git diff --name-only --diff-filter=ACMR`; select a target only when it is unambiguous, and ask when multiple targets remain.
2. Inspect target behavior and its layer, then use `test-authoring` to choose the smallest fitting coverage: unit, integration, HTTP, Aspire, architecture, snapshot, or E2E.
3. Run only targeted tests needed for the requested change. Report commands, results, and limits of the evidence.
4. Do not require redundant approval when the user already requested test implementation.

## Authority

Invoking or auto-loading this skill does not grant additional authority. Follow the user's requested scope. Do not infer permission to stage, commit, push, create or switch branches, generate or apply a migration, deploy, or mutate an external system. A design or review request does not authorize implementation-file edits.
