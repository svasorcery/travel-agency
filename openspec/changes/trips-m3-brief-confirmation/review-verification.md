# Independent planning review and verification

Date: 2026-10-27. Scope: this proposal/design/spec/tasks/research/evaluation plan only. No implementation or model execution.

## Independent review

Reviewer: separate agent `/root/review_trips_m3_plan`, read-only, independent of artifact author. It read the planning artifacts, applicable instructions/current Trips/AI contracts and relevant source. It made no file edits.

Initial findings:

| Severity | Finding | Resolution |
| --- | --- | --- |
| P2 | Public H texts/oracles influenced lexicon design and could not honestly be called a closed holdout | D/H48 now explicitly open preregistered qualification; no generalization claim. A NEW independently authored/sealed24 after configuration freeze is a separate model-acceptance gate before rollout, with prebound thresholds and no tuning access |
| P2 | Full48 evaluation could exhaust USD0.30 rolling-hour budget and produce artificial fallbacks | Predeclared6windows×8,65minute starts,>=11second dispatch spacing,6hour overall cap; pre-admission waits excluded from request latency, unknown/insufficient budget stops without auto-resume. Later24 uses3windows/3hours |
| P3 | Server expiry with local confirmation risked trusting browser wall clock | Conservative monotonic elapsed from dispatch compared with validated server lifetime; no receipt-time reset/wall-clock authority, resume/continuity uncertainty invalidates |

Author applied these changes consistently to design, evaluation plan, tasks and delta spec. Reviewer recheck: all three findings closed; no open findings or blockers. Its conclusion concerns plan consistency only, not implementation, runtime, model quality or rollout.

## Original planning checks (before the requested self-review)

- Pinned `node tools/openspec/run.mjs validate trips-m3-brief-confirmation --strict`: exit0, `Change 'trips-m3-brief-confirmation' is valid`, after review fixes.
- CLI status: all four planning artifacts complete; this does not mean implementation tasks are complete.
- Base/current HEAD/ref: `35953fd6c8feabf16acd7fb944201711e3d8c5fa`.
- Branch: `codex/trips-m3-brief-confirmation`; worktree C:/Users/Vladimir_sva/.codex/worktrees/trips-m3-brief-proposal/travel-agency.
- `git merge-base --is-ancestor <recorded-base> HEAD`: exit0.
- `git diff --exit-code` and `git diff --cached --exit-code`: exit0; no tracked or staged edits.
- Only new untracked planning files under `openspec/changes/trips-m3-brief-confirmation/`; all23future tasks unchecked,13requirements/45scenarios.
- Original D:/_Projects/_github/travel-agency remains clean on dev, with no branch switch.

No product tests were run because there is no implementation. No commit, push, PR, merge, migration, model/API call, credential/billing access, local demo process/state edit, package activation or human-review material change. Only the pinned OpenSpec tool dependencies were restored offline in the new worktree for planning validation.

## Remaining decisions and gates

The proposed first slice is ready for user review. Exact provider/model/processor terms and allowed real execution/call count/spend remain unselected. Source implementation, real-model qualification/independent holdout, real private-text permission, target and rollout require their own authorization. Existing human A/B, field calibration, M2 original rollout6.3 and Moscow v2 remain separate.

## Original artifact SHA256 (historical snapshot)

89901217D56BDFB49C20B541962DC5DB50E8161BEB30E5C2CAE1D189E5D53EB5  .openspec.yaml
AEB061620A5DFB30AD2CC53C9AF6E2B8537F302159570C8FA7607F4219B7B3FC  design.md
E9819550973A4DC9965C24962FF9562D5D9A4F2C963E75189561C38AA2D36FDE  evaluation-plan.md
A7487A67EDCF7468984D668E055473D2D13171606363D2CF7C14F3D3ABA18501  proposal.md
C28A0F4FB5F22AFC73644ECA45DD2551C01FA0D78EF83E96197D9F701BA9D20D  research.md
89FE7E00294E4A66C8565C16ECFFAC3A25876F20FAF90A60CF7EF4D8D174AE36  tasks.md
A27EF6F9FF05BBC626A2A35D83B2353FF0093A661EFCBB01640C84F063F5F65A  specs\trip-brief-extraction\spec.md

## Requested self-review (2026-10-27)

