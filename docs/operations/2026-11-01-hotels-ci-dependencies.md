# Hotels PR86: dependency gate remediation

PR86 head cfdfbe49392ccb51c1920b2b079ec7dcaf95a137 failed the fresh dependency gate in [CI41325402362](https://github.com/svasorcery/travel-agency/actions/runs/41325402362), attempt1. Its root/OpenSpec manifests and locks were unchanged from base c3fd3a6b3e8965ad8cf7850e160f22edfa12dedf. The owner explicitly expanded the task on2026-11-01 to fix the findings, verify compatibility and complete PR/merge. Earlier failed CI remains evidence.

## Final dependency graph

- Nx resolves follow-redirects1.16.2, addressing [cross-realm sensitive-header leakage](https://github.com/advisories/GHSA-mrwp-f3q8-3g9p).
- glob consumers on9+ resolve14.0.3, addressing [post-abort traversal](https://github.com/advisories/GHSA-3g3m-2fjr-6pw4). That package retains its own exact registry minimatch11.0.3 copy.
- Older/Nx consumers resolve minimatch10.2.5 with a source-bound local security patch for [preprocessing amplification](https://github.com/advisories/GHSA-g3hv-75wg-m249). The two limits are ported from [upstream77a0f5c](https://github.com/isaacs/minimatch/commit/77a0f5c4b80bdc641c3c930a1d9edf5ad2db57f8): bound splitting by braceExpandMax and skip quadratic deduplication above1000 patterns.
- Commitizen retains glob7.2.3, outside the AbortSignal advisory's affected range, using the patched compatible matcher.

A global minimatch11 update cannot preserve this repository's consumed API: Nx uses makeRe, glob7 needs RegExp set entries, webpack's type checker calls the default export, and ESLint uses an ESM default import. The final backport retains the10.2.5 matcher and makeRe implementation; the existing callable function additionally exposes named CommonJS exports, and ESM/type declarations expose that same function as default. No second matcher or makeRe emulation is introduced. glob14's nested11 copy preserves its distinct Matcher API, including unbundled entrypoints.

## Provenance and acceptance

The original npm archive is pinned by SHA-512. The source and patched archive retain53 upstream files, with only six declared changed/added paths: both runtime index.js files, both index.d.ts entrypoints, package metadata and PATCH-NOTICE.md. All other upstream bytes, license and attribution remain identical. Original source maps are retained and explicitly documented as unpatched provenance.

[Minimatch provenance](../../tools/security/minimatch-provenance.json) binds both archives and every source file. Pre-install checks verify this proof, the manifest and lock; installed checks verify the local bytes and exact two-copy inventory. The registry copy is fixed to glob's nested path, version11.0.3, registry URL and integrity. Unexpected copies, shadows, paths, versions, modified bytes or unverified proof fail.

This remains a local10.2.5 backport, not an upstream fixed-release claim. The final observed raw audit reports52 propagated entries from the known minimatch and deepmerge leaves. They remain visible. The gate requires the actual corresponding artifact proofs, exact reviewed package versions/node sets, approved leaf details and real lock reachability for each reported edge. npm can vary redundant propagation edges for the same graph; those edges are validated against the lock as in the existing braces policy. Unknown/mixed-new leaves, disconnected edges and new package chains fail. The existing braces/deepmerge policies, sources and tarballs remain unchanged; the pinned OpenSpec dependency tree is unchanged.

## Verification and publication

Bounded regressions reproduced cross-origin header leakage using fictional loopback headers, an extra directory read after cancellation, and pattern amplification. Review additionally reproduced the real Nx wildcard and Commitizen nocase failures; both now pass. Tests cover both CommonJS and ESM limits, ordinary matches, legacy defaults, patched archive/source integrity, malformed proof/lock cases, exact registry-copy binding and mixed-backport refusal cases.

Raw before/after audits, installs, RED/GREEN logs and rejected candidate evidence are retained locally under TestResults/hotels-m2-memory-load-acceptance/publication-v1/dependencies. The final publication identity and exact PR/post-merge CI receipts are recorded in PR86/PR87 and the local publication receipt. Final correction PR87 and its post-merge CI are accepted.

The Hotels accepted load run remains tied to its existing frozen archive. No new Hotels load, supplier/model call, deployment or target enablement follows from this dependency repair. Main-spec sync/archive was subsequently completed by the owner request; see [archive verification](../../openspec/changes/archive/2026-11-01-hotels-m2-memory-load-acceptance/archive-verification.md).


Local final verification:17/17 audit-policy tests,82/82 security tests,27/27 real-consumer tests; fresh root/OpenSpec gates pass. Nx build/test/lint completed15tasks across9affected projects without cache. Independent review reproduced and rechecked the compatibility fixes:31/31 focused tests, no remaining Critical/Important findings. Its minor test-name clarification was applied. Biome and strict OpenSpec validation passed. Full ordinary PR and post-merge CI remain the publication gates.
