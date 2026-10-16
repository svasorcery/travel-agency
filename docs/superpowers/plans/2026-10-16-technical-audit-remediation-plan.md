# Technical audit remediation implementation plan

**Goal:** Repair the audited correctness, readiness and dependency defects against baseline `b10cda544e2a8d959124b4ed9b7fa9efe257e6fc`.

**Execution:** One implementation worker, executing-plans, parent final review. The initial implementation phase stayed in the dev checkout without staging, commits or publication. After personal review and the separate dependency rereview, the user explicitly authorized commit, push, PR, merge and task-owned cleanup. Live changes and real model/provider/payment calls remain outside this work. Existing ADRs and OpenSpec remain the capability contracts. Migration source and disposable test database application are authorized.

**Architecture:** Preserve module facades, transport contract identities, financial guards and explicit initialization ownership. Use existing fixtures, fake external services and disposable PostgreSQL/NATS. Preserve Laya default-off/manual experimental behavior.

**Tech stack:** .NET 10, EF Core/PostgreSQL, Marten/Wolverine/NATS, Angular/TypeScript, npm/OpenSpec pinned tooling.

## Ordered tasks

Sequencing exception (parent ruling): resolve only transitive MessagePack `2.5.302 -> 2.5.303` (GHSA-qhrr-8q5h-9q3h) in the AppHost/test chain before task 2A. Restore is otherwise blocked by NU1902; preserve audit enforcement and obtain behavioral RED after restore. Broad dependency remediation remains task 3.

Accounting compatibility ruling (task 2D): every completed NL model call retains a durable row in the existing AI ledger, including unknown usage. Make token/cost columns nullable and add an explicit Outcome with historical `LegacyRecorded` default through a source-only migration. Commit an `Admitted` unknown row before inference; update that row on completion. A failed admission makes no model call; a failed final update retains the unknown row and returns safe failure. Keep v1 messages unchanged. Review preserving Up and guarded Down; apply only in disposable tests.

Task 4 bounds approved by parent: 16 KiB HTTP body; 1000 query characters; 4096 conservative input tokens including full system/user/schema/framing overhead; 512 output tokens and 16 KiB response; 3.5-second inference and 5-second whole-handler reply budget, within the existing 6-second Host wait. Per AI process: one active call, six admissions/minute, USD 0.10 maximum reservation/call and USD 1 rolling-hour budget using explicit pinned pricing estimates. Validate limits; retain unknown reservations and active leases until non-cancelling inference settles. Document process-reset/replica/ingress limits and verify any pricing assertion through public official documentation without real calls.

- [x] 1. Diagnose `BookingProjectionConvergenceTests.One_confirm_recovers_projection_and_version_gated_notifications(true)` first. Add bounded safe envelope/delivery diagnostics in the convergence fixture and assert the original notification recovers. Run the original five-class filter; identify notification count, envelope status and exception type. Repair the actual cause without changing deadlines/assertions or replaying confirmation. Prove one provider/payment dispatch and original-envelope recovery.
- [x] 2A. Add pending-schema disposable transport regression in Host NL transport tests; make failed ordered initialization keep diagnostic HTTP live but prevent business listener startup. Review Host ownership independently.
- [x] 2B. Add PostgreSQL regressions in IdempotencyStoreTests for equal keys across owners/routes/expired foreign scope. Align IdempotencyKeyConfig key and migration snapshot; add preserving migration, reviewed SQL and safe rollback notes. Preserve concurrent same-scope replay/body conflicts.
- [x] 2C-D. Add fake-client NL extractor/handler regressions for null/malformed responses and absent usage. Remove query-bearing exceptions, preserve known usage before parsing and represent unknown usage accurately without mutating v1 wire identity or retrying model requests.
- [x] 2E. Add Flights auth service regressions for concurrent first login, logout during creation/initialization and epochs. Coalesce service-level creation/login using existing Support patterns.
- [x] 2F. Add middleware HTTP regressions for mixed case, trailing slash, oversized/chunked quote and ancillary bodies; preserve 16 KiB rejection before downstream binding/provider calls.
- [x] 2G. Add real event-to-projection-to-email regression for 1–4 legs and legacy format; render readable routes without technical JSON.
- [x] 2H. Add Laya profile unit cases for zero/invalid guards; reject before HTTP and preserve defaults/deadline/manual contract.
- [x] 3. Refresh root and tools/openspec audits, trace advisory roots and reachability, update compatible dependency manifests/locks with exact tooling provenance preserved. Run npm/harness/OpenSpec checks. Record upstream blockers with evidence; no force fixes or blanket ignores.
- [x] 4. Correct current-state process graph/table and module inventory. Add conservative NL input/concurrency/output/deadline/admission/rate/cost bounds at existing owners with fake-model tests. Review narrow frontend boundary lint/auth types; report any material new product decisions before guessing.
- [x] 5. Run appropriate serial broad .NET suites, Angular/API client/build, formatting, architecture, harness and strict OpenSpec validation. Complete ignored progress/report with exact results and remaining live gates. Parent reviews actual diff; no commit.

## Per-task proof and resumability

Each behavioral fix starts with a meaningful failing regression, then the smallest production change and a passing focused check. Docker-heavy projects run serially with `--maxcpucount:1`. Store commands, outcomes, root causes and rulings under ignored `TestResults/technical-audit-remediation-2026-10-16/`. No old audit result counts as fresh verification.

## Review focus

Restart retains durable notification identity; failed schema never admits model work; scoped keys preserve existing rows and rollback refuses collisions; unknown model usage never becomes free usage; auth invalidation cannot revive an obsolete client; all ASP.NET accepted route casing receives identical body protection; emails never show unknown/raw payloads. External ingress, real-model quality, supplier/payment and deployed rollout acceptance remain separate gates.

### Accounting ownership and final verification

The private operation owns its async scope, DbContext and lease until storage settles. Only the outer handler wait is bounded. Cancellation is checked after a late pre-save before any model work; scope disposal precedes lease disposal, and a pending model can retain the lease longer. No late continuation accesses a disposed DbContext. AI handler completion within 5 seconds is separate from Host's existing 6-second wait on discarded Core NATS failures; automatic failure acknowledgements remain off. Direct public Handle tests and transport lifecycle metadata verify those separate intervals.

New standard-token estimates share the approved $5 input / $25 output per million source for reservation and ledger. Historical values, model identity and v1 contracts remain unchanged; provenance and reset/replica/billing limits are in the operations note. The current-state graph includes enabled Support and 24 module projects. Generic browser auth contracts live in app/auth with compatibility exports; the final Support lint rule preserves UI restrictions and rejects nested Flights imports.

The broad verification adds three Host findings requiring explicit classification. Shared saved-traveler fake errors reproduce in isolation and are reset before the foreign-resource assertion. Redaction passes alone and in one controlled parallel pair; the original cross-root attribution remains unproven, while the process-global telemetry test now uses the existing nonparallel collection with unchanged production redaction and privacy assertions. The Core NATS cleanup timeout did not recur in isolation; preserve its 3-second budget, reviewed ownership/order and historical uncertainty rather than claim a production shutdown fix. Complete the final full Host run before source freeze and parent review.

Worker verification and parent personal review are complete: final full Host 560/560, AI 174/174, affected Flights integration 34/34, Support integration 87/87 and all recorded unit/frontend/tooling gates passed. The subsequent braces security backport replaces the temporary waiver in both dependency trees; its recursive installed-copy discovery correction passed 75 dependency/security tests plus four independent Node-resolution cases. Separate rereview closed the P2 finding with no remaining source issue. Publication follows the user's later explicit authorization; live migration application and rollout remain separate.
