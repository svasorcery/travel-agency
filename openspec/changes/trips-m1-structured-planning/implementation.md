# Trips M1 implementation record

## Identity and authority

- User approved the complete plan and requested implementation, a separate worktree from fresh dev, independent review and fixes.
- Fresh `origin/dev` fetched 2026-10-20: `799e31a6b31ff256ba017861cd911e547e4e9896`.
- Native worktree: `C:/Users/Vladimir_sva/.codex/worktrees/trips-m1/travel-agency`.
- Branch `codex/trips-m1-structured-planning`; initial HEAD equals the requested base and ancestry was checked before branch creation. Original Rail checkout/branch were not switched.
- Approved plan copied without other uncommitted Rail changes. Design SHA256 `D2311040632CC1905D73FA54D9442D465BCD9740F249046392EB199B47D389BD`; tasks SHA256 `980BEB9E2921AC8A0CFF0EF41136F0EB6B2255DA6461C810039FA715197AC6A2`.
- Source implementation and ordinary source checks are authorized. No staging/commit/push/PR/merge/publication or deployed changes are implied. Model/provider calls remain excluded.

## Preparation

The new worktree had no ignored dependencies/assets. Restoring exact lockfile dependencies is preparation, not a product regression. Root `npm ci --ignore-scripts` deliberately avoids changing shared Git hooks; the pinned OpenSpec tool gets its own `npm ci --prefix tools/openspec --ignore-scripts`. NuGet restore is local to worktree build assets. No shared node_modules junction is used.

Architecture baseline and implementation progress are recorded below as checks finish. All task checkboxes remain unchecked until their complete acceptance is evidenced.

## Baseline and task receipts

- Pristine architecture suite:183executed/183passed,0failed/notExecuted. Receipt `tests/Travel.Tests.Architecture/TestResults/trips-baseline.trx`; source at requested base before product edits.
- CI inventory RED: new Trips Fact/Theory tests invalidated empty-scaffold classification. GREEN: added dedicated full Trips unit/integration lanes and unconditional E2E prerequisites; validator passed,62tests passed/1platform skip,0failed. This is source workflow validation, not a GitHub CI run.
- Core/Application receipt:53/53unit,0failed/skipped; initial absent-aggregate RED and populated-new-date regression were retained in worker report. Tasks1.1/1.2/1.3/2.1 verified and marked; canonical production codec/store coverage for2.2/2.3 is accepted with Infrastructure receipts, not fakes alone.
- Host build with Trips facade:0warnings/0errors, no application startup.
- First post-implementation architecture run:181passed/1failed/182total. Sole failure identified three events outside required `Core.DomainEvents`; moved them without relaxing policy. `trips-architecture.trx` is retained as RED; new GREEN run must have a distinct filename. The test denominator decreases183→182 because the obsolete Trips scaffold-only theory row is retired; new Trips positive layer/Api selectors replace that assumption and all module-boundary rules remain.
- Infrastructure intermediate receipts:35lean HTTP/codec/protection tests green, including nonvacuous emitted log/span sentinels and exact canonical byte boundary; first4owned PostgreSQL tests green. Final6relational tests and query-marker privacy checks are still being run; do not substitute these intermediate counts for final acceptance.
- Frontend intermediate receipts:253/253shared client tests,559/559Angular tests, first2fictional browser cases green. Additional focus/invalid-id/uncertain barrier/late-read checks are being finalized; final broader runs supersede these intermediate counts.
- Harness134/134 and README13/13 source checks passed. CI inventory62passed/1platform skip with new Trips unit/integration lanes and fictional browser command passed. No GitHub CI run or publication performed.

## Integration rulings

- M1 has no new public capability registry. Existing authenticated `/api/trips/destinations` is the browser-only availability probe. Disabled prefix returns404 before authentication/dependency resolution; enabled anonymous request401 exposes no private data but allows the navigation entry to lead to login. Ready200 and typed unready503 remain distinct; network/malformed failure is not proof of enablement.
- Work is partitioned across Core/Application, Infrastructure/Api and frontend ownership. Parent owns Host/architecture/CI/navigation integration and final acceptance. Central task checkboxes are changed only by parent after verifying receipts.
- Existing Flights fictional browser guard rejected exactly `GET http://127.0.0.1:4201/api/trips/destinations` from root eager discovery. Diagnostic attempt1failed/76notRun, then one confirming trace failed. Scope discovery to normal source mode in root App; Trips-demo page performs its own authenticated discovery. Existing guard/allowlists are unchanged; full77-case regression is pending. This is an explicit demo-isolation integration decision, not a provider/runtime fallback.

## Reviewable additive schema scope for task3.2

Concrete model: `modules/trips/Travel.Modules.Trips.Infrastructure/Persistence/TripsDbContext.cs`. One new Trips-owned table `trips.trip_plans`: UUID trip_id primary key; owner_issuer varchar512 and owner_subject UUID; positive bigint revision concurrency token; created_at/updated_at timestamptz; protected_payload text. One owner+updatedAt DESC+tripId index. No existing module table/column or row is changed; no data backfill. Rollback normally disables Trips and preserves rows; destructive Down is not automatically applied.

Only migration source/snapshot/SQL review and a separately approved disposable PostgreSQL fixture are proposed. No connection to an existing database, no target credentials, no model/provider calls, no Production startup migration or schema/key initialization is included.

Explicit user response received: «Да: migration source и одноразовые PostgreSQL-тесты». Task3.2 source generation and application inside the owned disposable fixture are now authorized, including fictional data and temporary test-only keys/certificates. Existing databases/target environments and real providers remain excluded. Infrastructure worker owns these tests and serializes Docker-heavy execution on the shared daemon.

## Final outcome

Source/local implementation and independent review corrections are complete; current progress20/21. Only6.2ordinary GitHub CI remains unchecked because publication was not requested. The [verification matrix](verification.md) records final counts, scope/fixtures, source hashes, intentional skips/warnings and remaining external gates. The [independent implementation review](implementation-review.md) records all six P2 and their accepted corrections. No planning approval is represented as runtime acceptance.

## Publication stage authorization

2026-10-20: the user explicitly authorized committing Trips M1, pushing this branch, creating a PR into `dev` and fixing CI until green, with no merge. This supersedes the earlier source-stage publication restriction only for that bounded scope. Freshly refetched `origin/dev` remains `799e31a6b31ff256ba017861cd911e547e4e9896`; original ancestry is retained. Before staging:98reviewed-scope files,82code-file hashes match the accepted final manifest,0unexpected files. Task6.2 stays open until actual ordinary CI acceptance is recorded. No merge/auto-merge/target/provider action is authorized.
