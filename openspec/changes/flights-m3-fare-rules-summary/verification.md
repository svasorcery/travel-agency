# Проверка плана и реализации

Дата: 2026-10-12. База: `163e3d9b467259518a11e6c6c423aa0e3f79a513`. Первые разделы сохраняют исторический planning receipt до разрешения «го». Текущий source receipt приведён в последнем разделе; он не является runtime/CI/model acceptance.

## Самопроверка до независимого review

Проверены соответствие пользовательскому запросу, source provenance, текущие четыре capability contracts, scope/денежная семантика, compatibility, ownership, PII, model/cost/time limits и разделение evidence gates. Важные решения и исправления:

| Риск | Действие |
|---|---|
| Null превращается в false; fees/code/scope теряются | D2 + требования Three states/Exact penalty/Separate scope + tasks2.1/3.1/3.2 |
| Старое re-quote меняет сумму, оставляя прежние условия | D3 очищает rich snapshot при absent replacement; отдельный historical scenario/test2.2 |
| Служебное время попадает в canonical search compare | D3 явно отделяет semantic fare identity от metadata; cachev5 и task3.3 |
| Summary lookup раскрывает ownerless quote | D4 не создаёт публичный GET; quote response и existing owner creation-reader; task4.3 |
| Nullable DTO сам по себе упадёт на malformed optional field | Добавлен bounded tolerant fare-field reader того же JSON в D2; никаких HTTP повторов/поправки правил |
| LLM выдаёт правдоподобное, но ложное правило | D5 ограничивает ответ IDs/scopes и controlled renderer; полный fallback на нарушении coverage/equality |
| Ledger выглядит как budget/dedup guarantee | D6 отделяет guards от post-call accounting, UnknownUsage, zero retries и process-only limits |
| Fake output tests выдаются за real-model quality | D7/требование acceptance; отдельные paid/human/live gates; organized mode default-off |
| Filtered Unit test создаёт keyring | Source audit assembly fixture; task1.1; .NET runtime tests в этом планировании не запускались |

Самопроверка не нашла незакрытой развилки, влияющей на scope: рекомендован A+B default-off. Численные критерии оценки — предложенные критерии данного плана, не результаты измерения. Source compatibility не обещает достоверность older UI.

## Выполненная проверка OpenSpec до review

- `node tools/openspec/run.mjs --version`:1.14.0; runtimeDigest совпал с pinned manifest.
- `list --json`, `context --json`: repo-local root новой managed worktree; active changes первоначально0.
- `new change flights-m3-fare-rules-summary`: CLI scaffold с .openspec.yaml; ручного scaffold не было.
- `status --change flights-m3-fare-rules-summary --json`: proposal/specs/design/tasks done, isPlanningComplete=true. Этот file-existence status не означает завершение24implementation tasks.
- `validate flights-m3-fare-rules-summary --strict --json`:1passed,0failed, issues=[].
- `git diff --check`: exit0; `git status`: только новый untracked change. Ни source, ни tooling/config не редактировались.
- Полная `validate --all --strict --json`:5passed/0failed — новый change и4действующих contracts. В старом cancellation contract осталось5INFO о длине требований; это не ошибки и не основание переписывать действующий contract.
- `npm.cmd run check:ai-harness`:133tests passed,0failed; validated8instruction pairs/8skills/5agent pairs/6pinned OpenSpec skills. Первый sandbox-запуск завершился spawn EPERM; тот же command после обычной одобренной эскалации прошёл. Реальный authenticated Codex verifier не запускался.
- В том раунде все local Markdown links существовали; корпус содержал15requirements/36scenarios и24implementation checkboxes,0completed. Новые planning files были без trailing whitespace. Tracked source/tooling/config/main specs не имели diff с исходным SHA; основная dev оставалась чистой.

## Независимое review

Отдельный агент `/root/fare_plan_review` провёл read-only проверку всего корпуса и существенных source/provider claims. Раунд1: needs revision, одноP2 — early JSON parsing baggage quantity и optional labels. Исправлены D2/task3.1, добавлен scenario; агент перечитал исправленный корпус. Раунд2: **Ready, P2closed, существенных остаточных gaps нет**. Неблокирующая поправка количества scenarios35→36 внесена. [Полная запись и disposition](review.md).