The author performed this pass personally after the user's explicit request to self-review and fix findings. This is not a second independent-agent review; the independent review above refers to its earlier snapshot. Scope stayed within the existing eight planning files.

| Finding | Correction and verification in the plan |
| --- | --- |
| No source for the UI's pre-dispatch processor notice or feature availability; old candidates could transfer after disablement | Added a minimal authenticated/no-store <=2KiB public capability GET, with no model/bus/store/package/ledger work; versioned processor/model/privacy tuple must match at Host/AI dispatch and capability reread before confirmation. Default-off/unconfigured/unknown policy never permits an active text submit |
| Auxiliary duration had no M1 destination and could imply hidden reuse of form dates | Defined exactly five unchecked transfer choices, with dates as one group; duration remains review-only evidence. No borrowed form start or implicit overwrite of an explicit end; derivation link must be explicit and conflicts resolved |
| Retained manual defaults/origin could be mistaken for extracted or reconciled values | Require not-extracted labels, unchanged retained values and visible unsupported origin/money; added explicit Москвы alias for source-role examples. No new M1 origin/destination restriction |
| Field shape and semantic invalidity versus malformed model output were underspecified | Fixed exactly seven normalized slots with typed state/value/evidence/alternatives; stated out-of-range text becomes an invalid slot, invented extracted values/malformed schema reject the response |
| Price reservation could be applied to a missing/different actual model identity | Require matching explicitly priced returned revision, unknown cost/conservative reservation and no candidate on identity/usage failure; total-run allowance also participates in evaluation admission |
| Ten-minute UI expiry and flag edit implied stronger cleanup/revocation than the runtime provides | Distinguish logical state expiry from an uncooperative SDK retaining its own references; keep the slot/no late output. Flags are startup configuration; rollback requires separately authorized restart and capability observation, with no instant-revocation or physical-erasure claim |
| Fixed date oracles had no defined execution path compatible with live deadlines | Quality harness reuses production extractor+normalizer with fixed reference data and real runtime/admission/accounting clocks, no public HTTP clock override. Its latency is component-pipeline evidence and cannot prove live Host/NATS/browser end-to-end timing |
| Slow chunked input could spend unbounded time before a fresh inference timeout | Start the same Host5000ms budget before request-body reading and require cancellation without deadline reset |

Corrections are reflected in proposal, design, delta spec, tasks, evaluation plan and research where applicable. No corpus input/oracle row was changed; the surrounding evaluation method was clarified before any model output. Existing independent-review findings remain retained as historical evidence, not relabelled as a review of these later edits.

Self-recheck: endpoint/DTO/policy version rules, five-choice transfer, logical/dependency lifetime, model accounting and quality clocks are consistent across artifacts. No unresolved self-review findings found. This conclusion is limited to the planning documents; it does not validate implementation or model behavior.

Current checks: strict OpenSpec validation exit0;13requirements/52scenarios;23unchecked tasks and0completed;48corpus rows/48unique IDs; H23's explicit weekday oracle independently checked as Sunday. No trailing whitespace found. Recorded base remains an ancestor; HEAD is still35953fd6c8feabf16acd7fb944201711e3d8c5fa. Tracked diff/index remain empty; only this planning directory is untracked. No product/runtime/model/demo/credential/billing/publication action was performed.

## Current artifact SHA256 after self-review

89901217D56BDFB49C20B541962DC5DB50E8161BEB30E5C2CAE1D189E5D53EB5  .openspec.yaml
00037CC92B9B7BDDD3B81260B7CADCF3FF3470E39A354B5E720FC45AF63F34E6  design.md
384DD27B55EFCCBDAFC7FA056BF429C19D02B29599319CA9CD9BB3D91A548616  evaluation-plan.md
63CC8729A56EB22E8D0A92C2DBDADA59AB3BB885B9120F4427AFDEC2CA5F2016  proposal.md
9F1DD0CDAE4F4AE3B5787C1DEF734554942A1FEC03B63CACADC7355760350D15  research.md
5954ADDA8B85B1049630A297A024D5EDD20CC8E16B6FBD63B8A1CA4AF59743FD  specs\trip-brief-extraction\spec.md
990A3C410253C5AE96A8DE2381B0C51FE72C3574E5516EBDCE681EC2E380D103  tasks.md
