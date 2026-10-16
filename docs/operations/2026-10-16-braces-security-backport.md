# Local braces security backport

The repository supplies one reviewed local backport of `braces 3.0.3` for [GHSA-vfj7-8cjw-p6xm](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm), patch revision 1. The advisory still lists no upstream patched release. The package keeps its upstream name, version and MIT license; neither the lock nor the audit report claims an upstream version fix.

The official base archive, local source, patched archive and exact file hashes are retained in `tools/vendor/` and `tools/security/braces-provenance.json`. The base is the official npm tarball at source commit `74b2db2938fad48a2ea54a9c8bf27a37a62c350d`. The original LICENSE, README, entrypoint and constants are byte-identical. `.gitattributes` keeps vendor text LF and archives binary. Review the source diff against the retained base before accepting a new patch revision.

## Structural bound and compatibility

The shared iterative validator rejects AST depth above 128 with controlled `SyntaxError`, code `ERR_BRACES_MAX_DEPTH`. Parser pushes check the bound before any fallback stringify, including unclosed brace and parenthesis structures. Public and direct internal compile/expand/stringify entrypoints validate supplied ASTs. Recursive expansion append also checks depth. Flattening uses an iterative traversal and rejects cyclic arrays with `ERR_BRACES_ARRAY_CYCLE`.

Ordinary boundary tests verify refusal and recovery on the next normal call. Differential tests compare ordinary syntax, options, direct AST APIs and errors against the exact pristine upstream archive. Root and OpenSpec fast-glob task generation exercise the installed copies. Earlier bounded local diagnostics did not reproduce a default runtime crash; the evidence is missing structural refusal before the patch, controlled refusal after it, source review and compatibility checks. No default-crash reproduction is claimed.

This deliberately changes behavior for unusually deep structures. It does not establish a general expansion-size budget, regular-expression complexity guarantee or safe execution of arbitrary untrusted build configurations. These are separate concerns from the recursive-depth defect.

## Reproducible installation

Both manifests use a direct security anchor pointing to the same checked-in `braces-3.0.3-travel.1.tgz` and the documented npm override reference `"braces": "$braces"`. The root uses `file:tools/vendor/...`; the OpenSpec tool uses `file:../vendor/...`. Both locks require the same SHA-512 integrity. The direct reference avoids npm resolving a transitive relative file spec from a consumer directory. It was checked by installing registry dependencies first, switching the manifest and lock, then running a clean install.

The tarball already contains the fix. `npm ci --ignore-scripts` for the root and `npm ci --prefix tools/openspec --ignore-scripts` require no patch hook. To reproduce the archive with npm 11.20.0, run `npm pack ./tools/vendor/braces-3.0.3 --ignore-scripts --pack-destination <empty-temporary-directory>` and compare its bytes/integrity with the canonical archive. Do not regenerate a new receipt merely to approve unexpected drift.

OpenSpec remains exactly 1.14.0 with its original package integrity, source commit and capability/skill hashes. Its runtime digest is updated to the actual verified dependency tree. The launcher still rejects any dependency-byte change, including removal or modification of the guard. Protected generated skills are unchanged.

## CI proof and raw audit findings

`npm run check:dependency-artifacts` is the preinstall phase. It checks the original and patched archives, source inventory/hashes, both manifests/locks and fresh advisory reports. It explicitly reports that the installed runtime has not been verified.

After both installs, `npm run check:dependencies` requires exactly one correct installed copy in each tree and verifies every file hash, then runs the ordinary tests and fresh audits. Discovery recursively checks physical package contents for `node_modules` at any depth, including lib/dist and scoped consumers; physical unscoped `node_modules/braces` directories count even if metadata is missing or renamed. Package metadata and file shadows are interpreted only in npm package/module contexts, so ordinary source folders/files named braces are allowed. The generated `.bin` exclusion applies only inside module directories; source subdirectories are traversed without following links. Additional copies, missing guards, edited walker bytes, links, arbitrary file dependencies, archive/source/lock drift and every other advisory fail closed.

Raw npm audit continues to match the unchanged version: 16–18 root and 4 OpenSpec propagated high entries (the final local report has 16 root entries; earlier npm 11 metadata included two additional reviewed peer consumers) describe this one leaf. These counts are not independent vulnerabilities. Classification as `LOCAL SECURITY BACKPORT` requires the actual artifact proof, exact approved paths/versions and complete nonempty recursively resolved leaf set equal to this advisory. Metavulnerability cache edge variation is accepted only across verified real lock edges. Missing edges, closed cycles without a leaf, mixed/new advisories, malformed reports and network failures fail. There is no expiry waiver or blanket severity exception. A scanner that only inspects upstream version ranges will continue to report the base version; use the source and installed-artifact receipt to assess this local remediation.

Future dependency updates must preserve these checks or replace the backport with a maintained upstream repair and corresponding review. CI source checks and local clean-install/build evidence do not claim deployment or acceptance of a different installed artifact.
