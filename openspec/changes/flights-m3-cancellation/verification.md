# Flights M3 — source and verification evidence

Status: 2026-10-06. Source and local verification are complete for the current permitted boundary; final feature acceptance is pending T9 existing CI. Original fetched base is `31a3803a4d558199eec79b48dc8cf91e078ac681`; checkpoint and M1 ancestry were rechecked. Draft PR36 is published from codex/flights-m3-cancellation; initial head c18e4a57842ad47a1aaaeebb970471a80ffa7ef4/CI37442622939 failed and the documented repair tree awaits its next run. Merge and deployment have not occurred.

The canonical requirements are the 17 requirements / 38 scenarios in [spec](specs/flights-whole-order-cancellation/spec.md). The table records actual local coverage and the separate decisive runtime gate. A compiled integration source is not a passing integration test.

## Observed local results

| Boundary | Result | Limits |
| --- | --- | --- |
| Isolated .NET Core/replay, loopback Duffel, signed-JWT lean HTTP, actual Wolverine HTTP metadata | 458/458 PASS | Exact repository sources linked by ignored CoreVerification project; no assembly PFX fixture, real Host, DB or schema. Includes new cancellation metadata and four old profile/search regressions. |
| Shared TypeScript contracts | 133/133 PASS | Strict financial source, terminal/history compatibility, malformed response rejection. |
| Angular | 377/377 PASS | Owner epoch, consent, polling, stale projection and truthful terminal UI. |
| Fictional Node API | 55/55 PASS | Five M3 cases include exact replay, lost create/confirm response, delayed two-tab requests and local save failure. |
| Focused browser acceptance | 4/4 PASS; subsequent semantic/keyboard case 1/1 PASS | Existing isolated Node+Angular configuration.360px whole-party keyboard consent, lost reply/reload, retained feed anchor/focus. |
| Production web build | PASS | Existing Flights-page stylesheet warning:4.50KiB versus4KiB. Budget unchanged. |
| Integration source build | PASS | Actual Marten/Wolverine fault sources compiled; not executed locally. |
| Host test source build | PASS,0 errors | Existing AD0001 ASP.NET analyzer warnings; no Host or fixture execution. |
| Architecture | 169/169 PASS | Reflection/project-graph suite; no DB/Host startup. Final repeated result passed and is recorded in process-log. |
| Harness | 129/129 PASS + exact inventory |8 Travel skills/instruction pairs,5 agent pairs/legacy commands,6 pinned effective OpenSpec skills. |
| .NET inventory / README | PASS | Inventory62 passed,1 existing skip; README12 passed. No CI changes. |
| OpenSpec strict / Biome / CSharpier | PASS at respective recorded checks | Final documentary edits are checked again before handoff. |

Local ignored logs live under `node_modules/.travel-m3-core-verification/`; they are diagnostics and are not publication artifacts. The safe verifier mirrors existing package versions and the Host test project's existing AD0001 warning treatment. Regular Unit/Integration/Host assemblies remain CI-only because their assembly fixture provisions PFX/key material. Detailed attempts, failures and measured costs are in [process-log](process-log.md).

## Requirement-to-evidence map

Test roots below are repository-relative: `Unit=tests/flights/Travel.Modules.Flights.Tests.Unit`; `Integration=tests/flights/Travel.Modules.Flights.Tests.Integration`; `HTTP=tests/Travel.Host.Tests.Integration/Flights`; `Client=shared/ts/api-client/src`; `Web=apps/web/src/app/flights`.

