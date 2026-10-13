# S2 source and ordinary CI acceptance — 2026-10-13

S2 source acceptance is complete:23/23tasks. [PR49](https://github.com/svasorcery/travel-agency/pull/49) head38dcf6627cf3fbf0e8d711049c625ef79c315297 passed all15ordinary jobs including mandatory PR E2E in [CI38420042325](https://github.com/svasorcery/travel-agency/actions/runs/38420042325). Merge b24c40bebc01e410c4c6464dce514f87666ac2be passed all14ordinary jobs in [postmerge38422734878](https://github.com/svasorcery/travel-agency/actions/runs/38422734878); paid AI evals/postmergeE2E are skipped by existing policy. CI and E2E were not disabled or bypassed.

Actual acceptance closes authoritative Marten source reads/lifecycle/time/no effects (task1.3), operator HTTP/Wolverine route/auth/body/no-store/typed contracts (4.1), real Host-generated OpenAPI and module architecture (5.1), and review/exact-head CI/PR E2E/postmerge (5.3). The initial hand-derived snapshot mismatch and its actual-CI reconciliation are retained below; full corrected-head checks passed.

Closeout branch codex/support-s2-closeout starts from freshly fetched exact merge b24c40b, not an old feature HEAD. Main capability sync/archive records this accepted source contract. It does not enable Laya, provision a target/keys/schema/operator grants, permit real private text, install weights or establish model quality. The next runtime/synthetic smoke and S3 RU/EN baseline/calibration/holdout steps remain in [evaluation](evaluation.md). Earlier sections below are historical checkpoints.

---

## Source implementation checkpoint — 2026-10-13

User «го» authorized source apply. Current branch codex/support-s2-laya-classification retains fresh base7fd26fecfd996751c22b7b370a4fcbf6d085b9f6; primarydev separate. S2 source is implemented; publication/ordinary exact-head CI/PR E2E and target/model acceptance are not established by the following proof.

| Executed source check | Result / boundary |
| --- | --- |
| Support Core/Application |96/96; pure store/protector/provider/order doubles, privacy/revision/admission/metrics, drift and final-clock/decryption races |
| Laya protocol |68/68; actual ACL with fake HTTP only, no model/network/key/DB fixtures |
| Contract |41/41; authoritative reader doubles, source ownership/lifecycle/time/no effects |
| Direct HTTP |13/13 in own audited assembly; real guard/DTO/directendpoint without Host/TestServer/issuer/DB/keys |
| Architecture |183/183; source-only assembly,24module assemblies and permitted leaf consumers |
| Shared TS client |229/229 |
| Angular web |512/512; actual component/state generation/manualdraft review regression |
| Demo Node |75/75; Support standaloneDisabled handler leaves records/receipts unchanged |
| Support browser |19/19; isolated fixture Node/Angular servers, no Host/AppHost/Laya/DB |
| Release solution build |0errors;27pre-existing Flights/Host AD0001 analyzer warnings, no suppression |
| Production web build |Passed |
| CSharpier |31changedC# files checked;3deep-review files formatted |
| Biome |275files checked,0errors; existing53warnings/800infos retained |
| Harness |133/133; original inventory/pinned tooling unchanged |

Independent backend/UI source reviews each found one Important race. Provider shared retirement/dispatch/disclosure and Application final profile check closed the concurrency finding; retained manual review now binds case/revision and re-arms on newer R. Controlled RED→GREEN tests passed; both reviewers re-read fixes and report0openCritical/Important. Actual source checks above were orchestrated by parent; reviewers did not execute runtime.

The first source-derived OpenAPI expectation added1path/3SupportDTOschemas. Actual CI later generated1path/3SupportDTOschemas plus the generic ProblemDetails schema and one endpoint tag; all prior paths/schemas were semantically preserved. The actual document was reconciled after the first snapshot mismatch as recorded below. Corrected-head output comparison and full publication acceptance still require ordinary CI.

Pending task gates:1.3 actualMarten integration;4.1 actual route/pipeline checks;5.1 actualHost-generatedOpenAPI receipt;5.3 publication/exactheadCI/PR E2E/postmerge. Remaining source tasks are complete. Installed/pinned Laya weights/image, target protection/schema/operator grants, trusted runtime/perimeter/private-text approval, quality/calibration and automatic routing remain separate. There is no source/runtime/quality shortcut.

[Operational handoff](../../../../docs/operations/support-classification.md), [request/outcome examples](contract-examples.md) and [evaluation](evaluation.md) are reviewable; feature default remains false. Logs in ignored worktree workspace plus owned TEMP/support-s2-*.log preserve local proof until delivery/cleanup.


---

# Verification — planned acceptance

## Current checkpoint

Planning-only on fresh branch codex/support-s2-laya-classification, base7fd26fecfd996751c22b7b370a4fcbf6d085b9f6; primarydev remains separate. User chose operator-button recommendations. Source tasks0completed; no product/runtime/model/keys/DB/grants/migrations run or changed here. Pinned OpenSpec1.14.0 copied as ignored runtime only; manifests/lock/skills unchanged. Self/independent review and strict validation receipts are recorded in [review](review.md).

S1 closing receipt: [PR48](https://github.com/svasorcery/travel-agency/pull/48), headb3193b240c37568b682821d019706127331eaa61, exact-head [CI38368001752](https://github.com/svasorcery/travel-agency/actions/runs/38368001752) all15ordinary SUCCESS incl PR E2E; merge7fd26fecfd996751c22b7b370a4fcbf6d085b9f6, [postmerge38369467073](https://github.com/svasorcery/travel-agency/actions/runs/38369467073) all14ordinary SUCCESS, AIeval/postmergeE2E policy-skipped. S1 sync/archive complete; these receipts do not accept S2.

## Scenario-to-evidence map

Каждый scenario в [spec](specs/support-classification-suggestions/spec.md) принадлежит одной строке ниже; coverage целая по requirement, исполнение ещё pending.

| Requirement / scenarios | Planned verification | Task |
| --- | --- | --- |
| Explicit operator request only | disabled/ordinary-operation zero-call spies,1per explicit click |2.1/4.3/4.5 |
| Authority and retention before private processing | operator/wrongclient/customer/expiry/resolve tests, decrypt/inference spies |2.2/2.3/4.1 |
| Complete bounded private input | first/sentinel/malformed/wrong binding/unicode/overflow/instructions/no diagnostics |2.2/3.1/4.1 |
| Verified serving identity | fullhealth wrong/missing revision/device/config/profile, scoped-fallback counter and0candidate |3.1/3.2 |
| Strict bounded typed output | category→queue, key/discriminator/probability/truncated/size/error fixtures |1.1/3.2/3.3/4.2 |
| Confidence and abstention are explicit | passed/absent/unevaluated/abstained/threshold mismatch guards |3.3/4.2 |
| Case revision and suggestion lifetime | reply/resolve/triage races, TTL/new generation and prefill invalidation |2.3/4.3/4.4 |
| Bounded computation without automatic replay | budget limits/no queues/cancellation/timeout/transport unknown/0retry |2.4/3.4/4.3 |
| Application rules for urgency | owner-source Held/active/closed/time/UTC24h/nextseg/unlinked/missing/past/high/unknown/lifecycle-barriers/expiredHeld |1.1/1.3/2.3 |
| Explicit manual acceptance and correction | suggestion no-write, separate accept/correction,actorOperator/CAS/receipts/unknownsave |4.4/4.5 |
| Private browser and diagnostic lifetime | no-store/logging markers/token leakage, logout/permission/client/route/late callback/reload |2.5/3.1/4.1/4.3/4.5 |
| Classification has no booking authority | forbidden call spies, untouched financial/consent/unknown barriers |1.2/2.3/4.5 |
| Separate source, runtime and quality acceptance | artifact/CI exacthead/PR E2E record and separate runtime/S3 approval |5.1–5.4 |

## Execution layers

- Pure Core/Application and Contract/Architecture tests: only after fixture/static/startup audit; .NET sequential. Fake store/protector/HTTP tests prove deterministic contracts, not actual protection/issuer/model.
- Existing actual Support DataProtection/PostgreSQL, authoritative Flights stream, Host HTTP/OpenAPI/Keycloak and full AppHost suites: disposable ordinary CI; no local Host/DB/PFX/bootstrap merely because a filter looks narrow.
- Shared TS/Angular and Support fixture browser suite: strict client lifetime/UX proof on demo data. Mandatory PR E2E against ordinary CI Host remains separate and must pass before accepted publication.
- Real Laya installed/checkpoint/network/compute/privacy and measured RU/EN quality: **not run** by source/ordinary CI; [evaluation](evaluation.md) describes separate authorized evidence.
- Format/harness/strict OpenSpec and corpus links: planning checks now; future source required checks named in repository AGENTS. No redundant broad tests for docs-only edits.

Acceptance cannot be declared from file-existence status: all future task boxes stay unchecked until their actual source/CI receipts exist. No automatic routing/time-saving claim is supplied by this plan.
## Deep-review final execution receipt — 2026-10-13

After the two reproduced Application corrections and the added actual decryption-revision boundary, parent executed these checks against the current uncommitted source:

| Check | Actual result |
| --- | --- |
| Release solution build |0errors,27unchanged pre-existing AD0001warnings |
| Release Support Unit |96/96 |
| Release Laya protocol (only audited fake HTTP class) |68/68 |
| Release Contract |41/41 |
| Release Architecture |183/183 |
| Direct HTTP own audited source assembly |13/13 |
| CSharpier changed C# inventory |31files checked;3deep-review files formatted |
| Dependency-free AI harness |133/133;9instructionpairs/8skills/5agentpairs/6pinned effective OpenSpec skills |
| Strict OpenSpec corpus |7/7;S2 change0issues |
| Metadata-derived OpenAPI delta audit |All36existing paths and79schemas deeply unchanged; exactly1newpath/3newDTOschemas |
| Text/corpus local-link audit |62changed/untracked source files strictUTF-8,49relative links resolve |
| Git boundary |Primarydev clean;worktree descends from requested7fd26fe;staging empty;no S2publication |

Ignored proof logs: .superpowers/sdd/support-s2-laya-classification/deep-review-{build-final,support-release,protocol-release,contract-release,architecture-release,http-final,harness-final,openspec-final}.log; failing-before-fix deep-review-red.log and retirement-green.log retained. The read-only Node→git audit initially hit sandbox EPERM; the identical audit passed outside that sandbox, without a source/tooling workaround.

The unchanged frontend source retains executed APIclient229/229,Angular512/512,DemoNode75/75,Supportfixturebrowser19/19 and production build receipts above; these suites were not needlessly rerun for Application-only fixes. Actual Host-generated OpenAPI/issuer/Marten/PostgreSQL,ordinary exact-head CI and mandatory PR E2E remain pending publication gates. Model/target/perimeter/private-text/quality acceptance remains separate and classification stays default-off.

## Publication authorization — 2026-10-13

After the requested deep review, the user explicitly authorized commit, push and merge (with PR/ordinary CI/E2E from the agreed sequence). Publication scope includes review fixes and normal exact-head CI; it does not enable Laya, run paid AI evals or authorize target deployment/private-text/model preparation. Fresh fetched origin/dev remains7fd26fecfd996751c22b7b370a4fcbf6d085b9f6, branch ancestry verified and primarydev clean. CI workflow is enabled for dev/master PRs/pushes; actual PR E2E is mandatory before merge. Earlier no-publication statements above describe their prior checkpoints.

## Pre-PR publication checkpoint

The first push was stopped by the existing pre-push hook because it requires a Debug architecture DLL while local proof used Release. Debug build then exhausted C: capacity. Parent stopped only its verified own build PID, removed53ignored generated bin/Release directories strictly inside this managed worktree (about1GiB), retained source/dependencies/logs, built Debug successfully and completed the unchanged hook183/183. Normal push succeeded; no hook bypass/force/runtime launch occurred.

Before opening the PR, source review corrected the new expected OpenAPI operation name: pinned Wolverine6.17.0 NuGet repositorySHA cab8c37dd8a1bb88c81cf2190109b907d9641163 HttpChain.Codegen.cs determineFileName replaces hyphens with underscores. New operation summary/description/id are therefore POST_api_support_operator_cases_caseId_classification_suggestion; route stays classification-suggestion. This is a metadata expectation correction, not actual Host-generation proof; ordinary CI remains authoritative.

## First exact-head CI and actual OpenAPI reconciliation

[CI38418055958](https://github.com/svasorcery/travel-agency/actions/runs/38418055958) ran PR49 head0c88427a22b3c30188e412834e719079f7ca6f25. Thirteen ordinary jobs passed; Host integration had exactly1failed/67passed because the manually derived OpenAPI expectation omitted the new429application/problem+json MIME, generic ProblemDetails schema and endpoint tag. Mandatory E2E was therefore skipped by its existing dependency policy; this run does not accept publication.

The existing Host test emits only normalized public OpenAPI on failure. Parent extracted its existing marker from actualjob115368533962, strict-decoded128268bytes, SHA256525ce8bd2ebfdad5bd4e4831d745839388b0170338d705e078187f6872bfbed5. All36prior paths and79prior schemas are deeply equal to the requested base, existing tags are an unchanged prefix, the new3SupportDTOfields are exact, and all136schema refs resolve. Actual delta is1path,3SupportDTOschemas plus ProblemDetails, and1tag. Expected snapshot now uses the actual generated normalized document, including its serializer escaping/key order; no test/filter/tolerance was weakened and Host was not run locally. Full ordinary CI/mandatory E2E on the corrected head remain required.

## Sync/archive closeout review

Main support-classification-suggestions preserves the accepted delta Purpose and all13requirements/29scenarios, with only main title/Requirements headings normalized. All23tasks were confirmed by CLI inventory before moving the change to2026-10-13-support-s2-laya-classification; .openspec.yaml and all11files retained. Existing main specs are byte/text-unchanged. Twenty-six relative references were rebased;62local links resolve. Strict corpus7/7 with newS2zeroissues, dependency-free harness133/133 and unchanged9instruction/8skill/5agent/6pinned inventory passed; gitdiffcheck clean.

Fresh read-only independent closeout reviewer/root/s2_closeout_review found0Critical/Important and one Minor stale module sentence, which was corrected to distinguish manualS1/default-offS2/later real-modelS3 gates and re-read closed. Final verdict0openCritical/Important/Minor. Rollback text now explicitly requires Host restart because IOptions/profile are immutable for the singleton lifetime. Review did not execute tests/runtime or mutate files. Real model/target/private-text quality remain explicitly outside accepted source scope; parent owns live CI proof.

Closeout publication changes only references, accepted task/CI receipts, main capability and archived planning history. Product code, OpenAPI baseline, tooling/pins/skills/CI policy and target settings remain at the accepted PR49 merge.
