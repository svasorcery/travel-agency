# Codex-First AI Harness (WS1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the local-only, duplicated Claude/Codex setup with a tracked Codex-first AI harness whose project facts, workflows, custom agents, Claude compatibility adapters, and integrity checks all come from one repository-owned source of truth.

**Architecture:** `AGENTS.md` is the canonical instruction contract at root and per-scope level; each `CLAUDE.md` is an exact same-directory import. Reusable procedures live only in `.agents/skills`, while `.claude/skills`, legacy Claude commands, and both clients' custom-agent manifests remain thin discovery/capability adapters. Broken edit/Stop hooks are removed; deterministic formatting remains owned by explicit agent verification, Lefthook, and CI. A dependency-free Node 22 validator enforces the complete inventory and parity contract on Windows and Linux.

**Tech Stack:** Markdown, YAML frontmatter, TOML, JSON, Node.js 22 built-ins (`node:test`, `node:fs`, `node:path`, `node:child_process`, `node:readline`), npm scripts, GitHub Actions, Codex repository instructions/skills/subagents and App Server JSON-RPC, Claude Code imports/skills/subagents.

**Spec reference:** [`docs/superpowers/specs/2026-08-11-ai-harness-architecture-remediation-design.md`](../specs/2026-08-11-ai-harness-architecture-remediation-design.md), specifically D1-D3, WS1, testing strategy row “AI harness,” migration/compatibility rules, and acceptance criteria 1-3.

