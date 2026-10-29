# Trips M3 publication and pending gates

Updated: 2026-10-29. Product source (PR81), documentary finalization (PR82), evaluator preparation (PR83) and documentary/deferral receipt PR84 have accepted ordinary PR/post-merge CI. PR84 is merged as75ec04a; paid evaluation stays deferred with external API budget USD0. Real-model quality and operational rollout remain pending. The PR81 receipts below retain their original scope; later publication evidence is recorded separately. No model execution, feature enablement or retained demo changes.

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
| Source and ordinary PR/post-merge CI | Product PR81, documentation PR82/PR84 and evaluator PR83 accepted; exact runs remain phase-specific. PR84 CI20/post-merge19 passed, without model-quality or rollout acceptance |
| Real-model qualification (48 cases), task 6.3 | Deferred/not run by user decision2026-10-28: paid model evaluation declined; external API budget USD0. No execution authority or quality waiver; any future reconsideration needs new explicit scope |
| Independent sealed 24-case holdout | Pending / not authored or evaluated; independently prepared after source/prompt/model freeze, separate permission, no tuning access |
| Real-model quality, observed cost/latency | Not evaluated; fake mechanics are not evidence of model quality |
| Private-text processing | Pending; minimal accepted processor transfer/retention and separate processing permission required |
| Target provision/enablement and rollout | Pending; operational topology/spend controls, authenticated target proof, rollback and explicit enablement authority required |
| Manual M1, catalogue1, historical plans and itinerary v1/v2 | Preserved; separate Apply/Save/Generate and owner/revision/CAS remain authoritative |
| Human A/B, field calibration, original M2 rollout 6.3, Moscow v2 | Independent pending/deferred gates, unchanged |

M3 flags remain off; Production excludes this first slice. No provider/model/pricing/processing selection, credential/billing access, real call, target/schema/key work or activation is authorized by this receipt or by spec sync.

## Documentary verification

This section retains the PR82 preparation/publication phase. Its historical authority statements and local counts are not a receipt for PR84 or for the current uncommitted reconciliation; PR84 has its own exact evidence below.

On this documentation branch, pinned strict validation passed for the active change and all 20 current specs (zero failures; existing long-requirement informational hints are not claimed absent). The synced 13 requirements and 52 scenarios match the merged delta byte-for-byte after line-ending normalization. All 22 checked task lines and the one unchecked 6.3 line are unchanged.

Repository Biome passed on 524 files without changing retained receipt bytes. Dependency-free AI harness passed 134/134 and instruction validation. Author review resolved local Markdown file links, recomputed all 14 receipt hashes, compared retained bytes with both original downloads and verified that every changed path is documentary Markdown or its sanitized JSON evidence. Runtime/source/configuration, historical archives, packages, demo/human materials and the fictional qualification corpus have no changes.

During local documentation preparation, no product implementation tests, model evaluator, model/provider API, credentials/billing, schema/key provisioning or deployment was run. The accepted PR81 CI receipts above concern the merged implementation. Documentation commit/push/PR and waiting for its CI were separately authorized after that preparation; documentation PR merge, model execution and M3 enablement remain unauthorized. The documentation PR records its own CI receipt.
## Documentation self-review before publication

The author corrected two documentation hazards: an old four-action grouping bundled Apply and Save despite their distinct persistence effects; and current-tense unpublished/no-test statements would become misleading after documentation publication. The README now distinguishes creating a new foundation from applying and separately saving an existing plan, and the evidence ledger binds preparation statements to their historical phase. No requirement, scenario, task checkbox, receipt byte, runtime/configuration or acceptance threshold was changed. Local file links, all14retained hashes, exact main/delta requirements and pending task6.3 were rechecked before commit.

## PR82 documentary closure and PR83 evaluator preparation

