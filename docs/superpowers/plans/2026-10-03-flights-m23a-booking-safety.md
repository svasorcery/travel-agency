# Flights M2.3a Bounded Booking Correctness Implementation Plan

> **For agentic workers:** use subagent-driven-development with explicit file ownership for independent tasks, TDD and whole-change review. User approved on 2026-10-03 conditional on no overengineering; the spec records the reduction.

**Goal:** correct price/outcome handling and tab-local booking state needed before multiple passengers.
**Architecture:** existing provider ErrorOr ports and atomic success commit; existing memory operation service; bounded request body in existing middleware. No new events, migration, durable barrier, endpoint or recovery engine.
**Spec:** [M2.3 design](../specs/2026-10-03-flights-m23-design.md), S1–S4.
**Base:** refetch dev at execution; last verified 02356dfc5010172548bb45938bc1bf29deae6dcd.

## Constraints

Fictional data/fake HTTP only; no real supplier/payment/Anthropic/paid evals, local schema/Host/AppHost/key provisioning/deploy or CI/CD edits. Preserve JWT/owner/scope, PII protection and raw-byte hashes. Unit/noDB HTTP tests only locally after fixture inspection; full DB/durable/Aspire CI mandatory. No user refund/cancellation recovery; no permanent server attempt state. Reload/cross-client/server restart limitations remain explicit; do not call this exactly-once payment.

## Review focus

1. Content-Length or chunked body/route aliases must not bypass cap or spool PII before validation — Task1.
2. A first supplier422/post-effect conflict must not be reported as a proved local rejection — Task2/3.
3. Fresh provider amount or 2xx failed/pending/malformed receipt must not authorize success — Task2.
4. Unknown hold/confirm cannot be cleared by later error, navigation or changed criteria within one owner/session. Logout or a new identity epoch clears PII and invalidates old completions; another owner cannot inherit them — Task3.
5. Removing cancellation/compensation lies must preserve actual successful atomic events/outbox; no unsupported server-wide safety claim — Task2/4.

## Task 0: fresh setup and ownership

- [ ] Fetch/record origin/dev and checkpoint ancestry; create branch from exact fetched SHA in suitable managed worktree; preserve reviewed docs.
- [ ] Read AGENTS/module instructions, spec, TDD/test skills; create ignored ledger. Record interface rulings and RED/GREEN evidence. No blanket solution test locally.
- [ ] Coordinator owns API middleware/body-limit tests, demo/E2E/docs/composition integration. Backend worker owns provider port/errors/provider implementation/ConfirmOrderHandler and affected C# provider/handler fakes/tests. Frontend worker owns Angular/API-client and their unit tests. Shared changes are coordinated, never reverted.

## Task 1: bounded in-memory booking bodies (coordinator)

Files: Api/Middleware/IdempotencyKeyMiddleware.cs; new safe body helper only if needed; noDB Host FlightsBookingBodyLimitTests (reuse FlightsApiFixture); nearest idempotency tests.

- [ ] RED exact16KiB and oversized16KiB+1, chunked/unknown length, UTF-8, case/trailing-slash route, missing auth/key, identical raw hash and body rewinding.
- [ ] Read at most cap+1 bytes before any EnableBuffering/hash/store/model binding; never file-spool. Restore original request body lifetime after next completes. 413 has safe error, zero store/bus/provider effects. Valid raw method/path/body fingerprint unchanged.
- [ ] GREEN selected noDB HTTP tests; existing database idempotency tests compile and run in CI only.

## Task 2: minimal provider and confirmation correctness (backend worker)

Files: Core/Providers/IFlightBookingProvider.cs, Core/Errors/FlightsErrors.cs; Infrastructure Duffel booking provider/DTOs and retry registration; Application/Handlers/Booking/ConfirmOrderHandler.cs; all affected provider fakes/tests (coordinate with Task1 files).

