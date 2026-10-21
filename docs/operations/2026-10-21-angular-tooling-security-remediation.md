# Angular tooling: MCP SDK and nested Vite remediation

## Cause and scope

Base: fresh origin/dev `e654c76915103a82083ef2abc01c0579117c9749`, after the independent follow-redirects fix in PR #61. Its post-merge CI and updated Rail documentation PR #60 failed the fresh dependency gate on existing Angular tooling. A local baseline check reproduced that failure; OpenSpec's artifact scope still passed. Earlier successful CI remains historical evidence, not an exception for new findings.

The official advisories identify seven affected leaves across two installed packages:

- Angular CLI 21.2.27 pins `@modelcontextprotocol/sdk` 1.31.0. Upstream 1.32.0 addresses [task/session isolation](https://github.com/advisories/GHSA-22jm-h49p-29qw), [HTTP redirect forwarding](https://github.com/advisories/GHSA-6prh-2h8m-c8cw), [validation memory exhaustion](https://github.com/advisories/GHSA-46jr-4wx8-v453) and [bearer audience handling](https://github.com/advisories/GHSA-rvq5-wwqv-78pq).
- Angular build 21.2.27 pins nested Vite 7.3.6. Upstream 7.3.7 repairs its vendored launch-editor for [Windows percent escaping](https://github.com/advisories/GHSA-698v-4r3x-3vcv), [editor argument interpretation](https://github.com/advisories/GHSA-r825-w8mm-5pxw) and [Windows file-existence bypass](https://github.com/advisories/GHSA-587h-7wmj-h5pm). An independent launch-editor override would not replace Vite's bundled implementation.

The manifest adds `overrides.@angular/cli.@modelcontextprotocol/sdk = "1.32.0"` and `overrides.@angular/build.vite = "7.3.7"`. Angular packages stay on their existing versions; the registry has no 21.2.28 update at verification time.

An initial scoped-Vite candidate caused npm to apply the shared mocker peer override to Vitest, add a second Vite 7 copy and warn about a peer conflict. That candidate was discarded and retained only as local diagnostic evidence. Explicit `vitest.vite = "$vite"` and `@vitest/mocker.vite = "$vite"` overrides preserve the existing root Vite range and installed 8.3.4 consumer. Regenerating from the base lock then changes only version, registry URL and SHA-512 integrity for the two intended package entries, with no added/removed packages or peer warnings. No force, legacy-peer-deps or manual lock exception is used.

## Verification

The existing dependency-consumer suite gains three fictional tests: MCP tool registration/list/call through an in-memory connection using Angular CLI's resolved SDK; a bounded loopback HTTP transport that must reject a redirect without forwarding its custom header or POST body; and Angular builder's resolved Vite serving a temporary ES module through its public dev-server API. The old SDK passed the ordinary compatibility test but failed redirect refusal. The old Vite passed its compatibility test; that test is not a reproduction of Windows editor command execution. No editor process, memory-exhaustion payload, supplier or model is executed.

After clean root and pinned OpenSpec installs, the consumer suite passed 7/7; the existing fictional Rail/Vitest suite passed 7/7; AI harness passed 134/134. The full dependency gate passed 15 audit tests and 60 security/backport tests, verified one installed braces copy per tree, and accepted fresh audit reports with only the existing remediated braces leaf (16 root / 4 OpenSpec propagated entries). Scoped Biome and whitespace checks passed. A structural comparison verified that the rest of both manifests/locks and protected trees are unchanged, and checked actual installed consumer resolution.

Ordinary exact-head PR CI is recorded in the remediation PR. Raw baseline, candidate, install and verification receipts are retained under ignored `TestResults/mcp-vite/`. The existing Nx redirect and Storybook/Webpack tests remain in the same CI entrypoint.

## Preserved boundaries

Braces source, tarballs, provenance, root/OpenSpec anchors, OpenSpec runtime contract, dependency audit logic, remediation policy, workflow conditions and existing test assertions are unchanged. Audit findings are not added to an allowlist. Installed artifacts still require the existing backport proof in both trees, and every unrelated advisory fails closed. Real Yandex stays default-off. This source/PR task authorizes no merge, deployment or provider/model request.
