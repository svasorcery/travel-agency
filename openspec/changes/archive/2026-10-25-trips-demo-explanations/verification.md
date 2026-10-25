# Verification

## Scope and source identity

Verified locally on 2026-10-25 in the isolated `codex/trips-demo-explanations` worktree, based on `2a2763e15f3093fb6c011be8e47eb003ae8bf41a`. The user selected the bounded UI explanation change after explicitly deferring human review. Human scores/signoff remain absent.

Only the Trips component, its template and its existing component test file changed, plus this canonical OpenSpec change. The display derives daily coverage from the validated candidate's covered/unmet union and each day's topics; it stores no extra state and does not use the editable draft. Selection, API/decoding, adoption, persistence and data release code are unchanged.

| Final implementation file | SHA256 |
|---|---|
| apps/web/src/app/trips/trips-page.component.ts | d42b1af084774f824392562f29a5d49a1bb6f1c83f64b3378205a83695e9773c |
| apps/web/src/app/trips/trips-page.component.html | 5d6e2f11bc4355b8be9a3a25f8d03eeec55c58dfab8530b85f2a5b4cd064f7e0 |
| apps/web/src/app/trips/trips-page-itinerary.component.spec.ts | ca249f74c2d5b776cdae8b9a599ace4b3a7b0376242c90bff11395923a2c0bae |

Dependencies were reused from the existing installation with an identical package-lock; the worktree has a node_modules junction and a physical, digest-checked copy of pinned OpenSpec. No dependency manifest/lock change was made.

## Local checks

Commands ran from the worktree with `NX_DAEMON=false`, `NX_ISOLATE_PLUGINS=false` and cache skipped. Windows sandbox blocks child-process creation; the normal approved execution path was used for Nx and the pinned OpenSpec launcher.

| Check | Evidence |
|---|---|
| Focused baseline: `npx.cmd nx test web --watch=false --include=apps/web/src/app/trips/trips-page-itinerary.component.spec.ts --skip-nx-cache` | 38/38 passed before source changes. |
| Daily-coverage red phase | After correcting a test-only empty-array inference error, 4 expected behavior failures / 37 passes: missing whole/day summary, deduplication display, unset selection and empty-day explanation. No product code had been written for those failures. |
| Focused green phase, same command | 41/41 passed. Three tests added; the existing empty/unsupported test strengthened. Assertions include no plan mutation or Save call. |
| Walking wording regression probe | The existing fixture has two crossings and interval 12–22. Temporarily restoring only the old explanation produced exactly 1 expected failure / 40 passes. A `finally` block restored the corrected HTML byte-for-byte; receipt time 2026-10-25T13:25:32.075Z and restored hash match the table above. |
| Final complete web suite: `npx.cmd nx test web --watch=false --skip-nx-cache` | 60 files / 900 tests passed after the final test addition. Bundle completed 2026-10-25T13:26:31.094Z; exit 0. Local log: TestResults/full-web-final.log. |
| `npx.cmd nx build web --configuration=production --skip-nx-cache` | Passed, including api-client/ui-kit prerequisites and three prerendered routes; bundle completed 2026-10-25T13:19:54.977Z. Production TS/HTML did not change after this build; the later change only strengthened the test. |
| `npx.cmd biome ci --colors=off --reporter=summary` on the two changed TypeScript files | Exit 0, no formatting errors. Five warnings and eight infos in pre-existing component code match a separate read-only run against original dev: optional chaining, non-null assertions, template literals. |
| `node tools/openspec/run.mjs validate trips-demo-explanations --strict --json` | Valid, no issues. |
| `git diff --check` and base ancestry | Passed; HEAD remains the declared base and the branch descends from it. |

Build warnings were reported for unchanged Support, Flights and Trips SCSS budgets (4.24/4.50/5.01 kB against 4 kB warnings), plus Node color/deprecation warnings. They were not hidden or fixed by broadening this change. During this initial source-check phase, no .NET/Host/DB/full-stack restart or human UX run was performed. Later CI and the separately authorized local-demo restart are recorded below; human acceptance remains deferred.

## Independent source review

Fresh-context reviewer `/root/review_trips_demo_explanations` read the uncommitted diff, change artifacts and related validators. No implementation regression was found. It confirmed selected-set derivation, per-day filtering/deduplication/order, empty behavior, absence of new private retained state or mutations, and the unchanged generator/loader/API/v1 boundary.

The review found one P3 gap: the old walking tests did not protect the upper-only wording. The existing metadata test now binds a nonzero crossing fixture, the displayed 12–22 interval, the upper-only explanation and retained original assumptions. The narrow recheck closed P3 and confirmed the HTML hash above. The reviewer did not independently execute tests; command results are the primary agent's receipts.

## Unchanged release and remaining gates

| File under moscow-outdoor/1 | Verified unchanged SHA256 |
|---|---|
| moscow-v1.json | 631fd5cd9650e3228af87ef8a31219d7ea59a2e4af42bc4328453f32268ffb08 |
| moscow-v1.manifest.json | 8f364b38540a3d8dfd21272021c50f3f18ca6c01d2863617368c12d2e6f60e43 |
| walking-evidence.json | 196c5cd39481bdb55b6c123a4555571a0aa6f2eabc720e24ef3d70f63e3b397b |

V1 validity, original assumptions and strict loading remain intact. The real Q01–Q12 material/audit archives and A/B forms were not edited or regenerated. The separate local human-review decision records deferral, not acceptance. No score, calibrated time, confirmed outdoor access or quality improvement is claimed by these checks.

