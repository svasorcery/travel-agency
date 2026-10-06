# Review и решения

**Текущий итог 2026-10-05:** план T1–T12 завершён; S1/S2 имеют self-review, original independent planning pass сохраняет свой scope. Tooling source/security/documentary focused reviews PASS, открытых P1/P2 нет. Full authenticated verifier и129 local harness/9 CLI tests PASS. T3 source/local proof получен; supplier/durable DB/delivery proof не заявляются; final documentary re-review ниже.

## Self-review, 2026-10-04

- Проверены один corpus и explicit approval boundaries; нынешние файлы — Markdown preparation, не CLI installation/config/init.
- Сверены handler/outbox/confirm/barrier/DTO/retry/idempotency факты с source; stale module itinerary limit не используется.
- Supplier документация не выдаётся за runtime guarantee; no inferred safe POST retry, no null-to-zero, no newest-candidate correlation, no order-cancelled shortcut к собственной operation success.
- Подтверждение возврата поставщика и клиентская выплата независимы; existing airline Refunded не reuse для пользовательских денег.
- Сбой после dispatch claim, lost create/confirm response и save failure покрыты отдельно; recovery не снимает unknown по 404/null/time/late error.
- Выявлена и исправлена блокировка review без безопасного закрытия: explicit abandon/expiry известного pre-consent review через CAS, race/late-consent tests. Accepted/unknown/manual не разблокируются. Добавлен requirement/scenarios и ограниченный endpoint/event в дизайне.
- Уточнён предлагаемый operator permission flights:cancellation-review, GUID sub и обычная audience validation. Scope grant/provisioning внешней Identity не подразумевается.
- Уточнён запрет client operation/key/consent в URL/history/browser storage/logs; durable server facts необходимы для recovery и отделены от browser persistence.
- Tooling alternatives включают tools none; рекомендуемый native target сохраняет exact old inventory, добавляет ровно core skill/marker inventory с independently reviewed provenance. Global delivery зависимость исключена XDG_CONFIG_HOME.
- D1–D8 остаются предложениями к совместному утверждению; отсутствие runtime OpenSpec/code/DB/paid/supplier evidence явно записано. Cost/token numbers не придуманы.

Независимый reviewer dispatch: `/root/m3_document_review`, read-only, без общего session transcript. Результат и disposition будут добавлены после ответа. Это ещё не PASS.

Промежуточные independent findings (исправлены, awaiting re-review):
- Monetary-only D2 не имел проверяемого settlement predicate: добавлены normalized classification, credit-list presence/shape, destination whitelist, explicit original CashOnly provenance и negative fixtures. Current legacy/payment evidence и guide availability caveat отмечены; число не выдаётся за cash-only.
- Wrapper терял delivery config между отдельными temp invocations: standalone config-step удалён из sequence; каждый init/update теперь seed core/skills внутри одной task-config lifetime. Global config не затрагивается.

## Независимый итог и повторное review

Reviewer `/root/m3_document_review` завершил read-only full pass и focused re-review исправлений. Итог: **ready-for-joint-design-approval**; открытых P1/P2 нет. Это не implementation, runtime или live acceptance.

| Finding | Severity | Repair | Re-review |
| --- | --- | --- | --- |
| amount/refund_to не доказывали monetary-only; mixed refund мог быть cash+credits grand total | P2 | design Monetary-only evidence predicates; spec mixed/ambiguous original-settlement scenario; tasks 3.3 fixtures; research guide availability limit | Closed |
| Temporary XDG config терял standalone delivery между wrapper commands | P2 | seed core/skills внутри каждого init/update lifetime, отдельный config command удалён | Closed |

Не найдены substantive contradictions с проверенным кодом/ADR0015–0017/0023–0025 или дырки в запрете mutation retry по timeout/lease, single-dispatch claim, confirm/cancel barrier, evidence correlation, projection lag, old route consent gate и historical readers.

Оставлены явные next gates: совместно утвердить D1–D8/tooling/version/next stage; до product code pin operator transport и проверяемый negative-unblock drain/fencing proof, unresolved confirmation policy и recovery counters/deadline, затем independent execution-plan review. После tooling approval — published-package generated hashes/inventory, repeat-init/update и CLI validation; authenticated discovery только по отдельной авторизации. Durable CI и supplier runtime proof отдельны. Reviewer ничего не запускал и не писал.

