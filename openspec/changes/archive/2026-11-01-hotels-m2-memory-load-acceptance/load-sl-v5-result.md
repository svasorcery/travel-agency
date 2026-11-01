# S/L v5 result — S recovery-cohort failure

2026-11-01. One version3 campaign: S22/23, Lnot-run understop-on-failure.409.263s,driver26684,exit−532462766,forcedfalse. S3152/3152outcomes,361samples; executor/finalization errors absent. Cleanup independently verified;425priorraw and30newraw pinned,source2022/build999unchanged.

All memory gates, actionable queue, source/business/cache limits passed. S baseline421969920B,peak481480704B,final467955712B; final−baseline45985792B<128MiB, recoverymean decreased166830.52B. Queue361complete observations,unknown0,maxraw2,maxrequired1,maxredundant1;terminalraw/required/redundant0. CompactDuffel1332logical aliases use620147BuniqueUTF8ratepayload;storedkeys42558B. HBX820logical entries storedkeypayload105058B. No entries/history cleared.

The sole failed mandatory check is recoveryWithin75Seconds. Search252s returned Limited: Duffelcomplete,HBXLocalCapacity. Its fixed descendants rates256.2,prepare263,create264.5 are NotDispatched. Search258s and later are healthy. In every legacy window ending300..315, the delayed child create264.5 remains in the purchase denominator; useful count reaches only2/3.

HBXwindow expiry is bounded around252.012..252.014s using the reset event258s and source monotonic timestamps; search252 started252.0005556s. Thus the refusal itself is valid, not a reason to change provider limits. The first search of that chain precedes the final rolling window[255,315), while its children still poison that window. The existing metric conflates newly started recovery chains and work carried in from before the window.

All jobs/refusals and legacyfalse remain retained. No replay changes this outcome. A prospective methodology correction is documented separately; no request rescheduling, replacement purchase, threshold expansion or repeatedv5run.

Raw: TestResults/hotels-m2-memory-load-acceptance/load-sl-v5/. FreezeF78F80A40E8DBC22C0CB647EDDFAD69A4D60739968BC10081DB3B7DC4F87DA2F;archive9BDC7A5762D75A7EEB2B75D8D4723E36312AE202DD2940C7A5A73AFB5A717856. Source preparation:397standalone,96evaluator+3rejected-artifact cases,2ImportantreviewfindingsfixedbyRED/GREEN. No new Lmemoryacceptance yet;plan6/8.
