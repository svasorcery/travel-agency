# Storybook preview-origin security remediation

## Cause and scope

PR #64 at `ac9fe1feb4aee348a676c1dd7af5776eb3c00a32` failed ordinary CI [39739096791](https://github.com/svasorcery/travel-agency/actions/runs/39739096791) at the unchanged dependency-artifact gate. A fresh local baseline reproduced the Storybook failure.

The upstream [GHSA-f2ph-cmg5-wxc8 / CVE-2026-114125](https://github.com/advisories/GHSA-f2ph-cmg5-wxc8) identifies missing origin validation in Storybook's development-server preview. It can permit a malicious website to cause unauthorized project-file changes while the affected development server is running, with further execution possible under additional conditions. Storybook 10.5.8 is within the affected 10.x range; the upstream fixed range starts at 10.6.2.

The root manifest pins `storybook` to 10.6.3. Existing `@storybook/angular`, `@storybook/builder-webpack5` and `@storybook/core-webpack` remain 10.5.8, whose peer ranges accept the fixed core; `@nx/storybook` 23.2.1 also accepts it. Installed consumer resolution confirms a single deduplicated Storybook 10.6.3 without peer conflicts.

The npm-generated lock changes the root pin, the Storybook entry and 25 new packages under `node_modules/storybook/node_modules/`. The new nested resolver is required by Storybook's exact `oxc-resolver` 11.21.2 dependency and includes its optional platform bindings. No existing package outside Storybook changes or disappears. Registry URLs and SHA-512 integrity remain recorded by npm. No force, legacy-peer-deps, new override or advisory exception is used.

## Verification

Receipts are retained locally under ignored `TestResults/rail-m2-storybook-20261022/`.

| Check | Result |
|---|---|
| Baseline `npm run check:dependency-artifacts` | Failed on the new Storybook advisory; OpenSpec passed |
| Post-update artifact/full gate with local npm 12.2.0 | Root passed; OpenSpec audit transport hit the unchanged 120-second deadline twice; failures retained |
| `npm exec --yes --package=npm@10.9.9 -- npm run check:dependencies` | Passed using the npm version observed in CI; audit tests 15/15, security/backport tests 60/60 |
| Fresh final audit reports | Only the already remediated braces leaf remains: 18 propagated root entries, 4 OpenSpec entries; one installed backport copy verified in each tree |
| `npm run test:dependency-consumers` | 7/7, including Storybook/Webpack's patched middleware serving a fictional loopback asset |
| `nx build-storybook web --configuration=ci --skipNxCache` | Passed on the existing project configuration in 41 seconds; telemetry disabled |
| `npm ls storybook @storybook/angular @storybook/builder-webpack5 --all --json` | All retained consumers resolve Storybook 10.6.3, no peer errors |
| Structural manifest/lock comparison | Only the direct Storybook pin and its required nested closure change |

The npm 12 timeouts are not reclassified as successes. The successful npm 10 verification used the same gate source, policy, deadline, both locks and installed security artifacts. npm 10 was task-local tooling; no repository npm/Node version or CI condition changed. Compatibility and static-build checks do not reproduce the malicious-origin exploit; the security remediation is the upstream fixed artifact, whose advisory disappears from the fresh audit.

Ordinary exact-head CI for this separate fix commit is recorded in PR #64. Earlier runtime/load proof remains bound to its original Rail source; this dependency change does not relabel or repeat that measured run.

## Preserved boundaries

The dependency checker, remediation metadata, audit thresholds, security tests, braces source/tarballs/provenance, pinned OpenSpec manifest/lock, workflow conditions and security feature defaults are unchanged. No unsafe Storybook security flag is enabled. Trips source/tests/specifications, Rail implementation and other product modules are unchanged by this fix. Supplier/model APIs, merge and deployment remain outside this task.
