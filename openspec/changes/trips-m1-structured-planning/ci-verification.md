# Trips M1 PR CI acceptance

Date: 2026-10-20. User authorized commit, push, PR into dev and CI fixes; PR merge/auto-merge remains excluded.

## Exact source identity

- PR: https://github.com/svasorcery/travel-agency/pull/58 (open into dev).
- Original freshly fetched dev ancestor: `799e31a6b31ff256ba017861cd911e547e4e9896`.
- Initial source commit: `3aebd3adeb142478a509de5e45f083947b2f6c5b`.
- Current dev/Rail ancestor: `22a06448edf1edc6cd139894aae3fcdc5e8edf49`.
- Reviewed integration/source head: `b39b6ce35cc51189bded16547150485a4eec8195`; parents are the initial source and current dev commits. Both ancestry checks pass; no history rewrite.
- CI: https://github.com/svasorcery/travel-agency/actions/runs/39455463089, attempt1, pull_request event, completed/success.
- Actual PR synthetic merge checkout in CI logs: `73986550bf715659311dbd523bb136575da5edf8`, against the dev ancestor above. This synthetic checkout is distinct from merging the PR into dev.

## Complete job denominator

All19ordinary jobs succeeded: lint, build-dotnet, frontend-affected, test-flights-unit, test-support-unit, test-support-integration, test-hotels-unit, test-hotels-integration, test-trips-unit, test-trips-integration, test-identity-unit, test-ai, test-host-http, test-architecture, test-contract, test-flights-integration, test-host-integration, test-aspire-smoke and test-e2e. Only test-ai-evals was intentionally skipped by its existing paid workflow_dispatch condition. No failure, cancellation, retry, bypass or paid/model/provider request was introduced.

| Executed suite | Passed/total | Skipped |
|---|---:|---:|
| Trips unit |53/53|0|
| Trips integration (37lean plus6owned PostgreSQL) |43/43|0|
| Flights unit / integration |1500/1500;413/413|0|
| Support unit / integration |98/98;87/87|0|
| Hotels unit / integration |79/79;125/125|0|
| Identity unit / AI deterministic tests |25/25;174/174|0|
| Host HTTP / Rail unit / Rail adapter HTTP |486/486;35/35;75/75|0|
| Host integration / Aspire smoke |118/118;6/6|0|
| Architecture / contract |183/183;41/41|0|
| Existing fictional browser command (includes Rail) / Trips fictional browser |86/86;5/5|0|
| General AppHost/Angular E2E |2/2|0|
| AI harness / README examples / CI inventory on Linux |134/134;13/13;63/63|0|

Frontend affected build/test/lint and production .NET build succeeded. Nx may restore unchanged task results from its configured cache; local merged-source Angular582/582 and earlier shared client255/255 are separate local evidence, not invented fresh CI execution counts. Existing style and action-runtime notices remain nonfailing; no budgets, timeouts or test requirements were relaxed. Exact commands and emitted counters are in the linked run logs and local ignored `TestResults/trips-m1/ci-source-final.log`; metadata is in `ci-source-final.json`.

## Review, schema and remaining gates

The six implementation P2 corrections were independently accepted before publication. A separate scoped review accepted the ten Rail/Trips conflict resolutions, retaining both Host compositions, SSR routes, exports, architecture boundaries and test lanes. Local merged-source architecture183/183 and Angular582/582 also passed before push.

Publication normalized one trailing blank SQL line only. Current SQL SHA256: `2A97B6953C45DD1051E9E7996191D4D6ED1B6B347BB2DEE74AFC8505CD91C4CF`; the earlier local verification hash remains historical. Migration semantics and model are unchanged. PostgreSQL tests use only owned disposable fictional fixtures and temporary test keys. No existing database or deployed environment was touched.

Task6.2 is complete from this source run. The follow-up documentation-only commit records this receipt; its final exact-head CI receipt belongs in the PR description, avoiding a self-referential receipt commit. Trips remains default-off. Full enabled Trips Host/Keycloak target acceptance, key/schema provisioning, real sources/models, rollout, OpenSpec sync/archive and PR merge remain separate.
