# Trips M3 evaluator source/test receipt

Date2026-10-28. The opening sections retain the pre-publication source phase; current PR83 acceptance and the later run-packet preflight are recorded at the end. Approved source/test P1–P5, subsequently explicit wire projection
with strict local validation and shared runtime budget. Main milestone22/23,
real-model task6.3 pending. No real provider/model calls, credential/billing reads,
M3 enablement, target/demo/human-material changes, staging, commit or publication.

## Provenance

Fresh fetched origin/dev base9ff60e42bb553214a22ed71fb4fa44b9d846c3d2; new managed
worktree trips-m3-evaluator-preparation, branch codex/trips-m3-evaluator-preparation.
HEAD remains that base, index empty. Prior dirty planning worktree retained.
Source files are uncommitted, so no result commit is invented; full working-tree
review inventory and hashes are retained in ignored out/eval-prep on task-owned D.
Product spec13requirements/52scenarios, M1/CAS/catalogue/city packages and public
contracts are unchanged. No target migration authored/applied.

## Implemented

P1 strict manifest binds source/build/prompt/local-schema/normalizer/lexicon/package,
wire-policy, resource-policy, published-worker closure and immutable runtime image.
No model/prices default. Pure preflight makes no resources or key access and never
claims kernel readiness. Future execute requires a separately approved exact
manifest SHA, clean committed source and repeated artifact admission.

P2 durable authorization claim, immutable intent/result/index and explicit uncertain
crash state; no retry/resume. One monotonic clock/schedule at actual SDK starts
preserves original windows, >=11s spacing and original authorization expiry through
setup, normalization and journal I/O. Pipeline entries and SDK attempts stay distinct.
P3 timing covers normalization and final deadline, with explicit normalizer-added
issues and rejected/not-assessed proposal observations. Existing thresholds stay
unchanged; accepted UnsafeCases=0 is not a raw model-quality claim.

P4 runs worker, existing EF ledger/owned PG, normalization and journal writer inside
one Linux cgroup: CPU2, RAM2GiB, swap0. UID999, capdropALL, no-new-privileges,
read-only root/app, no published ports or Docker logs, no reaper. Host only performs
source admission/control and checks image/owner/bind sources. Runtime validates
actual cgroup/PG membership/tmpfs/file limits before the key handshake.

Writable aggregate256MiB = PG112 + socket4 + tmp12 + scratch32 + shm32 tmpfs
(192MiB) + one persistent64MiB archive. Its parent is read-only; kernel fsize64MiB
and fixed mmap limit growth. Archive payloads <=64KiB use flushed commit markers
and hashes. Normalized fictional results only: no raw prompt/input/completion/key/
exception bodies. The older rename-based file journal remains a mechanical fixture;
it is not added to the runtime budget. Evidence survives container removal.
Supervisor terminates orphan containers after6h, with <=5s kill grace. Host build,
controller and shared Docker daemon are outside this runtime data-plane quota.

## Explicit wire decision and proof

The first SDK12.20.0 fake-wire test found automatic removal of maxItems/$defs and
added descriptions. User separately accepted an explicit projection. Implemented
trips-anthropic-wire-v1 removes only maxItems, preserves compact defs/refs and uses
native MessageCreateParams to bypass automatic transformation. Full local schema
SHA remains93b2b1ac6f3d88ce385445bc5abd5187fbc2d0486221e53593a2be66e7c1342c.
Oversized issues/evidence/alternatives remain InvalidOutput locally.

Actual fake HTTP covers all48 complete SDK envelopes, projected schema, model,
temperature0/max1024/no tools/thinking/cache directives/retries and returned usage.
Earlier logical estimate3491 undercounted D01 wire4153; the complete sender-shaped
serialized envelope plus conservative allowance now bounds every case <=4096.
No remote provider/schema/tokenizer acceptance or measured model quality is implied.
Provider-injected tokens/actual usage still require separately authorized evaluation.

## Source checks and incidents

Logs in out/eval-prep retain RED and GREEN evidence. wire-envelope-green:9/9 SDK
tests; archive guard RED then contract mechanics49/49; envelope-quotas:2/2 real
Linux worker+PG source probes, fake SDK1/ledger1, full48 receipt reconstruction,
kernel archive-growth denial and actual scratch ENOSPC. Crash probe preserves D01
uncertain intent, no retry and cleanup of only its labelled container.

