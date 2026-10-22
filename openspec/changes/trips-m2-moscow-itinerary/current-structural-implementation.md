# Task 4.8 — current source structural verification after Rail

**DONE_WITH_CONCERNS — requested executable local gates PASS; ready for fresh task review.** Parent owns canonical integration and the task marker. This verifies current source and fictional/demo integration only. Actual Q, S and actual-Q Node remain **UNSEEN**; neither Q_demo nor whole M2, CI, real banner, human quality, target or rollout is accepted here.

## Source identity and preserved authority

Worktree: `C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency`; branch `codex/trips-m2-moscow-itinerary`. Original requested fresh base `e654c76915103a82083ef2abc01c0579117c9749` still descends into integrated HEAD `a8721effd9056459d217966083816455fbea599d` (ancestor check exit0). HEAD is integration context, not the feature identity. No branch switch/history rewrite/Git mutation/staging/commit/push occurred; staged diff is empty.

Actual reviewable corpus against original base: **192 paths = 64 tracked changes + 128 untracked; zero deletion/path/byte drift**. The64 include25 upstream integration paths and39 tracked WIP paths; relative HEAD this is167 WIP paths. Start inventory SHA256 `851c404be98d0c5c2ed14874d989713c8f8e8563eb8da718115f3612a83871ca`; actual WIP+integration tree SHA256 `505e443683a073dfb885ef98fab7f33f5a433289a97d50da5284a0f3f59a3a7d`.

Frozen PhaseA was independently rehashed at start and finish: **1657/1657 source inputs**, **718/718 Integration output files**, **91/91 resolved build inputs** match exactly. Their manifest SHAs remain respectively `e3bdc08aeb9795d2f576a3645ca08111276ee1b39d85c91ae5f302ed733cd9f7`, `47008604aae1bb16b223c137282adcb572034785130ab302935f33959e5cb846`, `39878d20f8d0c231d7ab25f628586474fd5f2543989a8711c26ff4aee07ca7a6`. No source/test/config/Data/case/protocol/lock edit was made by this verifier. Only task4.8-owned scripts/reports/raw manifests and normal build/cache outputs were written.

All evidence paths below are relative to `.superpowers/sdd/tasks/task-4.8-evidence/`, unless a complete path is shown. Exact argv/start/end/exits are in `checks-results.json` and immutable `check-results.jsonl`; complete stdout/stderr are separate files. `task-4.8-hashes.json` inventories the complete reviewed output package; `task-4.8-review.patch` contains all authored verification scripts and proposed reports/maps.

## Fresh current checks

| Check | Actual result | Raw proof |
|---|---|---|
| `dotnet csharpier check` exact45 changed/untracked C#/csproj inputs |45 clean, exit0|csharpier.stdout/stderr.log|
| Installed Biome check exact48 supported changed inputs, JSON reporter/max10000 |0errors,145warnings,220infos,0omitted;48unchanged,exit0|biome.stdout/stderr.log + biome-warning-provenance.json|
| `git diff --check` |exit0, empty output|git-diff-check.stdout/stderr.log|
| Pinned `node tools/openspec/run.mjs validate trips-m2-moscow-itinerary --strict --json` |valid,issues=[],exit0|openspec-m2.stdout/stderr.log|
| Same pinned tool `validate --all --strict --json` |15/15valid =2changes+13specs,0failed|openspec-all.stdout/stderr.log|
| Dependency-free full harness selftests and validator |134/134passed,0skip;9instruction pairs/8skills/5agents/5legacy/6pinned effective skills|ai-harness-tests + ai-harness-validator stdout/stderr|
| Dependency-free .NET inventory selftests and validator |63total,62passed,0failed,**1existing platform skip**;validatorPASS|dotnet-inventory-tests + dotnet-inventory-validator stdout/stderr|
| Actual Nx web/api-client project and affected queries, installed `node_modules/nx/dist/bin/nx.js` |all exit0; Data=>[web]; wire=>[api-client,travel-e2e,web]|nx-*.stdout/stderr.log + effective-nx-input-proof.json|
| Current ordinary api-client, `node node_modules/vitest/vitest.mjs run --config shared/ts/api-client/vite.config.mts --exclude src/trips-itinerary.real-demo.spec.ts --reporter=json --outputFile=...` |**356/356,17files,0failed/skip/todo**,exit0|ordinary-api-client-results.json + stdout/stderr|
| Current Architecture `dotnet test tests/Travel.Tests.Architecture/Travel.Tests.Architecture.csproj -c Debug --no-restore --logger trx;LogFileName=current-architecture.trx --results-directory ...` |**208/208,0failed/skipped**,exit0|current-architecture.trx + architecture.stdout/stderr.log|
| ONE actual production Angular/Nx build with `--configuration=production --skip-nx-cache` |exit0,cacheSkipped;13.2sNx/8.874sAngular;411.84kBinitial;75actualdistfiles;3publicprerenders|production-build-start/result.json + stdout/stderr + production-dist-manifest.json|

