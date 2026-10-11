# Implementation ledger

2026-10-11: user said «го» after the additional plan self-review; implementation of this canonical change is authorized. Stage/commit/push/PR/merge/sync/archive, tooling-source changes and live remain separate gates.

Base/managed HEAD remains 7b7fb0f2831cb5067d4dea025e9e7d9d3f16e347; ancestry checked. Worktree is detached and existing planning files are retained. No branch switch or rewrite.

Existing frontend dependencies restored from unchanged lockfile using npm ci --ignore-scripts --no-audit --no-fund: 1609 packages. No install scripts or auth/CI policy edits.

Task 1: add read versions; TS decoder regressions written first. Whole Flights Unit assembly cannot run locally because its assembly fixture initializes a certificate/keyring. Backend test source/build evidence is separate from actual mandatory CI runtime evidence.

Implementation checkpoint:
- Backend additive GET versions and generic committed notifications implemented; Unit and Integration source compilation passed earlier, no backend test fixture execution.
- Shared client decoder/transport suite:170/170 passed, including19 SSE framing/auth/abort cases (RED missing module/invalid checkpoints→GREEN).
- Coordinator focused suite:9/9 passed, including Retry-After0/backoff regressions, stale acknowledgement, post-open epoch, token timeout and teardown.
- Order page suite:33/33 passed before adding the explicit V12→V11/error regression; a later full Angular run had422passed and1case-sensitive expected-copy failure, fixed in test.
- Node fictional suites:69/69 passed;4new browser ticket/report/refund/offline cases passed. Full existing browser suite is now running.
- Temporary source-edit driver removed; no local evidence scripts are retained as product source.

Rulings:
- Include OrderChangedNotification in the existing diagnostics/replay allowlist — direct code inspection showed the new safe notification otherwise invisible/unreplayable; dispatch allowlist stays closed. Add the nearest replay safety test.
- Distinguish retained snapshot version from fresh GET acknowledgement, and require a post-connect/resume read epoch — prevents old pending GET from satisfying a newer subscription boundary.
- Bound awaiting the existing auth SDK token without changing auth policy; a timeout is observation/network failure, not proof of authorization loss. Consume and ignore late completions by epoch.
- Existing order-page tests were adjusted for independent/coalesced reads and expected terminal fallback, retaining401/403/owner/unknown-write assertions. A temporary404 now preserves proven same-owner tickets, as required by the approved spec.

Remaining at this checkpoint: full browser/Angular/build/static checks, final source review, HTTP/OpenAPI source contract coverage and actual existing-CI/runtime acceptance. Publication and live are not authorized.

Final source checkpoint (2026-10-11):
- Independent source review initially found two P2 defects; actual leaf command freshness admission and original terms-expiry preservation corrected with regression tests. Reviewer rechecked corrections and accepted source.
- Additional hidden200s regression found and corrected observation-budget carryover across resume. No business deadline or duplicate-hint budget is extended.
- Angular final434/434PASS; full fictional browser56/56PASS before narrow resume fix and final SSE browser4/4PASS afterward; Node69/69PASS; client170/170PASS; harness133/133PASS.
- Production Angular build PASS (existing search SCSS warning502bytes); CSharpier805filesPASS; full Biome243filesPASS after formatting3changed browser helpers.
- Source test additions bind creation version/diagnosis, first-Hold/no-op/preappend notification enrollment, actual notification commit/rollback/loser delivery and post-replacement notification consumer. Unit/Integration/Host source builds PASS; none of their runtime fixtures executed.
-20/22canonical tasks closed for source/local scope. Actual Host OpenAPI snapshot + mandatory CI tasks1.4/6.3 remain pending publication authorization. No staging/commit/push/PR/merge/sync/archive/live action.