## Повторное review плана по запросу пользователя, 2026-10-04

Фокус этого прохода: исполнимость tooling/product/delivery последовательности. Исходная оценка ready-for-joint-design-approval относилась к обсуждению архитектурного направления; ниже найдены новые незакрытые plan findings. Текст design/tasks/harness plan этим проходом не исправлялся, implementation не авторизован.

### PR1 — P2: generated skills обходят предусмотренный pinned wrapper

Место: harness-integration.md:15–19, tasks.md:19. План закрепляет CLI в tools/openspec/node_modules и вызывает его через npm run openspec, но generated upstream skills используют bare openspec. Подтверждено на pinned SHA 94ca9c1eb15d1b49c06c988419b75c3d95f8b2b5: src/core/templates/workflows/apply-change.ts:61/71 вызывает openspec status и openspec instructions apply; propose/explore аналогично. Не задано, как эти команды в обычном агентском shell/root/Flights cwd находят wrapper. Без global binary они не выполнятся; при наличии стороннего global OpenSpec могут обойти pinned package/env/config. skills/list discovery не проверяет этот путь.

Исправление: выбрать один воспроизводимый механизм маршрутизации generated CLI commands к wrapper (проверяемая адаптация шаблонов с upstream+effective hashes либо task-owned shim/PATH), указать его в inventory/provenance и обновлении. Acceptance: representative generated workflow command из root и nested cwd исполняет exact pinned version и проходит wrapper env/config; чужой global binary не используется. Supplier API и product code для этого не нужны.

Источники: https://github.com/Fission-AI/OpenSpec/blob/94ca9c1eb15d1b49c06c988419b75c3d95f8b2b5/src/core/templates/workflows/apply-change.ts и аналогичные propose.ts/explore.ts. Прочитаны публичные исходники, не установлен и не выполнен CLI.

### PR2 — P2: restart discovery не имеет выбранного persistence/scheduling контракта

Место: tasks.md:27, design.md:65/83. Обещаны persisted read work и bounded periodic stale-operation scan, но не определено, как recovery work появляется атомарно с admission/dispatch claim, кто его запускает после restart, откуда scanner выбирает незавершённые операции и как конкурирующие scanners сохраняют budget. В базе BookingAggregateProjection имеет ProjectionLifecycle.Live; BookingStreamCatalog перечисляет все booking streams по первому событию и не содержит operation phase/next-check index. Поэтому простого запроса активных operations в текущей модели нет. Реализация вынуждена выбрать полный replay, новую проекцию/схему либо иную durable scheduling модель, что меняет объём и proof.

Исправление: до Application code выбрать конкретную схему — например, атомарно записываемый durable recovery message на каждом опасном переходе, с определёнными redelivery/counter/deadline правилами; либо явно спроектированный active-operation index/scan. Указать источник, restart trigger, bounded pagination/fairness и CAS/dedup. Acceptance в существующем CI: kill после commit claim до HTTP/до следующей schedule, потеря/повторная доставка и два recovery workers; операция обнаруживается без клиентского GET и без нового POST, общий budget не продлевается.

### PR3 — P2: archive/sync changes остаются вне описанного delivery cycle

Место: tasks.md:40–42. Пункт 5.2 завершает merge/postmerge, а 5.3 только после него создаёт canonical specs через sync и перемещает change в archive, затем допускает уборку. Эти операции меняют versioned files, но повторный review/commit/PR/checks/merge и проверка clean state перед cleanup отсутствуют. При буквальном исполнении merged dev не содержит итогового canonical OpenSpec состояния, а собственный worktree всё ещё нужен для локального diff.

Исправление: либо перенести проверенные sync/archive в финальный PR до exact-head checks/merge, либо явно добавить docs-only publication cycle после product merge; cleanup только после интеграции всех собственных итоговых файлов и clean/unpublished-commit проверки. Нового разрешения на публикацию этот review не выдаёт.

