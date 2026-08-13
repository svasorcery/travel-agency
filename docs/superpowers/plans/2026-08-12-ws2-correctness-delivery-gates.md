# WS2 Correctness and Delivery Gates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the platform's current correctness claims executable on a clean machine: one real versioned Host/AI message contract, reliable database bootstrap and production schema checks, explicit configuration and health semantics, complete `dev` CI inventory, and a fresh-volume functional smoke that uses no paid provider.

**Architecture:** WS2 is delivered as four sequential, independently reviewable pull requests. CI inventory lands first and becomes the guardrail for later work. The NL-search PR replaces mirrored CLR records with a leaf `Travel.IntegrationContracts.AI` assembly and proves Core NATS request/reply through both real processes. The bootstrap PR keeps generic orchestration in `Travel.Shared.Infrastructure`, while EF/Marten initializers and options stay with their owning process/module; production never migrates automatically and exposes health only on a dedicated internal listener. The final PR starts Aspire without persistent volumes and proves actual Flights EF/outbox behavior plus AI ledger persistence without an external LLM.

**Tech Stack:** .NET 10, ASP.NET Core health checks/Kestrel, Wolverine 5.13 Core NATS request/reply, NATS 2.x with JetStream available only for durable use cases, EF Core 10/Npgsql, Marten 8.37.4, Aspire 13.4 testing, xUnit v3, Shouldly, Testcontainers 4.11, Node.js 22 built-ins, GitHub Actions, Nx 22 for frontend affected tasks.

**Spec reference:** [`docs/superpowers/specs/2026-08-11-ai-harness-architecture-remediation-design.md`](../specs/2026-08-11-ai-harness-architecture-remediation-design.md), specifically D7, D8, D11, D12, WS2, migration rules, testing strategy, and acceptance criteria 8–11, 14–16, and 19.

**Approved operational choice:** Production health endpoints use a dedicated internal listener. They are not mapped on the public application listener and are not made public by the application.

## Global Constraints

- This plan implements **WS2 only**. Do not perform the WS3 Api-facade composition refactor, move provider registrations into Shared, redesign Wolverine/Marten process-global ownership, or clean legacy `src/` projects.
- Each implementation PR starts from the then-current `origin/dev`, after the previous WS2 PR has merged. Do not stack all four PRs, rewrite ancestry, or use a stale current `HEAD` as a substitute for fresh `origin/dev`.
- Keep the current Host/Flights composition split unchanged until WS3. WS2 may replace contract types and add module-owned initializer/configuration hooks, but it must not introduce the future `AddFlightsModule(IHostApplicationBuilder)` facade early.
- Approving this plan does not by itself grant authority to stage, commit, push, create a PR, merge, dispatch CI, deploy, or mutate an external system. The commit lines below are intended checkpoints; obtain the applicable implementation/publication/merge authority for each PR.
- `Travel.Shared.Infrastructure` owns only generic initializer state, phase ordering, health integration, and reusable host mechanics. It must not reference Flights, AI, EF provider-specific contexts, Marten configuration, SMTP, Keycloak, or provider options.
- The integration-contract project is a leaf. It may reference BCL serialization types and the smallest Wolverine package needed for stable message identity metadata; it must not reference apps, modules, ASP.NET, EF, Marten, `ErrorOr`, or shared domain projects.
- NL-search request/reply remains **Core NATS** with the existing bounded user timeout. Do not turn it into JetStream simply because the AppHost broker has JetStream enabled. Reserve JetStream for durable commands/events whose value survives a missing consumer.
- When implementation of WS2.2 is explicitly authorized, source migration generation is in scope only for Task 6. Never run `dotnet ef database update`, apply a migration, mutate a shared/live database, deploy, or dispatch external CI as an implied step.
- Development and test may apply source migrations to their own disposable databases. Production application processes must never call `MigrateAsync`; they validate compatibility and stay live-but-not-ready on mismatch.
- Production configuration must not inherit localhost service fallbacks from base `appsettings.json`. Development-only endpoints belong in `appsettings.Development.json` or Aspire-injected settings.
- External providers are not readiness dependencies. Disabled providers register neither their business adapter nor their dependency probe. Enabled-provider failures are visible on `/health/dependencies` and do not close `/health/ready`.
- The production health listener is internal-only. `/health/live`, `/health/ready`, and `/health/dependencies` must reject or be absent on the public listener; deployment/ingress publication remains outside source-ready WS2.
- Paid Anthropic calls, real Duffel/Travelpayouts calls, production migrations, deployment, and external environment creation are outside the mandatory local/PR gate.
- Use TDD for behavior changes: focused test must fail for the expected reason before implementation, then pass. Record the RED and GREEN commands in the PR description.
- On Windows PowerShell use `npm.cmd`/`npx.cmd`; in POSIX and GitHub Actions use `npm`/`npx`.
- Report completion in three separate evidence classes: source-ready, disposable integration-proven, and live/deployment-proven. A merged PR is not production proof.

---

## Delivery Order and PR Boundaries

| PR | Fresh branch from | Purpose | Required same-change ADR |
|---|---|---|---|
| WS2.1 | current `origin/dev` | Complete `dev` CI and repository/test inventory | amend ADR 0004 |
| WS2.2 | `origin/dev` after WS2.1 merge | Shared NL-search contract, Core NATS proof, ledger idempotency | supersede/amend ADR 0020 |
| WS2.3 | `origin/dev` after WS2.2 merge | Initialization, production schema gate, validated config, internal health | amend ADR 0007 |
| WS2.4 | `origin/dev` after WS2.3 merge | Ephemeral Aspire resources and fresh-volume functional smoke | no new ADR; verify prior decisions |

Do not open WS2.2 until WS2.1 is merged, and so on. This keeps every branch reviewable against the current `dev` contract and prevents the prohibited giant remediation PR.

---

# PR WS2.1 — Complete `dev` CI and Test Inventory

## Task 1: Create a fail-closed .NET project and test-lane inventory

**Files:**

- Create: `tools/ci/dotnet-inventory.json`
- Create: `tools/ci/validate-dotnet-inventory.mjs`
- Create: `tools/ci/validate-dotnet-inventory.test.mjs`
- Modify: `package.json`