**Current official client references:** [Codex `AGENTS.md`](https://learn.chatgpt.com/docs/agent-configuration/agents-md), [Codex skills](https://learn.chatgpt.com/docs/build-skills), [Codex custom agents](https://learn.chatgpt.com/docs/agent-configuration/subagents), [Codex non-interactive mode](https://learn.chatgpt.com/docs/non-interactive-mode), [Codex developer commands](https://learn.chatgpt.com/docs/developer-commands?surface=cli), [Codex App Server](https://learn.chatgpt.com/docs/app-server), [App Server protocol reference](https://github.com/openai/codex/blob/main/codex-rs/app-server/README.md), [official Codex IDE extension](https://marketplace.visualstudio.com/items?itemName=openai.chatgpt), [Claude imports](https://code.claude.com/docs/en/memory), and [Claude skills/legacy commands](https://code.claude.com/docs/en/slash-commands).

## Global Constraints

- This plan implements **WS1 only**. Do not change `.cs`, `.csproj`, `.slnx`, EF migrations, runtime configuration, `Program.cs`, module composition, Wolverine/Marten/NATS behavior, database initialization, CI branch filters, deploy workflows, or production infrastructure.
- Start from the existing `codex/ai-harness-architecture-remediation` branch and verify it still descends from `origin/dev`. Do not replace the requested base with current `HEAD`, rewrite branch ancestry, or force-move the branch.
- The current untracked `AGENTS.md` and `.codex/` files are migration inputs. Inspect and reconcile them; do not overwrite them blindly, clean them, or stage unrelated untracked files.
- Historical specs/plans may legitimately quote broken hook variables and obsolete paths as audit evidence. Integrity scans must cover only active harness surfaces, not `docs/superpowers/**`.
- `AGENTS.md` describes current truth plus explicit architectural rules. It must distinguish current implementation, approved future remediation, and unimplemented roadmap items.
- Do not hard-code an ADR count, Aspire dashboard port, package patch version, model ID/pricing, development credentials, or test counts in active instructions.
- Invoking a skill or custom agent does not grant authority to stage, commit, push, create/switch branches, generate/apply migrations, deploy, or mutate external systems. A design/review request does not authorize implementation edits.
- Task 8's authenticated live acceptance has two narrow client-state exceptions approved with this plan: (1) read each TOML in the active personal agent directory into memory solely to parse its required `name` for collision detection, then discard it without logging content; and (2) create one temporary local Codex root/child thread tree and delete exactly that tree in `finally`. It may not inspect any other personal client content, or archive, delete, rename, edit, disable, or otherwise mutate unrelated user tasks, agents, skills, configuration, or authentication.
- Keep all eight canonical skills instruction-only in WS1: one `SKILL.md` each, no `README.md`, assets, scripts, copied reference documents, or optional client UI metadata. This keeps the canonical workflow surface small and client-neutral; add such resources later only in response to demonstrated usage.
- Do not use symlinks or assume `@` imports work outside `CLAUDE.md`. Claude skill, command, and agent adapters must explicitly instruct the client to read the canonical repository path.
- Use LF line endings and UTF-8. Commands in skills must work from PowerShell and POSIX shells; avoid `head`, Bash-only line continuations, and path-case assumptions.

---

## File Structure and Responsibilities

```text
AGENTS.md                                      canonical repository facts and working rules
CLAUDE.md                                      exact `@AGENTS.md` compatibility import

apps/Travel.AI/{AGENTS.md,CLAUDE.md}           extracted AI service scope and import
modules/{flights,hotels,rail,trips,identity}/
  {AGENTS.md,CLAUDE.md}                        canonical module facts and import
shared/{AGENTS.md,CLAUDE.md}                   shared-layer charter and import

.agents/skills/
  {spec,adr,explore-domain,test-this,
   integration-from-openapi,domain-modeling,
   migration-authoring,test-authoring}/SKILL.md
                                                canonical reusable workflows

.claude/skills/<same-eight>/SKILL.md            thin Claude skill discovery adapters
.claude/commands/
  {spec,adr,explore-domain,test-this,
   integration-from-openapi}.md                legacy Claude command adapters

.codex/agents/
  {domain-modeler,adr-writer,test-author,
   migration-author,integration-mapper}.toml   Codex role and sandbox intent
.claude/agents/<same-five>.md                   Claude role and tool/permission intent

tools/ai-harness/validate.mjs                  embedded harness contract + CLI validator
tools/ai-harness/validate.test.mjs             portable validator fixtures/regressions
tools/ai-harness/verify-codex.mjs              clean-clone Codex/App Server live acceptance client
tools/ai-harness/verify-codex.test.mjs         offline parser/state-machine regressions
package.json                                   local harness check entry points
.github/workflows/ci.yml                       non-Nx harness gate in existing lint job

README.md                                      truthful public AI/runtime summary
CONTRIBUTING.md                                Codex-first contributor workflow
.devcontainer/devcontainer.json                Codex primary + Claude compatibility extensions
```

### Canonical source rules

| Concern | Canonical source | Adapters/consumers |
|---|---|---|
| Repository and module facts | nearest `AGENTS.md` | same-directory `CLAUDE.md` |
| Reusable workflow | the eight explicitly listed `.agents/skills/*/SKILL.md` files | Claude skill, legacy command, custom agent |
| Role capability | client custom-agent manifest | canonical skill defines procedure/authority |
| Formatting | CSharpier/Biome commands + `lefthook.yml` + CI | no AI-client edit hooks |
| Harness inventory | constants in `tools/ai-harness/validate.mjs` | tests, npm, CI |

---

## Task 1: Protect the migration baseline and record the approved plan

**Files:**

- Read only: `AGENTS.md`, `.codex/hooks.json`, `.codex/agents/*.toml`
- Read only: tracked `.claude/**`, root/module/shared `CLAUDE.md`
- Track: `docs/superpowers/plans/2026-08-11-codex-first-ai-harness.md`

- [ ] **Step 1: Verify branch ancestry and worktree ownership**

```powershell
git status --short --branch
git merge-base --is-ancestor origin/dev HEAD
if ($LASTEXITCODE -ne 0) { throw 'Current branch no longer descends from origin/dev.' }
```

Expected: branch `codex/ai-harness-architecture-remediation`; only the already-known untracked `AGENTS.md`, `.codex/`, and this plan are present before implementation work begins.

- [ ] **Step 2: Verify the untracked inputs still match the audited snapshot**

```powershell
git hash-object AGENTS.md .codex/hooks.json .codex/agents/adr-writer.toml .codex/agents/domain-modeler.toml .codex/agents/integration-mapper.toml .codex/agents/migration-author.toml .codex/agents/test-author.toml
```

Expected output, in the same order:

```text
39e11f482a196e436ee73b17c7c34e11c42a4799
005ab7dcd45a80ae98a934f07ba92e6c36373059
fbcdfa346eb83e72292ebb119fd51f3d1865aa58
292017c345af73802f658a21ec8eee1bf8e41be4
3271b294fb32b7413a83d630b05f12ab10b97981
4cb4299852e3a98e0031fe266f6ed7b33ce9f636
ec8a1ccc0c77dd9232db273c89b3f03fb0474be0
```

If any hash differs, inspect that file and reconcile its intent with this plan before editing. Do not discard the newer content.

- [ ] **Step 3: Record the tracked baseline**

```powershell
git diff --name-status origin/dev...HEAD
git diff --name-only
```

Expected: the accepted remediation design is the only committed branch change; this plan is the only additional untracked document; there is no unrelated tracked diff.

- [ ] **Step 4: Commit the approved plan as the execution checkpoint**

Do this only after the user has selected an execution approach for this plan.

```powershell
git add docs/superpowers/plans/2026-08-11-codex-first-ai-harness.md
git diff --cached --check
git commit -m "docs: plan Codex-first AI harness"
```

---

## Task 2: Make scoped `AGENTS.md` files canonical

**Files:**

- Replace/track: `AGENTS.md`
- Create: `apps/Travel.AI/AGENTS.md`
- Create: `modules/flights/AGENTS.md`
- Create: `modules/hotels/AGENTS.md`
- Create: `modules/rail/AGENTS.md`
- Create: `modules/trips/AGENTS.md`
- Create: `modules/identity/AGENTS.md`
- Create: `shared/AGENTS.md`
- Replace: `CLAUDE.md`
- Create: `apps/Travel.AI/CLAUDE.md`
- Replace: `modules/flights/CLAUDE.md`
- Replace: `modules/hotels/CLAUDE.md`
- Replace: `modules/rail/CLAUDE.md`
- Replace: `modules/trips/CLAUDE.md`
- Replace: `modules/identity/CLAUDE.md`
- Replace: `shared/CLAUDE.md`

- [ ] **Step 1: Prove the canonical scope inventory is currently incomplete**

```powershell
$instructionRoots = @('', 'apps/Travel.AI', 'modules/flights', 'modules/hotels', 'modules/identity', 'modules/rail', 'modules/trips', 'shared')
$missing = foreach ($root in $instructionRoots) {
  $path = if ($root) { Join-Path $root 'AGENTS.md' } else { 'AGENTS.md' }
  if (-not (Test-Path -LiteralPath $path)) { $path }
}
$missing
if ($missing.Count -eq 0) { throw 'Expected pre-migration AGENTS.md gaps were not found; re-audit before editing.' }
```

Expected: every nested `AGENTS.md` is missing; the root file exists only as an untracked duplicated draft.

- [ ] **Step 2: Rewrite root `AGENTS.md` as current, compact repository guidance**

Use these exact content responsibilities:

| Section | Required facts |
|---|---|
| Project shape | `Travel.AppHost` orchestrates resources/services; `Travel.Host` is the modular monolith; `Travel.AI` is a separate process; Angular is currently a foundation/status UI rather than a complete booking frontend. |
| Status map | Foundation and Flights M1 backend implemented; Identity is thin JWT/Keycloak integration; Hotels/Rail/Trips are scaffolds; Shared is primitives/helpers; Travel.AI currently implements NL-search, cost ledger, and observability. |
| Runtime honesty | Travel.AI currently uses direct Anthropic integration behind `Microsoft.Extensions.AI.IChatClient`; do not claim MAF or Semantic Kernel runtime wiring. |
| Layering | `Core -> Application -> Infrastructure -> Api` intent; no module may import another module's internals; external wire DTOs remain in Infrastructure. |
| Domain conventions | Expected outcomes use explicit `ErrorOr<T>` imports; value-object factories return `ErrorOr<T>`; events are past tense and implement `IDomainEvent`; production time comes from `TimeProvider`; do not assert that exceptions occur only in Core. |
| Provider naming | Name implementations by provider and capability, such as `DuffelFlightSearchProvider` and `TravelpayoutsSearchProvider`; do not prescribe a universal `{Provider}Adapter`. |
| Current debt boundary | Host/Flights composition is currently split; the approved Api-facade target is D4/WS3 and is not implemented by WS1. Do not extend the split or describe the target as current behavior. |
| Commands | Use the exact command block below. Do not include a fixed Aspire dashboard URL. |
| AI harness | Point to `.agents/skills`, `.codex/agents`, Claude compatibility files, dependency-free `npm run check:ai-harness`, and authenticated local `npm run verify:ai-harness:codex`; state the authority boundary and that the live verifier creates/deletes only its own temporary Codex thread tree. |
| References | Link the accepted remediation spec and `docs/adr/` without a numeric ADR count. |

Use this command block:

```text
dotnet run --project apps/Travel.AppHost
npx nx serve web
dotnet test Travel.slnx
dotnet tool restore
dotnet csharpier format .
dotnet csharpier check .
npx biome format --write .
npx biome ci .
npm run check:ai-harness
npm run verify:ai-harness:codex
```

In canonical documentation, add: “On Windows PowerShell, use `npm.cmd` / `npx.cmd` for the npm commands when execution policy blocks the `.ps1` shims.” Keep the unsuffixed form for POSIX/devcontainer shells.

- [ ] **Step 3: Create the Flights canonical instructions**

`modules/flights/AGENTS.md` must distinguish implemented behavior from known remediation:

- Flights M1 backend is implemented; the full booking frontend remains a separate milestone.
- Implemented surfaces include mixed Duffel bookable + Travelpayouts deeplink search, NL-search, quote/hold/confirm/cancel, get/list, Duffel webhook, SSE/email notifications, idempotency, cache/FX normalization, health checks, and telemetry.
- `BookingAggregate` is Marten event-sourced with `None`, `OfferQuoted`, `Held`, `Confirmed`, `Ticketed`, `Cancelled`, and `Refunded` states.
- Current events are `OfferQuoted`, `OfferReQuoted`, `OfferHeld`, `PaymentAuthorized`, `OrderConfirmed`, `OrderTicketed`, `OrderCancelled`, and `OrderRefunded`.
- Core ports are `IFlightSearchProvider`, `IFlightBookingProvider`, and `IPaymentGateway`; provider clients/DTOs/mappers stay under Infrastructure.
- Marten owns the booking stream; EF Core owns the read model, idempotency, webhook inbox, and deeplink cache.
- Preserve the useful transaction rule: ordinary Wolverine handlers use the transaction policy; explicit `IDbContextOutbox<FlightsDbContext>` is reserved for a boundary that must translate a meaningful DB race, such as duplicate webhook delivery, to HTTP behavior.
- Cohesive DTO/query records may share a file; the one-handler/endpoint-per-file convention remains.
- State explicitly that Host still owns part of Flights registration/routing/telemetry/auth/middleware and that WS3 will move module-specific contributions behind the Api facade.
- Link unit, integration, and contract test locations without counts.
- Record current M1 limits: single passenger, non-Production test wallet, and no real booking UI.

- [ ] **Step 4: Create honest scaffold and Identity instructions**

| File | Exact current-state contract |
|---|---|
| `modules/hotels/AGENTS.md` | Scaffold only: four projects, `HotelsModuleMarker`, empty test projects, no endpoint/handler/aggregate/provider/persistence/migration. Multi-supplier search/dedup/ranking/booking is future concept only; require a Hotels spec before choosing suppliers or storage. |
| `modules/rail/AGENTS.md` | Scaffold only: four projects, `RailModuleMarker`, empty test projects, no endpoint/handler/provider/persistence/migration. Read-only multi-source schedules are future scope; Yandex.Rasp and DB/GTFS are candidates, not connected integrations. |
| `modules/trips/AGENTS.md` | Scaffold only: four projects, `TripsModuleMarker`, empty test projects, no `TripAggregate`, endpoint, handler, agent, provider, or persistence. Composite planning and AI itineraries are future scope requiring a separate spec. |
| `modules/identity/AGENTS.md` | Thin Foundation authentication: `AddIdentityModule()` configures JWT bearer from `Keycloak:Authority`, default audience `travel-web`, and disables HTTPS metadata only in Development. Realm fixture lives under `infra/keycloak`; Host currently owns fallback/Flights authorization policies. Core/Application/Api and test projects otherwise contain no real domain behavior. Do not copy development credentials. |

For each scaffold module, say that current Host/project references are scaffolding and WS3 debt, not permission for premature wiring.

- [ ] **Step 5: Create Shared and Travel.AI instructions**

`shared/AGENTS.md` must list exact ownership:

- `Travel.Shared.Abstractions`: `IDomainEvent`, `IModuleAssemblyMarker`, `EquatableArray<T>`, `[TestOnly]`; no package dependencies.
- `Travel.Shared.Domain`: `AggregateRoot<TId>` and `Entity<TId>`; do not claim Flights currently derives from them.
- `Travel.Shared.Infrastructure`: generic initialization primitives only; no real module initializer is currently registered, so do not promise automatic bootstrap/migrations.
- `Travel.Shared.Web`: ErrorOr/ProblemDetails, claims/request helpers, and `TestOnlyGuard`; no business logic.
- `Travel.Shared.TestInfrastructure`: PostgreSQL-only `IntegrationTestBase` plus `StubOptionsMonitor<T>`; it does not provide Redis, NATS, or Keycloak.
- Shared never imports modules and does not own domain rules; ErrorOr is not a global using.

`apps/Travel.AI/AGENTS.md` must state:

- Travel.AI is a separate Aspire-orchestrated ASP.NET process; its current product capability is Flights NL-search request/reply over Wolverine/NATS.
- The LLM path is direct Anthropic behind `IChatClient`; MAF and Semantic Kernel are not wired at runtime.
- `NlSearchExtractor` owns prompt/structured extraction and is reused by handler/evals.
- `NlSearchAiHandler` records cost via `AiDbContext` in schema `ai`, uses `TimeProvider`, and emits OTel activity/metrics.
- Cost-ledger migration source exists, but startup migration application/fresh-database readiness is not wired.
- Host and AI currently use different same-shape CLR message types; shape tests do not prove Wolverine identity or live transport. This is a confirmed WS2 gap—do not build new contracts by copying the types again.
- Link `tests/Travel.AI.Tests`, `tests/Travel.Tests.AiEvals`, and `tests/Travel.Tests.Contract/Flights`.
- Paid Anthropic evals require a key and separate authorization; model IDs/pricing stay in code/config, not instructions.

- [ ] **Step 6: Replace every Claude instruction file with the exact import**

Every root and nested `CLAUDE.md` must contain exactly:

```markdown
@AGENTS.md
```

No title, copied guidance, client notes, or trailing legacy content is needed in WS1.

- [ ] **Step 7: Verify facts, imports, and Codex context budget**

```powershell
$instructionRoots = @('', 'apps/Travel.AI', 'modules/flights', 'modules/hotels', 'modules/identity', 'modules/rail', 'modules/trips', 'shared')
foreach ($root in $instructionRoots) {
  $agents = if ($root) { Join-Path $root 'AGENTS.md' } else { 'AGENTS.md' }
  $claude = if ($root) { Join-Path $root 'CLAUDE.md' } else { 'CLAUDE.md' }
  if (-not (Test-Path -LiteralPath $agents)) { throw "Missing $agents" }
  if ((Get-Content -Raw -LiteralPath $claude).Trim() -ne '@AGENTS.md') { throw "Invalid Claude adapter: $claude" }
  if (((Get-Item -LiteralPath 'AGENTS.md').Length + (Get-Item -LiteralPath $agents).Length) -gt 32768 -and $agents -ne 'AGENTS.md') {
    throw "Codex instruction chain exceeds the default 32 KiB budget: $agents"
  }
}

rg -n "Result<T>|12 ADRs|\.Codex|modules/shared|localhost:15888|localhost:17002|shared PostgreSQL \+ Redis|uses Microsoft Agent Framework|MAF 1\.0 \+|MAF agents, Semantic Kernel" AGENTS.md apps/Travel.AI/AGENTS.md modules/flights/AGENTS.md modules/hotels/AGENTS.md modules/identity/AGENTS.md modules/rail/AGENTS.md modules/trips/AGENTS.md shared/AGENTS.md
```

Expected: all pairs pass; the stale-claim search returns no matches. Negative statements that MAF/Semantic Kernel are not currently wired remain valid.

- [ ] **Step 8: Commit only the instruction migration**

```powershell
git add AGENTS.md CLAUDE.md apps/Travel.AI/AGENTS.md apps/Travel.AI/CLAUDE.md modules/flights/AGENTS.md modules/flights/CLAUDE.md modules/hotels/AGENTS.md modules/hotels/CLAUDE.md modules/identity/AGENTS.md modules/identity/CLAUDE.md modules/rail/AGENTS.md modules/rail/CLAUDE.md modules/trips/AGENTS.md modules/trips/CLAUDE.md shared/AGENTS.md shared/CLAUDE.md
git diff --cached --check
git commit -m "docs: make AGENTS canonical across project scopes"
```

---

## Task 3: Create the eight canonical repository skills

**Files:**

- Create: `.agents/skills/spec/SKILL.md`
- Create: `.agents/skills/adr/SKILL.md`
- Create: `.agents/skills/explore-domain/SKILL.md`
- Create: `.agents/skills/test-this/SKILL.md`
- Create: `.agents/skills/integration-from-openapi/SKILL.md`
- Create: `.agents/skills/domain-modeling/SKILL.md`
- Create: `.agents/skills/migration-authoring/SKILL.md`
- Create: `.agents/skills/test-authoring/SKILL.md`

- [ ] **Step 1: Apply the repository skill format**

Use the active `skill-creator` guidance for naming, concise trigger descriptions, imperative workflow bodies, and forward-testing. Each folder contains only `SKILL.md`. Every file starts with only `name` and `description` in YAML frontmatter:

```markdown
---
name: spec
description: Design and write a Travel feature or subproject specification. Use when a request needs requirements clarification, alternatives, architecture decisions, scope boundaries, acceptance criteria, and approval before implementation.
---
```

Use these exact descriptions so Claude adapters can match them byte-for-byte:

| Skill | Description |
|---|---|
| `spec` | Design and write a Travel feature or subproject specification. Use when a request needs requirements clarification, alternatives, architecture decisions, scope boundaries, acceptance criteria, and approval before implementation. |
| `adr` | Write or revise a Travel Architecture Decision Record for a confirmed architectural decision. Use when a decision must be captured in docs/adr with repository evidence, consequences, and status. |
| `explore-domain` | Produce a read-only, evidence-backed current-state map of a Travel domain module. Use when asked what a module contains, implements, tests, or still lacks. |
| `test-this` | Select and orchestrate the right tests for an explicit or recently changed file. Use when asked to test the current change and the target or test layer may need discovery. |
| `integration-from-openapi` | Design or implement a provider anti-corruption layer from OpenAPI or endpoint documentation. Use for external supplier clients, wire DTOs, domain mapping, ErrorOr contracts, and provider constraints. |
| `domain-modeling` | Model Travel domain concepts using aggregates, value objects, state transitions, domain events, invariants, and ubiquitous language. Use for domain exploration or design before persistence and transport choices. |
| `migration-authoring` | Design, generate, and review safe EF Core source migrations for approved model changes. Use for schema changes, migration safety, generated SQL review, rollback, and deployment notes; never apply a database implicitly. |
| `test-authoring` | Write focused .NET or TypeScript tests using the repository's seven-layer strategy and nearest existing patterns. Use for unit, integration, HTTP, Aspire, architecture, snapshot, or E2E coverage. |

Immediately after frontmatter, every canonical body includes the exact repository-owned workflow marker `Canonical Travel workflow ID: travel-agency/<skill-name>.` with its real skill name. This is ordinary provenance text, not hidden authority: the validator enforces it, while the live gate binds the tracked physical file through `skills/list`, structured skill input, and an independent behavior smoke.

- [ ] **Step 2: Put the shared authority contract in every canonical skill**

Use this exact block near the end of all eight files:

```markdown
## Authority

Invoking or auto-loading this skill does not grant additional authority. Follow the user's requested scope. Do not infer permission to stage, commit, push, create or switch branches, generate or apply a migration, deploy, or mutate an external system. A design or review request does not authorize implementation-file edits.
```

Skill-specific rules may narrow this further but may not weaken it.

- [ ] **Step 3: Implement the five migrated command workflows**

| Skill | Required workflow and output contract |
|---|---|
| `spec` | Read root/relevant nested `AGENTS.md`, current code, concept/spec/ADR evidence; resolve only material ambiguity; offer 2-3 approaches with trade-offs; present/review the design; write `docs/superpowers/specs/YYYY-MM-DD-topic-design.md` only within approved scope; self-review for contradictions/placeholders; never auto-commit or begin implementation before design approval. |
| `adr` | Require a confirmed decision/topic; inspect current ADR style and compute the next number from actual `docs/adr`; capture context, decision, alternatives, consequences, status, and evidence; do not infer a topic from `HEAD~5`; never auto-commit. |
| `explore-domain` | Read the module `AGENTS.md` and actual Core/Application/Infrastructure/Api/tests; return a current-state card. Include the exact instruction `Separate verified facts, gaps, and uncertainty.`; remain read-only. |
| `test-this` | Prefer an explicit target; otherwise run `git diff --name-only --diff-filter=ACMR` and select only when unambiguous; ask if multiple targets remain; select the correct layer; follow `test-authoring`; run only targeted tests needed for the requested change; do not demand redundant approval when the user already requested test implementation. |
| `integration-from-openapi` | Establish module/provider and authoritative API input; distinguish design from implementation; keep provider wire DTOs in Infrastructure; expose domain-facing Core ports returning `ErrorOr<T>`; follow existing Client + capability Provider + mapper/options naming; include mapping/error/resilience/TOS notes; source edits require implementation scope. |

Use this portable target-discovery command in `test-this`:

```text
git diff --name-only --diff-filter=ACMR
```

- [ ] **Step 4: Implement the three role-oriented workflows**

| Skill | Required workflow and corrections |
|---|---|
| `domain-modeling` | Read module `AGENTS.md`, ADR 0001, and actual Core; produce aggregates, identities, transitions, invariants, value objects, past-tense events, ubiquitous language, and bounded-context notes; mark uncertainty; do not design storage/transport or write code unless asked. |
| `migration-authoring` | Read `docs/adr/0007-storage-strategy-marten-ef-coexistence.md`; inspect the actual DbContext, design-time factory, migration history, provider, and output path; plan safe add/backfill/constraint/rename behavior; generate source/snapshot/SQL only when requested; review Up/Down and rollout/rollback; never run `dotnet ef database update` implicitly. Use one-line commands rather than Bash continuations. |
| `test-authoring` | Read ADR 0006, nearest real tests, target code, and module `AGENTS.md`; select unit/integration/HTTP/Aspire/architecture/snapshot/E2E by behavior; use xUnit v3/Shouldly and existing fixtures; state that shared `IntegrationTestBase` supplies PostgreSQL only; keep production edits outside scope unless separately requested. |

- [ ] **Step 5: Validate the canonical skill set before adding adapters**

```powershell
$expectedSkills = @('adr', 'domain-modeling', 'explore-domain', 'integration-from-openapi', 'migration-authoring', 'spec', 'test-authoring', 'test-this')
$actualSkills = Get-ChildItem -LiteralPath .agents/skills -Directory | Sort-Object Name | Select-Object -ExpandProperty Name
if (Compare-Object $expectedSkills $actualSkills) { throw 'Canonical skill inventory differs from the approved eight skills.' }
foreach ($skill in $expectedSkills) {
  $path = ".agents/skills/$skill/SKILL.md"
  $content = Get-Content -Raw -LiteralPath $path
  if ($content -notmatch "(?m)^name: $([regex]::Escape($skill))$") { throw "Invalid skill name in $path" }
  if ($content -notmatch '(?m)^description: .+$') { throw "Missing skill description in $path" }
  if (-not $content.Contains("Canonical Travel workflow ID: travel-agency/$skill.")) { throw "Missing repository workflow marker in $path" }
  if ($content -notmatch '(?m)^## Authority$') { throw "Missing authority contract in $path" }
}

rg -n "Result<T>|0007-marten-ef-coexistence|shared PostgreSQL \+ Redis|head -1|head -n 1|git diff HEAD~5|git commit|git push" .agents/skills
```

Expected: inventory/frontmatter/authority checks pass; stale and implicit-Git command search returns no matches.

- [ ] **Step 6: Commit canonical skills only**

```powershell
git add .agents/skills
git diff --cached --check
git commit -m "feat: add canonical AI workflow skills"
```

---

## Task 4: Replace Claude workflows with thin compatibility adapters

**Files:**

- Create: `.claude/skills/spec/SKILL.md`
- Create: `.claude/skills/adr/SKILL.md`
- Create: `.claude/skills/explore-domain/SKILL.md`
- Create: `.claude/skills/test-this/SKILL.md`
- Create: `.claude/skills/integration-from-openapi/SKILL.md`
- Create: `.claude/skills/domain-modeling/SKILL.md`
- Create: `.claude/skills/migration-authoring/SKILL.md`
- Create: `.claude/skills/test-authoring/SKILL.md`
- Replace: `.claude/commands/spec.md`
- Replace: `.claude/commands/adr.md`
- Replace: `.claude/commands/explore-domain.md`
- Replace: `.claude/commands/test-this.md`
- Replace: `.claude/commands/integration-from-openapi.md`

- [ ] **Step 1: Create all eight Claude skill adapters**

For each approved skill, copy the canonical `name` and `description` frontmatter exactly. Its body must be exactly one canonical-read instruction. For `spec` the complete file is:

```markdown
---
name: spec
description: Design and write a Travel feature or subproject specification. Use when a request needs requirements clarification, alternatives, architecture decisions, scope boundaries, acceptance criteria, and approval before implementation.
---

Resolve the Git repository root, then read and follow `.agents/skills/spec/SKILL.md` from that root as the canonical workflow.
```

Use these exact body mappings for the other seven files:

| Claude adapter | Exact body |
|---|---|
| `.claude/skills/adr/SKILL.md` | `Resolve the Git repository root, then read and follow .agents/skills/adr/SKILL.md from that root as the canonical workflow.` with the path in backticks |
| `.claude/skills/explore-domain/SKILL.md` | `Resolve the Git repository root, then read and follow .agents/skills/explore-domain/SKILL.md from that root as the canonical workflow.` with the path in backticks |
| `.claude/skills/test-this/SKILL.md` | `Resolve the Git repository root, then read and follow .agents/skills/test-this/SKILL.md from that root as the canonical workflow.` with the path in backticks |
| `.claude/skills/integration-from-openapi/SKILL.md` | `Resolve the Git repository root, then read and follow .agents/skills/integration-from-openapi/SKILL.md from that root as the canonical workflow.` with the path in backticks |
| `.claude/skills/domain-modeling/SKILL.md` | `Resolve the Git repository root, then read and follow .agents/skills/domain-modeling/SKILL.md from that root as the canonical workflow.` with the path in backticks |
| `.claude/skills/migration-authoring/SKILL.md` | `Resolve the Git repository root, then read and follow .agents/skills/migration-authoring/SKILL.md from that root as the canonical workflow.` with the path in backticks |
| `.claude/skills/test-authoring/SKILL.md` | `Resolve the Git repository root, then read and follow .agents/skills/test-authoring/SKILL.md from that root as the canonical workflow.` with the path in backticks |

Do not use an `@` import, symlink, dynamic shell injection, duplicated steps, or extra authority text here; the canonical skill owns all of it. Current Claude Code appends invocation arguments automatically when `$ARGUMENTS` is absent, so the one-line skill adapter does not discard user input.

- [ ] **Step 2: Replace the five legacy commands with exact adapters**

Each command body is exactly two paragraphs. Example for `.claude/commands/spec.md`:

```markdown
Resolve the Git repository root, then read and follow `.agents/skills/spec/SKILL.md` from that root.

Arguments: $ARGUMENTS
```

Use the same command-to-skill names for `adr`, `explore-domain`, `test-this`, and `integration-from-openapi`. Do not add `.codex/commands`; Codex discovers the canonical skills directly.

- [ ] **Step 3: Verify parity and thinness**

```powershell
$expectedSkills = @('adr', 'domain-modeling', 'explore-domain', 'integration-from-openapi', 'migration-authoring', 'spec', 'test-authoring', 'test-this')
foreach ($skill in $expectedSkills) {
  $canonical = Get-Content -Raw ".agents/skills/$skill/SKILL.md"
  $adapter = Get-Content -Raw ".claude/skills/$skill/SKILL.md"
  $canonicalDescription = [regex]::Match($canonical, '(?m)^description: (.+)$').Groups[1].Value
  $adapterDescription = [regex]::Match($adapter, '(?m)^description: (.+)$').Groups[1].Value
  $adapterBody = ($adapter -split '(?m)^---\s*$', 3)[2].Trim()
  $expectedBody = 'Resolve the Git repository root, then read and follow `.agents/skills/' + $skill + '/SKILL.md` from that root as the canonical workflow.'
  if ($canonicalDescription -ne $adapterDescription) { throw "Description drift: $skill" }
  if ($adapterBody -ne $expectedBody) { throw "Claude adapter does not resolve the canonical skill from the Git root: $skill" }
  if ($adapter.Length -gt 750) { throw "Claude skill adapter is no longer thin: $skill" }
}

rg -n "^When this command|git commit|head -1|CLAUDE\.md|Result<T>|@\.\./" .claude/skills .claude/commands
```

Expected: descriptions match, every adapter stays under the size guard, and the legacy-content search returns no matches.

- [ ] **Step 4: Commit Claude workflow adapters**

```powershell
git add .claude/skills .claude/commands
git diff --cached --check
git commit -m "chore: add thin Claude workflow adapters"
```

---

## Task 5: Align custom agents and remove broken client hooks

**Files:**

- Replace/track: `.codex/agents/domain-modeler.toml`
- Replace/track: `.codex/agents/adr-writer.toml`
- Replace/track: `.codex/agents/test-author.toml`
- Replace/track: `.codex/agents/migration-author.toml`
- Replace/track: `.codex/agents/integration-mapper.toml`
- Replace: `.claude/agents/domain-modeler.md`
- Replace: `.claude/agents/adr-writer.md`
- Replace: `.claude/agents/test-author.md`
- Replace: `.claude/agents/migration-author.md`
- Replace: `.claude/agents/integration-mapper.md`
- Delete: `.claude/settings.json`
- Delete: `.codex/hooks.json`

- [ ] **Step 1: Apply the exact role/capability map**

| Agent | Canonical skill | Codex `sandbox_mode` | Claude `permissionMode` | Claude `tools` |
|---|---|---|---|---|
| `domain-modeler` | `domain-modeling` | `read-only` | `plan` | `Read, Grep, Glob` |
| `adr-writer` | `adr` | `workspace-write` | `default` | `Read, Grep, Glob, Write, Edit` |
| `test-author` | `test-authoring` | `workspace-write` | `default` | `Read, Grep, Glob, Write, Edit, Bash` |
| `migration-author` | `migration-authoring` | `workspace-write` | `default` | `Read, Grep, Glob, Write, Edit, Bash` |
| `integration-mapper` | `integration-from-openapi` | `workspace-write` | `default` | `Read, Grep, Glob, WebFetch, WebSearch, Write, Edit` |

Do not pin a model or reasoning effort; inherit the active client configuration. Capability metadata is not authority—the canonical skill and user scope remain controlling.

- [ ] **Step 2: Rewrite each Codex TOML as a thin manifest**

Each file contains only `name`, `description`, `sandbox_mode`, and `developer_instructions`. Descriptions must describe the role and later match the Claude manifest exactly. Example:

```toml
name = "domain-modeler"
description = "Models Travel aggregates, value objects, domain events, invariants, and ubiquitous language from current repository evidence."
sandbox_mode = "read-only"
developer_instructions = """
Repository role ID: `travel-agency/domain-modeler`.
For the exact delegated message `TRAVEL_AI_HARNESS_IDENTITY_PROBE`, reply with only the repository role ID; do not read files or use tools. For every other task, follow the canonical workflow below.
Resolve the Git repository root, then read and follow `.agents/skills/domain-modeling/SKILL.md` from that root.
"""
```

Use these exact descriptions and skill paths:

| Agent | Description | Canonical read line |
|---|---|---|
| `domain-modeler` | Models Travel aggregates, value objects, domain events, invariants, and ubiquitous language from current repository evidence. | `Resolve the Git repository root, then read and follow .agents/skills/domain-modeling/SKILL.md from that root.` with the path in backticks |
| `adr-writer` | Writes or revises one requested Travel Architecture Decision Record from a confirmed decision and current repository evidence. | `Resolve the Git repository root, then read and follow .agents/skills/adr/SKILL.md from that root.` with the path in backticks |
| `test-author` | Writes focused .NET or TypeScript tests using the repository's seven-layer strategy and nearest existing patterns. | `Resolve the Git repository root, then read and follow .agents/skills/test-authoring/SKILL.md from that root.` with the path in backticks |
| `migration-author` | Designs, generates, and reviews safe EF Core source migrations without implicitly applying a database change. | `Resolve the Git repository root, then read and follow .agents/skills/migration-authoring/SKILL.md from that root.` with the path in backticks |
| `integration-mapper` | Designs or implements a provider anti-corruption layer while keeping external DTOs behind the Infrastructure boundary. | `Resolve the Git repository root, then read and follow .agents/skills/integration-from-openapi/SKILL.md from that root.` with the path in backticks |

Put the exact line `Repository role ID: travel-agency/<agent-name>.` with the ID in backticks first in both client bodies for every role, followed by the canonical read line. For `domain-modeler` only, put the exact identity-probe line shown in the TOML example between them. The probe is inert during normal work and gives the live App Server check a repository-owned response that is absent from its delegated prompt. Do not add a model pin, extra authority, or any other probe behavior.

- [ ] **Step 3: Rewrite each Claude agent as the matching thin manifest**

Example:

```markdown
---
name: domain-modeler
description: Models Travel aggregates, value objects, domain events, invariants, and ubiquitous language from current repository evidence.
tools: Read, Grep, Glob
permissionMode: plan
---

Repository role ID: `travel-agency/domain-modeler`.
For the exact delegated message `TRAVEL_AI_HARNESS_IDENTITY_PROBE`, reply with only the repository role ID; do not read files or use tools. For every other task, follow the canonical workflow below.
Resolve the Git repository root, then read and follow `.agents/skills/domain-modeling/SKILL.md` from that root.
```

Apply the table from Step 1 and descriptions/read lines from Step 2 exactly. Do not preload `.claude/skills`; the body directly names the canonical file.

- [ ] **Step 4: Delete both copied hook configurations**

Delete `.claude/settings.json` because its only content is the broken hook set. Delete the audited untracked `.codex/hooks.json`. Do not replace them with client-specific scripts in WS1.

Formatting ownership after deletion remains:

```text
explicit verification -> Lefthook pre-commit/pre-push -> non-mutating CI checks
```

- [ ] **Step 5: Verify exact agent parity and hook absence**

```powershell
$agents = @('adr-writer', 'domain-modeler', 'integration-mapper', 'migration-author', 'test-author')
$agentSkills = @{
  'adr-writer' = 'adr'
  'domain-modeler' = 'domain-modeling'
  'integration-mapper' = 'integration-from-openapi'
  'migration-author' = 'migration-authoring'
  'test-author' = 'test-authoring'
}
foreach ($agent in $agents) {
  $codex = Get-Content -Raw ".codex/agents/$agent.toml"
  $claude = Get-Content -Raw ".claude/agents/$agent.md"
  $codexDescription = [regex]::Match($codex, '(?m)^description = "(.+)"$').Groups[1].Value
  $claudeDescription = [regex]::Match($claude, '(?m)^description: (.+)$').Groups[1].Value
  $expectedRead = 'Resolve the Git repository root, then read and follow `.agents/skills/' + $agentSkills[$agent] + '/SKILL.md` from that root.'
  $expectedLines = @('Repository role ID: `travel-agency/' + $agent + '`.')
  if ($agent -eq 'domain-modeler') {
    $expectedLines += 'For the exact delegated message `TRAVEL_AI_HARNESS_IDENTITY_PROBE`, reply with only the repository role ID; do not read files or use tools. For every other task, follow the canonical workflow below.'
  }
  $expectedLines += $expectedRead
  $expectedBody = ($expectedLines -join "`n")
  $codexBody = [regex]::Match($codex, '(?s)developer_instructions\s*=\s*"""\s*(.*?)\s*"""').Groups[1].Value.Replace("`r`n", "`n").Trim()
  $claudeBody = ($claude -split '(?m)^---\s*$', 3)[2].Replace("`r`n", "`n").Trim()
  if ($codexDescription -ne $claudeDescription) { throw "Agent description drift: $agent" }
  if ($codexBody -ne $expectedBody -or $claudeBody -ne $expectedBody) { throw "Agent body drift: $agent" }
  if ($codex.Length -gt 1200 -or $claude.Length -gt 1200) { throw "Agent adapter is no longer thin: $agent" }
}
if (Test-Path -LiteralPath .codex/hooks.json) { throw 'Codex hook copy still exists.' }
if (Test-Path -LiteralPath .claude/settings.json) { throw 'Claude hook-only settings still exist.' }

