# WS4 Booking Consistency Implementation Plan

> **For agentic workers:** Execute only after user approval of this revised plan. Use `superpowers:test-driven-development` for source work and `migration-authoring` only after separate migration-generation authorization. No staging, commit, push, PR, deployment, live validation, or shared-data changes are authorized by this document.

**Goal:** Centralize Booking transition decisions in Core and make the EF order read model recover from committed Marten events through durable, versioned reconciliation.

**Architecture:** Booking writes use an expected stream version and commit events plus `ReconcileOrderReadModel(AggregateId)` through one enrolled `IMartenOutbox`. A durable consumer applies the ordered event suffix and saves EF data and `ProjectedStreamVersion` together. Incremental reconciliation, read-only validation, and exclusive-maintenance rebuild share one event applier.

**Tech Stack:** .NET 10; WolverineFx 6.17.0; Marten 9.14.0; EF Core 10.0.8; Npgsql.EntityFrameworkCore.PostgreSQL 10.0.1; PostgreSQL 17; ErrorOr; OpenTelemetry; xUnit v3; Shouldly; Testcontainers. The original baseline used Wolverine 5.13.0 and Marten 8.37.4; the security upgrade was separately authorized on 2026-09-22.

**Spec:** [Architecture remediation design](../specs/2026-08-11-ai-harness-architecture-remediation-design.md), D9, D10, WS4 and booking/projection flow §4.2; [ADR 0015](../../adr/0015-booking-aggregate-event-model.md); [ADR 0016](../../adr/0016-booking-saga-via-marten-es.md).

**Revision:** 2026-09-16, following the second review. Subsequent user approval authorized Tasks 1–5, committed/pushed as 4b5a483. On 2026-09-22 the user authorized Task 6 migration artifacts, the required security dependency upgrade, and then disposable migration application. See [review and execution evidence](../../operations/2026-09-22-ws4-dependency-upgrade-and-migration-review.md). These grants do not authorize shared/live application or deployment.

## Global constraints

- WS4 only; no WS5, frontend, Hotels, Rail, Trips, unrelated warnings, package upgrades, or provider/payment saga redesign.
- Existing isolated worktree: `D:/_Projects/_github/travel-agency/.worktrees/ws4-booking-consistency`; branch `codex/ws4-booking-consistency`; planning base `eb279591a1bfd04049f32713f86f900e875ee19c`.
- That base was fetched on 2026-08-26. Local HEAD was rechecked on 2026-09-16; this revision does not claim a fresh remote fetch. Before implementation, fetch `origin/dev`, compare it with the recorded base, and verify ancestry. If a new fresh base is needed, preserve this plan and create a clean branch/worktree from that ref; never substitute HEAD or rewrite an existing branch silently.
- Marten remains the booking source of truth. EF and its checkpoints are derived data.
- No real provider calls, deployment, live validation, or infrastructure mutation. Tests use fakes/WireMock and disposable resources only.
- One explicitly named **migration-artifact grant** may authorize EF migration source, its snapshot/Designer and idempotent SQL for review together. **Database application is a separate permission**, including disposable migration execution; neither is implied by approving a model change or this plan.
- Existing disposable fixtures can apply baseline migrations before Task 6. Their verification grant must therefore be obtained before the first such test in Tasks 2–5; it may be explicitly bundled with the source/test implementation grant. Applying the newly generated migration remains the later post-review gate in Task 6. No test may use shared/live connection strings.
- Production startup remains validation-only. The maintenance CLI is validation-only for schema in **every** environment.
- Source tasks use RED → minimal implementation → GREEN → refactor. Temporary coexistence of old/new services is allowed only where the new path is not yet invoked by production handlers.
- Semantic ADR amendments accompany their owning task. The last task verifies them; it does not postpone all documentation until the end.
- Every task ends with a compiling, reviewed, unstaged change. No intermediate deployment is proposed.

## Verified baseline and evidence map

The 2026-08-26 baseline run exited 0: 948 passed, 16 paid AI evals skipped; Flights integration was 181/181. Existing AD0001 analyzer warnings were observed. These are historical baseline results, not execution evidence for WS4 or a September rerun.

| Evidence | Current behavior / implication |
|---|---|
| `Core/Aggregates/BookingAggregate.cs` | Pure Apply methods; exception guards coexist with handler-side state checks. Re-quote Apply changes amount only. |
| `Application/Handlers/Booking/{QuoteOffer,HoldOffer,ConfirmOrder,CancelOrder}Handler.cs` | Existing-stream user commands use FetchForWriting. Confirm/Cancel still update EF after committing Marten. |
| `Application/Handlers/Webhooks/DuffelWebhookHandler.cs` | AggregateStreamAsync + unversioned Append; concurrent callbacks can violate guards. Missing EF correlation currently returns and is then marked processed. |
| `Infrastructure/Persistence/{OrderReadModelProjectorImpl,WebhookInboxStore}.cs` | EF has no stream checkpoint; owner/correlation depend on a row that may not exist after a crash. |
| `Application/Handlers/Notifications/*` | Email can return successfully on a missing EF row; SSE delivers status through an in-process connection registry. |
| `Api/Composition/FlightsModule.cs`, `apps/Travel.Host/Program.cs` | Host owns one Marten/Wolverine setup and global durability; module contributes mappings, routing, services, and scoped policies. |
| `tests/flights/.../Webhooks/DuffelWebhookHandlerTests.cs` | Explicitly proves Confirmed → Refunded; does not establish a requirement to receive ticketing first. |
| `tests/flights/.../Booking/{CancelOrderHandler,QuoteOfferHandler,ConfirmOrderOutbox}Tests.cs` | Pins repeated cancellation, cancellation from quoted state, re-quote pricing, and existing notification outbox behavior. |
| Git `fa030d75`, `bb6cd5d5`, `33786f34`, `3af633d5` | Expected-version user writes, explicit Marten outbox enrollment, terminal webhook handling, event-derived timestamps. None establishes a Ticketed-only refund requirement. |

Paths in the first column are relative to `modules/flights/Travel.Modules.Flights.` plus the named layer, unless already rooted at `tests/` or `apps/`. Exact implementation/test paths are listed per task.

## Decisions to approve

The following are recommendations for the revised implementation, not claims about current behavior.

### D1 — transition matrix and HTTP mapping

Core returns `Allowed`, `IdempotentNoOp`, or `Rejected` with a domain reason. Replay Apply methods remain unconditional; replay must not re-run today's transition decisions against historical events.

| Operation/state | Evidence and alternatives | Recommended decision |
|---|---|---|
| ReQuote / OfferQuoted | Existing handler permits refresh even after expiry. | Allowed; persist the refreshed quote snapshot as described in D6. Other states → InvalidState/409. |
| Hold / OfferQuoted | Current guard rejects expiry before provider call. | Allowed only before expiry. Expiry boundary is `ExpiresAt <= now`; OfferExpired retains Validation/400. |
| Confirm / Held | Current handler ignores HeldUntil; the aggregate stores it as ExpiresAt. | **Proposed behavior change:** HoldExpired/409 before payment/provider calls. Alternative: retain provider-authoritative expiry. Requires explicit approval. |
| Repeat Hold/Confirm | Current API idempotency middleware replays a matching request; a fresh duplicate command is rejected by state. | Preserve that separation; no new domain success/no-op for these commands. |
| Cancel / Held or Confirmed | Existing code and M1 acceptance support it. | Allowed, subject to owner validation. Preserve current best-effort provider cancellation; WS4 does not redesign compensation. |
| Cancel / Cancelled or Refunded | Current handler returns current state; explicit test exists for Cancelled. | IdempotentNoOp after matching-owner validation; 200 with command-derived current state, no new provider/event/notification. |
| Cancel / Ticketed | Current guard/history explicitly prohibit it. | Rejected(OrderAlreadyTicketed), OrderNotCancellable/409. |
| Cancel / OfferQuoted | Current integration test permits it; the M1 acceptance names Held/Confirmed, and the quoted stream is anonymous. | **Proposed behavior change:** Core rejects InvalidState. The owner-first HTTP boundary returns 404 for today's ownerless quoted stream; it does not expose state or assign an owner. Alternative: retain quote cancellation with a separately specified ownership contract. Do not justify this solely by omission from a diagram. |
| Ticket / Confirmed | Existing transition. | Allowed. |
| Ticket / Ticketed, Cancelled, Refunded | Existing terminal duplicate behavior. | IdempotentNoOp; mark inbox processed after the decision. |
| Ticket / Held | Callback may precede the local confirmation commit. | Rejected(PrerequisiteNotMet); retry without ProcessedAt. |
| Refund / Confirmed or Ticketed | Confirmed → Refunded is already integration-tested. Callback order is not domain order. | Allowed in both states. Do not require a ticketing callback as evidence for refund. |
| Refund / Cancelled or Refunded | Existing terminal no-op. | IdempotentNoOp. |
| Refund / Held | Current guard permits it, but no focused business test justifies a financial refund before confirmation. | **Proposed tightening:** PrerequisiteNotMet with bounded retry. Alternative: retain existing acceptance; settle explicitly before implementation. |
| Ticket/Refund / None or OfferQuoted | Current refund guard is broader than the established order lifecycle. | **Proposed tightening:** Rejected(InvalidState); retain the message in DLQ for investigation, not processed-success. No synthetic amount/currency fallback. |
| Missing EF provider-order correlation | Can be projection lag, an early callback, or an unknown order. | Application `BookingCorrelationNotReadyException`; bounded durable retry, then DLQ; never mark processed merely because the lookup is empty. |
| Missing/mismatched owner on Confirm/Cancel | The stream currently has no owner; D2 adds it. | Fail closed with the existing not-found HTTP shape, before provider calls or returning terminal state. |

