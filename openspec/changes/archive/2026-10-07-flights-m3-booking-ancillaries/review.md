# Change review

## Whole-solution audit requested by the user — 2026-10-07

The user explicitly requested a fresh audit after switching to Astra and authorized direct fixes
where no business choice is needed. This is the primary agent's new whole-solution audit, not a
claimed re-review by the earlier independent agent. Scope: original user requirements, the simplified
design and deltas, roadmap boundaries, production Core/Application/Infrastructure/Api, client/UI,
fictional demo, and the evidence/CI boundary.

### Assessment

The architecture fits the approved demo: baggage then optional seats in one whole-offer purchase;
one saved admission before one inline supplier create; one bounded known-ID check; lost-ID/manual
handling; cancellation-only for proven unpaid differences. These safety rules account for most of
the complexity and are required by the agreed truthful-outcome contract. No new orchestration
engine, durable inventory, general money framework, operator subsystem, schema or dependency is
needed. Existing Core ownership, provider ACL, auth and protected-party boundaries remain coherent.

The initial implementation was not ready to call defect-free despite its previous green counts.
Seven actionable issues were reproduced; all were corrected without changing the approved business
scope. No further user decision was necessary for these repairs.

| ID / priority | Reproduced defect | Repair and proof |
|---|---|---|
| AR1 / P1 | The latest service-aware order read could say cancelled or expired, yet the provider still sent /air/payments. The deadline could also expire during the awaited dispatch fence. | Recheck cancelled/deadline on the latest facts and deadline again after the fence. Three offline HTTP regressions failed with one POST before the fix and now require zero; a matching purchase still pays exactly once and returns typed paid-service proof. |
| AR2 / P2 | Catalog/final-quote requests resumed after token acquisition even if the authenticated identity epoch had changed. Response filtering happened too late to prevent a quote write. | Check captured generation and identity immediately before HTTP; both catalog and quote regressions now dispatch zero requests after an epoch change. |
| AR3 / P2 | If scheduled work was lost/dead-lettered, creation GET reported InProgress indefinitely after the fixed deadline. | At +180s an unresolved InProgress read is diagnosed as ManualReviewRequired. No event, no-effects proof, resend authority or financial action is inferred; 179/180/300s cases preserve the saved barrier. |
| AR4 / P2 | A valid empty Duffel seat name crossed the API as an empty optional string, which the strict client decoder rejects; the whole catalog became unreadable. | Normalize absent/blank presentation names to null in the ACL. Existing passenger-specific/free-seat mapping now covers both named and unnamed seats. |
| AR5 / P2 | Two different bag products for the same adult/segments were displayed as missing and unexpected even when all accepted terms matched. | Compare exact service terms before treating a scope-only match as a replacement. New IDs/order no longer fabricate differences; a same-price seat replacement remains visible. |
| AR6 / P2 | Typed operator order facts admitted negative baggage limits and malformed baggage disclosure text, producing facts the client could not read. | Validate optional limits and disclosures for every service kind at the common proof boundary. Both invalid-evidence cases refuse attachment before durable facts. |
| AR7 / P2 | After positive NotCreated, another request key could create against the same old quote, bypassing the promised explicit fresh-quote step. | Require a new quote before new admission; exact old identity still resolves its retained receipt. Regression covers refusal, allowed requote and subsequent fresh admission. |

All seven repairs have observed behavioral RED→GREEN evidence. The local fixtures were audited
again; the regular Unit assembly still initializes keys, so only the existing isolated source-linked
runner executed .NET tests. Its complete suite now has 92 passing cases. Final broader results and
the unchanged CI-only gates are recorded in verification.md.