rg -n "CLAUDE_TOOL_INPUT_FILE_PATH|CLAUDE_RECENT_EDITS|Result<T>|0007-marten-ef-coexistence|shared PostgreSQL \+ Redis|head -1|git commit" .codex/agents .claude/agents
```

Expected: parity/size/absence checks pass and stale-content search returns no matches.

- [ ] **Step 6: Commit custom-agent migration and hook removal**

```powershell
git add .codex/agents .claude/agents .claude/settings.json
git diff --cached --check
git commit -m "chore: align AI agents and remove broken hooks"
```

Expected: `.codex/agents` becomes tracked; neither hook file exists. The Codex hook file began untracked, so its deletion has no index entry and must be verified by absence.

---

## Task 6: Publish truthful Codex-first contributor guidance

**Files:**

- Modify: `README.md`
- Modify: `CONTRIBUTING.md`
- Modify: `.devcontainer/devcontainer.json`

- [ ] **Step 1: Correct README's AI product and development claims**

Make these exact semantic changes:

- Replace the MAF runtime showcase bullet with: `Extracted AI service using Microsoft.Extensions.AI with Anthropic-backed structured NL search`.
- Replace the Claude-only harness bullet with: `Tracked Codex-first AI harness with canonical skills, custom agents, and thin Claude Code compatibility adapters`.
- In the stack table, describe current AI runtime as `Microsoft.Extensions.AI + Anthropic` and do not claim MAF 1.0 is active.
- Change the architecture/conventions link from `CLAUDE.md` to canonical `AGENTS.md`.
- Add a compact “AI development harness” subsection linking:
  - `AGENTS.md` and nested scoped instructions;
  - `.agents/skills/` canonical workflows;
  - `.codex/agents/` Codex role manifests;
  - `CLAUDE.md` / `.claude/` compatibility adapters;
  - dependency-free `npm run check:ai-harness` and authenticated local `npm run verify:ai-harness:codex`.
- State that a workflow invocation never implies Git publication, migration application, deployment, or external-mutation authority.

Do not fix unrelated README runtime examples/dashboard ports in this task; executable public documentation belongs to WS5.

- [ ] **Step 2: Replace the Claude-only CONTRIBUTING section**

The “AI-augmented development” section must state:

```markdown
Codex is the primary repository AI client. Start with `AGENTS.md`; the nearest nested `AGENTS.md` adds module/service context. Reusable workflows live in `.agents/skills/`, and project custom agents live in `.codex/agents/`.