The confirmed→refunded behavior is preserved. The explicitly marked changes (expiry, quoted cancellation, early/invalid refund states) are approval items. Hold materialization and notification suppression in D3/D5 are also product-visible changes; do not describe expiry as the only new business rule.

### D2 — replayable ownership without a new event type

Recommended: add trailing optional `Guid? OwnerUserId = null` to `OfferHeld`. New successful holds supply the authenticated user; the aggregate replays ownership from that event. The existing event identity and eight event types stay unchanged.

Compare the actual alternatives:

- **Add the field to OfferHeld (recommended):** ownership becomes a fact of the existing authenticated hold operation; smaller event/model surface; old payloads deserialize with null. Old-reader tolerance of the additive field must be tested, not assumed.
- **Separate BookingOwnerAssigned event:** useful if assignment/transfer is an independently modeled business operation. It introduces a ninth event and an additional old-reader compatibility problem; no such operation is required for WS4.
- **Owner only in reconcile messages or EF:** insufficient for rebuilding from an empty read model; not a valid solution to D9.

Legacy policy:

- Ownerless OfferQuoted is valid and non-materialized; a subsequent Hold may establish the owner.
- Ownerless Held has no reliable owner in current storage in general. Confirm/Cancel fail closed; do not claim the order for the next caller.
- Legacy Confirmed/Ticketed/Cancelled/Refunded may have an EF owner, but that does not make it a stream fact. Validate reports the missing source field.
- No backfill is part of this source plan. A live audit may establish that legacy data is disposable, or require a separate audited data-repair design. Do not rewrite historical JSON or infer ownership from passenger email/idempotency TTL data.
- Before an otherwise Allowed ticket/refund webhook appends to an order-bearing stream, require a nonempty recorded OwnerUserId. An ownerless legacy stream raises `BookingSourceOwnershipMissingException` and goes to DLQ **before** event/notification publication; leave ProcessedAt null. This avoids an unconstructible ticket notification or a falsely successful unrebuildable write. A terminal webhook IdempotentNoOp may still acknowledge the already-recorded fact without creating any notification. No Guid.Empty or EF-owner fallback is allowed.

### D3 — notification dependencies and the limited SSE guarantee

Keep existing OrderConfirmed, user OrderCancelled and OrderTicketed messages in the **same Marten outbox** as the event and reconcile message. Add trailing optional `long? RequiredStreamVersion = null` for wire compatibility with already-persisted old messages. New senders always set a positive exact version; explicit zero/negative values are terminal-invalid.

For new messages, consumers require `EF.ProjectedStreamVersion >= RequiredStreamVersion`. For old messages with null, read the current Marten source version and require EF to reach that captured version. Do not let a deserializer's default zero bypass the dependency check.

- Missing/behind EF throws `BookingReadModelNotReadyException` before email/SSE effects.
- Recommended email policy: send confirmation when the currently read row is Confirmed/Ticketed; suppress it when Cancelled/Refunded. Send cancellation only when currently Cancelled. This coalesces obsolete notifications and requires approval; it is not an atomic promise about a status that may change while SMTP is executing.
- Wolverine notification processing is at-least-once; SMTP may duplicate after a crash. SSE itself is **best effort for an active connection**, not durable delivery or replay.
- Add stream version to the internal SSE event and emitted JSON. Each active connection keeps its last successfully enqueued version. Compare, channel write and version advancement occur under that connection's lock; drop older/equal versions and release this state when the connection unregisters.
- Keep no eternal order→high-water dictionary. A connection registered later may receive the same current version that an earlier connection already saw.
- State mapping: a confirmation notification observing Confirmed emits OrderConfirmed; observing Ticketed emits OrderTicketed. A ticket notification emits only for current Ticketed; user-cancel notification emits only for current Cancelled. Suppress delayed confirmation/ticket messages for Cancelled/Refunded; do not introduce a refund SSE event.
- On reconnect, GET is the source for the current snapshot. No Last-Event-ID replay, cross-node fan-out, or frontend changes are included. Cross-connection monotonicity is not claimed.

### D4 — failure-policy ownership and recovery contract

Host retains global storage, transport, durability and transaction defaults. Flights registers an `IHandlerPolicy` scoped to the concrete reconcile/webhook/notification message types and their named failure classes.

Retry schedules are bounded operating defaults, not delivery-order guarantees:

- Reconcile transient storage errors: 1s, 5s, 30s, then DLQ.
- Webhook optimistic-write conflict: scheduled retries after 100ms, 500ms, 1s, then DLQ, with a **new scope/session and decision** each attempt.
- Correlation/read-model/prerequisite lag: 2s, 10s, 1m, 5m, then DLQ.
- Known terminal failure: immediate DLQ. Unknown reconcile exceptions: DLQ with safe error classification; never discard silently.
- Known transient database failures during webhook inbox reads/ProcessedAt writes or notification readiness reads use the same classifier and 1s/5s/30s storage schedule. They must not bypass the policy merely because they arrive as raw DbUpdateException/NpgsqlException instead of a projection wrapper.
- Longer dependency delays improve normal recovery but do not prove causal ordering under overload/restart. Once retries exhaust, the explicit operator recovery flow below applies.

### D5 — order visibility

Recommended: materialize EF at successful Hold, when the stream has owner, passenger, provider order reference and HeldAt. This changes GET/List visibility and requires approval.

Quote/re-quote commits still atomically publish reconciliation. They return a successful `NotMaterialized` result without an EF row; they do not persist a per-offer checkpoint. Alternatives are preserving Confirm-time visibility or adding a separate offer projection; the latter is outside WS4.

### D6 — re-quote expiry must survive replay

The current `OfferReQuoted` only persists a price change; returning a refreshed expiry in the HTTP response does not update the aggregate. A later Hold therefore still sees the old expiry.

Recommended scoped correction: extend the existing event with trailing optional `BookableOffer? RefreshedOffer = null` using the existing **Core** domain offer, not a supplier DTO.

- New re-quotes persist the full refreshed offer; OfferId/NewAmount must match that snapshot.
- Apply uses that snapshot for OfferId, itinerary, amount, ExpiresAt, ProviderOfferRef and fare conditions. The HTTP response and subsequent Hold must describe the same offer.
- Old events lacking the snapshot retain historical amount-only replay. Do not synthesize a new historical expiry.
- Proposed re-quote identity contract: refresh the existing provider offer reference; a different itinerary/offer selection starts a new quote stream. If changing the reference on an existing stream is a required workflow, decide that explicitly instead of silently changing this rule.
- Alternative: leave amount-only behavior as a named limitation and defer the correction. In that case do not claim that re-quote repairs expired offers; this plan's recommended acceptance uses the correction.

## Consistency contracts

### Write-side transaction and webhook replay

All existing-stream writes, including both webhook event branches, load with `FetchForWriting<BookingAggregate>`. Core decisions are made against that loaded version; append, reconcile message and existing notification messages commit through its enrolled IMartenOutbox. Enrollment happens before **any** message publication, including sibling notifications; enrolling only just before Save cannot recover a notification that was already sent.

Use one low-level commit primitive that **throws** write conflicts. HTTP command boundaries map a conflict to ErrorOr/409. A background webhook lets a classified conflict escape so Wolverine retries; it must never swallow a returned ErrorOr and then mark ProcessedAt.

The EF inbox processed flag is a later idempotent acknowledgement, not part of a falsely claimed cross-ORM transaction:

1. Marten event + reconcile + notification commit atomically.
2. Only then mark the EF inbox processed.
3. If marking processed fails, retry sees the now-advanced stream, obtains terminal IdempotentNoOp, and marks the inbox without re-appending/re-publishing.
4. Test a crash in this exact interval. ProcessedAt is never written before a successful commit or an approved no-op.