[PR82](https://github.com/svasorcery/travel-agency/pull/82) merged on 2026-10-27T18:00:58Z as `9ff60e42bb553214a22ed71fb4fa44b9d846c3d2`, reviewed head `cdbf9d490e1b0e5d5898b910e22c5e005d42809b`, base `30a3efd31ec1b164e455abea493fa34056f03514`. [PR CI40522032756](https://github.com/svasorcery/travel-agency/actions/runs/40522032756) and [push CI40555980053](https://github.com/svasorcery/travel-agency/actions/runs/40555980053) both completed successfully, attempt1. These are documentation receipts; they do not replace PR81 implementation evidence.

[PR83](https://github.com/svasorcery/travel-agency/pull/83) prepared the bounded fictional evaluator, explicit Anthropic wire projection, durable evidence and shared worker/PG resource envelope, plus the separately authorized deepmerge backport and narrow vendor lint-inference correction. Fresh base `9ff60e42bb553214a22ed71fb4fa44b9d846c3d2`; reviewed head `57d09f7b164e5ac1c321dc8ec7ad5b723d3dd1d7`; actual dev merge `d318cb49b175fe0fe5a19ed6fc1161b4e65bf20f` at 2026-10-28T12:02:47Z. Its two parents are exactly that base and head; the merge tree equals the reviewed head tree. No completed branch was rewritten or reused.

| Phase | Exact bound head | Run / attempt1 | Result |
| --- | --- | --- | --- |
| Final PR83 | `57d09f7b164e5ac1c321dc8ec7ad5b723d3dd1d7` | [40677897457](https://github.com/svasorcery/travel-agency/actions/runs/40677897457) | completed/success;20 applicable jobs; paid test-ai-evals skipped |
| Post-merge dev push | `d318cb49b175fe0fe5a19ed6fc1161b4e65bf20f` | [40684645709](https://github.com/svasorcery/travel-agency/actions/runs/40684645709) | completed/success;19 applicable jobs; paid test-ai-evals and PR-only test-e2e skipped |

The earlier [failed PR83 run40675378507](https://github.com/svasorcery/travel-agency/actions/runs/40675378507), head `3b03631d8239953e6ca7a463fc844399ec71e265`, remains failed. Nx inferred an unwanted lint target for pinned upstream deepmerge; a source correction produced the distinct final green run above. No failure was waived or relabelled as a rerun success. Both final phases passed test-trips-full-stack; no new per-city artifact-stage claims are made from job status alone. Sanitized job-level observations: [PR83 CI receipt](verification/run-preparation/pr83-ci-receipt.json).

PR83 accepts source and ordinary CI only. Main tasks stay22/23, real-model6.3 unchecked/not run; qualification48, new sealed holdout24, provider/model/rates/spend, private text, target and rollout remain unapproved. The [run review packet](model-evaluation-options.md#11-пакет-первого-запуска-подготовка-без-исполнения) records the subsequent source-only preflight and its remaining blockers. These PR83 receipts are not CI acceptance for later documentary changes. PR84 has its separate publication/CI receipt below; neither that receipt nor the current uncommitted reconciliation refreshes the pinned d318cb49 worker evidence.

## User decision: defer paid evaluation

On2026-10-28 the user declined the separately paid model API path and chose to skip it. External model API budget is USD0; the 48-case qualification and later holdout are not run, task6.3 remains unchecked at22/23. Historical source/CI acceptance and source-only worker/image preparation stay accepted. Drafts remain denied, M3 remains default-off and private-text/target/rollout remain unaccepted. This is deferral, not a relaxed acceptance gate. See the [recorded decision](model-evaluation-options.md#13-платная-оценка-отложена-external-api-budget-usd0).

## PR84 documentary acceptance and post-merge CI

[PR84](https://github.com/svasorcery/travel-agency/pull/84) published the M3 source-only preparation receipts, clarified historical/current authority and recorded the user's paid-evaluation deferral (external model API budget USD0). Its22changed files were12Markdown documents and10sanitized metadata/draft JSON receipts. No implementation, configuration, dependencies, contracts, frozen corpus/prompt/oracles, city packages, saved demo or human-review material changed.

- Fresh publication base: `d318cb49b175fe0fe5a19ed6fc1161b4e65bf20f`.
- Reviewed head: `dec35bb5e9225110269c8e7f48babd51982fbad5`, branch `codex/trips-m3-run-packet`.
- PR CI synthetic checkout: `87cee98b873aa86b1ba45069e8f1bb63e1b371f5`; this is distinct from the actual merge.
- Separately authorized actual dev merge: `75ec04a01272d06f995f5cb925b548934b8f34ab`, merged2026-10-28T19:31:12Z. Parents are exactly the publication base and reviewed head; the merge tree equals the reviewed head tree.

| Phase | Run / attempt1 | Exact bound head | Terminal result | Expected skips |
| --- | --- | --- | --- | --- |
| PR84 | [40746861915](https://github.com/svasorcery/travel-agency/actions/runs/40746861915) | `dec35bb5e9225110269c8e7f48babd51982fbad5` | completed/success;20 applicable jobs | paid test-ai-evals |
| Post-merge dev push | [40755030053](https://github.com/svasorcery/travel-agency/actions/runs/40755030053) | `75ec04a01272d06f995f5cb925b548934b8f34ab` | completed/success;19 applicable jobs | paid test-ai-evals; PR-only test-e2e |

Both phases passed test-trips-full-stack; the PR additionally passed its general E2E job. Job status records ordinary compatibility CI, not new per-city/human/model-quality scores. In both build jobs the standalone source-only preflight reported Clean=true, ReadyForExecution=false, ProviderCalls=0: on the synthetic checkout above and on actual merge75ec04a respectively. No model-evaluation dispatch or job rerun was used. Sanitized metadata, all job conclusions and the controlled preflight fields are retained in [PR84 CI receipt](verification/run-preparation/pr84-ci-receipt.json).

PR84 accepts this documentary state and the recorded USD0 decision. Main tasks remain22/23 with6.3unchecked/deferred/not run; paid qualification48, new sealed holdout24, real-model quality/cost/latency, private-text processing, target and rollout are not accepted. M3 remains default-off. The worker/image and denied manifests still bind their historical source d318cb49; PR84/post-merge CI do not refresh those runtime identities or authorize execution.

The initial reconciliation started from freshly fetched origin/dev75ec04a in a new clean worktree/branch codex/trips-m3-pr84-receipt under documentary-only authority, without staging/commit/push or local evaluator/Docker/model/target/demo work. On2026-10-29 the user separately authorized self-review/fixes, commit, push, a PR into dev and waiting for ordinary CI, while keeping merge/model execution/M3 enablement prohibited and external API budget USD0. This later PR reports its own CI in its description; no follow-up commit is needed merely to record that CI. No main/delta specification or task checkbox is changed, and the active change is not archived.