Interface: add `Task<ErrorOr<Success>> ValidateConfirmationAsync(string providerOrderId, Money expectedTotal, CancellationToken ct)`; change confirm signature to `ConfirmOrderAsync(string providerOrderId, PaymentRef payment, Money expectedTotal, CancellationToken ct)`. Keep ErrorOr<ConfirmedOrder>, no outcome class hierarchy. Remove unsupported supplier idempotency-key assumption; HTTP idempotency remains unchanged.

Codes: Flights.OrderPriceChanged (409, safe constant description); Flights.HoldOutcomeUnknown and Flights.ConfirmationOutcomeUnknown (503). Local HoldExpired/OfferExpired guards keep their pre-effect meaning. Supplier hold error after possibly sent POST is HoldOutcomeUnknown, never local OfferExpired. Preserve thrown cancellation semantics with sanitized chains.

- [ ] RED fake HTTP preflight exact ID/amount/currency/unpaid/deadline; zero wallet calls on preflight refusal. Compare again before POST; actual body uses accepted Money, correct `/air/payments` with nested payment under data.
- [ ] RED receipt requires succeeded, matching order_id/id/amount/explicit currency/type; pending/failed/cancelled/malformed/202/mismatch cannot confirm. GET/HEAD retries stay bounded; Duffel POST never auto retries even with a key.
- [ ] RED handler capture/provider failure: no OrderCancelled/Refunded/Confirmed, no automatic Refund/Cancel, status Held. On success retain existing PaymentAuthorized+OrderConfirmed+notification/version atomic commit. Update old tests that expected fabricated cancellation.
- [ ] Implement preflight before wallet; wrap post-capture provider errors conservatively as ConfirmationOutcomeUnknown. No new compensation workflow, persistent state or cross-restart dedup promise. Keep production TestOnly guard.
- [ ] GREEN unit/fake-HTTP locally; affected integration tests compile, actual database suites CI-only. Never call actual provider.

## Task 3: narrow memory-owned hold and conservative UI (frontend worker)

Files: FlightOrderOperationsService, booking panel/page/quote comparison, related TS client/error mapping and unit specs. No new endpoint/read DTO/ConfirmationState.

- [ ] RED one active hold across SPA navigation/new aggregate/criteria edit; frozen raw body+key; pre/post await owner/epoch guards; old response cannot hand result to new owner.
- [ ] Unknown hold AND confirm disable subsequent write retry, even with same key. Do not clear on late expiry/400/401/403/503 or post-effect conflict. First proved local pre-effect errors may be rejected. Known late success resolves only its own attempt. Keep B5 cancellation retry logic unchanged.
- [ ] Owner GET is viewing only, not proof original attempt/body succeeded or failed. Never suggest reload as unlocking. Clear PII on logout/destroy and horizon; retain current-tab unknown barrier appropriately, no storage/URL/history state.
- [ ] Enforce quote acceptance/expiry in methods; compare prior non-PII quote even after login/requote source:null; invalidate old responses by intent/owner.
- [ ] GREEN API-client/web tests and lint. Update tests intentionally asserting unknown hold/confirm POST retry; retain happy-path and cancel regressions. Report actual commands/evidence.

## Task 4: demo/regression/review/delivery (coordinator)

- [ ] Extend fictional E2E as needed for lost hold/confirm response and zero second POST after navigation; observed GET does not silently resolve original attempt. Keep external-network/auth refusal and no PII storage/log assertions.
- [ ] Update README/current-state/result and ADR0016/0019 caveats for truthful known/unknown handling and test-wallet limitation. All docs travel with implementation, including the reduced spec/plans.
- [ ] Run safe unit/HTTP/client/web/architecture/build/format/harness checks and demo; database/Host/Aspire only mandatory CI. Independent review of each worker task and whole diff; important findings receive regressions.
- [ ] Commit/push, PR+artifact, exact-head all mandatory CI success (paid eval skip), normal merge, clean primary dev sync, own branches/worktree/temp cleanup and generated hooks restored to primary path.
- [ ] Immediately proceed to B using a newly fetched merged dev SHA. A is not completion of M2.
