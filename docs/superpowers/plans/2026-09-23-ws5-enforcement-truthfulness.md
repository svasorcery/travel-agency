# WS5 — Enforcement and truthfulness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task by task. Steps use checkbox syntax. The user approved source implementation after self-review. This document grants no Git publication authority.

**Goal:** Make architecture guards and current project claims match the code after WS4, and prove the documented demo through the existing local and CI test surfaces.

**Architecture:** Extend evaluated MSBuild and IL guards, retain the existing Host/Shared/contract policy owners, and make README examples share one checked source with HTTP tests. Use the existing CI lanes for documentation parity, real Host/OpenAPI checks and Aspire smoke. Deliver the Angular starter removal as a separate frontend change.

**Tech Stack:** .NET 10, xUnit v3, ArchUnitNET, MSBuild evaluation, WolverineFx.Http/OpenAPI 3.1, Node 22, Angular 21 and Nx 23.

**Spec:** docs/superpowers/specs/2026-08-11-ai-harness-architecture-remediation-design.md — D12, WS5, sections 6 and 8, acceptance criteria 17–19. Existing WS2–WS4 acceptance evidence must remain valid.

**Closure status (2026-09-23):** Tasks 1–6 were implemented and merged into dev. The one uninterrupted local aggregate run was interrupted; component suites and final dev CI passed. See the implementation closure record below.

## Base and authority

- Reviewed base: bec2ad2084bd8f01bbf139ac7e33ac195eb5d037, fetched from origin/dev on 2026-09-23, the WS4 merge.
- Worktree: C:/Users/Vladimir_sva/.codex/worktrees/ws5-enforcement-truthfulness/travel-agency.
- Branch: codex/ws5-enforcement-truthfulness. At planning/self-review time HEAD equals the reviewed base, and only this plan is untracked.
- Before implementation, verify status and that HEAD descends from this exact pinned base. A later advance of origin/dev is new information, not permission to silently replace the base, rewrite this branch or discard the plan. Before any later branch switch, recheck the requested ancestry.
- The planning phase changed this document only. The user subsequently approved source implementation with “го дальше”. Commits, publication, migrations, external supplier actions and deployment still require their applicable authority and are outside this request.
- Implementation groups: A = Tasks 1–2, B = Tasks 3–5, C = Task 6. Review A and B as bounded changes. C requires its own frontend branch/change from the agreed integration base when reached; do not mix its source changes into the backend change or create that future worktree during planning.

## Evidence and gaps at the reviewed base

| Area | Existing evidence | Remaining WS5 work |
|---|---|---|
| Host composition | Program.cs imports Flights/Identity Api.Composition; project, type and global-call-site tests exist | Retain and rerun these guards; do not rebuild composition |
| Module/layer boundaries | ModuleBoundaryTests is asymmetric; DependencyDirectionTests uses class selectors and permissive empty results | Complete all module pairs and layer edges at both evaluated-project and IL levels |
| Contract consumers | IntegrationContractArchitectureTests evaluates the four approved direct project consumers | Also prevent a non-Composition Flights Api type from consuming the transitively available contract |
| Domain events | FlightsArchitectureTests already checks the DomainEvents namespace without an Event suffix | Generalize the global suffix-based rule across modules; preserve the correct Flights check |
| README/OpenAPI | Real Host document has a snapshot; Aspire smoke exercises status and signed webhook | Connect all README request examples to checked source, execute their contracts, and wire the actual smoke into CI |
| Documentation | Root, Flights, Identity and Shared AGENTS contain obsolete WS2/WS3 facts | Audit all eight instruction scopes; repair current assertions without altering historical evidence |
| AI decision | Program registers AnthropicClient.AsIChatClient; ADR 0012 remains Accepted and describes unimplemented agents | Deferred ADR 0012 and explicit current-runtime amendments/cross-links |
| Legacy | No tracked or physical root src/ at the reviewed base | Bounded historical review; no deletion and no retrospective claim that prior approval was proven |
| Frontend | NxWelcome renders above /status; App unit test and stories have starter assumptions | Separate small shell cleanup, including the existing story and browser assertions |

## Global constraints