A fsize32MiB trial failed the runtime with Out of memory at cgroup peak136421376,
zero kernel OOM events. Raising only file/archive to64MiB and reducing shm64 to32
passed with the same total256MiB. A JIT backing-file limit is the inferred cause;
no native-call trace established it. This adjusts allocation, not the total cap.
No write/execute policy was weakened to bypass the runtime failure.

C filled during preparation; Docker stopped responding. After explicit user
permission, Docker Desktop was restarted. Its normal restart timed out; only
verified Desktop processes and docker-desktop WSL were stopped, then Desktop
started successfully. Only old evaluator image71ab8d70 and eight exact evaluator
cache layers were cleaned (about177MB cache plus metadata). Pinned base images,
foreign containers/volumes/cache and retained demo were not deleted. This does not
claim Windows recovered all VHD space. Pinned OpenSpec dependencies were backed up
to owned D and restored exactly. Earlier automatic approval rejected stopping a
hung docker CLI client as shared Desktop risk; that action was not retried before
the separate user authorization. No global prune, WSL shutdown or data relocation.

## Review and acceptance boundaries

Prior independent source review found4P2: authority through final flush, authority
anchor preceding inspection, shm omission and an expired next-window wait. Each
was fixed RED->GREEN; immutable summary always FinalizationPending/Passed=false.
Only returned PostFlushVerdict binds RunId/index SHA and rechecks final timing.
Earlier full Contract92, ordinary AI252 and architecture243 passed before this
wire/resource continuation. Those historical counts are not current final proof.

New independent whole-source review and final suite receipts are recorded below
when finished. No quality, provider expense, target or remote CI acceptance is
inferred. Main6.3 and later unseen holdout remain pending, with blank human A/B,
field calibration, original M2 rollout6.3 and Moscow v2 separate.

Unchanged baseline dependency-artifact check failed Storybook Angular ->
builder-webpack5 -> fork-ts-checker-webpack-plugin -> deepmerge,
GHSA-gcqx-w48m-w2h4. Root package/lock/policy unchanged; no allowlist/severity/version
relaxation. This separate failure prevents claiming every repository gate green.
Source mechanics can be reviewed independently; publication/CI need new authority.

Seven frozen files match actual fresh-base Git bytes: evaluation-plan, corpus,
prompt, validator, date normalizer, lexicon and leaf contract. Extractor estimate/
identity, chat wire adapter and evaluator are authorized source changes and need
new approval hashes. Corpus raw SHA c6152a8201fc24bc5a12704b59b58b3bcc3c56ec4e571bbffb046aedc7283bf9;
embedded LF prompt db7429e3ad0eeb0250dd55f40dbe4f28562bc26df4b9d613d64c11f8dc8b361d.
Old physical CRLF planning hashes are historical, not an execution binding.

Canonical checks from this worktree use --artifacts-path out/eval-prep/artifacts
and physical D TEMP/TMP. Never invoke the full paid AiEvals suite or real execute.

```text
dotnet test tests/Travel.Tests.Contract --artifacts-path out/eval-prep/artifacts
dotnet test tests/Travel.AI.Tests --artifacts-path out/eval-prep/artifacts --filter "Category!=AiEval&Category!=AiEvals"
dotnet test tests/Travel.Tests.Architecture --artifacts-path out/eval-prep/artifacts
dotnet run --project tools/trips/Travel.Trips.Evaluation --artifacts-path out/eval-prep/artifacts --no-build -- preflight --repo-root .
node tools/openspec/run.mjs validate trips-m3-brief-confirmation --strict
node tools/openspec/run.mjs validate --specs --strict
npm.cmd run check:dotnet-inventory
npm.cmd run check:ai-harness
npm.cmd run check:readme-examples
```

## Independent continuation review and fix pass

Fresh read-only whole-source review found5Important/P2 and no Critical/Minor.
All were accepted; none waived. Focused reproductions failed before the fix pass:

