# Trips M2: саморевью автора и исправления

Дата:2026-10-20. Поручение пользователя:самостоятельно перепроверить план,исправить всё найденное,объяснить порядок расширения географии после выбранного M2.

## Проверяемая отправная точка

Worktree:C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-plan/travel-agency;HEAD/base30cd68d892cf72994c83047a22ceff782814796f. Прочитаны полный proposal/research/design/spec/tasks,предыдущие independent review/recheck и актуальные Trips Core/Application/Api/Infrastructure/TS/editor/test seams.

Первоначальный independent [review](review.md) и [recheck](recheck.md) сохраняют свои точные hashes/выводы; они не переписаны. Последующий изменённый корпус проверен автором,а не новым независимым reviewer. Старый verdict0P1/P2/P3 относится к прежнему snapshot,не служит подписью под нынешними изменениями.

До саморевью hashes:design63BBDEEE1BF39F59A1BE653A6085001A5CCF7FD055425B8C2F39D9DB95B1EA77;proposalDAE55153B34C5731ADF8BB5AB600AF2ADA4D115C62FFDDEBD636DB5E93654B29;research30A4D661CEBCCDD5112A30676D77DD075234D0C641893F5C5CFEC8960A7D79AC;tasks30A87B364102D86C54A422D4AFBA5E20E05F34847EA5BA196AA64836D29AB235;specC5CB541F4D7F5CC365A72070F8620BA9A0D9A3F52E2A16575F48A8FC1677CE93.

## Найдено и исправлено

| ID | Приоритет | Подтверждённый пробел исходного плана | Исправление в документах |
|---|---|---|---|
| S1 | P2 | Endpoint-only design недостаточен:реальный M1 guard применяет create DTO ко всем POST и plan readiness ко всем GET;M2-off только post-auth не даёт обещанный anonymous404;новые ErrorOr codes без map попадут в400 | design §4.2:route classifier в existing pre-auth response policy и post-auth request guard;candidate{}1KiB отдельно от create,availability без DB/protection readiness,точные status maps,full-pipeline/sentinel/case/chunked/M1 regression scenarios и tasks3.3/3.4 |
| S2 | P2 | Hash не определял хешируемые bytes/границу manifest;1.3 требовал runtime hash до создания package3.1,а3.1 формально требовал уже закрытый D | design §2/7:immutable payload+отдельный manifest,SHA256exactUTF8/LFбезBOM,новая version при изменениях;сначала dossier/case freeze,затем bytes+human data release и отдельный run-binding до eval;task3.1 больше не зависит от собственного результата |
| S3 | P2 | median≥4each не определял уровень агрегации;высокий corpus median мог скрыть плохую пригодность отдельных маршрутов;24mixed cases могли выглядеть как24human real-city checks | design §7:для каждой(case,dimension) оба score≥3,mean≥4;12realQcases отдельно от12Sedge/negative,вымышленные/изменённые fixtures толькоS;original scores/empty/failed cases остаются в denominator;новые spec scenarios и tasks1.3/5.2/5.3 |
| S4 | P3 | «Количество совпадений interests» не определяло distinct coverage против частоты tags;детерминизм не учитывал clock eligibility и порядок pruning | design §3:distinct requested-interest union across chosen days+path,stable ID expansion/sort before pruning,единый asOfUtc и явный policyVersion;metadata/adoption связывают policyVersion,unit scenarios shuffled arrays/repeated tags/fixed clock |

Это ошибки/неоднозначности плана,не найденные runtime баги реализованного M2:продукт M2 ещё не написан. S1 выведен непосредственно из исходников без запуска приложения;остальные — из противоречий/недоопределённых acceptance/dependency правил.

Дополнительные узкие исправления:уточнено,что открытый docs PR59 не блокирует source planning/будущую разрешённую реализацию при чтении accepted closure по exact commit;ReadAsync port возвращает Task<ErrorOr<ValidatedItineraryFacts>>;candidate получает сохранённый DestinationSelection и проверяет city/IANA пакета;ranking не содержит Moscow-specific веток;expiry повторно проверяется перед success;source links не prefetch и используют noopener/noreferrer. Никакие helpers/DTO/runtime files по этим именам сейчас не созданы.

## Source evidence для S1 и географии

