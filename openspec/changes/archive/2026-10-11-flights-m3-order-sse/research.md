# Research — SSE order page, 2026-10-11

## Provenance and scope

Fresh `git fetch origin dev` returned `7b7fb0f2831cb5067d4dea025e9e7d9d3f16e347`; `FETCH_HEAD`, `origin/dev` and the new managed worktree HEAD are equal. `git merge-base --is-ancestor 7b7fb0f2831cb5067d4dea025e9e7d9d3f16e347 HEAD` exited 0. Main checkout was clean. Managed worktree: `C:/Users/Vladimir_sva/.codex/worktrees/flights-m3-order-sse-plan/travel-agency`, detached HEAD. No existing branch was reused, moved or rewritten. Source evidence below is relative to that exact base.

Read root/Flights/Shared AGENTS, current-state, roadmap M3 and ADR 0015–0019, 0021, 0023–0025. The three current capability specs and refund archive remain authoritative. Historical pending delivery text in current-state/ADR/archive is not a fresh blocker: the user handoff identifies merged PR41 and successful final/postmerge CI. This audit did not re-run or independently inspect those historical CI receipts and does not reuse their results as SSE proof.

This is planning only. No product test, local Host/AppHost, database, key provisioning, schema/migration, supplier/payment/AI API, deployment or CI dispatch ran.

## Current chain and concrete gaps

| Evidence at base (repository-relative file) | Observed behavior | Consequence |
|---|---|---|
| `modules/flights/Travel.Modules.Flights.Api/Endpoints/OrderEventsSseEndpoint.cs` | Authorize; invalid identity rejected; EF owner lookup gives non-disclosing 404; register before flushed `: connected`; 15s heartbeat; 32-slot channel; unregister in finally | Reuse endpoint. Handshake occurs after registration. No id/replay protocol. |
| `modules/flights/Travel.Modules.Flights.Infrastructure/Notifications/Sse/OrderSseConnectionRegistry.cs` | One-process singleton; per-connection locked increasing StreamVersion; estimated 1MiB budget plus bounded channel; full consumer is disconnected | Same-version notifications can coalesce; every surviving event must invalidate the whole page, not just its named panel. |
| `modules/flights/Travel.Modules.Flights.Application/Handlers/Notifications/PublishOrderSseHandler.cs` | Confirmed/Ticketed/Cancelled only; current state suppression; payload contains status; readiness before publish | Existing signals already suffice for ticket webhook. Never apply payload to UI as truth. |
| `modules/flights/Travel.Modules.Flights.Infrastructure/Notifications/BookingNotificationReadiness.cs` | Fresh EF query, owner, required version; missing/lagging row throws; legacy null version resolves current Marten version | Reuse readiness; no repair/projection from notification. |
| `modules/flights/Travel.Modules.Flights.Api/Composition/BookingConsistencyHandlerPolicy.cs` | Explicit list of 3 notification types; readiness retries 2s/10s/1m/5m then DLQ | New notification must join the existing scoped policy. No Host-wide policy change. |
| `modules/flights/Travel.Modules.Flights.Application/Handlers/Webhooks/DuffelWebhookHandler.cs` | Ticket event + notification + reconcile commit first; inbox ack later. Airline cancellation now records SupplierCancellationReported + reconcile with empty notifications | Add only missing report invalidation. AirlineChanged records a metric and has no new persisted customer fact in this slice. |
| `modules/flights/Travel.Modules.Flights.Application/Refunds/CustomerRefundWriter.cs` | Refund batches commit work/reconcile with empty notifications | Add one invalidation per nonempty committed batch, including claim/outcome/manual/review/no-refund. No payout rules change. |
| `modules/flights/Travel.Modules.Flights.Application/Cancellation/CancellationDecisionWriter.cs` | Confirmed/Cancelled notifications only for respective events; cancellation success also enqueues AssessCustomerRefund; report resolution and nonterminal phases can change without those signals | Add one materialization-eligible batch invalidation; this writer also handles pre-hold creation review, so source-only negative/inconclusive batches must stay fallback-only. Preserve existing work/email semantics. |
| `modules/flights/Travel.Modules.Flights.Application/Persistence/DocumentSessionExtensions.cs` | Existing enrolled Marten outbox and AllowMultiples boundary, rollback and conflict classification | Reuse; do not introduce a second transaction or publish before commit. |
| `modules/flights/Travel.Modules.Flights.Api/Contracts/Contracts.cs` | OrderView already has ProjectedStreamVersion, but OrderResponse/mapper omit it | Add nullable transport field, no EF migration; actual historical migration/entity sentinel is -1, which must map to unknown at API boundary. Shared mapper also affects list/cancel-shaped responses: audit all callers. |
| `modules/flights/Travel.Modules.Flights.Application/Handlers/Booking/GetBookingCreationHandler.cs`; `.../Queries/GetBookingCreationQuery.cs`; Api `Contracts/BookingCreationResponse.cs` | Source-derived creation snapshot has no bookingVersion; ObservedAt drives a time-derived manual outcome | Add snapshot-bound version; equal-version later observation must still advance the time-derived diagnosis. |
| Cancellation/refund owner GET handlers and `shared/ts/api-client/src/flights-customer-refund.types.ts` | Authoritative Marten state, bookingVersion (plus case/operation revisions), exact string money; no external reads | Read these directly even when order EF is behind. SSE must never call supplier refresh POST. |
| `apps/web/src/app/flights/flight-order-page.component.ts` | GET creation then order; polling 2s for 30s (190s creation); stops on terminal state; older ordinary order/creation snapshots lack server-version guard; 404/malformed can clear displayed data | Add versioned independent reconciliation; preserve prior good data for transient failure; terminal business state must not stop live observation. |
| `apps/web/src/app/flights/flight-cancellation.service.ts` | Owner/epoch/generation guards; monotonic booking/operation version; reads may be dropped while another is active; timers and explicit consent separate | Retain guards; coalesce follow-up reads; do not re-open service or reset consent on every signal. |
| `apps/web/src/app/flights/flight-customer-refund.service.ts` | Owner/epoch/generation guards; monotonic version/revision; 190s polling only for active states; keeps Sent on GET error; reads during active GET are dropped | Preserve proven result and bounded workflow polling; add signal-driven follow-up and periodic recovery for terminal/report changes. |
| `apps/web/src/app/flights/flights-auth.service.ts` | Memory token, accessToken refreshes SDK with 30s margin; identityEpoch changes on logout/replacement | Reuse API, no auth edits. Check identity again after await; rotate transport when token changes. |
| `apps/web/proxy.conf.json`; `apps/web/proxy.flights-demo.conf.json` | Ordinary proxy only `/api/**`; demo already routes `/events/**` | Add ordinary dev events route. Deployment proxy validation belongs to separately authorized live gate. |
| `tools/demo/flights-search-api.mjs`; `tools/demo/flights-customer-refunds.mjs` | Fictional Node API and refund presets; no real SSE producer found | Need controlled incremental stream and startup-only fictional webhook schedule. Existing demo refuses Authorization. |

