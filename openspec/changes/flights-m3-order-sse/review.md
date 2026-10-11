# Review log

## Self-review — 2026-10-11

Checked scope, existing capability invariants, exact base, all four read surfaces, commit/readiness path, privacy, recovery budgets, compatibility and requirement-to-test coverage.

Corrections before independent review:

1. Ordinary GET does not carry checkpoint; creation GET also lacks snapshot version. Plan includes both, uses one aggregate/version snapshot and includes equal-version180s diagnosis instead of a blanket equal-version rejection.
2. Existing busy readers drop signals; plan requires a coalesced follow-up and one per-resource gate shared with workflow polling.
3. Healthy SSE can silently miss an event, and terminal status ends old polling; periodic fallback stays active for visible terminal pages.
4. Existing refresh omits refund and sequential creation failure prevents order GET; plan reads all panels independently and retains successful partial results.
5. Same-version registry coalescing can hide generic event behind legacy status event; every recognized event refreshes all panels.
6. Freshness target now includes the maximum accepted GET version, not only the last SSE hint; stale prerequisite action flags cannot bypass a newer source observation.
7. Unknown-version compatibility cannot erase versioned evidence. No Core/schema/financial changes planned. Local .NET eligibility remains an explicit startup audit, not an inferred safe filter.

Placeholder scan: no unfinished template markers. Strict OpenSpec validation passed. Independent review follows; this self-review is not the independent review.

## Independent review — round1

Reviewer: separate agent `/root/sse_plan_review`, clean context, read-only comparison against user task, current source and capability contracts. Initial verdict: revisions-needed.

| Finding | Correction in this corpus | Required regression |
|---|---|---|
| P1: historical EF checkpoint default-1 breaks decoder/list compatibility | D4 maps nonpositive source checkpoint to nullable unknown at API boundary; negative wire stays invalid; no data backfill | Task1.1/1.3 actual sentinel mapper/decoder/list fixtures |
| P2: four-panel convergence treats never-materialized order as endless lag | D4a applicability/prerequisite matrix distinguishes source-only OfferQuoted/NotCreated from delayed Held projection; rendering and action gates are separate | Task4.2 source-only cases, no perpetual barrier and transition to first materialization |
| P2: every CancellationDecisionWriter batch includes pre-hold review with no possible EF row | D1 generic emission eligibility uses source already-held status or first-held batch, never current EF existence; pre-hold negative/inconclusive stays GET fallback | Task2.2 no futile notification/DLQ before Hold; first-held notification remains eligible despite actual EF lag |

All three corrected in design, tasks and normative scenarios before focused re-review. Reviewer found no new money-policy, supplier-call, auth-boundary or platform-scope defect in the checked remainder. Round1 does not imply final approval; focused verdict follows.

## Independent review — round2, final

The same independent reviewer re-read the corrected design/tasks/spec against the three findings and returned **plan-ready**: no remaining important findings. Confirmed sentinel compatibility, stage-aware applicability/action prerequisites, source-bound notification eligibility, one-snapshot creation version and bounded four-reader coordination. No additional framework or financial scope was introduced. Review is planning readiness only, not runtime proof or implementation permission.

## Final planning checks

- Pinned OpenSpec1.14.0 strict validation passed after corrections; all4 planning artifacts exist.
- AI harness133/133 passed; exact pinned harness inventory unchanged.
-8change files, local Markdown links and trailing-whitespace scan passed.
-22implementation tasks remain unchecked;12requirements cover30scenarios.
- Only the new canonical change is untracked; no staged/tracked product changes; original dev clean; managed HEAD remains the exact fetched SHA.
- No product/.NET/Host/DB/CI/live tests were run for SSE. Implementation, publication and sync/archive remain separate authorization gates.


## Requested additional self-review — 2026-10-11

Current source and user task were re-read without delegation or product execution. The earlier independent plan-ready receipt remains evidence for the earlier reviewed corpus; the refinements below were checked in this self-review and are not claimed as a new independent approval.