### Оставшиеся известные pre-code решения (не выдаются за новые findings)

D1–D8 ещё не утверждены. Operator transport и проверяемое прекращение возможности старого worker отправить запрос, lifecycle/read/manual handling нового confirmation barrier, recovery budget/count/deadline и exact execution breakdown уже отмечены как gates в предыдущем review. Их нельзя отложить на середину implementation или считать закрытыми общим согласием с архитектурой. Особо нужен сценарий crash после ConfirmationStarted до wallet/provider effect: cancellation operation ещё может не существовать, но владелец должен видеть причину блокировки и предусмотренный способ дальнейшей проверки. Автоматическая payment recovery по-прежнему вне scope.

Итог текущего прохода: **нужна доработка плана перед исполнением**. Три новых P2 открыты. Основное направление — один booking stream, consent-bound operation, read-only recovery и явная unknown/manual граница — сохраняется. Ни code/tooling/CI edits, ни tests/Host/DB/CLI/supplier calls не выполнялись; review основан на текущих документах, repository source и pinned upstream source.

## Исправления PR1–PR3 по команде пользователя «исправляй»

Статус: внесены в документацию, ждут независимого re-review; runtime implementation/proof отсутствуют.

- PR1: выбран deterministic upstream→effective skill adapter, все command sites ведут в absolute pinned repo launcher, включая root/nested cwd и invocation metadata; manifest хранит upstream+adapter+effective provenance. Staged regeneration и test fake global executable исключают PATH fallback. Authenticated acceptance теперь проверяет actual read command, не только discovery.
- PR2: scan удалён. Admission сохраняет executor+watchdog; каждый possible-dispatch claim атомарно сохраняет 4 fixed-slot observation envelopes и deadline. CAS slot budget/read-window/overdue coalescing, restart/DLQ/late evidence правила и ранний CI proof конкретизированы. Existing Wolverine 6.17.0 ScheduledTime/Enroll прочитаны в локальных XML; новый transactional scheduled-outbox proof ещё не выполнялся.
- PR3: выбран отдельный docs-only closure PR после product acceptance. Штатный archive синхронизирует current specs один раз, затем отдельные review/mandatory checks/merge/postmerge и clean/unpublished-state проверка перед cleanup. Delivery protocol записан без implementation checkboxes, чтобы archive не зависел от собственной ещё не выполненной задачи и не требовал выдуманного completion.

Самопроверка уточнила due slot при открытом read-window: slot расходуется без GET, бюджет не растёт; late positive evidence сохраняется. Owned status показывает unresolved confirmation barrier и при отсутствии cancellation operation. Операторское resolution/negative fencing, user refresh limit и бизнес D1–D8 остаются отдельными до-code решениями, не молчаливо утверждёнными правками.

Independent re-review уточнил PR2: identity admission watchdog теперь immutable operationId/admissionId/Prepare-or-Confirm stage, с CAS по свежему revision. Изначальный revision не выключает watchdog при harmless progress; старый Prepare deadline не действует на Confirm admission. CI acceptance включает оба случая. Reviewer также подтвердил необходимость consume+skip правила active read-window; оно уже было внесено self-review и передано на перепроверку. Окончательного PASS до ответа reviewer не заявляем.

## Финальное independent re-review исправлений PR1–PR3

Reviewer `/root/m3_document_review` перечитал текущие исправления. **Все три findings Closed на plan level, открытых P1/P2/P3 нет; ready-for-joint-design-approval.**

| Finding | Исправление и проверенный результат |
| --- | --- |
| PR1 | Единый absolute pinned launcher; deterministic adapter всех CLI command sites; upstream/effective hashes; scratch generation; offline и отдельно authorized authenticated actual-command proof из root/nested cwd входят в acceptance. |
| PR2 | Admission identity/watchdog + 5 scheduled recovery envelopes atomically with claim; fixed epoch/slots/deadline, reserve-before-GET, consume+skip при активном window, overdue coalescing; fresh CAS и stage isolation; late matching success не теряется. |
| PR3 | Отдельный docs-only closure PR; только штатный archive с однократной sync; exact-head review/checks/merge/postmerge и published/clean/unpublished-state gate до cleanup. |

