# Recovery cohort correction — future acceptanceVersion4

2026-11-01. Implementer's explicit decision under the owner's autonomous completion instruction. Motivation: v5 carries a failed search252s into rates256.2/prepare263/create264.5, then treats those descendants as new work in rolling windows whose start is after252. The quota refusal itself was valid.

For futureversion4 only, classify rolling recovery cohorts using the fixed dependency graph frozen before execution. A step is in the recovery cohort iff its own due is in[end−60,end) AND its root prerequisite(search for purchase chains) was offered at/after end−60. Steps due in the window with older roots are reported as carry-in with their original outcomes; no operation is deleted or retried. Steps not yet due are not offered and cannot improve a current window.

Use the same16candidate ends300..315,60second windows,95%useful threshold,searchp95<=1000ms, authoritative confirmation cutoff and existing purchase predicates. Require nonempty cohort for every search/rates/prepare/create kind; missing, duplicate or inconsistent facts/graphs fail closed. New membership uses only predeclared IDs/due/prerequisites, never observed success, latency or source availability. Fixed normal/final-steady gates and all6304global outcomes remain unchanged.

Forversion4, replace mandatory recoveryWithin75Seconds with m2RecoveryWithin75SecondsByOrigin and retain the legacyboolean plus per-window own-due denominators/useful counts alongside eligible/carry-in IDs. Version1/2/3 keep oldcohorts. Mandatorytotal23=14original+9M2. Version3due-work and version2final5s diagnostics remain inherited. No memory,sourcequota,safety,timeout,loadschedule,data or numeric success threshold changes.

Review tradeoff: complete-chain membership intentionally excludes carry-in from a claim about newly started recovered service. It does not erase their failures: global/fixed-phase and raw rolling results retain them. This is a prospective measurement-contract change, not retroactive acceptance ofv5.

Plan:
- [x] Realv5fixture + boundary/missing-data RED/GREEN; wire new classifier and explicit diagnostics into actualevaluator.
- [x] Full standalone, pureproducer checks, historicalM1replay, one fresh review and verifiedfreeze.
- [x] One newly frozen S/L under the same autonomous authorization, stop-on-failure; analyze/cleanup without unchanged-package retries.


Completion: v6failedmemorytrendretained; v7 completedbothprofiles under owner-approvedversion5 while retaining fixed-origincohorts. Newandlegacyrecoverypassed310/307 inthat run.
