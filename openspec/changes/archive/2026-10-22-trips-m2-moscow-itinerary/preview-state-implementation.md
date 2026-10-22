# Task 4.2 implementation report — frozen for independent review

HEAD remains e654c76915103a82083ef2abc01c0579117c9749, branch codex/trips-m2-moscow-itinerary, worktree C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency. Source/local verification only; parent owns canonical task state and independent review. No Git mutation, Host/DB/key/model/provider action, package installation, external publication, UI edit, Q-output read/generation, cleanup, or subagent was performed.

## Owned result

Only four source/test files changed:
- apps/web/src/app/trips/trips-itinerary-state.service.ts
- apps/web/src/app/trips/trips-itinerary-state.service.spec.ts
- apps/web/src/app/trips/trips-state.service.ts
- apps/web/src/app/trips/trips-state.service.spec.ts

Clock token lives in the new state file. M1 receives a small opaque clean-empty foundation/read/stage seam, an opaque reactive session/plan context, and a monotonic reactive invalidation counter at existing read/edit/form/mutation start boundaries. Existing editor/CAS/highest-revision/reconciliation/save-later-edit logic remains the authority. Original M1 tests were retained; nine seam cases appended.

M2 retains availability, one operation identity and one preview only in memory. Operation object identity is the request/adoption epoch, so cancellation/new requests cannot collide. Accessors check M1 guards synchronously before effects; cleanup effects abort requests and drop references. Owner GET never calls refreshDetail and never advances baseline or comparison. An early cancellation listener prevents owner GET dispatch after dismissal while token refresh was pending.

Adopt uses one foundation/preview/operation for BOTH reads, checks exact package identity and authoritative unchanged owner foundation, then checks expiry and local guards immediately before ordinary M1 stage. Copied blocks contain proposed UUID/title/historical note and null times/estimate only. Title/brief and original CAS revision survive. No automatic PUT/rebase/retry. Empty/unsupported cannot stage. Saved manual blocks prevent generation.

Boundary consistency against the captured brief covers exact revision/destination/IANA/catalogue/dates, requested-interest partition and actual requested tag union, pace target2/3/4, walk60/90/120 and total240/360/480 envelopes, and exact origin/budget exclusion presence. These are consistency checks, not another route engine. Wrong-context actual vectors and independently valid mutated walking/dwell vectors are rejected. The two mutated vectors explicitly pass the standalone decoder first.

## RED and corrections

Raw logs are under .superpowers/sdd/tasks:
- task-4.2-red.log: no-op public interface, 43 expected missing-flow failures / 1 SSR guard pass in 44 tests. This establishes missing functionality, not semantic race evidence.
- task-4.2-green-initial.log: 43/44 passed. Remaining failure was a TEST expectation: an unknown policyVersion is refused by the pinned decoder, therefore unavailable rather than stale. Corrected only that expectation; client policy validation was not relaxed.
- task-4.2-red-semantic.log: 65/67 passed; two actual failing assertions showed cleanup flattening retained M1 uncertainty/conflict into stale.
- task-4.2-red-token.log: 65/68 passed; retains both prior failures and adds real late-dispatch failure (owner detail called twice instead of once after dismissal during awaited token refresh).
- Fixes delegate visible conflict/uncertainty to existing M1 barriers and observe cancellation before token refresh in the guarded read. No second mutation/CAS machine.

## Final verification

Commands from worktree root:
```text
node node_modules/nx/dist/bin/nx.js test web --include=**/trips*state.service.spec.ts --watch=false --skipNxCache
node node_modules/nx/dist/bin/nx.js test web --watch=false --skipNxCache
node node_modules/@biomejs/biome/bin/biome check apps/web/src/app/trips/trips-state.service.ts apps/web/src/app/trips/trips-state.service.spec.ts apps/web/src/app/trips/trips-itinerary-state.service.ts apps/web/src/app/trips/trips-itinerary-state.service.spec.ts --reporter=json --max-diagnostics=200
```