No-op correlation-free/malformed-payload handling remains explicitly separate from a rejected domain transition. No extra domain event is appended to “record a retry.”

This transaction does not include external provider/payment effects that currently execute before the Marten commit. WS4 preserves the existing stable confirm idempotency keys and compensation paths; it does not claim to close the provider-success-before-Marten-crash window or make concurrent Hold calls externally exactly-once. The no-repeated-provider assertion in Task 10 applies specifically to repairing an already committed booking through reconciliation.

### Incremental, Validate and Reset have different failure rules

One pure `OrderReadModelEventApplier` owns event-to-row mapping and supports old/new event payloads. It handles PaymentAuthorized even if only the checkpoint changes. Core Apply remains the write-model replay; neither projection applies current command guards to history.

Incremental:

1. Load the EF row and its original concurrency token. Absent row starts with an in-memory version 0; do not insert a visible incomplete placeholder.
2. A persisted -1 means legacy/untrusted checkpoint: retain it and report a bootstrap-required failure.
3. Fetch Marten target version T, then the bounded suffix [persisted+1, T].
4. Check stream identity/type, exact contiguous versions and final version T. Unknown events or unreadable source data must not be skipped while advancing the checkpoint.
5. Apply the suffix to the existing row/accumulator. Validate owner consistency against source facts.
6. Save all fields and checkpoint T in one EF transaction, using original ProjectedStreamVersion as the concurrency token. Preserve row Id; a unique AggregateId insert conflict is a retryable competing creation.
7. On any failed save dispose the scope; the next durable attempt reloads EF and Marten. Never retry a poisoned DbContext/tracked row in place.
8. If persisted == T, no-op. If persisted > T, terminal diagnostic requiring maintenance repair. Events committed after T have their own durable reconcile message.

Validate:

- No EF writes, SaveChanges, messages, provider calls or schema application.
- Read a snapshot, replay from version 1 into a fresh accumulator using the same applier, and compare against the existing row.
- Report separately: invalid/unreadable **source**, missing historical owner, missing EF row, sentinel, checkpoint ahead, and derived-field mismatch.
- Row mismatch/checkpoint ahead are repairable by Reset if source is valid. Ownerless quote-only streams are valid and non-materialized.

Reset:

- Callable only through the exclusive-maintenance runner, after all normal booking/projection writers are drained/stopped.
- Rebuild from source version 1, ignoring existing EF values as authority. Valid source may repair a wrong EF owner, corrupt fields, or an ahead checkpoint.
- Replace one row atomically, preserving its Id when present. On failure retain the prior row; no global truncate/drop/delete.
- If valid source consists only of quote/re-quote events and should have no materialized row, Validate reports any spurious EF row; Reset deletes only that exact AggregateId's derived row atomically. Preview/report this deletion. A missing/unreadable source is not proof for deleting a row. No source event, inbox entry or other order is deleted.
- Reset may lower an invalid EF checkpoint; monotonicity is an **Incremental** invariant, not a ban on repairing corrupt derived data.
- Missing historical owner/unsupported source cannot be “repaired” by guessing. Return a failed item for an explicit data decision.
- Reset emits no notifications and does not mark pending webhooks processed.

A stream-version token alone cannot detect a same-version Reset rewrite. This plan deliberately requires maintenance exclusion instead of promising online rebuild safety.

### Maintenance execution boundary

Use the existing Host executable with a thin Api-facade entry point, but select maintenance mode **before** starting the web server, initializers or Wolverine consumers. Register only the required persistence/diagnostic services through the module facade; keep process-owned Marten/Wolverine storage configuration in Host. No new Composition assembly.

Proposed CLI:

```text
dotnet run --project apps/Travel.Host -- booking-read-model validate
dotnet run --project apps/Travel.Host -- booking-read-model rebuild --execute --exclusive-maintenance
dotnet run --project apps/Travel.Host -- booking-read-model inspect --aggregate-id <guid>
dotnet run --project apps/Travel.Host -- booking-read-model replay --message-id <guid> --execute
```

These are implementation targets, not commands to run now.

- Validate/rebuild/inspect do not start listeners, workers, provider clients, email or live notification publication. Schema is checked read-only in all environments.
- Rebuild without both flags refuses mutation. The exclusivity flag acknowledges an operator prerequisite; it does **not** prove other nodes are stopped. Deployment procedure must stop/drain all old and new booking/projection writers before Reset.
- Unit/integration tests prove the CLI process itself starts no consumers or schema writers. A separate coordinated disposable test proves Reset runs after in-flight Incremental completes and workers resume only after reset finishes.
- No online/concurrent Reset guarantee is offered. If operationally required later, design a separate fencing/revision mechanism.
- Runner progress is per stream, with counts and safe failure codes. Continue independent streams and exit non-zero on partial failure. A new run safely revalidates/rebuilds; no second projection implementation or progress table.
- Replay marks only explicitly selected allowed booking-consistency envelopes replayable through Wolverine's API. It never clears ProcessedAt or replays user booking commands.

## Schema, rollout and rollback

### Physical and serialized-contract delta

| Surface | Delta / compatibility proof |
|---|---|
| EF order_read_model | Add `projected_stream_version bigint NOT NULL DEFAULT -1`; map as concurrency token, ValueGeneratedNever. New code writes an explicit applied version. |
| Existing EF rows | -1 means checkpoint not yet established. Validate + exclusive Reset is required before these rows can be trusted. |
| OfferHeld | Add optional OwnerUserId; keep event identity. Old payload→new reader and new payload→actual old reader tests required. |
| OfferReQuoted | Add optional Core RefreshedOffer snapshot; keep event identity. Same two-way payload compatibility tests; legacy amount-only semantics retained. |
| Notification envelopes | Add optional RequiredStreamVersion; missing legacy value must take the explicit source-version compatibility path. |
| ReconcileOrderReadModel | New internal durable message; old binary has no handler. This is a downgrade boundary independent of EF schema compatibility. |

- No migration source/snapshot/SQL is generated in this planning turn.
- EF schema expansion requires the explicit migration-artifact grant (source/snapshot/Designer plus review SQL), review of Up/Down/idempotent SQL, and a separate disposable-application grant for upgrade evidence. Review lock/statement timeouts; the constant default does not eliminate the table-lock gate.
- The sentinel detects legacy/untrusted rows; it is **not** justified by replay-generated notifications, because this design never derives notifications from rebuild.
- Existing schema compatibility does not imply writer compatibility. Old projector updates can corrupt data while retaining a new checkpoint; old/new booking writers must not overlap.
- No automatic migration or owner backfill is introduced.

### Downgrade policy

The supported default after activation is **forward repair or rollback to a tested WS4-compatible artifact**, not a blind return to eb279591.

A compatible rollback artifact must still understand the additive event fields, enforce stream ownership, retain the reconcile handler and notification compatibility path, and preserve expected-version writes. Test it against new-format persisted data and pending envelopes.

A pre-WS4 binary lacks the new consumer and owner checks even if it tolerates the additive JSON fields. Resetting EF to -1 does not make that binary safe. An emergency pre-WS4 downgrade needs a separately reviewed procedure covering stopped writers/consumers, pending envelopes, security semantics and recovery; it is not an implemented feature or permission of WS4.

Keep the additive EF column during a compatible binary rollback. Destructive Down is not a routine rollback action. After any authorized old-writer interval, run read-only validation and exclusive Reset before returning to normal new writers; never trust inherited checkpoints blindly.

### Operator recovery after retry exhaustion

1. Inspect safe aggregate/checkpoint and exact DLQ envelope IDs; distinguish projection storage failure, missing owner/source anomaly and callback prerequisite lag.
2. Repair the cause. Reconcile transient failure can normally replay incrementally; sentinel/corrupt EF uses exclusive Validate+Reset. Invalid source/unknown owner requires an explicit data decision.
3. Recover projection first, verify the persisted checkpoint against the captured source target, then mark the selected dependent webhook/notification envelopes replayable.
4. Resume workers and let them reload/re-decide. A webhook may append a valid new event and create a new reconcile message; drain/verify this second cycle too.
5. Prove pending/delayed/DLQ disposition. Do not delete DLQ rows to make health green. A notification no longer relevant may be consumed by its approved suppression rule.
6. Never repeat Confirm/Hold/Cancel to repair projection. Provider/payment side effects must not be used as a recovery trigger.

Automatic convergence is proven for retryable failures within the durable recovery policy. Terminal/exhausted failures remain visible and require this operator path; indefinite automatic success is not claimed.

## TDD implementation tasks

Each task runs its listed focused command once for RED on the new behavior, then again for GREEN after the minimal change. Already-correct characterization tests may start green. File paths below are exact proposed ownership, not current implementation claims.

