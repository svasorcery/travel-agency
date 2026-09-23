# WS4 Task 11 implementation and verification

Task 11 is source-ready and disposable-integration-proven. It is not live-proven
and does not complete Task 12 or the entire WS4 workstream.

Workspace: `.worktrees/ws4-booking-consistency`, branch `codex/ws4-booking-consistency`,
starting HEAD `9be043e3052dea239e0d1da759e7469918f15d68`. Initial status was clean.
No branch switch, new worktree, new migration, PR or shared-data operation.
After reviewing the completed result, the user explicitly authorized the Task 11
commit, push to the existing branch, and cleanup of this task's temporary artifacts.

Scope: Application maintenance ports, Infrastructure implementations, Api facade,
early Host CLI entry, disposable tests and operator documentation. Tasks 1–10 are
the baseline; Task 12 remains excluded. Existing migrations may be applied only
to disposable fixtures.

## Implementation

- Catalog pages raw first-event metadata by global sequence and checks each stream's
  aggregate type. This avoids payload deserialization during enumeration and avoids
  an unbounded deduplication set. Each nonempty intact stream has one version 1 event.
- Host builds a separate maintenance service container but never starts it. Schema
  compatibility is checked read-only for every environment; no normal module
  initializers, providers, email services or HTTP server are registered.
- Exclusion is an operator acknowledgement, not a distributed lock. Reset uses the
  existing reconciler and atomic EF save; normal DI still denies Reset.
- Initial implementation scope required unstaged changes; subsequent explicit
  authorization permits commit/push. Run only justified focused checks with
  container projects sequential.

Application owns `IBookingStreamCatalog`, `IOrderReadModelRebuildRunner` and
`IBookingConsistencyDiagnostics`. Infrastructure owns storage enumeration, validation/
Reset orchestration and Wolverine diagnostics. The Host imports only the Api
Composition facade. There are no new HTTP endpoints, migrations or dependencies.

Runner output is synchronous per stream, with IDs, issue codes and cumulative counts.
Catalog failure returns the completed results plus `CatalogReadFailed`; cancellation
keeps already-written progress. Independent bad streams do not prevent valid streams
from being checked/rebuilt. Exceptions do not expose payloads or connection strings.

