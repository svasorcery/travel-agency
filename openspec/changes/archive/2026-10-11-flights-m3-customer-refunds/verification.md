# Verification — customer refunds implementation

2026-10-11. Source от base7aaf3d60e852670dd4a1f2f257d3e26995e5306f; accepted implementation head f68d8139e8ab79081be67f7f3bf124cd00e59603.
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
| Independent code review |4Important fixed,RED7->GREEN168|Read-only review and one fix pass:implementation-review.md; runtime accepted in existing CI38136072675|

Temporary offline project uses audited Compile Includes without linked FlightPiiTestRing.
Previous OS temp project was cleared between turns and recreated with the same boundary;
no tracked tooling. Ordinary Flights/Host .NET test assemblies never run locally: filter does not guard
assembly key bootstrap. Audited Architecture metadata/IL-only suite170passed through unchanged pre-push hook. Path:offline-probe-path.txt.
Nx stalled before build/output; only owned CLI descendants stopped.
Supported NX_ISOLATE_PLUGINS=false then passed the same target; repo config unchanged.
ENOSPC recovery and RED/GREEN evidence:process-log.md.

## Requirements and proof

| Requirement / scenarios | Source/tests | Evidence |
|---|---|---|
| Separate supplier/customer money; no settlement; proven dispatch|StatusTests/card/browser|Local PASS; runtime CI PASS|
| Whole-order penalty/unsupported facts|PolicyTests100/80,110/100,FX,credits/destination|Local PASS|
| Eligibility/restart/legacy/unresolved/operator|PolicyTests/PersistenceTests/RestartTests|Local PASS; persisted CI PASS|
| Funding after proven cancellation|policy/status/card, no settlement precondition|Local PASS|
| Zero/unpaid/missing facts|PolicyTests,zero counter0,legacy labels|Local PASS|
| Retained/concurrent admission and historic IDs|Decision/Orchestration/PersistenceTests|Local PASS; CAS/outbox CI PASS|
| One dispatch/crash/failed admission|ExecutionTests,RestartTests,enrolled writer|Local PASS; actual restart/rollback CI PASS|
| Matching/lost/mismatch/no-effect receipts|Decision/Review/FakeGatewayTests|Local PASS|
| Finite escalation/no browser/delivery failure|StatusTests/policy registration/RestartTests|Local deadline PASS; scheduled/DLQ CI PASS|
| Privileged review/owner/replay/quiescence/late receipt|Core ReviewTests/CustomerRefundHttpTests|Core/parser PASS; HTTP CI PASS|
| Private status/EF lag/foreign/stale/read error|Status/service8tests/browser|Local PASS; HTTP CI PASS|
| Truthful airline/no amount/race guard|SupplierReportTests/updatedWebhookTests|Core PASS; persisted webhook CI PASS|
| Resolvable report/no op/competing/old replay|SupplierReportTests/legacy hashes/metadata|Core PASS; Host CI PASS|
| Historical/services compatibility|historical isolated subset/ReplayTests/Angular/browser|Local PASS; full existing CI PASS|
| Fictional independent/Production exclusion|TestOnly/nonProduction registration/Node/browser|Demo PASS; architecture/startup CI PASS|

## Runtime proof and remaining boundary

Existing CI on f68d8139e8ab79081be67f7f3bf124cd00e59603 executed Flights Unit/Integration,
Host HTTP/Integration, architecture, AspireSmoke and PR E2E successfully.
Host replacement/receipt rollback/admission atomic rollback tests passed in that existing suite.
Restart fixture retains exact180s duration while seeding admission170s in the past; scheduled
work survived replacement without owner GET or republish.
Diagnostics correlates assessment/execute and refuses generic replay; deadline only is replayable.
Host generated OpenAPI baseline was reconciled from actual CI output and subsequently passed.
Paid evals remain skipped by policy and substitute for no mandatory lane.

Stage/commit/push/PR/CI continuation is user-authorized. No source migration, deployment,
local Host/AppHost/DB/key provisioning, or real supplier/payment/AI calls occurred.
Merge and canonical sync/archive finalization choice is pending while acceptance docs are prepared.
This accepts this fictional slice, not fare summary/SSE UI/fullM3 or real refunds.

## Exact-head CI acceptance (2026-10-11)

[CI38136072675](https://github.com/svasorcery/travel-agency/actions/runs/38136072675) completed success on f68d8139e8ab79081be67f7f3bf124cd00e59603 for [PR41](https://github.com/svasorcery/travel-agency/pull/41): all13normal jobs success, paid evals skipped by unchanged policy. FlightsUnit1373, FlightsIntegration397, HostHTTP394, HostIntegration52, Architecture170, AspireSmoke5 and actualPR-E2E2passed. Generated Host OpenAPI98905bytes baseline matched after first-run reconciliation (SHA25612a2aa9b41c55af6036c88a790844688f48f67dbb59c8c71f6845451d9790570).

This accepts fictional source/runtime contracts and durability scenarios. No real PSP/bank credit/supplier settlement, provider live or deployment proof follows. Docs/finalization changes require their own exact-head CI; prior-run receipts are attributed to the tested implementation head.