Exactly ONE predeclared future spec is excluded from the bound ordinary Node set: `src/trips-itinerary.real-demo.spec.ts`. Its13 checks were listed only by accepted PhaseA and remain pending until authorized PhaseB fixtures exist. **356 ordinary passed +13pending is not369passed, a full unfiltered suite or CI acceptance.** Current ordinary JSON includes the existing Rail decoder16 and Flights SSE19. No unfiltered Integration or actual-Q Node test was run. Architecture built referenced Host/AppHost projects as normal compile dependencies; neither process was launched.

The inventory platform skip remains `inventory-owned project paths must not be symbolic links`: Windows Developer Mode is required for that test's symlink creation. No OS setting/coverage/timeout/policy was changed. Harness `verify-codex.test.mjs` is an ordinary dependency-free selftest, not the authenticated Codex runtime verifier. No external task/model was created.

## Production artifacts, public data, privacy and warnings

All **six actual** production files under `dist/apps/web/browser/trips-data/moscow-outdoor/1` equal current canonical source bytes; the separate manifest's five child digests also match. No development server or deployment proof is substituted.

| File | Bytes | Canonical = actual production SHA256 |
|---|---:|---|
|ATTRIBUTION.txt|869|`0c8bca1abb555d22fc9a30133287abb9a11cc8554169ede7c379b9473cb01172`|
|input-manifest.json|295|`d5a3025a6670d7aaccfaaff8c15a085197c2c9734fec50339a6ecf2322870ac8`|
|inventory.json|7711|`c04bc32ca50be5fa5caa6057bf89948fee874c01ecdc418d4bd92059695f01ae`|
|moscow-v1.json|101163|`631fd5cd9650e3228af87ef8a31219d7ea59a2e4af42bc4328453f32268ffb08`|
|moscow-v1.manifest.json|488|`8f364b38540a3d8dfd21272021c50f3f18ca6c01d2863617368c12d2e6f60e43`|
|walking-evidence.json|137293|`196c5cd39481bdb55b6c123a4555571a0aa6f2eabc720e24ef3d70f63e3b397b`|

Actual public prerenders remain only `/`, `/hotels`, `/hotels/reservations`. Current `app.routes.server.ts` explicitly renders Trips and Rail client-only; no private plan or preview is prerendered. Every actual dist file is hashed in the75-entry inventory. Effective Nx build retains external canonical Data input/copy; web/api-client tests retain the shared raw wire JSON input. Existing CI topology, architecture inventory and single offline-helper step remain intact; no new registry/service/.NET cross-module dependency was introduced.

**Known final-review caveat:** current Trips CSS is **5.01kB,1.01kB above the4kB warning budget**, versus prior walking-only4.83kB/+830B. Existing Flights4.50kB/+502B and Support4.24kB/+238B warnings remain; both SCSS files are byte-identical to original base. No budget increase or style change was made. `style-warning-provenance.json` and raw stderr preserve all three exact warnings. This remains a P3 candidate for final6.1triage, not a failed build or a hidden waiver. NO_COLOR/FORCE_COLOR and NodeDEP0205 warnings are also retained in complete stderr.

Biome's145warnings are fully retained:133 on exact acceptedR3-owned source (132non-null+1optional-chain), plus12existing outside that scope (1oldTrips-page-spec,3browser-fixture,8workspace-spec). This does not relabel the earlier Rail implementation's new warning origins as old. No task4.8product/test edit introduced a warning and no suppression/config migration was applied. All220infos remain in the raw JSON. Canonical hashed Data/frozen cases/raw wire JSON retain formatter-only exceptions; parser/lint stays enabled. EOL proof verifies all six canonical files LF/noBOM and frozen CRLF quality-cases raw/filter Git blob equality.

