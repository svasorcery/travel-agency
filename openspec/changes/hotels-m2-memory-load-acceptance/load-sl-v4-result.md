# S/L v4 — one complete campaign, M2 acceptance failed

2026-11-01. После отдельного «Го» выполнен один frozen S/L v4 с acceptanceVersion2. **S прошёл23/23 обязательных checks, L —21/23. Весь пакет failed.** Оба профиля полностью измерены:6304/6304 terminal outcomes,722/722 samples. Повторов, продления окон, source/threshold изменений или новых снимков памяти не было.

В L не прошли workingSetRecovery и dueWorkAndInboxSettled. Пятисекундное сравнение сохранено отдельно по согласованной revision2: S=false/+12KiB, L=true/−437.6KiB; оно не заменяет mandatory memory/queue failures. Старые failed receipts остаются прежними.

## Выполнение и полнота

| Факт | S | L |
|---|---:|---:|
| Mandatory gates |23/23 passed|21/23 failed|
| Original / M2 additions |16/16 +7/7|14/16 +7/7|
| Planned / observed jobs |3152/3152|3152/3152|
| Response / Cancelled / NotDispatched |3138 /1 /13|3138 /1 /13|
| FailedBeforeResponse / NotRun / Unfinished |0/0/0|0/0/0|
| Samples |361/361|361/361|
| First / last sample, seconds |0.1015 /360.0486|0.4707 /360.0475|
| Maximum parent sample lateness |0.3355s|0.5303s|
| Profile elapsed with cleanup |367.671s|367.825s|
| Runtime failure / finalization errors |null /[]|null /[]|
| Evidence complete |true|true|

Campaign:11:20:14.533–11:33:55.265 UTC (14:20–14:33 Москва),820.730s; inner campaign814.206s, below1200s. Driver18212; S Host52396, L Host34484. Exit−532462766, forcedStop=false, cancelled=false. Negative exit reflects failed gates, not an executor exception during measurement. Exactly one attempt, no automatic retry.

All6304 outcomes =6276Response +2Cancelled +26NotDispatched. The26 unmet prerequisites remain in the denominator; no replacement purchases. No early dispatches, no lost terminal classifications. S completed cleanup before L started.

## Memory: absolute ceiling passed, L recovery failed

| Метрика | S | L |
|---|---:|---:|
| RSS baseline (last sample before30s) |425.602MiB|467.336MiB|
| Peak RSS |455.672MiB|665.313MiB|
| Final RSS |449.188MiB|658.043MiB|
| Final−baseline |23.586MiB|**190.707MiB**|
| Peak / final private |357.773 /350.898MiB|568.449 /560.906MiB|
| Peak / final estimated managed |169.586 /87.567MiB|360.445 /286.867MiB|
| Last-GC committed |199.207MiB|407.855MiB|
| Natural Gen2 count |67→68|67→82|
| Maximum cache payload |4.057MiB|15.994MiB|

L fails **both parts** of workingSetRecovery:
- Baseline490037248B; allowed final624254976B; observed final690008064B. Growth199970816B=190.707MiB, exceeding the128MiB allowance by65753088B=62.707MiB.
- Mean[300,330)=687679624.533B (30samples); mean[330,last]=692751459.097B (31samples, includes terminal360). Increase5071834.563B=4.837MiB, where non-growth is required.

For S, final−baseline24731648B is within128MiB and the latter recovery mean is lower by69565.935B. Both peak RSS values are below1073741824B. No epsilon, rounding before comparison, forcedGC or trimming. Offline reconstruction matches all runtime memory booleans; analysis-v1/memory-gate-replay.json.

The L final five-second diagnostic is true even though the mandatory longer recovery check fails. Its means690461900.8→690013798.4B decline448102.4B. S's diagnostic means470999859.2→471012147.2B grow12288B and remain false/nonblocking. Both report changed observed retained state; no full-state equality claim.

## What the saved memory data explains, and what remains unknown

L cache eviction is bounded: maximum16770577B<16777216B, final17entries/15605896B. The source fixtures retain considerably more data than S. Between29s and360s:
- Duffel retained rates1200→14600;
- Hotelbeds retained rates716→8626;
- cache payload7898803→15605896B;
- reservations38→38, retained inbox rows1→41.

Therefore post-warm and final states are not identical retained datasets. This is disclosed, not used to waive the frozen128MiB/non-growth requirements.

The last natural Gen2 occurs near343.011s. Its recorded heapSize330624424B includes fragmentedBytes126577784B; committedBytes427667456B. These are last-GC observations, not separate additive owners or current live-object sizes. Private/RSS are not obtained by adding cache, serialized fixture values and managed heap.

The source fix previously removed the large metadata allocation pattern; this S peak455.7MiB remains far below the pre-fix S-v2 peak1320.2MiB. L has now been measured for the first time. Its remaining growth cannot be causally assigned to a specific product owner or declared a leak from these counters. Growing fake retention and managed allocation/fragmentation are concrete candidates for a bounded follow-up source investigation.

## Remaining due work at the L boundary

L pendingInbox=0 throughout the final window. DueWork=0 through sample358.021s; sample359.015s has1due/28future, oldestDueLag233.252ms. At terminal360.048s the same counts are1due/28future, oldestDueLag1265.620ms; dueBodyBytes119, futureBodyBytes3300, missingRawBodies0. Thus the failure is a scheduled work item becoming due around358.782s, not an observed stuck inbox backlog.

All three final-phase purchases are authoritatively confirmed: due304.5 at305.024s, due324.5 at329.032s, due344.5 at345.018s; one effect each. Sources are inactive at the end. This does not prove the remaining work is harmless.

