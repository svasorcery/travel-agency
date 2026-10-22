# Rail M2 — sync/archive and source acceptance

Date: 2026-10-22. The user authorized synchronizing both Rail specs, archiving the completed change, updating completion records and preparing a separate docs PR. The initial commit is documentation finalization. A later, separately authorized unit-fixture synchronization correction is recorded below. Neither enables real Rail or authorizes another measured run.

## Accepted implementation

| Boundary | Identity and result |
|---|---|
| Original fresh implementation base | `a8721effd9056459d217966083816455fbea599d` |
| Rail implementation commit | `ac9fe1feb4aee348a676c1dd7af5776eb3c00a32` |
| Separate Storybook remediation | `99e8888db9417062610a34b36ebafcd1c7b58102`; upstream 10.6.3, security gate and backport unchanged |
| [PR #64](https://github.com/svasorcery/travel-agency/pull/64) | Merged normally into dev as `4ccdcaf1b9e1819f4532c0afe014ea611b550186`; no admin/bypass or rule change |
| [Exact-head PR CI](https://github.com/svasorcery/travel-agency/actions/runs/39742027955) | Attempt 1; head `99e8888...`; all 19 ordinary jobs passed, including final E2E 2/2; paid AI eval skipped by its existing condition |
| [Post-merge CI](https://github.com/svasorcery/travel-agency/actions/runs/39746706368) | Attempt 1; push on `4ccdcaf...`; all 18 applicable jobs passed; only PR-only E2E and paid AI eval were skipped |

The merge's Git tree `a19b9fc9d54fd6f34bc17dcf1a1d24eae88bd197` equals the accepted PR-head tree. Post-merge logs confirm Host HTTP 490/490, Rail unit 74/74, Rail adapter/HTTP 136/136, Host integration 118/118, Aspire smoke 6/6, and fictional browser suites 92 + 5. These counts are observed results, with no failed or unexpectedly skipped applicable jobs.

Separately authorized actual Host/OpenAPI passed 3/3. The single fixed M2 fictional measured attempt passed 6/6 cycles and 6000/6000 useful operations, zero failures and memory recovery 6/6. Its executed source digest remains `41ef618fd29d2d5dce3272bf71f1cd9d8c67ad4adba170223bd78f0bb4d4a1bb`; its profile digest remains `f7f81e4c8d9f9c63b241b6f0e6ae98da2d5fcbdb5fbf3f7d853202c5d0159206`. Later test-fixture, dependency and documentation changes do not replace that identity. See [runtime verification](runtime-verification.md), [source verification](verification.md) and [personal review](personal-review.md).

## Preserved failures

The [initial PR CI](https://github.com/svasorcery/travel-agency/actions/runs/39739096791), attempt 1 on `ac9fe1f...`, remains failed: the unchanged audit gate found the Storybook advisory; 2 ordinary jobs passed, 1 failed, 16 dependent jobs were skipped, with the paid AI eval separately skipped. A separate dependency commit fixed it without a waiver. [Storybook remediation](../../../../docs/operations/2026-10-22-storybook-security-remediation.md) retains the advisory, local npm 12 OpenSpec audit timeouts, successful npm 10 gate and compatibility/build checks.

Source-level REDs and subsequent fixes remain in verification.md, including parser/decoder/lifecycle regressions, the telemetry fixture race and platform-error classification. The pre-publication Nx fixture-import errors and successful local correction remain recorded. ASP.NET analyzer/Verify and advisory frontend diagnostics were not suppressed. Failed or incomplete historical runs are not relabelled as green.

Raw local receipts remain under the implementation worktree's ignored `TestResults/rail-m2/`, `rail-m2-runtime-20261022/`, `rail-m2-publication-20261022/`, `rail-m2-storybook-20261022/` and `rail-m2-postmerge-20261022/`; those paths are relative to `TestResults/` where shortened. They are not claimed to be committed artifacts.

## Sync and archive

Finalization branch `codex/rail-m2-openspec-finalization` starts at freshly fetched `origin/dev` `4ccdcaf1b9e1819f4532c0afe014ea611b550186`. The original implementation branch is preserved. OpenSpec resolved the repository-local planning root, all artifacts done and all 21/21 tasks complete.

- [rail-schedule-search](../../../specs/rail-schedule-search/spec.md): 2 requirements added; Bounded execution and cancellation modified with all 3 prior scenarios preserved and mixed search/detail admission added. The existing title/Purpose and 22 untouched requirement blocks remain unchanged. Result: 25 requirements / 99 scenarios.
- [rail-journey-details](../../../specs/rail-journey-details/spec.md): new main spec with the delta Purpose copied verbatim, 9 requirements / 41 scenarios.
- Every delta operation matches the main specs after synchronization. Main specs contain one Requirements section and no delta operation headers. Strict validation passed all 14 main specs.
- The complete change, including `.openspec.yaml`, design, tasks, delta specs and original review/runtime receipts, is retained in `openspec/changes/archive/2026-10-22-rail-m2-journey-selection-details/`.

The new docs PR's CI is recorded in that PR and is separate from the implementation receipts above. The original documentation commit 58cd276 changes no implementation, test, dependency, workflow or Trips source. The later explicitly authorized test-only correction below is the sole exception; production code, assertions and limits remain unchanged. Real source shape/rights/account quota/restart/banner and target rollout remain conditional external gates; Rail search and details remain default-off.

Finalization checks: strict OpenSpec validation passed all 15 items (14 main specs and the untouched active Trips change); AI harness passed 134/134; README examples passed 13/13; 218 local Markdown links resolve. Archive comparison retained all 9 original files, preserved delta/proposal/metadata hashes, and found no implementation/test/dependency/workflow or Trips edits in the initial docs commit. These checks are separate from the new docs PR CI.

## Separately authorized unit-fixture synchronization

[Initial docs PR CI39752476892](https://github.com/svasorcery/travel-agency/actions/runs/39752476892), attempt 1 on 58cd2760f325ec7f9a0876e07f607603bc8df0bd, remains failed: 17 ordinary jobs passed, test-host-http failed, final E2E was dependency-skipped, and paid AI eval was separately skipped. Host HTTP passed 490/490; Rail unit passed 73/74. The failing M1 test Cancelling_all_waiters_quarantines_uncooperative_loader_and_no_late_publish expected loader.Calls=1 but observed 0. The test and production service were unchanged from the accepted implementation.

The service deliberately yields before entering the loader. The test cancelled its waiter immediately and asserted the call count without proving that its intended uncooperative loader had started. Under that scheduling order the assertion raced with fixture startup. The user separately authorized a test-only fix in PR #66: the fixture signals Started after incrementing Calls, and the test awaits that signal before cancelling. The bounded 5-second wait is fixture coordination, not a change to product deadlines or acceptance thresholds. Assertions, production code, fixture completion behavior and Trips remain unchanged; no sleeps, skips or assertion weakening were added.

Fresh offline verification passed the targeted scenario 1/1, the entire Rail unit suite 74/74, and a bounded set of 10/10 independent targeted runs. CSharpier check passed. Commands used --no-restore, explicit SolutionDir/SolutionName for the targeted build, then --no-build for the full suite and repeated scenario; no Host/DB/provider calls occurred. Original CI logs, the proposed/approved patch, local logs and TRX are retained under TestResults/rail-m2-finalization-20261022/. Fresh CI for the separate fix commit is recorded in PR #66; the original failed run is preserved.
