# Task 2.3 independent candidate-validator review

Reviewed: 2026-10-21. Scope: task 2.3 specification compliance and code/test quality only. This is not the final M2 audit.

- Specification verdict: **PASS**.
- Code/test quality verdict: **PASS**.
- Confirmed findings: **no P1, P2 or P3 findings**. No source correction requested.

## Reviewed identity and method

Worktree: `C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency`. Review package records base/HEAD `e654c76915103a82083ef2abc01c0579117c9749`; reviewer made no Git calls or changes.

| Reviewed file | SHA-256 verified from current bytes |
|---|---|
| `modules/trips/Travel.Modules.Trips.Core/Itineraries/ItineraryCandidateValidator.cs` | `5f8defec3c0b7aefabaad17f7f1159e53972a15b20ff15a1395c82d1cc90ca61` |
| `tests/trips/Travel.Modules.Trips.Tests.Unit/ItineraryCandidateValidatorTests.cs` | `a9086d8443bddd8ac9fe2aeba2f5ecdbd8d589c09b367d84b6adb12e11997edc` |

Read the task brief, early interface, implementer report, current task 2.3, design sections 3-4 and relevant current delta-spec requirements for facts/unknowns, selection, outcomes/coverage, structural validation and historical intentions. Read the complete two-file review package once; reread only the output-cutoff range. Focused outside-file inspection covered the actual public outcome schema, trusted immutable fact factories/date predicates/model values and policy note/version/reason vocabulary. No repository-wide crawl, source edit, subagent, network, database, Host, real-data or Q-output execution was performed.

## Specification evidence

- **Independent recomputation:** validator lines 17-225 contain their own outcome/invariant traversal. There is no Generate call, clock read, effect, graph parser, or reuse of private policy eligibility/ranking/coverage routines. Permitted fact applicability predicates and value factories remain the trusted domain input boundary. It returns the exact original complete valid outcome, never repairs a result.
- **Input/package/metadata binding:** lines 25-68 and 228-275 validate saved destination ID/IANA/catalogue and output saved snapshot, supported dates in the saved zone, UTC generation instant, release/model freshness, exact dataset/version/hash/policy and clamped expiry. Preparation/model equality is by every exposed value, including publication/download/build/input hashes/profile/selector/model/validity, rather than object identity. Equivalent valid distinct objects are covered by the test at lines 445-467.
- **Stops and access:** lines 278-377 enforce bounded shape/text, original POI/title/date, nonempty UUID, null suggested times and money, precisely identity/access references and globally unique POI/UUID. Current and whole-local-day applicability are both checked. Trusted GetIntervalsOn recomputes weekly intervals and date exclusions; known closed is rejected, absent/expired access remains unknown with no invented full-day opening and its exact reason.
- **Historical note:** lines 308-316 require exact initial dataset/version/asOf/POI reference plus OSM/ODbL credit and URLs, bounded to 500 Unicode scalars. Appended feasibility/observed/budget claims and oversize/null text fail. The exact copyright escape matches the actual policy template; no truncation occurs. The note is initial editable planning content, not live verified provenance after acceptance.
- **Walking provenance and arithmetic:** lines 380-464 require exact directed adjacency and original edge/evidence scopes/restrictions, current/date-applicable modelled ODbL evidence with no observation, and complete source/path/hash/length/crossing/anchor/profile/selector/model binding. Integer lower/upper formulas are independently calculated with quotient/remainder and compared to both output and model bounds. Geometry recomputation is intentionally outside runtime validation.
- **Pace, coverage and honest outcomes:** lines 40-46, 108-220 recompute targets, source dwell and walking-upper sums; all requested local days appear once ascending. Distinct requested-interest union spans every selected day, food stays unmet, and Filled requires all target slots/all interests/no search limit. Candidate requires selected stops; Empty requires none; defensive Core Unsupported has no route. Exact sorted/deduplicated reasons/exclusions/uncertainties preserve opening-hours unknown, transferNotPlanned and totalCostUnknown independently of Filled.
- **Malformed results and counters:** enum/root/member/scalar/null checks and bounded lists reject the whole malformed result with stable `Trips.ItineraryUnavailable` and no exception text; normalized argument errors use `Trips.InvalidInput`. Public constructors defensively copy lists and reject null lists, while malformed members/scalars are explicitly exercised. The 0..20000 global counter is additionally bounded by the current shape proof `days * POIs * (1 + (target-1)*16) <= 7056` and at least the selected-stop count. This is a necessary trace bound, not a reconstructed trace. SearchLimited cannot occur for current valid bounds; no artificial counter units or test-only budget API were introduced.