Public-only contract cross-check used [Duffel seat maps](https://duffel.com/docs/api/v2/seat-maps/get-seat-maps),
[holding orders](https://duffel.com/docs/guides/holding-orders-and-paying-later),
[response handling](https://duffel.com/docs/api/overview/response-handling), and the
[official order types](https://raw.githubusercontent.com/duffelhq/duffel-api-javascript/main/src/booking/Orders/OrdersTypes.ts).
No supplier API was called. The seat-map sample's omitted cabin field is not treated as proof that
the documented SDK cabin_class contract is invalid; no speculative relaxation was made.

The original remaining CI/received-snapshot gates were subsequently passed in PR39 and its
postmerge run; see verification.md for exact heads/results. Separately authorized live Duffel
verification of A1 remains an availability boundary.
The historical analyzer AD0001 cause remains unestablished; a later incremental build without
warnings does not prove that it is fixed. Full customer refunds, fare-rule AI summaries and the
remaining M3 roadmap are outside this completed source slice. Broader recovery automation remains
deferred as requested.

## Earlier independent implementation review — 2026-10-07

One fresh whole-code reviewer, /root/ancillaries_code_review on gpt-6-astra, reviewed all source against
31f4a1da3571386044946240e957088e5ea21714 without edits or runtime execution. Historical planning
reviews below are not the implementation verdict. The reviewer found five P2 issues; they are
treated as important user-impact defects and repaired in the one authorized fix pass:

| Finding | Repair and verification |
|---|---|
| R1: selected quote bypassed flights:book | API checks policy; command carries trusted booking authority; handler checks fresh selections and omitted/clear refresh of a service-bearing purchase before provider access. Direct endpoint regression failed before fix; signed-JWT source regression added for existing CI. |
| R2: absent/unselectable key could not be removed | Explicit quantity=0 removes a retained choice without requiring a live catalog entry; UI renders remove controls for every saved draft position. New draft regression RED→GREEN. |
| R3: empty selection bypassed pricing-intent refusal | Empty purchase factory and ordinary Duffel refresh reject nonempty intended pricing. UI requests a different offer instead of suggesting bypass. New factory regression RED→GREEN; base-refresh provider regression added. |
| R4: operator actual services could reference foreign membership | Domain checks passenger, currency and actual itinerary membership plus unique seat/person/segment; malformed evidence never becomes terminal facts. Two new evidence regressions RED→GREEN. |
| R5: Matches/reload did not show purchased services | Owner order view shows actual seats, bags, passenger labels, coverage, quantity-inclusive line amounts and disclosures; differences are additional. New order-page regression RED→GREEN. |

The reviewer also correctly identified missing persisted CI-source scenarios: direct Marten fixtures
alone were not durable-inbox proof. Added real Wolverine restart/redelivery/DLQ source, final hold
commit rollback after one provider call in the existing outbox fixture, and atomic before-wallet
difference rollback/retry source. These compile; their actual Host/DB/key/schema runtime remains
existing-CI only. No fake-session or source compile is reported as persisted acceptance.

No additional production automation/admin subsystem, package, migration or CI/CD change was introduced.
No second reviewer pass is claimed; the covering regressions and final suites verify the repair pass.

## Planning edition before implementation — 2026-10-07

User selected the simple recommended path with more manual handling; expanded automation is deferred. This review applies to the rewritten proposal/design/specs/tasks, not the previous 2026-10-06 PASS. Product implementation is not authorized by this planning update.

### Audit findings and repairs

| Finding | Current repair | Self-review |
|---|---|---|
| A1: known order with different services had no exit | Known Created fact and service comparison separated; actual unpaid order is recorded, payment disabled, owner can consent to whole-order cancellation after completion/no financial blocker | Closed at plan level |
| A2: 120s catalog reset incompatible with group/leg selection | Temporary catalog, supplier offer expiry, draft preserved and new quote explicitly accepted | Closed |
| A3: old hold compatibility unspecified with long creation | Old 200 HeldOrderResponse only for Matches; typed 409 for differences/unknown; 15s client timeout switches to authoritative GET while operation is retained | Closed |
| A4: durable catalog/new prepare-review-hold and second booking provider overexpanded scope | Selected purchase only in stream, existing quote/hold/confirm/provider, two new HTTP operations, three new event identities and one bounded check | Closed |
| A5: G1 overblocked source on contradictory docs | A1 documented guide/changelog assumption; explicit unsupported failure; source/offline allowed after implementation approval, live evidence still separate | Closed |
| S1: a service change found by confirmation could leave UI offering confirm again | Before-money comparison update + no-effects closure atomic; current view cancellation-only. After money claim manual financial barrier retained | Closed |

The new choices deliberately accept manual handling after lost ID or crash-after-start, even when no effect actually occurred. No supplier list search, slot/epoch recovery scheduler, separate recent-attempt discovery API or new operator system is retained. No security, party, money, existing cancellation consent or positive-evidence requirements were waived.

### Light logic review after approved demo/UI additions — 2026-10-07

Пользователь разрешил добавить три небольших улучшения в план, сделать лёгкое ревью и остановиться. Добавлены reproducible presets + короткая README инструкция, compact adult/leg summary и явный selected→actual diff. Они встроены в existing tasks5.2/5.3/7.1, число checkpoints осталось25. Новые backend routes/states, recovery automation, operator UI и обязательный scenario selector не добавлены.

Проверена логическая цепочка catalog→final quote→один hold→Matches/known differences/unknown→допустимые действия. Сводка не требует новых PII reads, не удваивает multi-segment charge и не делает seat selection обязательным. Diff не считает unknown нулём, не вычитает разные currencies и не создаёт refund/replacement acceptance. Demo использует те же fixtures/HTTP shapes и остаётся изолированным от production.

Единственное уточнение прохода: purchase-unknown означает потерю supplier result/ID после server admission. Потеря только browser response при saved Matches остаётся успешным GET/reload сценарием; ради демо нельзя искусственно переводить его в manual. Это согласовано в design/tasks. Существующие проверки changed service key/removed bag сохранены.

**Итог лёгкого ревью: PASS; новых блокирующих противоречий не найдено.** Strict OpenSpec и structural/coverage/local-link audit PASS:9Markdown/23links/22requirements/46scenarios/25unchecked checkpoints. Это собственный ограниченный logic review, не новый independent/product/runtime pass. После фиксации результата работа останавливается; implementation/publication не запускались.

### Independent review of simplified core — before demo/UI additions

Reviewer /root/ancillaries_plan_review провёл новый read-only проход по упрощённой редакции и нашёл один P2: same-owner re-quote после CreationStarted мог менять QuoteBinding/маршрут до Held. Исправлены D2, spec scenario, tasks1.1/2.3 и trace: все quote writers freeze до provider read; pre-start refresh теряет CAS при победившем admission, новый refresh только после positive NotCreated/no other barriers. Это локальный guard, не новая automation feature.

Focused re-review подтвердил closure. **PASS для human design review, остаточных P1/P2 нет.** Reviewer не редактировал файлы и не выполнял tests/fixtures/API/install. Подтверждены простые границы: temporary catalog, one create/known-ID check, lost-ID manual, cancellation-only для proven unpaid differences, старый HTTP success и financial barriers.

Core audit before demo/UI additions: 9 Markdown, 23 local links, 20 requirements, 42 scenarios, 25 unchecked future checkpoints; strict OpenSpec и dependency-free inventory validator PASS. Product implementation/CI/live acceptance не следуют из этого verdict.

## Historical reviews — superseded draft, 2026-10-06

First self-review corrected same-facts revision churn, randomized ciphertext identity/keyless replay assumptions, no-create classification, wrong paths and discarded supplier wire IDs. Initial full independent review by /root/ancillaries_plan_review found two P2, then focused re-review passed for that draft:
- R1: offer/order IDs and booked price stage required explicit remapping and quantity-inclusive line totals.
- R2: old money-only manual ConfirmBooking needed service proof and compatible retained hashes.

Those useful constraints remain in the simpler edition (keyless recovery now explicitly through metadata GET; plaintext POST can depend on available keys). The later broader audit found A1–A5 above and superseded that overall assessment. Historical G1 as a writer blocker, draft counts 26 requirements/54 scenarios/46 checkpoints and full catalog/recovery architecture are not current requirements.
