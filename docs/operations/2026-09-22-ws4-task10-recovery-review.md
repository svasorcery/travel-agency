# WS4 Task 10: crash windows and failure recovery

## Scope and workspace

Existing worktree `.worktrees/ws4-booking-consistency`, branch
`codex/ws4-booking-consistency`, starting HEAD
`e6b74b1ad9c8257cd2513f9a5d7d32d03ae78e04`. Initial working tree was clean;
the recorded WS4 base `eb279591a1bfd04049f32713f86f900e875ee19c` remains an ancestor.
No branch switch or new worktree was performed. On 2026-09-23 the user separately
authorized committing Task 10 and cleaning up its temporary artifacts. Push and
PR remain outside that authorization.

The user authorized Task 10 source/tests and disposable integration execution using
existing migrations. Production code, migration artifacts, shared/live databases,
external providers, frontend, deployment and Tasks 11–12 are outside this change.

## Test design

The new `WolverineOutboxFixture` factory calls the production `AddFlightsModule`,
`ConfigureMarten` and `ConfigureWolverine` contributions. The test host supplies
the process-owned Marten/Wolverine transaction and durable-local-queue policies.
Production reconciler, EF pooling/options, inbox store, notification readiness,
email renderer, SSE registry, handler discovery and scoped retry rules remain in use.
Only booking/payment, email transport and user-directory integrations are replaced
with fakes. External Wolverine transports are disabled; no HTTP listener is opened.

Existing EF migrations and Wolverine's existing durable schema are applied only
to each test's fresh PostgreSQL container. Explicit Wolverine schema preparation
also permits a cold host A to start with recovery disabled from the outset.
Restart reuses that test database; it does not reset the stream or envelopes.

Test-only EF interceptors and Wolverine middleware implement bounded barriers and
record envelope IDs, context identities and notification requirements. Delays only
poll observable state; they do not select which side of a retry race wins.

| Scenario | Evidence required by the test |
|---|---|
| Marten commit before first EF projection save | Invoke Confirm once through Wolverine. At the EF barrier, independent sessions observe four committed stream events, the durable reconcile envelope, and the still-Held EF row at version 2. Only then release a representative transient `DbUpdateException`/SQLSTATE `40P01`. |
| Automatic recovery | The original reconcile envelope retries in a fresh context. While retry is held, EF is still stale and email/SSE have no effects. Release permits EF version 4, Confirmed status, stable row ID/owner and recovery of the original notification with `RequiredStreamVersion=4`. Authorize/capture/provider Confirm counts remain one. |
| Controlled host restart | Host A has recovery disabled before it ever starts. Verify its failed reconcile is persisted as Scheduled, stop A and recheck stale EF plus the stored envelope. Host B recovers that exact ID without a second Confirm or publication. A newly registered SSE connection receives the recovered notification after the projection gate opens. |
| EF commit before acknowledgement | `SavedChangesAsync` holds the handler after independent reads see EF version 4. Throw before Wolverine acknowledgement, then verify redelivery of the same envelope, full row equivalence and only one EF save: the retry is a checkpoint no-op. |
| Webhook commit before ProcessedAt | Real queued Duffel handler commits a ticket and messages, then fails the real EF inbox acknowledgement save. Independent reads see one ticket and null ProcessedAt. Durable retry uses a fresh EF lease, acknowledges, and does not publish another reconcile/ticket notification or append another event. |
| Conflict versus requested cancellation | A test-only Marten session listener either commits a competing ticket or cancels the caller token at the save boundary. The real handler distinguishes `BookingWriteConflictException` from `OperationCanceledException`, leaves the inbox unprocessed and persists no losing envelopes. A fresh delivery acknowledges safely. This does not claim automatic retry of requested lifecycle cancellation. |

The Held/Confirmed streams and starting read models are fixture setup. After the
single Confirm, projection repair is exclusively durable-message driven.

## Review and verification

Self-review checked the six new scenarios against Task 10, production DI ownership,
independent database observations, exact envelope identity, bounded cleanup and
the absence of manual command replay during projection repair.
An independent read-only review identified two improvements, both incorporated:

- record full pooled `DbContextId` (including its lease), because fresh scopes may
  legitimately reuse the same underlying EF instance;
- assert the original confirmation notification ID and exact required version 4,
  because SSE version alone would also pass through the legacy null-version path.

Initial harness failures were corrected: missing host routing services, scoped EF
configuration added to pooled options, resolving scoped IMessageBus from root,
and disabling conventional discovery while expecting assembly discovery to work.
The cold recovery-disabled host additionally required explicit Wolverine schema
preparation. After stopping A, its storage-admin API carries a cancelled lifecycle
token; the between-hosts envelope check therefore uses an independent Npgsql
connection and verifies both original IDs before B starts.
These are test-harness failures, not evidence of a production defect
or a behavioral regression RED/GREEN cycle. Production behavior was already
implemented by Tasks 1–9; this task adds its missing end-to-end failure evidence.

The first restore was blocked by sandbox networking; the authorized escalated
run restored the existing dependencies. One obsolete run was stopped after
startup failures and a stalled testhost teardown. Intermediate run artifacts were
removed during the authorized cleanup; the successful final log and TRX remain
under the ignored `TestResults/ws4-task10/` directory.

Final verification on the completed source:

- **13/13 integration tests passed**, 0 failed, 0 skipped, in 1m56s. This includes
  all 8 cases in the plan's two classes (6 new cases and 2 existing concurrency
  cases), plus 5 nearby outbox regressions. Evidence: `task10-final.trx` and
  `task10-final.log` under `TestResults/ws4-task10/`.
- The integration project and its dependencies built successfully. The existing
  Verify solution-discovery warning remains; no new dependency/version change.
- `dotnet csharpier check tests/flights/Travel.Modules.Flights.Tests.Integration`
  checked 61 files successfully; `git diff --check` passed.
- Self-review and independent review are complete with no unresolved findings.
  Verification was completed against the Task 10 changes based on
  `e6b74b1ad9c8257cd2513f9a5d7d32d03ae78e04`, before the separately authorized commit.

The final integration filter is:

```powershell
dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration/Travel.Modules.Flights.Tests.Integration.csproj --no-restore --filter "FullyQualifiedName~BookingProjectionConvergenceTests|FullyQualifiedName~BookingWebhookConcurrencyTests|FullyQualifiedName~MartenWolverineOutboxTests|FullyQualifiedName~EfWolverineOutboxTests|FullyQualifiedName~ConfirmOrderOutboxTests"
```

The five additional outbox tests cover the unchanged shared fixture and the nearby
Confirm commit/rollback/concurrency paths. The full Flights integration suite,
solution suite and unrelated test projects are not repeated: production source
and the existing fixture setup are unchanged. This checkpoint does not claim a
new all-green full-suite run. Container projects were never run concurrently.

## Evidence boundary

This is disposable local integration evidence. Controlled graceful host replacement
is **not** an OS-kill, power-loss, live rollout or multi-node recovery test.
SSE remains best effort for each active connection; SMTP remains at-least-once.
The tests do not close the provider-success-before-Marten-commit window, promise
external exactly-once effects, or prove recovery after retry exhaustion.
Task 11 maintenance/rebuild/operator replay and Task 12 final diagnostics remain
separate work. Publication requires separate authorization.
