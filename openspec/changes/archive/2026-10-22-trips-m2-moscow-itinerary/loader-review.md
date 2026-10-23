# Task 3.1 independent loader review

Reviewed 2026-10-21. Scope: the three new task 3.1 C# files and the two small asset/content build changes. This is a source review, not whole-M2 acceptance. No source edits, Git commands, network, Host, database, containers, policy/Q outputs or suite reruns were performed.

## Verdicts

- Spec compliance: REQUEST CHANGES for one P2 immutable-release identity gap.
- Code/test quality: REQUEST CHANGES for the same bounded gap and its missing regression. The remaining inspected loader boundaries and retained local verification are satisfactory. No P1 or additional P3 finding.
- The task 1.4 helper-to-Core P3 follow-up is CLOSED for this loader snapshot: actual reviewed bytes pass, and matching inventory/payload invalid IDs and titles are rejected without normalization.

## P2 - Bind the accepted version to its reviewed bytes, not only its editable manifest

Location: modules/trips/Travel.Modules.Trips.Infrastructure/Itineraries/CuratedMoscowFacts.cs:81-106 (also version checks at 78 and 236). Missing regression: tests/trips/Travel.Modules.Trips.Tests.Integration/CuratedMoscowFactsTests.cs:305 and :487.

The source fixes datasetId/version to moscow-outdoor/1, but obtains every expected digest from the manifest in the same replaceable directory. There is no trusted reviewed manifest/payload identity for that fixed version. A change to an existing POI title in BOTH inventory and payload, using another trimmed valid title of <=100 characters, plus updated hashes for those two files in the manifest, satisfies every check and Core factory and is returned under version 1. A valid coverage-text change plus the updated payload digest is an even smaller example. This follows directly from the complete source and actual Core factories; it is not a claimed executed exploit.

The Immutable dataset release identity requirement says changed facts/rights/validity require a new version, and its Changed bytes under the same release scenario rejects changed bytes while the released identity OR manifest digest remains the same. Merely returning a different DatasetHash under version 1 does not enforce that rule. It also permits unreviewed yet structurally valid values to enter this specifically reviewed package.

Bind this fixed release to a trusted reviewed digest outside the replaceable package (for this bounded v1 source, a pinned expected manifest digest is sufficient and binds all five children). Keep semantic validation. Add a regression that changes valid matching inventory/payload text, recomputes manifest digests, retains version 1, and expects whole-package Trips.ItineraryUnavailable. Do not obtain the expected identity from the same directory, and do not introduce a generic registry. Preserve meaningful semantic-negative coverage so the new outer identity guard does not turn every existing negative case into only a digest test.

## What was checked

Read the brief/interface/report and full task 3.1 review package; only tool-output cutoffs were fetched again. Compared task 3.1, current design section 2, applicable permitted/modelled/immutable/freshness/no-effect spec requirements, source dossier and helper implementation/review. Inspected actual Core factories for the named reference, immutable collection, method, clock, ID/text and formula boundary. The root asset-format/case configuration is outside this review's changes; its limited inspection disclosed no current byte-copy mismatch.

The implementation has fixed ordinal filenames and exact-six directory admission, child reparse-point rejection, no manifest-controlled paths, strict UTF-8/no BOM/no CR, recursive duplicate rejection, exact object shapes and depth 16. Stream length and accumulated reads are bounded independently: manifest 2 KiB, payload 256 KiB, evidence 4 MiB, attribution 8 KiB, input metadata 4 KiB, inventory 128 KiB. All five manifest digests are checked before mapping. Expected I/O/JSON/domain failures collapse to one nonleaking unavailable error; cancellation is not translated.

Input, inventory, payload, walking source and paths bind source hashes, publication/download/build instants, exact model/profile/selector versions, POI identity/revision/coordinates/tags/dwell/optional anchors, ordered exact path digests, node/way counts/endpoints, lengths/crossings and integer minutes. Runtime does not reconstruct graph geometry. Identity is source-record/CC0 with exact revision URLs; walking is modelled/ODbL with null observations. Human actors remain null and quality/calibration not-evaluated. Access remains unknown. All resulting records pass Core factories; Core defensively copies exposed collections. Current clock and withdrawal are sampled on each read, with no cached ready result. Publication-origin 30-day model expiry and identity validity are retained; prebuild and exact release-boundary instants reject.