Структурная проверка: 9 Markdown files, 15 requirements, 29 scenarios; physical files/UTF-8/fences/local links PASS. Dependency-free inventory validator PASS: 8 instruction pairs, 8 skills, 5 agent pairs, 5 legacy commands. 90 baseline tests повторно не запускались: implementation harness не менялся. Product/CLI/DB tests и supplier calls не выполнялись.

Остаются явно до исполнения: совместное утверждение D1–D8/варианта A/tooling adaptation и разрешённого этапа; до product code — operator transport/permission, проверяемый negative-unblock sender stop, manual confirmation-barrier lifecycle, user refresh limit и final execution-breakdown review. Нынешнее «исправляй» не считается ни этим утверждением, ни разрешением установить CLI/реализовать продукт. Runtime generated-tooling, actual authenticated workflow и scheduled-outbox/restart proof относятся к последующему approved этапу.

## Завершение полного плана после approval пользователя

Пользователь подтвердил описанное направление и прямо поручил довести полноценный план без дополнительных разрешений на планирование. Избыточный повторный вопрос о разрешении признан ошибкой процесса; он не сохраняется как workflow requirement.

Закрыты последние решения: operator transport/scope/typed evidence и trust labels; negative-unblock quiescence procedure; confirmation admission/effects/receipt lifecycle; shared60s refresh admission; LegacyHeld identification/clearance; full exact-file/interface/test/task decomposition. Unknown при реально недоступной evidence теперь определённый supported outcome, не planning placeholder.

Independent `/root/m3_document_review` перечитал final E1–E6/tasks/spec. Итог: **полный план готов к исполнению по команде запуска соответствующего этапа; открытых P1/P2/P3 и нерешённых planning decisions нет**.

| Финальное замечание | Исправление | Итог |
| --- | --- | --- |
| LegacyHeld не имеет dispatch instance/attempt ID | Target=aggregate/current version; cluster-wide legacy writer drain+positive no-effects; no fabricated ID | Closed |
| Lost payment response не имеет прежнего receiptRef | Explicit OperatorVerified correlation к persisted attempt/order/Money/saved PaymentRef; known ref не заменяется | Closed |
| Operator refresh был без отдельного route/policy | Dedicated review/refresh scope, shared owner-booking limiter, no owner-endpoint bypass | Closed |
| Legacy blocker DTO подразумевал несуществующий attempt | Discriminated LegacyHeld/ConfirmationAttempt view | Closed |
| Owner/operator queries могли конкурировать за один Wolverine handler | Separate query/result/handler и routing/private metadata tests | Closed |

Self-review также уточнил validation-before-fingerprint, exact commit extension signature, review-audit fields/source, сохранение known Capture observation до supplier confirm и actual batch notification version. Event history/PII purposes/Host authority сохранены в плане.

Проверки документов: 9 Markdown files, T1–T12 по порядку, 16 requirements/34 scenarios; UTF-8/physical files/fences/local links/key existing paths PASS. Inventory validator PASS (8 instruction pairs/8 skills/5 agent pairs/5 legacy commands). Existing90 tests не повторялись, код harness не менялся. .NET/product tests/CLI/DB/supplier/paid calls не выполнялись. Reviewer работал read-only.

## Дополнительное саморевью по запросу «сделай еще саморевью плана»

Проверены transaction/dispatch/recovery boundaries, historical replay, idempotency ordering, phase closure и owner/operator contracts. Исправлены два конкретных недочёта:

- **S1 (закрыт):** exact accepted retry со старым expectedRevision мог попасть под fresh-action guard; identity старой операции могла потеряться при новом prepare. Теперь auth/target binding → stable receipt/fingerprint lookup → только потом new-action revision/expiry guards. Operation/receipt dictionary восстанавливается из stream; historical response явно отличён от current operation. Не создаются повторные effects/notifications.
- **S2 (закрыт):** UnsupportedTerms от достоверно завершённой подготовки мог навсегда удерживать review, а refresh до claim не имел определённого epoch. Safe abandonment разрешён только при positive completed pending-quote evidence/no consent/no active sender; malformed/lost create остаётся unknown. Refresh matrix до claim/на завершённых terms использует локальный snapshot; supplier reads только при реальном сохранённом epoch.