Claude Code remains supported through exact `CLAUDE.md` imports and thin adapters under `.claude/`. Do not edit a compatibility adapter as an independent workflow; change the canonical skill first.

Run `npm run check:ai-harness` after changing any instruction, skill, command, agent manifest, or client configuration. Skill invocation does not grant staging, commit, push, migration, deployment, or external-system authority.

After changing Codex discovery behavior, run `npm run verify:ai-harness:codex` from the repository root. It requires Codex authentication, verifies committed artifacts through a clean clone, creates and deletes only its own temporary local Codex thread tree, and fails rather than masking a same-named personal agent.
```

- [ ] **Step 3: Make the devcontainer Codex-first without dropping Claude support**

In `.devcontainer/devcontainer.json`, add the official Codex VS Code extension identifier immediately before the retained Claude extension:

```json
"openai.chatgpt",
"anthropic.claude-code"
```

Do not add client CLIs, credentials, API keys, post-create login steps, or machine-specific settings.

- [ ] **Step 4: Verify public guidance and JSON formatting**

```powershell
rg -n "Codex|Claude|AGENTS\.md|\.agents/skills|check:ai-harness|verify:ai-harness:codex|Microsoft\.Extensions\.AI|MAF 1\.0" README.md CONTRIBUTING.md .devcontainer/devcontainer.json
npx.cmd biome check .devcontainer/devcontainer.json
```

Expected: Codex is primary, Claude is compatibility-only, current AI runtime wording is truthful, and the devcontainer JSON is valid. A remaining MAF roadmap mention is allowed only when clearly marked future work.

- [ ] **Step 5: Commit public guidance**

```powershell
git add README.md CONTRIBUTING.md .devcontainer/devcontainer.json
git diff --cached --check
git commit -m "docs: document the Codex-first AI harness"
```

---

## Task 7: Add a deterministic harness integrity gate

**Files:**

- Create: `tools/ai-harness/validate.mjs`
- Create: `tools/ai-harness/validate.test.mjs`
- Create: `tools/ai-harness/verify-codex.mjs`
- Create: `tools/ai-harness/verify-codex.test.mjs`
- Modify: `package.json`
- Modify: `.github/workflows/ci.yml`

- [ ] **Step 1: Write the failing validator tests first**

Use Node 22 built-ins only: `node:test`, `node:assert/strict`, `node:child_process`, `node:fs/promises`, `node:os`, `node:path`, `node:readline`, and `node:url`. Validator tests must import `validateHarness`, `HARNESS_CONTRACT`, and the CLI formatter from `validate.mjs`. Live-verifier tests import its pure personal-agent-collision, JSONL, skill-discovery, protocol, and App Server evidence helpers; they must never launch Git, Codex, a model request, or the network.

Start with these behaviors:

```javascript
import assert from "node:assert/strict";
import { test } from "node:test";
import {
  HARNESS_CONTRACT,
  formatValidationResult,
  validateHarness,
} from "./validate.mjs";

