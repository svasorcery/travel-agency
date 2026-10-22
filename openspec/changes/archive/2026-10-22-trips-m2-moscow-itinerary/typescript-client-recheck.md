# Task 4.1 scoped recheck — fix round1

**Spec compliance: PASS. Task quality: PASS / Approved.** The sole P2 from the original review is closed; no residual or fix-introduced findings were identified.

Scope: only the two-file correction in `task-4.1-fix1-results/review-package.md`, read once, and its saved verification evidence. Fixed base/HEAD remains `e654c76915103a82083ef2abc01c0579117c9749`. Original review `task-4.1-review.md` / `typescript-client-review.md` remains historical evidence with SHA256 `35d27d9ec156a9af8077e1f9687149adf13a4f498fdad2285c07333776ac5102`; its other checks and limitations are retained.

## P2 closure

- `shared/ts/api-client/src/trips-itinerary.decoder.ts:118-119`: required and optional word lists now remove zero-length tokens. An absent optional list contributes no allowed property, so an unknown empty-name property fails the existing exact-schema key check at every ordinary root/nested schema. The generic JSON parser remains unchanged and can still parse legal empty JSON names.
- `shared/ts/api-client/src/trips-itinerary.decoder.spec.ts:389`: raw-byte negatives cover ready plus all four candidate outcome fixture roots, nine nested candidate sites and a known opening interval. They inject an empty-name unvalidated subtree. Nested string replacement markers are checked; the known-hours fixture is positively decoded before mutation.
- The final positive control preserves generic JSON empty-name parsing and RFC7807 optional traceId both present and absent; an unknown empty-name problem field remains rejected. The fix changes schema key-list construction only and introduces no production parser, transport or wire-contract workaround.

## Checked evidence and identity

Independently hashed the frozen review package, updated implementation report and two changed source files; all match the requested identities:

- review package: `e83c7512d3e110f00b696c92817fcc62189c193552c60c3196c62f4613b1ed73`
- implementation report: `11cf321a084046be78ccac819d857130583dd21577aef6cb7134a9e50e9a9b84`
- decoder: `d5b180da9b511d2201ab67ec7acd2d3abb403432474597ec0902dce83affdb88`
- decoder tests: `02aa319b4b09dc94f0a1cd70a90b99b62b9fa319d32f5ecd6b808dbb6d2604e4`

Read `recheck-hashes.json` and the saved raw log results under `task-4.1-fix1-results`:

- `red-empty-key.log:93-143`:15 meaningful failures /70passes. Every new negative failed because decoding returned normally, not because setup or fixture loading failed.
- `green-focused.log:4-7`:85/85passed in1file.
- `api-client-full.log:91-101`:85decoder tests;356/356passed in17files; Nx success.
- `web-full.log:140-147`:614/614passed in52files; Nx success.
- Those completed totals show no failed/skipped tests. Original340/614receipts remain historical evidence and are not replaced.
- `tsc.log` is present and empty, consistent with the implementation receipt's strict production compile exit0; an empty log alone does not independently encode exit status.
- `biome.log` ends with two checked files and191infos, no warnings/errors, consistent with reported exit0. This differs in scope from the original six-file192infos receipt. Existing index-signature style constraints and NO_COLOR/FORCE_COLOR process warnings remain acknowledged; no unrelated root change is needed.

Unchanged service/types/exports/Angular tests/interface, six frozen actual-wire fixtures and M1 status are bound by the controller's before/final hash verification and scope receipt. No suites or focused execution were rerun: the narrow code correction and meaningful RED/GREEN evidence resolve the previous concrete doubt.

## Acceptance boundary

This closes task4.1's sole review defect and approves its source/local specification and quality gate. It does not close task4.2 retained-plan/current-clock/package checks, downstream UI adoption, Q_demo/Q_live, CI or target/live acceptance. Pure-decoder limits from the original review remain in force. No broad reaudit, Git calls, source edits, subagents, Host/DB/network operations or Q outputs occurred; only these requested recheck reports were written.
