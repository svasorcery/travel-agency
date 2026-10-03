# Travel Platform

> A local code demo of a modular travel backend, a separate AI process, and an Angular Flights search, booking, and owner-scoped order-list and order-status frontend. Deployment and real supplier booking are separate proof steps.

[![CI](https://github.com/svasorcery/travel-agency/actions/workflows/ci.yml/badge.svg)](https://github.com/svasorcery/travel-agency/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

## What runs today

- Travel.Host is a modular monolith. Flights M1 implements search and booking backend routes, an event-sourced booking stream, durable EF read-model reconciliation, webhook handling, and notifications. Identity provides JWT/Keycloak integration.
- Travel.AI is a separate process. Its implemented product path is Flights natural-language search using direct Anthropic through Microsoft.Extensions.AI.IChatClient and a cost ledger. Microsoft Agent Framework and additional travel agents are deferred.
- Hotels, Rail and Trips are scaffold projects outside the Host runtime graph.
- The Angular application offers anonymous Flights search and quote review, local Keycloak login, one complete 1–4-leg order for 1–9 adults, an owner-scoped list at `/flights/orders`, the page for one order at `/flights/orders/:aggregateId`, and private saved travelers at `/flights/travelers`. The local demo uses fictional data and fake auth; real provider booking, payment, and ticket issuance are not established by it.
- The Codex-first AI harness is tracked in Git, with same-directory Claude import adapters.

[Current architecture and evidence](docs/architecture/current-state.md) gives the module graph, data flows and boundaries. [ADR 0012](docs/adr/0012-maf-as-primary-agent-runtime.md) records the deferred agent-runtime decision.

## Run locally

Prerequisites: Docker, .NET 10 SDK and Node 22.22.3 or newer within the supported Node 22 range. The pinned Angular tooling in package-lock.json declares that Node floor. A devcontainer is also available.

    git clone https://github.com/svasorcery/travel-agency
    cd travel-agency
    npm ci
    dotnet restore Travel.slnx
    dotnet run --project apps/Travel.AppHost

In another terminal:

    npx nx serve web

On Windows PowerShell use npm.cmd and npx.cmd when execution policy blocks the .ps1 shims. Open http://localhost:4200/flights for the search UI, /flights/orders for your orders, or /status for system status. The Aspire dashboard URL is printed by AppHost at startup; its port can vary. Host's local public HTTP endpoint is normally http://localhost:5099. A valid search against this Host can require supplier sandbox credentials.

### Flights frontend demo without the backend stack

The opt-in demo uses only deterministic fictional results. In two terminals from the repository root:

```text
node tools/demo/flights-search-api.mjs
npx nx serve web --configuration=flights-demo
```

On Windows PowerShell use `npx.cmd`. Open http://127.0.0.1:4201/flights and select **Подставить пример**. `LED → DME` returns one-way or round-trip examples; other valid airport pairs return an explicitly labeled empty demo result. The bookable card can run fictional quote, fake login, hold and confirm for 1–9 adults. The page then opens `/flights/orders/:aggregateId`; bounded GET polling demonstrates a delayed read model and a fictional ticket number.

Select **Несколько перелётов** for 2–4 ordered airport/date rows, including open-jaw. The new anonymous `/api/flights/search/v2` supports 1–4 exact airport legs; old search/NL retain their location/city semantics. Review every refreshed leg, cabin and airport-local time before accepting one complete group offer. Independent ground travel between different airports is visibly outside the ticket. The demo covers two saved adults on open-jaw and four-leg orders, late-leg route/cabin changes, and an unknown hold followed by a new search without another write.

Open `/flights/orders` or select **Мои заказы** to see the fictional holds created while this demo server is running. The list is empty before the first hold and becomes empty again after restarting the server. List reads do not advance the single-order ticketing scenario. Returning from an order page restores the loaded list, scroll position, and selected order from browser memory. Reload clears that browser cache and requires another login; the server retains its fictional orders until it restarts.

The demo does not validate a JWT or prove owner enforcement, real EF projection, idempotent replay, provider booking, payment or real ticket issuance. Both local listeners bind to `127.0.0.1`; the proxy forwards `/api/**` and `/events/**` only to the local stub. There is no external forwarding, supplier key or Anthropic call. Prices, times, expiry, offer references and the partner link are fictional; the partner URL is inactive and uses `.invalid`.

Run the automated demo checks with `npm run test:flights-demo` (Windows: `npm.cmd run test:flights-demo`). The browser test starts and stops the two local processes, checks the real Angular proxy, and needs a locally installed Playwright Chromium. It is separate from the Host-dependent `/status` smoke. The checked [search](tests/fixtures/flights-search.json) and [quote](tests/fixtures/flights-booking.json) response examples are shared with no-database endpoint serialization tests and TypeScript decoder tests. The current OpenAPI search 200 schema still references `IResult`; this slice uses narrow checked TypeScript types until that metadata is corrected separately.

### Saved travelers

Open `/flights/travelers` after login to explicitly create, edit or delete fictional profiles. Each has a title, names, date of birth, gender, email and phone. **Заполнить пассажира** copies one selected profile into one current booking row; **Сохранить пассажира** saves that row explicitly. The slot ID and count stay fixed. Profile edits/deletion do not change a copied form or an existing order. A future-adult profile can be saved; booking still requires18 at first departure.

Backend CRUD requires `flights:book`, derives the owner from JWT and stores one encrypted EF row. Update/delete require its strong revision ETag; stale data needs a fresh read and review. Create uses a fresh client UUID and `If-None-Match: *`; writes return only ID/revision. Missing keys or unreadable data fail closed. Delete matches owner/revision without decrypt. Responses use `no-store`; details and operations stay in memory. Timeout or malformed success is unknown and never automatically resent; GET shows current data, not historical execution proof.

The isolated demo has an explicit A/B selector for fictional profile isolation. It establishes protocol/UI acceptance; JWT and encryption are verified in separate real layers. The source migration was not applied locally. See [M2.4 evidence](docs/superpowers/results/2026-10-03-flights-m24-local.md).

### Own orders

The authenticated `/flights/orders` page calls `GET /api/flights/orders?limit=21&offset=N`, displays 20 orders per batch, and uses the extra item to decide whether more are available. It automatically loads the next batch near the bottom of the list; **Показать ещё** provides the same action manually. Opening a card uses the existing `/flights/orders/:aggregateId` page. The loaded list, scroll position, and selected order are kept only in memory for the current owner and restored on return; reload starts with an empty browser cache and requires re-login.

The server determines the owner from the authenticated identity. Requests do not supply a user ID. Orders are returned by `BookedAt DESC, AggregateId DESC`; the displayed booking time means the hold was created. Offset pagination has no total count or snapshot guarantee, so newly created orders can shift later batches. Refresh starts at the beginning. A recently successful hold can briefly precede its eventual EF projection; the page waits for a bounded period and then offers manual refresh, without inventing an order. Tokens and passenger data stay out of the list cache, URLs, storage, and logs. The frontend retains its existing `flights:book` login requirement although the backend read routes require only authentication.

### Local persistence and health

AppHost uses named PostgreSQL, Redis, NATS and Keycloak volumes by default in Development. Use UseVolumes=false only for a disposable run. Development and Testing run ordered schema initializers before readiness. Production startup validates existing schema compatibility and does not apply migrations.

Host and AI internal listeners expose /health/live, /health/ready and /health/dependencies on ports 5098 and 5159 respectively. These probes are not served on the public application listener. Local tests prove the configured behavior; production ingress, network exposure and live schema rollout require separate validation.

Interactive NL-search uses bounded Core NATS request/reply. Wolverine's PostgreSQL-backed durability handles booking reconcile messages and sibling outbox notifications. JetStream being available on the broker does not make the NL-search request durable.

For source checks without paid Anthropic evaluations:

    dotnet test Travel.slnx --maxcpucount:1 --filter "Category!=AiEval&Category!=AiEvals"
    npm run check:ai-harness
    npm run check:dotnet-inventory
    npm run check:readme-examples

The aggregate .NET command runs one test project at a time because several integration suites share a local Docker daemon. CI runs the Docker-heavy lanes separately. The paid AI eval lane needs its own credential and authorization.

## Architecture

[Current-state overview](docs/architecture/current-state.md) maps Host, enabled facades, persistence, AI transport, health, tests and deferred modules. [Architecture decision records](docs/adr/) preserve the original decisions and their accepted amendments.

### AI development harness

[AGENTS.md](AGENTS.md) is the canonical entry point. Reusable workflows live in [.agents/skills](.agents/skills/), Codex role manifests in [.codex/agents](.codex/agents/), and [CLAUDE.md](CLAUDE.md) imports the same instructions for Claude Code. The dependency-free check is npm run check:ai-harness; npm run verify:ai-harness:codex is an authenticated local verifier that creates and deletes only its own temporary task tree.

## Current stack

| Area | Checked-in choice |
|---|---|
| Backend | .NET 10, Aspire 13, Wolverine 6, Marten 9, EF Core 10 |
| Storage | PostgreSQL 17, Marten booking stream, EF Flights read model and AI cost ledger |
| Frontend | Angular 21, Signals/httpResource, Tailwind 4, Spartan UI facade |
| Messaging | Core NATS for NL-search; Wolverine/PostgreSQL durable local queues and outbox |
| AI | Microsoft.Extensions.AI IChatClient with direct Anthropic integration |
| Tooling | Nx 23 for frontend affected work, explicit .NET solution/CI inventory, Biome, CSharpier and Lefthook |

## Provider configuration for the demo

The README smoke below needs no external provider key: it tests status, OpenAPI and a rejected validation request. A valid Duffel search or booking requires sandbox provider credentials. The Duffel search adapter is registered in Development even without a key, so such a request can return provider-unavailable; Travelpayouts registration is conditional on its configuration. NL-search needs an Anthropic key when it reaches the model. Production-required Keycloak, SMTP and provider settings are validated at startup.

Keep keys in user secrets, environment variables or protected deployment configuration; do not commit them. .NET nested configuration uses double underscores in environment variable names.

### Duffel sandbox

| Env var | Purpose |
|---|---|
| Flights__Duffel__ApiKey | Sandbox access token |
| Flights__Duffel__WebhookSecret | Webhook signing secret |

### Travelpayouts deeplinks

| Env var | Purpose |
|---|---|
| Flights__Travelpayouts__ApiToken | API token |
| Flights__Travelpayouts__PartnerMarker | Partner marker |

### Anthropic in Travel.AI

| Env var | Purpose |
|---|---|
| Anthropic__ApiKey | API key for NL-search model calls |

## Flights M1 — manual sandbox requests

Start the full stack first:

```bash
dotnet run --project apps/Travel.AppHost
```

The examples target the Host local public endpoint on port 5099. If AppHost selects a different endpoint, use the URL shown in its resource view.

The requests below are generated from [the checked example catalog](docs/examples/flights-requests.json). The quick smoke runs without provider keys and checks status, OpenAPI, and an invalid search request:

    npm run smoke:readme
    # Windows PowerShell: npm.cmd run smoke:readme

The booking commands are manual sandbox examples for one adult; the product accepts 1–9 adults. Keep search/quote passenger count aligned, and send one explicit title, contact and identity row per quoted slot. The amount is the quoted total for the entire group, never a multiplied single fare. Replace the double-brace values with a future departure date, a current Duffel offer reference, the aggregate ID, `binding.revision` and each `binding.slots[].bookingPassengerId` returned by the latest quote, and a JWT from the local Keycloak realm. Hold and confirm require the flights:book scope and a fresh GUID in each Idempotency-Key placeholder. Run these Bash commands from a POSIX shell or devcontainer; they are also exercised against fake downstream services in the HTTP test suite. A successful write can briefly precede its eventual EF order view. The plain list GET example uses the server defaults `limit=50` and `offset=0`; the UI separately requests `limit=21` for batches of 20. Both order GET examples derive the owner from the JWT and have no request body or idempotency key.

<!-- BEGIN FLIGHTS REQUEST EXAMPLES -->

### 1. Search (anonymous)

```bash
curl -sS -X POST http://localhost:5099/api/flights/search \
  -H 'Content-Type: application/json' \
  --data-binary '{
  "origin": "LED",
  "destination": "DME",
  "departureDate": "{{departureDate}}",
  "returnDate": null,
  "passengerCount": 1,
  "cabinClass": "economy"
}'
```

### 2. NL-search (anonymous; requires Anthropic for a parsed result)

```bash
curl -sS -X POST http://localhost:5099/api/flights/search/nl \
  -H 'Content-Type: application/json' \
  --data-binary '{
  "query": "из Москвы в Санкт-Петербург {{departureDate}}"
}'
```

### 3. Quote (requires a current Duffel offer reference)

```bash
curl -sS -X POST http://localhost:5099/api/flights/orders/quote \
  -H 'Content-Type: application/json' \
  --data-binary '{
  "providerOfferRef": "{{providerOfferRef}}",
  "provider": "duffel",
  "passengerCount": 1
}'
```

### 4. Hold (requires a JWT with flights:book)

```bash
curl -sS -X POST http://localhost:5099/api/flights/orders/hold \
  -H 'Content-Type: application/json' \
  -H 'Authorization: Bearer {{jwt}}' \
  -H 'Idempotency-Key: {{holdIdempotencyKey}}' \
  --data-binary '{
  "aggregateId": "{{aggregateId}}",
  "quoteRevision": "{{quoteRevision}}",
  "passengers": [
    {
      "bookingPassengerId": "{{bookingPassengerId}}",
      "title": "mr",
      "givenName": "Ivan",
      "familyName": "Ivanov",
      "dateOfBirth": "1990-01-15",
      "gender": "male",
      "email": "ivan@example.test",
      "phone": "+79001234567"
    }
  ]
}'
```

### 5. Confirm (requires a JWT with flights:book)

```bash
curl -sS -X POST http://localhost:5099/api/flights/orders/confirm \
  -H 'Content-Type: application/json' \
  -H 'Authorization: Bearer {{jwt}}' \
  -H 'Idempotency-Key: {{confirmIdempotencyKey}}' \
  --data-binary '{
  "aggregateId": "{{aggregateId}}"
}'
```

### 6. Read one order (authenticated owner)

```bash
curl -sS -X GET http://localhost:5099/api/flights/orders/{{aggregateId}} \
  -H 'Authorization: Bearer {{jwt}}'
```

### 7. List own orders (authenticated owner; defaults limit=50, offset=0)

```bash
curl -sS -X GET http://localhost:5099/api/flights/orders \
  -H 'Authorization: Bearer {{jwt}}'
```

### 8. Stream order status (authenticated)

```bash
curl --no-buffer -sS -X GET http://localhost:5099/events/flights/orders/{{aggregateId}} \
  -H 'Authorization: Bearer {{jwt}}'
```

<!-- END FLIGHTS REQUEST EXAMPLES -->

---

## Flights demonstration boundaries

- **Sandbox only** — Duffel is wired to its sandbox environment. Moving to production requires Duffel KYC, which is blocked for RU-based entities; this is a documented constraint (see concept §9.1).
- **Booking correctness (M2.3a)** — accepted-total and succeeded-receipt checks, bounded 16KiB booking requests, and memory-only hold/confirm guards. Unknown results must not be retried or reported as cancellation/refund; viewing an order does not prove the original attempt outcome. No cross-tab/reload/restart financial guarantee. See [scope and limitations](docs/superpowers/specs/2026-10-03-flights-m23-design.md).
- **Multi-passenger booking (M2.3b)** — 1–9 adults with quote-bound local passenger IDs, explicit title/contact details and one encrypted party snapshot. Age is checked at the first departure local date. Children, infants and identity documents remain outside booking. Saved profiles are covered by M2.4. Travelpayouts is explicitly skipped for groups because its current integration cannot honor the count.
- **Ordered journeys (M2.5)** — one complete offer/order across 1–4 legs, including open-jaw. V2 uses exact airports and nondecreasing local requested dates; equal dates are allowed. Slice duration is less than 48 hours, summed journey duration less than 192 hours, excluding stays/ground travel. Duffel local timestamps require truthful IANA resolution; missing/ambiguous/conflicting time facts fail safely. Legacy events/orders are read without new creation rules or timestamp rewrites. Travelpayouts is skipped for v2 before HTTP. See [ADR 0025](docs/adr/0025-flights-ordered-journeys-and-airport-time.md) and [M2.5 specification](docs/superpowers/specs/2026-10-03-flights-m25-multi-leg-design.md).
- **New passenger snapshots and webhook bodies require PII protection keys (M2.2)** — new holds use encrypted party `OfferHeldV3`; singular `OfferHeldV2` remains readable; legacy events, rows and backups retain their old plaintext. Only fictional passengers are allowed. Missing keys return 503 for new PII writes; search, quote and order metadata remain available. Provisioning is explicit and separate from normal Host startup: [key lifecycle/recovery](docs/operations/flights-pii-key-recovery.md), [ADR 0024](docs/adr/0024-flights-pii-protection.md).
- **Airline-initiated refunds only** — refunds are triggered by a Duffel webhook; user-initiated refund flows and fare-rule policies are M3.
- **Saved travelers (M2.4)** — owner-private ciphertext storage, explicit copy/save and optimistic revision checks. No passports, sharing, automatic save or changes to booked snapshots.
- **Explainable ranking (M2.1)** — deterministic price-first ordering within each currency, with duration/transfer tie-breaks, explicit unknown partner factors and original-price evidence. No LLM or paid API is needed for ranking. Different purchase paths are preserved; quote still verifies booking price and availability.
- **Local booking proof only** — Angular covers quote, local OIDC login, 1–9-adult hold/confirm, owner order feed, and cancellation from an order page with in-memory replay and return-position preservation. Cancellation is proven with fictional offline demo data. Incomplete real Duffel cancellation now returns `Flights.ProviderCancellationNotSupported` before HTTP; creating a cancellation quote is not a confirmed cancellation. Refunds and cancellation of ticketed orders are outside this slice. Production issuer configuration, deployed Host/projection acceptance, real provider/payment flows and durable recovery across reloads remain separate work.

---

## Companion content

- Architecture decisions: [docs/adr/](docs/adr/)
- Specs and plans: [docs/superpowers/](docs/superpowers/)

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Security issues: [SECURITY.md](SECURITY.md).

## License

MIT — see [LICENSE](LICENSE).