Requested solution self-review checkpoint (later on2026-10-11):
- Re-read requirements and implementation without delegation. Found P2 actual leaf admission bypass of pending/unknown confirmation and P2 source-only→Held transition retaining an obsolete notApplicable result. Both reproduced with failing Angular regressions and corrected in the existing page/coordinator boundaries.
- Full Angular435/435PASS; production Angular buildPASS with the unchanged search SCSS warning; Biome243filesPASS; git diff --checkPASS.
- Full fictional browser initially55/56: a saved-profile helper still asserted that all /events routes were forbidden. The corrected first-Hold transition exposed this race. Reused the already-approved precise fictional SSE route validator with unchanged credential/origin/method restrictions. Affected saved-profile+SSE suites17/17PASS; clean final full suite56/56PASS in2.6min on the final source.
- Latest changes have requested self-review/regression evidence; the earlier independent source verdict is not extended to them without a new independent check. Backend/Core/auth/tooling/CI and publication state are unchanged. Existing CI/OpenAPI/live gates remain pending.

Publication authorization checkpoint (2026-10-11):
- User said «чё дальше? мерж? го»: authorized routine branch/commit/push/PR and normal merge to dev after checks. No review/status bypass, sync/archive or live/deploy scope is inferred.
- Fresh origin/dev fetch remains7b7fb0f2831cb5067d4dea025e9e7d9d3f16e347; created codex/flights-m3-order-sse explicitly from that SHA; ancestry checked. Original checkout remains clean. Deploy workflow is manual-only and was not invoked.
- Before publication, reran client170/170 and harness133/133; Angular cached receipt435/435 was identified as cache replay, so a separate uncached full run is used for the publication receipt. Existing runtime fixtures remain CI-only.
- OpenAPI generated snapshot, mandatory normal CI/PR E2E and postmerge delivery receipts remain to be captured. Latest two UI self-review corrections will receive focused independent review before merge.

PR42 / initial CI repair checkpoint:
- Commit18c7a384e45930214deef2c45e6701c96b3a42c5 is the published source; PR42 targets dev. Independent reviewer accepted that exact head before merge.
- First pre-push failed because the architecture DLL had not been built in the fresh worktree. Audited Architecture project/startup/static loader/helpers: no PII assembly fixture/module initializer, Host/AppHost Main, container/schema/key startup; only IL/source/MSBuild evaluation and owned temporary fixtures. Built the existing project, then normal unchanged pre-push passed170/170 and push completed. No hook bypass.
- CI38162914202 on18c:8normal jobs passed,4failed, PR E2E skipped due prerequisites; paid evals skipped by unchanged policy. Build/lint, contract, identity, AI, architecture, Host integration and Aspire passed. Merge remains blocked by the failures.
- Corrections from exact logs: ESLint empty callbacks use explicit undefined/test spies; nullable long InlineData uses long literals; cancellation fixture uses valid hex hashes plus asserts Allowed; exact HTTP list field assertion includes projectedStreamVersion. Existing outbox test adapter forwards generic invalidations separately from status sibling probes and asserts only committed checkpoint delivery, preserving prior metadata commits during final rollback. Convergence accepts the two defined event kinds at the same checkpoint while retaining legacy/email/retry assertions.
- Additional recovery regression: an all404 cycle must not stop after retained same-owner evidence. Positive checkpoint case reproduced, and reviewer caught the legacy/null checkpoint variant. Added owner-scoped presence callback separate from version/freshness; both survive on30s fallback with actions blocked. Unknown all404 resource still stops. No new auth/domain/financial rule.
- Local correction receipt: Angular438/438 and client170/170 uncached passed; web/api-client/travel-e2e ESLint passed without rule changes; Unit/Integration/Host correction sources compiled only. Persisted/backend execution remains existing CI-only. New CI and merge receipts pending.

