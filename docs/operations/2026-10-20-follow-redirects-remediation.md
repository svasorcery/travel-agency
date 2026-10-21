# Nx follow-redirects security remediation

## Cause and bounded change

The fresh dependency gate for Rail documentation PR #60 failed on the existing root lock at `30cd68d892cf72994c83047a22ceff782814796f`. Nx 23.2.1 pins its own `follow-redirects` 1.16.0; the separate hoisted 1.16.1 copy did not replace that nested consumer. The two upstream advisories, [array-form credential headers](https://github.com/advisories/GHSA-q3jh-wrc6-jgq8) and [custom sensitive-header regex escaping](https://github.com/advisories/GHSA-8r9p-f939-6h3c), identify 1.16.1 as patched. The [official release](https://github.com/follow-redirects/follow-redirects/releases/tag/v1.16.1) and npm registry metadata confirm that version.

The fix adds only `overrides.nx.follow-redirects = "1.16.1"`. npm 11.20.0 regenerates the lock by removing the nested 1.16.0 entry; Nx now resolves the existing official registry 1.16.1 copy. Its SHA-512 integrity is `sha512-FNvFGzoMLWmE6Yj9spb/zjd7yiNCHiAW9/Tg9CXrQ8wuu32HtlJOwWO11OJafl5FfY3DxTdQ0vj42zU1kvv5jg==`. No other locked package version changes. Nx itself stays 23.2.1 and retains the existing axios, smol-toml and brace-expansion overrides.

## Verification

The baseline `npm run check:dependency-artifacts` reproduced exit1, `Unapproved advisory in dependency chain: @nx/angular`. The existing dependency-consumer suite now also resolves the transport from Nx's actual package context and exercises loopback HTTP redirects with fictional values. On the old installed copy, two regression cases failed because credential headers reached the other host: alternating-array headers and custom header names containing `^` or `|`. Ordinary object headers and the existing Storybook/Webpack consumer passed. Each redirect case also checks preservation on the same host and delivery of an ordinary public header.

After a clean root install and the pinned OpenSpec install, `npm run check:dependencies` passed: 15 audit-gate tests, 60 security/backport tests, one verified installed braces copy per tree and fresh audits containing only the existing remediated braces leaf. `npm run check:dependency-artifacts` passed separately. Consumer tests passed 4/4, AI harness 134/134, scoped Biome and whitespace checks passed. Nx's installed context resolved the hoisted `follow-redirects` 1.16.1. Raw root audit counts varied between 16 and 18 propagated entries; these are the existing braces chain, not additional leaves.

Exact-head ordinary PR CI is recorded in the remediation PR. Local raw RED/GREEN reports are retained under ignored `TestResults/follow-redirects/`. The earlier failed Rail documentation CI remains failed; a successful remediation PR does not retroactively accept that run or update its branch.

## Preserved boundaries

The braces backport source, archives, provenance, both braces lock anchors, the OpenSpec manifest/lock/runtime digest, dependency-audit logic and remediation policy are unchanged. Fresh audit findings still fail closed unless they match the verified existing braces backport. No threshold, workflow condition, test assertion or security check is relaxed.

Installation and regression traffic use registry downloads and local fictional HTTP servers; no supplier/model request is part of this verification. This change does not enable real Yandex, change application feature flags, merge a PR or deploy anything.
