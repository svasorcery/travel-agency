# Task 3.1 scoped recheck - revision 1

Reviewed 2026-10-21 against the exact initial review plus task-3.1-fix1-review-package.txt. Scope is the P2 correction, its focused release descriptor, loader delta and related tests. The original review remains retained in loader-review.md; this recheck supersedes its open P2 verdict for the source hashes below.

## Verdicts

- Spec compliance: PASS for the scoped correction. The immutable-release P2 is CLOSED.
- Code/test quality: APPROVE for the scoped correction. No new P1/P2/P3 finding.
- The original helper-to-Core P3 closure remains valid; negative semantic coverage still reaches the actual parser/domain boundary rather than failing every case at the new outer digest pin.

This is task 3.1 loader source/local review acceptance, not whole-M2, D_demo/Q_demo, CI, Composition, publication or live acceptance. Task bookkeeping and cross-stage gates remain parent-owned.

## Closure and new-risk checks

CuratedMoscowRelease is a sealed immutable caller-owned descriptor with get-only dataset/version/manifest digest. ReviewedV1 pins the exact reviewed manifest SHA256 outside the replaceable package. Constructor validation permits only moscow-outdoor, canonical bounded version text and lowercase SHA256, and specifically prevents version 1 from being rebound to any different digest. There is no registry, database, automatic discovery or scope expansion.

CuratedMoscowFacts defaults its optional fourth argument to ReviewedV1. It hashes the bounded actual manifest bytes before parsing or trusting any manifest field, then checks both manifest and payload identity against the same descriptor. Since the pinned manifest binds all five children, coordinated valid changes to payload/inventory plus refreshed local manifest hashes cannot enter version 1. Existing child digest checks, strict bytes/schema/path admission, Core factories, current-clock freshness, cancellation and per-read withdrawal remain in place. IItineraryFactSource and the normal three-argument constructor call remain compatible.

An explicit different release is a trusted caller input, not a package-derived override. The reader never computes its trusted descriptor from the directory. Future Composition must retain that boundary: use the default reviewed descriptor or a separately reviewed explicit descriptor; do not copy the test-only manifest-binding helper. This is an integration constraint, not an observed defect or additional blocker.

The three new version-1 regressions first load the real release, then alter matching valid POI text, valid coverage text, or manifest whitespace. They retain version 1 and do not rebind their expected identity. All three failed against the old loader because it returned success, then pass after this fix. Four additional cases reject v1 repinning, another dataset, a whitespace version and uppercase digest.

Semantic-negative fixtures deliberately use a distinct test-semantic-v1 identity, preparing and explicitly binding their known fixture manifest outside the source after mutation. This avoids turning existing tests into only pin-mismatch checks. The positive alternate-release test loads changed valid text as 20 POIs/90 edges through the real mapping, asserts the distinct version and changed title, and confirms the default v1 reader rejects those same bytes. Matching inventory/payload bad IDs/titles, source/rights/formula/UTC/schema/size and valid-digest malformed-path tests retain their intended deeper checks. No test-only bypass was inserted into the production loader.

## Exact source and receipts checked

Read the complete fix delta/new descriptor and revision-1 report/interface. Independently computed live hashes and matched the frozen report/receipt:

| File | SHA256 |
|---|---|
| CuratedMoscowFacts.cs | f0932e8ddd6ebfb37c92ebdcc292825847b57f88e45a0ada12988e97bb485cb6 |
| CuratedMoscowRelease.cs | 9ae7d7d9eb618c380fa2f9eac21951ebd1fef0cb92325f76a1696fc96a780d32 |
| CuratedMoscowFactsTests.cs | 944e2e4d9f3f6861904a681078aa4e59bb6444fba7a91e4a521fb490070da7ee |
| IItineraryFactSource.cs | b28e21ee86c424b5027d2d3babe6ee4f7a367e7a4521b601c0b95e427a660145 |
| Infrastructure.csproj | 9c1b927530934118d14fdeac38bf2ec27a25d66d35f0adc9822d5f5f5a02449d |
| apps/web/project.json | b4e151e7619c67caa0c2ed00d7d570da86aa523c841503f7c6fc0cdcc2c3ba40 |
| task-3.1-interface.md | 9cc0119f5a07d17bf02df70c156e3036933ea0fc73fc8331a5c59d7c110904b1 |

Read the actual retained TestResults/trips-m2/curated-facts-release-red.log and curated-facts-release-final.log. Parsed both corresponding loader/*.trx files rather than relying only on reported totals:

- RED: 93 executed, 90 passed, three failed, zero notExecuted. Individual failures are exactly matching-valid-title, valid-coverage and manifest-bytes, with IsError false instead of expected true. Run 2026-10-21T12:32:23.0571478+03:00 to 12:32:26.3947736+03:00.
- FINAL: 98 total/executed/passed, zero failed/notExecuted; 98 individual Passed results. Run 2026-10-21T12:35:46.4928692+03:00 to 12:35:50.1588404+03:00. Individually checked the three repaired regressions, positive alternate-release case, four descriptor checks and six matching inventory/payload ID/title cases.
- curated-facts-release-format-check.log: five files checked clean by CSharpier. Angular configuration is unchanged from the first reviewed clean Biome snapshot.

Read curated-facts-release-byte-identity.json, then independently rehashed all six canonical files and both current .NET output sets (Infrastructure/bin and Integration/bin). Every canonical hash remains equal to its original review identity, and every output copy equals canonical. In particular, manifest remains 488 bytes / 8f364b38540a3d8dfd21272021c50f3f18ca6c01d2863617368c12d2e6f60e43, exactly matching ReviewedV1; payload remains 101163 bytes / 631fd5cd9650e3228af87ef8a31219d7ea59a2e4af42bc4328453f32268ffb08. This fix did not rewrite the real data.

## Limits

No suites, formatter, policy/Q output or runtime repro were rerun by this reviewer. No product source edits, Git commands, source/provider/model network, Host, database or container action occurred. Only the two assigned recheck reports were written atomically after checking free space. The supplied base e654c76915103a82083ef2abc01c0579117c9749 was not reverified with Git.

This scoped recheck does not repeat the prior full loader audit or independently re-audit unrelated concurrent Core changes. Composition/Production exclusion, actual Angular build/public URL, Application/HTTP/UI integration, Q run-binding and quality outcomes, full-M2 audit, CI, target and human/field acceptance remain separate. No new acceptance is inferred for those stages.