- [ ] **Step 1: Write the inventory validator fixtures first.**

  Cover these failure modes with temporary repository fixtures:

  - a tracked `*.csproj` exists but is absent from the inventory;
  - a solution project is absent from `Travel.slnx` inventory or vice versa;
  - an executable/tool project is mislabeled as a test project;
  - a non-empty test project has no CI lane;
  - an explicitly empty scaffold gains a `[Fact]`/`[Theory]` but remains marked empty;
  - a test project is assigned to overlapping full-project lanes;
  - a project outside the solution lacks an explicit exclusion and rationale;
  - a manifest lane references a job/filter absent from `.github/workflows/ci.yml`;
  - path casing or slash normalization makes Windows and POSIX produce different inventories.

- [ ] **Step 2: Run the focused test and capture the genuine RED.**

  ```powershell
  node --test tools/ci/validate-dotnet-inventory.test.mjs
  ```

  Expected: fail because `validate-dotnet-inventory.mjs` and the manifest do not exist.

- [ ] **Step 3: Implement the dependency-free validator with Node 22 built-ins.**

  The validator must:

  1. recursively enumerate every repository `*.csproj`, excluding `.git`, `node_modules`, `bin`, and `obj`;
  2. parse every `<Project Path="..." />` in `Travel.slnx` and compare exact normalized paths;
  3. classify every project exactly once as `application`, `library`, `test`, `tool`, or `excluded-legacy`;
  4. require `IsTestProject=true` projects to be either assigned to one full-project lane, assigned to explicit mutually exclusive filtered lanes, or declared `empty-scaffold` with a non-empty reason and zero discovered xUnit test attributes;
  5. require every off-solution project to be explicitly `excluded-legacy` with a reason;
  6. require every declared lane/job/filter to exist literally in `.github/workflows/ci.yml`;
  7. reject duplicates, unknown keys, missing files, path traversal, symlinks in the inventory-owned path set, and unrecognized classifications;
  8. print one deterministic success line and actionable, path-specific failures.

  The initial manifest must explicitly account for the three current legacy Rail projects under `src/Modules/Rail/**`; it must not delete or silently ignore them.

- [ ] **Step 4: Add scripts.**

  Add:

  ```json
  "test:dotnet-inventory": "node --test tools/ci/validate-dotnet-inventory.test.mjs",
  "check:dotnet-inventory": "npm run test:dotnet-inventory && node tools/ci/validate-dotnet-inventory.mjs"
  ```

- [ ] **Step 5: Run focused GREEN and formatter.**

  ```powershell
  npm.cmd run check:dotnet-inventory
  npx.cmd biome ci tools/ci package.json
  ```

- [ ] **Step 6: Commit the validator separately.**

  ```text
  test(ci): add fail-closed dotnet inventory
  ```

## Task 2: Make CI authoritative for `dev` and every current test surface

**Files:**

- Modify: `.github/workflows/ci.yml`
- Modify: `tools/ci/dotnet-inventory.json`
- Modify: `tools/ci/validate-dotnet-inventory.test.mjs`

- [ ] **Step 1: Add a fixture that requires `dev`, explicit build, and all lane names.**

  The test must fail against the current workflow because it targets only `master`, lacks explicit `dotnet build Travel.slnx`, omits the trait-free Host HTTP lane, and lets Nx act as the incomplete .NET graph authority.

- [ ] **Step 2: Run RED.**

  ```powershell
  npm.cmd run test:dotnet-inventory
  ```

- [ ] **Step 3: Rewrite CI into explicit, non-overlapping gates.**

  Preserve frontend affected execution, but make .NET explicit:

  - triggers: pull requests and pushes to both `dev` and `master`; retain `workflow_dispatch` for the protected paid lane;
  - lint: AI harness, .NET inventory, Biome, CSharpier;
  - build: `dotnet restore Travel.slnx`, then `dotnet build Travel.slnx --no-restore`;
  - frontend: `npx nx affected -t build,test,lint --exclude=travel-agency` using the actual PR base SHA/merge base; the excluded root NX project is a tooling container, not a frontend deliverable;
  - Flights unit: the full `Travel.Modules.Flights.Tests.Unit` project;
  - AI tests: the full `Travel.AI.Tests` project;
  - Host HTTP: `Travel.Host.Tests.Integration` filtered with `Category!=Integration&Category!=AspireSmoke` so the currently trait-free HTTP suite is not dropped;
  - architecture: the full `Travel.Tests.Architecture` project;
  - contract: the full `Travel.Tests.Contract` project, not `FullyQualifiedName~NlSearch`;
  - Flights integration: the full Flights integration project;
  - Host integration: `Category=Integration`;
  - Aspire smoke: `Category=AspireSmoke`;
  - E2E: run for `dev` and `master` PRs after required build/test lanes;
  - AI evals: only `workflow_dispatch` with an explicit boolean input and protected `paid-ai-evals` environment; never on a normal push/PR. Fail before restore/build/test when the protected environment does not supply `ANTHROPIC_API_KEY`, keep the secret scoped to the preflight and test steps, and never print its value.

  Every job must restore/build what it needs; do not assume binaries cross GitHub-hosted runners without an explicit artifact. Keep Docker-dependent jobs separate from fast unit/architecture/contract jobs.

- [ ] **Step 4: Update the manifest to prove exact lane coverage.**

  Mark current empty Hotels/Rail/Trips/Identity test projects as `empty-scaffold` with reasons. When later WS2 work adds Identity tests, the validator must force its promotion into a real lane.

- [ ] **Step 5: Run local workflow/static gates.**

  ```powershell
  npm.cmd run check:dotnet-inventory
  npm.cmd run check:ai-harness
  npx.cmd biome ci .github/workflows/ci.yml tools/ci package.json
  dotnet build Travel.slnx --no-restore
  ```

- [ ] **Step 6: Run every new .NET lane locally.**

  Use the exact commands committed to CI. The paid AI eval lane is excluded locally unless separately authorized and credentialed.

- [ ] **Step 7: Commit.**

  ```text
  ci: make dev dotnet gates complete
  ```

## Task 3: Amend ADR 0004 to match the proven CI boundary

**Files:**

- Modify: `docs/adr/0004-nx-monorepo-tooling.md`

- [ ] **Step 1: Add a dated amendment without rewriting historical context.**

  Record that Nx remains authoritative for frontend affected work, while explicit solution build and manifest-backed .NET lanes are authoritative until the repository proves a complete and stable Nx .NET graph. Explain why current `nx affected` alone was insufficient and how the inventory prevents silent project/test omission.

- [ ] **Step 2: Verify the ADR matches workflow names and commands exactly.**

  ```powershell
  npm.cmd run check:dotnet-inventory
  git diff --check
  ```