- [TripsRequestGuardMiddleware](../../../modules/trips/Travel.Modules.Trips.Api/Middleware/TripsRequestGuardMiddleware.cs):lines63–111,120–134 — POST/PUT generic body validation,создание требует title/brief;ReadyAndNext всегда разрешает ITripsReadiness.
- [TripsResponsePolicyMiddleware](../../../modules/trips/Travel.Modules.Trips.Api/Middleware/TripsResponsePolicyMiddleware.cs):lines9–31 — существующий pre-auth M1 feature guard и no-store.
- [Host Program](../../../apps/Travel.Host/Program.cs):177–185 — response policy до Authentication/Authorization,request guard после них.
- [TripsProblems](../../../modules/trips/Travel.Modules.Trips.Api/Contracts/TripsProblems.cs):32–43 — неизвестные code получают default400.
- [CityCatalogue](../../../modules/trips/Travel.Modules.Trips.Core/TripValues.cs):триcity IDs/две зоны/version1.
- [TripPlan.Restore](../../../modules/trips/Travel.Modules.Trips.Core/TripPlan.cs):195–196 — zone allowlist и equality сохранённой catalogueVersion с текущей.
- [TS decoder](../../../shared/ts/api-client/src/trips.decoder.ts):248–256 — те же зоны и толькоcatalogueVersion1.
- [Editor](../../../apps/web/src/app/trips/trips-state.service.ts):refreshDetail сохраняет dirty changes в comparison/conflict,keepLocalAgainstCurrent требует явного решения. Проверка не подтвердила скрытый rebase уже описанного M1 reconciliation,поэтому его не меняли.

## Географическое расширение

