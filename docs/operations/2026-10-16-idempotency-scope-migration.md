# Flights idempotency scope migration

Source migration: `20261016090418_ScopeIdempotencyKeys`. Implements ADR 0017's owner-and-route scope. It changes only `flights.idempotency_keys`' primary key from `key` to `(key,user_id,route)`; no receipt, response, timestamp or expiry is rewritten or deleted.

The reviewed upgrade operation is:

```sql
ALTER TABLE flights.idempotency_keys DROP CONSTRAINT pk_idempotency_keys;
ALTER TABLE flights.idempotency_keys ADD CONSTRAINT pk_idempotency_keys
    PRIMARY KEY (key, user_id, route);
```

EF wraps these statements and its migration-history update in one transaction. Existing globally unique keys necessarily satisfy the wider key. PostgreSQL takes a table lock while replacing the constraint/index; plan a bounded maintenance window after checking table size, active requests and lock holders. No live application is authorized by this source change.

Apply the checked-in migration through the separately authorized deployment job before switching to the updated application. Production initialization validates this migration and closes business-consumer startup while it is pending. Drain old writers for the schema/application switch: their EF identity map still treats key as global, so mixed-version cross-scope writes are not an accepted compatibility window. The request and response contracts and 24-hour receipt retention are unchanged.

Rollback refuses to recreate a global key if any retained key now has multiple owner/route scopes. It raises an explicit exception before dropping the composite constraint; the transaction preserves both schema and receipts. Do not delete receipts to force rollback. Retain the scoped schema/application or wait for normal, separately approved expiry cleanup and check compatibility again. When there are no overlapping keys, Down restores the global constraint without changing rows. Binaries alone do not undo a migration.

Offline SQL inspection commands (no database update):

```powershell
dotnet ef migrations script 20261003115745_AddSavedTravelers 20261016090418_ScopeIdempotencyKeys --project modules/flights/Travel.Modules.Flights.Infrastructure --output TestResults/technical-audit-remediation-2026-10-16/idempotency-up.sql
dotnet ef migrations script 20261016090418_ScopeIdempotencyKeys 20261003115745_AddSavedTravelers --project modules/flights/Travel.Modules.Flights.Infrastructure --output TestResults/technical-audit-remediation-2026-10-16/idempotency-down.sql
```

`IdempotencyScopeMigrationTests` exercises historical receipt preservation, independent route creation, refused rollback with both rows intact, and successful rollback after a test-only second-scope removal, exclusively in disposable PostgreSQL. It does not claim target application, lock duration or live rollout acceptance.