| Canonical requirement | Actual local proof / source | Remaining decisive gate |
| --- | --- | --- |
| Whole-order eligibility and ownership | Unit/Cancellation/CancellationDecisionTests, CancellationCommandValidationTests, CancellationSourceOwnershipTests; HTTP/CancellationJwtHttpTests; mobile whole-party browser case | Existing CI authoritative stream/production routing. |
| Current concrete cancellation terms | CancellationTermsTests, DuffelCancellationMapperTests; loopback DuffelFlightCancellationProviderTests; Client/flights-cancellation.decoder.spec | Actual supplier sandbox acceptance is outside current authorization. |
| Consent bound to immutable proposal | CancellationDecisionTests, CancellationFingerprintTests, CancellationLifecycleTests; Web/flight-cancellation.service.spec | Real competing writer commits in T9. |
| Durable operation discovery | CancellationStatusTests; lost-response/reload browser proof; Integration/Cancellation/CancellationRestartTests source | Actual stored-work Host A→B run without owner GET/republish. |
| Single dispatch and stage deduplication | CancellationRecoveryDecisionTests, CancellationReplaySafetyTests, ConfirmationBarrierTests; Node two-tab delayed-response case | Claim-before-send and duplicate-worker production-consumer runtime. |
| Separate conclusive and inconclusive outcomes | CancellationEvidenceTests, CancellationRecoveryDecisionTests, offline Duffel provider cases; strict client decoder | Actual outbox delivery under failure. |
| Evidence-based recovery | CancellationEvidenceBindingTests, ManualPreparationResolutionTests; exact-ref loopback GET mapping | Restart/correlation runtime; no heuristic supplier-ID recovery. |
| Local finalization after external success | Source: CancellationRestartTests.ExternalSuccessLocalRollbackRecovers; pure late-positive decision cases | Actual failure before Marten finalization commit and stored observation recovery. |
| Projection lag and monotonic user evidence | CancellationProjectionTests, CancellationStatusTests; Web stale Held/404/history/terminal cases; browser reload | Existing BookingProjectionConvergenceTests with updated metadata-version fixture. |
| Explicit bounded manual recovery | ManualResolutionDecisionTests, ManualPreparationResolutionTests; signed-JWT HTTP scope/body cases | Integration/ManualCancellationResolutionTests; real late original worker after manual escalation. |
| Separate supplier refund and customer payout | Financial terms vs outcome provenance regression; decoder; component and browser amount/destination/notice assertions | No payout acceptance claimed; customer payouts are excluded. |
| Historical compatibility and safe activation | CancellationReplayTests, CancellationProjectionTests, ConfirmationBarrierTests; legacy terminal-noop/TermsRequired source tests | Existing CI legacy marker/writer/webhook regressions. |
| Evidence boundaries of pilot | Harness/strict checks and this report explicitly separate synthetic, metadata, durable and supplier evidence | Exact-head existing CI; later supplier acceptance separately authorized. |
| Safe end of an unaccepted review | CancellationDecisionTests consent/abandon exclusion; CancellationLifecycleTests expiry/unknown barrier; UI acceptance reset | Actual concurrent commits in existing CI. |
| Recovery without returning client | RecoveryScheduleTests, CancellationRecoveryDecisionTests; source: RestartTests and FaultBoundaries partial | Actual persisted five envelopes, slot-before-GET crash, deadlines and storage/DLQ behavior. |
| Shared refresh admission and readable blockers | CancellationStatusTests, CancellationEvidenceBindingTests, HTTP refresh202/429 and scope cases; Web blocker UI | Integration/CancellationRefreshConcurrencyTests shared owner/operator CAS. |
| Stable historical request identity | CancellationReplaySafetyTests, CancellationEvidenceBindingTests; HTTP raw fingerprint/body cases; Node exact prepare/consent replay | Actual retained receipts/restart transactions in existing CI. |

The T9 names in tasks are acceptance case labels, not claims that similarly named source methods ran. Pure watchdog/revision/expiry coverage does not by itself prove persisted delivery; runtime results must link to the exact CI head/run.

## Review and implementation refinements

Independent whole-change review found no P1 and four P2. All four are closed at source-review level after the final bounded fix pass: legacy CI fixture adaptation, cold restart injection, missing fault-boundary sources and independent financial-terms provenance. The last fixture fix preserves historical webhook reconciliation without waiting for a nonexistent confirmation attempt. Reviewer ran no tests; actual test evidence above belongs to the implementer.

The ordinary owner demo does not gain an operator resolver/admin flow. Typed operator resolution is exercised against production Core and signed-JWT lean HTTP contracts, with durable audit/retry source prepared for CI. This refines T11 proof placement and avoids implementing the same operator decision rules in the Node stub. Product scope and E1/E4 operator API procedure are unchanged.

New routes expose actual200/202 response metadata and required financialSource. Actual isolated Wolverine discovery passed; full Host OpenAPI snapshot acceptance is deliberately pending the real existing CI run. The actual first-CI received snapshot was extracted and independently reviewed:8new paths/16schemas/8tags, no historical path/schema changes. It is now the source baseline; acceptance requires the next actual full Host CI comparison.

## Next gate

The user granted publication authorization for branch/stage/commit/push/draft PR and existing CI repair on2026-10-06. The intermediate PR keeps this OpenSpec change active with T9/acceptance unchecked. Repair any real CI failures, review the actual full Host snapshot, and obtain exact-head required checks before acceptance/merge. Docs-only OpenSpec archive/sync closure follows the approved Delivery protocol after product acceptance and postmerge. No local DB/Host/schema/key operation or real supplier request is needed to reach the current gate.
