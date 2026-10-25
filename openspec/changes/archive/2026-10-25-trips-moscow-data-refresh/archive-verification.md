# Moscow data maintenance archive receipt

Date: 2026-10-25 (Europe/Moscow). Schema: `spec-driven`. All 8 tasks and all four planning artifacts are complete for the bounded maintenance/source/ordinary-CI scope.

## Authority and source

The user explicitly authorized the completion-summary and PR73 receipt update, specification sync and archive in a separate PR into dev, without merging that documentation PR, releasing v2 or enabling real users. The documentation branch `codex/trips-moscow-maintenance-closure` starts from freshly fetched dev `92d549192fb2f8229e61a63fc16c72ede0d4c948`. Its final PR head and CI belong in its own PR metadata, separately from the accepted implementation below.

## Accepted implementation

- PR73 final head `c08bd4cae7f6985f0b3222e090c58bed3a28c3f2` passed [PR CI40134522551](https://github.com/svasorcery/travel-agency/actions/runs/40134522551), attempt 1: all 20 ordinary jobs passed; paid AI evaluation was the single expected skip.
- PR73 merged into dev as `92d549192fb2f8229e61a63fc16c72ede0d4c948` after separate merge authorization. [Post-merge CI40135795012](https://github.com/svasorcery/travel-agency/actions/runs/40135795012), attempt 1, passed all 19 ordinary push jobs; paid AI evaluation and the general PR-only E2E were expected skips. Host Integration passed 121/121 without skips in both runs; the dedicated Trips full-stack job ran in both.
- Clean post-merge stack `b3a4ffcc7155743d48e24b6f94110503` passed all 13 browser stages, startup and cleanup, with complete Host/Keycloak/PostgreSQL restart, saved-plan/protected-row/key comparison, three real PKCE logins and zero external browser requests. Exact source, artifact identities and timestamps are in [verification](verification.md#final-pr-and-post-merge-acceptance).

The tool requires newer publication and different source bytes, compares two complete builds, validates through the strict reader and rejects expiry reached during preparation. Actual v1 reproduction and synthetic package-change compatibility are separate proofs; neither releases new map facts. Earlier red tests, the missing-assets architecture attempt and all successful reproduction phases remain recorded in [verification](verification.md), with the closed expiry finding in [review](review.md).

## Specification sync and retained history

The accepted capability is [trip-data-package-maintenance](../../../specs/trip-data-package-maintenance/spec.md). Its Purpose, five requirements and eleven scenarios match the delta in `specs/trip-data-package-maintenance/spec.md`; only the title and Requirements section heading differ. No other capability changes. The archive retains the original proposal, design, delta, review, metadata and all 8 checked task lines. Initial no-merge wording in planning artifacts describes their original delivery scope; subsequent merge authorization and receipts remain explicit.

The [completion review](../../../../docs/architecture/trips-completion-review.md), [maintenance guide](../../../../tools/trips/data-maintenance.md), root/module summaries and module README point to the canonical specification and this archive. Sync was completed and checked before the change directory moved. The current user request supplies sync/archive/publication authority; the launcher context's older Flights pilot is not this change's scope and supplies no additional runtime authority.

## Documentary validation

Pinned OpenSpec strict validation passed all 19 main specifications; the active change list is empty after this archive. AI harness checks passed all 134 tests and canonical instruction validation. The documentation-only staged inventory has 52 valid relative file links. Exact comparison preserved the six original proposal/design/review/task/delta/metadata files, all 8 checked tasks and the five-requirement/eleven-scenario semantic sync. Both retained final-PR/post-merge browser receipts were checked for their exact checkout, 13 stages, restart/storage, login/network and cleanup results. `git diff --check` passed. The documentation PR's final head/run and successful CI must be recorded in its metadata before delivery, without relabelling PR73 implementation receipts.

Independent documentary review found no Critical or Important issues. Its one Minor finding was a premature statement about this documentation PR's final CI; the sentence now states the delivery requirement explicitly. Scoped recheck closed the finding with no open Critical, Important or Minor issues. Runtime implementation, new-source/v2 suitability, human/target acceptance and the new PR's eventual CI were outside that documentary review.

## Boundaries that remain open

V2 stays deferred until a genuinely new snapshot and separately accepted data release. V1 bytes, manifest pin, public URLs and `2026-11-17T03:55:13Z` validity are unchanged. No source acquisition, preparation, retained-demo mutation, schema/key provisioning, deployment or feature activation is part of this documentary closure. Human route quality/calibration and [M2 target/rollout task 6.3](../2026-10-22-trips-m2-moscow-itinerary/tasks.md) remain separate; fixed-time integration evidence does not renew data or enable real users.
