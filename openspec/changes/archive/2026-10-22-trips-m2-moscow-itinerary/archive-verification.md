# Trips M2 — specification sync and archive acceptance

Date: 2026-10-22. The user authorized synchronizing the Trips M2 specifications, archiving this change and checking document consistency, explicitly without feature enablement. This records source/demo and ordinary CI acceptance. Task 6.3 remains incomplete; archiving does not perform or waive target acceptance, human evaluation or rollout.

## Accepted implementation and CI

| Boundary | Identity and result |
|---|---|
| Original requested fresh-dev base | `e654c76915103a82083ef2abc01c0579117c9749`; retained in the merged ancestry |
| Final feature head | `3de876126884981a4278b1f569ec14c45143f436` |
| [PR #65](https://github.com/svasorcery/travel-agency/pull/65) | Merged into dev on 2026-10-22 at 10:25:00 UTC as `62713d11aece29c86a2fc5c96b17f2010721d73d` |
| Merge parents | Fresh dev `93c5df0288400ba5b8f8fd50b89f1b16a19f1951` and final feature head `3de876126884981a4278b1f569ec14c45143f436` |
| [Final PR CI](https://github.com/svasorcery/travel-agency/actions/runs/39760749900) | Attempt 1, pull_request, exact feature head above; 19 ordinary jobs passed; paid AI evaluation skipped by its existing condition |
| [Post-merge CI](https://github.com/svasorcery/travel-agency/actions/runs/39762931740) | Attempt 1, push, exact merge above; 18 applicable jobs passed; paid AI evaluation and PR-only E2E were the two expected skips |

The merge tree `88f20def7576bb2fbc386f8f679ec16b8fa93324` equals PR test checkout `237275798887bc5897f203ea51bc5a87cb1d70cf`. The published PR receipt records verification of all 19 PR and 18 post-merge checkouts. Each successful run executed 4,095 .NET tests with zero test skips, 92 shared demo browser cases and 80 Trips browser cases. The PR run also passed two isolated AppHost E2E cases. The finalization independently refreshed the PR, both run identities and every job conclusion; it did not rerun those workflows.

The final feature retained standalone Rail M2 and PR #66's test-fixture synchronization, with the sole overlapping Rail test resolved to incoming dev bytes. Its local checks include 882 web, 392 client and 74 Rail unit tests. Those phase-bound local results and the final CI results do not replace the earlier frozen quality run.

Earlier failed CI runs, intermediate reviews and corrected test/UI attempts remain historical failures. Original review and execution evidence is indexed in [verification](verification.md), the [implementation ledger](implementation.md) and the [PR receipt](https://github.com/svasorcery/travel-agency/pull/65). The archived reports retain their original phase boundaries; their old pending statements are not the current acceptance status.

## Canonical contracts and archive boundary

- [trip-itinerary-candidates](../../../specs/trip-itinerary-candidates/spec.md): new main specification, 21 requirements and 62 scenarios. The delta Purpose and all added requirement/scenario bodies are preserved.
- [trip-rail-intentions](../../../specs/trip-rail-intentions/spec.md): new main specification, 12 requirements and 35 scenarios, with the same preservation rule.
- The complete change is retained in `openspec/changes/archive/2026-10-22-trips-m2-moscow-itinerary/`, including `.openspec.yaml`, planning, delta specs, reviews and frozen quality evidence. Existing main specifications, including the still-active M1 change, are not rewritten.
- [Tasks](tasks.md) record **26/27 complete**. Task 6.2 is accepted from the exact PR/post-merge receipts. Task 6.3 remains unchecked and explicitly deferred by the user's no-enablement scope; no completed marker or waiver is substituted for it.

The checked-in archive input manifest records each original file's SHA256 at merge `62713d11aece29c86a2fc5c96b17f2010721d73d` and its final archive hash. Changed documentation is identified as acceptance bookkeeping or relative-link rebasing. Original committed report bytes remain available at that exact merge; link maintenance does not reinterpret an old report's hash as a new execution receipt.

The regression test's corpus-directory reference and the existing Git/Biome frozen-corpus path protections move to the archive. Assertions, case set, clock, thresholds and production dependencies remain unchanged. Any fresh execution is an ordinary deterministic regression, not a new first-run evaluation, tuning or replacement for the original bound Q_demo.

## Frozen evidence and local retention

| Input | Unchanged SHA256 |
|---|---|
| `quality-cases.json` | `f50b7511fc12642b20df7ca207c780740382eeb4a482258929551ce29aa5756c` |
| `quality-protocol.md` | `bcb608c08f4761c973498186942194ab7d8913ea27f5ccac00ed757bd4489a1f` |
| `quality-demo-instantiations-v1.json` | `4abb961b24aa5fa6da95db3bafebd49ab4e64bad21a6715cdbf052d913b70437` |

The original first bound run remains one Fact with 24 records: 12 Q, six structural edge and six negative cases; 12/12 filled Q targets against the unchanged 11/12 threshold, and 13 raw-response Node checks. Human scores and reviewers remain absent, with humanQuality=not-evaluated. No calibrated travel-time or human suitability claim follows.

After the separately authorized cleanup, original ignored OSM inputs, raw logs, original Q binaries and reviews are retained locally in `TestResults/retained-evidence/trips-m2-20261022.zip` (SHA256 `13e9550b7df6dccab7b1f9966bec65011b8f98b2f0d41283491bca8b9d4be869`) with its retention manifest and cleanup receipt. Older absolute worktree/TestResults paths in reports are historical paths, not claims that the deleted worktrees still exist. These local archives are not checked-in or public distribution artifacts.

## Remaining gates

Trips and itinerary generation remain independently default-off; Production omits itinerary service registrations. Real Rail data remains transient/view-only. No schema, keys, source/provider configuration, runtime feature flags, deployment or real-user access changes as part of this finalization.

Task 6.3 still requires separately authorized real Host/Keycloak/private UX/ingress acceptance against provisioned protection and schema, package/freshness/withdrawal/rollback checks and an explicit enablement decision. Human field review and per-case Q_live, calibration, real access/hours, and real Rail rights/banner/quota remain separate. The [geography direction](design.md#8-расширение-географии-после-этого-m2--направление-не-текущие-задачи) remains future work with its own city package and evidence.

## Finalization verification

- Strict OpenSpec validation passed all 17 current items: 16 main specifications and the untouched active M1 change. M2 is absent from the active change list. Both new main specs exactly match their archived deltas after only the canonical title and Requirements-header conversion.
- All 81 original files survive the move: 71 are byte-identical, six contain explicit current-status bookkeeping, and four contain only link maintenance (three relative-path rebases and one old section-anchor repair). `.openspec.yaml`, both delta specs, frozen inputs and original execution/final source-audit/fix/recheck reports remain byte-identical. The per-file comparison is retained in [the archive input manifest](archive-input-manifest.json).
- The document check resolves local file links and referenced Markdown section anchors across 89 files with zero missing targets. Root/module instructions, README, current architecture, operations and shared-client links point to the accepted contracts/archive. Historical status entries remain explicitly phase-bound.
- Existing AI-harness checks passed 134/134, README checks 13/13, CSharpier passed for the sole changed C# file, and focused Biome CI passed five JSON/configuration files. Whitespace checks passed. No runtime implementation/configuration, dependency, workflow, migration or unrelated module file changed. Biome's existing frozen-input exclusion only changes its archive path; its pre-existing configuration deprecation information is retained.
- After cache-only offline restore, the existing corpus-path regression passed 1/1 Fact, zero skips: all 24 records completed, 12/12 filled Q targets, zero failed/unrun cases. Its fixed clock, inputs, assertions and 11/12 threshold are unchanged. This is post-archive regression evidence, not a replacement first-run Q_demo or human evaluation. The existing standalone-project Verify solution-discovery warning remains a build diagnostic; no gate was suppressed.

Finalization logs, document/preservation checks and TRX are local under `TestResults/trips-m2-openspec-finalization-20261022/`. The initial local sync/archive phase made no commit, push, PR, deployment or enablement.

## Separately authorized publication

The user subsequently authorized a fresh-dev comparison, commit, push and a PR into dev, with ordinary CI and necessary failure fixes, explicitly without merge or enablement. Freshly fetched dev remains `62713d11aece29c86a2fc5c96b17f2010721d73d`; the finalization branch starts at that exact merge. Publication preserves the archive's 26/27 task status and the incomplete task 6.3. The new documentation PR records its own exact head/run/attempt/results separately from the accepted implementation PR #65 receipts above. This authorization does not dispatch paid AI evaluation, deployment or target work.
