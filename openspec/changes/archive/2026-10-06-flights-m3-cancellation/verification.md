# Flights M3 — source and verification evidence

Status:2026-10-06. Final product head49e203c3e2fb89e393c57cf47578c175cf780176 passed CI37464429596: all13normal checks including E2E. PR36 merged into dev ascf9731a2c43f16cf8e44502b72b6573ee7e1308a; postmerge CI37470390776 passed all12normal checks. E2E is PR-only and skipped postmerge; paid evals skipped in both. Original fetched base31a3803a4d558199eec79b48dc8cf91e078ac681 and M1 ancestry retained. Docs-only archive closure is authorized and prepared; its own exact-head CI/merge/postmerge and cleanup are not yet claimed. No real supplier/payout/deployment proof.

The canonical requirements are the 17 requirements / 38 scenarios in [spec](specs/flights-whole-order-cancellation/spec.md). The table separates local proof, actual existing-CI execution and excluded supplier/payout scope. Compilation alone is not a runtime proof.

## Observed local results

| Boundary | Result | Limits |
| --- | --- | --- |
| Isolated .NET Core/replay, loopback Duffel, signed-JWT lean HTTP, actual Wolverine HTTP metadata | 458/458 PASS | Exact repository sources linked by ignored CoreVerification project; no assembly PFX fixture, real Host, DB or schema. Includes new cancellation metadata and four old profile/search regressions. |
| Shared TypeScript contracts | 133/133 PASS | Strict financial source, terminal/history compatibility, malformed response rejection. |
| Angular | 386/386 PASS after requested self-review | Owner epoch, consent, polling, stale projection, source labels, per-booking deadlines and denied-read/late-callback eviction. |
| Fictional Node API | 55/55 PASS | Five M3 cases include exact replay, lost create/confirm response, delayed two-tab requests and local save failure. |
| Focused browser acceptance | 46/46 full fictional browser suite PASS; focused4/4 and semantic/keyboard1/1 retained | Existing isolated Node+Angular configuration.360px whole-party keyboard consent, lost reply/reload, retained feed anchor/focus. |
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

| Canonical requirement | Actual local proof / source | Decisive proof / boundary |
| --- | --- | --- |
| Whole-order eligibility and ownership | Unit/Cancellation/CancellationDecisionTests, CancellationCommandValidationTests, CancellationSourceOwnershipTests; HTTP/CancellationJwtHttpTests; mobile whole-party browser case | PASS: Unit/Host scope and actual stream/routing checks in CI37450413774. |
| Current concrete cancellation terms | CancellationTermsTests, DuffelCancellationMapperTests; loopback DuffelFlightCancellationProviderTests; Client/flights-cancellation.decoder.spec | PASS: offline/loopback contract and strict reader tests; actual supplier sandbox is excluded. |
| Consent bound to immutable proposal | CancellationDecisionTests, CancellationFingerprintTests, CancellationLifecycleTests; Web/flight-cancellation.service.spec | PASS: exact-consent unit/UI and competing-writer integration checks. |
| Durable operation discovery | CancellationStatusTests; lost-response/reload browser proof; Integration/Cancellation/CancellationRestartTests source | PASS: stored-work Host A→B without client GET or republish. |
| Single dispatch and stage deduplication | CancellationRecoveryDecisionTests, CancellationReplaySafetyTests, ConfirmationBarrierTests; Node two-tab delayed-response case | PASS: claim-before-send/restart and duplicate-worker integration checks. |
| Separate conclusive and inconclusive outcomes | CancellationEvidenceTests, CancellationRecoveryDecisionTests, offline Duffel provider cases; strict client decoder | PASS: typed evidence/outcome and actual outbox delivery under failure. |
| Evidence-based recovery | CancellationEvidenceBindingTests, ManualPreparationResolutionTests; exact-ref loopback GET mapping | PASS: exact-ref recovery, manual preparation and restart; no supplier-ID heuristic. |
| Local finalization after external success | Source: CancellationRestartTests.ExternalSuccessLocalRollbackRecovers; pure late-positive decision cases | PASS: actual pre-commit rollback and stored observation finalization. |
| Projection lag and monotonic user evidence | CancellationProjectionTests, CancellationStatusTests; Web stale Held/404/history/terminal cases; browser reload | PASS: real checkpoint/rebuild/convergence, signed HTTP and monotonic UI. |
| Explicit bounded manual recovery | ManualResolutionDecisionTests, ManualPreparationResolutionTests; signed-JWT HTTP scope/body cases | PASS: actual atomic audit/retry and late original worker; attestations remain operator trust. |
| Separate supplier refund and customer payout | Financial terms vs outcome provenance regression; decoder; component and browser amount/destination/notice assertions | PASS: separate source labels and notice; payouts are excluded. |
| Historical compatibility and safe activation | CancellationReplayTests, CancellationProjectionTests, ConfirmationBarrierTests; legacy terminal-noop/TermsRequired source tests | PASS: Unit and real legacy marker/writer/webhook regressions. |
| Evidence boundaries of pilot | Harness/strict checks and this report explicitly separate synthetic, metadata, durable and supplier evidence | PASS: exact-head13checks and preserved inventory; real supplier acceptance excluded. |
| Safe end of an unaccepted review | CancellationDecisionTests consent/abandon exclusion; CancellationLifecycleTests expiry/unknown barrier; UI acceptance reset | PASS: consent/abandon/expiry matrix and integration writer behavior. |
| Recovery without returning client | RecoveryScheduleTests, CancellationRecoveryDecisionTests; source: RestartTests and FaultBoundaries partial | PASS: actual persisted five envelopes, slot crash/deadline and storage/DLQ behavior. |
| Shared refresh admission and readable blockers | CancellationStatusTests, CancellationEvidenceBindingTests, HTTP refresh202/429 and scope cases; Web blocker UI | PASS: actual owner/operator shared CAS, HTTP and UI blockers. |
| Stable historical request identity | CancellationReplaySafetyTests, CancellationEvidenceBindingTests; HTTP raw fingerprint/body cases; Node exact prepare/consent replay | PASS: retained receipt/restart transactions, raw-body binding and historical readers. |

