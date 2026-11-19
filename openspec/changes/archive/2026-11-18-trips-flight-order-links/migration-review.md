# Trips flight-link schema review

EF Core 10.0.8 generated `20261117192732_AddTripFlightLinks` from the Trips design-time factory. The previous migration is `20261020143618_AddTripsPlanWorkspace`. The runtime and factory both use Npgsql, snake-case names and `trips.__ef_migrations_history`. EF reports no pending model changes.

## Forward change

The [forward SQL](migration-up.sql) creates one initially empty table in the Trips schema. Its primary key identifies one collection per trip. A foreign key to `trips.trip_plans(trip_id)` cascades parent deletion. Each persisted collection has a positive bigint revision, required owner issuer and subject, and a required protected base64 payload of at most 8,192 characters. Owner and revision checks also apply on writes. The codec and separate Data Protection purpose bind the envelope to the owner, trip, schema version and revision.

The script creates the table and inserts one EF history entry in a single transaction. It has no plan-row update or backfill. An existing plan reads as an empty collection at revision zero until the first effective addition. The existing plan serializer, protection purpose and ETag continue to apply to the calendar.

Apply this migration through the authorized schema process before starting an enabled Trips module with the new binaries. Adding the foreign key takes a lock on the referenced parent table and can wait for concurrent writes. With keys ready, the readiness probe checks both table projections using `WHERE FALSE`. A missing child table or mandatory column fails readiness even when migration history is current and the flight-link flag is off.

## Rollback

To withdraw the feature while preserving saved associations, disable `Trips:FlightLinks:Enabled` and retain the table and Trips key ring. An older application can continue working with the existing plan table under the expanded schema. Its parent deletes also follow the database cascade.

The [downgrade SQL](migration-down.sql) drops the entire child table and removes this migration's history entry. Running it permanently discards every saved association unless its ciphertext, binding columns and corresponding key material can be restored from backup. Existing plans and Flights event streams survive. Use the downgrade only after retiring callers requiring this schema and separately approving that data loss or recovery procedure.

## Disposable PostgreSQL verification

On 17 November 2026, the six existing Trips persistence tests and five new link persistence tests passed on disposable PostgreSQL 17 (11/11). With current migration history, renaming the child table or a mandatory column caused an unhealthy readiness result and a failed private read. The complete schema with the fixture key ring returned healthy and revision zero for an existing owner. The concurrency tests observed the competing backend blocked in PostgreSQL before releasing the first transaction. They covered first insert versus another insert and a no-op, and both orderings of trip deletion versus attachment. Additional checks covered stale zero after removal, independent calendar writes and cascading deletion of a damaged child.

A separate disposable PostgreSQL test executed EF's generated downgrade and upgrade (1/1). The downgrade removed the populated child table. The upgrade restored a genuine empty collection. Both steps preserved the original plan row, including its protected payload. SHA256 over UTF-8 SQL with LF line endings and trimmed outer whitespace matched the generated review files in each direction.

| Script | Executed SHA256 |
| --- | --- |
| Forward | `821aa71de5183abf6e7e2d9c804db1b03ecd32af579491a686e43331c766847a` |
| Down | `edaf13ae12faf020d1284ffa8367c8df629c45085e1b6a3f191b4f4aecaa9879` |

Docker reported zero remaining containers for both Testcontainers sessions after the runs. The fixtures used fictional plan data and temporary keys.