Дополнительно уточнены OfferHeldV3→coordination-marker ordering и защита UI от late old confirm success/historical replay. Добавлены named tests в T4/T5/T8/T10 и4 spec scenarios; решения/исполнение tooling не менялись.

Проверки: 9 Markdown, T1–T12,17 requirements/38 scenarios; encoding/fences/local links/scenario structure PASS; exact harness validator PASS. Tracked/staged implementation diff отсутствует; tools/openspec/config/installation не созданы. Это self-review/doc consistency evidence, не runtime проверка будущего поведения.

## T1/T2 implementation review

Independent `/root/openspec_tooling_review` (read-only source) провёл initial/full/focused re-review. Найденные2P1/4P2 Closed: leading global setup bypass; write-capable read flags; runtime tamper with unchanged version/lock; conflicting target children/metadata; inventory file-vs-directory; fail-open project YAML regex. Каждое исправление подкреплено регрессией; negative cases observed RED→GREEN.

Дополнительный runtime defect public init ESM-cycle исправлен после actual failure и subprocess RED→GREEN; reviewer подтвердил final delta. Final source review PASS, no open P1/P2. Actual read-only prepared-scope/no-write тест добавлен отдельно от mock tests. Reviewer тесты/CLI/сетевые вызовы не выполнял.

Local source/CLI evidence не объявляется authenticated runtime acceptance. Auto-review отклонил передачу контекста внешнему Codex; конкретное согласие запрошено, live gate пока открыт. Product cancellation остаётся unsupported до HTTP.
## Native continuation и отклонённый permission prototype

Пользователь явно разрешил передачу контекста и runtime check. First actual run поймал raw8.3/physical-root mismatch; targeted RED→GREEN, physicalTarget consistently used, independent review PASS. Safe failure diagnostics и explicit per-run model binding reviewed PASS; configured6.1/cached5.6 catalog discrepancy не скрыта default fallback.

Native Node status repeatedly declined by local policy. Own-clone trust parity не помогла; изменение удалено. On-request bounded-approval prototype был только source/mock draft: independent reviewer нашёл P1 wrapper/profile authorization, P2 missing accepted-item binding/decision и незакрытый command-sandbox contract. Prototype целиком удалён до any real approval; все его findings Closed by removal, не объявлены исправленным/live-проверенным механизмом. Default approvalPolicy never, original unknown-ID/protocol rejection и read-only guard восстановлены. Пользовательская execpolicy не менялась.

Полный T2 native exit не достигнут. Требуется compatible read-only permission contract/environment. Product code/testing/delivery не начаты. Материалы остаются в одном canonical комплекте; затраты/отрицательные результаты не исключаются из s03a02.
Final independent review отдельно нашёл активный P2 в прежнем evidence-only matcher: arbitrary pwsh/bash basename+exit0+matching JSON мог дать false proof. Negative observed RED; wrapper unwrapping removed, unsupported wrappers now failclosed; full122 harness GREEN. Не вводилась authorization exception. Runtime acceptance требует verified wrapper provenance или direct executable record и разрешённого execution; текущее Windows wrapped/declined состояние не принимается. Prototype removal не использован для скрытия этого самостоятельного finding.

## Decoder self-review и независимое review, 2026-10-05

После паузы HEAD31a3803a/detached и сохранённый scope подтверждены. Self-review: actual command ранее completed exit0, но JSON-escaped display не распознавался; canonical decode исправляет representation, не execution authority. Observed RED reproducer и GREEN90 snapshot/verifier tests; full129 harness и9 actual CLI integration PASS. Временная raw command diagnostic удалена, retained diagnostics ограничены счётчиками/booleans.

Independent `/root/openspec_tooling_review`: narrow delta PASS, no P1/P2. Reviewer подтвердил exact JSON round-trip, прежние executable/NoProfile/argv/cwd checks и отсутствие новых shell полномочий; файлы/тесты/API не запускал и не менял. Actual OpenSpec command/provenance/no-write proof прошёл отдельно. Полный Travel verifier затем получил180s explore-domain timeout; самостоятельный source review не заменяет этот незавершённый native gate.

