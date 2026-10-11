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
