# Future acceptanceVersion4 — recovery by fixed chain origin

2026-11-01. This is a prospective measurement-contract decision under the owner's autonomous completion instruction. It does not reclassify v5. Source implementation and independent review are required before the next run.

Keep all numeric workload, success, latency, source, memory, queue, time and safety limits. Keep the fixed3152jobs/profile and every failure/cancellation/not-dispatched outcome. Do not change due times, dependencies, data, fake faults, quotas, cleanup or retry policy.

Only rolling recovery membership changes. For each candidate end300..315, each search/rates/prepare/create step due in[end−60,end) is eligible iff its fixed root search(or itself for search) is also due at/after end−60. Earlier-root steps are carry-in, with IDs and original raw useful counts retained separately. Not-yet-offered children are never counted. Membership is computed from the frozen graph before observing results, not from success/completion/source availability.

For every kind require a nonempty eligible cohort and>=95%useful; searchp95 remains<=1000ms. Nonpurchase completion and authoritative purchase confirmation must occur by that end. Use the existing WindowPurchaseSuccess predicate unchanged. A failed chain whose root is inside the window remains a failure. Missing/duplicate/mismatched facts or missing/cyclic/future/wrong-kind graph dependencies fail closed.

Inversion demonstrated by realv5: search252failed correctly just before quota expiry; children256.2/263/264.5 remain failed globally. At end312 the root252 is still inside and they must count. At end313 it is carry-in and excluded only from the new-chain metric. All original raw windows still show the failed ratio. This change measures newly started recovered work; it does not claim every carry-in operation succeeded.

Forversion4 replace recoveryWithin75Seconds with mandatory m2RecoveryWithin75SecondsByOrigin. Preserve the original boolean and raw/window counts in recoveryDiagnostic. Versions1/2/3 keep their original recovery membership. Other inheritedversion2/3 diagnostic/classification rules remain unchanged. Mandatory23=14original+9M2.

Freeze both S/L origin maps from the source-proof together with source/build/profile/data. Full acceptance requires a fresh complete S/L run and verified cleanup. Offline counterfactual/source tests are not runtime acceptance. Historicalfailedreceipts remainimmutable; no real suppliers,target,publication or rollout.