### Task 1 — typed Core transition decisions

**Owner:** Core.

**Files:**

- Create `modules/flights/Travel.Modules.Flights.Core/Aggregates/BookingTransitionDecision.cs`.
- Modify `modules/flights/Travel.Modules.Flights.Core/Aggregates/BookingAggregate.cs`.
- Modify/remove after callers migrate `modules/flights/Travel.Modules.Flights.Core/Exceptions/InvalidBookingStateException.cs`.
- Create `tests/flights/Travel.Modules.Flights.Tests.Unit/Aggregates/BookingTransitionDecisionTests.cs`.
- Modify `tests/flights/Travel.Modules.Flights.Tests.Unit/Aggregates/BookingAggregateApplyTests.cs`.
- Amend `docs/adr/0015-booking-aggregate-event-model.md` for the approved matrix in this task.

**Contract:** decision records below and `DecideReQuote(string providerOfferRef)`, `DecideHold(DateTimeOffset now)`, `DecideConfirm(DateTimeOffset now)`, `DecideCancel()`, `DecideTicket()`, `DecideRefund()`. The approved same-reference rule returns OfferReferenceMismatch from Core rather than a handler-side duplicate domain guard. Ownership is a separate Core decision added in Task 2 and is invoked before returning command state.

```csharp
public enum BookingTransition { ReQuote, Hold, Confirm, Cancel, Ticket, Refund }
public enum BookingRejectionCode
{
    InvalidState, OfferExpired, HoldExpired, OrderAlreadyTicketed,
    PrerequisiteNotMet, OwnerMissing, OwnerConflict, OfferReferenceMismatch
}
public sealed record BookingRejection(
    BookingTransition Transition, BookingRejectionCode Code, BookingStatus Status);
public abstract record BookingTransitionDecision
{
    public sealed record Allowed : BookingTransitionDecision;
    public sealed record IdempotentNoOp(BookingStatus Status) : BookingTransitionDecision;
    public sealed record Rejected(BookingRejection Reason) : BookingTransitionDecision;
}
```

- [ ] RED: full operation×state matrix, equality at expiry, Confirmed refund without any ticket event, terminal no-ops, repeated Confirm rejection.
- [ ] Implement decisions from D1 only; keep Apply pure and retain old Guard entry points until their test/caller replacements compile.
- [ ] GREEN: preserve replay of historical sequences even if new commands would reject them; decisions must not mutate state.

```powershell
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit/Travel.Modules.Flights.Tests.Unit.csproj --filter "FullyQualifiedName~BookingTransitionDecisionTests|FullyQualifiedName~BookingAggregateApplyTests"
```

### Task 2 — replayable ownership and event compatibility

**Owner:** Core payload/aggregate, Infrastructure Marten mapping, integration tests.

**Files:**

- Modify `modules/flights/Travel.Modules.Flights.Core/DomainEvents/OfferHeld.cs`.
- Modify `modules/flights/Travel.Modules.Flights.Core/Aggregates/BookingAggregate.cs`.
- Verify/update `modules/flights/Travel.Modules.Flights.Infrastructure/Marten/BookingAggregateConfig.cs` without creating a ninth event.
- Create `tests/flights/Travel.Modules.Flights.Tests.Integration/Marten/BookingEventCompatibilityTests.cs`.
- Modify `tests/flights/Travel.Modules.Flights.Tests.Unit/DomainEvents/DomainEventsTests.cs`.
- Modify `tests/flights/Travel.Modules.Flights.Tests.Unit/Composition/FlightsModuleRegistrationTests.cs`.
- Amend `docs/adr/0015-booking-aggregate-event-model.md` for owner semantics/legacy constraints.

**Contract:** `OfferHeld(..., Guid? OwnerUserId = null)`, `BookingAggregate.OwnerUserId`, and `DecideOwner(BookingTransition transition, Guid userId)`. For Hold on an unowned quoted stream, matching a valid nonempty input may establish ownership; Confirm/Cancel require an existing matching owner. All ownership decisions are pure.

- [ ] RED: old JSON without the field replays null; new JSON replays exact owner; empty/mismatched owner is rejected and cannot produce a terminal-state response.
- [ ] Extend the existing event, retaining its `OccurredAt => HeldAt` contract and type identity.
- [ ] Verify old-reader tolerance using isolated pre-change Core/Marten binaries built from the recorded baseline in a temporary test fixture; do not replace them with a permissive “legacy simulator” for compatibility proof.
- [ ] GREEN: eight event registrations remain intact; old payloads are not guessed/backfilled.

```powershell
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit/Travel.Modules.Flights.Tests.Unit.csproj --filter "FullyQualifiedName~BookingTransitionDecisionTests|FullyQualifiedName~DomainEventsTests|FullyQualifiedName~FlightsModuleRegistrationTests"
dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration/Travel.Modules.Flights.Tests.Integration.csproj --filter "FullyQualifiedName~BookingEventCompatibilityTests"
```

### Task 3 — persist the refreshed quote used by the next Hold

**Owner:** Core event/replay and Application re-quote orchestration.

**Files:**

- Modify `modules/flights/Travel.Modules.Flights.Core/DomainEvents/OfferReQuoted.cs`.
- Modify `modules/flights/Travel.Modules.Flights.Core/Aggregates/BookingAggregate.cs`.
- Modify `modules/flights/Travel.Modules.Flights.Application/Handlers/Booking/QuoteOfferHandler.cs`.
- Modify `tests/flights/Travel.Modules.Flights.Tests.Unit/Aggregates/BookingAggregateApplyTests.cs`.
- Modify `tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/QuoteOfferHandlerTests.cs`.
- Modify `tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/HoldOfferHandlerTests.cs`.
- Extend `tests/flights/Travel.Modules.Flights.Tests.Integration/Marten/BookingEventCompatibilityTests.cs`.
- Amend `docs/adr/0015-booking-aggregate-event-model.md` for D6.

**Contract:** trailing optional `BookableOffer? RefreshedOffer = null` on OfferReQuoted; old fields/type identity/OccurredAt remain. Use the refreshed ID/amount consistently in new payloads and returned results.

- [ ] RED: quote expires; a fake provider returns later expiry, revised price/fare data; re-quote, dispose session, replay, then Hold before the new expiry succeeds using the same persisted snapshot.
- [ ] Add old amount-only replay and new-payload/old-reader tests; pin the approved same-reference rule through Core's decision and its Application Conflict/409 mapping. New event construction keeps snapshot Id/amount consistent with the existing fields; inconsistent persisted payloads are reported by Validate/projection as a source-payload error. Do not add exception guards to Core Apply.
- [ ] Persist and Apply the new snapshot; legacy payloads continue to update only amount.
- [ ] GREEN: compare HTTP command result, replayed aggregate and captured fake Hold input; no real provider calls.

```powershell
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit/Travel.Modules.Flights.Tests.Unit.csproj --filter "FullyQualifiedName~BookingAggregateApplyTests"
dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration/Travel.Modules.Flights.Tests.Integration.csproj --filter "FullyQualifiedName~QuoteOfferHandlerTests|FullyQualifiedName~HoldOfferHandlerTests|FullyQualifiedName~BookingEventCompatibilityTests"
```

### Task 4 — consume Core decisions and serialize webhook decisions with writes

**Owner:** Application orchestration/HTTP error mapping, thin Api input/output.

**Files:**

- Create `modules/flights/Travel.Modules.Flights.Application/Booking/BookingTransitionErrorMapper.cs`.
- Create `modules/flights/Travel.Modules.Flights.Application/Booking/BookingConsistencyExceptions.cs`.
- Modify `modules/flights/Travel.Modules.Flights.Application/Commands/HoldOfferCommand.cs` and `CancelOrderCommand.cs`.
- Modify `modules/flights/Travel.Modules.Flights.Application/Handlers/Booking/QuoteOfferHandler.cs`, `HoldOfferHandler.cs`, `ConfirmOrderHandler.cs`, `CancelOrderHandler.cs`.
- Modify `modules/flights/Travel.Modules.Flights.Application/Handlers/Webhooks/DuffelWebhookHandler.cs`.
- Modify `modules/flights/Travel.Modules.Flights.Application/Persistence/DocumentSessionExtensions.cs`.
- Modify `modules/flights/Travel.Modules.Flights.Application/Webhooks/IWebhookInboxStore.cs` and `modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/WebhookInboxStore.cs`.
- Modify `modules/flights/Travel.Modules.Flights.Api/Endpoints/HoldOfferEndpoint.cs`, `CancelOrderEndpoint.cs`, and `modules/flights/Travel.Modules.Flights.Api/Contracts/Contracts.cs`.
- Modify the existing handler tests; create `tests/flights/Travel.Modules.Flights.Tests.Integration/Webhooks/BookingWebhookConcurrencyTests.cs` and `tests/flights/Travel.Modules.Flights.Tests.Unit/Handlers/BookingTransitionErrorMapperTests.cs`.
- Modify `tests/Travel.Host.Tests.Integration/Flights/FlightsEndpointsHttpTests.cs`.
- Amend ADR 0015/0016/0018 for the decision/processed-flag boundary as it changes.