`rg` across Angular/shared TS found no EventSource/fetch SSE consumer. The roadmap mentions generic `/events/{userId}`; implemented owned-order endpoint is the actual contract to retain. Do not implement the historical generic stream.

## Options

1. **Recommended: existing SSE + authenticated fetch + GET reconciliation/fallback.** Small transport/parser and focused read coordination; targeted missing notifications and version fields. Keeps JWT headers and existing security. Requires explicit parser/lifecycle tests.
2. Poll-only UI. Lowest protocol cost but does not demonstrate requested SSE; retain its reads only as fallback.
3. Native EventSource with cookies/token URL, or a third-party generic streaming/recovery client. Cookie conversion changes auth; token URL violates privacy; generic library/platform adds scope without retained need. Rejected. Likewise no broker, Last-Event-ID store, cross-tab shared connection or multi-node delivery claim.

Alternative emission from the EF reconciler would avoid writer hooks, but couples notification delivery to replay/reset maintenance and risks losing the post-EF signal on crash. Retain current transactional notification pattern and add three narrowly located hooks. Fallback covers best-effort loss even after a successful publish with no connected browser.

## Official external contracts checked 2026-10-11

- [WHATWG SSE](https://html.spec.whatwg.org/multipage/server-sent-events.html): EventSource init exposes URL/credentials, no arbitrary authorization headers; UTF-8 line/event framing and blank-line dispatch. Implement the small protocol subset the endpoint uses, with safe handling of other fields.
- [MDN Fetch](https://developer.mozilla.org/en-US/docs/Web/API/Fetch_API/Using_Fetch): explicit headers, AbortController and streaming response body are available; fetch requires explicit HTTP status handling. This supports the chosen header-authenticated transport.
- [Angular dev proxy](https://angular.dev/tools/cli/serve#proxying-to-a-backend-server): route proxy patterns belong to dev-server configuration. Add `/events/**` beside the existing `/api/**` mapping.

No provider wire contract is changed or newly relied on. No provider API was contacted; new webhook tests reuse the current normalized-facts and signed fictional-ingress contracts.

## Planning environment

Worktrees contain tracked files, not ignored installed dependencies. The new worktree initially lacked `tools/openspec/node_modules`; user explicitly authorized pinned runtime restoration. `npm.cmd ci --prefix tools/openspec --ignore-scripts --no-audit --no-fund` restored 70 packages from the unchanged lockfile. Launcher verification accepted the committed runtime digest. Sandbox `spawn EPERM` required the normal execution-permission mechanism; no policy/config workaround was installed. `list --json` and `context --json` then resolved this worktree; schema is spec-driven.


## Additional self-review evidence — 2026-10-11

Read current auth and both root reader services again. `FlightsAuthService.accessToken` calls `acceptIdentity`, which writes a new authenticated status object even for the same owner/epoch. `FlightCancellationService` and `FlightCustomerRefundService` currently own workflow timers and visibility listeners; their existing generation also owns mutation/view lifecycle, so page-only cancellation is not sufficient to guard late leaf GET application. Refund reader currently accepts only strictly newer snapshots: successful same-version reads therefore require an explicit completion acknowledgement distinct from replacing data.

These source facts justify idempotent page-session start, one observation pause gate for retained readers, read-only epoch separate from command admission generation, and equal-version freshness acknowledgement. Completed/pending duplicate suppression and exhausted-window latching are design completeness corrections; no new runtime defect is claimed before implementation. Shared fictional schedule is once per startup/order, not once per reconnect.
