# S1 implementation ledger

2026-10-12. User «го дальше» after requested self-review authorizes source implementation of the reviewed S1 plan, including its necessary new module, published contract, UI/auth fixture and CI coverage source. Generated migration source/SQL will be reviewed before any generator invocation; no database apply, local Host/AppHost/Travel.AI/DB, target certificates/role grants, Laya/supplier/payment/Anthropic API, deployment or publication is inferred.

Fresh fetched `origin/dev` remains `af725448e6632708d5129971f8570c1d047f4233`; existing attached `support-s1-plan` worktree starts at this exact SHA, branch `codex/support-s1-plan`. Only canonical plan is initially untracked; no foreign edits. Pinned1.14 runtime/root/apply status verified:0/25source tasks, ready.

## Preflight interfaces and ownership

| Producer / consumer | Shared seam and verification |
|---|---|
| Core/Application → Infrastructure/Api | Domain DTOs and service/store/protector ports. Freeze public contracts before backend adapter, compile all consumers. |
| API → TS/UI/demo | Fixed wire examples below; strict decoders reject mismatched IDs/revisions/kinds. |
| Published Flights ownership → Support create | Leaf owner/order/version proof, no PII or financial facts; source owner guard covers EF lag. |
| Privacy provider → EF transaction | Envelope tests before context; persisted CI integration after schema source. |
| Operator fixture → auth session/backend policy | Dedicated client/role-gated scope, actual code-flow CI, old customer unchanged. |
| Retained receipt → UI unknown gate | Positive old receipt confirms only its action; negative/stale/forbidden lookup does not prove NoEffect. |
| Inventory/CI → Unit/Integration/Host/UI tests | New projects must execute in ordinary CI; heavy suites/keys remain CI-only. |

Workers own disjoint paths and do not commit, publish, edit other owners' files or run .NET concurrently. Root coordinates all .NET commands and records safety audits before tests. Root owns Host/Flights wiring, architecture/inventory/CI, canonical bookkeeping and final review. Code/test errors are repaired without silently waiving behavior.

All25task descriptions were read; lifecycle/guard/protection/receipt/expiry behaviors align with14requirements/38scenarios. Per-task completion is recorded only after specified proof, not from compilation or planning status.

## Rulings and test checkpoints

Ruling: D3 explicit unlinked creation uses two reserved create pre-write order-rejection types; every generic negative lookup remains unknown. This reconciles D3 with SR1 without weakening uncertain writes. Cost if wrong: incorrectly classified post-commit error could duplicate a case; zero-store-call and lost-response regressions enforce the boundary. No financial operation is introduced.

Safe .NET allowlist: only new Support Unit project/Core/Application/Flights leaf; no API/Infrastructure/Host/Program, assembly fixtures, PFX/TestRing or DB/static bootstrap. Architecture/Contract are metadata/project graph tests, no service fixture; their new consumers are reviewed before run. Integration/Host suites containing keys/DB remain CI-only. First compile-only attempts repaired missing Xunit/typed ErrorOr fake helpers; behavioral RED then executed21tests:20failed/1passed against stubs. No test-key/runtime bypass occurred.

Source Ruling: UTC microsecond normalization before immutable row/receipt construction preserves PostgreSQL retained timestamp precision and exact initial-versus-replay equality; no deadline or money rule changed. Cost if wrong: <1microsecond clock rounding; real EF and fake retention regressions cover exact receipt replay. Producer ownership proof uses the same precision.

## Source acceptance checkpoint

Root safe Unit29/29,Contract30/30,Architecture183/183,leanHTTP/Wolverine/OpenAPI43/43,API-client198/198,workerAngular85/85,Node71/71,browser67/67 andReleasecompile0errors observed. Final independent review0openCritical/Important after persistedrotationtest andcontrolledhealthfixture fixes.17/25source/localtasks checked;8 actualCI/publication-dependenttasks remain. More precisecommands/audits/scenario mapping are inverification.md/scenario-map.md. RootcopiedonlycentralOpenAPI/testdependencies intoitsownTMPstandaloneauditedproject; no parentHost/PII fixturesexecuted. Rootwrotedemo/browserfilesafterauto-reviewrefusedworkerwritingroot-ownedpaths; rejectionwasnotbypassed. Endstatecontainsapprovedsourceandcanonicalreceipts,nostage/commit/push/PR/merge/archive/sync/targetruntime.
