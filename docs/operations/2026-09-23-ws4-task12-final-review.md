# WS4 Task 12: diagnostics and final verification

Task 12 is source-ready and disposable-integration-proven. The full local solution test
completed successfully. WS4 is not live-proven: no shared environment, deployment,
historical rollback artifact, OS-kill/power-loss recovery, or distributed fencing was
tested or authorized.

Worktree: `.worktrees/ws4-booking-consistency`; branch `codex/ws4-booking-consistency`;
starting HEAD `ef488b546c02c8e541649deab75ccb2eb2808333`. The starting status
was clean. This task produced unstaged source, tests and documentation only; no new
migration, commit, push, PR or deployment.

## Result

- Application exposes `IBookingProjectionMetrics`; the existing `FlightsMetrics`
  meter implements it for both the normal module and maintenance container.
  Reconciliation attempts record bounded outcomes, applied event count, attempt
  duration and the captured source target minus the checkpoint of that same stream.
  Failures use bounded storage/source/checkpoint/ownership/unknown categories.
  Rebuild records a bounded outcome per processed stream. No aggregate ID, envelope
  ID or passenger field is a metric label.
- The internal dependency check reads Wolverine's persisted incoming, scheduled,
  outgoing and DLQ counts. It degrades when DLQ is nonempty and becomes unhealthy
  when diagnostics cannot be read. It does not claim projection freshness. The
  separate readiness check closes for a negative checkpoint sentinel or inaccessible
  EF storage. Ordinary eventual lag keeps readiness open. Both checks use the
  existing internal health routes, tags and JSON response; there is no new public API.
- The existing `inspect --aggregate-id` path still uses Task 11 validation and
  Wolverine diagnostics. It now emits explicit `BootstrapRequired` and
  `DerivedMismatch` flags in addition to source/persisted versions, issue codes and
  correlated pending/outgoing/DLQ records. Zero DLQ and zero sentinels do not
  establish completeness; the runbook requires per-stream `validate`.
- ADR 0015's outdated Marten-projection wording, ADR 0018's operator-recovery gate,
  ADR 0023's later-WS4 wording, and the runbook were aligned with current behavior.
  ADR 0016 already describes per-connection SSE, exclusive Reset, bounded recovery
  and rollback limits. The eight-event model, replayable owner, compatible re-quote
  and Confirmed-to-Refunded transition remain unchanged. The module AGENTS file
  required no factual correction.

## Verification

| Check | Result |
|---|---|
| Focused Flights metrics, registration and inspect unit tests | 56 passed |
| Focused booking projection health tests with disposable PostgreSQL/Wolverine | 7 passed, including sentinel, DLQ, unavailable storage, missing row with no DLQ, and ordinary lag |
| Host health endpoint contract | 21 passed; internal listener/tag policy and public denial retained |
| Focused Flights architecture tests | 23 passed |
| `dotnet csharpier check .` | passed, 468 files checked |
| `npm.cmd run check:ai-harness` | passed, 90 harness tests plus contract validation |
| `dotnet test Travel.slnx --maxcpucount:1` | exit 0; 1142 tests passed across nonempty projects |
| `git diff --check` | passed |

The full run included Flights integration 279/279, Flights unit 480/480,
Host integration 156/156, architecture 151/151, AI unit 45/45,
contract 9/9 and Identity unit 22/22. Paid AI evals were skipped by their
existing gate. Hotels, Rail, Trips and Identity integration scaffolds without
tests reported no available tests. Existing compiler AD0001 analyzer warnings
and the Verify solution-discovery warning appeared; neither failed the run.

The first sandboxed attempt to run the focused container tests could not access
the Docker named pipe. The same disposable tests passed with approved local
Docker access. No shared database or real provider was contacted.

## Review and remaining gates

Self-review checked meter names and bounded tags, same-stream lag arithmetic,
health tag placement and JSON safety, failure handling, the existing diagnostic
path, operator wording and the source/production boundary. The health check
reports Wolverine-wide persisted counts and DLQ availability; it does not
identify which independent stream is behind. `validate` remains the required
per-stream proof before a rollout. The maintenance process creates metric
instruments but does not itself establish a live telemetry exporter.
An independent read-only review of the complete Task 12 diff found no actionable
correctness or acceptance-gap issue. It confirmed that Wolverine-wide counts are
deliberate diagnostics, not a per-booking freshness measure.

Task 11's disposable retry/DLQ and coordinated recovery evidence remains separate
from this Task 12 run. No live recovery, process-kill/power-loss recovery,
distributed exclusion or safe downgrade to a pre-WS4 binary is established.
The next gate for operational use is a separately authorized deployment and
environment-specific validation, including per-stream `validate` and an approved
WS4-compatible rollback artifact.
