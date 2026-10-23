# Trips M2 data strategy revision: offline OSM demo

Date:2026-10-21. This is a change-history/authority receipt,not a second feature specification. Current contract remains proposal/design/spec/tasks in this same canonical change.

## Trigger and user direction

User approved v1implementation in a new fresh-dev worktree,requested independent audit/fixes,no push/enablement. User had no ready source and asked Codex to research it. Bounded primary-source research did not establish sufficient permitted fresh measured directed walks. User then selected «Пересмотреть план под офлайн OSM и модельные оценки» and asked whether this is too complex and sufficient for a demo project.

V2therefore bounds data preparation to one offline helper and a modelled-demo package. It does not add GraphHopper/OSRM/live routing/interactive maps/LLM/new DB. Modelled means a documented deterministic mathematical estimate,not AI inference or fabricated observations.

## What changed and what did not

- Replaced measured-walk prerequisite for honest demo data with pinned OSM topology plus reproducible paths and explicitly uncalibrated scenario estimates.
- Added bounded offline-only XML/gzip build helper (same .NET10SDK,BCL only),source/extract/inventory/profile/model hashes,ODbL derived-data/attribution artifacts and semantic review metadata.
- Human actor/observation fields remain nullable when not performed;human suitability and empirical calibration are separate D_live/Q_live,not invented scores or a prerequisite for source/demo engineering.
- Demo source stays default-off and excluded from Production. Current no push/no enablement remains;public data/demo distribution and target actions are separate.
- Owner privacy,CAS/no retry,empty-plan-first candidate,guarded preview adoption,exact M1 money/null default estimates,other module contracts and the19→21requirement corpus retain the same user scenario.
- Prior human rubric/frozen quality-cases.json is retained unchanged as Q_live evidence;Q_demo has its own explicitly source-integrity-only report/binding. No old failed/pending gate is relabelled passed.

## Concrete feasibility evidence

BBBike lists a Moscow OSM XML gzip extract155MB and checksum;the actual hyperlink was verified read-only as /osm/bbbike/Moscow/Moscow.osm.gz. This is smaller than the previously researched840MBregional PBF;no map was downloaded or imported during this revision.

Primary evidence:
- [BBBike Moscow extract formats](https://download.bbbike.org/osm/bbbike/Moscow/)
- [OSM copyright/licence](https://www.openstreetmap.org/copyright) and [ODbL legal text](https://opendatacommons.org/licenses/odbl/1-0/)
- [OSM foot](https://wiki.openstreetmap.org/wiki/Key:foot) and [oneway:foot](https://wiki.openstreetmap.org/wiki/Key:oneway:foot)
- [.NET10file-based apps](https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps)

The input size/format supports the proposed small offline-tool approach. It does not prove that a particular bounded profile/anchor set will emit18POI/60usablepaths within caps;the real build task must establish that,or fail explicitly.

## Initial source work preserved

Core task2.1v1 source and tests:parent/worker full Unit72/72passed,0skips;independent task review0P1/P2/P3. [Preserved review](core-facts-review.md) is bound to exactv1source hashes. It is not a v2or whole-M2audit.

V2changes evidence/review/model semantics,so current task2.1remains unchecked pending adaptation/TDD. No generator,HTTP,loader,UI or OSM build tool has yet been added. No new review asserts otherwise. The implementation branch remains codex/trips-m2-moscow-itinerary from fresh e654c76915103a82083ef2abc01c0579117c9749;the old planning worktree/approved v1snapshot is unchanged.

## Next boundary

Independent v2planning review checks source/method/licence/estimate/size/acceptance coherence before dependent code changes. The initial implementation authorization and newer revision request are recorded separately;there is no silent producer-source swap or human gate waiver. A final independent whole-source audit still follows actual implementation,as the user requested.

## R1–R3correction and storage recovery

Independent v2review found threeP2(profile/crossings,pairselection,snapshotTTL/buildtimestamps). Corrected design defines the exact conservative table,distinct crossing-node count,nearest5outgoing/source(18connected→90,48→≤240),Last-Modified-from-same-download SHA binding/no-publication-failure and frozen digest-affecting buildmetadata. Task/spec fixtures match.

During the correction write C:had0freebytes;PowerShell Set-Content truncated design.md to0bytes. Writes stopped immediately. Removed only newly npm-ci-installed node_modules inside the exact verified implementation worktree path(no symlink root);source/evidence were preserved and backed up to an owned ignored D:TestResults recovery directory. Recovered the design from final approved v1snapshot AD64EEBF2375C97ADAF9D1119000EF10D49AD693554E4DADE2EDD336AFD003FC plus documented v2decisions and corrections. This is a new full-text snapshot,not a false byte-identical restoration of pre-failurev2B983…;full independent recheck is required.

Recovered design:56133bytes,SHA256431B113575EC9350E98614DDA3C9F42561D557D05561F1CBA309129C2120038D. At recovery C:free≈4.16GB. No maps were downloaded. All three v1Core/test hashes still match their clean task-review snapshot;their72/72evidence remains applicable. Frontend dependencies must be restored later;future large inputs belong in an owned D:cache. Document writes now use temp+same-directory atomic replacement with a free-space check.