One canonical public data directory feeds the .NET Content glob and Angular asset glob. No private plan field, database/protection/readiness dependency, provider call, router or dynamic public API was introduced. The Angular source configuration points to trips-data/moscow-outdoor/1; this is not evidence of an executed Angular build or a live URL.

## Receipts independently inspected

Live file hashes match the frozen report:

- IItineraryFactSource.cs: b28e21ee86c424b5027d2d3babe6ee4f7a367e7a4521b601c0b95e427a660145.
- CuratedMoscowFacts.cs: 15648e006273d9a8c65bc63441023f6a7a545524b7230614a96510f6b57b570e.
- Infrastructure.csproj: 9c1b927530934118d14fdeac38bf2ec27a25d66d35f0adc9822d5f5f5a02449d.
- CuratedMoscowFactsTests.cs: a698eabe601468c04b447358fcd85c358dd9a6228872da9de317ed201ed5c897.
- apps/web/project.json: b4e151e7619c67caa0c2ed00d7d570da86aa523c841503f7c6fc0cdcc2c3ba40.

Read TestResults/trips-m2/curated-facts-final.log and parsed loader/curated-facts-final.trx: 90 total/executed/passed, zero failed/notExecuted; 90 individual Passed results. Retained run start 2026-10-21T12:16:39.3177228+03:00, finish 12:16:42.2307354+03:00. The actual 20-POI/90-edge success, built-copy test and all six matching-inventory/payload ID/title cases have individual Passed results. RED TRX counters independently match 68/70, 84/85, 85/87 and 87/90 before their respective fixes. Formatting logs report four CSharpier files clean and one Biome file clean. No suites or formatters were rerun.

Independently rehashed all six canonical files and both existing .NET output sets (Infrastructure/bin and Integration/bin). Each copy hash equals canonical, each of the five manifest child digests equals its actual file, and all match curated-facts-byte-identity.json:

| File | Bytes | SHA256 |
|---|---:|---|
| ATTRIBUTION.txt | 869 | 0c8bca1abb555d22fc9a30133287abb9a11cc8554169ede7c379b9473cb01172 |
| input-manifest.json | 295 | d5a3025a6670d7aaccfaaff8c15a085197c2c9734fec50339a6ecf2322870ac8 |
| inventory.json | 7711 | c04bc32ca50be5fa5caa6057bf89948fee874c01ecdc418d4bd92059695f01ae |
| moscow-v1.json | 101163 | 631fd5cd9650e3228af87ef8a31219d7ea59a2e4af42bc4328453f32268ffb08 |
| moscow-v1.manifest.json | 488 | 8f364b38540a3d8dfd21272021c50f3f18ca6c01d2863617368c12d2e6f60e43 |
| walking-evidence.json | 137293 | 196c5cd39481bdb55b6c123a4555571a0aa6f2eabc720e24ef3d70f63e3b397b |

The canonical payload independently contains 20 POIs and 90 edges; its frozen build is 2026-10-21T08:16:16.5024146Z and release expiry 2026-11-17T03:55:13Z. Matching inventory/payload mutations explicitly cover digit-leading, 65-character and whitespace IDs, and leading/trailing/101-character titles on isolated morozovsky-garden. Their rejection cannot be explained only by a mismatched inventory title/ID. Edge/path overlength cases are also present. This closes the earlier helper accepted-input mismatch without claiming the helper itself became stricter.

## Limits and next gate

No runtime mutation repro was executed for the P2; source control flow establishes it and a focused RED/GREEN regression belongs to the implementation owner. The accepted current real bytes and existing 90/90 results are not invalidated by this finding. Full task 3.1 acceptance needs the correction and scoped recheck.

Composition/default-off/Production exclusion, actual Angular build, complete Application/HTTP/UI behavior, immutable Q run-binding and Q runtime outcomes, whole-M2 review, CI, target provisioning/publication and human/field quality remain unverified or separate stages. This review does not assert D_demo release, Q_demo, source-ready M2 or live readiness. Source snapshot base e654c76915103a82083ef2abc01c0579117c9749 is supplied by the task/report; no Git command was used to reverify it.
