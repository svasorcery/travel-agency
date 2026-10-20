# Travel Platform

## Current shape and scope

`Travel.AppHost` orchestrates resources and services. `Travel.Host` is the modular monolith; `Travel.AI` is a separate process. The Angular application includes Flights B1–B5 search/booking/order UI and M2.1 explainable ranking plus M2.3b booking for 1–9 adults and M2.4 private saved travelers, and M2.5 one complete offer/order for 1–4 flight legs including open-jaw with airport-local IANA timestamps; booking proof is an isolated fictional demo.

Foundation and the Flights M1 backend are implemented; M2.2 adds protection for new passenger snapshots and webhook inbox bodies; M2.3b adds quote-bound encrypted passenger parties and explicit count metadata. Identity is thin JWT/Keycloak integration; Hotels M1/M2 implement default-off Test-only Duffel Stays and Hotelbeds, bounded multisupplier search, reviewed property matching, explainable supplier-cost ranking and the Angular one-room/1–2-adult purchase/private-result flow. Source/local and PR/post-merge CI acceptance are recorded in `docs/architecture/hotels-completion-review.md`; current contracts are in `openspec/specs`, completed M1/M2 evidence is archived. M1 memory16/17 remains failed (workingSetRecovery=false), with no M2 waiver; real suppliers/load/target rollout remain separate. Rail M1 has a default-off read-only Yandex.Rasp source slice with bounded temporary station/search data and independent ticket facts; its actual Host, measured, real-source/banner and target acceptance remain separate in the active rail-m1-yandex-search change. Trips M1 has a default-off private structured planning source slice with protected EF snapshots and revision-checked editing; its current task/local verification state is in openspec/changes/trips-m1-structured-planning. Automatic itineraries and free-text extraction remain later stages. Shared contains primitives and helpers. Travel.AI currently provides Flights natural-language search, a default-off fare-summary candidate, a cost ledger, and observability.

Travel.AI uses a direct Anthropic integration behind `Microsoft.Extensions.AI.IChatClient`. Microsoft Agent Framework and Semantic Kernel are not wired at runtime.

Support S1 has a source implementation for private manual customer/operator conversations, protected text, relational action receipts and90day resolved retention. Its CI and target provisioning acceptance are tracked in `openspec/changes/archive/2026-10-13-support-s1-case-workflow/verification.md`; Laya classification is a separate later stage.

The accepted S1 capability contract is `openspec/specs/support-case-workflow/spec.md`; the completed25tasks, design, reviews and implementation/CI receipts are retained in `openspec/changes/archive/2026-10-13-support-s1-case-workflow`. Sync/archive records source acceptance and does not authorize target keys/schema/operator grants, model execution or rollout.

Support S2 on-demand operator classification is merged in PR49 with source and ordinary CI acceptance. Current contract: `openspec/specs/support-classification-suggestions/spec.md`; all23completed tasks, design, deep reviews and exact-head/PR E2E/postmerge receipts: `openspec/changes/archive/2026-10-13-support-s2-laya-classification`. Laya remains default-off; category/priority persistence is an explicit operator action, and real-model/target/private-text enablement and S3 automatic routing remain separate.

PR51 adds the explicitly accepted experimental label and review warning while retaining Laya, eight categories, separate manual confirmation and the 10-second request deadline. All five source tasks and successful PR/postmerge CI receipts are retained in `openspec/changes/archive/2026-10-15-support-classification-experimental-mode`. A separately authorized disposable pilot used real Support API/PostgreSQL/Laya with test identities and fictional cases: two model requests, one Refund candidate and one LowConfidence/manual outcome; the oversized case made no model request. Its `runtime-pilot.md` records component integration, not improved quality, full Host/Keycloak acceptance or real-case enablement. The earlier failed quality evaluation remains failed; classification stays default-off for real Support traffic.

## Architecture rules

