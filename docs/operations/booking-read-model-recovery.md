# Recover the booking read model

Use this procedure when committed booking events exist but the order projection or
dependent messages have not recovered automatically. Never repeat Confirm, Hold or
Cancel to repair the projection: those commands can call payment and booking providers.

The examples use the existing Host executable. Supply `ConnectionStrings__travel`
through your approved secret/configuration mechanism; do not put credentials in
command arguments or incident reports. These instructions do not authorize access
to a shared environment. Obtain the environment's operational authorization first.

## Check before changing data

```text
dotnet run --project apps/Travel.Host -- booking-read-model validate
dotnet run --project apps/Travel.Host -- booking-read-model inspect --aggregate-id <booking-guid>
```

`validate` enumerates Booking streams and compares each source-derived row against
EF. `inspect` reports the source and persisted version of one stream, explicit
`BootstrapRequired` and `DerivedMismatch` flags, validation issue codes and
correlated pending/outgoing/dead-letter envelope IDs. It does not
print passenger payloads, email addresses, connection strings or exception details.
Webhook correlation uses the inbox and provider-order mapping; repeat inspection
after fixing a missing projection. No DLQ result is evidence that every projection
is current. Inspection across independent stores is diagnostic, not an atomic snapshot.

The internal `/health/dependencies` response includes a booking projection diagnostic check:
it reads persisted Wolverine incoming, scheduled, outgoing and DLQ counts. A DLQ entry degrades
that dependency check; inaccessible diagnostics make it unhealthy. This check does not prove
that each EF row is current. `/health/ready` separately closes when an EF row still has the
negative bootstrap checkpoint or the bootstrap query fails. Ordinary per-stream lag does not
close readiness. Even zero sentinel rows and zero DLQ entries cannot establish that every
historical order exists: run `booking-read-model validate` for that proof.

Both commands are read-only. Maintenance is selected before the web application
is built. It never starts the Host, consumers, providers, email, schema initializers
or a web listener. Existing EF migrations and compatible Marten/Wolverine storage
must already be present in **every** environment, including Development. A missing
or inaccessible schema returns `SchemaPrerequisite`; apply nothing automatically.

| Issue | Action |
|---|---|
| `ProjectionMissing`, `CheckpointBehind` | Check the durable reconcile envelope. A valid suffix may catch up; for recovery after DLQ, restore and validate projection before dependent replay. |
| `ProjectionBootstrapRequired`, `CheckpointAhead`, `DerivedFieldsMismatch` | Stop/drain all writers, then exclusive Reset. Incremental processing cannot repair arbitrary prefix corruption. |
| `ProjectionUnexpected` on quote-only source | Exclusive Reset removes only the spurious derived order row. The quote events remain. |
| `SourceOwnerMissing`, `SourceOwnerConflict` | Stop this stream's recovery. Historical ownership needs a separately approved data decision; never infer it from EF, passenger email or the next caller. |
| `SourceMissing`, `SourceStreamTypeInvalid`, `SourceEventUnsupported`, unreadable/invalid payload, version gap | Investigate source integrity/compatibility. Reset is not permission to delete or rewrite events. |
| Storage or schema prerequisite failure | Restore connectivity/compatible schema through the separately authorized procedure, then revalidate. |

Quote/re-quote streams with no Hold are valid and do not materialize an order.
An ownerless order-bearing history is not a valid non-materialized quote. Source
failures remain visible; independent valid streams can still be repaired.

## Restore derived rows under exclusive maintenance

1. Record the intended artifact and database, validation report and affected IDs.
2. Stop admission of booking writes and drain/stop **all** normal booking writers,
   webhook handlers, notification workers and projection consumers on every node.
   Wait for in-flight Incremental transactions to complete.
3. Keep those processes stopped while running:

   ```text
   dotnet run --project apps/Travel.Host -- booking-read-model rebuild --execute --exclusive-maintenance
   dotnet run --project apps/Travel.Host -- booking-read-model validate
   ```

4. Review per-stream results and exit codes. Keep writers stopped if a source issue
   or a failed rebuild remains relevant to the affected orders. Re-run validation
   after repairing the cause; partial runs are safe to repeat.

