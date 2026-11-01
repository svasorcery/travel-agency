# AcceptanceVersion5 — bounded RSS budget, diagnostic steady trend

2026-11-01. Explicit owner answer: «Да, тренд — диагностика; продолжай сам». This authorizes the prospective classification and autonomous source/tests/review/new single S/L execution. Historical v6 remainsfailed on+665.049B mean change; no historical outcome is reclassified.

For version5, replace combined mandatory workingSetRecovery with m2PostWarmMemoryBudget: finalRSS−lastRSSbefore30s<=134217728B. Keep mandatory workingSetCeiling<=1073741824B and every other mandatory check unchanged.

Retain both original30s windows [300,330) and[330,last] including terminal360, exact counts/sums/means/delta, the original non-growth boolean and original combined workingSetRecovery boolean as nonblocking rssTrendDiagnostic. Report baseline/final/growth and budget result alongside them. Do not round or add epsilon; positive trend remains positive/false. Equal row/cache counts do not establish identical complete retained state or leak absence.

Require complete361unique ordered samples, each actual timestamp within its planned one-second slot, valid nonnegative RSS, baseline and both windows. Missing/duplicate/nonfinite/early/late evidence fails. Versions1–4 retain original combined mandatory predicate and their existing diagnostics. Unsupported or mixed versions reject. M1 original evaluator stays byte-for-byte unchanged.

Version5 inherits version4 fixed-origin cohorts and version3 required/no-op classification. Mandatory23=13original+10M2, with all prior supplemental booleans retained diagnostically. No schedule/data/sourcequota/95%/p95/75s/window-duration/cleanup/safety changes; no GC/trim/cacheclear/new idle phase.

Plan:
- [x] Actual-evaluator RED on positive-trend-only; realv6data and exact budget/ceiling/evidence boundary tests GREEN.
- [x] Full standalone, actual evaluator allversions, historicalM1replay, one independent review.
- [ ] New load-sl-v7 freeze/archive with485priorraw, then one S/L campaign with stop-on-failure and cleanup.
- [ ] Analyze and finalize plan only if both profiles pass complete mandatory acceptance. No unchanged-package retries, publication or rollout.

Sampling clarification: resource observations originate from a ConcurrentBag and are sorted before assessment. Validity retains the original inclusive deadline i<=actualAt<=i+1, plus independent planned-second frame validation; the diagnostic averaging intervals still use actual timestamps. This is not a new tolerance.