- [ ] **Step 3: Commit.**

  ```text
  docs(adr): amend nx ownership for dotnet ci
  ```

## WS2.1 PR Gate

Run from a fresh clone or clean worktree with Docker available:

```powershell
npm.cmd ci
dotnet tool restore
npm.cmd run check:ai-harness
npm.cmd run check:dotnet-inventory
dotnet restore Travel.slnx --no-cache
dotnet build Travel.slnx --no-restore
dotnet test Travel.slnx --no-build --verbosity minimal --maxcpucount:1
dotnet csharpier check .
npx.cmd biome ci .
git diff --check origin/dev...HEAD
```

The PR may merge only after its actual `dev`-targeted GitHub Actions run is green. If the protected `paid-ai-evals` environment does not yet exist, report that lane as external setup pending; do not weaken normal PR gates or expose the secret.

---

# PR WS2.2 — Shared NL-Search Contract and Real Core NATS Proof

## Task 4: Add the leaf `Travel.IntegrationContracts.AI` project

**Files:**

- Create: `shared/dotnet/Travel.IntegrationContracts.AI/Travel.IntegrationContracts.AI.csproj`
- Create: `shared/dotnet/Travel.IntegrationContracts.AI/NlSearch/NlSearchContracts.cs`
- Modify: `Travel.slnx`
- Modify: `Directory.Packages.props` only if the selected identity attribute requires a package not already centrally versioned
- Modify: `tests/Travel.Tests.Architecture/Travel.Tests.Architecture.csproj`
- Create: `tests/Travel.Tests.Architecture/IntegrationContractArchitectureTests.cs`

- [ ] **Step 1: Write failing architecture tests first.**

  Assert that:

  - the contract assembly references no Travel app/module/shared assembly;
  - it references no ASP.NET, EF, Marten, or `ErrorOr` assembly;
  - direct project consumers are a subset of the approved allowlist: Flights Application, Travel.AI, Flights Api composition, and Contract tests;
  - no second `NlSearchRequested` or `NlSearchParsed` CLR definition exists elsewhere in production source.

- [ ] **Step 2: Run RED.**

  ```powershell
  dotnet test tests/Travel.Tests.Architecture --filter FullyQualifiedName~IntegrationContractArchitectureTests
  ```

- [ ] **Step 3: Create the leaf project and stable v1 types.**

  Define `NlSearchRequested` and `NlSearchParsed` once, preserving current JSON field names/types/defaults. Apply these exact stable identities and version metadata using constants owned by the contract assembly:

  ```text
  travel.ai.nl-search.requested  v1
  travel.ai.nl-search.parsed     v1
  ```

  Use Wolverine's supported `MessageIdentity` metadata; do not rely on CLR namespace/full name as wire identity.

- [ ] **Step 4: Add the project to `Travel.slnx` under `/shared/dotnet/`.**

  Do not create a NuGet publication pipeline in WS2. Both processes build from the same repository project reference.

- [ ] **Step 5: Run GREEN and update the CI inventory.**

  ```powershell
  dotnet test tests/Travel.Tests.Architecture --filter FullyQualifiedName~IntegrationContractArchitectureTests
  npm.cmd run check:dotnet-inventory
  ```

- [ ] **Step 6: Commit.**

  ```text
  feat(contracts): add versioned ai integration contract
  ```

## Task 5: Replace both mirrored contracts atomically

**Files:**

- Delete: `modules/flights/Travel.Modules.Flights.Application/Contracts/NlSearchContracts.cs`
- Delete: `apps/Travel.AI/NlSearch/Contracts/NlSearchContracts.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Application/Travel.Modules.Flights.Application.csproj`
- Modify: `apps/Travel.AI/Travel.AI.csproj`
- Modify: `modules/flights/Travel.Modules.Flights.Application/Handlers/NlSearch/NlSearchHandler.cs`
- Modify: `apps/Travel.AI/NlSearch/NlSearchAiHandler.cs`
- Modify: `apps/Travel.Host/Program.cs`
- Modify: `tests/flights/Travel.Modules.Flights.Tests.Unit/Handlers/NlSearchHandlerTests.cs`
- Modify: `tests/flights/Travel.Modules.Flights.Tests.Integration/NlSearch/NlSearchHandlerTests.cs`
- Modify: `tests/Travel.AI.Tests/NlSearch/NlSearchAiHandlerTests.cs`
- Modify: `tests/Travel.AI.Tests/NlSearch/NlSearchExtractorTests.cs`
- Modify: `tests/Travel.Tests.AiEvals/Flights/NlSearchEvalRunner.cs`
- Modify: `tests/Travel.Tests.Contract/Travel.Tests.Contract.csproj`
- Rewrite: `tests/Travel.Tests.Contract/Flights/NlSearchContractShapeTests.cs`

- [ ] **Step 1: Rewrite the contract test to fail until both sides use the same CLR types.**

  Verify:

  - exact identity string and version for both messages;
  - exact Web-default JSON shape and round trip;
  - request correlation ID survives serialization;
  - reply correlation ID must equal request correlation ID;
  - Host and AI production assemblies reference the same contract assembly/type, not mirrored shapes.

- [ ] **Step 2: Run RED.**

  ```powershell
  dotnet test tests/Travel.Tests.Contract --filter Category=Contract
  ```

- [ ] **Step 3: Add direct project references only where allowed and replace imports.**

  Flights Application and Travel.AI reference the new project directly. Contract tests reference it directly. Host may use the type through its existing transitive module graph in the current split; do not add a new Host-to-module-internals dependency and do not perform WS3 composition work.

- [ ] **Step 4: Delete both mirrored source files in the same commit.**

  No compatibility alias is needed because no production messages are yet guaranteed in flight; this atomic source change is the approved migration path.

- [ ] **Step 5: Run GREEN.**

  ```powershell
  dotnet test tests/Travel.Tests.Contract --filter Category=Contract
  dotnet test tests/Travel.Tests.Architecture --filter Category=Architecture
  dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit --filter FullyQualifiedName~NlSearch
  dotnet test tests/Travel.AI.Tests --filter FullyQualifiedName~NlSearch
  ```

- [ ] **Step 6: Commit.**

  ```text
  refactor(ai): replace mirrored nl search messages
  ```

## Task 6: Make the AI ledger record idempotent by contract identity and correlation

**Files:**

