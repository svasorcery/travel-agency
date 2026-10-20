# Trips M1: source/local verification

Date: 2026-10-20. Source implementation, authorized local checks, independent code review and corrections are complete. Ordinary GitHub CI, full Host/Keycloak target acceptance and rollout are not claimed. OpenSpec progress is20/21: task6.2 stays open because its ordinary CI part requires publication that was not requested.

## Identity

- Worktree: `C:/Users/Vladimir_sva/.codex/worktrees/trips-m1/travel-agency`.
- Branch: `codex/trips-m1-structured-planning`.
- Freshly fetched dev/base and current HEAD: `799e31a6b31ff256ba017861cd911e547e4e9896`. HEAD remains the base because no commit was requested; implementation is the unstaged/untracked working-tree diff. Requested ancestry was verified before branch creation and again at completion.
- Final82code-file manifest: local `TestResults/trips-m1/final-source.json`; content fingerprint `D3B17CAB76B62C03DE72C3ABF93CC81A81CDD6FB54D5B9CDEDE411CEAB620462`, manifest-file SHA256 `726A1B8F46D4DB0689E126E9DD9E4D4B01B53E75509E329D6111C698D53DBE2A`. Documentation/checkbox updates are excluded from the code fingerprint to avoid recursive receipts.
- Original Rail checkout was never used for implementation or branch switching. Demo ports4201/5100 were used only for the owned existing Flights regression and released; subsequent Rail lane was respected. Trips browser used4203 and stopped its own server.

## Verified behavior

Structured single-city briefs, exact optional user estimates, bounded day/block editing, owner-private persistence, destructive-transition prerequisites and optimistic revision checks follow the [capability delta](specs/trip-plan-workspace/spec.md). Data is protected before storage; reads guard ownership before decryption; no automatic mutation retries or startup schema/key provisioning. Trips defaults off, has no model/provider/booking/payment effects, and does not depend on unfinished Rail code.

M1 remains the manual foundation. The first generated-route scenario is M2, with separate source-data, rights/freshness, model and human-quality decisions; M3 adds text extraction into the same brief contract. See [design](design.md), [module guide](../../../modules/trips/README.md) and [operations](../../../modules/trips/operations.md).

## Local acceptance matrix

| Check | Final result | Receipt/scope |
|---|---|---|
| Core/Application unit |53/53passed,0skipped | `trips-parent-final-unit.trx`; parent rerun after final whitespace-only formatting |
| Real Wolverine HTTP/codec/protection |37/37passed,0skipped | `trips-parent-final-lean.trx`; includes R5 correction and actual emitted-log/span sentinels |
| Owned disposable PostgreSQL |6/6passed,0skipped | `trips-final-all.trx` total42=36then-lean+6relational; only HTTP status mapping changed afterwards, with new targeted/lean proof |
| Architecture |182/182passed,0skipped | `trips-architecture-green.trx`; original baseline183 and RED181/1 retained |
| Whole Angular unit suite |571/571passed | `frontend/web-unit-reviewed.log` |
| Whole shared TS client suite |255/255passed | parent final run;22Trips decoder/vector tests among15files |
| Corrected frontend regressions |43/43passed after9observed failures | `frontend/fixes-red.log`, `fixes-targeted-green.log` |
| Trips fictional browser |5/5expected,0skipped/unexpected/flaky | genuine `frontend/trips-browser-reviewed.json`; includes first-save sequencing,129072-byte/112-block plan, stale GET, uncertainty and storage/SSR assertions |
| Existing Flights fictional regression |77/77expected,0skipped/unexpected/flaky | genuine `frontend/flights-browser.json`; retained unchanged allowlists |
| Production Angular build |passed | `frontend/production-build-reviewed.log`; initial379.22kB, Trips lazy67.30kB; no changed budgets or Trips budget warning |
| Actual Host compile |passed,0warnings/errors | no Host startup or external dependency execution |
| CSharpier |51changed-scope files checked | no formatting errors |
| Biome CI whole worktree |333files checked,0errors | normal configured warning/info rules preserved; `--diagnostic-level=error` limits display, not rules |
| AI harness |134/134passed | dependency-free checks only; authenticated verifier not invoked |
| README catalog/tests |13/13passed | no live Host smoke |
| CI inventory |62passed/1platform skip | only `inventory-owned project paths must not be symbolic links` skips when Windows Developer Mode denies symlink creation; no product-test skip |
| OpenSpec |strict valid=true,issues=[] |15requirements/49scenarios;21tasks with only CI6.2pending |

TRX files are local under `tests/trips/*/TestResults` and `tests/Travel.Tests.Architecture/TestResults`. Frontend machine-readable reports, console logs and hashes are under local ignored `TestResults/trips-m1/frontend`. They are local receipts, not versioned GitHub artifacts or CI runs.

Commands include `dotnet test` for Trips unit, Trips integration with and without `Category!=Integration`, architecture; `node node_modules/nx/dist/bin/nx.js test web --watch=false`, shared-client Vitest config, production web build, isolated Playwright Trips/Flights configs; CSharpier/Biome, harness, README, inventory and pinned OpenSpec validators. Full exact frontend commands are in `frontend-receipt-reviewed.json`. No paid AI job or provider call was executed.

## Independent review and corrections

Fresh independent reviewer audited the complete implementation, including untracked code. Six confirmed P2 were corrected with observed RED→GREEN regressions; scoped recheck closed R1–R6 with no new confirmed P1/P2/P3. [Full review record](implementation-review.md) separates code review from the earlier design audits.

Named RED/GREEN receipts remain separate for the architecture namespace failure, R5 HTTP400-versus409 and frontend9-failure review regressions. The Flights demo guard refusal and its exact unintended Trips discovery GET are recorded in implementation diagnostics; the later77-case GREEN is not presented as that failed attempt. The obsolete Trips scaffold-only theory row was retired, accounting for183→182architecture tests; real Trips positive Core/Application/Infrastructure/Api selectors and all layer rules now apply.

After the scoped review, two final formatter corrections removed only trailing blank lines in Core/TripPlan.cs and trips.decoder.spec.ts. Parent reran the affected unit/lean/shared-client suites; source behavior, SQL and accepted UI contract did not change. Static UI prerequisite copy was read/hash-bound by the reviewer and included in the final production build and5-case browser run.

## Schema and authority

User separately authorized additive migration source/SQL and owned disposable PostgreSQL with fictional data and temporary test keys. `20261020143618_AddTripsPlanWorkspace` and its snapshot/SQL touch only Trips schema/table/index/history. SQL SHA256 `2B524AA41E71F4B81281F26807EF2697DE52F250FD48085656D6DE765880067C`; EF reports no pending model changes. Source review includes locking, compatibility and destructive Down warning. No existing database or environment was used.

Trips remains default-off. Enabling a target still needs its schema/key provisioning and authenticated runtime acceptance. Production startup cannot create them. Supplier/model/payment, whole-trip generation, full Host/Keycloak/deployed ingress, load/throughput and target rollout were not exercised. Existing Support4.24kB and Flights4.50kB style-budget warnings remain; nested Verify test projects emit their existing solution-discovery warning on project-only builds.

No stage/commit/push/PR/merge/sync/archive/deploy was performed. Ordinary CI remains pending rather than borrowing local GREEN or archived acceptance from another module.
