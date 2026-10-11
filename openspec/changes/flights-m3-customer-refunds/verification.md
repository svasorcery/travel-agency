# Verification — customer refunds implementation

2026-10-11. Uncommitted source от base7aaf3d60e852670dd4a1f2f257d3e26995e5306f.
Base CI/прошлыеPR не подтверждают этот implementation.

| Local check | Result | Boundary |
|---|---|---|
| Audited isolated .NET |170PASS|Core/JSON/projection/fake sessions/adapter/parser/explicit120s+deadline budget/historical keyless subset; no assembly PII fixture|
| Shared Vitest |143PASS|Client contracts|
| Full Angular |413PASS|Card/service/report guards, до final formatting|
| Fictional Node |65PASS|In-memory presets/retention/counter<=1|
| Fictional Playwright |52PASS|3independent refund starts,9adults/4legs,mobile/reload/stale/privacy|
| Flights Integration source build |PASS,1Verify solution-discovery warning|No DB/runtime execution|
| Host Integration source build |PASS,27ASP.NET AD0001 analyzer warnings|No Host/PII execution; warnings not suppressed|
| Final formatting/production web build/OpenSpec/harness |PASS scoped CSharpier/Biome, web build, strict spec/harness133|Biome warnings/info and existing style warning retained|
| Independent code review |4Important fixed,RED7->GREEN168|Read-only review and one fix pass:implementation-review.md; runtime CI still pending|

Temporary offline project uses audited Compile Includes without linked FlightPiiTestRing.
Previous OS temp project was cleared between turns and recreated with the same boundary;
no tracked tooling. Ordinary .NET test assemblies never run locally: filter does not guard
assembly key bootstrap. Path:offline-probe-path.txt.
Nx stalled before build/output; only owned CLI descendants stopped.
Supported NX_ISOLATE_PLUGINS=false then passed the same target; repo config unchanged.
ENOSPC recovery and RED/GREEN evidence:process-log.md.

## Requirements and proof

| Requirement / scenarios | Source/tests | Evidence |
|---|---|---|
| Separate supplier/customer money; no settlement; proven dispatch|StatusTests/card/browser|Local PASS; runtime CI pending|
| Whole-order penalty/unsupported facts|PolicyTests100/80,110/100,FX,credits/destination|Local PASS|
| Eligibility/restart/legacy/unresolved/operator|PolicyTests/PersistenceTests/RestartTests|Local PASS; persisted CI pending|
| Funding after proven cancellation|policy/status/card, no settlement precondition|Local PASS|
| Zero/unpaid/missing facts|PolicyTests,zero counter0,legacy labels|Local PASS|
| Retained/concurrent admission and historic IDs|Decision/Orchestration/PersistenceTests|Local PASS; CAS/outbox CI pending|
| One dispatch/crash/failed admission|ExecutionTests,RestartTests,enrolled writer|Local PASS; actual restart/rollback CI pending|
| Matching/lost/mismatch/no-effect receipts|Decision/Review/FakeGatewayTests|Local PASS|
| Finite escalation/no browser/delivery failure|StatusTests/policy registration/RestartTests|Local deadline PASS; scheduled/DLQ CI pending|
| Privileged review/owner/replay/quiescence/late receipt|Core ReviewTests/CustomerRefundHttpTests|Core/parser PASS; HTTP CI pending|
| Private status/EF lag/foreign/stale/read error|Status/service8tests/browser|Local PASS; HTTP CI pending|
| Truthful airline/no amount/race guard|SupplierReportTests/updatedWebhookTests|Core PASS; persisted webhook CI pending|
| Resolvable report/no op/competing/old replay|SupplierReportTests/legacy hashes/metadata|Core PASS; Host CI pending|
| Historical/services compatibility|historical isolated subset/ReplayTests/Angular/browser|Local PASS; full existing CI pending|
| Fictional independent/Production exclusion|TestOnly/nonProduction registration/Node/browser|Demo PASS; architecture/startup CI pending|

## Existing CI gate

Flights Unit/Integration, Host HTTP/Integration, architecture, AspireSmoke and PR E2E must run
on exact implementation head after separate publication authorization.
Host replacement/receipt rollback/admission rollback tests are source-compiled only.
Restart fixture seeds admission170s in past but retains exact180s duration; old scheduled
work survives replacement without userGET or republish.
Diagnostics inspection correlates assessment/execute, generic replay rejects them; only
CustomerRefundDeadline is replayable.
HostWebContractTests.OpenApi baseline includes Flights and needs reconciliation in allowed
CI/Host run. Local Host forbidden; ungenerated snapshot is not fabricated manually.
Paid eval skip does not replace mandatory evidence.

User authorized uninterrupted publication/existing CI continuation after prior gate proposal. Stage/commit/push/PR/CI proceed; merge/finalization remain governed separately. No schema/migration/key bootstrap/deploy
or real supplier/payment/AI calls performed.
Next:final formatting/build/harness, fresh whole-change review/fixes, then concrete publication
decision with explicit generated snapshot/runtime gates. Sync/archive separately authorized.
Fare summary/SSE UI/fullM3/real refunds are not delivered by this slice.