## Strict default и budget review, 2026-10-05

Independent documentary focused re-review: stale unmarked T2 status-policy P2 Closed/PASS; remaining gate и частичная evidence согласованы. Последующий source/security focused pass подтвердил Windows default never/readOnly/networkfalse с отсутствующим approval callback, separate explicit opt-in authority и exact immutable/provenance checks. Оригинальные literal prompts/validators сохранены; только bounded timeout180→300s и stage logging изменены. PASS, no new P1/P2. Controls не поддержали profile hypothesis; увеличенный timeout не выдаётся за установленную/исправленную первопричину. Full native replay ещё требуется.

## Final observed acceptance and self-review, 2026-10-05

Full strict-default native command completed exit0, including actual command/provenance/no-write and both original literal responses under bounded300s. Source129/129, actual CLI9/9, exact inventory and strict schema evidence retained. No fallback/new grant/permanent policy/config/product/publication change used for PASS.

Self-review identified the literal map's stale one-way/return claim conflicting with Accepted ADR0025. Root/Flights descriptions corrected to actual M2.5, architecture/authority/auth/historical-reader rules retained. This does not independently validate every map assertion. Native snapshot named separately from later fact/evidence-only edits; final documentary re-review/static checks do not manufacture a second native run on the post-ledger digest.

## Final focused independent documentary verdict

2026-10-05, reviewer /root/openspec_tooling_review: PASS, no P1/P2 or actionable contradictions in the final documentary delta. Reviewer read current T2 headers/checklist/Exit, native acceptance and its evidence limits, root/Flights descriptions and Accepted ADR0025. T1–T2 tooling is closed separately from product; passing native digest is distinct from later ledger/description edits. Architecture/auth/authority rules are unchanged. Native success is the implementer's observed run; reviewer did not rerun CLI/models/tests or mutate files.

Final local command completed exit0 after the closure edits: check:ai-harness (129 tests plus exact inventory), strict change validation, read-only instructions apply (ready,9/53 complete,44 remaining), git diff --check and affected Biome (19 files,46 informational style notes, no errors/warnings). Separate actual CLI integration remains9/9;138 distinct local tests. Documentary append records that already observed verdict, without changing verified tooling code or pretending to rerun native on the final ledger bytes.

T4 source/domain review completed2026-10-05: independent reviewer read-only PASS after eight concrete findings and RED/GREEN regression fixes (capture/clock/read binding/bounds/pending refresh/provenance/stage uncertainty/replay ordering). Author observed219 GREEN before final existing regression expansion,319 GREEN afterwards. Reviewer did not run .NET, Host, DB, keys or API. Typed event/replay/projector source exit accepted; actual outbox/concurrency/new-process FetchForWriting proof remains T6–T9 in existing CI. No tooling/product/external permission is created by this review.

### T6/T7 and T8 independent source re-review (2026-10-05)

PASS in inspected scope, no open P1/P2. Reviewer `/root/m3_document_review` inspected source only and wrote no files or executed Host/DB/keys/API. T6/T7 repairs: final financial comparison, all inspected order-fact paths, post-final-read payment continuation fence, persisted absolute read budget. T8 repairs: blocker selection, exact enum names, manual pending read202. Author runtime verification:451/451 isolated cases; Integration source compilation0warnings/errors. Remaining runtime/discovery/outbox/restart/concurrency/TS/UI gates stay open. Source review does not replace them.

## Whole-change source review and final fix pass, 2026-10-06

Reviewer `/root/m3_whole_change_review` inspected the entire uncommitted tracked/untracked product change read-only. Initial result: no P1, four P2. Source fixes and final focused review closed all four:

