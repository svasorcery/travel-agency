# Travel local security backport, revision1

Base: official deepmerge4.3.1 npm tarball, original name/version/MIT retained.
Advisory: https://github.com/advisories/GHSA-gcqx-w48m-w2h4

No upstream fixed release is claimed. All three destination assignments in
mergeObject now create own enumerable/writable/configurable data properties with
Object.defineProperty. This prevents the __proto__ setter from changing the
returned object prototype, in both target-copy and source-merge paths. Apply the
same change to source, CommonJS and UMD distributions; no build/install hook.
Ordinary options, arrays, symbols and customMerge behavior are retained. Dangerous
own keys may remain own data; callers must still validate schemas/inputs. This is
not a sandbox for executable build configuration or malicious custom callbacks.

Unchanged source, license, type declarations and archive inventory are verified
against the retained official upstream archive. See deepmerge-provenance.json,
security regression/differential tests and operation receipt before replacement.