- Modify: `apps/Travel.AI/Persistence/Entities/CostLedgerEntry.cs`
- Modify: `apps/Travel.AI/Persistence/AiDbContext.cs`
- Modify: `apps/Travel.AI/NlSearch/NlSearchAiHandler.cs`
- Modify: `tests/Travel.AI.Tests/NlSearch/NlSearchAiHandlerTests.cs`
- Create: `apps/Travel.AI/Persistence/Migrations/<timestamp>_CostLedgerMessageIdempotency.cs`
- Create: matching migration designer file
- Modify: `apps/Travel.AI/Persistence/Migrations/AiDbContextModelSnapshot.cs`

- [ ] **Step 1: Add RED tests for the ledger key.**

  Prove that a ledger entry records the stable request identity and correlation ID, and that two inserts with the same pair violate a unique database constraint while different identities/correlations remain valid.

- [ ] **Step 2: Run RED.**

  ```powershell
  dotnet test tests/Travel.AI.Tests --filter "FullyQualifiedName~NlSearchAiHandlerTests|FullyQualifiedName~CostLedger"
  ```

- [ ] **Step 3: Add `MessageIdentity` (or an equivalently named explicit key component) and a unique composite index with `CorrelationId`.**

  Populate it from the canonical request identity constant, never from `typeof(T).FullName`.

  Scope statement: this WS2 invariant prevents duplicate **ledger records** for the same logical message. It does not claim to cache/replay the parsed response or suppress every possible duplicate external LLM invocation; that would require a persisted request-execution/result state machine beyond D7 and must be designed separately rather than hidden in this migration.

- [ ] **Step 4: Generate the source migration from the module design-time factory.**

  ```powershell
  dotnet ef migrations add CostLedgerMessageIdempotency --project apps/Travel.AI --output-dir Persistence/Migrations
  ```

  Do **not** run `database update`.

- [ ] **Step 5: Review generated source and SQL.**

  Generate an idempotent SQL script against the migration range for review only. Verify schema `ai`, table `cost_ledger`, the new column, unique index, and rollback. Do not execute the script.

- [ ] **Step 6: Run GREEN.**

  ```powershell
  dotnet test tests/Travel.AI.Tests
  dotnet ef migrations script --idempotent --project apps/Travel.AI
  ```

- [ ] **Step 7: Commit migration source with the model change.**

  ```text
  feat(ai): make cost ledger messages idempotent
  ```

## Task 7: Prove real Host → NATS → AI → reply behavior

**Files:**

- Modify: `Directory.Packages.props`
- Modify: `tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj`
- Create: `tests/Travel.Host.Tests.Integration/NlSearch/NlSearchTransportTests.cs`
- Create or modify: test fixture helpers under `tests/Travel.Host.Tests.Integration/NlSearch/`
- Modify: `apps/Travel.AI/Program.cs` only if required to expose the existing `partial Program` test entry point consistently

- [ ] **Step 1: Write the integration test before adding its fixture.**

  The test must start disposable PostgreSQL and Core NATS, boot the actual Travel.AI and Travel.Host programs with their production Wolverine configuration, replace only `IChatClient` in AI with a deterministic fake, and invoke the request from Host's real `IMessageBus`.

  Assert:

  - exact shared request type goes through subject `travel.ai.nl_search`;
  - AI handler returns the exact shared reply type;
  - request and reply correlation IDs match;
  - bounded timeout succeeds on the healthy path and produces the current typed fallback on a missing AI consumer;
  - exactly one matching AI ledger row is persisted;
  - the test uses Core NATS request/reply, not a JetStream durable endpoint.

- [ ] **Step 2: Run RED.**

  ```powershell
  dotnet test tests/Travel.Host.Tests.Integration --filter FullyQualifiedName~NlSearchTransportTests
  ```

- [ ] **Step 3: Add explicit test dependencies and aliased app references.**

  Use aliases for the two global `Program` types. Add explicit central versions for any newly required `Microsoft.AspNetCore.Mvc.Testing`/generic Testcontainers package; do not rely on an accidental transitive dependency.

- [ ] **Step 4: Implement the fixture without copying production messaging setup.**

  Override configuration/DI only. Do not duplicate route or listener declarations in test code. The fake chat response must be deterministic and contain no Anthropic key/network call.

- [ ] **Step 5: Run GREEN repeatedly.**

  ```powershell
  dotnet test tests/Travel.Host.Tests.Integration --filter FullyQualifiedName~NlSearchTransportTests
  dotnet test tests/Travel.Host.Tests.Integration --filter FullyQualifiedName~NlSearchTransportTests
  ```

  Two consecutive passes are required to expose cleanup/port/idempotency flakiness.

- [ ] **Step 6: Commit.**

  ```text
  test(ai): prove core nats request reply
  ```

## Task 8: Supersede ADR 0020 in the same PR

**Files:**

- Modify: `docs/adr/0020-nl-search-cross-service-contract.md`
- Modify: `README.md`

- [ ] **Step 1: Add a dated superseding amendment.**

  Explicitly retract the mirrored-record/JSON-shape-only ratification. Record the leaf shared project, stable v1 message identities, contract/architecture checks, real transport test, Core NATS interactive semantics, JetStream durable-only distinction, bounded timeout, and ledger key. Correct the README stack overview so broker capability is not presented as the semantics of every subject.

- [ ] **Step 2: State the compatibility boundary.**

  The atomic replacement is valid before production messages are guaranteed in flight. Future v2 changes require a new identity/version and an explicit compatibility window, not an in-place field mutation.

- [ ] **Step 3: Verify no stale active claim remains.**

  ```powershell
  rg -n "mirrored records|snapshot.*sufficient|Travel\.AI\.NlSearch\.Contracts|Flights\.Application\.Contracts" README.md CONTRIBUTING.md AGENTS.md apps modules shared tests docs/adr
  ```

- [ ] **Step 4: Commit.**

  ```text
  docs(adr): supersede mirrored nl search contract
  ```

## WS2.2 PR Gate

```powershell
npm.cmd ci
dotnet tool restore
npm.cmd run check:dotnet-inventory
dotnet restore Travel.slnx --no-cache
dotnet build Travel.slnx --no-restore
dotnet test tests/Travel.Tests.Contract --filter Category=Contract
dotnet test tests/Travel.Tests.Architecture --filter Category=Architecture
dotnet test tests/Travel.AI.Tests
dotnet test tests/Travel.Host.Tests.Integration --filter FullyQualifiedName~NlSearchTransportTests
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit --filter FullyQualifiedName~NlSearch
dotnet csharpier check .
npx.cmd biome ci .
git diff --check origin/dev...HEAD
```