| Finding | Why it matters | Correction and regression |
|---|---|---|
| P2: start always tears down previous generation; current auth refresh emits a fresh status object for unchanged identity | A reactive integration could restart on every token read, drop watermarks and cause connect/GET churn | D3 makes start idempotent for normalized identity/page key; only a changed token rotates connection. Task4.1 and a normative same-identity scenario. |
| P2: page pause alone does not govern old root workflow timers or leaf state updates; reusing mutation generation would discard admitted command completion | Hidden/offline observation could continue, or a read pause could corrupt the local view of an explicitly admitted action | D3/D5 define a shared observation gate and read-only epoch; preserve mutation generation, retained outcomes and original expiry. Task4.3 covers late leaf GET, zero paused reads, one resume and admitted-command completion. |
| P2: repeated hints can set follow-up after each completed read or reopen an exhausted fast catch-up window | A duplicate stream could keep rapid GETs running despite the stated bounded model | D5 ignores already satisfied hints and repeats of the same pending target; exhausted target remains on slower fallback until convergence. Task4.4 and two normative duplicate/budget scenarios. |
| P2: equal-version successful reads lack explicit freshness/error acknowledgement independent from snapshot replacement | Unchanged current refund data could remain falsely marked failed/pending forever | D5 read outcomes settle on every exit and acknowledge a valid equal-version success; keep existing snapshot/revision guards. Task4.3 and a normative unchanged-success scenario. |

Other coherence corrections: D5 references the preceding D4a matrix, first activation waits for applicable materialized SSE, successful independent order read may establish applicability when creation read fails, and the fictional schedule does not restart on reconnect. Proposal/spec/tasks/verification were reconciled to these refinements. No new domain state, financial rule, framework, package, schema or auth policy was introduced.

After corrections, the reviewed requirements have concrete task/test ownership. Implementation remains0/22tasks. Strict validation and final static checks are recorded below; no product tests were run.

Final checks for this additional self-review: pinned OpenSpec strict PASS;8files with valid local links/no trailing whitespace;12requirements/35scenarios;22unchecked tasks/0checked tasks. Only canonical change documents are untracked; staged and tracked diffs empty; original dev clean. No remaining blocking contradiction found in this self-review. Current refinements have self-review evidence; prior independent review remains scoped to its earlier corpus.

## Implementation self-review and independent source review — 2026-10-11

Source implementation was authorized by «го». The historical planning receipts above remain scoped to their earlier corpus.

| Finding | Concrete correction | Regression/result |
|---|---|---|
| Independent P2: page readiness did not guard child cancellation commands | Reactive readiness callback is wired to actual canPrepare/canConsent/canAbandon/canRefresh and dispatch admission. Existing execute/business/owner guards remain; admitted commands are not cancelled by observation pause. | Actual page leaf prepare/consent reject newer pending/failed hint; admitted consent survives token wait + pause. Angular passed. |
| Independent P2: equal-version delayed GET extends original terms expiry | For the same operation/terms key local expiry takes minimum of prior and observed. New operation/terms resets acceptance. |60s original expiry remains expired despite a delayed response suggesting69s; Angular RED→GREEN. |
| Additional self-review: long pause retains expired catch-up budget | Visible/online resume creates a new observation epoch, clears only old observation budget/exhaustion, and requires fresh GET. Duplicate/higher hints do not reset the budget. | Hidden200s ignores late result, performs no paused reads and resumes at most one cycle + one follow-up; Angular RED→GREEN. |

Separate read-only reviewer `/root/sse_source_review` rechecked both P2 fixes, additional backend test sources, final resume correction and post-replacement notification test additions. Final verdict: **source-review-ready**, no remaining substantial findings. Reviewer independently inspected source and did not execute runtime/tests.

Self-review additionally retained the existing diagnostic safety boundary for the new notification, separated retained versions from fresh-read epochs, bounded late auth token wait and corrected fictional browser route guards without accepting credentials/foreign origins.

Evidence limit retained from independent review: the new persisted creation test appends after the first handler returns and proves returned-snapshot stability/fresh-read advancement, not an interleaving inside Handle. The handler uses one source snapshot by inspected API contract. The signed pipeline recording outbox proves enrollment; durable transaction/rollback/restart consumer tests are separate CI-only sources.

Local results and startup safety audit are in [verification.md](verification.md). Review accepts source for the next publication/CI gate; it grants no staging, publication, merge, archive, deployment or supplier/payment authority.

## Requested solution self-review — 2026-10-11, after source review

Performed by the implementing agent without delegation. Re-read the12requirements/35scenarios, current transport/coordinator/page/leaf services, actual child action bindings, backend writers/readiness/owner endpoint and test evidence lanes. The prior independent source verdict above applies to its earlier source revision; it is not a fresh independent review of the corrections below.

