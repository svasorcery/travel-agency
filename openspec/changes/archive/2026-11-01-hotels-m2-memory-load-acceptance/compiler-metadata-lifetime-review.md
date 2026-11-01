# Compiler metadata lifetime — independent source review

2026-10-31. One fresh read-only gpt-6-astra/high review under executing-plans. Scope: two new Host compiler/composition files, Program registration, test friend, two test files and the plan. Reviewer did not edit, execute tests/builds/services/tracing/UI or delegate.

Verdict: **0 Critical / 0 Important / 1 Minor**. No source blocker. No fix pass or re-review was required.

Confirmed in source: each Emit owns independent metadata, records ownership before GetReference, disposes on reference/parse/compile/load failures, and attaches generated types after metadata disposal. Actual ModuleMetadata tests execute generated code after disposal. Upstream naming/reference inputs/noncollectible load-context behavior are substantially preserved. Host registration follows Wolverine; saved DynamicTypeLoader resolves the existing IAssemblyGenerator through DI. Runtime claims remain separate.

## Deferred minor

Concurrent_calls_have_separate_disposable_metadata uses two Task.Run calls without a bounded overlap barrier. A scheduler may run them serially, so the test is weaker than a guaranteed-overlap regression. Present scopes are structurally independent; no production race was found. Future follow-up: synchronize both first metadata-open operations before either proceeds. Per the one-review workflow, this minor was recorded, not folded into an unrelated extra fix pass.

## All declined-to-judge items and executor rulings

1. Actual RSS/private/native reduction, CPU/I/O cost, startup latency and repeated-load stability: not measured; source ownership fix does not waive runtime acceptance.
2. Exact H6 anonymous managed callers and causal identity of the two groups: still unknown; source mechanism is confirmed, complete trace-to-method attribution is not.
3. Whole-Host startup, real Marten/Wolverine workflows, endpoint materialization and DB behavior: builder-only DI and generated-type tests do not claim full service acceptance.
4. Every upstream path bypassing DI: documented DynamicTypeLoader consumer is covered; exhaustive binary call-graph audit not performed.
5. Single-file/hint-directory failures, dynamic/locationless assemblies, missing platform satellites, alternate load contexts, multi-module and platform-specific filesystem behavior: source compatibility considered, these deployments not exercised.
6. Injected load/attach failures and metadata-cleanup exceptions: ordering reviewed, exact fault injection absent; no concrete ownership defect found.
7. Concurrent mutation of the same GeneratedAssembly, global HintPaths, naming/reference configuration: no new transactional concurrency guarantee is introduced for caller-owned objects.
8. AOT/trimming/collectible unloading: upstream warning annotations and noncollectible lifetime preserved; these modes not introduced or accepted.
9. Tests/build/format/CI: reviewer did not run them; executor receipts provide local checks, CI is not claimed.
10. Historical hashes/upstream provenance: executor verifies separately; reviewer did not independently rehash/authenticate them.
11. Unrelated inherited changes, supplier/model/S-L/target/deployment/rollout: outside this six-file source slice.

Executor retains all these limits. Cost: source-ready does not guarantee a measured memory win or every deployment/input combination. The remaining concrete runtime question is the effect on the same retained-state memory measurement, under a later prepared run.