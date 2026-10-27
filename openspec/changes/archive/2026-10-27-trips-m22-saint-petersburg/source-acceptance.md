# Trips M2.2 source/local acceptance

Status: source/local verification and final PR/post-merge ordinary CI are accepted.
Task8.1 is complete; all27 tasks are checked. The phase-bound local evidence below
is preserved, with later immutable CI receipts in [publication.md](publication.md).
Target provisioning and real-user enablement remain separate.

Base: `11ba5a1572e53cd901ae4ec56b77b482ec95f699`.
Branch: `codex/trips-m22-saint-petersburg-d`.
Active checkout: `D:/_Projects/_github/travel-agency/.worktrees/trips-m22-saint-petersburg`.
At this original source/local checkpoint, no staging, commit, push, PR, merge or target action had been performed. The later publication authorization and pre-PR review are in [publication.md](publication.md).

## Result

Saint Petersburg has its own default-off/Test-only outdoor package: 20 POI,
62 directed paths and a bounded excluded-way profile. The existing generator,
minute model, admission/deadline, catalogue version 1 and protected M1 saving remain.
V2 is explicit on the existing routes; absence of the header preserves v1.
The UI uses the saved city, checks source/owner/revision again before local
adoption, and requires a separate ordinary Save. Kaliningrad is explicitly
unsupported for proposals and remains available for manual plans.

Moscow v1's six files, pins, URLs and expiry remain byte-identical.
Both legacy-codec city snapshots retain exact content, historical metadata,
order, money, times and protection binding. No schema migration is introduced.

## Data and quality

| Gate | Result |
| --- | --- |
| D_demo | One retained SPb download; two real seven-file builds and actual strict package-check; exact maintenance reproduction |
| Factual audit | Every identity/anchor/path/exclusion reviewed; Anichkov-garden P1 corrected and independently rechecked |
| Q_demo | First registered 12/12 real cases passed with frozen inputs, thresholds and source binding |
| Structural | 11 server cases plus actual Angular N05; all 12 passed; two initial fixture-instantiation failures retained |
| Human A/B | Not performed; scores/reviewers remain blank |
| Field calibration | Not performed |

SPb manifest:
`3417fad216eaa25f2e0f85c8abd932b080220347cf85134f9f5a763900542a4d`.
Payload:
`dd128fa989936720b2e5cd0893a658b4878c49b4be99ba6aad5c89dfe91a3d12`.
Validity ends `2026-11-24T04:08:52Z`; reading/reproduction does not renew it.
See [source dossier](source-dossier.md), [data audit](data-audit.md),
[registered Q binding](quality-run-binding.json) and
[structural results](structural-results.json).

## Local verification

| Scope | Passed |
| --- | ---: |
| Trips Core/Application Unit | 298 |
| Entire Trips Integration, including both corpus regressions | 271 |
| Architecture | 208 |
| Actual Host lifecycle | 4 |
| Actual Host OpenAPI filter (Trips + existing Rail contract) | 2 |
| Local C# clock/package tools | 20 |
| Angular state / API transport / all three page-component suites | 78 / 37 / 104 |
| Strict TS decoders, recorded real bodies and legacy-codec fixtures | 126 |
| Common offline builder | 21 |
| Maintenance | 18 |
| Trips/M1/Rail consumer browser matrix | 81 |
| Standalone Rail browser | 15 |
| Original-response Chromium regression | 13 |
| Final controller/capture/completion-gate tests | 23 |
| Dependency-free AI harness | 134 |

Repository Biome and changed-file CSharpier checks passed.
Actual Host OpenAPI declares the optional v2 header/city query on the existing
routes; conditional requirements/error outcomes are enforced by the HTTP guards,
boundary tests and documented contract. [Actual selected operations](actual-openapi.json).

## Real-service E2E

The successful ordinary two-profile invocation is retained as
[aggregate-success.json](receipts/aggregate-success.json).
Both profiles and owned cleanup passed, with one original candidate POST,
three real authorization-code/PKCE logins and zero external browser requests each.

- Moscow: 13 stages, application instant `2026-10-24T12:00:00Z`.
- Saint Petersburg: 14 stages, instant `2026-10-27T12:00:00Z`; nine-stop candidate,
  explicit edit/save, additional historical Moscow plan through real M1 endpoints.
- Both fully stopped/restarted Host, Keycloak and PostgreSQL. Fresh owner login
  compared full authoritative details, protected row/key fingerprints and UI.
  Foreign-owner and stale-write refusals passed for both plans in the SPb run.

Original receipts and failures are retained under [receipts/index.json](receipts/index.json).
The interrupted fourth attempt was inspected and cleaned up; it is not a pass.

### Source-fingerprint correction and final-tree boundary

The successful E2E invocation exercised the frozen tree whose independently
computed raw fingerprint before/after the run was
`c3badaf10bbe768d87bd8081c8b3e517b90f17c765e0315cc3b930cfb317c9f9`.

The old controller converted each stdout chunk separately. Four split UTF-8
characters corrupted its reported Git-diff fingerprint `d592006a…`.
That field is not an exact-byte fingerprint. Original receipts are unmodified;
[the separate correction](receipts/source-fingerprint-correction.json) binds
the raw fingerprint and observation times to both successful profiles.

After that run, the controller alone was repaired to preserve bounded raw
buffers, count the combined 8 MiB byte cap, decode streams completely and hash
Git bytes directly. Actual-child RED/GREEN tests cover split UTF-8, Unicode-secret
redaction, nonzero/allowFailure, both-stream cap and arbitrary raw bytes.
All 21 controller tests passed; an independent raw-Git comparison matched.
An independent scoped review found no actionable issue.

These focused tooling tests apply after the successful E2E source snapshot.
The earlier E2E is not presented as an exact-source full-stack run of the final
post-repair tree. The product/runtime and E2E assertions were unchanged by this
capture repair. Later PR/post-merge runs closed task8.1 on exact clean checkouts;
their distinct source identities and retained receipts are in [publication](publication.md).

## Review and limits

Independent data, runtime/contract/adoption and tooling/privacy reviews and
scoped rechecks have no open actionable findings; [review record](implementation-review.md).
The late source-byte correction and its verification are kept explicit.

Source acceptance does not enable a target, refresh Moscow v1, create Moscow v2,
change the existing user demo/archive, or evaluate humans. Human A/B, field
calibration, target keys/schema/provisioning and M2 rollout task 6.3 remain pending.
No real supplier/model/paid API was called. See [execution history](execution.md)
for unsuccessful attempts and measured phase-specific receipts.


## Pre-publication self-review

A fresh review found P2: Playwright exit0 alone could accept a skipped full-stack test. The runner now requires an owned stack/city/clock-bound completion receipt with all13/14 passed stages, one candidate POST, three PKCE logins, zero external requests, full restart and protected-state hashes before marking browser success. A real skipped Playwright invocation proves exit0 is rejected; both recorded positive profiles and malformed/incomplete receipts are tested. All23 controller tests and the complete test:trips-stack target (including13 Chromium regressions) passed. Independent scoped recheck closed P2. Both profiles subsequently passed final PR and post-merge CI; see [publication](publication.md).
