# Travel Platform

> Production-grade travel booking and trip-planning platform showcasing modern .NET + Angular + AI-augmented development practices.

[![CI](https://github.com/svasorcery/travel-agency/actions/workflows/ci.yml/badge.svg)](https://github.com/svasorcery/travel-agency/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com)
[![Angular](https://img.shields.io/badge/Angular-21-DD0031)](https://angular.dev)

## What this is

Public showcase project demonstrating:
- DDD modular monolith with Wolverine + Marten + WolverineFx.Http
- Extracted AI service using Microsoft Agent Framework (MAF) 1.0
- Angular 21 + Signals + httpResource + NgRx SignalStore + Tailwind v4
- AI-augmented development with custom Claude Code agents, slash commands, and hooks
- Honest BYO-keys with graceful degradation across providers

Implementation roadmap:
- [x] **Subproject 0 — Foundation** (current): scaffold, AI-harness, vertical slice
- [ ] **Subproject 1 — Flights flagship**: end-to-end booking, NL-search, explainable ranking
- [ ] **Subproject 2 — Hotels**: multi-supplier search with dedup
- [ ] **Subproject 3 — Rail**: read-only multi-source schedules
- [ ] **Subproject 4 — Trip Planning**: AI-orchestrated multi-day itineraries
- [ ] **Subproject 5 — AI service core**: own eval framework, MAF deep-dive

## Quick start

### Prerequisites
- Docker (Desktop / Engine / Podman)
- Optional: VS Code with Dev Containers extension

### Run locally

```bash
git clone https://github.com/svasorcery/travel-agency
cd travel-agency
# Option A: Dev container (recommended) — open in VS Code, "Reopen in Container"
# Option B: native — install .NET 10 SDK + Node 22

npm ci
dotnet restore
dotnet run --project apps/Travel.AppHost
# In another terminal:
npx nx serve web
```

Open `http://localhost:4200/status` — you should see `db: ok` and a Postgres version.

The Aspire dashboard at `https://localhost:17002` shows all running resources.

## Architecture

(insert C4 context diagram or ASCII overview here)

See [CLAUDE.md](CLAUDE.md) for the architectural map and conventions, and [docs/adr/](docs/adr/) for all architectural decisions.

## Stack

| Layer | Choice | Why |
|---|---|---|
| Backend | .NET 10 + Aspire 13 + Critter Stack (Wolverine + Marten + WolverineFx.Http) | MIT-only after MediatR/MassTransit went commercial |
| Storage | PostgreSQL 17 + pgvector + Marten ES + EF Core 10 | Polyglot persistence on one database |
| Frontend | Angular 21 + Signals + Tailwind v4 + PrimeNG unstyled | Modern Angular with full SSR |
| Messaging | NATS JetStream + Wolverine outbox | In-process and cross-process |
| AI | MAF 1.0 + Claude (via Anthropic NuGet) | Production-ready agent framework |
| Tooling | NX 22 + Biome + CSharpier + Lefthook + commitlint + Renovate | Polyglot monorepo |

## Bring Your Own Keys

The project is designed for graceful degradation — providers without keys are simply not loaded, and the UI / logs document the missing source. To run with full functionality, see [docs/byo-keys.md](docs/byo-keys.md) (TBD — will be created in Subproject 1 when first external provider is wired in).

| Provider | Required for | Sandbox available? | Production from RU? |
|---|---|---|---|
| Anthropic API | All AI features | yes, self-service | non-RU card required |
| Duffel (Flights/Stays/Cars) | Flight bookings | yes, self-service | KYC blocked |
| Travelpayouts | Flight deeplinks (RU content) | yes, no auth | yes |
| Yandex.Rasp | Rail schedules | yes, email-confirmed | yes |
| Keycloak (self-hosted) | Identity | yes, Aspire-hosted | yes |

## Companion content

- Blog series: link TBD
- AI-augmented development sessions: [docs/ai-conversations/](docs/ai-conversations/)
- Architecture decisions: [docs/adr/](docs/adr/)
- Specs and plans: [docs/superpowers/](docs/superpowers/)

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Security issues: [SECURITY.md](SECURITY.md).

## License

MIT — see [LICENSE](LICENSE).
