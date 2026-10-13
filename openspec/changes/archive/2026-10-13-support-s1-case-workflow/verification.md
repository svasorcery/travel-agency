# Verification — planning history and S1 source

## Final summary review / sync and archive — 2026-10-13

The current accepted contract is [support-case-workflow](../../../specs/support-case-workflow/spec.md). This archive retains completed25tasks,14requirements/38scenarios and the full planning/source/review/CI history. Final independent summary review found0newCritical/Important; exacthead/postmerge evidence and target/model limits remain as stated below. Sync preserves every accepted requirement/scenario and authoredPurpose; only main-spec headings change. Relative links are reconciled for the dated archive. Cleanup removed completedworktrees and verified ownedtransient data while preserving source, Git/CI records and requireddependencies/runtime; detailed receipt is in [review](review.md).

Acceptance documents [PR47](https://github.com/svasorcery/travel-agency/pull/47) head8ed70d0fa0214e264029c05d0614ad38256aeb37 passed all15ordinary checks including PR E2E in [CI38360329531](https://github.com/svasorcery/travel-agency/actions/runs/38360329531). Merge b377c7db9afe9b20c1a1da0a6268ce1b05a8a6d2 passed all14ordinary checks in [postmergeCI38361984560](https://github.com/svasorcery/travel-agency/actions/runs/38361984560); paid evals/postmergeE2E policy-skipped. This sync/archive documentation has its own publication/CI receipt; the product is unchanged.

## Source and CI acceptance — 2026-10-13

S1 implementation is merged through [PR46](https://github.com/svasorcery/travel-agency/pull/46). Final product head `16f17621c7dc52e5ad34282d0d19274030953ea8` passed [CI38357138705](https://github.com/svasorcery/travel-agency/actions/runs/38357138705): all15ordinary jobs, including mandatory PR E2E2/2 and actualHost README smoke. Paid AI evals were policy-skipped. Merge `01f4afb382d1c4ac7d66c7b66366275e07419186` passed [postmerge CI38358538463](https://github.com/svasorcery/travel-agency/actions/runs/38358538463): all14ordinary jobs; paid AI evals and postmerge E2E were policy-skipped. Required PR E2E was executed successfully, not replaced by postmerge.

Actual execution now includes Support Unit29, Support PostgreSQL/DataProtection14, full Host Integration68 (real disposable Keycloak code-flow/role/PKCE plus actual snapshot), Host HTTP453, Architecture183, Contract30 and Flights Integration401 (authoritative held-before-EF proof). Frontend CI ran full Angular/client suites, Node71 and fixture browser67. Initial CI38305129203 failed and is retained as diagnostic history, not acceptance; narrow fixes have independent0Critical/Important review and successful exact-head runtime verification.

Source/ordinary CI acceptance covers this S1 manual workflow. Target schema/keys/operator grants, real provider/payment/model acceptance and deployment remain unestablished. Laya S2/S3 and OpenSpec sync/archive remain separate scope. Historical local/source-only receipts below retain their original date and do not override this current acceptance.

All25source/CI tasks are complete;38scenario mapping updated from planned coverage to executed source/CI evidence. This small acceptance receipt branch starts from fresh origin/dev01f4afb; it changes only the canonical documents and current-state summary. Primary dev was fast-forwarded cleanly to product merge. No archive/sync or new product work is part of this bookkeeping.

## Pre-publication source receipt — 2026-10-12

User «го дальше» after planning self-review authorized source implementation. Worktree/branch/base remain as recorded below; primary dev is separate. Source is implemented and ready for publication/CI review, not accepted delivery. No S1 source commit/push/PR/merge exists yet. [38scenario map](scenario-map.md) and [handoff](handoff.md) retain pending evidence explicitly.

| Check | Observed result |
| --- | --- |
| Support Core/Application Unit |29/29, fake store/protector/order-access; no Infrastructure/Api/Host/PFX/DB fixture |
| Contract suite / Flights ownership source |30/30, including17ownership/version/ownerless/terminal/error/cancellation/read-only cases |
| Architecture |183/183, exact24assemblies/all module pairs/facades/real selectors, leaf consumers and generic constraint/argument IL guards |
| Lean HTTP + actual Wolverine/OpenAPI + invalid key CLI args |43/43in temporary explicitly linked standalone assembly;12actual production handlers execute against fake workflow service and synthetic signed JWT |
| Typed TS client |198/198,12files; meaningful nonempty queue regression RED→GREEN after map-index fix |
| Angular changed auth/routes/order/Support components |85/85,12files; worker observed; production build passed with only pre-existing Flights CSS budget warning |
| Node demo regression |71/71, Flights+Support; synthetic loopback-only auth, actor receipts/keyset/pre-write rejection/lost outcome |
| Complete browser demo regression |67/67,9Support cases plus existing58Flights cases; keyboard375px/plain text/private RAM/no forbidden financial/model traffic |
| Full Release solution build |PASS,0errors;27existing AD0001 warnings from original Flights method-group fixture remain; new Support mappings add0such warnings. Source generator gets its ordinary OpenAPI package targets; no new suppression |
| AI harness |133/133, source inventory includes9instruction pairs after Support; no authenticated verifier or new Codex chats |
| .NET inventory |62passed/1existing Windows fixture skip;7new projects and2ordinary Support lanes discovered, both required by PR E2E |
| README examples |13/13and catalog match |
| Strict OpenSpec |6/6,0failed; old cancellation long-requirement INFO hints retained |
| Biome / Csharpier / diff |Biome CI PASS, Csharpier source formatted; final checks recorded below after bookkeeping |

Safe .NET startup audit: new Unit assembly has only Core/Application/leaf and no PFX/static/assembly/collection bootstrap. Contract/Architecture load metadata/types and project graph without application entry points; source generators compile but do not execute Host. The temporary HTTP project explicitly links SupportApiFixture/BodyGuard/EndpointMetadata/Http/JwtTestTokens/WolverineOpenApiFixture/WolverineOpenApiTests and invalid-argument CLI source tests. It references Support.Api/Identity.Infrastructure and cached central packages only. No parent Host testproject, FlightPiiTestRing, SupportTestRing, EF/issuer fixture or Program is linked/run. TestServer authentication signs only synthetic memory JWTs. Isolated OpenAPI capture/merge helpers write public metadata only; production Host snapshot is a candidate until real Host CI equality. Every pre-existing path/schema remains semantically unchanged.

Migration source `20261012190847_AddSupportCaseWorkflow` was generated using reviewed design-time factory with an offline localhost sentinel, no password and no ambient Host config. [Up SQL](migration-up.sql) creates only schema/history and cases/messages/receipts under `support`; composite per-case IDs, revision/order-link/resolution checks, cascade FKs only within Support and bounded keyset/retention indexes reviewed. [Down SQL](migration-down.sql) drops all three Support tables and its migration history row: destructive loss if executed. No SQL/database update was run. Production initializer validates; non-Production startup applies schema and remains outside local authorized entry points.

Independent whole-source review by `/root/support_source_review` covered production, root wiring, source migration/SQL, issuer/auth/unknown gates, CI/inventory, demo/browser, generic matrix and OpenAPI candidate. Final disposition:0openCritical/Important. [Detailed findings](review.md). Local tests exposed and fixed nonempty-queue decoder callback-index misuse. Review closed controlled health fixture omission and added actual persisted title+messages/restart/rotation/new-writer/missing-old-reader/metadata-receipt source regression. Runtime-sensitive regressions were compiled only here; do not describe them as executed.

Still pending ordinary CI: actual PostgreSQL case/message/receipt atomic commits/races/rollback/physical purge, actual certificate/envelope+persisted rotation, actual Marten held-before-EF proof, real disposable operator/non-operator/customer PKCE issuance, full Host health/auth/OpenAPI and mandatory PR E2E. Task6.2 requires separate publication authority, exact-head runs and merge/postmerge receipts. Source-ready does not prove target schema/key/client grants, real supplier/payment/Anthropic/Laya, deployment, classification quality or operator time savings. No local/target provisioning, Host/AppHost/Travel.AI/DB/Keycloak/model startup or external API call occurred.

The optional reserved order-link rejection is pre-write only and never reused for storage/commit errors. Generic404/503/negative receipts/expiry/permission denial retain unknown barriers; old positive receipts confirm only their action. Private bodies/drafts are erased on current permission loss and never persisted to browser storage. Flights unknown confirmation remains independent.

## Historical planning receipt

Historical planning receipt, 2026-10-12. Этот раздел фиксирует written S1 plan до user «го дальше». Current source receipt ниже. Base `af725448e6632708d5129971f8570c1d047f4233` fresh fetched; managed worktree `support-s1-plan`, branch `codex/support-s1-plan`, ancestry содержит merged PR44/45. Основная dev отдельно чистая на этом SHA.

## Выполнено

- Pinned OpenSpec1.14.0 и runtimeDigest `d8c98d3e37285006042a99745ed1eaffdf1c66a7cba70a178d45ea1480d61b00` verified before/after ignored runtime copy. Source tooling/lock/manifest/skills/config unchanged; no init/update.
- CLI root/context/proposal/specs/design/tasks instructions прочитаны в selected physical root. Proposal→specs/design→tasks dependencies существуют; все четыре planning artifacts done. Этот status не означает implemented.
- Strict OpenSpec:5main specs плюс S1 change,6passed/0failed. Старые cancellation INFO length hints сохранены; S1 validation errors нет.
- Delta after requested self-review:14requirements/38scenarios. Tasks:0/25completed; каждый task указывает собственное evidence. Ни один planning checkbox не выдаётся за source completion.
- AI harness133/133;8instruction pairs/8skills/5agent pairs/5legacy commands,6exact pinned OpenSpec skills.
- Первый link pass:6planning docs/33local targets/0missing; после review corrections7docs/36local targets/0missing. Финальный pass включает новый [review receipt](review.md).
- Whitespace/placeholder/self-review: нет TODO/TBD/unfinished fields, Git tracked diff пуст; только новый canonical change. Исправлены retained-lifetime scope idempotency и случайный чужой термин в task4.3.
- Historical independent planning review: Ready,0openCritical/Important/Minor на14/33edition. P2 actual operator issuance и Minor private routing исправлены в artifacts и перепроверены; реальные auth/realm/CI/grants не менялись. Requested self-review добавил и закрыл SR1–SR3 на уровне плана: current14/38edition с25unchecked source tasks; отдельное новое independent review ему не приписывается. [Findings/dispositions](review.md).

## Планируемое покрытие сценариев

| Requirements | Source tasks / необходимое доказательство |
|---|---|
| Private customer / dedicated operator |4.1–4.3,4.5,5.3; signed-JWT owner/foreign/scope/azp matrix плюс actual disposable operator/non-operator/customer token issuance, no-store и actor-before-decrypt. |
| Optional verified owned order link |1.3,3.4; source ownership/EF-lag/unavailable, zero lookup без ID, zero финансовых вызовов. |
| Bounded plain text |2.1,4.1,4.3; Unicode/UTF-8/32KiB/duplicate/unknown fields/no echo/HTML plain rendering. |
| Atomic retained creation |3.3,5.2,5.5; real EF case+first message+receipt commit, failure/lost reply and retained known ID, no partial row. |
| Explicit lifecycle |2.2,3.3,5.2–5.3; actor-state matrix, ReplyAndReopen, race with resolution/expiry. |
| Concurrency / repeated identity |2.3,3.3,4.3,5.1,5.4–5.5; common mutation namespace/CAS, changed actor/kind/content conflict, direct actor-bound receipt beyond page, early404/unavailable/expiry≠NoEffect, old receipt/new snapshot. |
| Manual triage |2.2,5.3; operator old/new/source audit, client text remains allegation. |
| Protected private text |3.1–3.3,4.1–4.4; local fake guards отдельно от actual provider+persisted EF ciphertext в disposable CI: bound retained revision, tampering/wrong binding/missing keys/no ordinary bootstrap/restart/rotation. |
| Private pagination |4.4,5.1–5.3;20/21/cursor and503error versus empty200, no storage. |
| Retention / physical removal |3.5; before/at/after90days, multi-worker/interrupted pass, conditional delete/reopen и zero Flights deletion. |
| No booking / AI authority |1.3,2.2,3.4,5.2,5.5; zero supplier/payment/classifier/booking mutations, existing uncertainty unchanged. |
| Browser identity / write lifetime |4.2,5.2–5.5; logout/new client/capability/epoch/late response/reload/no repeat, same-GUID401/403private erase без uncertainty reset, stale403isolation, explicit Client render mode и safe callbacks; existing Flights guards. |
| Source acceptance / separate authority |1.1,1.4,6.1–6.3; safe local allowlist, actual new CI coverage/PR E2E, explicit provisioning/deploy/model limits. |

## Невыполнено и не разрешается этим receipt

Product source implementation, source migration generation/application, source auth/CI/inventory changes, certificates/keys/operator grants, local .NET/Host/AppHost/Travel.AI/DB, package/model installation, Laya inference/evaluation, supplier/payment/Anthropic API, plan/source publication или deployment не выполнялись. Written design требует review; provisioning и working-target proof остаются отдельно.

M3 closeout delivery перепроверена отдельно: PR45 final head `d31edde8760b05f3346bfa0379f57ff1018f2594`, CI38274371418all13ordinaryincludingPR-E2E; merge/baseaf72544, postmerge38276155312all12ordinary. Эти runs не принимают ещё не реализованный S1. Реальная классификация/качество Laya не выводятся из существования research appendix.

## Requested self-review verification — 2026-10-12

SR1–SR3 reconciled across proposal/design/spec/tasks и existing review/verification receipts; intent S1 manual workflow сохраняется. Проверены14requirements/38scenarios,25unchecked planned tasks; current open Critical/Important findings:0. Исправлен dependency order actual protection proof: provider/envelope tests3.1, persisted EF integration3.3после schema3.2.

Fresh checks: AI harness133/133 и exact instruction/skill/agent inventories PASS;8Markdown files/38local link targets/0missing, UTF-8 fatal decode PASS,0trailing-whitespace/unfinished placeholders; strict OpenSpec all6PASS/0failed с прежними cancellation INFO hints. Проверка включает untracked planning files, а не только пустой tracked diff. Branch `codex/support-s1-plan`/HEADaf72544 сохранены; единственный untracked change — этот canonical corpus. Product source/tests/auth/realm/CI/tooling, stage/commit/push/merge, keys/DB/models/external systems не изменялись и не запускались данным саморевью.

## Final source checkpoint

Final Csharpier check918files PASS; Biome CI272files PASS; strict OpenSpec6/6 PASS;18Markdown files/181local links/38mapped scenarios/0broken, fatal UTF8/trailing-whitespace checks PASS; git diff --check and fresh-base ancestry PASS.17/25tasks complete;8remain:1.3,3.1,3.3,3.5,4.1,4.4,4.5(actual runtime-sensitiveCIacceptance) and6.2(publication/exact-headCI/merge/postmerge). Source fixtures for each remaining acceptance are implemented and compiled; none is silently waived. Source migration/key-onlyCLI remain unapplied/unexecuted. No source publication or archive/sync occurred.

## Publication authorization

The user explicitly approved commit, push, PR, ordinary CI and merge after green checks. Target provisioning/deployment, local Host/DB/key operations and Laya/model execution remain outside that authorization. Fresh precommit checks: origin/dev is still af725448e6632708d5129971f8570c1d047f4233; branch ancestry verified; Support Unit29/29, BiomeCI and diff checks passed. Exact-head CI receipts will follow before task6.2 can close.

Publication preflight repaired the new module scope in commitlint and trailing blank lines exposed only after staging previously untracked files. Normal hooks retained; no bypass.

## First PR CI and bounded fixes — 2026-10-13

PR46 headff5e44c0c67a3b25e4807d0ad8d69045d1684298 ranCI38305129203. Twelve ordinary jobs passed, including Support Unit29/29, actual Support PostgreSQL/DataProtection14/14 and Flights Integration401/401 with the real held-before-EF ownership test. Frontend failed only on the new fixture import Nx boundary; Host Integration passed63/68, with one text-order OpenAPI snapshot mismatch and four issuer code-flow POST400failures. PR E2E was therefore skipped and this head is NOT accepted for merge.

Fixes: retain the existing per-line test-only Nx fixture import exception (RED→GREEN full travel-e2e lint); reconcile snapshot from normalized actualHost CI output after proving all36paths/79schemas semantically unchanged; adapt the CI-only cookie jar to Keycloak26.6's trusted loopback Secure-cookie behavior. It projects cookie selection through HTTPS only for the exact isolated loopback origin while leaving requests, Secure/HttpOnly flags, path, expiry, PKCE, roles/scopes and client policies unchanged. No real/target grants are changed. Pure cookie tests13/13passed in the standalone audited assembly; no fixture initialization/Host/DB/Keycloak/PFX locally. Temp test setup uses the already pinned patched SSH.NET2026.0.0 instead of an unpinned transitive2025.1.0; audit checks were not suppressed. HostTests compiled0errors with original27Flights analyzer warnings.

Independent bounded-fix reviewer/root/support_cookie_review reports0Critical/Important, confirmed snapshot equality and cookie forwarding bounds. Correct negative hostname is localhost.example.invalid: NETUri canonicalizes the word loopback to localhost, so that alias is not a foreign parsed origin. Actual issuer/fullHost/new exact-head CI and mandatory PR E2E remain required before merge.
