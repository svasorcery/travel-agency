# WS4 Task 9: booking commit and notification cutover

## Workspace and authority

Worktree: `.worktrees/ws4-booking-consistency`; branch: `codex/ws4-booking-consistency`.
Starting HEAD: `eb821909c395100e65e92d3cab641a0c5d51bc16`, descending from the recorded
`eb279591a1bfd04049f32713f86f900e875ee19c` base. Checkpoints `4b5a483`, `0cbe0c5` and
`eb82190` were preserved. A read-only `git ls-remote` confirmed the remote WS4 branch also
points to `eb82190`; no merge was needed to incorporate those commits.

The user authorized Task 9 source changes and disposable integration tests with the existing
ProjectedStreamVersion migration. No new migration, shared/live database, provider call,
deployment, staging, commit, push or PR is part of this checkpoint.

## Implemented behavior

- Every booking commit uses the enrolled Marten outbox helper: new quote, re-quote, hold,
  confirm, capture/provider compensation, user cancel, ticket and direct refund.
- Events and reconcile requests commit together. Existing confirmation/cancellation/ticket
  sibling notifications carry the exact loaded stream version plus appended-event count.
  Compensation/refund do not introduce new notifications.
- The synchronous EF projector and registration are removed. Responses remain command-owned;
  EF materializes asynchronously from Hold onward. Quote/re-quote remain non-materialized.
- Readiness returns a fresh EF snapshot and performs no projection writes. Missing/behind rows
  cause dependency retry before effects. Null legacy versions capture current Marten version;
  explicit nonpositive versions are terminal-invalid. All three legacy envelope shapes are covered.
- Email/SSE apply the approved current-state suppression rules. SSE exposes streamVersion,
  compares/enqueues/advances under a per-connection lock, disconnects full consumers and releases
  connection state on unregister. Completed readers exit; heartbeat and reader waits do not overlap.
- ADR 0016/0018 are amended alongside the cutover. No D1–D6 business decision was changed.

## Review and test evidence

Initial inspection found no substantial Task 8 cutover blocker. Its existing durable local
routing, scoped failure classification and atomic commit helper were retained. Notification
readiness needs the same narrow Wolverine service-location opt-in as the existing reconciler;
the real production notification handlers were exercised through Wolverine to verify it.

Observed RED/GREEN checks include readiness/legacy handling, email suppression, SSE suppression
and version payloads, channel backpressure, connection monotonicity, endpoint completion and
heartbeat, command reconcile/no-synchronous-EF behavior, and removal of the projector.
The full integration run also exposed Wolverine's automatic middleware saving again after an
explicit booking conflict was mapped to 409. The five explicit-commit writers now locally use
NonTransactional; the enrolled helper still commits the events/messages, and Host-wide policy
is unchanged. The real generated-handler concurrency test is the verification gate for this fix.
A second failing legacy test still expected synchronous EF.RefundedAt; its assertion is updated
to expect the deferred projection, with real reconciliation covered by the correlation tests.

The commit-shape tests caught Marten CurrentVersion changing during AppendOne: a ticket notification
initially requested version 6 for a committed version 5. Capturing the version before append fixed it.

The nine commit-shape tests use real Marten/Wolverine transactions with a test outbox decorator
that routes sibling delivery to a probe while recording original notification fields. The existing
ConfirmOrderOutbox tests retain actual confirmation-envelope delivery. Missing-correlation tests
use the real EF inbox store and reconciler, then explicitly retry with a fresh Marten session.
The mixed-writer test is a local old-SQL-writer simulator, not old-binary compatibility evidence.

Final verification:

- Flights unit: 475/475, including legacy envelopes, current-state gates and strengthened SSE tests.
- New focused integration: 21/21; real Wolverine reconciler/notification composition: 1/1.
- Architecture: 151/151; the two cutover guards also pass after the explicit-transaction correction.
- Host HTTP and module wiring: 27/27.
- Solution build passes with existing AD0001 warnings. Focused project commands also emit
  the existing Verify solution-discovery warning. CSharpier and whitespace checks pass.
- One complete Flights integration run: 245/247 in 17m24s before the two fixes above.
  The final focused rerun passes 26/26, including both previously failing cases, all nine commit
  shapes with rollback, missing-correlation recovery, actual Wolverine notification composition,
  and the new losing real-ticket-handler test. The full suite was not repeated: this is combined
  evidence, not a claim of a single all-green final full-suite run.
- The losing real ticket test verifies both non-delivery and absence of persisted outgoing/incoming
  envelopes, then retries with a fresh scope and acknowledges without another event/message.
- A supplementary build attempted while testhost was running hit Windows DLL copy locks. It was
  retried after the test process exited; no build-output workaround or dependency change was used.

Logs are local, ignored artifacts under `TestResults/ws4-task9/`. The complete Flights integration
suite was run once; follow-up execution is limited to changed or failing tests. Host module wiring
also starts disposable PostgreSQL; remaining container runs are kept sequential.

## Limitations and next checkpoint

This is Task 9 source/disposable-test evidence, not completed WS4 or live rollout evidence.
At-least-once SMTP can duplicate after a crash; SSE is best effort per active connection and
has no reconnect replay or cross-node guarantee. Old and new projection writers must not coexist:
old SQL can corrupt derived fields without changing the checkpoint, which validation detects.
Historical ownership/bootstrap and live schema validation remain separate operational gates.

Task 9 self-review found no remaining cutover blocker after the exact-version and generated
second-save fixes. The review covered all nine producer paths, message enrollment, conflict mapping,
consumer freshness/suppression, connection locking and cleanup, failure-policy scope, removed
projector references, command-owned responses and ADR consistency. No independent reviewer was used.

The next implementation checkpoint is Task 10's crash-window and
failure-recovery proof. Task 11 maintenance/rebuild/operator replay and Task 12 final diagnostics
remain outside this change. Publication still requires separate user authorization.