- The deliverable is a code demo. Preserve source-ready, scoped integration-proven and live-proven distinctions. Deployment and paid provider execution are not acceptance gates.
- Preserve WS2 Core NATS request/reply, WS3 ownership, WS4 transitions, durable projection, recovery and notification guarantees. A discovered runtime defect is reported and scoped separately, not hidden by weakening a guard.
- Use expected-project inventory plus actual repository discovery. An added module or removed DLL must fail until policy is explicitly updated; it must not disappear from test data.
- Dependency rules inspect all IL types, including interfaces and structs. Positive selectors distinguish implemented behavior from marker-only scaffolds.
- Use current DTOs, real OpenAPI and endpoint metadata as authorities. A 200 response alone does not prove that every request field was recognized.
- Use fictional input. Automatic examples must not invoke supplier search/booking, SMTP or Anthropic; positive provider behavior is tested with explicit fakes.
- No new generic architecture framework or general JSON Schema engine. Reuse current MSBuild readers, ArchUnitNET models, HTTP fixtures and CI inventory.
- Exclude both Category=AiEval and Category=AiEvals in the aggregate local command. The current eval runner can call Anthropic whenever ANTHROPIC_API_KEY is present; missing credentials must not be the only protection.
- Every required check is reported as Passed, Failed or NotRun. Missing Docker does not waive an integration gate or prevent source-only checks from running.

## Review focus and required regression cases

1. Imported or Release-only unused ProjectReferences violate both module and layer rules: Task 1/2 mutation fixtures.
2. A missing module DLL, an unlisted sixth module or an interface-only forbidden dependency cannot produce green empty selectors: Task 1 inventory/IL mutations.
3. Identity Api currently contains Composition only. Empty non-Composition selectors there are legitimate, while disappearance of Flights Endpoints/Contracts/Middleware or Identity Composition must fail: Task 2 selector cases.
4. Non-Composition Api use of Infrastructure or IntegrationContracts.AI fails even with a transitive assembly reference; real non-Event-named domain events remain selected: Task 2 mutations.
5. Unknown README fields, stale literal dates, missing auth/idempotency metadata or an invalid OpenAPI route fail automatically; smoke validation does not count as a successful real booking: Task 3.

## Change set A — architecture enforcement

### Task 1: Make the module inventory and cross-module matrix complete

**Files:**

- Modify tests/Travel.Tests.Architecture/ArchitectureTestBase.cs and ModuleBoundaryTests.cs.
- Create tests/Travel.Tests.Architecture/Support/ModuleArchitectureInventory.cs.
- Reuse Support/EvaluatedProjectReferences.cs unchanged unless an actual reader gap is found.
- Create Fixtures/ModuleMatrix/Source.proj and ForeignReference.props under the architecture project.

**Interfaces:** ModuleArchitectureInventory exposes the exact five module names/folders and four layer names. Build project paths and assembly names from those entries. Expose the 20 ordered distinct module pairs; inspect each of the 20 source projects in Debug and Release through ForProjectAsync. Keep policy data separate from the reader and return diagnostics containing origin, target and configuration.

- [x] Add a failing inventory test: discovered production module csproj paths must equal the expected 5 x 4 set. Exclude bin/obj rather than accepting arbitrary extra projects. Compare exact expected module assembly names with available test output before loading. Prove a missing DLL and a controlled unknown module each fail.
- [x] Add a data-driven evaluated-reference rule for all 20 ordered module pairs, all source layers and both configurations. Reject cross-module references even when no type uses the reference. The imported-reference fixture must also cover a Release-only edge.
- [x] Reuse the existing evaluation cache so the matrix evaluates 20 projects per configuration, not a new MSBuild process for every assertion. Evaluation failures remain test failures with diagnostics.
- [x] Replace asymmetric namespace rules with checks over all matching Arch.Types and their Dependencies. Use namespace segment boundaries and assembly identity; do not allow a lookalike prefix or a misleading namespace to hide a reference to another module assembly.
- [x] Require non-marker behavior in every Flights layer and Identity Infrastructure/Api.Composition. Core markers in Identity/Hotels/Rail/Trips prove assembly presence only. Empty scaffold layers still receive evaluated-reference checks; inspect any types added to them immediately.
- [x] Add a controlled interface or record-struct dependency fixture that the old Classes-only selector misses. Assert the rule returns the exact offending edge.
- [x] Run the architecture project in Debug and Release. Both the clean repository matrix and the tests that assert rejection of controlled violations must pass.

**Acceptance:** all 20 pairs, all 20 module projects and both configurations are accounted for. A missing/extra project or assembly fails independently of the boundary result. No test-only fixture namespace can accidentally enter the production inventory.