## Test and receipt review

Tests use the existing explicitly fictional fixture and mutated public output constructors. They assert safe whole-result rejection for fabricated/duplicate/scoped/expired references, reversed or missing edges, provenance/model/formula changes, forged coverage and hidden unknowns, invalid enums/nulls, note/time/money claims and global metadata. Additional semantic cases cover source dwell, cumulative walking caps, repeated tags, cross-day uniqueness/order, current versus whole-day validity, known open/closed/unknown access, all three paces, unsupported/empty and equivalent metadata/source ordering. These tests do not mirror the validator traversal.

The reviewer inspected existing actual raw logs/TRX and their hashes; no already verified suite was rerun. The complete full-unit TRX groups to 110 validator + 46 facts + 37 policy + 53 M1 tests.

| Saved receipt under `TestResults/trips-m2/validator/` | Executed | Passed | Failed | NotExecuted |
|---|---:|---:|---:|---:|
| `red.trx` | 88 | 3 | 85 | 0 |
| `red-additional.trx` | 6 | 0 | 6 | 0 |
| `green-verified.trx` | 110 | 110 | 0 | 0 |
| `full-unit.trx` | 246 | 246 | 0 | 0 |

All 85 initial RED and six additional RED failures have the semantic `Shouldly.ShouldAssertException : result.IsError` signature. Failed diagnostic runs disclosed in the implementer report are not treated as final proof. Successful final logs agree with TRX. `format-final.log` reports `Checked 2 files in 770ms.` Source last-write times precede the successful full run.

| Independently checked receipt | SHA-256 |
|---|---|
| `green-verified.log` | `74705473cb9c1bf664134db035dacd0294834d6e71f227f4688393caddf8306b` |
| `green-verified.trx` | `927850ae15c0750439bca1319478e681da0bb33bd068adeae2de40dd35cab57e` |
| `full-unit.log` | `b0ead7cbf1a21fc5208b9247fd0832dda95d9cb8ce8cd7578b43003f1b31efa7` |
| `full-unit.trx` | `a718eb5c06d984ff3671dff4b41a4caa72b49fefeadb65c7002f176f7b68ffc1` |
| `format-final.log` | `2d2d7e47718e1193a5988405eab454953609dab3418c4e41fb81cb5cb5481e26` |
| `red.log` | `3c526950dbff686e727cd70d232f78ba18c43002e1526e9245df09138f196af4` |
| `red-additional.log` | `2e39db007b1d3c8934248a87313987b0ae87b5d86222a78e22d21b9610a81eec` |

## Limits and cross-stage gates

This review accepts task 2.3 source/local fictional structural validation. It does not establish ranking optimality, the exact expansion trace, empirical cap exhaustion, geometry truth, walking feasibility, calibrated minutes, real route/human quality or live licence/target acceptance. It relies on factory-created immutable ValidatedItineraryFacts, not arbitrary reflection-corrupted domain objects.

Application source-free ScopeUnsupported/Moscow selection, API strict decoding/complete response and M1 size checks, final owner/revision/expiry recheck, UI adoption/session isolation, source loader/real package, Q_demo/Q_live, ordinary PR/postmerge CI, full Host/Keycloak/target and enablement remain their own tasks/gates. No claim is made about current in-progress consumer code or aggregate M2 completion. Parent owns canonical task checkboxes and final acceptance.