## Exact reuse and current dependency identity

Current root node_modules is physical. Independently rehashed **59,458/59,458 installed files,741,458,297bytes**, all equal accepted physical-copy manifest SHA `37535d5d35fb050a2984ade4a2ae295aac4bedaaeb6a0fe7064632f79f2c263c`;0mismatches. Current package SHA `9bdf9692353206cdfb6b1d44f37eb96903051374522e1fda61b2a3f415d2a6e8`, lock SHA `69a09b317c29e502b81487e1c1853ae0cfa5f0e3eb31940a873fb20cd9b8766b` match installed D a872 inputs. Installed Vitest4.1.12/Vite8.3.4/TypeScript5.9.3/Biome2.5.15, Node26.11.1; active .NETSDK10.0.301 differs from CI Node22/.NET10.0.203.

Retained local consumer7/7, dependency-validator15/15 and physical security60/60 raw log SHA match the accepted dependency receipt; all installed bytes/source inputs match. These were not gratuitously rerun. Earlier security-on-junction failure and artifact-command setup failure remain historical. The earlier approved artifact-only invocation also audited and is recorded separately. **Fresh full network audit remains deferred with no human permission; no direct or indirect npm audit/check:dependencies/check:dependency-artifacts/install was called.** Installation/local checks are not complete security or CI acceptance.

| Reused execution | Actual result and reuse basis |
|---|---|
| Trips Unit/Core/Application |294/294,0skip; all latest accepted source and raw TRX hashes exact|
| Strict real package loader |98/98,0skip; source/Data/csproj closure exact; old web project config is explicitly superseded by fresh effective Nx/build proof|
| Itinerary HTTP/composition/codec |85/85,0skip;36/36accepted source/raw manifest refs exact|
| M1 HTTP |30/30,0skip; raw and production codec source exact|
| Offline helper |15/15,0skip; helper/test/Data source and accepted raw exact; no rebuild/download|
| Current UI R3 source/full-web |21/21frozen source;846/846,57files,0skip; R3full/focused raw and approved personal+independent reviews exact|
| Current fictional Trips browser |80unique IDs/results/ledgers =5M1+30walking+45Rail,allpassed/retry0;0skip; all actual ledger hashes exact|
| Existing standalone Rail browser |9/9unique IDs/results,allpassed/retry0,0skip; unchanged source+accepted raw exact; no new per-case terminal ledgers claimed|
| Source-only PhaseA |full api-client tsc,1Factdiscovery,13literalNodecheckdiscovery,11pure synthetic recorder guards,19existingSSE; exact report/13raw artifacts verified; no Q/S generator/results|

Historical manifests were not silently relabelled current. Old4.3 has5superseded UI hashes, old4.4 has4 and old4.2 has3; current accepted R3/web/browser replace those affected closures. All13old4.1client entries and45oldArchitecture closure refs match, but the one-line Flights mock typing changed an auxiliary spec, so fresh ordinary356 and current208 were executed. Backend/helper latest closures all match except loader-era `apps/web/project.json`, now covered by current queries/build.

The84historical browser files remain create-only under D `prior-browser-evidence`, manifest `4ea63d693f29b3041c448f689ba26df33b2d26088b0d08cb0719ea1514a30d38`;84/84independently rehashed. Final current80ledgers total4006page requests/1079API/137Rail/25explicitPUT/0unknownfailures/0externalattempts, plus6asset+2document same-origin GETs recorded separately. Accepted current six PNG visual views are reused because all current UI/browser closure bytes match; no browser or screenshot rerun. Owned ports4201/4203/5100 retain explicit closed ECONNREFUSED receipts.

Actual served frame policy permits self plus exact official yandex.st URL, with no CSP response header. RAIL08 applies stricter TESTONLYselfCSP before a synthetic real response, composed with actual component otherwise-valid real-view-only refusal. It does not prove an official banner. Preliminary v2externalattempt was aborted before network and is retained; route-abort did not prove iframeerror-clearing. Earlier preliminary source revisions were not all snapshotted, so final exact-source binding is limited to v7. No preliminary failure is relabelled a pass.

## Scenarios, frozen quality and next gates

