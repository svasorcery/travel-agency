# Trips M2 verification: source implementation in progress

Current2026-10-21source status supersedes historical planning-only status below. User authorized source implementation in a separate fresh-dev worktree,independent audit/fixes,no push/enablement. ImplementationWT C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency;fixedbase/HEAD e654c76915103a82083ef2abc01c0579117c9749,branchcodex/trips-m2-moscow-itinerary. Full ledger implementation.md;no stage/commit/push/merge/Host/DB/model/targeteffects.

Completed source subgates:task2.1Corefacts99/99unit+independentreview;task2.2policyfixedP2credit,new136/136unit+3formatfiles+independentscopedrecheck;task1.4offlinehelper15/15CLI+format+independentreview andtwoidenticalreal20POI/90pathpreparations. Exactrawlogs/TRX/sourcehashes in per-taskreports. SourceDossier/immutableevaluationinputprotocol tasks1.2/1.3done;actualQrunbinding/outputsnotyetcreated. Loader3.1completewith98/98leanfilesystemIntegration+5formats+independentrecheck;Corevalidator2.3completewith110/110focused+246/246fullUnit,0skips,2formats+independentreviewPASS. Application3.2completewith294/294fullUnit(48service),0skips,5formats+independentmostcapablereviewPASS. HTTP3.3/3.4completewithNEW85/85boundary+retained30/30M1HTTP,0skips,15formats+1testfixformat+independentrecheckPASS. Architecture3.5completewith208/208full0skip+34/34focused+10formats+independentreviewPASS. InventoryvalidatorPASS,selftests62pass/1existingWindowscapabilityskip. TS4.1completeafterP2fix/recheckPASS:NEWapi-client356/35617files(85M2decoder)+web614/61452files(32M2HTTP),0skip/tsc0/Biome0errors-warnings191styleinfos. State4.2completewith105/105focused,FULL698/69853files0skip+independentreviewPASS;0newBiomewarnings,36unchangedM1baselinewarningsdocumented. Component4.3completeafterP2focusfix/scopedrecheckPASS:46/46focused+735/735web54files0skip;0newBiomediagnostics/13exactoldM1. Browser4.4complete/independentreviewPASS:35/35(5M1+30M2),35completeledgers/0unknown-external+6actualstaticGETsbyteSHAequal. Real-sourceQ/fullM2auditpending. ParentNxexternalasset/wireinputcheckscomplete;Productionbuild5.1passedonce/6distDataSHAmatch;structuralreviewPASS/62map58source4protocolpending;localD_demo/sourceSaccepted. Q_demo/S12evaluation/wholeaudit/CI/target/live remainpending. NewTripsstyle4.83kB(+830Bwarning)routed6.1fix,nobudgetchange. Defaultoff.

Read-onlyfullOpenSpecstrictvalidationaftertheseupdates:15/15itemsvalid;M2issues=[]. Formattingimmutable-data wiringcheckedwithlock-matchingBiome2.5.15;sevenJSONfilesvalid,formatterdisabledonlyforcanonicalhasheddataandfrozencasebytes. Existing recommended-config deprecation INFO remains (samebaselinefield,notnewerror);no unrelatedmigration. CaseSHA remainsF50B7511FC12642B20DF7CA207C780740382EEB4A482258929551CE29AA5756C;Gitfiltered/rawblobidentity59c8357973552114da4367812db2c0087d432cbdmatcheswithoutstaging.

## Historical planning receipts (unchanged evidence)
### Initial planning verification

2026-10-20. Это проверка документов, не implementation/route/target acceptance.

**Current status:** после первоначального independent recheck пользователь заказал дополнительное [саморевью](self-review.md). Оно изменило author artifacts; старые verdict/hash-match receipts ниже относятся к прежнему snapshot. Текущая проверка — в последней секции этого файла,без заявления нового независимого recheck.


## Scope and isolation

- User: plan Trips M2, fresh independent audit, corrections/recheck; no implementation. City clarification: «москва, остальное сам реши».
- Managed worktree: C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-plan/travel-agency.
- Fresh fetched origin/dev and worktree HEAD:30cd68d892cf72994c83047a22ceff782814796f. merge-base ancestor check succeeded. Shared checkout/branch was not switched or rewritten.
- M1 docs closure source:3b95b749107da9cdde07d9d8ca173293ab38764c, PR59 open at initial and follow-up live checks. Read exact Git objects; no second M1 corpus.
- No M2 stage/commit/push/merge. Product source, migrations, database/key configuration and runtime datasets untouched.

## Planning checks before independent audit

| Check | Result |
|---|---|
| Pinned launcher list/context | Correct physical managed-worktree root |
| validate trips-m2-moscow-itinerary --strict --json |1/1valid,issues=[] |
| validate --all --strict --json |15/15valid:12canonical specs + active Rail/M1/M2 changes; old Flights long-text INFO only |
| Artifact coverage | proposal/design/spec/tasks present; research evidence map retained |
| Requirement/scenario/task count |16requirements/39scenarios/21unchecked tasks,0completed |
| Traceability/uniqueness |16/16requirements mapped, no duplicate requirement/scenario/task IDs; new descriptions≤500characters |
| Local Markdown paths |32/32valid before adding this verification record |
| Whitespace | git diff --check clean; custom check of all five new Markdown files found0trailing whitespace |
| Change scope | git status --untracked-files=all contains only this OpenSpec change; tracked product diff empty |