The T9 names in tasks are acceptance case labels, not claims that similarly named source methods ran. Pure watchdog/revision/expiry coverage does not by itself prove persisted delivery; runtime results must link to the exact CI head/run.

## Review and implementation refinements

Independent whole-change review found no P1 and four P2. All four are closed at source-review level after the final bounded fix pass: legacy CI fixture adaptation, cold restart injection, missing fault-boundary sources and independent financial-terms provenance. The last fixture fix preserves historical webhook reconciliation without waiting for a nonexistent confirmation attempt. Reviewer ran no tests; actual test evidence above belongs to the implementer.

The ordinary owner demo does not gain an operator resolver/admin flow. Typed operator resolution is exercised against production Core and signed-JWT lean HTTP contracts, with actual durable audit/retry executed in existing CI. This refines T11 proof placement and avoids implementing the same operator decision rules in the Node stub. Product scope and E1/E4 operator API procedure are unchanged.

New routes expose actual200/202 response metadata and required financialSource. Actual isolated Wolverine discovery and full Host OpenAPI snapshot acceptance both passed. The actual first-CI received snapshot was extracted and independently reviewed:8new paths/16schemas/8tags, no historical path/schema changes. It is now the source baseline and actual Host integration52/52 passed on CI37447374393. All13normal exact-head CI gates passed on CI37450413774.

## Current delivery gate

The user has authorized full publication, merge and cleanup. Product PR36 and its postmerge CI are complete. Separate lifecycle repair PR37 passed CI37472769020 (all13normal checks including E2E) and merged as9c1d018f83013fbb9717c00ff8575b9bc3550fe2; its postmerge CI37474489380 is pending. This archive and the canonical capability are prepared. The remaining gates are independent docs review, docs-only exact-head CI/publication/merge/postmerge and cleanup. No local DB/Host/schema/key operation or real supplier request is needed.

## Historical first product-code CI acceptance

Product code head: `575baaf998679817af4b59150dc1afb2cf424ef6`. [CI37450413774](https://github.com/svasorcery/travel-agency/actions/runs/37450413774) completed success on2026-10-06: all13normal mandatory checks, including PR-only E2E, passed. Paid evals skipped by existing policy.

Actual results:1242/1242 Flights Unit;384/384 Flights Integration;52/52 Host Integration;133/133 client;377/377 Angular;55/55 fictional Node API;46/46 fictional browser cases;2/2 whole-stack E2E. Host HTTP, Identity, AI, architecture, contract and Aspire also passed their existing lanes. The real Host OpenAPI comparison accepted the received-derived snapshot. Stored-work restart, claim-before-send, slot-before-GET, late-positive/manual, duplicate workers, external-success/local-rollback, confirmation/cancellation race, refresh, audit and old projection/notification suites executed in CI.

Runtime repairs and review are documented in [process-log](process-log.md) and [review](review.md). New cancellation/confirmation metadata uses the pinned framework's supported multiple-flush lifecycle at the booking commit boundary; effects still have single dispatch claims.

Historical pre-merge freeze: the accepted code evidence above was retained in the active change until product merge/postmerge and this separately authorized archive closure. Current status is recorded at the top. This docs-only closure will receive its own exact-head checks; its subsequent run/head/merge outcome is reported in PR metadata/chat without a self-referential sequence of journal commits.
