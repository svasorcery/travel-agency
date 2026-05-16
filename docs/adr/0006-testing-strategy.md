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
2. **Integration tests** — xUnit v3 + **Testcontainers** for PostgreSQL. Each module's integration tests run against a real Postgres container. `IntegrationTestBase` in `Travel.Shared.TestInfrastructure` provides the shared container lifecycle.
3. **HTTP integration tests** — **Alba** (`AlbaHost.For<Program>()`). Spins up the real `Program.cs` pipeline against a Testcontainers Postgres. Exercises WolverineFx.Http handlers end-to-end without a network hop. The canonical pattern for testing HTTP endpoints in this codebase.
4. **Full-stack smoke tests** — **`DistributedApplicationTestingBuilder`** (Aspire). Starts the entire Aspire application (Postgres, Redis, NATS, Keycloak, Mailpit, Travel.Host, Travel.AI) and makes real HTTP calls. Tagged `[Trait("Category", "AspireSmoke")]`; runs only in CI on PR (~60–90 seconds). Located in `tests/Travel.Host.Tests.Integration/AspireStackSmokeTests.cs`.
5. **Architecture tests** — **ArchUnitNET** (TNG, 0.13.x, pure C# NuGet, no Java dependency). Three rule sets: module boundary isolation, dependency direction, naming conventions. Tagged `[Trait("Category", "Architecture")]`. Run on every build.
6. **Snapshot tests** — **Verify**. Serialises complex objects (domain event payloads, API responses, projection states) to `.verified.txt` files committed to the repository. Regression is detected as a diff rather than a broken assertion.
7. **Browser E2E** — **Playwright** with `toHaveScreenshot()` for visual regression. `travel-e2e/` project. Foundation creates one `health.spec.ts` as the baseline; functional E2E grows in subprojects.

Contract tests (Pact.NET / Pact-JS) are provisioned as `Travel.Tests.Contract/` but populated in Subproject 5 when the `Travel.Host` ↔ `Travel.AI` boundary is stable enough for formal consumer-driven contracts.

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
- Alba tests verify the full WolverineFx.Http routing and handler wiring without requiring a running server, making them faster and more deterministic than full Aspire smoke tests.

### Negative / Trade-offs
- Seven layers means seven tooling decisions to maintain and keep compatible. When a test framework releases a breaking version, it must be updated across all affected layers. The Aspire smoke test layer in particular is sensitive to Aspire and NATS version alignment.
- Testcontainers adds Docker as a hard dependency for integration test execution. Developers without Docker cannot run integration tests locally without configuration changes.

### Neutral
- `Travel.Tests.AiEvals/` is provisioned in Foundation as an empty project. The AI eval framework is a separate ADR decision (ADR 0011); the test location is established here as a convention, not a content decision.

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