**Contracts:**

- `HoldOfferCommand(Guid AggregateId, Guid UserId, PassengerInfo Passenger)`; authenticated identity is passed by the endpoint.
- `CancelledOrderResult` carries a command-owned current order snapshot, sufficient for the existing OrderResponse. No post-command GetOrderQuery.
- Typed exceptions: `BookingWriteConflictException`, `BookingCorrelationNotReadyException`, `BookingTransitionPrerequisiteException`, `BookingTransitionRejectedException`, `BookingSourceOwnershipMissingException`. Domain rejection remains a value in Core; Application chooses HTTP mapping or durable retry/DLQ behavior.
- In this task keep the old projector temporarily; no reconcile message is routed before its consumer exists.

- [ ] RED: owner/input/error mapping, ownerless quoted Cancel returns 404, approved expiry decisions, repeat owned terminal cancel, and cancel response without EF query. Assert owner validation precedes any state-bearing response; Core InvalidState maps to 409 only after ownership passes.
- [ ] RED: barrier-controlled concurrent duplicate ticket/refund, distinct inbox IDs for the same transition, ticket-vs-cancel race. Both handlers must load before either saves.
- [ ] Replace status guards with decision handling; new Hold writes OwnerUserId in OfferHeld.
- [ ] Before an Allowed webhook write, require the recorded owner and construct its ticket notification from that owner. RED/GREEN legacy ownerless Confirmed ticket/refund tests must assert no appended event, no outgoing message and ProcessedAt still null; never fall back to EF ownership/Guid.Empty. Terminal no-op callbacks retain their acknowledged/no-new-message behavior.
- [ ] Use FetchForWriting in both webhook branches. A stale write throws a classified conflict and leaves ProcessedAt null; on fresh replay re-evaluate the domain decision. Move the existing ticket notification into the enrolled Marten outbox before Save in this task, so a crash immediately after commit cannot lose that notification; there is still no reconcile message until Task 9.
- [ ] RED/GREEN: inject failure after Marten commit and before inbox MarkProcessed; replay marks processed with unchanged stream/event/notification counts.
- [ ] Separate HTTP conflict→409 from background conflict→throw; no background branch consumes an ErrorOr then acknowledges success.

```powershell
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit/Travel.Modules.Flights.Tests.Unit.csproj --filter "FullyQualifiedName~BookingTransitionErrorMapperTests"
dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration/Travel.Modules.Flights.Tests.Integration.csproj --filter "FullyQualifiedName~BookingWebhookConcurrencyTests|FullyQualifiedName~DuffelWebhookHandlerTests|FullyQualifiedName~HoldOfferHandlerTests|FullyQualifiedName~ConfirmOrderHandlerTests|FullyQualifiedName~CancelOrderHandlerTests"
dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --filter "FullyQualifiedName~FlightsEndpointsHttpTests"
```

### Task 5 — add the tested atomic commit primitive and reconcile port

**Owner:** Application.

**Files:**

- Create `modules/flights/Travel.Modules.Flights.Application/ReadModels/ReconcileOrderReadModel.cs`.
- Create `modules/flights/Travel.Modules.Flights.Application/ReadModels/IOrderReadModelReconciler.cs`.
- Extend `modules/flights/Travel.Modules.Flights.Application/Persistence/DocumentSessionExtensions.cs`.
- Create `tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/BookingCommitOutboxTests.cs`.
- Update `tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/SharedFakes.cs` as needed.

**Contract:**

```csharp
public sealed record ReconcileOrderReadModel(Guid AggregateId);
public enum OrderReadModelReconcileMode { Incremental, Reset }
public sealed record ReconcileResult(
    Guid AggregateId, long? PreviousVersion, long SourceVersion,
    long? PersistedVersion, int AppliedEvents, bool Materialized);
public sealed record ProjectionIssue(string Code, bool RepairableByReset);
public sealed record ProjectionValidation(
    Guid AggregateId, long? SourceVersion, long? PersistedVersion,
    bool WouldMaterialize, IReadOnlyList<ProjectionIssue> Issues);
public interface IOrderReadModelReconciler
{
    Task<ReconcileResult> ReconcileAsync(
        Guid aggregateId, OrderReadModelReconcileMode mode, CancellationToken ct);
    Task<ProjectionValidation> ValidateAsync(Guid aggregateId, CancellationToken ct);
}
```

Low-level helper: `Task SaveBookingWithReconcileAsync(IDocumentSession session, IMartenOutbox outbox, Guid aggregateId, IReadOnlyList<object> notifications, CancellationToken ct)`. It enrolls the supplied session first, buffers the supplied sibling notifications and one reconcile message, then saves. It translates Marten's `EventStreamUnexpectedMaxEventIdException` into `BookingWriteConflictException` while retaining the inner cause; it never returns a success-shaped result for a failed write. HTTP conflict-to-ErrorOr translation belongs outside this helper. Callers pass an empty notification list for commit paths without an existing notification.

- [ ] RED: event+reconcile commit together; pre-save exception and stale expected version commit neither; existing sibling notification participates in that same transaction.
- [ ] Assert no notification is published before enrollment, and test the wrapper exception through a real stale expected-version write rather than throwing the wrapper directly.
- [ ] Implement the helper and test with real PostgreSQL/Wolverine storage.
- [ ] Do not switch production handlers yet; test-only probe consumers establish the primitive without changing runtime routing.

```powershell
dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration/Travel.Modules.Flights.Tests.Integration.csproj --filter "FullyQualifiedName~BookingCommitOutboxTests|FullyQualifiedName~MartenWolverineOutboxTests"
```

### Task 6 — model checkpoint and separately authorized migration verification

**Checkpoint 2026-09-22:** artifact generation and disposable application were separately
approved. Metadata, fresh migration, baseline upgrade, idempotent SQL replay, explicit zero
and positive checkpoints, query mapping, stale-writer protection and Host schema/readiness
checks passed. Shared/live application remains outside this checkpoint.

**Owner:** Infrastructure persistence.

**Files:**

- Modify `modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/Entities/OrderReadModelEntity.cs`.
- Modify `modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/Configurations/OrderReadModelConfig.cs`.
- Modify `modules/flights/Travel.Modules.Flights.Application/Queries/OrderQueries.cs`.
- Modify `modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/OrderReadModelQueries.cs`.
- Create `tests/flights/Travel.Modules.Flights.Tests.Integration/Persistence/OrderReadModelMigrationTests.cs` (metadata) and `OrderReadModelMigrationDatabaseTests.cs` (disposable application); modify `FlightsDbContextTests.cs` in that directory.
- Only after permission, generate `Persistence/Migrations/<generated timestamp>_AddOrderReadModelProjectedStreamVersion.cs`, its Designer and FlightsDbContextModelSnapshot in the Infrastructure project.

- [x] RED/GREEN model tests: bigint, default -1, ValueGeneratedNever, concurrency token and unique aggregate index. Use the disposable model-created fixture only if its database creation is authorized.
- [x] Stop before generating artifacts; obtain the explicitly named grant: “generate migration source, snapshot/Designer and idempotent SQL for review; do not apply to a database.” Approval of source implementation alone does not satisfy this gate.
- [x] Only after that migration-artifact grant, use module-owned runtime/design-time configuration for both commands:

```powershell
dotnet tool restore
dotnet ef migrations add AddOrderReadModelProjectedStreamVersion --project modules/flights/Travel.Modules.Flights.Infrastructure --context FlightsDbContext --output-dir Persistence/Migrations
dotnet ef migrations script --idempotent --project modules/flights/Travel.Modules.Flights.Infrastructure --context FlightsDbContext --output artifacts/ws4-booking-projection-migration.sql
```

- [x] Review Up (add only this column/default), Down (drop only this column), snapshot and SQL locking/data preservation.
- [x] After disposable execution authorization: fresh migration and upgrade from FlightsM1Init leave old rows at -1; an explicit new-code value is saved rather than replaced by the DB default. No incomplete zero-version row is committed as an implementation convenience.

```powershell
dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration/Travel.Modules.Flights.Tests.Integration.csproj --filter "FullyQualifiedName~OrderReadModelMigration|FullyQualifiedName~FlightsDbContextTests"
dotnet ef migrations has-pending-model-changes --project modules/flights/Travel.Modules.Flights.Infrastructure --context FlightsDbContext
```