### Task 2: Enforce layer direction, Api exceptions and contract/event selectors

**Files:**

- Modify tests/Travel.Tests.Architecture/DependencyDirectionTests.cs, ApiCompositionBoundaryTests.cs, NamingConventionTests.cs and IntegrationContractArchitectureTests.cs.
- Update ArchitectureTestBase.cs and Travel.Tests.Architecture.csproj to load all four production app assemblies (Host, AI, AppHost and ServiceDefaults) and the existing contract assembly for consumer IL inspection. Add AI and an aliased AppHost project reference following the Host integration project pattern; add no direct ProjectReference from the architecture project to IntegrationContracts.AI.
- Add Fixtures/LayerMatrix/Source.proj and ForbiddenLayerReference.props plus controlled IL fixture types in the owning test files.
- Preserve SharedCharterTests.cs, HostModuleProjectReferenceTests.cs, HostModuleTypeDependencyTests.cs and CompositionCallSiteTests.cs. Change them only for an evidenced coverage gap.

**Policy:** for every module, Core cannot reference Application/Infrastructure/Api; Application cannot reference Infrastructure/Api; Infrastructure cannot reference Api. Enforce these rules on evaluated ProjectReferences in Debug/Release and on IL. The same-module Api -> Infrastructure project reference is permitted only for implemented composition owners; every Api type outside the exact Composition namespace subtree is still forbidden from using Infrastructure. Retain scaffold Api project-reference restrictions.

The layer predicate is explicit:

    static bool IsForbiddenLayerEdge(string source, string target) => source switch
    {
        "Core" => target is "Application" or "Infrastructure" or "Api",
        "Application" => target is "Infrastructure" or "Api",
        "Infrastructure" => target is "Api",
        _ => false
    };

- [x] Add controlled unused/imported layer references before implementing the generalized evaluated-project rule. Prove all six forbidden layer directions, including a Release-only case. Keep the existing Core/Application -> Shared.Web rule at project and IL level.
- [x] Add IL rules for all modules, with source types selected by assembly and namespace. Only Flights Endpoints/Contracts/Middleware and Flights/Identity Composition require nonempty Api selectors today. Identity non-Composition and scaffold Api selectors may be empty by a named baseline policy; any future types still receive the prohibition automatically.
- [x] Prove CompositionExtra is not Composition, and that an Api helper outside the three named transport areas cannot reference Infrastructure.
- [x] Retain the four exact evaluated direct consumers of IntegrationContracts.AI: Flights Application, Flights Api, Travel.AI and Travel.Tests.Contract. Add a production IL consumer rule restricting actual contract use to Flights Application, Travel.AI and Flights Api.Composition. Scan forbidden production origins too, including Host, AppHost, ServiceDefaults, Infrastructure, Shared and scaffold modules; a project-level allowlist alone cannot constrain a namespace.
- [x] Prove a non-Composition Flights Api type using the contract is rejected, and that the existing FlightsModule composition usage remains allowed. Load the contract from its existing transitive output without weakening the leaf package/reference allowlist.
- [x] Generalize global domain-event selection to each module's Core.DomainEvents namespace plus IDomainEvent implementations. Classes/record structs in the event namespace must implement IDomainEvent; exclude enums and the interface declaration itself. Require a nonempty Flights event set and explicitly select OfferHeld/OrderConfirmed, neither of which ends in Event. Other modules currently have no domain events. Preserve the already correct Flights-specific guard or consolidate it only with equivalent positive and mutation coverage.
- [x] Run all architecture tests and the inventory validator. Assert the existing Shared/Host/contract tests still run and pass. Do not create a second parser or replace established allowlists with weaker discovery.

**Acceptance:** both unused project edges and actual type dependencies fail. The only Api exception is exact Composition. Empty selectors represent documented scaffold state, not a global waiver.

## Change set B — executable demo and truthful documentation

### Task 3: Put every README request under an executable contract check

**Files:**

