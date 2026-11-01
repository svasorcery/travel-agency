# Travel local security backport, revision 1

This remains minimatch10.2.5 with a local patch, not an upstream fixed release.
GHSA-g3hv-75wg-m249 is mitigated by porting the two preprocessing limits from upstream commit77a0f5c4b80bdc641c3c930a1d9edf5ad2db57f8: stop **/.. splitting at braceExpandMax (default100000), and skip pairwise deduplication above1000 patterns. Matching semantics and makeRe/RegExp representation remain on10.2.5.

CommonJS additionally exports the existing minimatch function with all named exports attached; ESM and both declaration entrypoints add that existing function as the default export. This preserves the callable/default API consumed by older tooling without implementing a second matcher. No security fix is claimed for unrelated advisories.

Modified runtime entrypoints: dist/commonjs/index.js and dist/esm/index.js. Both index.d.ts files receive the default export. Package metadata records this patch and includes this notice. All other upstream files are byte-identical, including original maps retained for provenance (their embedded source does not include the local patch). License and attribution are preserved. The original and patched archives, sources, manifest/lock and installed bytes are verified by the dependency gate; raw audit findings remain visible and only exact reviewed chains with this verified backport are accepted.
