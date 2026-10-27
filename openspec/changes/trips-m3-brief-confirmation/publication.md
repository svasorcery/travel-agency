# Trips M3 publication and pending gates

Updated: 2026-10-27. Source and ordinary PR/post-merge CI accepted; real-model quality and operational rollout remain pending. This is documentary reconciliation after PR81, without model execution, feature enablement or retained demo changes.

## Exact publication identity

- Original fresh implementation base: `35953fd6c8feabf16acd7fb944201711e3d8c5fa`.
- [PR81](https://github.com/svasorcery/travel-agency/pull/81), branch `codex/trips-m3-brief-confirmation`, reviewed head `9b155a0e4c62371516aaa90fb141cd91f1185ab3`.
- Four commits: `6548346` bounded backend/tests; `3324af6` guarded UI/client; `13b750c` fictional qualification/evaluator; `9b155a0` OpenSpec/local evidence.
- PR validation merge ref: `b2157058618ac7a1d615193b6869f382b1e4b836`. This is a synthetic CI checkout, not the actual merge.
- Separately authorized dev merge: `30a3efd31ec1b164e455abea493fa34056f03514`, merged 2026-10-27T13:47:31Z, parents original base and reviewed head. Merge and head tree both `19c7f1adf43f50b9a5fd5519bb445eec403c6e22`.
- Documentation branch `codex/trips-m3-documentation` starts clean at freshly fetched origin/dev=`30a3efd31ec1b164e455abea493fa34056f03514`, without rewriting/reusing the completed implementation branch. Its local preparation predates documentation publication; implementation CI receipts below do not serve as CI for later documentation commits.

## Ordinary CI acceptance

| Phase | Run | Bound head / actual checkout | Result, attempt 1 | Expected skips |
| --- | --- | --- | --- | --- |
| PR | [40503112170](https://github.com/svasorcery/travel-agency/actions/runs/40503112170) | `9b155a0e4c62371516aaa90fb141cd91f1185ab3` / `b2157058618ac7a1d615193b6869f382b1e4b836` | completed/success; 20 applicable jobs | paid test-ai-evals |
| Post-merge push | [40515192305](https://github.com/svasorcery/travel-agency/actions/runs/40515192305) | `30a3efd31ec1b164e455abea493fa34056f03514` / same SHA | completed/success; 19 applicable jobs | paid test-ai-evals; PR-only test-e2e |

The general PR E2E passed in the PR run. Both phases ran the dedicated actual two-city full-stack lane. No failed-job rerun, paid workflow dispatch or model request was performed. Post-merge full-stack steps finished before GitHub finalized the workflow aggregate; acceptance waited for completed/success, not merely passed steps. [Earlier local verification](verification.md) remains historical pre-publication evidence and is not relabelled as exact-commit CI.

## Sanitized two-city evidence

| Phase | Moscow run | Saint Petersburg run | Actual source |
| --- | --- | --- | --- |
| PR | c04a6050541e3e1f428bb09a6b3950bf | 1f6c822abcea8631da243b0c9b23e708 | clean synthetic PR merge |
| Post-merge | a3920bdaa8bfd6cb20de1a171ee045d8 | d43d0f3644704807187a2cda523b6317 | clean actual dev merge |

Each phase passed discovery/start/browser/cleanup, Moscow 13/SPb 14 browser stages, exactly 1 original itinerary candidate POST, 3 PKCE logins and 0 external browser requests per city. Full Host/Keycloak/PostgreSQL restart preserved storage/protected plans/keys; SPb also preserved historical Moscow content. These are itinerary compatibility receipts, separate from actual M3 Host/AI/CoreNATS fake-client and browser tests. No actual extraction-model quality, real processor latency/cost or target readiness follows from them.

Actions artifact `trips-full-stack-evidence`: PR ID 12734740403, ZIP digest `0def778785c2f169721b8a18c55e80fa6df049f6f3e096fa65ff7269abd61a21`; post-merge ID 12740201702, ZIP digest `8bd1ba10d17cbc832e996ed5154cff90a3a8ca2873cd47817d51ea2abcaaf1de`. Each original download included six per-city JSON receipts plus one aggregate. Fourteen JSON files are copied byte-for-byte below; screenshots, credentials, cookies, raw keys and temporary download links are not retained.

| Receipt | SHA256 of retained original bytes |
| --- | --- |
| [verification/pr-ci/1f6c822abcea8631da243b0c9b23e708-browser-receipt.json](verification/pr-ci/1f6c822abcea8631da243b0c9b23e708-browser-receipt.json) | `a6f6c8ee6d3e82ef429952660128a680b9e8c342d78e0acd31a8f970d6287c04` |
| [verification/pr-ci/1f6c822abcea8631da243b0c9b23e708-receipt.json](verification/pr-ci/1f6c822abcea8631da243b0c9b23e708-receipt.json) | `0b01c8694c5ac04e02842b5857c72abda782bebc82d7326f92312f74e4c5d08d` |
| [verification/pr-ci/1f6c822abcea8631da243b0c9b23e708-restart.json](verification/pr-ci/1f6c822abcea8631da243b0c9b23e708-restart.json) | `b228fed8de830315f7595fd4dbcc810a81787973f9b7434a6b9e706d108f15cd` |
| [verification/pr-ci/aa2176274662799cb2d12cdfdba83bce-both-city-aggregate.json](verification/pr-ci/aa2176274662799cb2d12cdfdba83bce-both-city-aggregate.json) | `92f7146aa812e7792e8d54c69a67c4dca71b7af2cdedbc67430f654d97330764` |
| [verification/pr-ci/c04a6050541e3e1f428bb09a6b3950bf-browser-receipt.json](verification/pr-ci/c04a6050541e3e1f428bb09a6b3950bf-browser-receipt.json) | `bfc5c104dfc6f8286af0d2ccb3325e8a6dd6cd740b2871e1b3a8d02caf5cd9eb` |
| [verification/pr-ci/c04a6050541e3e1f428bb09a6b3950bf-receipt.json](verification/pr-ci/c04a6050541e3e1f428bb09a6b3950bf-receipt.json) | `d113fe5018dcae2a867aa189b8206a65e0f70aaec32eac2e1d17a954b78f29a7` |
| [verification/pr-ci/c04a6050541e3e1f428bb09a6b3950bf-restart.json](verification/pr-ci/c04a6050541e3e1f428bb09a6b3950bf-restart.json) | `a7f89e26940e92770e02f45e31a1021ce572cb930a30fd315fca51a0a18fa97a` |
| [verification/post-merge/8b3dd497cfecc92eaebdf62fdbc86315-both-city-aggregate.json](verification/post-merge/8b3dd497cfecc92eaebdf62fdbc86315-both-city-aggregate.json) | `b906ee47a05fe534e5d37df1b0101b8c848fce03f83ba8c8f1865d98a750f1ba` |
| [verification/post-merge/a3920bdaa8bfd6cb20de1a171ee045d8-browser-receipt.json](verification/post-merge/a3920bdaa8bfd6cb20de1a171ee045d8-browser-receipt.json) | `d67281e6c26f440118ca104adad9516728c8f01c7e36ce5988a3ea857efd640d` |
| [verification/post-merge/a3920bdaa8bfd6cb20de1a171ee045d8-receipt.json](verification/post-merge/a3920bdaa8bfd6cb20de1a171ee045d8-receipt.json) | `2b5b93d30bbb19ef0be717f57e3efd2c44cca4b8c02cfb09444ca7824d8c8da9` |
| [verification/post-merge/a3920bdaa8bfd6cb20de1a171ee045d8-restart.json](verification/post-merge/a3920bdaa8bfd6cb20de1a171ee045d8-restart.json) | `1a2a0588db06472847ec5de56155f211ca601bfb3e9a8c89b700919654de7736` |
| [verification/post-merge/d43d0f3644704807187a2cda523b6317-browser-receipt.json](verification/post-merge/d43d0f3644704807187a2cda523b6317-browser-receipt.json) | `24d2d87373dfa2785848fa8debd2b3d9434b40dd0e4b25f18900e4bc9a411f09` |
| [verification/post-merge/d43d0f3644704807187a2cda523b6317-receipt.json](verification/post-merge/d43d0f3644704807187a2cda523b6317-receipt.json) | `8cb471da0f6f043a13b2811bc8c53a70e694d841242590eb73e3ef892090e3fc` |
| [verification/post-merge/d43d0f3644704807187a2cda523b6317-restart.json](verification/post-merge/d43d0f3644704807187a2cda523b6317-restart.json) | `dcb1bc7cb1041e307c866050067bacd08d834ba0c6c51dab69c1d80948b96c0f` |

## Synced contract and task status

The [trip-brief-extraction current spec](../../specs/trip-brief-extraction/spec.md) receives the ADDED requirements from the merged delta without changing requirement/scenario text or its default-off/explicit-confirmation boundaries. M1/M2/M2.2 contracts, runtime/configuration, immutable packages and saved demo/human materials are untouched.

[Tasks](tasks.md) remain 22/23. Task 6.3 stays unchecked; checked 7.3 records pending gates, not completed rollout. The change is intentionally active, with no archive or blanket completion claim.

| Gate | Status and required evidence |
| --- | --- |
| Source and ordinary PR/post-merge CI | Accepted through PR81 and the exact runs above |
| Real-model qualification (48 cases), task 6.3 | Pending / not run; exact processor/model/terms, verified prices, count/cost/resource/time authority and frozen binding required |
| Independent sealed 24-case holdout | Pending / not authored or evaluated; independently prepared after source/prompt/model freeze, separate permission, no tuning access |
| Real-model quality, observed cost/latency | Not evaluated; fake mechanics are not evidence of model quality |
| Private-text processing | Pending; minimal accepted processor transfer/retention and separate processing permission required |
| Target provision/enablement and rollout | Pending; operational topology/spend controls, authenticated target proof, rollback and explicit enablement authority required |
| Manual M1, catalogue1, historical plans and itinerary v1/v2 | Preserved; separate Apply/Save/Generate and owner/revision/CAS remain authoritative |
| Human A/B, field calibration, original M2 rollout 6.3, Moscow v2 | Independent pending/deferred gates, unchanged |

M3 flags remain off; Production excludes this first slice. No provider/model/pricing/processing selection, credential/billing access, real call, target/schema/key work or activation is authorized by this receipt or by spec sync.

## Documentary verification

On this documentation branch, pinned strict validation passed for the active change and all 20 current specs (zero failures; existing long-requirement informational hints are not claimed absent). The synced 13 requirements and 52 scenarios match the merged delta byte-for-byte after line-ending normalization. All 22 checked task lines and the one unchecked 6.3 line are unchanged.

Repository Biome passed on 524 files without changing retained receipt bytes. Dependency-free AI harness passed 134/134 and instruction validation. Author review resolved local Markdown file links, recomputed all 14 receipt hashes, compared retained bytes with both original downloads and verified that every changed path is documentary Markdown or its sanitized JSON evidence. Runtime/source/configuration, historical archives, packages, demo/human materials and the fictional qualification corpus have no changes.

During local documentation preparation, no product implementation tests, model evaluator, model/provider API, credentials/billing, schema/key provisioning or deployment was run. The accepted PR81 CI receipts above concern the merged implementation. Documentation commit/push/PR and waiting for its CI were separately authorized after that preparation; documentation PR merge, model execution and M3 enablement remain unauthorized. The documentation PR records its own CI receipt.
## Documentation self-review before publication

The author corrected two documentation hazards: an old four-action grouping bundled Apply and Save despite their distinct persistence effects; and current-tense unpublished/no-test statements would become misleading after documentation publication. The README now distinguishes creating a new foundation from applying and separately saving an existing plan, and the evidence ledger binds preparation statements to their historical phase. No requirement, scenario, task checkbox, receipt byte, runtime/configuration or acceptance threshold was changed. Local file links, all14retained hashes, exact main/delta requirements and pending task6.3 were rechecked before commit.
