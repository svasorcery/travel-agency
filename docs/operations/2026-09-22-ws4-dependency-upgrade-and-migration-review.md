# WS4 dependency upgrade and checkpoint migration review

## Scope and authorization

The user approved Task 6 migration source, Designer/snapshot and idempotent SQL generation
for review, without applying the new migration. The user subsequently approved the
Marten security upgrade that blocked generation. Deployment, live validation and application
of the new migration to shared/live databases remain separate gates. After artifact review,
the user separately authorized application and verification in disposable PostgreSQL containers.

## Dependency changes

- Marten 8.37.4 -> 9.14.0.
- Wolverine packages 5.13.0 -> 6.17.0, whose NuGet manifest requires Marten 9.14.0.
- NATS.Client.Core 2.7.2 -> 2.8.2, required by Wolverine's NATS dependency.
- Add WolverineFx.RuntimeCompilation 6.17.0 to Host, AI and the standalone Flights
  integration-test host to preserve the existing dynamic handler compilation mode.

The NuGet advisory GHSA-rfx3-98h7-v3xp identifies SQL injection in Marten <=9.12.0
and lists 9.13.0 as patched. Audit remains enabled; no vulnerability suppressions were added.
Reference: https://github.com/advisories/GHSA-rfx3-98h7-v3xp

## Compatibility adaptations

Marten 9 uses compile-time generated event dispatch. BookingAggregateProjection is a
partial Infrastructure projection that anchors generation with a Create(OfferQuoted)
method and delegates all state changes to the existing Core Apply methods. It is a live
single-stream projection, not a second EF read-model implementation. Core retains no Marten
dependency.

Host constructs Marten options through its service-provider factory and consumes validated
HostConnectionOptions first. This preserves the existing production configuration error
contract with Marten's earlier connection validation.

Wolverine 6 adds streaming methods to ICommandBus and a Solo heartbeat hosted service.
It also rejects opaque scoped DI factories by default. AI explicitly opts only AiDbContext
into service location because Aspire owns its scoped factory. The global rejection policy
remains enabled. The NATS replica load-balancing/failover test reproduces the missing opt-in
as a request timeout and passes with the type-level opt-in.
Test doubles implement the unused streaming methods explicitly. Configuration/health fixtures
remove both Wolverine-owned hosted services while retaining strict descriptor-count assertions.

The schema validation fixture now builds the Marten schema with the same Wolverine-integrated
registration as the validation path. A plain Marten store no longer describes that schema:
the integrated configuration includes mt_doc_envelope. Production validation remains read-only.
Do not assume a binary-only upgrade is safe against an older live schema; the existing
platform schema gate and rollout procedure must assess all Marten/Wolverine changes.

## Task 6 review contract

Only flights.order_read_model.projected_stream_version is added:
bigint NOT NULL DEFAULT -1, explicitly saved by EF and used as a concurrency token.
Existing rows start at -1 (untrusted checkpoint); new entities also start at -1.
No source events, owners, inbox rows or order fields are backfilled.

The intended Up adds that column; Down removes only that column and loses its checkpoint
values. Routine rollback should retain the additive column. Reset/rebuild must use the
planned reconciler under exclusive maintenance; it is not implemented or executed here.

Before rollout: review SQL and locks, complete
reconcile/cutover work, validate historical stream ownership and checkpoints, stop old
writers before enabling new projection writers, and validate readiness after the explicit
schema gate. A constant default does not remove the ALTER TABLE lock requirement; deployment
must choose bounded lock/statement timeouts and an appropriate maintenance window.

## Evidence boundaries

Dependency regression runs against the baseline EF schema before generation of the new
migration. During that run only, the pending ProjectedStreamVersion mapping is excluded and
its metadata test is filtered out. The mapping is restored before generation and separately
tested without opening a database connection.

Generated migration/SQL and metadata validation are source evidence. The separately authorized
disposable application step now proves fresh installation, existing-row upgrade, explicit
version persistence and concurrent-update protection. No live evidence or deployment claim
is made.

## Recorded verification (2026-09-22)

- Restore succeeds with audit enabled. Flights Infrastructure vulnerability audit including
  transitive dependencies reports no vulnerable packages.
- Baseline-schema upgrade regression: Flights integration 197/197, Flights unit 435/435,
  AI 45/45, architecture 149/149, contracts 9/9.
