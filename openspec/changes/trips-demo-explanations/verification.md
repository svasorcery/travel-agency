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

Build warnings were reported for unchanged Support, Flights and Trips SCSS budgets (4.24/4.50/5.01 kB against 4 kB warnings), plus Node color/deprecation warnings. They were not hidden or fixed by broadening this change. No .NET/Host/DB/full-stack restart or human UX run was performed for this UI-only change.

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

The checks above record local source acceptance before publication; they do not claim CI, merge, deployment, v2 release or real-user enablement. The previously running local demo still uses its earlier published assets.

## Authorized PR publication

On 2026-10-25 the user authorized committing/pushing this reviewed change, opening a separate PR to dev and waiting for successful ordinary CI, without merge. Before publication, a fresh fetch confirmed origin/dev at `2a2763e15f3093fb6c011be8e47eb003ae8bf41a`; the branch descends from it and all three implementation hashes still match the verified table above. The mandatory formatter/safe-fix commands made no changes. Exact PR head/run/attempt/job receipts are recorded on the PR and in the task's local publication receipt after the checks finish; green CI is not human or rollout acceptance.