The source HotelObservationRunner schedules a bounded successor before completing a read and returns without another provider call for Confirmed/Cancelled rows. Such a successor is a plausible candidate for the late due item. **Its identity/type/target were not retained in this run's aggregate envelope measurements**, so exact attribution and whether it would be a no-op remain unknown. No observation after360s, extra database query or extended drain window is used to turn the gate green.

## Business and source behavior

Normal and final steady useful cohorts passed for both profiles: search20/20 and10/10; rates8/8 and4/4; prepare/create6/6 and3/3; status600/600 and300/300; callbacks4/4 and2/2. RecoveryAdmission purchases2/3 in both; later confirmations count only in eligible recovery windows. Useful service recovered within75s in both.

| HTTP completion p95 from due | S normal / steady | L normal / steady |
|---|---:|---:|
| Search |135.5 /144.2ms|283.1 /272.3ms|
| Rates |149.5 /123.8ms|185.6 /175.0ms|
| Prepare |242.0 /138.4ms|168.5 /202.1ms|
| Create |202.5 /147.4ms|212.2 /139.0ms|
| Status |17.7 /17.3ms|18.5 /17.6ms|
| Callback |37.3 /86.4ms|23.2 /16.0ms|

Burst status150/150 and callback30/30 remain useful in both; p95 status/ACK20.8/48.6ms inS and31.6/51.8ms inL. Burst search1200/profile gives only8 healthyS and7healthyL: HTTP200 limited outcomes and429 are not counted as useful searches. No statistical or external-provider SLA claim.

Final rows in each:25Confirmed,11Quoted,2Unknown. Lost reply, cancellation and Limited/Timeout recipes passed; no duplicate or unknown extra effects, no source overflow.

| Profile / source | Seed / timed HTTP starts | Create / read | Final rates / quotes / bookings | Serialized values |
|---|---:|---:|---:|---:|
| S / Duffel |40/113|6/11|1314/31/19|1268648B|
| S / Hotelbeds |16/108|7/6|820/0/8|70165B|
| L / Duffel |40/115|7/11|14600/32/20|13912967B|
| L / Hotelbeds |16/105|6/6|8626/0/7|700053B|

Actual event counts match cumulative starts; all final active counts0, overflowfalse, unknownEffects0, duplicateEffects0. Per-source foreground maxima24starts/1active; recovery maxima within6starts/1active. Some planned purchases/reads were not triggered because prerequisites failed; all offered jobs remain accounted. Every actually admitted known-reference outcome passed the corresponding read gate.

## Resources and cleanup

Both Host processes: .NET/ASP.NET10.0.9, serverGC=true,cachedProcessorCount8, affinity0x3. Driver peakRSS220221440B duringS and241209344B duringL. Owned DB peak cgroup memory191467520B/100126720B, reported separately from Docker VM.

Minimum available RAM14092009472B/13992292352B; minimum free disk13044969472B/13027905536B. Maximum profile evidence15382882B/15378209B. Frozen4GiB/5GiB/512MiB resource stops were not breached; no environment limit change.

Independent cleanup confirmed driver18212, Host52396/34484 and both owned containers/temp directories absent:
- S container1b375f9e9651118c388547708b521c2d7e900acfc73351eeb39fe507fb5569a3; temp travel-hotels-host-aa4723eb2084482c876a73775d92b627.
- L container2d55d73b1e3e3d4ea517b99afb47d9eedb28f23c3daf9a50932f3301fbe1df7b; temp travel-hotels-host-a9298452a285498ea242ba50c31fd2d7.

Both runtime cleanup receipts completed/cleaned/exited=true. No residual assets, broad cleanup/prune, WPR/ETW/VMMap/UI, forcedGC or retry.

## Provenance

Raw/evidence: TestResults/hotels-m2-memory-load-acceptance/load-sl-v4/. Post-run verify-only passed:2018source/input files,999build files and runtime selection unchanged.373prior raw files retain their hashes;52new raw files are pinned in analysis-v1/integrity.json. Analysis decision is explicitly accepted=false.

| Frozen artifact | SHA256 |
|---|---|
| source-manifest.json |9AAC4C79C7076DC7F094E0779E93ADDD21426CA976E56B40BDC97F35D58DB03C|
| freeze.json |B89FF0FD3836A4B689D1A2C99AE1B9B1216E383AE77281665B26B2397CB39F48|
| frozen-source-build.zip |F3E35C33120E420B334049B3136E98CA17963244A0717D8B3132B67F92EE0F70|

The frozen preparation record runtimeAuthorized=false is historical; this turn's separate execution-authorization.json records the owner's permission. Preparation metadata was not rewritten. The two source profiles remained acceptanceVersion2 throughout.

## Outcome and next step

The bounded campaign and its analysis are complete. **Full M2 load acceptance is not obtained:** S passed, L failed memory recovery and terminal due-work checks. Plan acceptance tasks3.2/3.3 remain open,6/8; this failed result report does not mark successful acceptance complete.

Next: inspect the L retained-state/allocation path and the semantics of the late scheduled work, using the saved evidence and source. Exact owners or work identity must be established before a product/fixture/method change. No new retry, lowered threshold, forced cleanup or automatic diagnostic run is proposed as a fix.

[H4 ownership evidence](diagnostic-v5-result.md), [H5 private category](host-private-diagnostic-v4-result.md), [H6 allocation attribution](native-heap-diagnostic-v2-result.md), [metadata lifetime fix](compiler-metadata-lifetime-verification.md) and [H6 partial comparison](native-heap-after-metadata-fix-result.md) retain their separate scope. M1failed16/17 and priorS failures are unchanged. Real suppliers, target/rollout, whole-system hard memory capacity and leak absence are not accepted. No stage/commit/push/sync/archive.
