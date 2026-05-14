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