[Design §8](design.md#8-расширение-географии-после-этого-m2--направление-не-текущие-задачи) фиксирует этапы,а не новый implementation scope. После Moscow D/S/Q+ordinaryCI можно отдельно начинать второй city source;real users всё равно требуют target/rollout. Новый пакет/curation/quality на каждый город;общий алгоритм остаётся прежним. Сначала existing city catalogue,затем новые IDs/zones с backward-compatible snapshot/decoder чтением. M3/AI/booking не prerequisite.

Найденная опасность будущей дорожки:простое повышение CityCatalogue.Version сломало бы Restore старых зашифрованных планов и TS decoder. Сейчас M1 не изменён;вместо фиктивного «исправлено» поставлена обязательная backward-compatibility граница будущего geographic change.

## Проверка и границы

Изменены только документы этого OpenSpec change. Product source,fixtures,dataset payload/manifest,миграции,Host/БД,ключи,модель/поставщики не выполнялись и не менялись. Все21future tasks остаются unchecked;Moscow-only scope не расширен.

Новый independent audit не заявляется:это запрошенное саморевью. Текущие document hashes и финальные syntax/link/traceability/scope receipts добавляются ниже после проверки;предыдущие review/recheck остаются историческими.

## Итоговая проверка саморевью

Pinned OpenSpec strict:15/15corpus valid,M2issues=[];19requirements/52scenarios/21unchecked tasks.19/19requirements mapped,duplicate IDs=0,new descriptions>500chars=0,trailing whitespace=0,49/49local Markdown paths valid. Tracked product diff empty;original ancestry30cd68d… проверена. Старые review/recheck hashes совпадают с исходными. Эти проверки подтверждают документы,а не выполненные будущие tests/данные/качество/target.

Все S1–S4 исправлены в описанном planning scope;неисправленных подтверждённых замечаний этого саморевью нет. Неразрешённые release dependencies (actual rights/records,curator/human reviewers,implementation,CI,target/rollout) сохранены,не названы «устранёнными».
| Author artifact после саморевью | SHA256 |
|---|---|
| proposal.md | 253F972C4B389FBF444E702ABF421FD037F6560187EA5210D468C1E9368FBF77 |
| research.md | B495C737EAA89AB60C2B3E53C3660796AB86A059808A57069A8210E250A452A5 |
| design.md | AD64EEBF2375C97ADAF9D1119000EF10D49AD693554E4DADE2EDD336AFD003FC |
| specs/trip-itinerary-candidates/spec.md | 75AC28F027082D2CE7C0FFA495FF9E50A6AE4401A6C527A60246372FD3EC30E0 |
| tasks.md | 36DCBF856B75AAF945CF139A44CF02A78BE385BE7B1DADFB634E5EFBEA48B1F9 |

## Rail refinement self-review, 2026-10-21

The earlier self-review, hashes and21-task/19-requirement receipts above are historical planning evidence. They are preserved rather than relabelled as current implementation or Rail acceptance. This refinement is an author self-review; the controller will dispatch a fresh independent plan reviewer before code.

| Finding | Resolution in the current plan |
|---|---|
|Published Rail prohibits durable supplier data, so a historical note is not automatically permitted for real results|Fictional-only automatic projection; real profile transient/view-only with explicit explanation. Future real projection needs revised Rail capability/rights, not an enable flag or stripped-data workaround|
|Old prose said cross-module work was later, source not implemented and all22tasks Moscow-only|Proposal/design now identify17 historical walking completions, additive Rail consumer and27 total tasks; geography remains Moscow-only for walking. Research/self-review old receipts explicitly remain historical|
|Nested Rail form could submit M1 Save|Picker sibling outside existing editor form; non-submit actions type=button; actual browser Enter/search0PUT regression|
|Current RailState can retain same-profile reference after failure; null TTL/timer delays might admit stale choice|Explicit failed-reference invalidation and click-time result/reference checks, source profile/epoch/generation/request/member checks; no timer-only authority|
|A copied plan or new parallel epoch machine could bypass M1 dirty/revision barriers|Reuse opaque shared foundationVersion/session/read/edit ticket; shared clean-saved guard with unchanged empty-only walking wrapper; nonempty Rail supported|
|Separate add/update could leave half a block, or valid long refs could exceed note/body bounds|One synchronous append after full decodeTripWrite,100/500scalar and fullUTF8 preflight,8/day cap; exact projection refuses overflow without truncation|
|Limited was underspecified and damaged tickets could leak into a fresh choice|Only selected complete mode or limited page-limit/interval-service-unsupported concrete intact row; tickets.damaged refuses choice; envelope/mode reasons remain visible|
|Target day could be confused with an unsaved brief edit or departure date|Independent selector from saved dates; no dirty form mutation, no date/feasibility inference|
|Component extraction could regress iframe/CSP, global focus or standalone Rail|Shared component preserves exact allowlisted frame/sandbox/no-referrer/attribution failure; scoped IDs/focus and standalone tests|
|Real/fictional lifetime and saved notes could falsely regain trusted status|Generated note explicitly fictional/historical; no restore/parse/badge; real failure never falls back; teardown clears source memory|
|Local stage could imply server freshness, autosave or lost-PUT retry|No extra owner GET for Rail; shared local foundation plus explicit ordinary M1 PUT/CAS,412 preserves draft; lost-command uncertainty blocks new picker actions|
|Fresh base, dependencies and older green receipts could be conflated|Original e654 ancestry preserved through ordinary ff to a872;1.5 controller receipt,4.8 current build/structural review; physical dependency consumer and separate blocked network audit tracked by controller|
|New Rail fixtures could contaminate walking quality evidence|12Q+12S frozen corpus/thresholds and fixture asOf unchanged;5.2 PHASE A paused before any real Q output, Rail browser denominator separate|

New tasks1.5/4.5–4.8 are unchecked.4.5+4.6 share one fresh implementer because of component/state/editor file coupling, then primary full-patch personal review and a fresh task reviewer;4.7 has its own fresh test implementer/reviewer.6.1 retains final most-capable full-source audit, one unified fix wave and recheck including root config/dependencies.6.2publicationCI and6.3target/enablement remain unchecked and unauthorised.

The new delta has12 requirements with exact task traceability in tasks.md. Detailed eligibility/projection/interfaces and risks are in rail-refinement.md. The author has not run future implementation tests, generated Q outputs or claimed live quality. Raw pinned validation and preservation receipts are saved under .superpowers/sdd/tasks/rail-plan-validation; final results and file hashes are in .superpowers/sdd/tasks/task-rail-plan-report.md.

Final author validation: pinned strict change1/1 valid with issues=[]; strict corpus15/15 valid. Five pre-existing INFO long-description notices belong to flights-whole-order-cancellation, not this delta. New Rail12requirements/35scenarios,12/12mapped;27tasks/17checked;67/67local document links exist; new descriptions≤500characters. SHA256 equality confirms unchanged trip-itinerary-candidates/spec.md, quality-cases.json and quality-protocol.md. Recipes, Data and accepted receipts were not edited. No product/test/config/dependency/ledger changes or Q outputs by this author.

Primary review refinement: fixed generic title hid the selected train in the calendar. Title now uses transport noun + exact optional number + demo marker when valid within100scalars; missing/overlong/M1-incompatible optional number uses a fixed noun fallback without truncation. Essential exact identity remains in the note or staging is refused. Task4.6 and a new normative scenario cover the fallback; new Rail scenario total is35.