Both rebuild flags are mandatory. `--exclusive-maintenance` acknowledges that the
operator has stopped other writers; it supplies **no distributed fencing or lock**.
Overlapping/online Reset is unsupported. A stream-version token cannot detect a
same-version repair racing an Incremental writer.

Rebuild calls the same reconciler/event applier as normal delivery, using Reset.
For an existing valid order it retains row Id and saves reconstructed fields plus
checkpoint atomically. A failed save leaves the previous row intact. It never
clears all orders first, rewrites events, deletes inbox records, alters other module
tables, calls providers or derives notifications from history.

## Replay selected messages after projection repair

1. Re-run `inspect` and record the **exact** dead-letter message IDs. Fix source or
   storage errors first. Confirm the order validates at its captured source version.
2. For each reviewed ID, while workers remain stopped:

   ```text
   dotnet run --project apps/Travel.Host -- booking-read-model replay --message-id <envelope-guid> --execute
   ```

3. Verify `MarkedReplayable`, then resume normal workers. This flag does not mean
   the message has already been delivered. Wolverine moves selected replayable
   envelopes back into delivery when its recovery agent resumes.
4. Wait for the dependency cycle: a callback can append a ticket/refund event and
   enqueue another reconcile request. Inspect again after that request is processed;
   validate the new source version and check the selected IDs' final disposition.

The allowlist contains only `ReconcileOrderReadModel`, `ProcessDuffelWebhookCommand`
and the confirmation/cancellation/ticket notification messages. User booking
commands, arbitrary message types, empty IDs and absent DLQ IDs are refused. No
wildcard/type-wide replay is offered. Reconcile can be replayed for a missing/behind
row only when validation finds no other issue; any derived-field mismatch requires
Reset. Dependent callbacks/notifications require a clean projection first.

Replay uses Wolverine's exact-ID API. It does not edit payloads, clear inbox
`ProcessedAt`, discard dead letters or publish a replacement envelope. Normal
successful delivery may remove its own DLQ record. Leave unrelated DLQ records
untouched; their presence must not be hidden to make recovery appear successful.
Already-processed callbacks keep their normal acknowledgement guard. Notifications
can be suppressed by their existing state rules. SMTP remains at-least-once; SSE is
best effort for an active connection, not reconnect replay.

## Read the result

JSON output contains safe issue codes, stream IDs, progress/counts and final report.
Keep stdout and the process exit code together. A failed/partial run is not success.

| Exit code | Meaning |
|---|---|
| 0 | Validation/rebuild succeeded, inspection is consistent, or selected replay was marked. These are distinct outcomes. |
| 1 | Per-stream validation/rebuild problem, partial enumeration, or replay refused. |
| 2 | Invalid arguments or missing mutation flags; no database access. |
| 3 | Configuration/schema prerequisite missing or inaccessible; no schema repair attempted. |
| 4 | Unexpected maintenance failure; some prior streams may have committed. |
| 130 | Requested cancellation; use recorded progress and revalidate before resuming. |

Do not interpret a successful local test as shared/live recovery evidence.

## Rollback limits

Prefer forward repair or a **tested WS4-compatible artifact**. Such an artifact must
understand new owner/re-quote fields, enforce ownership and expected-version writes,
handle reconcile messages and support legacy notification payloads. Keep the additive
checkpoint column. Destructive migration Down is not routine rollback.

A pre-WS4 handler surface cannot process the new reconcile contract. Tolerating
additive JSON fields does not establish safe rollback: old writers can overwrite
derived fields while leaving a new checkpoint unchanged, and old ownership checks
differ. Resetting the checkpoint to -1 does not fix these incompatibilities.

Current tests cover old payload shapes and a pre-WS4-style missing-handler fixture;
they do **not** certify any historical deployable binary. No old artifact is approved
for rollback by this work. An emergency downgrade needs a separate procedure for
stopped writers, pending envelopes, ownership/security semantics and subsequent
validation/repair. After any authorized old-writer interval, validate and exclusively
Reset before trusting the new projection again.
