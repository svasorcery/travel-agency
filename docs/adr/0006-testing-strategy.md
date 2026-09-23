# 0006. Seven-Layer Testing Strategy

**Date:** 2026-05-04
**Status:** Accepted
**Deciders:** Foundation spec author

## Context

The Travel platform encompasses a modular .NET backend, a separate AI service, an Angular frontend, external provider integrations, and cross-module architectural boundaries. No single test type covers all the failure modes that matter: a unit test cannot detect a mis-wired Wolverine handler, an integration test cannot detect a module importing another module's internals, and an end-to-end test cannot pinpoint which layer failed. A coherent testing strategy names each layer, assigns it a specific technology, and specifies what it is responsible for verifying.

One additional forcing function shaped the assertion library choice: **FluentAssertions 8.0** moved to a commercial license under Xceed in January 2025. An OSS showcase repository that depends on FluentAssertions 8.x is not cleanly open-source. A community fork (AwesomeAssertions) exists and is active, but adopting it means taking a dependency on a project whose long-term maintenance is uncertain and whose name trades on confusion with the original. The cleaner choice is an assertion library that was never commercial.

## Decision

The platform uses a **seven-layer testing strategy**:

1. **Unit tests** — xUnit v3, **Shouldly** assertions (BSD-2-Clause; MIT-style). Shouldly was chosen over FluentAssertions (commercial since Jan 2025) and AwesomeAssertions (uncertain fork). Pure domain logic, value objects, handlers without I/O.
2. **Integration tests** — xUnit v3 with Testcontainers for implemented PostgreSQL-backed modules. Scaffold test projects remain empty until their module gains behavior. IntegrationTestBase supplies the shared disposable PostgreSQL lifecycle.
3. **HTTP tests** — xUnit v3 with two complementary fixtures: a lightweight FlightsApiFixture runs real ASP.NET routing/auth/model binding with a fake message bus; real Host WebApplicationFactory/Alba tests run Program.cs against disposable PostgreSQL and inspect Wolverine wiring/OpenAPI. The CI inventory puts trait-free HTTP tests and Docker-backed integration tests in separate lanes.
4. **Full-stack smoke tests** — DistributedApplicationTestingBuilder starts disposable AppHost resources (PostgreSQL, Redis, NATS, Keycloak, Mailpit, Host and AI) and makes real local HTTP calls. The AspireSmoke trait runs in its CI lane and can also run locally with Docker; it is not live deployment proof.
5. **Architecture tests** — ArchUnitNET, evaluated MSBuild ProjectReferences and Mono.Cecil IL inspection. They cover the five-module project/type matrix, method-body dependencies, Host Api-facade imports, Shared and integration-contract consumers, and positive selectors.
6. **Snapshot tests** — Verify. The implemented Host OpenAPI snapshot is committed as a verified file; additional domain/API snapshots are added only when a concrete contract needs them.
7. **Browser E2E** — Playwright in tests/travel-e2e. The implemented baseline checks the Angular /status page against a running local stack. Screenshot regression and booking UI flows remain future work.

The current contract project includes shared NL-search CLR/wire identity and shape tests. Consumer-driven Pact contracts for broader boundaries remain a possible later addition.

## Alternatives Considered

### Option A: FluentAssertions (pre-commercialization) + NUnit

The dominant .NET assertion + test framework combination before 2025. Large community, extensive documentation, familiar to most .NET developers.

Rejected because: FluentAssertions 8.0+ requires a commercial Xceed license; an OSS portfolio reference must not depend on commercial assertions. NUnit and xUnit v3 are comparable; xUnit v3 was chosen for its explicit async test support and xUnit.v3 runner compatibility with newer test infrastructure.

### Option B: Single Integration Test Layer Only

Skip dedicated unit tests and architecture tests; rely on Alba HTTP integration tests to cover everything.

Rejected because: integration tests are slow and require Docker; fast unit tests catch domain logic errors in milliseconds. Architecture tests are the only reliable enforcement mechanism for module boundary rules — they catch violations that code review might miss. Collapsing all testing to a single layer sacrifices both speed and specialised enforcement.

## Consequences

### Positive
- Each layer has a specific, non-overlapping responsibility; contributors know which test to write for which type of failure.
- ArchUnitNET architecture tests provide automated enforcement of the modular-monolith boundary rules (ADR 0001) without relying on code review discipline alone.
- Lightweight FlightsApiFixture tests verify HTTP routing and authorization without Docker; real Host HTTP tests prove composition/OpenAPI with disposable PostgreSQL and no separately deployed server.

### Negative / Trade-offs
- Seven layers means seven tooling decisions to maintain and keep compatible. When a test framework releases a breaking version, it must be updated across all affected layers. The Aspire smoke test layer in particular is sensitive to Aspire and NATS version alignment.
- Testcontainers adds Docker as a hard dependency for integration test execution. Developers without Docker cannot run integration tests locally without configuration changes.

### Neutral
- The AI eval project now contains paid, credential-gated Flights NL-search cases. ADR 0011 states the implemented scope and limits; mandatory local solution checks exclude paid categories.

## Out of Scope

- Specific test coverage targets — the project does not impose a numeric coverage gate; architecture and integration tests are the quality gates.
- Load and performance testing — not in Foundation scope; deferred to a future subproject.
- Contract testing between Travel.Host and external providers — each provider integration has its own ACL and stub; Pact is for the internal Host ↔ AI boundary only.

## References

- Concept doc: `docs/superpowers/specs/2026-05-03-travel-platform-concept.md` § 4.4
- Foundation spec: `docs/superpowers/specs/2026-05-04-foundation-design.md` § 6 (row 0006), § 8
- Shouldly: https://shouldly.io (BSD-2-Clause)
- Alba: https://jasperfx.github.io/alba
- ArchUnitNET (TNG): https://github.com/TNG/ArchUnitNET
- Verify: https://github.com/VerifyTests/Verify
- Aspire `DistributedApplicationTestingBuilder`: https://learn.microsoft.com/en-us/dotnet/aspire/testing
