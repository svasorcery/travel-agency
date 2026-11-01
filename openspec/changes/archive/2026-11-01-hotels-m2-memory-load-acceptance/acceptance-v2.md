# Future M2 acceptance revision 2

2026-11-01. Owner accepted the proposal to retain the final five-second RSS comparison as diagnostic evidence rather than a blocking gate. This is an explicit future acceptance change, not a product fix or reinterpretation of historical results.

The frozen load-plan.md remains the version-1 workload/reference artifact. For new acceptanceVersion2 packets this document supersedes only its additional finalSameRetainedRowsMemoryStable blocking requirement. Version1 and M1 retain that gate. The new S/L execution packet is load-sl-v4; fixture/data/catalog revision stays1.

Mandatory memory checks remain exact: Host RSS<=1073741824 B; final RSS<=last sample before30s+134217728 B; mean[330,last]<=mean[300,330). Cache<=128 entries/16777216 B, source/queue/business constraints,3152 jobs and361 samples/profile, two360s profiles, all safety/deadline/cleanup/stop-on-failure rules remain unchanged. No epsilon, window changes, forcedGC, additional idle phase or fixture clearing.

Revision2 has23 mandatory gates:16 original plus7 M2 additions. The seventeenth original result is separately retained as a diagnostic with blocking=false, its exact legacy boolean, sample counts, means, delta and row/cache observations. False must remain false. Equal row/cache counts do not establish equal complete retained state; explicit comparability status and raw source/memory artifacts preserve that limitation.

Version2 is explicit in both profiles and receipt/campaign metadata. S and L must use the same version. Versions outside1/2 are rejected. No old packet silently adopts revision2. Source/build hashes and archive stay pinned; previous raw receipts and freezes remain unchanged.

Implementation:
1. Keep the M1/shared legacy evaluator unchanged. Add an M2 classification step after calculation, moving only the named legacy result out of mandatory gates for version2.
2. Preserve diagnostic evidence in the receipt; missing windows/evidence cannot produce accepted execution.
3. Prove one-page final RSS growth becomes diagnostic only, while peak ceiling, baseline tolerance, longer recovery means, business/source failures and incomplete evidence still fail. Cover version1, unknown versions and mixed campaign versions.
4. Run bounded standalone tests and actual pure evaluator, replay historical M1, then one independent review. Freeze a new S/L packet without Host/DB/load.

Current M1failed16/17, S-v2failed22/24, S-v3failed23/24 and Lnot-run remain historical outcomes. M2 acceptance still requires a separately authorized fresh full S/L execution. No runtime, stage/commit/push, supplier/model call or rollout is authorized by this source revision.