| Finding | Root cause and correction | Regression proof |
|---|---|---|
| P2: actual leaf preparation bypassed an existing unknown confirmation barrier | Page `canCancel` checked `operations.blocksWrite`, but the callback guarding child actions checked only identity/freshness. A pending/unknown client confirmation could therefore leave the direct prepare button enabled, including after a route round trip with fresh Held reads. The shared leaf admission callback now also checks the existing per-order write barrier. Admitted commands keep their original completion guards. | Extended the actual page test to check both pending and unknown confirmation, hidden prepare button, false direct prepare admission and no prepare POST. Failed at `canPrepare` before the fix; passed afterward. |
| P2: an old notApplicable result survived a transition to Held inside one read cycle | Order reader could skip EF based on the old OfferQuoted snapshot while the independent creation reader advanced to Held. End-of-cycle applicability did not revoke the old skip, delaying the first projection read/subscription. The coordinator now marks that result stopped when order becomes applicable and queues one bounded follow-up for the false→true transition. SSE still waits for successful owned order GET. | New coordinator test delays the creation transition until after the order skip. Initially no stream; after Held, owned projection is read and one stream opens. Failed at materialization before the fix; passed afterward. |
| Test evidence gap: saved-profile browser guard still rejected every events route | Earlier browser success could finish before the stream opened. The corrected first-Hold transition exposed its stale unconditional assertion. Reused the existing precise fictional SSE route validator in that helper; same-origin, GET-only, nonzero UUID, no query and no Authorization restrictions stay enforced. | Full run reproduced the guard failure at `flights-saved-travelers.spec.ts:42`; affected suites and final results are recorded in verification.md. |

Other reviewed contracts: empty generic payload; commit-before-delivery and readiness; no pre-hold futile notification; owner concealment; exact version binding/sentinel compatibility; parser/frame limits; connection-only token rotation; abort/late callback isolation; fixed catch-up/backoff; independent panels; original consent expiry; money separation and zero observation-induced mutations. No additional confirmed substantial source defect found in those paths. Runtime-dependent conclusions remain pending existing CI/live lanes.

Fresh local results after corrections: full Angular435/435 and production build passed; full Biome enforcement passed. New corrections affect only page/coordinator admission/applicability, their tests and one fictional test helper. Core, backend, auth implementation, tooling, dependencies, CI policy and publication state are unchanged. No new independent-review verdict is claimed for this requested self-review.

Final browser receipt: affected profile+SSE17/17 passed, then the full fictional suite56/56 passed in2.6min. The initial55/56 guard failure is retained as diagnostic history, not attributed to runtime infrastructure or ignored as a flake. Strict OpenSpec and diff/format checks are recorded in verification.md. No confirmed substantial source finding remains open in this self-review; actual backend runtime/Host OpenAPI/CI/live acceptance remains pending.

## Publication review and CI repairs

Independent reviewer accepted published source18c7a384e45930214deef2c45e6701c96b3a42c5 before merge. CI38162914202 then exposed lint/fixture/contract assumptions, recorded in process-log.md; no CI acceptance was inferred from source review.

The reviewer re-read the correction diff: status probes remain separate from generic metadata invalidations, committed checkpoint assertions cover rollback boundaries, same-version convergence permits only OrderConfirmed/OrderChanged, and fixtures use valid long/hash data. No rules were relaxed. An additional all404 test reproduced loss of fallback with a retained checkpoint. Reviewer also found its legacy/null variant: positive version alone cannot represent known resource presence. Added owner-scoped retained-evidence callback, independently from freshness and stream eligibility. Positive/unknown/legacy cases passed RED→GREEN; legacy remains action-blocked with30s fallback and no new stream.

Final correction verdict: ready for repair commit, no remaining substantial source findings. Local uncached Angular438/438, client170/170 and ESLint passed; backend corrections compile only. The reviewer did not execute fixtures. Runtime acceptance requires the new mandatory CI and PR E2E; live proxy/supplier/payment remain separate. The only declined behaviors were actual backend runtime proof (assigned to CI) and separate deployment/provider acceptance; neither is silently waived.

Second CI run38165251104 accepted all backend jobs on cd392d3da9b887fa18c6b1faecf9abc4300d83b1, including the full Host HTTP/OpenAPI and contract checks. Frontend fictional search fixture exposed a transient projection assertion and reused deterministic order on retry. Focused independent read-only review of the test-only correction found one P2: the handoff message can precede completion of the first upstream GET. The fixture now awaits `expect.poll` proving upstream200 before releasing its404 barrier. Reviewer rechecked the correction and accepted it with no open P1/P2; repeat/retry identity, real demo proxy proof and mutation assertions remain intact. Runtime completion of the corrected browser suite and mandatory PR E2E still requires the next CI run.

Acceptance receipt: CI38166506507 on exact corrected source c2fadc7e1e4666d61f5286c3c42d57af8878efa7 passed all13 normal jobs, including clean fictional browser56/56 and mandatory real Host E2E2/2. Backend/Host/OpenAPI/contract runtime acceptance is now established for that SHA in existing CI. The independent verdict remains read-only source review; implementation execution proof is separately recorded in verification.md. Closing documentation introduces no product change; final published-head CI and normal merge/postmerge checks remain delivery gates.