### Task 7 — implement the single projection pipeline and storage failure classification

**Implementation checkpoint:** Incremental, Validate and exclusive Reset share one event
applier. Each call owns fresh EF/Marten contexts. For an existing checkpoint, Incremental
additionally reads the bounded applied prefix to verify the immutable Hold/owner fact;
only the suffix is applied. This catches a corrupt EF owner and a spurious quote-only row
even when the suffix contains no Hold or the checkpoint already equals the target. It
adds source-read cost (linear in the booking stream length) without a second projection
implementation. Keep the old projector active until Task 9; this service is not yet
invoked through a durable consumer.

**Owner:** Infrastructure behind Application ports.

**Files:**

- Create `modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/OrderReadModelEventApplier.cs`.
- Create `modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/OrderReadModelReconciler.cs`.
- Create `modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/BookingStorageFailureClassifier.cs`.
- Create `modules/flights/Travel.Modules.Flights.Application/ReadModels/BookingProjectionExceptions.cs`.
- Create `modules/flights/Travel.Modules.Flights.Application/ReadModels/IBookingProjectionMaintenanceContext.cs`.
- Create `modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/BookingProjectionMaintenanceContext.cs` with the default deny-Reset implementation and an internal maintenance-only construction path.
- Modify `modules/flights/Travel.Modules.Flights.Infrastructure/FlightsInfrastructureServiceCollectionExtensions.cs`.
- Create `tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/OrderReadModelReconcilerTests.cs`.
- Create `tests/flights/Travel.Modules.Flights.Tests.Unit/Persistence/BookingStorageFailureClassifierTests.cs`.

**Contracts:** `BookingProjectionTransientException`, `BookingProjectionTerminalException`, `BookingReadModelNotReadyException`; maintenance context exposes `RequireExclusiveReset()` which rejects Reset in normal runtime. The runner receives the maintenance-only implementation; normal DI always denies Reset.

**Exception table:**

| Failure | Classification |
|---|---|
| DbUpdateConcurrencyException; unique violation specifically on `ix_order_read_model_aggregate_id` during competing insert | Transient, fresh scope on retry |
| Npgsql transient connectivity/timeout; serialization failure 40001; deadlock 40P01; lock-unavailable 55P03 | Transient; unwrap DbUpdateException/inner errors without parsing message strings |
| Caller/host cancellation with requested token | Propagate cancellation; no conversion to business/storage failure |
| Unknown event, source gap, missing source owner, ahead incremental checkpoint, schema missing/incompatible, non-race integrity violation | Terminal safe code; DLQ |
| Other unexpected exception | Preserve failure; narrow reconcile policy routes to DLQ, no blanket success/discard |

- [ ] RED: new row, suffix catch-up, duplicate/older delivery, two insert races, two update races, event timestamps, owner retained when the suffix has no OfferHeld, PaymentAuthorized-only suffix, exact target-version boundary.
- [ ] RED: Validate performs no save, missing row is repairable, historical owner absence is not; Reset repairs same-version field corruption, wrong derived owner and ahead checkpoint from valid source.
- [ ] RED/GREEN: valid quote-only source plus a spurious EF order row is reported by Validate and removed by exclusive Reset; injected failure preserves the previous row and other orders/source events are untouched.
- [ ] Implement the mode-specific algorithm above; use fresh scopes after failure. Existing tracked EF data must not contaminate Validate/Reset accumulators.
- [ ] Assert Incremental rejects Reset-only situations, and normal DI rejects any Reset call.
- [ ] RED/GREEN classifier tests with DbUpdateException wrapping actual Npgsql/Postgres exception shapes. Do not prove transient policy only by throwing the custom wrapper directly.
- [ ] Keep the old projector registered until the cutover task; no rebuild/notification side effects from the new applier.

```powershell
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit/Travel.Modules.Flights.Tests.Unit.csproj --filter "FullyQualifiedName~BookingStorageFailureClassifierTests"
dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration/Travel.Modules.Flights.Tests.Integration.csproj --filter "FullyQualifiedName~OrderReadModelReconcilerTests|FullyQualifiedName~OrderQueriesTests"
```

### Task 8 — durable consumer and narrowly scoped failure policy

**Owner:** Application handler, Infrastructure exception classification, Api.Composition policy.

**Files:**

- Create `modules/flights/Travel.Modules.Flights.Application/Handlers/Booking/ReconcileOrderReadModelHandler.cs`.
- Create `modules/flights/Travel.Modules.Flights.Api/Composition/BookingConsistencyHandlerPolicy.cs`.
- Modify `modules/flights/Travel.Modules.Flights.Api/Composition/FlightsModule.cs`.
- Create `tests/flights/Travel.Modules.Flights.Tests.Unit/Composition/BookingConsistencyHandlerPolicyTests.cs`.
- Modify `tests/flights/Travel.Modules.Flights.Tests.Unit/Composition/FlightsModuleRegistrationTests.cs`.
- Create `tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/BookingProjectionDeliveryTests.cs`.
- Narrowly amend `docs/adr/0023-module-api-facades-and-cross-cutting-ownership.md`.

- [ ] RED: reconcile explicitly routes to a durable local queue and never NATS; only named message/error combinations receive these policies.
- [ ] Apply the Task 7 storage classifier to raw webhook/notification persistence exceptions too, including MarkProcessed failure after a successful Marten commit. Domain prerequisites and write conflicts retain their distinct schedules and reasons.
- [ ] Route `BookingSourceOwnershipMissingException` and `BookingTransitionRejectedException` for ProcessDuffelWebhookCommand directly to DLQ; do not burn dependency retries on a historical owner fact that cannot appear automatically.
- [ ] Implement a thin handler calling Incremental. No handler-local retry loop or provider dependency.
- [ ] RED/GREEN real-Wolverine delivery tests for transient retry, terminal DLQ, retry exhaustion and pending-envelope recovery on a new host.
- [ ] Verify attempt scopes/sessions differ after an optimistic conflict, and scope disposal removes buffered messages from a losing Marten transaction.

```powershell
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit/Travel.Modules.Flights.Tests.Unit.csproj --filter "FullyQualifiedName~BookingConsistencyHandlerPolicyTests|FullyQualifiedName~FlightsModuleRegistrationTests"
dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration/Travel.Modules.Flights.Tests.Integration.csproj --filter "FullyQualifiedName~BookingProjectionDeliveryTests|FullyQualifiedName~BookingWebhookConcurrencyTests"
```

### Task 9 — cut over commits, version-gate notifications, bound SSE state

**Owner:** Application write/notification flow, Api response/SSE serialization, Infrastructure registry.

**Files:**

- Modify all five mutating handlers listed in Task 4 and `Application/Persistence/DocumentSessionExtensions.cs`.
- Remove `modules/flights/Travel.Modules.Flights.Application/Handlers/Booking/OrderReadModelProjector.cs` and `modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/OrderReadModelProjectorImpl.cs`.
- Update their registration in `modules/flights/Travel.Modules.Flights.Infrastructure/FlightsInfrastructureServiceCollectionExtensions.cs`.
- Modify `modules/flights/Travel.Modules.Flights.Application/Contracts/OrderNotifications.cs`.
- Modify `modules/flights/Travel.Modules.Flights.Application/Handlers/Notifications/SendOrderConfirmationEmailHandler.cs`, `SendOrderCancellationEmailHandler.cs`, `PublishOrderSseHandler.cs`.
- Create `modules/flights/Travel.Modules.Flights.Application/Notifications/IBookingNotificationReadiness.cs`.
- Create `modules/flights/Travel.Modules.Flights.Infrastructure/Notifications/BookingNotificationReadiness.cs` (including the old-message source-version lookup).
- Modify `modules/flights/Travel.Modules.Flights.Application/Notifications/SseEvent.cs`, `modules/flights/Travel.Modules.Flights.Infrastructure/Notifications/Sse/OrderSseConnectionRegistry.cs`, and `modules/flights/Travel.Modules.Flights.Api/Endpoints/OrderEventsSseEndpoint.cs`.
- Create `tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/BookingProjectionOutboxTests.cs` and `MixedProjectionWriterCompatibilityTests.cs`.
- Update `tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/ConfirmOrderOutboxTests.cs`, `tests/flights/Travel.Modules.Flights.Tests.Integration/Notifications/EmailNotificationTests.cs`, `SseRegistryTests.cs`, and `tests/flights/Travel.Modules.Flights.Tests.Unit/Notifications/SseBackpressureTests.cs`.
- Update `tests/Travel.Host.Tests.Integration/Flights/FlightsEndpointsHttpTests.cs` and `tests/Travel.Tests.Architecture/Flights/FlightsArchitectureTests.cs`.
- Amend ADR 0016 and 0018 in this semantic cutover.

