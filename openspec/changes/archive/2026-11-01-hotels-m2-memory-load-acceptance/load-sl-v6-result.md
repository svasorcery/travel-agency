# S/L v6 result — strict RSS mean comparison is the sole failure

2026-11-01. One version4 campaign: **S22/23,Lnot-run** under stop-on-failure. No retry, source/threshold change, new dump or forcedGC. FullS3152outcomes/361samples; no executor/finalization error. Runtime460.690s,driver53496,exit−532462766,forcedfalse. UTC13:23:29.752–13:31:10.443 (16:23–16:31 Moscow).

## Outcome

All business/source/cache/queue gates, absoluteRSS1GiB, postwarm+128MiB and both legacy/new useful-recovery interpretations passed. New-chain recovery reachedend308 (68s afterburst); legacyrecovery also passed in this run. Independent replay matched all16windows, memberships/raw/carry-in counts and361queue samples.

Only workingSetRecovery=false because its strict mean comparison increased by **665.049462B**:
- [300,330):30samples,sum14941835264B,mean498061175.46666664B.
- [330,last]:31samples,sum15439917056B,mean498061840.516129B.
- Exact cross-multiplied difference618496B; dividing by930 gives665.049462B. This is not a floating-point sign error.
- PeakRSS480.941MiB; final−baseline49.8008MiB, below128MiB.
- Final5s diagnostic independently decreased1889894.4B and istrue.

The literal zero-growth criterion therefore failed. The result is not rounded, waived or reclassified; a positive average difference, however small, remains false underversion4.

## Verified runtime boundaries

Independent cleanup: driver53496/Host48068 absent, ownedcontainered8604d18b3d3d48429aaa203565cea1896e5e2e9b3ed89752b9092ef9fdf226 absent, ownedtemptravel-hotels-host-c3849c080ea34701bdb9e71964d5a4a0 absent. L created no assets.

Source2027/input/build1000 and runtime selection verified afterrun.455historicalraw files unchanged;30newraw retained in load-sl-v6/analysis-v1/integrity.json. New queue observed maxraw2,maxrequired1,maxredundant1,unknown0, allcomplete;terminalallzero. No newservice acceptance follows from source tests alone.

Freeze53433801A8E09E8F6919055776680E2D30AAD865053EE96D1491C6CB7F5733BF.
Archive57BE09D0424E4D69A367B96817B2090B6626E9B3817F03A6146B8A6A7B32B46E.
All16canonicaldata unchanged; frozenoriginmaps3152/profile independently matched schedules beforeexecution. Source406standalone/128evaluator+3rejected-artifact checks;review0Critical/0Important/1deferredtest-coverageMinor.

## Interpretation and concrete remaining decision

This observation does not establish a product leak. Working set depends on page residency while the load and retained state continue changing; a zero-tolerance mean sign is a literal acceptance constraint, not an attribution of memory owners.

Historical comparison, descriptive only:
| Packet/profile | PeakRSSMiB | Final−baselineMiB | Latter-minus-first recovery mean,B |
|---|---:|---:|---:|
| v2S beforemetadatafix |1320.23|52.285|−21973.06|
| v3S |453.49|42.047|−55507.41|
| v4S |455.67|23.586|−69565.94|
| v4L |665.31|190.707|+5071834.56|
| v5S compactfixtures |459.18|43.855|−166830.52|
| v6S |480.94|49.801|+665.05|

These different executions are not matched-state causal experiments. The mean comparison passed even the large pre-fix memory footprint; the1GiBceiling caught that failure. OldL also exceeded the independent128MiBbudget, so making the trend diagnostic would not retroactively accept it.

A concrete prospective option has been presented to the owner: retain mandatory RSS<=1GiB and final<=baseline+128MiB, keep the full30s trend as diagnosis rather than a zero-tolerance blocking predicate. This changes a previously explicit acceptance requirement and is pending an explicit answer. No version5 implementation/runtime or threshold change has been made on silence. Keeping the current criterion leaves this packet failed.

All prior failures remain unchanged. Plan6/8, fullM2acceptancepending; compactLruntime has not yet been reached. No publication,commit,push,sync/archive,supplier or target rollout.