Report the migration as source-reviewed and disposable-test-proven only. No database application or deployment is part of this PR.

---

# PR WS2.3 — Initialization, Configuration, and Internal Health

## Task 9: Make generic initialization deterministic and health-aware

**Files:**

- Modify: `shared/dotnet/Travel.Shared.Infrastructure/Travel.Shared.Infrastructure.csproj`
- Modify: `shared/dotnet/Travel.Shared.Infrastructure/Initialization/IInitializer.cs`
- Create: `shared/dotnet/Travel.Shared.Infrastructure/Initialization/InitializationPhase.cs`
- Create: `shared/dotnet/Travel.Shared.Infrastructure/Initialization/InitializationState.cs`
- Modify: `shared/dotnet/Travel.Shared.Infrastructure/Initialization/AppInitializer.cs`
- Modify: `shared/dotnet/Travel.Shared.Infrastructure/Initialization/InitializationExtensions.cs`
- Create: `shared/dotnet/Travel.Shared.Infrastructure/Initialization/InitializationHealthCheck.cs`
- Create: `tests/Travel.Host.Tests.Integration/Initialization/AppInitializerTests.cs`

- [ ] **Step 1: Write RED tests for ordering and state.**

  Cover deterministic phase order (`Platform`, `RelationalSchema`, `EventStoreSchema`, `DevelopmentSeed`), stable name ordering inside a phase, pending/running/succeeded/failed state, cancellation, and error diagnostics without secrets.

  Development/test failure must fail startup. Production validation failure must be recorded, allow the process to become live, and keep readiness unhealthy.

- [ ] **Step 2: Run RED.**

  ```powershell
  dotnet test tests/Travel.Host.Tests.Integration --filter FullyQualifiedName~AppInitializerTests
  ```

- [ ] **Step 3: Implement only generic orchestration in Shared.**

  `AppInitializer` resolves all initializers once, sorts by phase then stable type/name, executes sequentially, and records state transitions. `InitializationHealthCheck` is tagged `ready`; zero registered initializers is a deliberate healthy state for processes with no owned schema, while Host and AI will gain real initializers in the next task.

- [ ] **Step 4: Run GREEN.**

  ```powershell
  dotnet test tests/Travel.Host.Tests.Integration --filter FullyQualifiedName~AppInitializerTests
  dotnet test tests/Travel.Tests.Architecture --filter Category=Architecture
  ```

- [ ] **Step 5: Commit.**

  ```text
  feat(shared): make initialization phased and observable
  ```

## Task 10: Unify runtime/design-time EF configuration

**Files:**

- Create: `modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/FlightsDbContextConfiguration.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/FlightsDbContextFactory.cs`
- Modify: `apps/Travel.Host/Program.cs`
- Create: `apps/Travel.AI/Persistence/AiDbContextConfiguration.cs`
- Modify: `apps/Travel.AI/Persistence/AiDbContextFactory.cs`
- Modify: `apps/Travel.AI/Program.cs`
- Modify: `tests/flights/Travel.Modules.Flights.Tests.Integration/Persistence/FlightsDbContextTests.cs`
- Create: `tests/Travel.AI.Tests/Persistence/AiDbContextConfigurationTests.cs`

- [ ] **Step 1: Write RED tests that inspect provider metadata.**

  Runtime-style and design-time-style option builders must produce the same provider, snake-case convention, default schema model, migrations assembly, and history tables:

  ```text
  flights.__ef_migrations_history
  ai.__ef_migrations_history
  ```

- [ ] **Step 2: Run RED.**

  ```powershell
  dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration --filter FullyQualifiedName~DbContext
  dotnet test tests/Travel.AI.Tests --filter FullyQualifiedName~DbContext
  ```

- [ ] **Step 3: Extract one module/process-owned Npgsql configuration function per context.**

  Runtime `AddNpgsqlDbContext` callbacks and design-time factories call the same provider-specific function. Do not put context types or EF provider details into Shared and do not retain duplicated `MigrationsHistoryTable` setup.

- [ ] **Step 4: Run GREEN and migration drift checks.**

  ```powershell
  dotnet ef migrations has-pending-model-changes --project modules/flights/Travel.Modules.Flights.Infrastructure
  dotnet ef migrations has-pending-model-changes --project apps/Travel.AI
  ```

  Expected: no pending model change beyond migrations intentionally added in WS2.2.

- [ ] **Step 5: Commit.**

  ```text
  refactor(storage): unify dbcontext configuration
  ```

## Task 11: Add real Flights/AI initializers and production schema gates

**Files:**

- Create: `modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/Initialization/FlightsEfInitializer.cs`
- Create: `modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/Initialization/FlightsMartenInitializer.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Infrastructure/FlightsModuleServiceCollectionExtensions.cs`
- Create: `apps/Travel.AI/Persistence/Initialization/AiEfInitializer.cs`
- Modify: `apps/Travel.AI/Travel.AI.csproj`
- Modify: `apps/Travel.AI/Program.cs`
- Modify: `apps/Travel.Host/Program.cs`
- Create: `tests/Travel.Host.Tests.Integration/Initialization/DatabaseInitializationTests.cs`
- Create: `tests/Travel.AI.Tests/Persistence/AiDatabaseInitializationTests.cs`

- [ ] **Step 1: Write disposable-Postgres RED tests.**

  Prove:

  - Development/Testing on a blank database applies Flights EF migrations, AI EF migrations, and configured Marten changes in phase order;
  - expected schemas, migration-history tables, core Flights tables, Marten tables, and AI ledger table exist before readiness succeeds;
  - a second run is idempotent;
  - Production never calls EF migration/apply APIs;
  - Production with pending EF migration or Marten mismatch stays live but fails initialization readiness with a useful component name;
  - a compatible Production schema passes without mutation.

- [ ] **Step 2: Run RED.**

  ```powershell
  dotnet test tests/Travel.Host.Tests.Integration --filter FullyQualifiedName~DatabaseInitializationTests
  dotnet test tests/Travel.AI.Tests --filter FullyQualifiedName~Initialization
  ```

