# Task 2.2 fix1 scoped independent recheck

Date: 2026-10-21. This recheck supplements, and does not overwrite, task-2.2-review.md / policy-review.md.

## Verdicts

- Specification recheck: **PASS**. The initial review's sole P2 is closed.
- Code-quality recheck: **PASS** within the fix and newly introduced risks. No new P1/P2/P3 findings.
- Combined task 2.2 source review is now accepted on the original review plus this scoped correction. Whole-M2, real-data/quality, Application/wire/UI, ordinary CI and target acceptance remain outside this verdict.

## P2 closure

`modules/trips/Travel.Modules.Trips.Core/Itineraries/ItineraryPolicy.cs:169-172` now retains datasetId/version/asOf/POI and appends `© OpenStreetMap contributors; ODbL: https://opendatacommons.org/licenses/odbl/1-0/; https://www.openstreetmap.org/copyright.` This is in the initial ready-to-transfer historical note itself, satisfying the accepted note requirement in design section 2.4 without relying on a future UI footer or note rewrite. No legal-compliance opinion is inferred.

`tests/trips/Travel.Modules.Trips.Tests.Unit/ItineraryPolicyTests.cs:534` adds the semantic regression fixture with 64-character datasetId, version and POI IDs. It verifies the preserved identity/asOf, attribution and both URLs; a 100-supplementary-scalar original title remains unchanged. It checks the full note's scalar bound and ordinary M1 TripBlock construction/preservation. The maximum-identity note length was also independently calculated as 386: all note characters are BMP, so that string's length equals its Unicode scalar count. This leaves 114 scalars below the existing 500-scalar bound without truncation.

The production change is only the note string. Ranking, pace, search units/limits, fact knownness, times/money and outcome semantics are unchanged. Keeping bounded-beam-v1 is consistent with design's ranking/limit version rule. The new fixed text introduces no mutable output or network operation.

## Scope and byte verification

Read the narrow task-2.2-fix1-review-package.txt and updated worker report once. A read-only reconstruction applied that narrow delta to the exact three-file package inspected during the initial review and compared it with current source bytes: policy/test matched exactly. The only delta is one policy string replacement plus the single new regression test. Candidate.cs remains byte-identical. No Git command, suite rerun, source mutation or unrelated source crawl was used.

Verified current hashes:

| Artifact | SHA-256 |
|---|---|
| ItineraryCandidate.cs (unchanged) | 57b52398c7dbcfaef2e58af9c87ce9c8c49429e9482ce46a69d488983a4310b2 |
| ItineraryPolicy.cs | c5df39020d036bf5896b4054ee32f48d994c0fd0f949a46c1a801b8607f0d102 |
| ItineraryPolicyTests.cs | 224027aada09a246e4ea8336c5cc0dc470ace3b82d4be7ea6d1fcaaf13da55bc |
| task-2.2-interface.md | 89df7abbe7d6e2ac67f1410ebffd83f357e2f50d04677251ddac240e67b91b37 |

Supplied base/HEAD remains e654c76915103a82083ef2abc01c0579117c9749; ancestry was not re-queried in this scoped review.

## Newly persisted verification evidence inspected

The actual log files were read, TRX counters and result entries parsed, and all receipt hashes verified against the report. These are fresh correction-round evidence, not reconstructed proof of the original 135-test run or initial RED.

- RED: task-2.2-fix1-red.log/.trx contain exactly 1 executed / 0 passed / 1 failed / 0 notExecuted. The named maximum-identity test fails specifically because the original note lacks `© OpenStreetMap contributors`. TRX finish: 2026-10-21T11:56:47.4389992+03:00.
- GREEN: task-2.2-fix1-final.log/.trx contain 136 recorded result entries, all Passed; TRX counters total/executed/passed=136, failed/error/timeout/aborted/notExecuted=0. The new historical-note test is present and Passed. The full unit log reports 803ms. TRX finish: 2026-10-21T11:57:42.3840818+03:00.
- Formatting: task-2.2-fix1-format.log records formatting of the three owned C# files and `Checked 3 files in 693ms.`

| Receipt under TestResults/trips-m2/ | SHA-256 |
|---|---|
| task-2.2-fix1-red.log | fd3c62270657e4ea55c445a58162f5b4405719773f89e3082768be25a94d9661 |
| task-2.2-fix1-red.trx | 5b0d9429ff81fc1411af11b5d9c43d6cfbda30f923e2b703b34b67195ae17adb |
| task-2.2-fix1-final.log | 254c78b9660e699b7c1002aedc9493298650e37a78b3736f933cd1c353ec5146 |
| task-2.2-fix1-final.trx | d4c36ed1b42cfb0b268271580623277536fb36c96ef27cd09bfb17447148b0f1 |
| task-2.2-fix1-format.log | 9e74a0dd95d5213a8a2529ea35f07ce31973520ec406325d9aa12c2dbc905b90 |

The original 135/135 run remains historical worker-reported output with no original persisted raw receipt. The new 136/136 evidence independently resolves the current verification gap and does not retroactively change that history. Review did not run any suite.

## Retained boundaries and review effects

All later cross-consumer and D_demo/Q_demo/Q_live/CI/target items from the initial review remain unverified here. No real package/quality cases/outputs, Host, database, provider, model or network were accessed. Default-off state is unchanged. Only this recheck and its canonical copy were written using same-directory temporary files and atomic File.Move; C: free space was checked at 3010715648 bytes before writes. No product or Git mutation was performed.