The checks above describe the prepublication source checkpoint. At that time, CI/merge had not occurred and the local demo still used earlier assets. The later receipts below supersede that delivery status without changing the historical test results or authorizing v2/real-user rollout.

## Authorized PR publication

On 2026-10-25 the user authorized committing/pushing this reviewed change, opening a separate PR to dev and waiting for successful ordinary CI, without merge. Before publication, a fresh fetch confirmed origin/dev at `2a2763e15f3093fb6c011be8e47eb003ae8bf41a`; the branch descends from it and all three implementation hashes still match the verified table above. The mandatory formatter/safe-fix commands made no changes. Exact PR head/run/attempt/job receipts are recorded on the PR and in the task's local publication receipt after the checks finish; green CI is not human or rollout acceptance.

## Final PR and post-merge acceptance

[PR75](https://github.com/svasorcery/travel-agency/pull/75) final head was `7a9c1473950d4577818abb22c4313c8961a67b63`, based on dev `2a2763e15f3093fb6c011be8e47eb003ae8bf41a`. [PR CI40205101433](https://github.com/svasorcery/travel-agency/actions/runs/40205101433), event `pull_request`, attempt 1, succeeded at 2026-10-25T15:16:47Z: 20 applicable jobs passed; only paid `test-ai-evals` was expected-skipped. Its job checkout was the synthetic merge ref `97f9760f69e2a4151acfdbcda53e7c7be06d895f`; the frontend browser suites reported 92 Flights-demo and 80 Trips-demo passes. The dedicated Trips full-stack job and general PR E2E both passed.

After explicit merge authorization, PR75 was squash-merged into dev as `dfb8e18b9510e6095b99eda42df71834b39e118a` at 2026-10-25T16:05:29Z, with an exact-head guard and no rule bypass. [Post-merge CI40210020699](https://github.com/svasorcery/travel-agency/actions/runs/40210020699), event `push`, attempt 1, succeeded at 2026-10-25T16:14:41Z: 19 applicable jobs passed; paid `test-ai-evals` and PR-only `test-e2e` were expected-skipped. Trips full-stack executed successfully in both runs. Run identity, event, attempt, result and all job conclusions were read again for documentary closure.

## Isolated local demo after merge

The existing owned stack `feef71a41691bf7b4fd0ca418f6e33e7` was stopped and resumed through the normal local-stack commands after fast-forwarding its clean checkout to the merge SHA. Host/tool binaries and ordinary Angular assets were rebuilt from that source; readiness passed at 2026-10-25T16:21:13Z. All six services restarted with the same mounted storage. Application time remained real, and only loopback addresses were used. This was the existing fictional demo, not shared-target provisioning.

The before/after comparison preserved all 13 saved plan/protection-key snapshots and 98 frozen audit/material/form files. These 13 plans comprise the 12 Q-case foundations and one separate explanation-smoke foundation; the restart comparison does not claim adoption or saved generated blocks in all 13. A fresh real Keycloak authorization-code/PKCE login reopened the smoke plan at revision 1. One ephemeral three-day candidate then verified whole-trip versus daily interest coverage and upper-only crossing allowance, while retaining original assumptions. There were no new plans, adoption, save or delete requests, and no external browser requests. The served CSR index and all newly built web assets matched the published build; completion was recorded at 2026-10-25T16:25:15.462Z.

The first standalone browser check incorrectly navigated the whole document after login, losing the in-memory session. The second used the correct deep-link/callback flow but failed its default five-second editor assertion. Read-only diagnosis obtained owner GET 200 and then a populated editor. The final check used the same 20-second assertion deadline as the canonical full-stack configuration and passed. The timing cause was not established as a product defect; no application changes or hidden retries were made. Both failed receipts, the test-only corrections and the passing result remain retained.

Local-only evidence lives under `TestResults/pr75-postmerge-20261025/` in the owning checkout; these ignored files are not committed or implied to be downloadable CI artifacts. Hashes below bind the retained receipts, not private payloads or credentials.

| Local receipt | SHA256 |
|---|---|
| completion.json | dcc7b2f61cbb854b27d2b6e689bb2e25c49f56edc63d084b5ac026630d9a97ef |
| before.json | 4ddc2967bb28ec38caf900bf11d4556c8812213f74ac57654d663d1b271aee4f |
| after.json | 722b5f1e59c1054b3212e6f1e394a5efc7ea10a20797abdcc1286d3ecc9a98f2 |
| ui.json | 5f30fd4375670f562feffbb4497b53d1dedc11c5a958b143bbfaa7e8298f50ba |
| ui-failure.json | 9adced07dceea710997d5be3f3b6feee033b0c7e7fa0b284eb631df484f9b504 |
| ui-attempt2-failure.json | 84be1226970390b8c3d2288f5cd946e1450e14ed022da2e1d1f4258446dc7c88 |
| ui-test-correction.json | 3ea011b1b6a315a90530e66d0bb84490fff5fcd503c4b31106da33db04e37c84 |
| ui-wait-correction.json | 258d29cf286864a9ed054cf242ec89e95ad41c5da2ebc73c1051b17d2a0a9270 |
| build-artifacts.json | 21da29a72bed8acc6690933c359f2c4cc7bd723061f92ad8a59875b6335abc86 |

The frozen Q01–Q12 source run remains `moscow-v1-materials-20261025T095646076Z`, output-set SHA256 `3fde1c3164cab17c6f948aba6cca0624799ecc7221d7a8762dd4b4b519dc3668`. Independent agent fact audits and this functional smoke are separate from A/B review. The user deferred human acceptance; human scores remain blank. V1 still expires at `2026-11-17T03:55:13Z`; v2, field calibration and real-user enablement remain unaccepted.