**Notification examples:**

```csharp
public sealed record OrderConfirmedNotification(
    Guid AggregateId, Guid UserId, long? RequiredStreamVersion = null);
public sealed record OrderTicketedNotification(
    Guid AggregateId, Guid UserId, long? RequiredStreamVersion = null);
public sealed record OrderCancelledNotification(
    Guid AggregateId, Guid UserId,
    CancelReason Reason = CancelReason.User, long? RequiredStreamVersion = null);
```

- [ ] Prepare and test notification version gates while production senders still emit legacy-compatible envelopes. Readiness port returns a fresh OrderView or throws; it does not mutate projection.
- [ ] RED/GREEN active-connection SSE concurrency: v6 arrives before paused v5, same-version duplicates, two connections, unregister/re-register, full buffer and cleanup. Change the endpoint's current DropOldest channel mode to Wait plus nonblocking TryWrite, so a full buffer is detected. Under the connection lock, compare, TryWrite, then advance last-enqueued version only after success; disconnect a full consumer without awaiting network I/O under the lock.
- [ ] The endpoint must exit when WaitToReadAsync returns false after channel completion, then unregister and release per-connection version/buffer state in finally. Keep at most one outstanding PeriodicTimer wait; do not create overlapping waits when an event wins the heartbeat race. Test completed-reader exit and full-buffer termination through the endpoint, not only registry TryComplete calls.
- [ ] Modify SSE JSON to expose streamVersion; remove per-order history after the last connection, retaining no global high-water tombstones. Test late new connection receiving current version.
- [ ] Switch every successful booking commit (new quote, re-quote, hold, confirm success/two compensation paths, cancel, ticket, refund) to the Task 5 primitive. Pass sibling notifications to the helper instead of publishing them beforehand; compute their required version from loaded version + appended event count, not the aggregate's unrevised in-memory Version.
- [ ] Remove synchronous EF projector only when all producers/consumers compile and the affected suite passes. Never read EF to construct a command response.
- [ ] RED/GREEN event+reconcile+notification rollback for all changed commit shapes; losing concurrent webhook must leave no sibling envelope.
- [ ] Correlation tests: Confirmed stream + missing EF + ticket callback; Confirmed stream + missing EF + refund callback without ticket callback. Both recover through reconciliation/retry.
- [ ] Characterize old-projector/new-checkpoint corruption with a test-local old-write simulator to document the no-mixed-writer gate. This is not a substitute for actual old-reader compatibility testing.
- [ ] Add targeted architecture guards for removed projector usage and centralized commit path; behavior tests, not source grep alone, prove concurrency.

```powershell
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit/Travel.Modules.Flights.Tests.Unit.csproj --filter "FullyQualifiedName~SseBackpressureTests|FullyQualifiedName~FlightsModuleRegistrationTests"
dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration/Travel.Modules.Flights.Tests.Integration.csproj --filter "FullyQualifiedName~BookingProjectionOutboxTests|FullyQualifiedName~ConfirmOrderOutboxTests|FullyQualifiedName~BookingWebhookConcurrencyTests|FullyQualifiedName~DuffelWebhookHandlerTests|FullyQualifiedName~MixedProjectionWriterCompatibilityTests|FullyQualifiedName~EmailNotificationTests|FullyQualifiedName~SseRegistryTests"
dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --filter "FullyQualifiedName~FlightsEndpointsHttpTests"
dotnet test tests/Travel.Tests.Architecture/Travel.Tests.Architecture.csproj --filter "FullyQualifiedName~FlightsArchitectureTests"
```

### Task 10 — prove the crash windows and failure recovery

**Owner:** disposable integration tests using production registrations.

**Files:**

- Create `tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/BookingProjectionConvergenceTests.cs`.
- Extend `tests/flights/Travel.Modules.Flights.Tests.Integration/Outbox/WolverineOutboxFixture.cs`.
- Extend `tests/flights/Travel.Modules.Flights.Tests.Integration/Webhooks/BookingWebhookConcurrencyTests.cs`.
- Test-only EF interceptor/barriers live beside these fixtures; no production fault-injection flag/service.

- [ ] Invoke Confirm exactly once using fake payment/provider and real Marten/Wolverine/EF storage.
- [ ] Fail the first EF projection save with a representative transient storage exception **after** a verification connection observes the committed Marten events and durable envelope.
- [ ] Use barriers to observe EF still stale and release the next attempt deterministically; do not rely on sleeps racing a 1-second retry.
- [ ] Assert automatic catch-up, one committed transition, unchanged fake provider/payment call counts and notification version-gate recovery.
- [ ] Repeat across host restart with the pending envelope confirmed in storage before host B starts; block recovery in host A so a graceful shutdown cannot silently finish the test before restart.
- [ ] Separately inject failure after EF commit but before message acknowledgement: duplicate delivery no-ops without row regression.
- [ ] Pin webhook Marten-commit-before-ProcessedAt failure and conflict-vs-cancellation behavior.
- [ ] Preserve the evidence boundary: controlled process restart proves durable pending-envelope recovery; do not label it an OS-kill/power-loss test.

```powershell
dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration/Travel.Modules.Flights.Tests.Integration.csproj --filter "FullyQualifiedName~BookingProjectionConvergenceTests|FullyQualifiedName~BookingWebhookConcurrencyTests"
```

### Task 11 — exclusive maintenance, validation, rebuild and targeted DLQ replay

**Owner:** Application ports, Infrastructure runner/catalog, Api facade, thin Host command entry.

**Files:**

- Create `modules/flights/Travel.Modules.Flights.Application/ReadModels/IBookingStreamCatalog.cs`, `IOrderReadModelRebuildRunner.cs`, `IBookingConsistencyDiagnostics.cs`.
- Create `modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/BookingStreamCatalog.cs` and `OrderReadModelRebuildRunner.cs`; wire the Task 7 `BookingProjectionMaintenanceContext.cs` maintenance-only construction path from the CLI composition.
- Create `modules/flights/Travel.Modules.Flights.Infrastructure/Diagnostics/BookingConsistencyDiagnostics.cs`.
- Create `modules/flights/Travel.Modules.Flights.Api/Composition/BookingReadModelMaintenance.cs`; extend `FlightsModule.cs`.
- Modify `apps/Travel.Host/Program.cs` and create `apps/Travel.Host/Commands/BookingReadModelCommand.cs` for argument routing, read-only process-store registration and exit codes.
- Create `tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/OrderReadModelRebuildRunnerTests.cs`, `BookingConsistencyRecoveryTests.cs`.
- Create `tests/Travel.Host.Tests.Integration/Flights/BookingReadModelMaintenanceCommandTests.cs`.
- Create `docs/operations/booking-read-model-recovery.md`; amend ADR 0016 with exclusion/downgrade constraints.

**Port signatures:** `IBookingStreamCatalog.ReadIdsAsync(CancellationToken)` returns `IAsyncEnumerable<Guid>` with each Booking stream once; `IOrderReadModelRebuildRunner.RunAsync(bool execute, bool exclusiveMaintenance, CancellationToken)` returns a report with succeeded/non-materialized/failed counts; diagnostics exposes `InspectAsync(Guid, CancellationToken)` and `ReplayAsync(Guid envelopeId, CancellationToken)`. No public HTTP endpoint for these mutations.

- [ ] RED: CLI dry validation does not start listeners/consumers/schema initializers/provider calls; missing schema is a reported prerequisite, not auto-repaired.
- [ ] RED: normal runtime Reset and rebuild without both flags refuse mutation.
- [ ] Implement paged/streaming Booking enumeration via Marten APIs; deduplicate raw-event-derived stream IDs and exclude unrelated aggregate types. Pin behavior against the installed Marten version.
- [ ] Implement Validate/Reset only by calling the same reconciler. Reset retains existing rows until their atomic replacement succeeds.
- [ ] Validate fixtures: quote-only valid, old payload owner missing, wrong derived owner, ahead checkpoint, unsupported source, corrupt row, absent row.
- [ ] Coordinated maintenance test: hold Incremental in flight, stop/drain normal processing, Reset same-version corruption, restart and append another event; repaired fields must remain repaired. Explicitly document that overlapping online Reset is unsupported.
- [ ] RED/GREEN operator sequence: force reconcile + dependent callback/notification into DLQ; repair projection, mark selected envelopes replayable, restart workers, verify callback event and subsequent projection converge without replaying a user command.
- [ ] Replay filters permit only known booking-consistency message types, validate the exact message id, and do not clear inbox ProcessedAt or discard envelopes.
- [ ] Verify backward payload tests and test downgrade failure/unknown reconcile handler in a pre-WS4 fixture; only the tested compatible artifact qualifies as rollback.
- [ ] Report failures/partial runs with safe codes and a nonzero exit; preserve source events and other module tables.