test("the current repository tree satisfies the complete harness contract", async () => {
  const issues = await validateHarness(new URL("../../", import.meta.url));
  assert.deepEqual(issues, []);
});

test("issues are deterministic and use slash-normalized paths", async (t) => {
  const fixture = await createValidFixture(t);
  await injectKnownFailures(fixture);
  const first = await validateHarness(fixture);
  const second = await validateHarness(fixture);
  assert.deepEqual(first, second);
  assert.ok(first.every((issue) => !issue.path.includes("\\")));
});
```

Add focused fixture tests for:

1. valid UTF-8 BOM/CRLF imports;
2. missing `AGENTS.md` and a duplicated/non-exact `CLAUDE.md`;
3. missing/extra skill and invalid frontmatter/name/description/body;
4. Claude skill description/reference drift and an oversized duplicated body;
5. missing/extra agent, exact inventory drift, name/description/mapping/role-ID drift, missing or duplicated `domain-modeler` identity probe, extra/model keys, Claude tools drift, additional body instructions, and wrong capability metadata;
6. `.codex/hooks.json`, Claude top-level `hooks`, and both obsolete Claude environment tokens;
7. a physical `.codex/commands/example.md`; in a separate fixture, a two-step case-only rename `.codex -> __codex_case_probe__ -> .Codex` followed by an assertion that `readdir` really reports `.Codex` (portable on case-insensitive Windows); plus `head -1`, `head -n 1`, and a POSIX line continuation before an option;
8. malformed JSON/TOML/frontmatter reported as structured issues rather than stack traces;
9. CLI success to stdout/exit 0 and failure to stderr/exit 1;
10. deterministic issue ordering regardless of fixture creation order.
11. verifier orchestration never treats `debug prompt-input` or a model self-report as skill provenance; initialization and exhaustive personal-agent collision inventory happen before any authenticated behavior smoke, and a collision aborts with zero `codex exec` calls;
12. App Server tests use headerless envelopes and validate exact result/error/notification shapes, initialize `codexHome`, root/nested `skills/list` entries, repository-owned discovery errors, repo scope, enabled state, exact descriptions, physical non-symlink paths, conflicts involving target names or canonical physical paths, and structured skill input using the returned path. An unrelated discovery error outside the clean clone does not invalidate exact repository evidence; an error under the clone, malformed error record, target-name conflict, or canonical-path conflict fails closed. Personal-agent collision detection uses only the returned `codexHome`, finds `name` independently of basename, and fails closed for every unparseable `.toml`. Collaboration evidence accepts only an exact-root-turn, completed `spawn_agent` `collabToolCall` whose child is linked to the root and contains the repository role ID; reject missing child IDs, root-only self-report, generic-agent output, child tool use, incomplete/failed turns, and child completion arriving before the root.

Create a complete minimal valid tree under `mkdtemp(join(tmpdir(), "travel-ai-harness-"))`; register recursive cleanup with `t.after`. The fixture builder must include all four harness files plus minimal `package.json` and `.github/workflows/ci.yml` so negative tests do not produce unrelated integration failures.

- [ ] **Step 2: Run the test and confirm RED**

```powershell
node --test tools/ai-harness/validate.test.mjs tools/ai-harness/verify-codex.test.mjs
```

Expected: failure because the validator/live-verifier modules or their exported contract/functions do not yet exist. Do not weaken the tests or make unit tests depend on installed Codex/authentication.

- [ ] **Step 3: Implement the embedded inventory contract**

Use this exact exported inventory; do not derive expected items from the filesystem:

```javascript
export const HARNESS_CONTRACT = Object.freeze({
  instructionRoots: [
    "",
    "apps/Travel.AI",
    "modules/flights",
    "modules/hotels",
    "modules/identity",
    "modules/rail",
    "modules/trips",
    "shared",
  ],
  skills: [
    "adr",
    "domain-modeling",
    "explore-domain",
    "integration-from-openapi",
    "migration-authoring",
    "spec",
    "test-authoring",
    "test-this",
  ],
  legacyCommands: [
    "adr",
    "explore-domain",
    "integration-from-openapi",
    "spec",
    "test-this",
  ],
  agents: Object.freeze({
    "adr-writer": Object.freeze({ skill: "adr", codexSandbox: "workspace-write", claudePermission: "default" }),
    "domain-modeler": Object.freeze({ skill: "domain-modeling", codexSandbox: "read-only", claudePermission: "plan" }),
    "integration-mapper": Object.freeze({ skill: "integration-from-openapi", codexSandbox: "workspace-write", claudePermission: "default" }),
    "migration-author": Object.freeze({ skill: "migration-authoring", codexSandbox: "workspace-write", claudePermission: "default" }),
    "test-author": Object.freeze({ skill: "test-authoring", codexSandbox: "workspace-write", claudePermission: "default" }),
  }),
  harnessFiles: [
    "validate.mjs",
    "validate.test.mjs",
    "verify-codex.mjs",
    "verify-codex.test.mjs",
  ],
});
```

- [ ] **Step 4: Implement normalization, parsers, and deterministic issues**

The public API is:

```javascript
export async function validateHarness(root) {
  // Return a sorted array of { code, path, line, message }.
}