- task-4.2-green-seams-final.log: 105/105, 2 files, 0 failed/0 skipped, exit0. 75 M2 state cases +30 M1 cases (21 retained +9 seam).
- task-4.2-web-frozen.log: FINAL exact sources after introduced-warning cleanup, 698/698, 53 files, 0 failed/0 skipped, exit0; retained614 +75new M2 +9new seam. Final run started 2026-10-21 15:27:31 Europe/Moscow; Nx15.7s.
- Earlier successful complete runs are retained in task-4.2-web-full.log and task-4.2-web-final.log. The latter adds direct standalone-decoder validity assertions for the mutated pace vectors; final frozen run includes those assertions and warning cleanup.
- task-4.2-biome-current.json: exit0, 0errors, 36warnings, 0infos, 0new diagnostics, 4files checked. Remaining36 are ALL unchanged HEAD M1 noNonNullAssertion warnings:29existing M1 test locations +7existing M1 production locations. No warnings in new M2 files or new M1 seam/tests.
- task-4.2-biome-warning-provenance.json maps EVERY remaining diagnostic current file:line:column/category to exact unchanged baseline source line(s) and baseline source SHA256. Initial51warnings/4infos included15introduced warnings and4infos; all introduced diagnostics removed using explicit null checks and test fixture helpers. No suppressions, rule/config changes, unsafe fixes, or unrelated M1 test rewrites.
- Initial stdin Biome baseline exploration did not emit JSON diagnostics: task-4.2-biome-baseline-0/1.json contain raw original-source stdout, and adjacent stderr explains stdin not-fixed. These are NOT claimed as successful baseline lint runs. The provenance JSON uses current actual diagnostic output and exact git-show HEAD source comparison.
- Existing NO_COLOR/FORCE_COLOR warnings remain in Nx logs; no environment workaround to hide them.

Coverage includes both read orders/one read pending/failure of either/actual owner HttpClient timeout/availability timeout, actual M2 HTTP decoder with frozen JSON vectors, double-click, dismiss/new generation and late old responses, form-only/revert/read/plan A-B-A/owner A-B-A/logout/delete/failed mutation/save failure, immediate pre-effect privacy and SSR, package identity/expiry/pinned policy invalidation, final clock after both replies, unsupported/empty/partial/typed errors, stable UUID/null fields/no hidden facts, explicit later M1 save, malformed ordinary staging and original M1 regressions. No browser/Host acceptance or quality evaluation is implied.

## Resource/environment receipts

NX_DAEMON=false; NX_NO_CLOUD=true; NX_CACHE_DIRECTORY=D:/_Projects/_github/travel-agency/TestResults/trips-m2-dependencies-e654c769/nx-cache. node_modules stayed the existing owned D junction.

Parent measured the C headroom drop and authorized process-only TEMP/TMP=D:/_Projects/_github/travel-agency/TestResults/trips-m2-dependencies-e654c769/tmp. That longer temp path caused an Nx named-socket length failure BEFORE any tests; task-4.2-green-seams.log retains the precise diagnostic and is not counted as test RED. Parent then authorized process-only NX_SOCKET_DIR=D:/_Projects/_github/travel-agency/TestResults/m2-ipc-01a15a. Paths checked as ordinary directories; no global environment/configuration changes, plugin disabling, test timeout adjustment, cleanup, or cache deletion. Subsequent test processes succeeded with all these settings. Final C free2656731136bytes (~2.47GiB), above parent1.5GiB stop threshold.

## Handoff and limits

task-4.2-interface.md is the final component contract. Parent independently reviews frozen source before marking4.2 complete or dispatching4.3. Hash manifest task-4.2-hashes.json binds four TS files, interface/report and final receipts plus the six frozen actual API JSON fixtures.

Unknown pinned policy => contract/unavailable; valid changed dataset/version/hash/release expiry => stale; candidate404 stays ambiguous/stale and must not label global feature disabled. Availability remains independent of manual M1 readiness. Clock checks are best-effort client observations, no polling or OS-clock guarantee; package withdrawal after final successful read remains the already accepted best-effort race. Subsequent explicit PUT still passes ordinary M1 CAS. Source is frozen; no residual known implementation blocker is claimed, but independent review is pending.
