# Dependency Security Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this approved scope inline, followed by an independent whole-branch review. Track execution in this plan's ignored SDD workspace.

**Goal:** Publish one security-fix PR from fresh dev 0738c121d4058451d5b3f628f3c878a24ad7e90d with ordinary CI green; do not merge it.

**Architecture:** Update only the affected npm dependency closure. Preserve Angular/Nx/Spartan entrypoint versions and OpenSpec 1.14.0 source, integrity and generated workflows. Keep the existing audit and verified braces backport gates unchanged. Use a scoped Spartan Zod 4 override only after validating its actual configuration and generator consumers.

**Tech Stack:** Node/npm, Angular build tooling, Nx/Spartan generators, pinned OpenSpec, existing Node consumer/security tests and GitHub Actions.

**Spec:** User-approved separate security-fix PR after PR59 post-merge CI40075423976 failed. Current constraints: root AGENTS.md, tools/ci/dependency-audit.mjs and docs/operations/2026-10-16-braces-security-backport.md. The baseline audit is retained under TestResults/dependency-security-20261024/.

## Global Constraints

- Fresh origin/dev base above; preserve its ancestry in a new codex branch.
- Commit/push/PR/CI fixes are authorised; merge, deploy, provider/model calls and feature enablement are excluded.
- No npm audit fix --force, legacy-peer-deps, advisory allowlist, security threshold change or fixture weakening.
- Preserve both braces source/tarball/proof sets and all OpenSpec source/skill/capability pins; only its verified dependency runtime digest may change.
- Preserve product source, schemas, feature flags and existing tests/workflows.
- Root baseline leaves: body-parser 1.20.8/2.3.0, fast-uri 3.1.8, serialize-javascript 7.1.2, Angular builder undici 7.29.1, Spartan zod 3.25.76. Tool baseline zod 4.6.5.
- Patched target families: body-parser 1.20.9/2.4.0, fast-uri 3.1.9, serialize-javascript 7.1.3, undici 7.30.1, zod 4.6.6. Do not conflate scanner clearance with production reachability proof.

## Review Focus

- Spartan configuration defaults, invalid input diagnostics and both Angular/Nx schema modes survive the scoped major dependency override.
- Spartan style records remain validated and the actual button generator still emits usable source and path mappings in a virtual tree.
- Malformed compressed HTTP or excessive content encoding cannot leave retained consumers unusable; bound local probes, never public targets or resource-exhaustion payloads.
- OpenSpec still validates real commands and rejects byte drift after a clean install; generated skills and feature specifications remain unchanged.
- Lock resolution changes only the intended leaves and necessary transitive closure; audit remains closed to unrelated findings.

### Task 1: Root dependency repair and retained consumer coverage

Files: package.json, package-lock.json, tools/ci/dependency-advisory-consumers.test.mjs; an isolated HTTP fixture only if the crash regression requires a child process.

- [x] Record fresh baseline audit and exact lock/source identity before edits.
- [x] Add bounded behavioural regression/compatibility cases for the changed consumers. First run them on the old dependencies; retain genuine RED results separately from compatibility cases that already pass.
- [x] Update allowed leaf versions with npm-generated lock changes. Add scoped overrides only where the parent pins prevent remediation: Angular build undici and Spartan zod.
- [x] Inspect every changed lock node; reject unrelated package/version churn or unsupported peer resolution.
- [x] Clean-install, run all old and new consumer tests, and verify actual consumer resolution with npm ls. Exercise the actual Spartan generator without running its returned install task.

### Task 2: Pinned OpenSpec dependency repair and complete acceptance

Files: tools/openspec/package-lock.json, tools/openspec/manifest.json, docs/operations/2026-10-24-dependency-security-remediation.md.

- [x] Update only OpenSpec's zod to a patched 4.6.x release through npm; preserve the exact OpenSpec package pin and the rest of the dependency tree where possible.
- [x] Clean-install both trees, verify the existing backport/security artefacts, and recompute runtimeDigest from the actual pinned installation using the repository's treeDigest rules.
- [x] Run the existing pinned CLI/inventory/unit/integration tests, strict specification validation and AI harness. Verify no protected source/skill/spec files changed.
- [x] Run full dependency audits/security checks, all consumer tests, frontend production build and scoped formatting; preserve any informational diagnostics.
- [x] Record exact changes, RED/GREEN evidence, compatibility limits and current advisory sources in the operations note.
- [x] Obtain one independent whole-branch review and address material findings with focused regressions. No material findings remained.

Publication checkpoint (recorded in the PR rather than a self-referential source receipt): commit/push, open the separate PR into dev, wait for exact-head ordinary CI including PR E2E and record the complete denominator. Keep the PR open without auto-merge.
