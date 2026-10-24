# Trips M1 independent implementation review

2026-10-20. User authorized source implementation in a separate worktree from fresh dev, independent review and corrections. This document records code review, separately from the earlier planning reviews.

## Snapshot and independence

Fresh reviewer `/root/trips_m1_implementation_review`, `fork_turns=none`, had no author/implementer history. Reviewed actual source and tests against15requirements/49scenarios, including untracked files. Base/HEAD `799e31a6b31ff256ba017861cd911e547e4e9896`, branch `codex/trips-m1-structured-planning`. Initial94-file snapshot: local `TestResults/trips-m1/independent-review-source.json`, SHA256 `28DD9288496CAF73ED36FEE76045E0F7FED0C398C7A1F551EB8D49F725AE6856`.

Reviewer checked hashes: production source matched the snapshot; five frontend/browser test files and central task progress changed during final regressions and were read in their current form. No review conclusion relies on planning approval. Reviewer made no file/Git/runtime/provider changes; verified two state-machine failures by bounded in-memory execution of actual transpiled code.

## Initial result: changes required

Six confirmed P2, no P1 or confirmed private-data disclosure:

| ID | Confirmed failure | Required correction |
|---|---|---|
| R1 | Availability tests invent a Trips-specific problem.type; real API uses about:blank plus code=Trips.Unavailable. Enabled503 becomes error/hidden navigation | Parse real typed RFC7807 extension; use exact API error JSON in regression |
| R2 | enter(B) invalidates A's operation epoch but leaves global pending=save; A's finally cannot clear it and B remains blocked | Scope/detach pending operation at plan transition; discard old response without blocking new editor |
| R3 | UI checks draft while Core checks saved baseline: new populated dates, locally cleared blocks plus city/currency change, or moved blocks plus shorter range fail on save | Baseline-aware transition guards and explicit save prerequisites; preserve local edits, no hidden multi-step autosave or weakened Core |
| R4 | POST capacity409 sets conflict barrier; list reconciliation and new draft cannot release it | Separate known capacity refusal from revision/uncertain outcomes; allow explicit retry after authoritative reconciliation |
| R5 | DateRangeWouldDropBlocks is Error.Conflict but HTTP mapping falls through to400 | Map409; exercise populated-date shrink through real HTTP and prove unchanged saved state |
| R6 | Explicitly replacing editor with server version keeps unapplied briefDraft because equality effect sees same underlying brief | Explicit reset of form draft on chosen server reconciliation, preserving ordinary edit-during-save behavior |

Parent verified actual code/wire mismatches before dispatching one bounded correction pass. Frontend owner handles R1/R2/R3/R4/R6; Infrastructure/Api owner handles R5. Earlier accepted HTTP task4.1 is reopened while R5 is unresolved. Original GREEN and diagnostic RED receipts remain distinct; new regression receipts are required.

## Boundaries

The review accepted the implemented server mechanisms subject to these corrections: owner-before-decrypt, CAS/no-op/quota, exact money, protected canonical sizing, SQL projection/pagination, default-off composition and no startup provisioning. Actual local source/disposable receipts do not establish GitHub CI, full Host/Keycloak target acceptance or rollout. All publication/Git changes remain outside scope.

The initial report required corrections. The following section records their implementation and scoped independent recheck.

## Corrections and independent scoped recheck

The original independent reviewer resumed read-only after one correction pass. All R1–R6 are closed; no new confirmed P1/P2/P3 was found in the corrections. Core transition rules, schema/protection and provider/default-off boundaries were not relaxed.

- R1: new Trips problem reader consumes the actual `code` extension; regression uses real TS HTTP transport and `type: about:blank`/`code: Trips.Unavailable`.
- R2: changing plan detaches the previous pending operation; late A cannot clear B's own pending mutation.
- R3: UI checks saved baseline, disables new-day add/move until its empty-date save, and explains block-change save prerequisites. Parameter-only reset preserves separate block/title edits; no hidden autosave.
- R4: only a confirmed creation capacity refusal can be cleared by authoritative owner-list reconciliation; explicit retry remains required, while other conflict/uncertain barriers remain.
- R5: mapping includes `Trips.DateRangeWouldDropBlocks`=409. Actual HTTP RED observed400 after no-store/snapshot invariance checks; targetedGREEN1/1 and leanGREEN37/37. Original6PostgreSQL receipts remain applicable to unchanged persistence/Core logic; no redundant Docker rerun for the HTTP-only switch case.
- R6: explicit server adoption resets component brief fields; form-only edits participate in dirty/read protection. Ordinary save A retains later form B; reverting/resetting brief does not discard separate block edits.

Reviewer independently read frontend9failing→43passing and fullAngular571/571, R5 RED→GREEN and lean37/37. Corrected source snapshot `TestResults/trips-m1/independent-review-fixed-source.json` has97entries/SHA256 `801008A84C18617A50F6EF4CD82E8BFB815AD96E97A09B423D5DB6B3D5B120DB`. Later static prerequisite help/README text and the fifth browser scenario were read in their actual current form and bound separately: template `4CA60C7CC30DEE3DA36EFDF4E882785A6B3EC90D32257A2A8E63929AE80891DB`; frontend README `707DF73712A3AB8C41B661597F23EE90746599ED1A282EFE44BF83965A56F10E`; browser spec `E21BB09AA6E5477F94D9AA593102A357A2DACA74D4A37BDEF3BD64765E10F66E`.

At the reviewer's final response the fifth browser run was pending and explicitly excluded from that verdict. Parent then independently parsed the genuine `trips-browser-reviewed.json`:5expected,0skipped/unexpected/flaky. The earlier existing Flights regression also has77expected,0skipped/unexpected/flaky. No new source behavior was added after the scoped review; final commands/counts/source binding are retained in [implementation.md](implementation.md) and local receipts. GitHub CI and target/full Host acceptance remain pending separate actions.


## Final accepted lifecycle state

This record preserves its original stage-specific evidence. PR58 was subsequently merged and post-merge CI passed; the canonical specification is synced and this completed change is archived. See [finalization.md](finalization.md) for exact final identities, current source acceptance and remaining target/M2 gates.