1. Legacy cancel tests now expect TermsRequired/no effects; terminal owned replay stays compatible. Webhook race no longer waits for a nonexistent cancel commit. Projection convergence seeds the coordination marker, observes final version10 and uses the recorded attempt wallet key. Its injected reconcile fault selects one envelope only in confirmation/projection windows; the historical webhook window retains its original path and never waits for a nonexistent ConfirmationAttempt.
2. Claim-before-send restart injection disables the first host's durability agent explicitly and verifies four distinct persisted observation identities before stop. Host B consumes stored work without owner GET/republish. Source compilation is not a runtime restart PASS.
3. Required CI-only sources cover lost final slot before GET/deadline, competing confirm/cancel admission in both directions and positive completion from the still-live original worker after manual escalation. Missing quiescence cannot authorize negative clearance.
4. Required financialSource survives backend owner DTO, strict TS decoder and UI independently from outcome resolutionSource. The watched regression proves OperatorVerified terms remain labeled after later SupplierApi success; separate before/after-consent labels are asserted.

Final reviewer verdict: all four P2 closed in source-review scope, no open P1/P2. The reviewer wrote no files and ran no tests/Host/DB/supplier commands. Actual evidence belongs to the implementer and is mapped in [verification](verification.md).

Final self-review additionally identified missing response metadata: actual Wolverine discovery initially documented generic IResult rather than CancellationStatusResponse. Observed one runtime RED; explicit200/202 schemas repaired it. New metadata and four historical profile/search regressions then passed5/5; full safe verifier458/458. Profile/search checks still compare their actual route and complete referenced contract schemas, rather than assuming the shared fixture contains no other feature. Full Host snapshot was not manufactured/accepted locally; actual existing-CI received artifact and acceptance remain open.

The metadata delta was included in the reviewer's inspected final repair scope without additional P1/P2. Source review is not durable acceptance or a publication grant.

## Actual-CI repair review, 2026-10-06

First actual CI37442622939 exposed a P1 delivery defect beyond the previous source-only review: pinned Wolverine's default single flush does not support the reused multi-commit outbox. It also exposed a P2 admission-versus-completion rollback assertion. Reviewer /root/m3_whole_change_review established the exact framework behavior from local pinned DLL IL/XML and official pinned source identity, then reviewed the minimal repaired booking context configuration, decorator construction and separate rollback cases.

Final verdict: P1/P2 closed at source-repair level, no actionable findings in the bounded repairs; actual next CI must prove delivery. Both new-fixture corrections (coordination marker seed and final-sibling-only incoming/outgoing checks) were independently verified. Hold cipher/count/single-V3 checks remain. Snapshot independently reviewed:8addedpaths16schemas8tags, no changed historical paths/schemas. Reviewer did not execute tests/Host/DB or modify source.

This review does not turn the failed first CI into PASS and does not accept the current uncommitted repair tree before its exact-head run.

Final CI2 fixture/guard review: reviewer confirmed all five reconcile IDs, selected7/unselected1, separate late-metadata gate/checkpoint10 and teardown. One P2 was found in the second browser guard's credential handling; origin+URL credentials+Authorization now fail before dispatch. Actual browser requestfailed regression and shared URL-credential negative are added. Final reviewed scope has no open P1/P2. Full local46/46 browser cases passed in1.7m; backend fixture source compiled0warnings/errors. Actual third CI is still required; no production-code changes in this batch.

## Product-code runtime acceptance, 2026-10-06

Actual existing CI37450413774 on575baaf998679817af4b59150dc1afb2cf424ef6 passed all13normal required checks, including E2E. The repaired runtime P1 and all retained strict rollback/delivery/projection/race/refresh/manual/restart suites passed; see verification.md/process-log.md for exact counts and limits. This updates source-only repair closure with actual runtime evidence.

Final documentary freeze records the accepted code/run without merge/postmerge/archive/live supplier or payout claims. A documentation-only final PR head must pass its own unchanged CI; final post-freeze run metadata is reported outside this immutable journal.

Final independent documentary freeze review PASS: source/CI head and counts checked against stored actual CI logs; no P1/P2 or actionable contradiction. Real supplier/payout/deploy are excluded, full Delivery remains open, active change retained, and the final docs-only head receives its own unchanged CI. Reviewer did not mutate files or run native/model/tests/Host/DB. Final local harness129/129+exact inventory and strict OpenSpec passed after factual edits. No product/test/tooling/CI code diff in the freeze.