- Intended module layers are `Core -> Application -> Infrastructure -> Api`. Modules must not import another module's internals; use shared abstractions or published events.
- Keep external wire DTOs, provider clients, and their mapping inside Infrastructure. Keep domain rules out of Infrastructure.
- Use explicit `ErrorOr<T>` imports for expected outcomes. Value-object factories return `ErrorOr<T>`; events use past tense and implement `IDomainEvent`.
- Inject `TimeProvider` in production code. Do not infer a rule that exceptions can occur only in Core.
- Name provider implementations by provider and capability, for example `DuffelFlightSearchProvider` and `TravelpayoutsSearchProvider`; do not impose a universal `{Provider}Adapter` suffix.
- Host imports only implemented Flights, Identity, Support, Hotels, Rail and Trips Api.Composition facades. Host owns process-wide Marten/Wolverine registrations, transport policy, security middleware order, and the single endpoint mapping; module-specific contributions stay in their Api facades. Hotels/Flights share one raw Duffel webhook route with isolated module receivers/inboxes.

## Working commands

```text
dotnet run --project apps/Travel.AppHost
npx nx serve web
dotnet test Travel.slnx --maxcpucount:1 --filter "Category!=AiEval&Category!=AiEvals"
dotnet tool restore
dotnet csharpier format .
dotnet csharpier check .
npx biome format --write .
npx biome ci .
npm run check:ai-harness
npm run verify:ai-harness:codex
```

On Windows PowerShell, use `npm.cmd` / `npx.cmd` for the npm commands when execution policy blocks the `.ps1` shims. Keep the unsuffixed form for POSIX and devcontainer shells.

The aggregate local solution test is intentionally single-project-at-a-time because several test projects share one Docker daemon and otherwise compete during container startup. CI keeps the Docker-heavy suites in separate jobs and runners.

## AI harness and authority

Reusable workflows live in `.agents/skills`; project role manifests live in `.codex/agents`; same-directory `CLAUDE.md` files are compatibility adapters. `npm run check:ai-harness` is dependency-free. `npm run verify:ai-harness:codex` is an authenticated local verifier and creates/deletes only its own temporary Codex thread tree.

Instructions, skills, and custom agents do not grant authority to stage, commit, push, create or apply migrations, deploy, or mutate external systems. Obtain the applicable authorization separately.

## References

- [AI-harness remediation design](docs/superpowers/specs/2026-08-11-ai-harness-architecture-remediation-design.md)
- [Architecture decision records](docs/adr/)

## OpenSpec tooling

The selected Flights M3 cancellation pilot is implemented and merged in PR36. Its current capability contract is `openspec/specs/flights-whole-order-cancellation/spec.md`; the completed design, tasks, reviews and measured process evidence are retained in `openspec/changes/archive/2026-10-06-flights-m3-cancellation`. OpenSpec tooling is pinned and launched through `node tools/openspec/run.mjs` from the repository root. Its generated skills inherit the authority boundary above. Pinned tooling is connected and verified. The fictional customer refund contract is `openspec/specs/flights-customer-refund-after-cancellation/spec.md`; its completed source tasks and PR41 implementation CI evidence are retained in `openspec/changes/archive/2026-10-11-flights-m3-customer-refunds`. It keeps supplier entitlement/settlement, customer dispatch and bank credit separate; the TestOnly gateway is excluded from Production. Future feature work uses one canonical OpenSpec corpus; historical pilot evidence does not authorize supplier calls, publication or rollout.

Flights M3 order-page live updates are merged in PR42 with source and CI acceptance. The current contract is `openspec/specs/flights-order-live-updates/spec.md`; all22completed tasks, design, reviews and implementation/CI receipts are retained in `openspec/changes/archive/2026-10-11-flights-m3-order-sse`. Header-authenticated SSE provides best-effort single-process invalidation; owner GET remains authoritative, with bounded fallback and no observation-induced financial action. Deployment-proxy streaming and real supplier/payment acceptance remain separate live gates.

Flights M3 fare-rules summary is merged in PR44 with source and CI acceptance. Current contract: `openspec/specs/flights-fare-rules-summary/spec.md`; all24completed tasks and evidence: `openspec/changes/archive/2026-10-12-flights-m3-fare-rules-summary`. Structured explanation preserves unknowns, exact money and adult/segment scopes; saved facts remain historical. AI only organizes validated claims and stays off until separately authorized model/human evaluation. The [M3 completion review](docs/architecture/flights-m3-completion-review.md) records source readiness for Support planning and the remaining model/provider/payment/deployment gates.