Replay validates the exact dead-letter ID and a five-message-type allowlist. It
checks projection prerequisites before calling Wolverine `ReplayAsync` with
`MessageIds = [id]`. It never discards, edits or republishes envelopes. The installed
Wolverine 6.17.0 [administrative implementation](https://github.com/JasperFx/wolverine/blob/cab8c37dd8a1bb88c81cf2190109b907d9641163/src/Persistence/Wolverine.RDBMS/MessageDatabase.DeadLetterAdminService.cs)
was inspected; tests verify persisted replayable flags and subsequent delivery of
the selected IDs. Callback correlation uses the EF provider-order mapping, so a
missing row must be rebuilt before that callback is replayed/fully correlated.

The only change to the existing reconciler is comparison of timestamp fields at
PostgreSQL/Npgsql storage precision. Event JSON can retain 100ns ticks while SQL
timestamps store integer microseconds from the 2000 epoch. Direct equality caused
false `DerivedFieldsMismatch` immediately after successful Reset. A focused RED
test observed the actual 7-tick truncation; storage-precision comparison made it GREEN.
No source timestamps or events are rewritten.

The source call-site guard was updated for the two mutually exclusive Host entry
paths, with exactly one registration per path and endpoint mapping still only in
Program. This follows the approved Task 11 maintenance boundary; ADR 0023 receives
a narrow ownership amendment alongside the requested ADR 0016 recovery amendment.

## Review findings and corrections

Self-review checked each Task 11 acceptance item, source ownership, schema gates,
argument validation, mutation flags, per-stream errors, atomic replacement, envelope
identity, inbox acknowledgement preservation and the downgrade evidence boundary.
One independent read-only review found two issues; both were reproduced and fixed:

1. Allowing `DerivedFieldsMismatch` through replay could leave corrupt fields at the
   same checkpoint or advance a behind checkpoint without repairing its prefix.
   Replay now refuses any such mismatch, requiring Reset. Both equal/behind cases
   pass through the real operator sequence after repair.
2. Catalog failure previously discarded the report of already-committed streams.
   The runner now returns a partial report and writes ordered per-stream progress.
   Tests cover enumeration failure after a successful commit and cancellation with
   retained progress/counts.

No independent second review was performed after these fixes; targeted RED/GREEN
tests and the final focused regressions verify them. There are no deferred findings.

## Disposable test evidence

| Boundary | Proof |
|---|---|
| Catalog | Crosses the 128-entry page boundary, yields Booking IDs once, excludes unrelated aggregate types and reads metadata without loading each payload. |
| Validation/Reset | Ownerless history, valid quote-only, missing row, wrong owner, ahead checkpoint, corrupted fields, unsupported event; source events preserved and row ID retained. Existing reconciler tests also cover missing/wrong-type source and spurious quote rows. |
| Failed Reset | Test-only EF save failure leaves original ID, fields and checkpoint intact; report is failed. |
| Coordinated exclusion | Barrier holds Incremental in flight; test releases and awaits it, performs same-version corruption repair with no concurrent writer, then resumes Incremental for the next event and validates repaired fields. This is local coordination, not distributed fencing. |
| CLI | Missing schema fails without creating any tables in Development and Production. Invalid flags fail before connecting. A separate real Host process runs maintenance against disposable storage with a pending durable envelope; no web startup or consumption occurs. |
| Operator DLQ recovery | One real Confirm invocation with fake provider/payment commits v4 and messages. Test-only transient storage failure injection repeatedly fails reconcile, dependent notification and callback. Each original envelope makes four attempts using production 1s/5s/30s retry intervals and then reaches real DLQ. Stop host A, repair projection, mark only selected original IDs replayable through the production diagnostic port, start B, observe callback acknowledgement, one ticket event and projection v5. |
| Replay limits | Missing/empty ID, user command type and equal/behind corrupted row are refused. Unselected allowed DLQ envelope remains. Inbox `ProcessedAt` stays null through maintenance and changes only during normal successful callback handling. |
| External effects | Confirmation, authorization and capture remain exactly once in the operator test. Unexpected Hold/Cancel paths throw in the test fake. No real provider/payment call. |
| Downgrade | Legacy event payload-shape tests plus a pre-WS4-style handler surface accepting the old notification but unable to route the new reconcile contract. This fixture uses the current secured runtime; it is not an old deployable binary. |

The initial operator test used terminal failures. It was then strengthened to
exhaust the real transient-storage policy without shortening its retry intervals;
both equal/behind-corruption variants passed. This does not exhaust every possible
callback/readiness/conflict policy. Task 10 covers recovery before exhaustion;
this task proves the manual path afterwards. Controlled host replacement is not
OS-kill/power-loss or multi-node/live recovery evidence.

Intermediate harness corrections were limited to tests: missing `type: ticket` in
the new callback fixture, static middleware type used as a generic argument, and
the installed runtime reporting `IndeterminateRoutesException` for the missing old
route. The missing ticket type caused a bounded convergence timeout; it was not a
maintenance-mode or production callback defect. Read-only inspection of Wolverine
source confirmed there was no need to start consumers for administrative replay.

## Final verification

- Flights focused integration: **51/51**, 0 failed/skipped, in 2m43s;
  `flights-final.log` / `.trx`. Includes runner, recovery, old event payloads,
  reconciler, Task 10 convergence/webhook concurrency and adjacent outbox tests.
- After strengthening the two operator cases from terminal failure to real retry
  exhaustion: **6/6**, 0 failed/skipped, in 1m53s;
  `exhausted-recovery-final.log` / `.trx`. Includes those two cases and four
  notification-readiness/legacy-version cases. Production source did not change
  after the 51-test run. This is combined evidence, not a claim that the earlier
  51-test artifact already contained the stronger retry-exhaustion assertion.
- Host CLI plus ordinary HTTP/module wiring: **34/34**, 0 failed/skipped, in 1m50s;
  `host-final.log` / `.trx`. The real-process test exercises validate, rebuild,
  inspect and replay exit paths with pending storage left untouched.
- Focused architecture guards: **45/45**, 0 failed/skipped;
  `architecture-final.log` / `.trx`.
- Notification version/state unit checks: **23/23**, including all three legacy
  notification shapes and explicit invalid-version distinction;
  `notification-unit-final.log` / `.trx`.
- CSharpier checked all 16 changed C# files; `git diff --check` passed.
- `npm.cmd run check:ai-harness`: 90/90 tests and inventory validation passed.

Logs and TRX are retained under ignored `TestResults/ws4-task11/`; no existing
Task 9/10 artifacts were removed. Affected projects and dependencies built through
the test commands. Existing Verify solution-discovery and AD0001 warnings remain.
Authorized cleanup retains the five final log/TRX pairs listed above and removes
the temporary API reflection probe and intermediate Task 11 run artifacts.

The full solution and full Flights suite are not repeated. Container projects run
sequentially. No reliance on absent lefthook, Git hooks or historical green counts.

The final filters were:

```text
Flights integration: OrderReadModelRebuildRunnerTests | BookingConsistencyRecoveryTests | BookingEventCompatibilityTests | OrderReadModelReconcilerTests | BookingProjectionConvergenceTests | BookingWebhookConcurrencyTests | MartenWolverineOutboxTests | EfWolverineOutboxTests | ConfirmOrderOutboxTests
Follow-up Flights integration: Operator_repairs_projection | BookingNotificationReadinessTests
Host integration: BookingReadModelMaintenanceCommandTests | FlightsEndpointsHttpTests | FlightsModuleWiringTests
Architecture: CompositionCallSiteTests | HostModuleTypeDependencyTests | HostModuleProjectReferenceTests | FlightsArchitectureTests
Flights unit: NotificationVersionGateTests
```

All commands used `dotnet test <project> --no-restore --filter` with the corresponding
`FullyQualifiedName~` alternatives, console logs and TRX under the evidence directory.
Final self-review is closed after the two independent findings' RED/GREEN corrections.

## Operational limits

No live/shared database, migration application outside disposable fixtures,
provider calls or deployment occurred. Git commit/push is separately authorized;
no PR creation or merge is included.
Task 12 health/metrics/final-WS4 acceptance remains separate. No historical artifact
is certified for rollback; use forward repair or separately test a WS4-compatible
artifact. Maintenance exclusion is an operator prerequisite, not distributed fencing.
The [operator runbook](booking-read-model-recovery.md) gives commands, safe codes,
projection-first replay order and the required follow-up callback/reconcile cycle.

Initial harness evidence: first RED build found absent runner/catalog/CLI types.
Docker access was denied in the sandbox; identical container tests were rerun with
authorized elevated access. Existing Verify and AD0001 warnings remain.
