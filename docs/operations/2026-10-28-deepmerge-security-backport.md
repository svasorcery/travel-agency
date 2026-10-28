# deepmerge local security backport

Source-only scope authorized separately2026-10-28 to unblock the Trips M3 evaluator
branch. No commit/push/PR/merge, real model, M3/target enablement or retained demo
change. Base/head remains9ff60e42bb553214a22ed71fb4fa44b9d846c3d2; the evaluator's
existing47-file uncommitted work is preserved.

## Finding and chosen correction

The fresh root artifact gate failed on Storybook -> builder-webpack5 ->
fork-ts-checker-webpack-plugin9.1.0 -> deepmerge4.3.1. Raw audit contains four
propagated low entries for one [advisory](https://github.com/advisories/GHSA-gcqx-w48m-w2h4).
At verification time npm latest deepmerge is4.3.1 with no fixed release; latest
Storybook builder10.6.5 still depends on the same fork-ts-checker line. The
[upstream report](https://github.com/TehShrike/deepmerge/issues/273) is disputed,
but six local CJS/UMD regressions reproduced poisoned returned-object inheritance
(null-prototype target, nested null/primitive placeholders and own target __proto__).
Object.prototype itself remained unchanged. Ordinary merge control passed.

Broad Storybook upgrades do not remove this leaf. A local revision1 backport uses
the repository's existing braces artifact-proof approach. All three destination
assignments in mergeObject now create own enumerable/writable/configurable data
properties. This avoids invoking __proto__ setters while retaining ordinary key,
array, clone, symbol and customMerge behavior. Source, CJS and UMD receive the same
small correction. It does not ban all dangerous key strings: own data can remain,
and callers still need schema validation. This is not protection against executing
untrusted build configuration/custom callbacks.

The original npm tarball SRI and commit5b87756a5671635679001cbac72aa42f23472c81,
original source, MIT license, patched source/archive and per-file hashes are retained
under tools/vendor and tools/security/deepmerge-provenance.json. Name/version remain
deepmerge4.3.1; no upstream fixed-version claim, postinstall hook or expiry waiver.
Only index.js, both distributions, package metadata and PATCH-NOTICE change.

## Admission and reproducibility

Root devDependencies contains one file-tarball security anchor and overrides refers
to $deepmerge. npm12.2.0 generated the lock without force/legacy-peer-deps. Exactly
two lock nodes differ: root and node_modules/deepmerge. Every other version/path/
metadata entry is byte-value equivalent; Angular/Nx/Storybook versions stay pinned.

Preinstall artifact gate verifies both archives, exact source inventory/hashes,
package metadata, direct override and lock SRI. Full gate verifies exactly one
physical installed copy, including nested/aliased copies and file-resolution
shadows. Proof objects are unforgeable within the process; provenance files remain
reviewed repository source, not signatures against a compromised host/source tree.
Only the exact root low advisory/range/nodes/versions and real lock edges are
classified as verified local remediation. New/mixed advisories, altered severity,
network failure or drift remain closed. Existing braces proof and scope are retained.
Raw deepmerge findings remain visible; four entries do not mean four repaired CVEs.

Clean npm ci --ignore-scripts --no-audit installed1483packages. Only this worktree's
old node_modules junction was removed, after verifying its exact task-owned target;
its D target and retained demo were left untouched. A new physical install lives
inside this worktree. Npm cache/TEMP for installation use task-owned D output.
Pinned OpenSpec runtime/source/skills and its lock are unchanged.

## Verification and limits

Local logs are ignored out/eval-prep/deepmerge-*.log. Initial security reproduction
had6failures/1ordinary pass; artifact and audit proof tests failed before new guards.
Current proof tests22/22, security patch suite74/74 and retained dependency consumers
15/15 pass. Full installed gate passes root and OpenSpec; root raw audit4,
OpenSpec0. Differential CJS/UMD tests compare ordinary results/options against the
exact pristine archive; symbols/cloning/arrays/custom policy are also exercised.

Actual Storybook build, final strict/harness checks and fresh independent security
review are recorded below after completion. No source/installed acceptance of a
different tarball is inferred. Future upstream fixes or consumer replacement should
remove this maintained local backport through an explicit reviewed dependency
change. Changes to this revision require fresh source/archive/hash review; do not
regenerate proof simply to accept unexpected drift.

## Final source/local receipt

Actual npm exec --offline -- nx run web:build-storybook:ci --skip-nx-cache passed
in24s, producing only this worktree's dist/storybook/web. Nx daemon/cloud and
Storybook telemetry opt-out were configured for the build. An initial direct
old Nx bin path did not exist; the supported package bin was then used, without
changing a dependency or target. The retained demo was never rebuilt/served.

Independent read-only26-file security review found no Critical/Important/Minor.
It confirmed upstream-equivalent algorithm after reversing only the descriptor
helper, exact artifact closure, root-only advisory admission and all vendor files
visible to Git. Later edits only organized imports/test string templates; final
security/audit gate was rerun. No review findings were waived.

Final checks: security suite74/74, audit unit16/16 (combined proof22/22 includes
six artifact tests), retained consumers15/15, full installed dependency gate and
preinstall gate green, raw audit root4/OpenSpec0. Harness134/134, inventory64pass/
1Windows symlink skip, strict active change plus20specs pass. Biome checks own
files; vendor bytes are intentionally not reformatted. Node26.11.1/npm12.2.0 used
locally; CI remains a separate future Linux/install/publication acceptance gate.

The reviewer excluded existing M3 source, model/human/target/demo operations,
revalidation of external advisory availability, compromised source/host and
malicious executable callbacks. Those are deliberate unchanged boundaries:
prior M3 source receipts remain valid because no .NET/product file changed;
registry availability was checked by the executor; integrity is not a signature
against a malicious repository; own dangerous keys require downstream schema
validation. Source proof does not promise arbitrary-config sandboxing or a future
CI result. No further product/model/rollout acceptance follows.

Future publication can keep this security remediation as a separate meaningful
commit from evaluator mechanics. User has not authorized any commit/push/PR here.

Reproducibility recheck: npm12.2.0 pack ./tools/vendor/deepmerge-4.3.1
--ignore-scripts into a fresh owned directory produced a byte-identical canonical
tarball. Final inventory is out/eval-prep/final-source-with-deepmerge-manifest.json;
all12vendor files and both archives are Git-visible, nine frozen M3/.NET inputs
and three pinned OpenSpec metadata files match base bytes, index empty and HEAD
unchanged. Existing41non-Markdown evaluator source/config/test files retain their
previous final hashes. Remote CI and clean Linux npm installation remain future
publication checks; no assertion of them is made from this Windows receipt.

## Publication authorization and formatter check

The user subsequently authorized commits, branch push and a PR to dev with CI;
merge/model/M3/target enablement remain unapproved. Earlier no-publication lines
record the source-preparation phase. Before staging, actual Biome CI reproduced
four formatter/import errors on pinned vendor source. A narrow whole-directory
exclusion matching the existing braces convention preserves reviewed bytes during
hooks/CI; the independent source/archive/installed security checks remain mandatory.
This changes no advisory policy and does not bypass hooks. Publication uses the
verified existing branch/base, not a replacement HEAD base.

## PR83 CI correction

Initial PR CI40675378507 on3b03631 had18successful jobs, frontend-affected failed,
E2E was blocked and paid AI eval skipped. Build, ordinary AI/Contract/architecture,
backend suites and both-city real-service Trips profile job passed. The frontend
failure was Nx auto-inference of deepmerge:lint on preserved upstream ES5 bytes.

Actual local RED reproduced no-var errors. A global ESLint ignore alone rejected
all inputs but could leave an inferred empty lint task; the failing invocation
also deleted the pinned upstream .eslintcache. That known own side effect was
restored exactly from HEAD. Final correction excludes only this vendor path from
both first-party ESLint and Nx ESLint inference, preserving the complete source
inventory. Actual ESLint and Nx graph regressions check that maintained source,
web and ui-kit retain lint targets. Exact affected build/test/lint and subsequent
artifact/installed checks are required together; no ignore or all-files-ignored
exit is treated as success. No byte of the backport or any security check changes.
The failed run remains historical evidence; a new ordinary PR run is required.

The final local sequence passed: exact affected command15tasks/9projects in43.7s, then security74/74 and unchanged installed/source proofs, then consumers17/17 including both scope regressions. Required vendor bytes stayed unchanged. A separate CI-fix commit carries only lint/inference configuration, those tests and this receipt.
