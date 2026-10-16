# Local security backport

This package retains the upstream name and version braces 3.0.3. It includes Travel patch revision 1 for GHSA-vfj7-8cjw-p6xm; it is not an upstream fixed release.

Base: official npm braces 3.0.3, git 74b2db2938fad48a2ea54a9c8bf27a37a62c350d, MIT. The original LICENSE is retained unchanged. Exact base and local artifact integrity/file hashes are in tools/security/braces-provenance.json in the owning Travel repository.

The local change enforces a hard structural depth limit 128 before parser fallback/recursive AST walkers, guards recursive expansion append, and makes array flattening iterative with controlled cycle rejection. It preserves ordinary syntax/options/API behavior. It does not claim to solve all regex complexity or unrestricted expansion-size problems.

Installation uses a prebuilt local tarball through both lockfiles, including npm ci --ignore-scripts. No install script applies this patch. Upstream version-range advisories may still match 3.0.3; security acceptance requires verified patched bytes, ordinary boundary/API tests and all retained installed copies, not a temporary advisory waiver or invented version.