- Modify README.md, package.json and .github/workflows/ci.yml.
- Create docs/examples/flights-requests.json containing the five POST examples (search, NL-search, quote, hold, confirm) and the SSE GET example, with id/method/path/pathParameters/headers/body fields. The path is the exact OpenAPI route template; SSE maps orderId to the aggregateId substitution token in pathParameters.
- Create tools/docs/readme-examples.mjs, readme-examples.test.mjs, readme-smoke.mjs and readme-smoke.test.mjs.
- Create tests/Travel.Host.Tests.Integration/Documentation/ReadmeExamples.cs and ReadmeRequestExamplesTests.cs.
- Extend tests/Travel.Host.Tests.Integration/Web/HostWebContractTests.cs. Reuse FlightsApiFixture, FakeMessageBus and TestAuthHandler for the lightweight contract tests.

**Shared contract:** the JSON example catalog is the single source of request methods, paths, required headers and bodies. Six explicit substitution tokens are supported: departureDate, providerOfferRef, aggregateId, jwt, holdIdempotencyKey and confirmIdempotencyKey. Each manual booking command needs a fresh GUID. Departure date is UTC today + 30 days; tests supply a fixed clock. Render the catalog into one marked README region. The renderer accepts --check (non-mutating, nonzero on drift) and --write (updates only that region). Unknown/missing/duplicate example IDs or tokens fail. Require departureDate in the search and NL examples to remain a substitution token, not a calendar literal; require pathParameters to match the route placeholders exactly.

- [x] Write renderer tests for changed method/header/body, missing or duplicate markers, unresolved tokens and a stale README region. Add package script check:readme-examples to run the two Node test files and the non-mutating renderer check. The CI lint job invokes it.
- [x] Correct catalog/README inputs from Contracts.cs: passengerCount for search; givenName/familyName/dateOfBirth/gender/email/phone for hold; only real request fields. Use a relative departure token in both date and NL examples. Keep current quote/hold/confirm and SSE prerequisites explicit: sandbox provider offer, JWT, flights:book where applicable, and Idempotency-Key for the current guarded command routes.
- [x] Create trait-free ReadmeRequestExamplesTests using the catalog and FlightsApiFixture. Execute all five POST bodies with fake bus responses and use OnCapture to assert the exact mapped query/command values. Check SSE route/id/auth with the existing fake registry and bounded cancellation. For authenticated cases, supply TestAuthHandler.UserIdHeader and ScopesHeader with flights:book; the catalog still asserts the documented Bearer header. Keep these test-only headers out of README. These tests prove request binding and endpoint behavior, not JWT verification, database persistence, real Keycloak, NATS or suppliers.
- [x] Reject unknown JSON members when deserializing the example into each actual request DTO, including nested passengers. This is essential because ordinary ASP.NET binding can ignore passengers in SearchRequest and silently use its default count. Add mutation tests for passengers, firstName, passportNumber and an omitted required passenger value.

The strict test serializer uses the actual request DTOs, not a second handwritten schema:

    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    var request = JsonSerializer.Deserialize<SearchRequest>(resolvedExampleJson, options);
    request.ShouldNotBeNull();
    request.PassengerCount.ShouldBe(1);

- [x] Extend the real Host OpenAPI test to assert every catalog method/path and its actual requestBody DTO/schema association. Resolve local component references needed by these DTOs; compare named properties/required fields and nested passenger fields. Keep the existing complete snapshot. Do not claim that OpenAPI alone proves auth metadata or IResult response shape: use real EndpointDataSource/HTTP assertions for those.
- [x] If that real Program test reveals a Wolverine service-location failure for the existing IFxRates registration, add a narrowly scoped Flights Api.Composition policy and prove the same test turns green; do not substitute IFxRates in the test.
- [x] Add a real Program HTTP test for the documented valid search using a recording IFlightSearchProvider that returns a successful empty result and an ISearchCache fake that misses. Assert the actual handler was reached, captured criteria match the catalog, and the HTTP response is 200 with empty offers/partialFailures. Keep the real message bus, routing, validation and database initialization; external Wolverine transports remain disabled as in the existing HostWebFactory. Any unplanned provider call fails the test.
- [x] Implement the live local runner as exactly three bounded checks: GET /api/status with db=ok; GET /openapi/v1.json with all catalog routes; POST /api/flights/search using a copy of the search body with origin=LE, requiring 400 application/problem+json and IataCode.Length. The invalid origin is rejected by SearchEndpoint before provider dispatch. Do not send the catalog's quote/hold/confirm/NL requests from this runner.
- [x] Give the runner a --base-url argument, 10-second per-request deadline and nonzero exit on timeout, redirect, wrong code/content, malformed JSON or absent route. Output phase/status only. Its Node tests use a local fake HTTP server to assert request count, payloads, error handling and that no other route is contacted.
- [x] Add package script smoke:readme. In the existing CI test-e2e job, run it against http://localhost:5099 after Wait for /api/status and before starting Angular. This is the real Aspire/module-route proof missing from script unit tests. Keep the existing host-http and host-integration filters: the new trait-free request tests run in host-http; real Program/OpenAPI tests run in host-integration. No new test project or duplicate lane is needed.
- [x] Document the local commands and their limits. The automatic runner proves status/OpenAPI/validation; positive search is proved by the Host test with a fake supplier. Authenticated sample flows remain manually runnable with their stated prerequisites and are automatically checked at the request-contract level. Do not promise a Mailpit delivery or complete booking from the validation smoke.