No solution, application, UI, DB, Host, paid eval or provider runtime tests were run. Existing M1 CI is read documentary evidence, not a fresh M2 test. Public licence/source pages were read; this does not constitute a real Moscow data release.

## Tooling and corrections during preparation

New worktree had no pinned OpenSpec installation. Copied the existing tools/openspec/node_modules runtime from primary checkout into this worktree; normal launcher verified the unchanged package/version/manifest/digest. Product dependencies were not installed or changed. First sandboxed copy failed on the outside writable root; the docs-tooling copy succeeded under explicit scoped sandbox escalation. No launcher bypass, fallback global CLI or source pin change.

First local-link check caught active-change evidence paths with archive-depth extra parent. Corrected research paths mechanically and rechecked32/32. One ad-hoc checker invocation had a JavaScript parenthesis typo, corrected before the successful result. Neither tool error was treated as a product or plan acceptance pass.

## Independent review status

Fresh reviewer /root/trips_m2_plan_audit, fork_turns=none, dispatched after all artifacts existed. Reviewer owns only review.md and later recheck.md; author keeps the design/spec/tasks. Full audit and corrections are pending at this record's initial creation. Agent review is not human route-quality evaluation. Final review receipt and checks will be appended after corrections/recheck.

## Remaining acceptance

All21future tasks remain unchecked. Real source rights/POI/edges/coverage, human curator/two quality reviewers, structural implementation, exact-head CI, existing target provisioning/enabled Host/Keycloak and rollout remain unperformed independent gates. This plan does not authorize them.

## Initial independent verdict and author correction

[Full independent review](review.md) completed: one confirmed P2 (R1), no P1/P3 or other material issue. Review SHA256:03CD275A682473BD85F8DF9D39A4E774593663C3D1D0F3CAA2761B5D7C195077.

Author verified the finding against actual v1 TripPlan detail/ETag and TripsAvailabilityService catalogue-only reads: neither carries itinerary dataset identity. R1 was corrected consistently in proposal/design/spec/tasks without touching M1 source or contract:
- design §4.1 defines authenticated no-store GET /api/trips/itinerary-availability, ready identity/version/hash/release validity≤2KiB, independent404-disabled/401/403/503-unready, without DB/private fields;
- adoption requires both owner revision and current generation identity reads, one guarded ticket and fresh clock check before copying content; mismatch/withdrawal/feature-off/failure blocks transfer, local edits survive;
- the post-observation race remains explicitly best effort; later ordinary save is intention-only M1 CAS, not a live-source capability;
- added requirement/scenarios and actual-JSON API/client/state/browser task coverage.

Post-correction checks: strict15/15all; M2issues=[];17requirements/43scenarios/21unchecked tasks;17/17traceability;no duplicates/long descriptions/trailing whitespace;32/32local links across then-seven Markdown files;tracked product diff empty and exact original ancestry preserved. Fresh same-independent-reviewer scoped recheck requested against fixed hashes; its verdict is separate from the author's validation.

## Final independent recheck

[Scoped independent recheck](recheck.md) closed R1; remaining confirmed P1/P2/P3:0/0/0. Recheck SHA256:C001D565A3DB7C9C34A4A73F2FD59259A21ED10C5449C630342644A7B48DF21C. Author read the actual report and verified both review/recheck file hashes; substantive author artifacts match the recheck table.

This is a reviewed plan ready for human agreement, not product acceptance. The route/data/AI alternatives and scope trade-offs remain proposed author choices. Moscow and the structured wishes→suggestion→editing/acceptance sequence are the confirmed human direction. No new scope question blocks this planning deliverable; curator/reviewer identities and actual permitted records remain explicit future data/evaluation dependencies.

Final scope: only this canonical OpenSpec change, all21future tasks unchecked, tracked source diff empty, no M2 stage/commit/push/merge, migration or runtime call. Shared checkout and other worktrees remain untouched.
Final receipt: Markdown files=8; local links=35/35 valid; requirements=17; scenarios=43; tasks=21 unchecked/0 completed; hashes match independent recheck; strict corpus=15/15; M2 issues=0; all4planning artifacts present. The CLI isPlanningComplete/isComplete values mean artifact existence only, not task, implementation or rollout completion.

## User-requested self-review: current final receipt

2026-10-20: [саморевью автора](self-review.md) добавило S1–S4 и исправления в proposal/research/design/spec/tasks. Это дополнение после initial independent audit;нового independent recheck не заявляем. Старые review/recheck не менялись и подтверждены исходными hashes.

Current corpus:19requirements,52scenarios,21unchecked tasks;all4planning artifacts present. Strict corpus15/15,M2issues=[];19/19traceability,no duplicates/long descriptions/trailing whitespace;49/49local paths. Git tracked diff empty,only current OpenSpec change untracked,initial ancestor30cd68d892cf72994c83047a22ceff782814796f preserved. Current substantive document hashes приведены в self-review.md.

Исправлены real-pipeline route/body/readiness/status gaps,immutable payload/manifest digest и release/freeze dependencies,per-case human-quality metrics и раздельные realQ/syntheticS denominators,distinct-interest/stable-clock/policy identity. Географическая дорожка design §8 не добавляет implementation tasks/города в текущийM2. Runtime/product source/schema/dataset/model/provider действия и commit/push/merge не выполнялись.