1. Changed header with retained approval hash: independent canonical typed-manifest
   SHA now travels in host admission alongside original raw-file SHA. Worker checks
   both plus source/kernel evidence and emits WORKER_AUTHORIZED. Controller waits
   for that second ACK and rechecks authority/source before resolving the key.
   Tests Changed_header_cannot_reuse_original_raw_approval_hash and
   Key_resolver_waits_for_validated_worker_ack failed first.
2. Empty/duplicate/unknown/E-prefixed case scope reached execution factories.
   Pure fixed48 qualification preflight now rejects it, before resource/key access.
   Invalid_case_scope_is_rejected_before_owned_factories failed first (factory1).
3. Independently deserialized ApprovedCaseIds arrays failed record reference equality.
   Archive finalization compares canonical binding values. Subset_binding_survives_
   independent_manifest_round_trips failed first and now reconstructs the index.
4. Nonzero Docker inspect was mistaken for absent. Cleanup now requires a successful
   exact-id container-list query proving absence; daemon/permission/query failure is
   cleanup-unproven. Both pre/post-disposal fault cases failed first; confirmed
   absence remains accepted. No unrelated id or resource is removed.
5. PG image default listener was reachable on bridge. Supervisor now sets
   listen_addresses=127.0.0.1. Worker reads actual PG setting over its Unix socket
   before readiness; the owned kernel probe failed before that supervisor fix.

Contract after all fixes98/98. Full ordinary AI/kernel suite and final architecture/
preflight receipts follow below when completed. No second reviewer was dispatched;
this is one independent review plus one tested fix pass, not author self-approval.

Reviewer declined four judgments; explicit rulings:

- Current final GREEN belongs to executor command evidence, not read-only review;
  retain exact final suite receipts. Cost if omitted: stale binaries could be accepted.
- Remote provider/schema/tokenizer/pricing/quality remain unassessed and unapproved;
  fake serialization is not proof. Cost if treated green: unknown quality/spend.
- Target/rollout and remote CI remain pending separate authority. Cost if confused:
  unapproved publication or real-user enablement.
- Fully compromised host/Docker daemon is outside this local trusted-operator
  design. Hashes/cgroup checks do not claim that threat protection. Cost if needed:
  separate hardened execution environment and review, not an inferred guarantee.

No deferred minors. Baseline dependency failure is retained, not waived.

## Evaluator receipts before the dependency extension

- Full Contract98/98 (review-fixes-contract-green.log).
- Full ordinary Travel.AI.Tests261/261,5m14s (review-fixes-ai-green.log).
  This includes the revised real worker+PG/loopback/quota/crash probes and fake SDK;
  no AiEval/AiEvals suite, real key or real provider was invoked.
- Actual standalone preflight exit0, ReadyForExecution=false, ProviderCalls=0,
  Clean=false, SelectionAndAuthorizationPending (final-preflight-current.json).
- Strict active change and all20 specs valid after the fix documentation.
  AI harness134, inventory64passed/1Windows symlink skip, README13 passed.
- CSharpier33files, Biome inventory and git diff --check passed.
- Eleven protected source/spec/dependency files matched raw base Git bytes;
  original milestone23lines unchanged at22checked, spec13/52;18local links resolve.
- No owned evaluator containers remain. Index empty; HEAD/base unchanged. No PR/CI
  dispatch or publication. Source file hashes are in final-source-manifest.json.

The5P2 fix pass is complete, no findings waived and no deferred minors. Runtime
wire/budget mechanics are proven only within the documented source/fake scope.
P4 combined GREEN/CI and P5 Handoff are deliberately not checked wholesale: the
unchanged dependency-artifact advisory remains failed. This is an independent
repository acceptance limitation, not permission to change dependency policy.

## Implementation rulings carried from the ledger

- Original tasks remain22/23; source preparation is a prerequisite, not model6.3
  acceptance. Wrong interpretation would authorize unmeasured model/rollout work.
- Seven frozen files stay byte-identical; authorized source helpers/runner/wire
  changes require fresh execution hashes. Wrong binding would approve stale code.
- Physical D TEMP/evidence roots retain anti-link guards; no symlink workaround.
  Wrong path interpretation could write outside the approved evidence root.
