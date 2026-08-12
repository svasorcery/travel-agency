---
name: test-authoring
description: Write focused .NET or TypeScript tests using the repository's seven-layer strategy and nearest existing patterns. Use for unit, integration, HTTP, Aspire, architecture, snapshot, or E2E coverage.
---

Canonical Travel workflow ID: travel-agency/test-authoring.

# Test Authoring

1. Read ADR 0006, the nearest real tests, target code, and module `AGENTS.md`.
2. Select coverage by behavior: unit, integration, HTTP, Aspire, architecture, snapshot, or E2E. Use xUnit v3, Shouldly, and existing fixtures and helpers.
3. Treat shared `IntegrationTestBase` as PostgreSQL-only; it does not supply Redis, NATS, or Keycloak.
4. Keep production edits outside the task unless separately requested. State the test layer, behavior, fixture boundary, and command used.

## Authority

Invoking or auto-loading this skill does not grant additional authority. Follow the user's requested scope. Do not infer permission to stage, commit, push, create or switch branches, generate or apply a migration, deploy, or mutate an external system. A design or review request does not authorize implementation-file edits.