Proposed canonical Rail map: `.superpowers/sdd/tasks/task-4.8-rail-scenario-coverage.md`; machine proof `rail-scenario-receipt-map.json` maps **12requirements/35namedscenarios** to exact current semantic sources, real raw browser IDs/ledgers or structural receipts. Real-source retention/no-bypass rows use the actual component positive otherwise-valid real refusal, separate from the induced CSP browser contour. Structural acceptance rows bind current ancestry/dependencies/assets/absence of outputs. No real-source or human acceptance is inferred.

Walking `current-walking-scenario-map.json` updates all62 rows without changing the spec: **58current source-semantic rows +4explicit protocol-pending rows16/60/61/62**. Current846web and v7oldwalking ledgers replace affected old UI receipts; backend/helper exact evidence is reused. Row37 explicitly separates the M2 Unit `ITripPlanCodec` test double from passed real M1 `TripsSizeHttpTests.Near_limit_utf8_full_detail_is_readable_and_next_larger_candidate_never_commits`, which uses actual `TripPlanJsonCodec`, complete reserved fields, multibyte save/reload and next-larger refusal. Candidate response cap is a distinct HTTP proof.

Frozen cases SHA remains `f50b7511fc12642b20df7ca207c780740382eeb4a482258929551ce29aa5756c`; fixed clock remains EXACT `2026-10-21T14:16:33.0000000Z`; recipe/protocol/Data match PhaseA. Exactly three future fixtures `responses.json`, `run-binding.json`, `results-manifest.json` remain absent before/after. Rail45browsercases do not change walking12Q+12S or quality thresholds. Source harness format/build/list/tsc/guards are preparation; no run-binding or evaluation output was produced or observed.

Next: parent personal acceptance plus **fresh independent4.8review** over the complete verification patch/raw hashes; then separately fresh most-capable pre-output5.2method review, and explicit parent authorization before original worker PhaseB. Final6.1whole-M2source audit/fixes (including CSS triage), ordinary exact-head PR/postmerge CI, public deployment/real banner/rights/quota, Host/Keycloak target and human D_live/Q_live/rollout remain separate.

## Operational record

Per-process only: current D a872 tmp/Nxcache, short `m2-ipc-01a15a`, NX_DAEMON=false/NX_NO_CLOUD=true. C was2.63GiB before heavy work; after build2,817,945,600bytes and final2,817,941,504bytes, above1.5GiB floor. No cleanup/install/restore/user-machineconfig inspection/credential/profile/certificate/provider/model/DB/key/schema/migration/feature-enable action occurred.

Read-only `Get-Volume` was denied by the environment; supported `DriveInfo.AvailableFreeSpace` provided the numeric disk check. The first ignored artifact-directory creation hit the sandbox boundary and succeeded through the already-authorized scoped tool escalation. No failed acceptance gate was bypassed or retried. A verifier-only draft standalone test path was corrected from actual `rg --files` before final map seal; its note is retained. One derived warning assertion initially saw0rather than3because ANSI codes split `[WARNING]`; raw stderr visibly retained all3. After inspecting actual bytes/channels, the owned parser strips VTcontrol codes and correctly records3stderrwarnings. The failed metadata assertion/diagnosis is retained in `warning-parser-diagnostic.json`; no build/test was repeated and no product fix was needed.

The runner initially serialized inherited process.env into local ignored `checks-results.json`. Before any content was read back or emitted, it was overwritten from separate raw command JSONL with exactly the six task-owned env values; no inherited values were displayed/copied/transmitted externally. Only local `checks-results.json` was affected; no unsafe copy/log/manifest was produced. First correction completed no later than2026-10-21T18:19:41.532Z; the exact correction timestamp bound and later audit time are in `runner-metadata-correction.json`. Execution CODE was reconstructed by undoing the sole serializer edit, with a post-correction snapshot hash; no observed pre-correction hash is invented. Safe final runner, raw argv/exit/results JSONL and metadata correction remain. `environment-field-audit.json` statically checks every owned generated JSON object for inherited environment fields and verifies safe receipt results equal all15raw JSONLrecords; only the explicit six-key env allowlist is present. Test argv/results are unchanged. This is a corrected verifier-metadata mistake, not a source/runtime workaround. No broad environment content is included in the final package.
