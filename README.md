# Travel Platform

> Production-grade travel booking and trip-planning platform showcasing modern .NET + Angular + AI-augmented development practices.

[![CI](https://github.com/svasorcery/travel-agency/actions/workflows/ci.yml/badge.svg)](https://github.com/svasorcery/travel-agency/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com)
[![Angular](https://img.shields.io/badge/Angular-21-DD0031)](https://angular.dev)

## What this is

Public showcase project demonstrating:
- DDD modular monolith with Wolverine + Marten + WolverineFx.Http
- Extracted AI service using Microsoft.Extensions.AI with Anthropic-backed structured NL search
- Angular 21 + Signals + httpResource + NgRx SignalStore + Tailwind v4
- Tracked Codex-first AI harness with canonical skills, custom agents, and thin Claude Code compatibility adapters
- Honest BYO-keys with graceful degradation across providers

Implementation roadmap:
- [x] **Subproject 0 — Foundation**: scaffold, AI-harness, vertical slice
- [x] **Subproject 1 — Flights M1** (production-grade): end-to-end booking, NL-search, mixed bookable+deeplink aggregation, resilience, observability — see [remediation design spec](docs/superpowers/specs/2026-05-14-flights-m1-remediation-design.md)
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

### Local persistence, startup, and health

Aspire keeps named PostgreSQL, Redis, NATS, and Keycloak volumes by default in Development. Set `UseVolumes=false` only for a disposable run; the normal Development default remains persistent.

Host and Travel.AI use Core NATS request/reply for interactive NL-search. NATS JetStream is enabled for durable commands and events; the NL-search request/reply subject does not use JetStream persistence.

In Development and Testing, Host and Travel.AI run their ordered initializers and apply checked-in EF migrations before readiness becomes healthy. In Production they never apply migrations automatically: they validate the existing schema, keep `/health/ready` unhealthy on a mismatch, and leave `/health/live` as process-liveness only. Applying migrations is a separate deployment responsibility.

Health routes exist only on the internal listeners: Host uses port `5098` and Travel.AI uses `5159`, with `/health/live`, `/health/ready`, and `/health/dependencies`. Aspire waits on `/health/ready`; these routes are deliberately unavailable on the public application listener. Production still requires deployment/ingress/network-policy wiring and live validation of those internal ports.

Run the fresh-volume functional smoke twice when changing startup, persistence, messaging, or Aspire topology:

```bash
dotnet test tests/Travel.Host.Tests.Integration --filter Category=AspireSmoke
dotnet test tests/Travel.Host.Tests.Integration --filter Category=AspireSmoke
```

Each run uses `--environment=Testing` and `UseVolumes=false`, starts from blank disposable storage, verifies Host and AI initialization, sends a locally signed Duffel webhook through the EF inbox/outbox/handler path, checks duplicate delivery, and writes the AI ledger through the production DbContext options without calling Duffel or Anthropic. This is disposable integration proof, not deployment, external ingress, or production database proof.

## Architecture

(insert C4 context diagram or ASCII overview here)

See [AGENTS.md](AGENTS.md) for the architectural map and conventions, and [docs/adr/](docs/adr/) for all architectural decisions.

### AI development harness

[AGENTS.md](AGENTS.md) is the canonical entry point; nested scoped instructions add local context. Reusable workflows live in [`.agents/skills/`](.agents/skills/), and Codex role manifests live in [`.codex/agents/`](.codex/agents/). [CLAUDE.md](CLAUDE.md) and [`.claude/`](.claude/) are compatibility adapters for Claude Code.

Use the dependency-free `npm run check:ai-harness` to validate harness changes; `npm run verify:ai-harness:codex` is the authenticated local verifier. A workflow invocation never implies authority for Git publication, migration application, deployment, or external mutation.

## Stack

| Layer | Choice | Why |
|---|---|---|
| Backend | .NET 10 + Aspire 13 + Critter Stack (Wolverine + Marten + WolverineFx.Http) | MIT-only after MediatR/MassTransit went commercial |
| Storage | PostgreSQL 17 + pgvector + Marten ES + EF Core 10 | Polyglot persistence on one database |
| Frontend | Angular 21 + Signals + Tailwind v4 + Spartan UI | Modern Angular with full SSR |
| Messaging | Core NATS request/reply for interactive NL-search; JetStream for durable commands/events; Wolverine outbox | Message semantics follow delivery value, not broker capability |
| AI | Microsoft.Extensions.AI + Anthropic | Structured natural-language flight search |
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

## Bring Your Own API Keys — Flights M1

Flights M1 wires three external providers. The app reads configuration via standard .NET configuration; the env var format for nested keys uses double-underscore as the separator.

**Graceful-degradation behaviour:** with no keys set the app still builds and runs — provider calls fail at request time (search returns provider-unavailable partial failures; NL-search returns an unparseable error). This is intentional and follows the concept's "optional providers" principle.

Set the variables as environment variables or via `dotnet user-secrets` on the `Travel.Host` / `Travel.AI` projects. When running under Aspire you can also supply them as Aspire parameters.

### Duffel (flights search + booking — sandbox)

Sign up at <https://app.duffel.com/>, create a sandbox access token and a webhook signing secret.

| Env var | Purpose |
|---|---|
| `Flights__Duffel__ApiKey` | Sandbox access token |
| `Flights__Duffel__WebhookSecret` | Webhook signing secret |

### Travelpayouts (deeplink flight offers)

Sign up at <https://www.travelpayouts.com/>, get an API token and your partner marker.

| Env var | Purpose |
|---|---|
| `Flights__Travelpayouts__ApiToken` | API token |
| `Flights__Travelpayouts__PartnerMarker` | Partner marker |

### Anthropic (NL-search, runs in Travel.AI)

Get a key at <https://console.anthropic.com/>.

| Env var | Purpose |
|---|---|
| `Anthropic__ApiKey` | Anthropic API key |

---

## Flights M1 — happy-path walkthrough

Start the full stack first:

```bash
dotnet run --project apps/Travel.AppHost
```

All curl examples below target `http://localhost:5099` (Travel.Host HTTP port from `launchSettings.json`; verify the actual port on the Aspire dashboard at `https://localhost:17002` if it differs).

### 1. Search (anonymous)

```bash
curl -s -X POST http://localhost:5099/api/flights/search \
  -H 'Content-Type: application/json' \
  -d '{
    "origin": "LED",
    "destination": "DME",
    "departureDate": "2026-08-01",
    "passengers": [{ "type": "adult" }],
    "cabinClass": "economy"
  }'
```

### 2. NL-search (anonymous)

```bash
curl -s -X POST http://localhost:5099/api/flights/search/nl \
  -H 'Content-Type: application/json' \
  -d '{ "query": "из Москвы в Питер на 15 июля" }'
```

### 3. Quote (anonymous)

Replace `<offer-ref>` with a `providerOfferRef` returned by search.

```bash
curl -s -X POST http://localhost:5099/api/flights/orders/quote \
  -H 'Content-Type: application/json' \
  -d '{
    "providerOfferRef": "<offer-ref>",
    "provider": "duffel"
  }'
```

### 4. Hold (authenticated)

Obtain a Bearer JWT from the Aspire-provisioned Keycloak realm `travel` first. Replace `<aggregate-id>` with the `aggregateId` returned by quote.

```bash
curl -s -X POST http://localhost:5099/api/flights/orders/hold \
  -H 'Content-Type: application/json' \
  -H 'Authorization: Bearer <your-jwt>' \
  -H 'Idempotency-Key: hold-001' \
  -d '{
    "aggregateId": "<aggregate-id>",
    "passengers": [
      {
        "firstName": "Ivan",
        "lastName": "Ivanov",
        "dateOfBirth": "1990-01-15",
        "gender": "male",
        "email": "ivan@example.com",
        "phone": "+79001234567",
        "passportNumber": "1234567890",
        "passportExpiry": "2030-01-01",
        "passportIssuingCountry": "RU",
        "nationality": "RU"
      }
    ]
  }'
```

### 5. Confirm (authenticated)

```bash
curl -s -X POST http://localhost:5099/api/flights/orders/confirm \
  -H 'Content-Type: application/json' \
  -H 'Authorization: Bearer <your-jwt>' \
  -H 'Idempotency-Key: confirm-001' \
  -d '{ "aggregateId": "<aggregate-id>" }'
```

The confirmation email lands in **Mailpit** — open `http://localhost:8025` in your browser.

### 6. Stream order status (SSE)

```bash
curl --no-buffer http://localhost:5099/events/flights/orders/<aggregate-id> \
  -H 'Authorization: Bearer <your-jwt>'
```

Each state transition (held → confirmed → cancelled) is pushed as a Server-Sent Event.

---

## Known limitations in Flights M1

- **Sandbox only** — Duffel is wired to its sandbox environment. Moving to production requires Duffel KYC, which is blocked for RU-based entities; this is a documented constraint (see concept §9.1).
- **Single-passenger booking only** — multi-passenger and multi-leg / open-jaw itineraries arrive in M2.
- **Passenger PII is stored unencrypted** — field-level encryption is planned for M2 alongside saved-traveller profiles (see ADR 0015).
- **Airline-initiated refunds only** — refunds are triggered by a Duffel webhook; user-initiated refund flows and fare-rule policies are M3.
- **Price-then-duration ranking** — explainable ranking (anchoring, transparency scores) is M2; M1 sorts by price then total duration.
- **Backend milestone only** — the Angular UI, Playwright E2E tests, and visual regression suite are a separate plan; this milestone delivers the backend and integration tests.

---

## Companion content

- Blog series: link TBD
- AI-augmented development sessions: [docs/ai-conversations/](docs/ai-conversations/)
- Architecture decisions: [docs/adr/](docs/adr/)
- Specs and plans: [docs/superpowers/](docs/superpowers/)

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Security issues: [SECURITY.md](SECURITY.md).

## License

MIT — see [LICENSE](LICENSE).
