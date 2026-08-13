# Travel Platform

## Current shape and scope

`Travel.AppHost` orchestrates resources and services. `Travel.Host` is the modular monolith; `Travel.AI` is a separate process. The Angular application is currently a foundation/status UI, not a complete booking frontend.

Foundation and the Flights M1 backend are implemented. Identity is thin JWT/Keycloak integration; Hotels, Rail, and Trips are scaffolds; Shared contains primitives and helpers. Travel.AI currently provides Flights natural-language search, a cost ledger, and observability.

Travel.AI uses a direct Anthropic integration behind `Microsoft.Extensions.AI.IChatClient`. Microsoft Agent Framework and Semantic Kernel are not wired at runtime.

## Architecture rules

- Intended module layers are `Core -> Application -> Infrastructure -> Api`. Modules must not import another module's internals; use shared abstractions or published events.
- Keep external wire DTOs, provider clients, and their mapping inside Infrastructure. Keep domain rules out of Infrastructure.
- Use explicit `ErrorOr<T>` imports for expected outcomes. Value-object factories return `ErrorOr<T>`; events use past tense and implement `IDomainEvent`.
- Inject `TimeProvider` in production code. Do not infer a rule that exceptions can occur only in Core.
- Name provider implementations by provider and capability, for example `DuffelFlightSearchProvider` and `TravelpayoutsSearchProvider`; do not impose a universal `{Provider}Adapter` suffix.
- The Host and Flights composition is presently split. The approved Api-facade target belongs to D4/WS3 and is not current behavior; do not extend that split.

## Working commands

```text
dotnet run --project apps/Travel.AppHost
npx nx serve web
dotnet test Travel.slnx --maxcpucount:1
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