export function formatValidationResult(issues) {
  // Return the stable success or failure text used by the CLI tests.
}
```

The CLI accepts an optional repository/fixture root and otherwise uses the current working directory:

```text
node tools/ai-harness/validate.mjs [root]
```

Detect direct execution with `fileURLToPath(import.meta.url)` versus `resolve(process.argv[1])`. On success, write the formatted result to stdout and leave `process.exitCode` at `0`; on contract failure, write to stderr and set `process.exitCode = 1`. Imported use must never execute the CLI branch.

Implementation requirements:

- Accept filesystem path strings and file URLs.
- Normalize BOM, CRLF, and lone CR before comparing text.
- Normalize every reported relative path to `/`.
- Sort by `code`, then `path`, then numeric `line` (missing last), then `message`.
- Parse only the deliberately small YAML/TOML shapes used by this harness; no dependency is needed.
- Convert malformed config and unexpected read errors to issues, not unhandled stack traces.
- Compare directory entry names from `readdir({ withFileTypes: true })` to enforce case-sensitive inventory even on Windows.
- Do not call Git; the check must work before staging and in a fresh checkout.

Issue object helper:

```javascript
function issue(code, path, message, line) {
  return {
    code,
    path: path.replaceAll("\\", "/"),
    ...(line === undefined ? {} : { line }),
    message,
  };
}
```

- [ ] **Step 5: Implement all repository checks**

The validator must enforce:

| Area | Exact rule |
|---|---|
| Instructions | Each root has non-empty `AGENTS.md`; normalized `CLAUDE.md` equals exactly `@AGENTS.md`; root+nested chain is at most 32 KiB; files are not symlinks. |
| Canonical skills | Exact eight directories; each has one `SKILL.md`; frontmatter `name` matches folder; description is non-empty/single-line; body, exact `Canonical Travel workflow ID: travel-agency/<skill-name>.` marker, and exact Authority block exist. |
| Claude skills | Exact same eight; name and description match canonical; normalized body resolves the Git repository root and reads `.agents/skills/name/SKILL.md` from that root as canonical. |
| Legacy commands | Exact five `.md` files; normalized body equals the two-paragraph canonical read + `Arguments: $ARGUMENTS` contract. |
| Agents | Exact five files per client; declared name matches basename; descriptions, mapped canonical read line, and exact `travel-agency/<agent-name>` role ID match; only `domain-modeler` has the exact inert identity-probe line; Codex sandbox and Claude permission match the embedded contract; adapter size stays at or below 1,200 bytes. |
| Hooks | `.codex/hooks.json` absent; `.claude/settings.json`, if reintroduced for a non-hook setting later, may not contain top-level `hooks`. |
| Unsupported paths | The client directory is exactly lowercase `.codex`; reject any top-level entry equal to `.codex` case-insensitively but spelled differently. Inside exact `.codex`, reject any file or directory equal to `commands` case-insensitively. Enforce both with `readdir`, not only a text scan. |
| Active stale text | No obsolete Claude env tokens, `.Codex`, `.codex/commands`, stale Result/ADR/test-infra/CSharpier claims, auto-commit commands, or false active MAF runtime claim. |
| Windows portability | Canonical skills contain no `head` pipeline or POSIX continuation before an option. |
| Harness tooling | `tools/ai-harness` contains exactly the four embedded `harnessFiles`; offline tests and deterministic validation are separate from the authenticated live verifier. |
| npm/CI | Offline test, check, and live-verifier package scripts exist; the named CI step runs only the dependency-free offline check outside Nx affected logic. |

Scan stale text only in expected instruction pairs, canonical/Claude skills, commands, both agent sets, existing hook/settings files, `README.md`, `CONTRIBUTING.md`, and `.devcontainer/devcontainer.json`. Exclude `docs/superpowers`, the validator, and validator tests because they preserve/encode historical bad strings intentionally.

Use precise token/line checks rather than broad technology bans. The active-surface stale set is:

```text
CLAUDE_TOOL_INPUT_FILE_PATH
CLAUDE_RECENT_EDITS
.Codex/
.Codex\
.codex/commands/
.codex\commands\
static Result<T> Create
0007-marten-ef-coexistence.md
shared PostgreSQL + Redis
git diff HEAD~5
git commit -m
```

Also reject a full command line equal to `dotnet csharpier .` and the two exact false README claims `Extracted AI service using Microsoft Agent Framework (MAF) 1.0` and `| AI | MAF 1.0 + Claude`. Do not reject truthful negative or roadmap references to MAF/Semantic Kernel.

Success text must be exactly:

```text
AI harness validation passed: 8 instruction pairs, 8 skills, 5 agent pairs, 5 legacy commands.
```

Failure format:

```text
AI harness validation failed (2 issues):
- [agents/missing] .codex/agents: missing agent "test-author"
- [hooks/forbidden-file] .codex/hooks.json: copied hook adapter must be removed; formatting is owned by Lefthook and CI
```

- [ ] **Step 6: Implement the dependency-free live Codex verifier and its offline tests**

`verify-codex.mjs` is an authenticated local acceptance client, not a CI check. Export pure helpers for testability and a CLI entry point that defaults to the current repository; use the same `fileURLToPath(import.meta.url)` direct-execution guard as the validator so importing it never starts a process. Keep subprocess creation behind injected functions so `verify-codex.test.mjs` can exercise every parser/state transition without launching Codex.

The CLI must perform this bounded sequence:

1. Resolve `git` and `codex`/`codex.cmd` from `PATH`; print the Codex version, but no configuration, prompt dump, auth material, or transcript contents.
2. Create a GUID-named directory directly under `os.tmpdir()`, clone the current repository with `git clone --no-hardlinks`, and run the cloned `validate.mjs`. The clone is the tracked-state boundary: uncommitted adapters cannot participate.
3. Start `codex app-server` over stdio and speak the documented newline-delimited JSON-RPC wire format with the `jsonrpc` header omitted. Send `initialize` with `capabilities.experimentalApi = true`, validate its required result fields, capture the exact returned `codexHome`, then send `initialized` without `params`. Accept only the documented notification envelope plus an optional non-negative integer `emittedAtMs`; reject schema drift. Resolve personal agents only from that returned `codexHome` and inspect every `agents/*.toml` by parsed `name`, independent of filename. If any declares `domain-modeler`, fail with `personal-agent collision`; if any personal TOML cannot be parsed confidently, fail closed with `personal-agent inventory not provable`. Never move, edit, disable, print, or otherwise mutate personal agent contents. Do not call `codex debug prompt-input`: it receives plain text without typed skill selection and is not a valid content-provenance attestation.
4. Call typed `skills/list` with `cwds` equal to clone root and nested Flights plus `forceReload: true`. Require exactly one result entry per requested cwd, a schema-valid `errors` array, and exactly one enabled repo-scoped record for each selected skill with exact canonical name, frontmatter description, and absolute path. Fail on every discovery error whose absolute path is under the clean clone; unrelated errors from external user/system skill roots are outside WS1 acceptance and do not invalidate an exact target record. The selected path must equal the expected physical non-symlink `SKILL.md` under the clone after platform normalization/realpath. Reject missing/extra cwd entries, conflicts involving a target name or canonical path, disabled targets, metadata drift, alternate target sources, or schema drift. Pass the server-returned path verbatim in a structured `{ type: "skill", name, path }` turn input together with the literal `$skill` text.
5. Run the two literal-only skill behavior smokes with `codex exec --ephemeral --ignore-user-config --sandbox read-only --json`; parse JSONL structurally and capture final output separately. `--ignore-user-config` skips only user `config.toml`; clean-clone static validation plus typed repo discovery/path binding establish project provenance, while these calls verify actual `$skill` behavior.
6. Create one root thread with clone `cwd`, `approvalPolicy: "never"`, and the CLI-compatible legacy enum `sandbox: "read-only"`; assert its `instructionSources` include clone-root `AGENTS.md`. Complete one read-only structured skill turn using the exact `skills/list` path and require its exact turn to complete successfully. Keep this legacy field distinct from camel-cased `sandboxPolicy.type` values used by newer turn/command APIs.
7. Start one turn whose prompt requires an actual `domain-modeler` spawn and delegates exactly `TRAVEL_AI_HARNESS_IDENTITY_PROBE`; the prompt does not contain the expected role ID. Capture the exact turn ID and accept only a matching `turn/completed` notification with status `completed`.
8. From authoritative `item/completed` notifications correlated to the exact root thread and spawn turn, require exactly one completed `collabToolCall` with `tool === "spawn_agent"`: `senderThreadId` equals the created root, `newThreadId` is non-empty, and its delegated `prompt` equals the probe token. Do not use regex over reasoning/final text as spawn evidence.
9. Call `thread/read` for that exact child with `includeTurns: true`. Require a child `agentMessage` whose complete text is exactly `travel-agency/domain-modeler`; reject the role ID if it appears only in the root response, and reject any child `commandExecution`, `fileChange`, `mcpToolCall`, or other tool-use item. Then call experimental `thread/list` with `parentThreadId` and all `subAgent*` source kinds and require the same child ID.
10. Assert the clone remains clean. In `finally`, call `thread/delete` only for the exact root thread ID created by this verifier, close App Server stdin and accept an authoritative bounded wrapper-process close before force-terminating its owned process tree, then remove only the validated GUID directory beneath `os.tmpdir()`. Never enumerate or delete unrelated Codex threads or processes. A cleanup failure is reported separately and fails the run.

Use these exact behavior prompts, with the `$skill` token passed literally and no canonical filesystem path or success marker:

```text
Use $explore-domain to map the current Flights module from repository evidence. Read only; do not edit.
Use $migration-authoring to review a hypothetical required-column change. Do not edit files, generate a migration, or touch a database.
```

The first run starts in the clone's `modules/flights`; the second starts at clone root. The migration smoke must contain `docs/adr/0007-storage-strategy-marten-ef-coexistence.md`; both final responses are printed for the Task 8 semantic review.

The verifier must fail closed for missing CLI/auth, App Server schema drift, duplicate skill sources, personal-agent collision, missing/failed collaboration items, unavailable child evidence, unexpected writes, timeout, or cleanup failure. This composite check proves a real child spawn and gives practical project-agent provenance through tracked TOML + no personal name collision + a repository-only instruction observed in the child. State explicitly in output that the current public protocol does not return the source TOML path directly.

Offline tests use literal fixtures mirroring headerless App Server envelopes, `skills/list` response, initialize result, structured skill input, and collaboration streams. They cover success plus every failure above, including an ignored unrelated external discovery error, a rejected clone-local discovery error, conflicting target catalog sources, a pre-smoke personal collision, a canary copied only into the root response, malformed protocol envelopes, and graceful wrapper closure before force termination. They also prove the cleanup guard refuses the temp base itself, a sibling path, an empty path, and a non-GUID directory. No offline test may read the real home agent directory.

- [ ] **Step 7: Add package scripts and CI wiring**

Add these exact scripts to `package.json`:

```json
"test:ai-harness": "node --test tools/ai-harness/validate.test.mjs tools/ai-harness/verify-codex.test.mjs",
"check:ai-harness": "npm run test:ai-harness && node tools/ai-harness/validate.mjs",
"verify:ai-harness:codex": "node tools/ai-harness/verify-codex.mjs"
```

Add this named step to the existing `lint` job immediately after `actions/setup-node` and before .NET setup/`npm ci`:

```yaml
- name: Validate AI harness
  run: npm run check:ai-harness
```

The check has no dependencies and must not be hidden behind `nx affected`; root-only harness changes otherwise disappear from the graph. Do not change current `master` workflow triggers in WS1—branch-policy repair belongs to WS2. No `package-lock.json` update is needed for script-only changes.

- [ ] **Step 8: Run the focused offline gate and confirm GREEN**

```powershell
npm.cmd run check:ai-harness
npx.cmd biome check tools/ai-harness/validate.mjs tools/ai-harness/validate.test.mjs tools/ai-harness/verify-codex.mjs tools/ai-harness/verify-codex.test.mjs package.json .devcontainer/devcontainer.json
```

Expected: all validator tests pass; CLI prints the exact success line; Biome reports no errors.

- [ ] **Step 9: Commit the integrity gate and live verifier**

```powershell
git add tools/ai-harness/validate.mjs tools/ai-harness/validate.test.mjs tools/ai-harness/verify-codex.mjs tools/ai-harness/verify-codex.test.mjs package.json .github/workflows/ci.yml
git diff --cached --check
git commit -m "test: enforce AI harness integrity"
```

---

## Task 8: Forward-test real Codex discovery, scope, and final repository state

**Files:**

- No planned edits
- If a forward test exposes a real defect, fix only the owning canonical skill/adapter/validator, rerun Task 7, and commit the focused correction before continuing

- [ ] **Step 1: Prove repository skill and custom-agent discovery in a clean clone**

After every WS1 commit exists, create an outer bootstrap clone and invoke the package script from that clone. This prevents an unstaged edit to `package.json` or `verify-codex.mjs` in the working tree from orchestrating or passing acceptance. The committed verifier then creates its own inner clean clone for the artifacts it probes:

```powershell
$previousErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = 'Stop'
try {
  $gitCommands = @(Get-Command git -CommandType Application -ErrorAction Stop)
  $npmCommands = @(Get-Command npm.cmd -CommandType Application -ErrorAction Stop)
  $gitCommandPath = $gitCommands[0].Path
  $npmCommandPath = $npmCommands[0].Path
  $separatorChars = [char[]]@('\', '/')
  $tempBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd($separatorChars)
  $bootstrapName = 'travel-ai-harness-bootstrap-' + [Guid]::NewGuid().ToString('N')
  $bootstrapRoot = [System.IO.Path]::GetFullPath((Join-Path $tempBase $bootstrapName))
  $enteredBootstrap = $false

  $sourceHeadOutput = & $gitCommandPath rev-parse HEAD
  $sourceHeadExit = $LASTEXITCODE
  $sourceHead = ($sourceHeadOutput -join "`n").Trim()
  if ($sourceHeadExit -ne 0 -or $sourceHead -notmatch '^[0-9a-f]{40}$') {
    throw 'Cannot resolve the committed source HEAD.'
  }

  try {
    & $gitCommandPath clone --no-hardlinks . $bootstrapRoot
    if ($LASTEXITCODE -ne 0) {
      throw 'Could not create the committed verifier bootstrap clone.'
    }

    $cloneHeadOutput = & $gitCommandPath -C $bootstrapRoot rev-parse HEAD
    $cloneHeadExit = $LASTEXITCODE
    $cloneHead = ($cloneHeadOutput -join "`n").Trim()
    if ($cloneHeadExit -ne 0 -or $cloneHead -ne $sourceHead) {
      throw "Bootstrap clone HEAD mismatch: expected $sourceHead, got $cloneHead."
    }

    Push-Location -LiteralPath $bootstrapRoot -ErrorAction Stop
    $enteredBootstrap = $true
    & $npmCommandPath run verify:ai-harness:codex
    $verifyExit = $LASTEXITCODE
    if ($verifyExit -ne 0) {
      throw "Committed Codex verifier failed with exit code $verifyExit."
    }

    $bootstrapStatus = & $gitCommandPath status --porcelain=v1 --untracked-files=all
    if ($LASTEXITCODE -ne 0 -or $bootstrapStatus) {
      throw 'The committed verifier modified its outer bootstrap clone.'
    }
  }
  finally {
    if ($enteredBootstrap) {
      Pop-Location -ErrorAction Stop
    }

    $cleanupParent = [System.IO.Path]::GetFullPath((Split-Path -Parent $bootstrapRoot)).TrimEnd($separatorChars)
    $cleanupLeaf = Split-Path -Leaf $bootstrapRoot
    $isExactTempChild = [string]::Equals(
      $cleanupParent,
      $tempBase,
      [System.StringComparison]::OrdinalIgnoreCase
    )
    if (-not $isExactTempChild -or $cleanupLeaf -notmatch '^travel-ai-harness-bootstrap-[0-9a-f]{32}$') {
      throw "Refusing unsafe bootstrap cleanup target: $bootstrapRoot"
    }

    $cleanupError = $null
    for ($cleanupAttempt = 1; $cleanupAttempt -le 3; $cleanupAttempt++) {
      if (-not (Test-Path -LiteralPath $bootstrapRoot -ErrorAction Stop)) {
        break
      }

      try {
        $cleanupItem = Get-Item -LiteralPath $bootstrapRoot -Force -ErrorAction Stop
        $isReparsePoint = [bool]($cleanupItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint)
        if (-not $cleanupItem.PSIsContainer -or $isReparsePoint) {
          throw "Refusing non-directory or reparse-point cleanup target: $bootstrapRoot"
        }
        Remove-Item -LiteralPath $bootstrapRoot -Recurse -Force -ErrorAction Stop
        $cleanupError = $null
      }
      catch {
        $cleanupError = $_.Exception
        if ($cleanupAttempt -lt 3) {
          Start-Sleep -Milliseconds (250 * $cleanupAttempt)
        }
      }
    }

    if (Test-Path -LiteralPath $bootstrapRoot -ErrorAction Stop) {
      $cleanupDetail = if ($null -ne $cleanupError) { $cleanupError.Message } else { 'unknown error' }
      throw "Bootstrap cleanup failed after 3 attempts: $cleanupDetail"
    }
  }
}
finally {
  $ErrorActionPreference = $previousErrorActionPreference
}
```

Inspect the verifier's concise evidence summary and the two printed skill behavior responses before accepting the step:

- clean-clone validation passes before authenticated checks, and no personal-agent collision exists in the exact initialized `codexHome`;
- `skills/list` returns exact root/nested cwd entries and enabled repo-scoped canonical metadata, and the structured skill turn uses the returned path verbatim;
- the Flights output is evidence-backed and does not call the implemented backend a scaffold;
- the migration output cites `docs/adr/0007-storage-strategy-marten-ef-coexistence.md`, proposes safe sequencing, and performs no source or database write;
- the App Server summary records a completed `collabToolCall`, the linked child ID, and the repository role ID found in that child only;
- the outer bootstrap ran the package script from the exact committed source HEAD and stayed clean; the inner cloned validator passed, its clean clone stayed unchanged, and the verifier-owned root/child threads plus both temporary directories were cleaned up.

If the CLI, authentication, App Server/`skills/list` surface, unambiguous repository provenance, explicit `$skill` discovery, named project-agent spawn, child-thread evidence, or safe cleanup is unavailable, stop and report it as a WS1 acceptance blocker. Do not silently fall back to `debug prompt-input` body guesses, a final-answer marker, raw JSONL regex, a generic subagent given a skill path, or temporary mutation of personal agent files.

- [ ] **Step 2: Verify both client inventories statically**

```powershell
npm.cmd run check:ai-harness

$instructionRoots = @('', 'apps/Travel.AI', 'modules/flights', 'modules/hotels', 'modules/identity', 'modules/rail', 'modules/trips', 'shared')
$skills = @('adr', 'domain-modeling', 'explore-domain', 'integration-from-openapi', 'migration-authoring', 'spec', 'test-authoring', 'test-this')
$commands = @('adr', 'explore-domain', 'integration-from-openapi', 'spec', 'test-this')
$agents = @('adr-writer', 'domain-modeler', 'integration-mapper', 'migration-author', 'test-author')
$expectedTracked = @(
  foreach ($root in $instructionRoots) {
    $prefix = if ($root) { "$root/" } else { '' }
    "${prefix}AGENTS.md"
    "${prefix}CLAUDE.md"
  }
  foreach ($skill in $skills) {
    ".agents/skills/$skill/SKILL.md"
    ".claude/skills/$skill/SKILL.md"
  }
  foreach ($command in $commands) { ".claude/commands/$command.md" }
  foreach ($agent in $agents) {
    ".codex/agents/$agent.toml"
    ".claude/agents/$agent.md"
  }
  'tools/ai-harness/validate.mjs'
  'tools/ai-harness/validate.test.mjs'
  'tools/ai-harness/verify-codex.mjs'
  'tools/ai-harness/verify-codex.test.mjs'
  'docs/superpowers/plans/2026-08-11-codex-first-ai-harness.md'
) | Sort-Object -Unique

$actualTracked = @(git ls-files -- $expectedTracked) |
  ForEach-Object { $_.Replace('\', '/') } |
  Sort-Object -Unique
$trackedDifference = @(Compare-Object -ReferenceObject $expectedTracked -DifferenceObject $actualTracked)
if ($trackedDifference.Count -ne 0) {
  $trackedDifference | Format-Table -AutoSize
  throw 'Required AI-harness artifacts are not all tracked.'
}
```

Expected: the exact assertion passes, the package script and live verifier ran from the outer bootstrap clone at the exact committed source HEAD, and that verifier independently ran the full validator inside its own clean clone. Together these prove both the acceptance orchestrator and the canonical/compatibility artifacts exist in committed state rather than only in the worktree. Claude compatibility is static-only because no Claude account is required or available for WS1.

- [ ] **Step 3: Run final focused, non-mutating checks**

```powershell
npm.cmd run check:ai-harness
npx.cmd biome check tools/ai-harness/validate.mjs tools/ai-harness/validate.test.mjs tools/ai-harness/verify-codex.mjs tools/ai-harness/verify-codex.test.mjs package.json .devcontainer/devcontainer.json
git diff --check origin/dev...HEAD
```

Expected: harness and targeted Biome checks pass and no whitespace errors exist. Do not make `npx.cmd biome ci .` a WS1 pass gate: the 2026-08-11 planning baseline reports 25 pre-existing errors in runtime/frontend/email-template files outside this workstream. Record that full-repository baseline as residual debt for WS2 rather than expanding WS1.

- [ ] **Step 4: Enforce an exact WS1 changed-path allowlist**

```powershell
$allowedWs1Paths = @(
  '^AGENTS\.md$',
  '^CLAUDE\.md$',
  '^apps/Travel\.AI/(AGENTS|CLAUDE)\.md$',
  '^modules/(flights|hotels|identity|rail|trips)/(AGENTS|CLAUDE)\.md$',
  '^shared/(AGENTS|CLAUDE)\.md$',
  '^\.agents/skills/(adr|domain-modeling|explore-domain|integration-from-openapi|migration-authoring|spec|test-authoring|test-this)/SKILL\.md$',
  '^\.claude/skills/(adr|domain-modeling|explore-domain|integration-from-openapi|migration-authoring|spec|test-authoring|test-this)/SKILL\.md$',
  '^\.claude/commands/(adr|explore-domain|integration-from-openapi|spec|test-this)\.md$',
  '^\.codex/agents/(adr-writer|domain-modeler|integration-mapper|migration-author|test-author)\.toml$',
  '^\.claude/agents/(adr-writer|domain-modeler|integration-mapper|migration-author|test-author)\.md$',
  '^\.claude/settings\.json$',
  '^tools/ai-harness/(validate|validate\.test|verify-codex|verify-codex\.test)\.mjs$',
  '^package\.json$',
  '^\.github/workflows/ci\.yml$',
  '^README\.md$',
  '^CONTRIBUTING\.md$',
  '^\.devcontainer/devcontainer\.json$',
  '^docs/superpowers/specs/2026-08-11-ai-harness-architecture-remediation-design\.md$',
  '^docs/superpowers/plans/2026-08-11-codex-first-ai-harness\.md$'
)

$unexpected = foreach ($changedPath in git diff --name-only origin/dev...HEAD) {
  $normalizedPath = $changedPath.Replace('\', '/')
  if (-not ($allowedWs1Paths | Where-Object { $normalizedPath -match $_ })) { $normalizedPath }
}
if ($unexpected) {
  $unexpected
  throw 'WS1 changed paths outside the exact approved allowlist.'
}
```

Expected: no unexpected path. This allowlist catches runtime TypeScript, JSON/YAML, Dockerfiles, schemas, project files, and infrastructure changes that an extension-only blocklist would miss.

- [ ] **Step 5: Review the final diff and report residual work honestly**

```powershell
git status --short --branch
git log --oneline origin/dev..HEAD
git diff --stat origin/dev...HEAD
git diff --name-status origin/dev...HEAD
```

Report separately:

- verified WS1 outcomes;
- Claude compatibility validation boundary (static, not live-account tested);
- unchanged pre-existing/runtime risks delegated to WS2-WS5;
- exact next gate: user review before any WS2/WS3 or runtime implementation begins.

Do not push, open a PR, merge, deploy, or begin another workstream without separate user authorization.

---

## Plan Self-Review Checklist

- [ ] Every WS1 bullet from the accepted design spec maps to a task and verification step.
- [ ] No task changes runtime source, module project references, persistence, schema, transport, or CI branch filters.
- [ ] All eight instruction scopes, eight skills, five legacy commands, five agent pairs, and four harness tool files are enumerated explicitly.
- [ ] Claude imports use `@AGENTS.md` only; no skill/agent adapter assumes `@` expansion.
- [ ] Current project facts distinguish implemented Flights, thin Identity/AI, and scaffold modules.
- [ ] Known WS2/WS3 gaps are documented as gaps rather than silently “fixed” in instructions.
- [ ] Authority language does not auto-grant Git, migration, deploy, or external mutation actions.
- [ ] Commands are Windows/POSIX portable and use current CSharpier v1 syntax.
- [ ] Offline tests cover validator positive/negative/path-case/line-ending/parity/CLI behavior plus typed skill discovery/input, personal-agent collisions, App Server collaboration evidence, and cleanup safety.
- [ ] Historical audit documents are excluded from active stale-token scans.
- [ ] Each implementation commit is focused and stages explicit paths only.
- [ ] A committed outer-bootstrap verifier plus inner clean-clone validation, typed repo discovery/path binding, structured skill input, and read-only behavior smokes prove repository `$skill` discovery and execution; structured App Server evidence proves a linked custom-agent child and repository-only role response without relying on model self-markers or JSONL regex.
- [ ] Final Biome verification is scoped to WS1-owned files and reports the known full-repository baseline separately.
- [ ] Final verification uses an exact changed-path allowlist rather than an extension-only runtime blocklist.