**Focused commands:**

    npm.cmd run check:readme-examples
    dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --filter "FullyQualifiedName~ReadmeRequestExamplesTests"
    dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --filter "FullyQualifiedName~HostWebContractTests"
    npm.cmd run smoke:readme -- --base-url http://localhost:5099

Run the last command only against the disposable/local stack prepared for this verification. A passing fake HTTP server test cannot substitute for the actual Host run.

### Task 4: Align public claims and all canonical instruction scopes

**Files:**

- Create docs/architecture/current-state.md.
- Modify README.md, AGENTS.md, modules/flights/AGENTS.md, modules/identity/AGENTS.md, shared/AGENTS.md and docs/conventions/developer-tooling.md.
- Audit the remaining scopes apps/Travel.AI/AGENTS.md, modules/hotels/AGENTS.md, modules/rail/AGENTS.md and modules/trips/AGENTS.md, plus their exact same-directory CLAUDE.md adapters.
- Modify docs/adr/0012-maf-as-primary-agent-runtime.md and docs/adr/0002-ai-as-extracted-service.md; review ADRs 0001/0004/0006/0007/0009/0015/0016/0020 and the related implemented-AI claims in ADR 0011. Preserve accepted semantics and historical text with clearly scoped amendments.

- [x] Build a compact claim/evidence table in current-state.md, covering Host/facades, five module states, Shared ownership, direct Anthropic runtime, Core NATS, environment-specific initialization, booking projection/recovery, HTTP contracts, tests and current frontend. Link source files and owning tests/ADRs. Include a small process/data-flow diagram derived from current Program registrations.
- [x] Correct all four known stale instruction surfaces: root/Flights split-composition claims; Identity's Host-owned Flights policy and empty-Api claim; Shared's absent-initializer and Shared.Web TestOnlyGuard ownership claims. Verify the Flights instructions describe WS4's explicit NonTransactional booking writers alongside ordinary transactional handlers. Keep all eight canonical/import pairs consistent.
- [x] Mark ADR 0012 Deferred for a separate MAF product milestone. State current direct Anthropic IChatClient NL-search and unimplemented MAF/custom agents plainly. Keep prior rationale visibly historical; do not retain unverified framework release/licensing statements as current project proof.
- [x] Correct ADR 0002's current process/runtime/transport assertions through an amendment linked to ADR 0020 and Deferred ADR 0012. Review ADR 0011 for the same unimplemented-agent/eval-store claims and distinguish existing eval code from planned evaluation infrastructure.
- [x] Record a reviewed row for each specifically required ADR: 0001, 0004, 0006, 0007, 0009, 0015, 0016 and 0020. Explain changed text or a no-change conclusion with current code/test pointers. Preserve WS2–WS4 amendments. If the code contradicts an accepted semantic decision, report the discrepancy before proposing a new decision.
- [x] Replace README's empty architecture placeholder with the overview link/diagram. Remove the broken docs/byo-keys.md link and keep the existing provider configuration sections, corrected against typed options and registrations. Do not introduce a new keys guide simply to satisfy the broken link.
- [x] Separate source/demo capabilities from roadmap: enabled Flights/Identity versus scaffolds, real AI NL-search versus future agents, Angular status foundation versus booking UI, and local proof versus deployment. Historical ADR version choices stay historical; current version labels come from checked-in manifests. Do not turn planned JetStream uses into a claim that current durable booking queues use that transport.
- [x] Run the dependency-free harness and inventory checks and verify touched Markdown links. The harness validator checks paths/imports/inventory, not whether prose is true; complete the code-to-claim table even if that validator is green.