- [ ] **Step 3: Implement module/process-owned policies.**

  Flights EF and AI EF initializers use `MigrateAsync` only outside Production; Production uses pending-migration inspection. Flights Marten uses configured schema application outside Production and schema assertion in Production. Register AI's generic initialization hosted service; it currently lacks one.

  Do not invent a fake platform initializer just to fill the `Platform` phase. If no platform-owned schema exists, the phase remains available and empty.

- [ ] **Step 4: Ensure readiness is closed until all registered initializers succeed.**

  A database connection failure is not masked by a localhost fallback. Production failure is observable through readiness and logs; development/test failure aborts startup.

- [ ] **Step 5: Run GREEN twice against fresh disposable databases.**

  ```powershell
  dotnet test tests/Travel.Host.Tests.Integration --filter FullyQualifiedName~DatabaseInitializationTests
  dotnet test tests/Travel.AI.Tests --filter FullyQualifiedName~Initialization
  ```

- [ ] **Step 6: Commit.**

  ```text
  feat(storage): add environment-aware schema initialization
  ```

## Task 12: Replace raw configuration and localhost masking with validated options

**Files:**

- Create: `modules/identity/Travel.Modules.Identity.Infrastructure/KeycloakOptions.cs`
- Modify: `modules/identity/Travel.Modules.Identity.Infrastructure/IdentityServiceCollectionExtensions.cs`
- Create: `modules/flights/Travel.Modules.Flights.Infrastructure/Notifications/Email/SmtpOptions.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Infrastructure/Notifications/Email/MailKitEmailSender.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Infrastructure/FlightsModuleServiceCollectionExtensions.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Application/FlightsFeatureFlags.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Infrastructure/Providers/Duffel/DuffelOptions.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Infrastructure/Providers/Travelpayouts/TravelpayoutsOptions.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Infrastructure/Notifications/Keycloak/KeycloakAdminOptions.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Infrastructure/ExternalServices/FrankfurterOptions.cs`
- Create: `apps/Travel.AI/Configuration/AnthropicOptions.cs`
- Modify: `apps/Travel.AI/Program.cs`
- Modify: `apps/Travel.Host/appsettings.json`
- Modify: `apps/Travel.Host/appsettings.Development.json`
- Modify: `apps/Travel.Host/appsettings.Production.json`
- Modify: `apps/Travel.AI/appsettings.json`
- Modify: `apps/Travel.AI/appsettings.Development.json`
- Modify: `apps/Travel.AI/appsettings.Production.json`
- Modify: `apps/Travel.AppHost/Program.cs`
- Create: `tests/flights/Travel.Modules.Flights.Tests.Unit/Composition/FlightsOptionsValidationTests.cs`
- Modify: `tests/identity/Travel.Modules.Identity.Tests.Unit/Travel.Modules.Identity.Tests.Unit.csproj`
- Create: `tests/identity/Travel.Modules.Identity.Tests.Unit/Configuration/KeycloakOptionsTests.cs`
- Create: `tests/Travel.AI.Tests/Configuration/AnthropicOptionsTests.cs`
- Modify: `tools/ci/dotnet-inventory.json` because Identity unit tests cease being an empty scaffold

- [ ] **Step 1: Write environment/feature matrix RED tests.**

  Required cases:

  - Production JWT Keycloak authority/audience missing, relative, localhost, or non-HTTPS fails validation;
  - Development may use the explicit localhost Keycloak setting;
  - Production SMTP host/port/from missing or localhost fails; Development Mailpit passes;
  - `Flights__Smtp__Host` is the exact Aspire environment key (replace current `Smtp__Host`);
  - Duffel production API key/webhook secret and valid HTTPS base URL are required; WS2 does not invent a Duffel disable flag because current booking behavior has no approved disabled-provider contract;
  - Travelpayouts token/marker/base URL are required only when its feature flag is enabled;
  - a completely absent optional Keycloak-admin integration stays disabled; any partial configuration fails validation;
  - Frankfurter URI/timeouts are valid;
  - Production Anthropic API key is required for the currently enabled AI service;
  - Production `travel`, `nats`, and `redis` connection settings cannot fall back to localhost.

- [ ] **Step 2: Run RED in the owning test projects.**

  ```powershell
  dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit --filter FullyQualifiedName~Options
  dotnet test tests/identity/Travel.Modules.Identity.Tests.Unit --filter FullyQualifiedName~Options
  dotnet test tests/Travel.AI.Tests --filter FullyQualifiedName~Options
  ```

- [ ] **Step 3: Bind typed options with `ValidateOnStart`.**

  Keep validation next to the owner. Inject `IOptions<SmtpOptions>` into `MailKitEmailSender`; remove raw `IConfiguration` reads and inline localhost/default substitutions. Make conditional validators environment/feature-aware without leaking secrets into error text.

- [ ] **Step 4: Move development-only values out of base settings.**

  Base/Production settings contain safe non-environment defaults only. Local Keycloak, SMTP, NATS, Redis, and database endpoints belong in Development or Aspire injection.

- [ ] **Step 5: Register provider services and probes only when enabled.**

  At minimum, disabled Travelpayouts must register neither its business provider nor its named dependency health client/check. Preserve the current startup decision that feature-registration changes require restart; do not pretend DI registrations hot-reload.

- [ ] **Step 6: Run GREEN and the full composition tests.**

  ```powershell
  dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit
  dotnet test tests/identity/Travel.Modules.Identity.Tests.Unit
  dotnet test tests/Travel.AI.Tests
  dotnet test tests/Travel.Host.Tests.Integration --filter "Category!=AspireSmoke"
  npm.cmd run check:dotnet-inventory
  ```

- [ ] **Step 7: Commit.**

  ```text
  fix(config): validate production dependencies
  ```

## Task 13: Implement live/ready/dependency health on an internal listener

**Files:**

- Modify: `apps/Travel.ServiceDefaults/Extensions.cs`
- Create: `apps/Travel.ServiceDefaults/Health/HealthEndpointOptions.cs`
- Modify: `apps/Travel.Host/Program.cs`
- Modify: `apps/Travel.Host/appsettings.Development.json`
- Modify: `apps/Travel.AI/Program.cs`
- Modify: `apps/Travel.AI/appsettings.Development.json`
- Modify: `apps/Travel.AppHost/Program.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Infrastructure/FlightsModuleServiceCollectionExtensions.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Infrastructure/HealthChecks/DuffelHealthCheck.cs`
- Modify: `modules/flights/Travel.Modules.Flights.Infrastructure/HealthChecks/TravelpayoutsHealthCheck.cs`
- Modify: `tests/flights/Travel.Modules.Flights.Tests.Integration/HealthChecks/FlightsHealthCheckTests.cs`
- Create: `tests/Travel.Host.Tests.Integration/Health/HealthEndpointContractTests.cs`
- Modify: `tests/Travel.AI.Tests/Travel.AI.Tests.csproj`
- Create: `tests/Travel.AI.Tests/Health/HealthEndpointContractTests.cs`