- Unknown SDK wire behavior was first blocked, then explicit projection accepted
  separately; full local validation stays strict. Wrong projection could reject
  provider requests or produce local InvalidOutput, never silent form defaults.
- Original windows and actual-start monotonic clock stay shared; H09 out-of-catalogue
  Paris tests rejection because Moscow is deterministically downgraded. Wrong
  attribution would falsely credit raw model safety or allow catch-up calls.
- Durable summary cannot know its own flush duration: store Pending/false and bind
  a separate post-flush verdict. Wrong treatment could accept a crashed/late run.
- Shared kernel envelope replaces independent per-PG/per-journal limits;64+192MiB
  allocation keeps total256 after the fsize32 JIT failure. Wrong allocation causes
  controlled failure/uncertain journal, not a larger authorized budget.
- Builds/controller/Docker daemon and compromised-host resistance are outside the
  runtime cap; model/tokenizer/provider/target/remoteCI proof remains separate.
  Wrong scope would overstate resource/security/quality acceptance.
- No commits while prohibited: retain ignored ledger and actual working-diff/new
  file hashes for review, not invented result commits. Wrong provenance could
  omit untracked files from review or publish unapproved source.
- Docker recovery used separate user authority and exact owned image/cache scope.
  No global prune/foreign deletion. Wrong ownership would affect other work.

Final architecture suite243/243 passed after the5P2 fix pass
(final-architecture-current.log). No source changes followed that suite; remaining
edits are receipts/status wording. Pinned OpenSpec validation and working-tree
inventory are rechecked after those documentary edits. Latest C measurement is18483253248bytes free (about17.2GiB); this is an observed
host state, not an attribution to our limited cleanup or a space guarantee.

## Dependency extension2026-10-28: baseline locally resolved

After the above receipt, the user authorized the proposed minimal dependency step.
[Security receipt](../../../docs/operations/2026-10-28-deepmerge-security-backport.md)
records reproduced poisoning, reviewed local revision1 backport, official SRI and
source/archive/lock/installed proof. Audit remains transparent at raw4root/0OpenSpec
entries. Both dependency gates,74patch tests,16audit unit tests,15consumers and
actual24s Storybook build are GREEN; fresh security review has no findings.
No npm framework versions changed: lock differs only at root and deepmerge.

P1–P5 source preparation is complete locally. Earlier failed-baseline statements
are historical, not a current blocker. Nine protected protocol/product/.NET input
files remain byte-identical; package.json/lock are now intentionally changed by
this separately authorized scope. All pre-existing evaluator .NET/test/CI files
match their prior final hashes; documentation alone records the new status.
Main task6.3 stays unchecked. No publication/remoteCI, model/provider/price/cost,
private-text/target/rollout permission was inferred, and no saved demo changed.

Publication follow-up2026-10-28: user authorized meaningful commits, push and PR to
dev plus waiting for ordinary CI. Base origin/dev was refreshed and still equals
9ff60e42bb553214a22ed71fb4fa44b9d846c3d2. No merge/model/M3/target permission follows.
Local statements above retain source-phase provenance; actual PR/CI are reported
separately once observed, without inventing a result SHA or terminal CI receipt.

Pre-push clean-HEAD smoke found a genuine build-identity gap: the Web SDK emits
Travel.AI.staticwebassets.endpoints.json, so clean Git still reported Clean=false.
The exact top-level generated metadata is now hashed with the execution build
closure. Unknown JSON and nested lookalikes remain rejected. A new RED regression
was followed by focused environment2/2 GREEN and the full ordinary AI suite.
This adds a small corrective commit before any push, rather than weakening source
admission or omitting the metadata from provenance. No model call or M3 enablement.

Final ordinary AI regression run after the build-metadata fix:262/262 passed in4m37s. Contract98 and architecture243 source boundaries are unchanged; original frozen corpus/prompt/schema/validator are preserved.

## PR83 acceptance and subsequent no-model run preparation