### Task 5: Record the already-completed legacy deletion accurately

**Files:** Create docs/operations/2026-09-23-legacy-src-review.md and link it from current-state.md.

- [x] Reconfirm the root src/ absence with git ls-tree and filesystem inspection.
- [x] Inspect the deletion/file inventories of d17eca9dcdc190665bfce10a50211715db02a93e (Foundation wipe) and 4f8f28ddecef155797cfd54b7f408b709dfeb3c6 (Rail legacy removal), their relevant parent-tree fixtures/provider sources, and corresponding retained modules/tests/docs. Bound the review to useful fixtures/provider knowledge; do not reopen all project history.
- [x] Record exact commits, reviewed paths, retained knowledge, candidate omissions and any in-repository evidence of the earlier deletion review. When prior review evidence is unavailable, say so.
- [x] The spec's gate is required before a future deletion; a retrospective audit cannot prove it happened earlier. Mark new deletion as not applicable at this base. Any restoration candidate requires a separate reviewable proposal.

## Change set C — separate frontend foundation

### Task 6: Replace the starter with the existing status application's minimal shell

**Files:** Modify apps/web/src/app/app.ts, app.html, app.spec.ts and app.stories.ts; remove nx-welcome.ts and nx-welcome.stories.ts; extend tests/travel-e2e/specs/health.spec.ts. Change app.scss only for the small shell layout.

- [x] In the distinct frontend change, replace NxWelcome with a Travel Platform brand/link and the router outlet. Preserve the default /status route, its data loading, API client and SSR behavior. The page's existing System Status heading remains the page heading.
- [x] Update the existing unit test and App story to assert the intended shell instead of starter text or the incidental /app/ expression. Provide the Router test/story services needed by the shell rather than relying on NxWelcome content.
- [x] Extend the existing browser test to check both / and /status reach the status page, db: ok is visible, and starter content is absent. Do not introduce a booking UI or a visual redesign in this cleanup.
- [x] Run web unit/build/lint and the existing travel-e2e project against the local stack. Confirm no NxWelcome references remain in application or stories.

## Verification and closure

Before implementation, record baseline results for the focused architecture suite and dependency-free harness/inventory checks. Existing failures are recorded before evaluating WS5 changes. Do not claim a clean baseline from the planning audit.

| Gate | Command or lane | What it proves |
|---|---|---|
| Architecture | Architecture project in Debug and Release | Evaluated graph, IL matrix, positive selectors and controlled violations |
| Example parity | npm.cmd run check:readme-examples | README/catalog parity and runner/renderer behavior |
| HTTP examples | ReadmeRequestExamplesTests, host-http lane | Real endpoint binding with fake downstream responses |
| Host/OpenAPI | HostWebContractTests, host-integration lane | Real Program/OpenAPI and successful search with a fake supplier |
| Real local stack | npm.cmd run smoke:readme; existing AspireSmoke and test-e2e lanes | Status/OpenAPI plus real module validation on disposable infrastructure |
| Frontend | web test/build/lint and travel-e2e | Status shell and routing after starter removal |
| Harness/inventory | existing check:ai-harness and check:dotnet-inventory | Import/path consistency and exact CI lane coverage |

- [x] Run restore/build regardless of Docker availability, then focused checks as each task lands. These commands passed:

      dotnet build Travel.slnx --configuration Release
      npm.cmd run check:ai-harness
      npm.cmd run check:dotnet-inventory
      npm.cmd run check:readme-examples
      npx.cmd biome ci .
      dotnet csharpier check .

- [ ] Complete one uninterrupted local aggregate run with Docker ready and paid categories explicitly excluded. The attempted run was interrupted; see the closure record:

      dotnet test Travel.slnx --maxcpucount:1 --filter "Category!=AiEval&Category!=AiEvals"

- [x] Run frontend checks in the separate frontend worktree:

      npx.cmd nx test web --watch=false
      npx.cmd nx build web
      npx.cmd nx lint web
      npx.cmd nx e2e travel-e2e

- [x] Do not rerun the aggregate after prose-only changes unless a required gate or new failure justifies it. No paid lane, authenticated harness agent run, migration application to shared data or deploy is required.
- [x] Report each required gate as Passed/Failed/NotRun, with exact commit and commands. Source-ready requires its source/test checks; integration-proven names the concrete real resources and fake boundaries. A completed Host test and a NotRun Aspire/browser check must be reported separately. Live-proven remains unclaimed.
- [x] Review the plan against every WS5 bullet and criteria 17–19. Do not call all WS5 complete while C or a required integration gate remains pending.