После того исправления `validate flights-m3-fare-rules-summary --strict --json` дал1passed/0failed/issues=[]. Проверка local links/whitespace:8files,15requirements,36scenarios,24uncompleted source tasks. Эти receipts относятся к версии до последующего пользовательского саморевью.

## Повторное саморевью

По прямому запросу пользователя повторно проверены proposal/design/spec/tasks и source boundaries. ЧетыреP2 planning gaps и дваP3уточнения исправлены; details/disposition S1–S6 в [review.md](review.md). Intent и scope A+B default-off сохранены; changed only canonical planning documents.

Текущая delta:16requirements/44scenarios. Чекбоксы24source tasks остаются невыполненными;4/4planning artifacts — file-existence status. Strict validation после содержательных исправлений:1passed/0failed/issues=[]. Предыдущая независимая оценка Ready не является новым независимым принятием этих поправок. AI-harness133pass — исторический receipt предыдущего planning раунда, в этом docs-only self-review не повторялся.

Заключительные проверки этого раунда:

- `node tools/openspec/run.mjs validate --all --strict --json`:5passed/0failed; новый change issues=[], старые5INFO о длине cancellation requirements сохранены без правок.
- `status --change flights-m3-fare-rules-summary`:4/4planning artifacts complete. Source tasks24pending/0completed.
- Проверены8files: links/line anchors существуют, нет whitespace/placeholder/постороннего текста;16requirements≤500characters и44WHEN/THEN scenarios.
- `git diff --check`, base ancestry и сравнение tracked code/tooling/main specs с исходным SHA: exit0. Worktree содержит только untracked canonical change; основная dev чистая на163e3d9b.
- Никаких .NET/Host/DB/Travel.AI/paid/API запусков, source изменений, stage/commit/push или публикации в этом self-review не было.

## Не выполнялось

.NET/Host/AppHost/Travel.AI/DB/Aspire tests, keyring provisioning, paid/model/supplier/payment API, schema/migrations, CI dispatch, stage/commit/push/PR/merge, sync/archive и deployment. Будущие проверки в tasks.md — план, а не acceptance evidence. Указанные пользователем PR/CI handoff не являются свежей проверкой этого change. Все planning notes находятся в одном каноническом change.

## UI source verification, 2026-10-12