PR83 is merged as d318cb49b175fe0fe5a19ed6fc1161b4e65bf20f. Final PR CI40677897457 completed/success with20 applicable jobs; post-merge push CI40684645709 completed/success with19. Exact base/head/parents, retained earlier CI failure and expected skips are in [publication](publication.md#pr82-documentary-closure-and-pr83-evaluator-preparation). The earlier no-publication statements above describe their historical stages, not current blockers.

The subsequent authorized packet work starts clean at that freshly fetched merge in a new managed worktree trips-m3-run-packet, branch codex/trips-m3-run-packet. Release evaluator build passed with zero warnings/errors. Before documentary edits, pure preflight and strict draft preflight both exited0 with Clean=true, ProviderCalls=0 and ReadyForExecution=false. Draft SHA256: af2ad8a35d9c28179ebc1f0032a69c83a5e59906d39f87aafcc8901eb8df98a7. [Current packet and full fingerprints](model-evaluation-options.md#11-пакет-первого-запуска-подготовка-без-исполнения) distinguish this accepted-source observation from any future final committed execution source.

Source-only worker preparation failed with exit2 after publish because dockerDesktopLinuxEngine was unavailable. During that first attempt no restart, fallback, key read, execute, evaluation container, model call, target or demo work followed. Worker/image binding was pending at that point; the successful source-only retry is recorded below, while runtime readiness remains unverified by preflight. This does not reopen or relabel the historical PR83 shared-budget fake/kernel proofs. The completed review packet contains deliberately denied draft metadata, not an executable or authorized model run. Public provider terms/prices/account access were not newly verified in this preparation. Main task6.3 remains not run; real quality, private text and rollout are pending.

## Run-packet documentation review and local validation

Independent read-only reviewer found one P2 in the proposed failure row: a known-cost per-case InvalidOutput/refusal/normalizer rejection/component deadline can continue the run. The row now distinguishes these failed-case outcomes from run-level authorization/binding/accounting/budget/journal/worker/overall-deadline stops; the same reviewer rechecked the correction with no important findings remaining. Product code and all acceptance thresholds are unchanged.

Pinned strict validation passed the active change and all20 current specs (0 failures, existing long-requirement informational hints retained). Dependency-free AI harness passed134/134 plus instruction validation. Mechanical packet checks passed12 protected raw files, all23 original task checkbox lines (22 checked/6.3 unchecked),13 requirements/52 scenarios,100 local Markdown file links, exact CI job counts and the denied draft SHA. The sole initial Biome discrepancy was mergeParents line wrapping in the sanitized CI receipt; formatting fixed it without changing any identity or the bound draft bytes. JSON/whitespace and final scope checks follow the documentary edits.

The final no-model working-copy preflight is retained separately in verification/run-preparation/preflight-working-documents.json: exit0, Clean=false, ReadyForExecution=false, ProviderCalls=0 and ArtifactsDirtyOrChanged, in addition to the pending selection/authorization/worker/evidence reasons. Documentary edits intentionally invalidate current execution admission; future committed source and worker must be rebound. No stage/commit/push/PR/merge, model call, key/billing inspection or M3 activation occurred in this preparation.

Final documentary checks passed: strict active-change validation, Biome6/6JSON, git diff --check and mechanical scope/identity verification (11Markdown files,6new JSON receipts,12protected files,101local links, empty index). No product/runtime/configuration, corpus, main/delta spec, package, retained demo or human-review bytes changed.

## Source-only retry after Docker recovery

The user explicitly requested another attempt. A new clean managed verification worktree trips-m3-worker-binding/branch codex/trips-m3-worker-binding pins the same accepted d318cb49; uncommitted documentation remains separately in trips-m3-run-packet. Initial pure preflight reproduced Clean=true and all previous non-worker fingerprints, ProviderCalls=0. No completed branch was repurposed or dirty source presented as clean.

Desktop was off; first launch stalled on the docker-desktop init API. The previously authorized recovery scope allowed one Docker Desktop restart: graceful20s stop timed out, native force-stop succeeded, only docker-desktop WSL was terminated, Desktop restarted hidden, engine29.8.2 answered. No global WSL shutdown, cleanup/deletion or Ubuntu action. No retained demo or target was started by us; engine restart is distinct from an uptime claim about other workloads.

The requested prepare-source retry exited0. WorkerClosure=b0156436e89404847c682f5dda4e3f733aab73d128a657d3975f86156641f0ef; WorkerImage=sha256:b2096d42c003488fa855acd748f851a6d700c7e6e02c54a834d8efcb760fb91d; SourceKey=60f3764fec65bc5d87ebe0d954ad8b807e9d91b3d79c019b320659e1af357aef. [Current controlled receipts and denied draft](model-evaluation-options.md#12-повтор-source-only-подготовки-worker) retain the initial failure and initial draft unchanged. Post-preparation pure and draft preflight both have Clean=true, ProviderCalls=0, ReadyForExecution=false. All non-worker artifact identities match the initial clean observation; the new exact draft SHA is 72f1ce72117938710aa1bbb55b3433be64ac541a07a689b5a901a1b0a226d782. Only BoundWorkerPreparationRequired was removed from the old7reasons; the remaining6authority/selection/pricing/expiry/evidence reasons block execution as intended.

No evaluation worker/PG, SDK fake/kernel probe, model request, credential/billing read, feature flag or product source change occurred in this retry. A published worker/image closes source-only preparation for this pinned source, not the future runtime handshake, model quality, task6.3, private-text, holdout, target or rollout. At that source-only retry stage commit/push were unauthorized and absent; later documentary publication has separate explicit authority.

Scoped independent read-only review accepted section12, the updated statuses and all4new JSON with no important findings. The reviewer confirmed the clean pinned source and both draft hashes; no runtime or model was run by the reviewer. Final checks passed: pinned strict active change and20current specs, Biome10JSON,107local file links,12protected raw inputs and all23original task lines unchanged (22checked/6.3unchecked). The worker source branch remains clean; document branch index is empty. No code, dependency, flag, model/cost selection, demo or human materials changed.

## User declined paid API evaluation

On2026-10-28 the user confirmed they do not want model calls billed outside the ChatGPT/Codex subscription and asked to skip this step. The direct Anthropic API evaluation is deferred, external model API budget USD0. Qualification48/holdout remain not run;6.3 stays unchecked/22of23. Source-only worker preparation remains accepted and denied draft bytes/hashes are preserved. This decision grants no alternate API/local-model/subscription execution, quality waiver, M3 enablement, private-text, target or rollout permission. Only public OpenAI/Anthropic pricing documentation was checked; no credentials/account billing or model API was accessed. No commit/push occurred.

## Documentary self-review before the separately authorized PR

On2026-10-28 the user separately authorized self-review/fixes, commit, push, a PR into dev and waiting for ordinary CI. Merge, real-model calls and M3 enablement remain prohibited; external model API budget stays USD0. Freshly fetched origin/dev still equals d318cb49b175fe0fe5a19ed6fc1161b4e65bf20f and the current unfinished documentary branch descends from it. No completed branch was reused.

Author self-review corrected three documentation hazards: historical no-publication statements could be read as current authority; old provider-choice instructions could look like the next step after defer F/USD0; and clean d318cb49 worker receipts could be mistaken for proof of the later documentation commit. Historical stage boundaries and conditional future instructions are now explicit. The Trips README carries the current USD0/deferred status; the instruction summaries separate current decisions from source acceptance. Input-token ceilings are identified as policy/source estimates without claiming actual provider tokenization.

No product code/configuration, dependencies, corpus/oracles/prompts, thresholds, task checkbox lines, main/delta specs, city packages, saved demo or human-review material is changed. Both denied draft files and all10sanitized JSON receipts retain their existing bytes. Source/runtime preflight, Docker and model execution are not repeated for this documentary publication. Local documentary checks and this PR's own terminal CI are recorded separately from the historical PR83/source-only receipts.

Fresh pre-publication local verification passed: pinned strict active change and20current specs (0 failures), dependency-free AI harness134/134 plus instruction validation, repository Biome538files with0errors (existing warning/info diagnostics retained), git diff --check, and mechanical scope/identity checks for12Markdown files,10new sanitized JSON receipts,12protected raw inputs and131local file links. Main23task lines remain byte-identical at22checked/6.3unchecked; current/delta contract remains13requirements/52scenarios. Both denied draft SHA values are unchanged. No local model/evaluator execution, Docker action, credential/billing access or feature enablement occurred during this self-review.
