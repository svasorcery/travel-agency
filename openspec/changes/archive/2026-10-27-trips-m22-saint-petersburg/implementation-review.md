# Independent implementation review

Review date:2026-10-26. Active checkout:
`D:/_Projects/_github/travel-agency/.worktrees/trips-m22-saint-petersburg`,
base `11ba5a1572e53cd901ae4ec56b77b482ec95f699`.
Reviewers performed read-only inspection; neither edited files nor ran Docker,
generation or publication. Local execution receipts remain separate.

## Runtime, contract and adoption

Independent reviewer `spb_plan_review` inspected the whole backend/HTTP/TS/Angular
runtime change against the accepted plan, v1/v2 boundary, trusted city tuples,
owner/CAS/epoch/adoption, default-off fencing and old-codec compatibility.

P2: the inner preview download list remained Moscow-only, despite the corrected
outer credit. The fix derives the list from the decoded current preview and its
exact allowlisted offer, and adds the SPb preparation profile. RED1/43 demonstrated
the failure; GREEN104/104 across all three page/component suites verifies the
complete city-specific links alongside retained M1/Rail behavior. The same
reviewer rechecked the correction and reported no new actionable findings.

## Mechanical evaluation

Before first real generation, the independent reviewer found P2: coverage checks
trusted the producer's covered/unmet fields. The harness now independently
recomputes them from the selected source POI tags, checks exact wire tags/dwell and
resource bounds. Scoped recheck closed P2 before the recorded source binding/run.
The first real12-case Q run passed without tuning or removing any denominator.

The first structural run had two fixture instantiation errors: S05 varied map
expiry while identity facts remained eligible; S06 attempted a walking lease
inconsistent with its model. Corrected inputs retain the frozen expectations:
S05 identity/release expiry before local-day end, and S06 two disconnected eligible
POI with all others explicitly closed. Independent scoped recheck found these
corrections valid. Both first failures remain retained; corrected server11/11 and
the separate actual Angular N05 make the structural denominator12.

## Data, maintenance, tooling and privacy

Independent reviewer `spb_data_audit` separately reviewed maintenance/builder,
candidate verification, city clocks, full-stack preservation/aggregation,
original-response capture, cleanup ownership and privacy export. No actionable
findings were reported. The earlier Anichkov-garden data P1 and its closure are
retained in [data-audit.md](data-audit.md).

Two subsequent tooling corrections received scoped rechecks:

- Import the pure decoder entry point and run resource-free Playwright discovery
  before provisioning, following the observed Angular-barrel JIT failure.
- Preserve separate bounded loopback readiness statuses, always close response
  bodies, reject redirects and export only known status/failure values. Existing
  readiness conditions and timeouts are retained. Immutable JSON formatter
  exclusions affect only the explicitly listed golden/corpus/binding files.

Both rechecks reported no actionable findings. Failed full-stack attempts remain
failed. The later actual two-profile run passed; its phase-bound record and source-fingerprint correction are in [source-acceptance.md](source-acceptance.md).

## Limits

No open actionable source-review findings remain at this checkpoint. Human A/B
scores remain null; no field calibration, target provisioning, real-user enablement,
PR/post-merge CI or publication acceptance is implied. See [execution.md](execution.md)
for actual local outcomes and failures.

## Final scoped capture recheck

The reviewer checked the late raw-buffer capture fix: combined byte cap, complete-stream UTF8, redaction before truncation, nonzero/allowFailure, direct raw Git fingerprint and no raw-buffer export. No actionable findings. The documented distinction between the pre-fix successful E2E and post-fix tooling tests was accepted; earlier reported hashes remain unmodified and are not treated as exact bytes.

## Pre-publication self-review and recheck

Fresh runtime review reported no actionable findings. Fresh tooling review found P2: e2e() converted Playwright exit0 to browser passed without a required completion receipt, so a skipped test could falsely pass the aggregate. The fix requires the full profile/stack/clock-bound completion receipt before success. A real test.skip invocation returns0 but is rejected; positive city receipts and negative mutations are covered. Controller23/23 and the complete stack test target passed. The same reviewer verified the production callsite and actual skip regression and closed P2 with no new actionable findings.