- Host run initially passed 146/147; the remaining NATS replica test exposed the scoped
  factory compatibility issue. After the AiDbContext opt-in, its isolated rerun passed.
  Focused Host startup/schema/HTTP tests passed 14/14. These are combined run results,
  not a claim of a final single green solution run.
- After restoring Task 6 mapping: two connection-free metadata/Up/Down tests passed;
  EF reports no pending model changes.
- Generated migration: 20260922132058_AddOrderReadModelProjectedStreamVersion.
  SQL: artifacts/ws4-booking-projection-migration.sql. It includes guarded baseline
  creation and the guarded single-column upgrade, with schema-qualified migration history.
  Snapshot changes are the new property and EF ProductVersion 10.0.5 -> 10.0.8 metadata.
- New-migration checks: 5/5 (two metadata/operation tests and three PostgreSQL tests).
  Fresh MigrateAsync persists -1, explicit zero and positive versions; query DTO retains
  the checkpoint. Baseline-to-checkpoint idempotent SQL is executed twice and preserves
  existing order fields while assigning -1. A stale EF writer cannot overwrite the winner's
  status or version.
- The database fixture reuses the module design-time configuration and replaces its connection
  string with the fixture-owned Testcontainer connection before any database operation.
  Containers are disposed by IntegrationTestBase. No shared/live connection is used.
- With the generated migration applied only in disposable fixtures: Host schema/readiness
  5/5; existing Flights persistence/provider parity/Marten outbox regression 9/9.
  Formatting and git diff whitespace checks pass; EF reports no pending model changes.

Commands used for the disposable checkpoint:

```powershell
dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration/Travel.Modules.Flights.Tests.Integration.csproj --no-restore --filter "FullyQualifiedName~OrderReadModelMigration" --blame-hang-timeout 2m
dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --no-restore --filter "FullyQualifiedName~DatabaseInitializationTests" --blame-hang-timeout 2m
dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration/Travel.Modules.Flights.Tests.Integration.csproj --no-restore --filter "FullyQualifiedName~FlightsDbContextTests|FullyQualifiedName~FlightsDbContextOptionsParityTests|FullyQualifiedName~BookingCommitOutboxTests|FullyQualifiedName~MartenWolverineOutboxTests" --blame-hang-timeout 2m
```

## Self-review and Task 7 continuation

Self-review rechecked the generated single-column Up/Down, explicit checkpoint persistence,
concurrency original values, module-owned database configuration, Marten live replay registration,
Wolverine runtime compiler and the narrow AiDbContext service-location opt-in. No blocking
Task 6 finding remained. Old-binary event compatibility and live schema upgrade remain separate,
unproven rollout concerns.

Task 7 introduces one event applier shared by Incremental, Validate and exclusive Reset,
fresh owned contexts per invocation, and a storage-error classifier. Normal DI denies Reset;
the internal maintenance-context factory is reserved for the later maintenance runner.
Validate performs no writes. Reset keeps the row identity and changes one derived row atomically;
it can remove only a verified quote-only stream's spurious row.

Failure tests exposed and fixed three initial defects: equal-version delivery could accept
a spurious quote-only row, suffix projection could retain a corrupt EF owner, and a payment
without Hold/owner could be mistaken for a valid quote-only stream. Incremental now verifies
the immutable ownership fact from the applied prefix before trusting an existing row.
This adds bounded prefix reads; applying events remains suffix-only.

Task 7 tests cover competing insert/update writers and fresh retry, exact captured target,
unknown event, missing/wrong stream, injected source gap, sentinel and ahead checkpoints,
missing owner, event timestamps, payment-only suffix, re-quote consistency, same-version repair,
read-only validation, unrelated tracked entities, failed save and scoped deletion. The fault
injection deleting an event is limited to the test-owned disposable stream.

Production handlers still use the legacy projector. Task 8 supplies durable delivery/policies;
Task 9 switches commit paths. No automatic convergence or live rollout is claimed at Task 7.

Task 7 final verification: Flights unit 445/445; reconcile plus existing query integration
26/26 (16 reconcile cases); architecture 149/149. Solution build succeeds with the existing
AD0001 analyzer warnings; CSharpier and git diff checks pass. No new schema migration is
introduced by Task 7 and no commit/push is performed in this checkpoint.