```powershell
dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration/Travel.Modules.Flights.Tests.Integration.csproj --filter "FullyQualifiedName~OrderReadModelRebuildRunnerTests|FullyQualifiedName~BookingConsistencyRecoveryTests|FullyQualifiedName~BookingEventCompatibilityTests"
dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --filter "FullyQualifiedName~BookingReadModelMaintenanceCommandTests"
```

### Task 12 — diagnostics, documentation consistency and final verification

**Owner:** Flights observability/composition and targeted architecture tests.

**Files:**

- Create `modules/flights/Travel.Modules.Flights.Application/Observability/IBookingProjectionMetrics.cs`.
- Modify `modules/flights/Travel.Modules.Flights.Infrastructure/Observability/FlightsMetrics.cs`.
- Create `modules/flights/Travel.Modules.Flights.Infrastructure/HealthChecks/BookingProjectionHealthCheck.cs` and `BookingProjectionBootstrapHealthCheck.cs`.
- Modify `modules/flights/Travel.Modules.Flights.Infrastructure/FlightsInfrastructureServiceCollectionExtensions.cs` and `modules/flights/Travel.Modules.Flights.Api/Composition/FlightsModule.cs`.
- Modify `tests/flights/Travel.Modules.Flights.Tests.Unit/Observability/FlightsMetricsTests.cs`.
- Create `tests/flights/Travel.Modules.Flights.Tests.Integration/HealthChecks/BookingProjectionHealthCheckTests.cs`.
- Modify `tests/Travel.Host.Tests.Integration/Health/HealthEndpointContractTests.cs`, `tests/Travel.Tests.Architecture/Flights/FlightsArchitectureTests.cs`.
- Verify owning amendments to ADR 0015/0016/0018/0023 and `docs/operations/booking-read-model-recovery.md`; adjust `modules/flights/AGENTS.md` only for WS4 facts that changed.

**Metrics/diagnostics:**

- Counters: `flights.booking_projection.reconcile_total{outcome}`, `failures_total{category}`, `rebuild_total{outcome}`.
- Histograms: applied event count, attempt duration, source-target minus checkpoint lag at attempt time.
- Wolverine's persisted pending/delayed/DLQ state remains the delivery source. Logs may include aggregate/envelope IDs; never use those IDs as metric labels or emit passenger payloads.
- `inspect --aggregate-id` reports source version, persisted version, bootstrap status, derived mismatch and relevant pending/DLQ records; compare versions **of the same stream**.
- Internal dependency health reports diagnostic availability and DLQ counts; absence of dead letters is not proof of projection freshness. Do not use cross-order min/max versions as a lag metric.
- Separate bootstrap readiness check closes for sentinel rows. Readiness does not demand steady-state zero lag. A zero-sentinel table does not prove there are no missing historical rows: rollout additionally requires the per-stream Validate report.
- Existing health JSON can carry a safe compact description. Detailed diagnostics belong to the CLI/structured logs; no new public health payload or WS5 platform rewrite.

- [ ] RED/GREEN MeterListener tests and health tests for sentinel, DLQ, storage unavailable, no-DLQ-but-missing-row, and ordinary eventual lag.
- [ ] Verify module registration and unchanged global health endpoint policy.
- [ ] Verify all semantic ADR text agrees with the final approved behavior; eight events, owner field, compatible re-quote, Confirmed refund, per-connection SSE, exclusive rebuild and bounded recovery.
- [ ] Run focused checks; then one full suite after integration stabilizes. Do not repeat the full suite without a new change/failure.

```powershell
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit/Travel.Modules.Flights.Tests.Unit.csproj --filter "FullyQualifiedName~FlightsMetricsTests"
dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration/Travel.Modules.Flights.Tests.Integration.csproj --filter "FullyQualifiedName~BookingProjectionHealthCheckTests"
dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --filter "FullyQualifiedName~HealthEndpointContractTests"
dotnet csharpier check .
dotnet test Travel.slnx --maxcpucount:1
npm.cmd run check:ai-harness
git diff --check
git status --short
```

## Acceptance and evidence levels

| Requirement | Owning proof |
|---|---|
| One Core transition source, expected errors/no-op decisions | Tasks 1/2/4; operation×state matrix and side-effect-free rejected/no-op cases |
| Concurrent webhook transitions cannot bypass Core | Task 4/9 barrier races, fresh-decision retry and post-commit inbox failure |
| Event + reconcile + sibling notification atomicity | Tasks 5/9; all commit paths including compensation/rollback |
| Incremental version never regresses, duplicates/out-of-order safe | Task 7 real EF insert/update races and bounded source slices |
| Convergence after Marten commit before EF, no repeated user command | Task 10; real durable storage, injected failure and restart |
| Terminal failure/DLQ has a recovery route | Tasks 8/11; classified failures and projection-first targeted replay |
| Rebuild uses same applier, safely repairs corrupt derived rows | Tasks 7/11; read-only Validate, exclusive Reset, coordinated drain/resume |
| Expired quote can be refreshed and then held | Task 3 replay-based quote→re-quote→Hold test |
| Owner is reconstructable for new streams | Task 2/7; additive event field; unknown legacy owners fail visibly |
| Notifications tolerate projection lag; SSE active connection cannot regress | Task 9; version gates, concurrent enqueue tests, cleanup/reconnect limits |
| Rollback distinguishes schema, payload, message and security compatibility | Tasks 2/3/11; old-reader tests and supported-artifact boundary |
| Owning ADRs change with semantics; WS5 stays out | Per-task ADR amendments and Task 12 verification |

**Source-ready:** approved source changes, reviewed authorized migration source, focused/build/format/architecture checks and matching docs. This does not mean a database or live system changed.

**Disposable-integration-proven:** fresh/upgrade schema tests, real expected-version/outbox behavior, EF races, representative storage exceptions, durable retry/restart, targeted DLQ recovery and maintenance rebuild pass on isolated resources. Quote-only streams return source version with Materialized=false; order streams persist their target checkpoint.

**Live-proven:** outside this task; requires separate authorization and all of:

1. Audit actual legacy ownership and resolve any order-bearing source gaps through an independently approved data decision.
2. Review target/previous artifact compatibility and the migration job; drain all old/new booking writers before bootstrap/reset.
3. Run schema migration under its own permission; maintenance Validate, reviewed Reset, and zero-sentinel **plus per-stream validation** before routing normal traffic.
4. Resume workers, process/replay selected pending dependencies as appropriate, and verify their follow-up reconcile cycle and diagnostics.
5. Run separately approved live smoke. Do not infer provider-call or infrastructure authority from plan approval.

## Review closure and next checkpoint

| 2026-09-16 finding | Revision |
|---|---|
| Webhook TOCTOU / conflict swallowed as ErrorOr | All writes expected-version; HTTP/background conflict handling separated; concurrent and post-commit-inbox tests |
| Ticketed-only refund without business evidence | Preserve Confirmed→Refunded; early-state changes explicitly pending approval |
| Reset vs Incremental same-version lost repair | Exclusive maintenance, no online guarantee; Reset may repair ahead/wrong-derived state |
| EF-only rollback reasoning | Additive event payload recommendation and full event/message/security compatibility gate; compatible-artifact rollback/forward repair |
| Unclassified real DB errors and missing DLQ recovery | Explicit classifier and representative exception tests; projection-first targeted replay runbook/test |
| Eternal SSE state and false delivery promise | Per-active-connection serialized enqueue/version state; cleanup; best-effort SSE with GET recovery |
| Re-quote expiry only in command response | Optional source snapshot with old/new reader tests and replay→Hold acceptance |

Self-review completed on 2026-09-16: checked matrix coverage, task/interface dependencies, enrollment-before-publication, owner-first response mapping, mode-specific repair rules, payload/downgrade limits, exception classification and scoped SSE guarantees. The additional independent-audit cases (ownerless webhook before write, spurious quote-only EF row, completed SSE reader cleanup) are included in their owning tasks. The independent audit found no remaining booking-consistency correctness blocker; its last authority-wording finding was resolved by naming the source/snapshot/SQL artifact grant separately from database application. Document structure/whitespace and the untouched source/migration boundaries were checked; these are plan checks, not runtime evidence. This revised plan has not been executed.

**Next checkpoint:** review/approve D1–D6 and the revised implementation scope. Implementation can then begin with Tasks 1–5 through the source checkpoint. Task 6 stops for the migration-artifact grant covering source/snapshot/Designer and review SQL together; disposable database application requires a different, explicit grant. No commit/push/PR or live operation follows automatically.