- [ ] **Step 1: Write RED contract tests.**

  For both Host and AI prove:

  - `/health/live` reports process responsiveness only;
  - `/health/ready` includes initialization/schema and required local dependencies;
  - `/health/dependencies` reports optional external providers separately;
  - provider failure cannot close readiness;
  - a disabled provider is absent from dependency output;
  - initialization pending/failure closes readiness but not liveness;
  - all three endpoints work on the configured internal listener in Development, Testing, and Production;
  - the same paths are absent/rejected on the public listener;
  - Production refuses to start if the internal port is missing/invalid or collides with the public port.

- [ ] **Step 2: Run RED.**

  ```powershell
  dotnet test tests/Travel.Host.Tests.Integration --filter FullyQualifiedName~HealthEndpointContractTests
  ```

- [ ] **Step 3: Add explicit tags and status semantics.**

  Use exact tags `live`, `ready`, and `dependency`. Optional provider checks return `Degraded` for reachability failures so diagnostics stay visible without pretending the platform is unready. Do not map an unfiltered catch-all health endpoint.

- [ ] **Step 4: Configure the dedicated listener.**

  Add validated `HealthEndpoints:InternalPort` settings for each process. Use `5098` for Host and `5159` for AI in Development/Testing; Production has no fallback and must receive an explicit value. Configure Kestrel to listen internally and constrain endpoint metadata/host matching to that listener. AppHost declares a `health-internal` endpoint on each resource and uses `/health/ready` for resource readiness; it must not publish these as the public service endpoint.

- [ ] **Step 5: Update telemetry filtering.**

  Exclude all three `/health/*` paths from request traces without weakening redaction or business telemetry.

- [ ] **Step 6: Run GREEN and verify both listeners.**

  ```powershell
  dotnet test tests/Travel.Host.Tests.Integration --filter FullyQualifiedName~HealthEndpointContractTests
  dotnet test tests/Travel.AI.Tests --filter FullyQualifiedName~Health
  ```

- [ ] **Step 7: Commit.**

  ```text
  feat(health): separate internal live ready dependencies
  ```

## Task 14: Amend ADR 0007 with the environment-specific storage policy

**Files:**

- Modify: `docs/adr/0007-storage-strategy-marten-ef-coexistence.md`

- [ ] **Step 1: Add a dated amendment.**

  Replace the stale `ApplyAllPendingMigrationsOnStartup` claim with the implemented phase model. Record common context configuration, per-module history schemas, dev/test apply behavior, production no-auto-migrate rule, Marten assertion, separate deployment migration gate, and readiness failure semantics.

- [ ] **Step 2: Include rollback/operation consequences.**

  Source-ready migrations are not live application. Deployment must run an explicit migration job before switching traffic, and the app must remain not-ready when the job was skipped or schema is incompatible.

- [ ] **Step 3: Verify docs/code parity.**

  ```powershell
  rg -n "ApplyAllPendingMigrationsOnStartup|MigrateAsync|__ef_migrations_history|not-ready" docs/adr/0007-storage-strategy-marten-ef-coexistence.md apps modules shared
  git diff --check
  ```

- [ ] **Step 4: Commit.**

  ```text
  docs(adr): define environment-specific schema gates
  ```

## WS2.3 PR Gate

```powershell
npm.cmd ci
dotnet tool restore
npm.cmd run check:dotnet-inventory
dotnet restore Travel.slnx --no-cache
dotnet build Travel.slnx --no-restore
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit
dotnet test tests/identity/Travel.Modules.Identity.Tests.Unit
dotnet test tests/Travel.AI.Tests
dotnet test tests/Travel.Host.Tests.Integration --filter "Category!=AspireSmoke"
dotnet test tests/Travel.Tests.Architecture --filter Category=Architecture
dotnet ef migrations has-pending-model-changes --project modules/flights/Travel.Modules.Flights.Infrastructure
dotnet ef migrations has-pending-model-changes --project apps/Travel.AI
dotnet csharpier check .
npx.cmd biome ci .
git diff --check origin/dev...HEAD
```

The PR must report Production behavior from tests as source/disposable-environment proof only. It does not create an internal ingress, run a deployment migration job, or alter a production database.

---

# PR WS2.4 — Ephemeral Aspire and Fresh-Volume Functional Smoke

## Task 15: Make persistent Aspire volumes opt-out and dependency order explicit

**Files:**

- Modify: `apps/Travel.AppHost/Program.cs`
- Modify: `apps/Travel.AppHost/appsettings.json`
- Modify: `apps/Travel.AppHost/appsettings.Development.json`
- Create: `tests/Travel.Host.Tests.Integration/Aspire/AppHostResourceModelTests.cs`

- [ ] **Step 1: Add RED model tests.**

  Prove that default Development keeps persistent volumes, while `UseVolumes=false` attaches none to PostgreSQL, Redis, NATS, or Keycloak. Prove Host waits for required database/NATS/Redis/Keycloak resources, AI waits for database/NATS, and both expose named internal readiness endpoints.

- [ ] **Step 2: Run RED.**

  ```powershell
  dotnet test tests/Travel.Host.Tests.Integration --filter FullyQualifiedName~AppHost
  ```

- [ ] **Step 3: Gate each `.WithDataVolume()` call on `UseVolumes` (default `true`).**

  Tests pass `--environment=Testing` and `UseVolumes=false`. Do not delete developer volumes or change the default local-development persistence behavior.

- [ ] **Step 4: Correct AppHost environment names and wait graph.**

  Keep the already fixed `Flights__Smtp__Host` contract, pass internal health ports explicitly, and use Aspire wait/reference APIs rather than arbitrary sleeps.

- [ ] **Step 5: Run GREEN.**

  ```powershell
  dotnet test tests/Travel.Host.Tests.Integration --filter FullyQualifiedName~AppHost
  ```

- [ ] **Step 6: Commit.**

  ```text
  feat(aspire): support ephemeral dependency volumes
  ```

## Task 16: Replace the status-only smoke with a fresh-volume functional proof

**Files:**

