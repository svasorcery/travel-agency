# Trips M3 implementation ledger

Current documentary status: PR81 is merged and source/ordinary CI accepted. See [publication receipt](publication.md). The execution notes below retain their historical phase states. Real-model6.3 and operational rollout remain pending; no model was called and M3 was not enabled.

Plan: tasks.md; binding specification: specs/trip-brief-extraction/spec.md.
Base: 35953fd6c8feabf16acd7fb944201711e3d8c5fa.
Branch/worktree verified unchanged and descending from the recorded fresh base.
User “го дальше” authorizes the planned source/test phase. No staging, commit, push, PR, merge, model execution, target/rollout or retained demo edits.

Execution: shared contract first, independent file ownership for backend/AI/UI, then whole-source integration and independent review. Test behavior before implementation; fake IChatClient only. Docker tests may create only NEW owned disposable resources; existing demo remains untouched. Optional real-model task6.3 stays pending.

Preflight interfaces: leaf TripsBriefRequested/Parsed and seven-slot fields are shared between Trips Infrastructure and AI; public HTTP shape is design section5. Root owns leaf and contract/architecture tests; workers own disjoint source areas. Current .NET assets and npm dependencies are being restored in this fresh worktree.

Baseline: Docker29.8.2 accessible with required sandbox permission. Initial --no-restore test cannot run before project.assets.json exists; normal restore/test is in progress. No baseline behavior failure inferred.


## Source/test progress and explicit rulings

- Baseline completed: Trips unit298/298; shared contract TDD RED missing types -> GREEN5/5.
- Backend owned source complete: whole Trips unit353/353; expanded HTTP/composition31/31; earlier disposable-PG full integration280/280, later additions verified separately. Catalogue/Core/schema/packages/public assets unchanged.
- AI owned source:70feature tests and4fresh-PG ledger tests; broader true non-Docker AI regression226/226. Model/pricing/permission defaults unset/off. Actual Host/NATS proof is in progress; not yet accepted.
- UI owned source: final API-client430/430; final focused Trips325/325; broader web944/944 before narrow final guards; new isolated fictional Chromium3/3. No retained demo used.
- Architecture after scoped consumer integration:243/243. Trips consumes only its TripsBriefExtraction leaf capability, never Flights NL/fare contracts; Core/Itineraries/unrelated Api remain forbidden.
- Root mechanics: exact48 text/oracle transcription verified row-by-row, all48 normalizer slot/issue oracles and actual full prompt bounds pass.20focused contract/evaluation tests pass with fake pipeline only. No model quality measured.
- Actual Host M3 integration has retained infrastructure RED attempts: initial30s container phase timeout, repeated3s CoreNATS cleanup failures, then15s opt-in cleanup correction measured actual3.058–3.395s; legacy timing/product deadlines unchanged. Current membership/configuration issue is being debugged, not called successful.

Ruling: first source implementation wires only the existing Anthropic SDK adapter and reviewed notice, with no provider/model selected or authorized by default. A configured local/fictional label cannot falsely describe an Anthropic transmission; another provider requires its real adapter/notice acceptance. Cost if wrong: an additional adapter/notice change, never unapproved calls.

Ruling: C: ENOSPC stopped writes and tests. Only dependencies/build outputs created for this task were relocated under D:/_Projects/_github/travel-agency/out with checked paths. node_modules target retains the literal node_modules basename; worktree junctions preserve Node peer lookup and logical C-based test-root discovery. Existing primary source/dependencies, user data and demo remain untouched. Initial partial source files were recreated and retested. Cost if wrong: recoverable generated artifacts; source and targets are preserved.

Ruling: unlisted ambiguous alternatives are not implicit empty-list quality oracles. Only explicitly listed alternatives are exact-scored; independent structural admissibility stays required. No corpus input/slot-state/value/issue row changed, and no model output existed.

Ruling: existing fixture3s cleanup was below measured owned CoreNATS disposal; only new briefEnabled cleanup gets15s, with actual completion still required. Startup30s, old NL/fare fixtures and3500/4500/5000ms product limits stay unchanged. Cost if wrong: test fails after a longer bounded cleanup observation, without product behavior change.

Current separate pending gates: optional real-model task6.3 not run; new independent holdout not authored or opened; real private-text permission, target provisioning/enablement, original M2 rollout6.3, human A/B, field calibration and Moscow v2 remain pending. No stage/commit/push/PR/merge/target/model/demo action authorized or performed.


## Final source review and integration acceptance

Independent whole-source review found4P2 and noP1. Fixes: keep negation through interest lists; reject negated duration before deriving an end; validate weekday associations beside every date; guard untransferred text/review on navigation/unload and clear logout synchronously. Backend12new regressions passed (wholeunit365), UI navigation tests passed (Trips330/Chromium4). Scoped independent recheck closed all4 without residual findings.

A final evaluator self-review observed a failing one-authorization/restart test. Atomic metadata-only one-shot consumption now precedes dispatch; claims persist on success/failure/crash. New authorization may name exact remaining IDs; old calls are not replayed and original denominator remains. FullContract63passed; separate scoped independent review approved this bounded fix. No actual paid execution occurred.

Final current-product checks: Trips integration300/300; composed real Host/AI/NATS M3 tests3/3 after semantic fixes; two-city full-stack all lifecycle/browser/restart/privacy/cleanup stages passed. Product source did not change after first full-stack start; two distinct dirty-tree receipts retain their actual hashes. See verification.md for exact commands/scopes/receipts, phase-bound counts, unchanged artifacts and remaining gates.

Ruling: UI client files use the existing concise capability naming `trips-brief-api.service.ts`/`TripsBriefApiService`; task wording is reconciled to actual names. This changes no API or behavior. Cost if wrong: local rename only.

Ruling: the evaluator needs a stable approved local receipt directory to prevent manual permission reuse across restarts. This is a metadata file before dispatch, not a generic AI registry, persistence of wishes or execution authority. Relocating/resetting that directory requires new permission. Cost if wrong: run rejected without dispatch; unknown spending never silently resets.

At source/local closeout, acceptance was source/local only. Actual model gate6.3 is pending, defaults remain off, and no publication/target/private-text enablement is implied.