## Self-review record — 2026-09-23

The review was static: compared the plan with the design, current source, tests, CI and Git history. No product code or tests were executed/changed during self-review.

| Finding in the first plan | Correction |
|---|---|
| Cross-module project checks did not cover forbidden same-module project edges or newly added modules | Explicit inventory parity, both configuration graphs, six layer edges and IL fixture coverage |
| Existing project allowlist could not enforce Flights Api.Composition-only contract usage | Added actual consumer IL rule and required AI/contract assembly loading |
| Generic positive selector wording would misclassify Identity's composition-only Api and marker-only Core | Exact implemented selectors and named empty/scaffold policy |
| Flights already had a correct namespace-based domain-event guard | Preserve that proof and generalize the global rule instead of duplicating Flights |
| Search-only smoke left other README payloads unchecked; default JSON binding could hide bad fields | Shared catalog, strict DTO checks, all request examples and real Host positive search |
| Runner tests were not wired to execution against the actual app | Exact lint/HTTP/Host/test-e2e lane ownership and local command |
| Root-only documentation audit missed stale scoped instructions and adjacent AI claims | All eight scopes, known four-file repairs and ADR 0002/0011 consistency |
| Aggregate test could run paid AI with an existing environment key | Explicit exclusion of both paid categories |
| Retrospective legacy audit risked implying earlier review approval; frontend story was omitted | Clear historical evidence limit and separate frontend story/browser changes |

This self-review record describes the preimplementation checkpoint. The later implementation, verification and final diff review are recorded below.

## Implementation closure — 2026-09-23

WS5 Tasks 1–6 above are complete. The architecture and documentation change was merged through [PR #15](https://github.com/svasorcery/travel-agency/pull/15/changes) (source commit 75974e1, merge commit 6c54ed2). The separate frontend foundation change was merged through [PR #16](https://github.com/svasorcery/travel-agency/pull/16/changes) (source commits ab1e8b0 and 1b4c824, merge commit 1ae64d4). The final dev push [CI run 35898804687](https://github.com/svasorcery/travel-agency/actions/runs/35898804687) succeeded at 1ae64d4.

| Gate | Status | Recorded evidence |
|---|---|---|
| Build and architecture | Passed | Release solution build: 0 warnings/errors. Architecture project: 167/167 in Release and Debug, including controlled IL and evaluated-project violations. |
| README and HTTP contracts | Passed | npm.cmd run check:readme-examples: 9/9 plus catalog parity. ReadmeRequestExamplesTests: 8/8. Host integration: 166/166, including real Program/OpenAPI and a valid search with a fake supplier. |
| Other backend suites | Passed | Flights Integration: 279/279; Flights Unit: 480/480; AI: 45/45; Identity Unit: 22/22; Contract: 9/9. Empty scaffold test projects exited successfully without test cases. |
| Local stack and frontend | Passed | npm.cmd run smoke:readme -- --base-url http://localhost:5099 passed against disposable AppHost resources. Web unit/build/lint/Storybook passed; local browser E2E: 2/2. PR #16 test-e2e passed; the final dev push skips that PR-only lane by design. |
| Harness, inventory and formatting | Passed | npm.cmd run check:ai-harness: 90/90. npm.cmd run check:dotnet-inventory: 62 passed, 1 skipped. Full Biome CI, CSharpier check, Markdown links and git diff --check passed. |
| One uninterrupted local aggregate | NotRun to completion | dotnet test Travel.slnx --configuration Release --maxcpucount:1 --filter "Category!=AiEval&Category!=AiEvals" --no-build --verbosity minimal was started and interrupted when quiet output was mistaken for a stall. Its long Flights suite and the affected Host/architecture suites were then rerun separately and passed; the final dev CI passed. This is not claimed as a green single-command aggregate run. |
| Live external proof and deployment | NotRun by scope | Paid AI evals, real supplier/Anthropic calls, migration application to shared data and deployment were outside this code-demo WS5. Live-proven is not claimed. |

The one unchecked verification item above preserves the exact aggregate-command exception. The root src/ directory was already absent; WS5 recorded its history and did not delete or restore it. No WS5 source, frontend, CI or review gate remains pending.
