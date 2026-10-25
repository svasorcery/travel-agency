# Trips demo explanations archive receipt

Date: 2026-10-25 (Europe/Moscow). Schema: `spec-driven`. All four tasks and all four planning artifacts are complete for the bounded explanation/source/CI scope.

## Authority and accepted implementation

The user approved documentary synchronization and archival after PR75 merge and local-demo verification. This separate documentation branch, `codex/trips-demo-explanations-docs`, starts from freshly fetched dev `dfb8e18b9510e6095b99eda42df71834b39e118a`. Its publication and CI are separate from implementation receipts; it must remain unmerged until separately authorized.

[PR75](https://github.com/svasorcery/travel-agency/pull/75) head `7a9c1473950d4577818abb22c4313c8961a67b63` passed [PR CI40205101433](https://github.com/svasorcery/travel-agency/actions/runs/40205101433), attempt 1, with 20 applicable successes and one expected paid-AI skip. Its authorized merge `dfb8e18b9510e6095b99eda42df71834b39e118a` passed [push CI40210020699](https://github.com/svasorcery/travel-agency/actions/runs/40210020699), attempt 1, with 19 successes and expected paid-AI/PR-only-E2E skips. Trips full-stack passed in both. Exact checkout identities and local checks are retained in [verification](verification.md#final-pr-and-post-merge-acceptance).

The existing isolated real Host/Keycloak/PostgreSQL demo was rebuilt from merged dev and restarted. Thirteen saved foundation/key snapshots and 98 frozen audit/material/form files stayed unchanged. Real PKCE login, reopening and three-day explanation checks passed without saving a candidate. The two standalone browser-probe failures and test-only corrections are retained in [local-demo evidence](verification.md#isolated-local-demo-after-merge); this is not a fresh human-quality run.

## Specification sync and retained history

The accepted [trip-itinerary-candidates contract](../../../specs/trip-itinerary-candidates/spec.md) receives exactly two added requirements and five scenarios from this change: daily selected-interest explanation and precise existing walking-scenario explanation. The original Purpose and every prior requirement/scenario remain unchanged. Sync is checked before moving the change to this dated archive.

Original proposal, design, delta, metadata and all four checked task lines are retained byte-for-byte. Prepublication boundaries in those artifacts remain historical; the subsequent publication, explicit merge and local-demo acceptance are documented separately. The [completion review](../../../../docs/architecture/trips-completion-review.md), root/module summaries and module README point here and to the canonical contract. The launcher context's older Flights pilot is unrelated to this change and grants no additional runtime authority.

The documentation PR's own exact head, CI run/attempt and result belong in its PR metadata before delivery. They must not be substituted for the accepted PR75 implementation receipts.

## Documentary validation

Pinned strict OpenSpec validation passed all 19 main specifications; the active change list is empty. The unchanged Flights cancellation specification retains five informational long-requirement hints, with no validation failures. AI harness checks passed 134 tests and canonical instruction validation. A separate documentary check confirmed five original artifacts byte-for-byte, four completed tasks, an exact additive sync of two requirements/five scenarios and 49 valid relative file links. All 98 protected local evidence/form files and the three implementation hashes remained unchanged; the main demo checkout stayed clean. `git diff --check` passed. The initial attempt to run the documentary check was blocked by Windows sandbox child-process permissions; the same read-only check passed under the normal approved execution path.
Independent read-only documentary review found no Critical, Important or Minor issues. It independently checked the preserved artifacts/tasks, exact additive sync, relative links/anchors, nine local receipt hashes, before/after plan/file/mount identities and unchanged implementation/v1 files. It used the coordinator's fresh GitHub and strict/harness results; it did not rerun runtime/browser checks, reassess human quality or claim this documentation PR's not-yet-executed CI.
## Remaining boundaries

Human A/B review is deferred and scores remain blank. Agent fact checks, modelled walking and integration evidence do not establish human route suitability, calibrated travel times or current physical access. V1 bytes, expiry and runtime pin remain unchanged; no v2 is released. [M2 target/rollout task 6.3](../2026-10-22-trips-m2-moscow-itinerary/tasks.md) remains pending. This documentary closure does not rebuild the retained demo, provision schema/keys, call providers/models or enable real users.