PR42 / second CI repair checkpoint:
- CI38165251104 on cd392d3da9b887fa18c6b1faecf9abc4300d83b1: all11 normal backend/build/lint jobs passed. Full Host HTTP397/397, generated OpenAPI comparison and contract checks passed unchanged; task1.4 has actual CI evidence. Frontend unit/lint/build passed, fictional browser55/56 failed and mandatory PR E2E was skipped. Paid evals skipped by unchanged policy.
- Exact failing search case: post-connect SSE recovery outran the one-shot404 lag assertion. Its retry reused the already Held deterministic offer/order in the shared in-memory server. Both observed failures are retained in the full frontend job log; neither is waived as infrastructure flake.
- Test-only correction retains actual demo proxy responses and legitimate booking flow: hold GET projection404 through the lag assertion, release afterward, and use a fresh form departure date per repeat/retry. No product, reset API, config or policy change. First repeat experiment exposed the repeated identity; after including repeatEachIndex, the affected scenario passed2/2 in43.7s. travel-e2e ESLint and Biome passed.
- Full CI, mandatory PR E2E, final independent repair review and normal merge remain to be completed on the corrected head.
- Independent repair reviewer found a final asynchronous assertion race: the handoff message can precede the first upstream200. Replaced synchronous check with awaited expect.poll before releasing the404 barrier; reviewer rechecked and accepted, no open P1/P2. Final affected scenario2/2PASS in42.0s after this correction. Pinned strict OpenSpecPASS;21/22tasks closed,6.3 remains pending actual whole CI/E2E acceptance.

Implementation acceptance checkpoint:
- CI38166506507 on c2fadc7e1e4666d61f5286c3c42d57af8878efa7 completed success: all13 normal PR jobs passed, including mandatory E2E; only paid AI evals skipped normally. Angular438/438, client170/170, fictional browser56/56 clean, Flights Unit1389/1389, persisted Flights Integration400/400, Host HTTP397/397 and unchanged generated OpenAPI passed. Aspire/Host startup, README smoke and actual Host browser2/2 passed.
- Closed1.4 and6.3 from actual CI,22/22canonical tasks complete. Independent source/repair review has no open P1/P2. Raw frontend/backend/E2E receipts retained in ignored local evidence; exact implementation SHA/run/jobs are in verification.md.
- Closing documentation alone changes the accepted implementation. Same normal CI on its published head, authorized normal merge to dev and postmerge CI are the remaining delivery steps. No policy bypass, sync/archive, deployment or real supplier/payment acceptance is inferred.

Final implementation delivery and sync/archive authorization:
- Final publication head938896dde25318199fa35acb8e4b4a6ecc274f81 passed CI38167545464:13/13normal jobs including mandatory E2E. PR42 merged normally into dev135fc134cfb386870851ef3dfec3c2fa0a78eae4 on2026-10-11T19:32:36Z; postmerge CI38168466905 passed12/12normal jobs on that exact SHA. Paid evals and PR-only E2E skipped by unchanged dev-push policy. Main checkout safely fast-forwarded and strict OpenSpec validation passed.
- User then said «Го» to the proposed separate sync/archive step. Fresh dev135fc134cfb386870851ef3dfec3c2fa0a78eae4 is the explicit base of clean codex/flights-m3-order-sse-archive in the existing managed worktree. No branch rewrite or current-HEAD base substitution.
- Archive status confirmed all4artifacts done and list confirmed22/22tasks. Selected delta introduces flights-order-live-updates; no existing main spec is overwritten. Current specs-rule snapshot read once; new main Purpose and all12requirements/35scenarios preserved verbatim with canonical main headers. Strict validation passed4/4main specs. Existing unrelated long-requirement informational hints were retained without rewriting those contracts.
- Updated current pointers and final receipts; dated planning/source checkpoints remain historical. Closure changes documents only. Live/deploy, real suppliers/payment and paid evals remain unperformed.
- Verified synchronized main content against the full selected delta before moving; moved9files into2026-10-11-flights-m3-order-sse with identical hashes, including .openspec.yaml. Active change list is empty; strict --all validation passed4/4main specs after archive.
