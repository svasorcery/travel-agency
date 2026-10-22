# Trips M2 independent scoped recheck

Date: 2026-10-20. Reviewer: `/root/trips_m2_plan_audit`, the original independent planning reviewer.

**Verdict: R1 closed. Remaining confirmed P1/P2/P3: 0/0/0. No new material issue was found in the correction or its integration with the reviewed M2/M1 boundaries.** This accepts the corrected planning contract only, not implementation, data, quality, CI, target or rollout.

## Snapshot and retained history

Workspace: `C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-plan/travel-agency`. HEAD remains `30cd68d892cf72994c83047a22ceff782814796f`; tracked source has no local diff. M1 source/accepted-closure basis remains the exact basis identified in [review.md](review.md), including closure commit `3b95b749107da9cdde07d9d8ca173293ab38764c`. No branch/history action or M1 corpus rewrite was performed.

Read the corrected proposal, complete design, delta specification and tasks; checked the R1 API/client/adoption/rollback changes against the rest of those documents and the actual M1 evidence already reviewed. The unchanged research hash and original review hash were verified. The initial review retains its original finding and snapshot unchanged.

| Artifact | Rechecked SHA256 |
|---|---|
| proposal.md | DAE55153B34C5731ADF8BB5AB600AF2ADA4D115C62FFDDEBD636DB5E93654B29 |
| research.md | 30A4D661CEBCCDD5112A30676D77DD075234D0C641893F5C5CFEC8960A7D79AC |
| design.md | 63BBDEEE1BF39F59A1BE653A6085001A5CCF7FD055425B8C2F39D9DB95B1EA77 |
| specs/trip-itinerary-candidates/spec.md | C5CB541F4D7F5CC365A72070F8620BA9A0D9A3F52E2A16575F48A8FC1677CE93 |
| tasks.md | 30A87B364102D86C54A422D4AFBA5E20E05F34847EA5BA196AA64836D29AB235 |
| review.md, preserved original | 03CD275A682473BD85F8DF9D39A4E774593663C3D1D0F3CAA2761B5D7C195077 |

## R1 correction assessment

- **Authoritative observable state:** `design.md:93-104` defines GET `/api/trips/itinerary-availability` separately from M1 destinations/detail. Ready exposes only bounded strict dataset identity and release validity; disabled is 404 before authentication/dependency resolution; trusted authenticated requests can return 401/403 or typed 503 without stale identity. No-store, no DB/private facts, schema bounds and independent M1 readiness are explicit. This removes the missing state source identified by R1.
- **Mandatory transfer checks:** `design.md:111` requires both owner detail and availability rereads. One adoption ticket guards session, plan, local edits/form, revision and candidate epoch; checking-adoption prevents a duplicate transfer. Both reads and the final clean-state/clock checks must pass before synchronous staging. Unchanged plan revision alone can no longer authorize transfer after package A changes to B or is withdrawn. Failure of either observation, dataset/expiry mismatch, feature-off or a local/session transition blocks copying without hidden retries or generation.
- **Bounded guarantee:** `design.md:102-104` explicitly distinguishes disabled/unavailable/stale and recognizes replica/package consistency and the race after the final observation. It does not turn availability into an enduring capability grant or final financial/supplier authority. `design.md:113-117,170-174` retains ordinary editable historical M1 intentions, no persistence extension, M1 CAS/no-retry, and rollback preserving plans/keys. A source change after staging does not retrospectively rewrite saved content.
- **Requirement and delivery coverage:** `proposal.md:13` now states the two-read gate. `specs/trip-itinerary-candidates/spec.md:135-162` adds observable availability, same-revision package switch/withdrawal, feature-off/read failure, the later-withdrawal race and edits/session changes during checks. `tasks.md:30,35-38,66` assigns actual API JSON/strict TS, component state-machine and fictional browser verification for those cases, including no stale metadata, double-click, interleaved replies and unchanged-revision withdrawal. The implementation/test seams remain consumer-specific.

The corrected flow resolves the original fresh-builder question: the builder must implement and consume the defined availability GET, require its matching fresh ready observation together with the owner revision observation, and reject staging on either failure. The former alternatives are no longer an undocumented implementation choice.

## Limits of this result

This recheck inspected planning documents and their consistency with previously reviewed actual M1 source/contracts. It did not execute or validate a new API/client implementation, tests, dataset, human route evaluation or deployed rollback. No product source, runtime data, fixture, migration, Git index/history or external runtime was changed; only this recheck artifact was written.

The existing separate data/structural/human-quality/CI/target/rollout gates remain pending as documented. The original full-plan audit plus this scoped correction recheck leave no confirmed material planning issue, while granting no implementation or publication authority.