- Rewrite: `tests/Travel.Host.Tests.Integration/AspireStackSmokeTests.cs`
- Modify: `tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj` only for explicit test dependencies
- Add focused helpers under `tests/Travel.Host.Tests.Integration/Aspire/`

- [ ] **Step 1: Expand the smoke assertions before changing AppHost behavior.**

  Start with:

  ```text
  --environment=Testing
  UseVolumes=false
  ```

  The test must initially fail because blank databases are not bootstrapped and production-grade readiness endpoints do not yet prove schemas.

- [ ] **Step 2: Define one bounded 300-second test with phase-specific diagnostics.**

  Await resource health via Aspire notifications, not fixed sleeps. On failure, include the failing resource/phase and available diagnostics without printing secrets.

- [ ] **Step 3: Assert initialization and health.**

  Wait for PostgreSQL, NATS, Redis, Keycloak, Host readiness, and AI readiness. Query PostgreSQL metadata to prove `flights` and `ai` migration-history tables, required Flights tables, configured Marten tables, and `ai.cost_ledger` exist.

- [ ] **Step 4: Execute a real EF-backed Flights flow through HTTP.**

  POST a correctly HMAC-signed, deterministic Duffel webhook with a non-domain-changing event to `/webhooks/duffel`. Assert `200`, one `flights.webhook_inbox` row, outbox/handler processing to `ProcessedAt`, and duplicate-delivery idempotency. This exercises endpoint → EF inbox → Wolverine outbox → handler without calling Duffel.

- [ ] **Step 5: Execute an AI ledger write without a paid LLM.**

  Use `AiDbContext` against Aspire's `travel` connection string and the production context configuration to insert/query one deterministic ledger row with a unique non-production identity/correlation. This assertion intentionally proves schema/bootstrap and EF persistence only; the separate WS2.2 transport test proves the real AI handler with a fake `IChatClient`. Do not add a production-only test endpoint or a hidden fake-provider mode.

- [ ] **Step 6: Prove the old status route still works.**

  `/api/status` remains an additional E2E signal, not the sole database/bootstrap proof.

- [ ] **Step 7: Prove cleanup and freshness.**

  Dispose the distributed app and verify the test used no named data volumes. Run the smoke twice; both runs must create independent blank state and pass.

  ```powershell
  dotnet test tests/Travel.Host.Tests.Integration --filter Category=AspireSmoke
  dotnet test tests/Travel.Host.Tests.Integration --filter Category=AspireSmoke
  ```

- [ ] **Step 8: Commit.**

  ```text
  test(aspire): prove fresh-volume functional startup
  ```

## Task 17: Close CI/inventory/docs over the final WS2 state

**Files:**

- Modify: `tools/ci/dotnet-inventory.json` if project/lane classification changed
- Modify: `.github/workflows/ci.yml` only if the Aspire lane timeout/dependencies need the proven values
- Modify: `README.md` and/or `CONTRIBUTING.md` only where current commands or health/config contracts changed

- [ ] **Step 1: Run inventory against the final graph.**

  ```powershell
  npm.cmd run check:dotnet-inventory
  ```

- [ ] **Step 2: Update active documentation with exact current behavior.**

  Document Core NATS versus JetStream, dev/test migration versus production schema gate, internal health paths, and the fresh-volume command. Do not claim production deployment or external ingress proof.

- [ ] **Step 3: Run the full repository gate.**

  ```powershell
  npm.cmd ci
  dotnet tool restore
  npm.cmd run check:ai-harness
  npm.cmd run check:dotnet-inventory
  dotnet restore Travel.slnx --no-cache
  dotnet build Travel.slnx --no-restore
  dotnet test Travel.slnx --no-build --verbosity minimal --maxcpucount:1
  dotnet csharpier check .
  npx.cmd biome ci .
  git diff --check origin/dev...HEAD
  ```

- [ ] **Step 4: Commit any final parity-only change.**

  ```text
  docs: close ws2 delivery gates
  ```

## WS2.4 PR Gate

The normal full gate above must pass, plus the fresh-volume smoke must pass twice with Docker. The GitHub Actions run on the actual `dev` PR is required before merge.

---

## Final WS2 Acceptance Matrix

| Claim | Required proof | Evidence class |
|---|---|---|
| Every current project/test is visible to CI | inventory validator + actual `dev` Actions run | source + CI proven |
| Host and AI use one contract | same CLR type, stable v1 identity, JSON/reply tests | source proven |
| Request/reply really crosses the process boundary | actual Host and AI programs over disposable Core NATS | disposable integration proven |
| JetStream is not misclaimed for NL-search | route/listener test + amended ADR 0020 | source proven |
| Ledger rows are idempotent by logical message | unique identity/correlation DB test + migration review | source/disposable DB proven |
| Blank dev/test DB becomes usable before ready | phased initializers + fresh-volume schema checks | disposable integration proven |
| Production app never migrates automatically | production-policy tests + amended ADR 0007 | source proven; deployment unproven |
| Production schema mismatch blocks traffic, not process liveness | live/ready contract tests | source/in-process proven |
| Config cannot silently use localhost in Production | environment/feature validation matrix | source proven |
| External provider outage is diagnostic, not readiness-blocking | dependency endpoint tests | source/in-process proven |
| Health is not on the public listener | dual-listener contract tests | source/in-process proven; ingress unproven |
| Fresh stack does real work without paid providers | signed webhook EF/outbox flow + direct AI EF ledger write | disposable Aspire proven |

## Explicit Residual Gates After WS2

- A deployment-owned migration job and rollback procedure must be implemented/proven before production rollout; WS2 only defines and tests the application-side contract.
- The internal health port still requires deployment/ingress/network-policy wiring and live validation.
- Paid AI evals require a protected GitHub Environment, secret, budget policy, and an explicitly authorized dispatch.
- Suppressing/replaying duplicate LLM executions requires a persisted AI request-outcome state machine; ledger-row uniqueness alone does not make that claim.
- WS3 remains responsible for the full Flights Api composition facade and removal of Host imports of module internals.
- Legacy `src/Modules/Rail/**` projects remain explicitly inventoried but are not deleted in WS2.

## Handoff

After this plan is approved, implement **WS2.1 only** from a fresh branch based on current `origin/dev`. Complete its review and merge before creating the WS2.2 branch. Every PR handoff must include exact commit SHA, changed-path allowlist, commands/results, migration/deployment evidence boundary, and the next still-unapproved external gate.