Этот отдельный receipt относится к последующей разрешённой реализации в managed worktree, а не изменяет исторические planning receipts выше. Scenario-to-test mapping для6.1–6.4: [demo.md](demo.md#ui-scenario-to-test-mapping).

Проверенный bootstrap fictional browser: existing `playwright.flights-demo.config.ts` запускает только Node demo listener `127.0.0.1:5100` и Angular `flights-demo` serve `127.0.0.1:4201`; proxy направляет `/api` и `/events` только на этот loopback. File replacements используют fake source/fake auth, demo accessToken=null; real Host/AppHost/Travel.AI/DB/Keycloak/model/supplier/payment не запускались.

| Проверка | Receipt |
|---|---|
| Standard isolated Node runner `node --test tools/demo/flights-fare-summary.test.mjs tools/demo/flights-search-api.test.mjs` |65passed/0failed. Первый sandbox runner получил spawn EPERM; обычный runner после scoped escalation прошёл. Предварительный diagnostic no-isolation run не заменяет этот receipt. |
| `nx test api-client` после Int32/decimal boundary corrections |189passed,11files; новый summary suite18tests. Exact decimal.MaxValue и scale28 остаются строками; overflow отвергается без Number conversion. |
| `nx test web --watch=false` |442passed,36files. Первоначальный441/1 выявил оставшиеся bool-only display expectations; исправлены assertions unknown, guards сохранены. |
| `nx build web --configuration=flights-demo` |Success, actualwebbuild; dependencybuilds2/3cache. |
| `nx build web --configuration=production` |Success, initialbundle298.05kB; existing unchanged flights-page.component.scss4.50kB exceeds4kB warningthreshold, below8kB errorlimit. |
| Full `tsc --noEmit` app and api-client configs после typedAIstatus |Success. |
| Focused fictional Playwright fare-summary |1passed: four legs/nine adults at390px, exact zero/unknown/scopes, keyboard disclosure, unchanged HTTP count and zero financial actions. Первый run выявил testDOM-count race добавления legs; исправлен existingpattern ожидания count2→3→4. |
| Biome new UI/decoder/demo source checks |No error diagnostics; bracket-access infos сохранены для noPropertyAccessFromIndexSignature. |
| Owned UI/TS/demo paths `git diff --check` |Success. Whole-worktree checks принадлежат root coordinator. |

Первый full fictional browser run:55passed/2failed. Search header assertion читала асинхронный response.allHeaders callback слишком рано; заменена на expect.poll. SSErefund case остался в auth при первом входе; isolated rerun без source/test изменений прошёл1/1. Этот fullrun совпал с исправлением TSdecoder под watched devserver; reload мог сбросить tab-memory auth, поэтому receipt не объявлен green. Финальный fullrun после остановки правок исходников записывается ниже отдельно.

Финальный полный `node node_modules/@playwright/test/cli.js test -c tests/travel-e2e/playwright.flights-demo.config.ts`: **57passed/0failed,2.8minutes**, Chromium,1worker,0retries. Включает новый fare-summary сценарий и прежние SSErefund/asyncheader сценарии; отдельного изменения SSE/financial services или отключения assertion/retry не было. Сборка и тесты исполнялись через существующие locked dependencies, без installation/config/package изменений.

Ни локальный fake runner, ни build/tests не являются real-model quality, supplier/payment acceptance, deployed runtime или full-M3 proof. CI/PR E2E, paid model evaluation и live/proxy acceptance сохраняют отдельные gates.

### UI independent review P2: honest organized fallback

Finding: UI initially ignored `aiStatus`/`presentation`, so an explicitly requested organized stage silently fell back. Corrected only UI/component/browser tests: controlled Russian status notices explain each fixed failure reason and retain deterministic facts; `AiOrganized` gets a factual grouping label. Ordinary Disabled/NotRequested stays quiet; explicitly requested disabled mode shows the reason. History does not present an active optional AI stage. Choice/disclosure still dispatches zero requests; only the ordinary explicit re-quote carries the requested mode.

RED: focused component target produced7expected failures (new notices absent),4existing passes. GREEN: `nx test web --watch=false --include=apps/web/src/app/flights/flight-fare-summary.component.spec.ts`12passed/0failed. Runner include is workspace-relative; initial project-relative include was rejected before tests and is not behavioral evidence.

Changed production template compilation: `nx build web --configuration=production`success, initial298.42kB, existing unchanged4.50kB stylesheet warning retained. Focused fictional Chromium run `flights-fare-summary.spec.ts`:2passed/0failed,18.7seconds including bootstrap; rejected-plan notice shown after authenticated fictional organized re-quote, exact0.00fee retained, choice/disclosure zeroHTTP, no hold/confirm/cancel/refund. Prior full57/57 is retained as the pre-P2 regression receipt; full suite was not repeated for this presentation-only correction.

### UI contract review P2: included allowance bounds

Ancillary catalog reader still capped informational included quantities at891, conflicting with new rich facts and server KnownAllowance Int32. Changed only included `checkedQuantity`/`carryOnQuantity` bound to Int32.MaxValue; selected service quantity and maximum purchasable quantity retain1..99 guards. Full catalog fixtures prove892/Int32.MaxValue accepted, each informational field's2147483648 rejected, and purchased quantity100 still rejected. RED2failed/192passed before correction; GREEN `nx test api-client`194passed/11files after correction. Biome safe import ordering applied and owned checks passed. This changes readonly contract compatibility, not purchase or financial authority.

## Итог source реализации 2026-10-12

User «го» authorizes source/tests. Managed worktree remains detached at the exact fetched base163e3d9b467259518a11e6c6c423aa0e3f79a513; no stage/commit/push/PR/CI dispatch/sync/archive/deploy. Main dev is checked separately at handoff. Pinned tooling/config/lock/skills unchanged; existing locked ignored Node dependencies were copied without installation/prepare scripts. OpenSpec runtime1.14.0 was copied only after the separate approved digest check.

Реализованы nullable/scoped supplier evidence, exact fee currency, brand/code separation и common informational baggage semantics; snapshot freezes before both quote commits, replay copies stored versions/hash/time, incomplete replacement clears it. Cache/keyv5 and semantic dedup exclude observation/render metadata. Existing quote exposes Current/Stale/Unavailable and optional organized mode after successful commit; authoritative stream version and time rechecked before presenting an AI plan. Existing owner creation reader adds Historical/Stale or Unavailable, without provider/AI/decrypt/outbox. No new public summary GET.

AI default-off uses same SDK/IChatClient, leafv1, separate fixed prompt, allowlisted fact IDs, no raw labels/identity/profile data. One publication/call, feature-specific no-retry Wolverine+SDK,4s optional budget/12s handler remainder/offerexpiry/firstdeparture and3.5s model cap,2048 conservative input/512output/16KiB/USD0.10 verified-price gate; per-process1inflight/6admissions per minute. Model and ledger dependencies that ignore cancellation do not hold reply beyond deadline; an unsettled task keeps admission Busy until recovery/settlement, preventing overlap. Timeout/unknown usage is not unbilled proof or a fabricated zero ledger entry. Known usage is accounted before output rejection.

Safe startup audit was extended to pure Contract and Architecture projects: no linked key fixture, ModuleInitializer, Program invocation, host/service start, provider/model client connection or database. Architecture uses reflection/IL and its retained read-only MSBuild evaluations. Full Flights.Unit/Travel.AI.Tests/HostIntegration runtime were NOT run locally. Source solution build only compiles their original fixture paths; build is not fixture execution. No migrations were generated/applied locally.

| Проверка | Свежий результат | Evidence log / source |
|---|---|---|
| Safe Flights runner, new FareRules tests |92passed/0failed/0skipped | out/fare-rules-safe-tests/final-green.log |
| Safe AI runner, fake client/ledger/HTTP only |109passed/0failed/0skipped | out/fare-ai-safe-tests/final-green.log |
| Contract, audited pure project |13passed | out/contract-tests.log |
| Architecture, audited read-only project |172passed | out/architecture-tests.log |
| Full solution source build |0errors;27retained AD0001 RouteHandlerAnalyzer warnings | out/solution-source-build.log |
| API client unit target |194passed,11files | separate UI receipt above |
| Angular unit target after review fixes |450passed,36files | out/web-final-tests.log |
| Production and demo Angular builds |success; existing4.50KiB SCSS warning | separate UI receipt above |
| Full existing test:flights-demo target |58Playwright passed, Chromium1worker/0retries | out/node-demo-final.log |
| Node demo unit runner |65passed for fare/search modules | separate UI receipt above |
| AI harness |133passed | out/harness-check.log |
| Biome CI, blocking diagnostics |0errors,252files; existing warning/info diagnostics not waived | out/biome-errors.log |
| Pinned OpenSpec all strict |5passed/0failed, newchange issues[]; old cancellation5INFO unchanged | out/openspec-final.log |

CSharpier1.2.6 formatted changed C# paths only; final check and git diff/link checks are recorded at final handoff. The generated nullable explanationMode OpenAPI field was copied from actual DTO schema via public offline document transformer, without starting Host/application/HTTP. Other snapshot schema/reference structure was preserved. Actual Host OpenAPI and new production pipeline runtime are CI-only.

48fixed cases are32tuning/16holdout,12declared eligible mixed holdout+4ineligible; bothparts include9adults/4legs. Actual immutable evidence, formatter catalog and prompt/schema payload estimator are tested, not just hand-written eligibility labels.16adversarial outputs reject free text/altered/omitted/duplicated claims. Source scorer counts eligible fallback/timeout/skipped/missing outcome as failure, excludes ineligible from model numerator, requires two independent blind Russian readers and whole-optional-stage timing. Direct extractor timing cannot enable the feature. Paid runner has no xUnit entry; explicit authorization flag+separate token+key+verified exact model/price metadata are all required. No paid or human evaluation occurred; modelUNSELECTED/priceUNVERIFIED remain true.

Independent source review closed UI silent-fallback, duplicate root conditions, mandatory duplicate strictness, informational ancillary bounds and missing production composition/EF test source. Additional self-review fixed noncooperative model/ledger deadlines, single-inflight including accounting, unsupported historical9adult metadata/extras notices, and retained v4 test expectations. No open important source finding remains after final delta check; review receipt is in review.md.

Canonical traceability: scenario-matrix.md maps all44scenarios; contract-examples.md explains source/current/history/defaults and partial OpenAPI proof; demo.md maps UI/browser cases; supplier-cache-compatibility.md records cache/baggage/legacy compatibility; packet README retains D7 and separate authorization. All20source/local tasks can close only after final review/check;4.3/5.4/5.5/7.3 remain open for mandatory CI/HTTP/persistence/transport/ledger/PR E2E acceptance. Their source is written and compiled, execution unverified.

Tests on fixtures mean the browser used a local test API with prepared responses; they are not calls to Duffel sandbox. No supplier/payment/Anthropic API, credentials, model-quality/usefulness, actual release/asset compatibility, installed environment or full-M3 acceptance was verified. Next gate is separately authorized publication to a PR and ordinary CI/PR E2E; model/human/live/merge/deploy remain separate.

Final check receipts: CSharpier1.2.6 checked66changed C# files; git diff--check exit0; strict OpenSpec all5passed; API-client194/web450/full browser58 success;92safe Flights+109safe AI+13Contract+172Architecture success. Main dev is clean, HEAD and cached origin/dev remain163e3d9b467259518a11e6c6c423aa0e3f79a513; managedworktree HEAD is the same exact base and ancestry check succeeds. No diff in .agents/.codex/tooling/locks/Directory.Packages.props/Host source/auth/CI/migrations. No publication, real API or paid evaluation. Final independent delta recheck Ready,0openP1/P2,20/24tasks closed;4CI/runtime tasks open.

## Authorized publication and CI round1

User explicitly authorized commit/push/merge on2026-10-12. Branch codex/flights-m3-fare-rules-summary was created from the exact fetched base163e3d9b; source commit705d9cb3a6765debead9fc8cfe2b3327ac63d5ff published in PR44. Normal hooks were retained; pre-push Architecture172passed. README13passed and inventory62passed/1existing platform skip; ignored safe-project csproj files were preserved in an isolated TEMP directory to keep the real repository inventory unchanged. Logs remain in ignored out.

PR CI38249937227 on source705d9cb3: build-dotnet succeeded; lint failed because full CSharpier also formats csproj while local/hook verification had targeted cs files. The same full command reproduced the failure; three touched feature csproj files were formatted and full CSharpier check840files passed. No behavior, tooling, auth or CI rule changed. Downstream skipped jobs and earlier local passes are not PR acceptance. Merge remains gated on a fresh fully green PR head, including PR E2E. Real provider/model/live/enablement gates remain unexecuted.

## PR CI round2 and regression fixes

CI38250560707, exact source467758ea9616c46420e6ead8cbd7944f3b59acf3: lint/build/frontend/AI/Identity/Contract/Architecture/HostHTTP/FlightsIntegration/AspireSmoke succeeded. Flights.Unit1480/1481: only legacy null-baggage expected full-provider failure conflicted with the approved tolerant informational contract. Updated scenario now requires usable full inventory, precise scoped unknown allowances, invalid-data notice and2adult/4leg metadata; mandatory-invalid cases remain unchanged. Its stronger full-provider fake test found a null-array-member invalid flag gap; non-object included-baggage array members now carry InvalidData=true. RED reproduced and expanded safe runner113tests passed.

HostIntegration57/58: production Host+twoAI Programs+real EF ledger and one-call/no-retry cases passed. Only the supplementary source-chain assertion failed on CSharpier line breaks. It now strips whitespace before checking the same exact published/listener/group/scoped-policy chains; no runtime assertion was removed. Source build passed with retained analyzer warnings. PR E2E skipped due failed prerequisites and is NOT accepted. Next exact head must pass all ordinary jobs including E2E before merge.
