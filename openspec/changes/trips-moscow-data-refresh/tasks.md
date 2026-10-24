# Tasks

Implementation uses the approved mechanism-only scope in [proposal](proposal.md), [design](design.md) and [contract](specs/trip-data-package-maintenance/spec.md). Execute inline with the executing-plans workflow; the OpenSpec task file is the single implementation plan. User authorized source/tests/public PR and green ordinary CI, not merge, actual v2 release or real-user enablement.

## 1. Read-only package validation

- [x] 1.1 Add `PackageVerification.cs` and `package-check` dispatch to `tools/trips/Travel.Trips.LocalStack`, without changing state/provision/schema commands; verify real v1 success, wrong pin/UTC/path/expired/corrupt refusals and no state/DB provisioning in focused `TripsLocalToolTests` or a neighboring test class. Document candidate validation versus release approval.

## 2. Bounded maintenance commands

- [x] 2.1 Add `tools/trips/moscow-refresh.mjs` with `check` and acquisition/preflight helpers; write failing Node cases then verify unchanged/older/newer, missing/future/redirect/error metadata, size/time/truncation limits, same-GET provenance, unchanged-hash refusal, existing/foreign/reparse output and no implicit retry. Pin v1 identity and verify its six files before using metadata. No dependency or source URL override.
- [x] 2.2 Complete `prepare` and offline `reproduce-current`: frozen inputs/as-of, two existing helper executions, exact six-file comparison, real `package-check`, bounded immutable receipt with hashes/timing/resource facts and explicit candidate-needs-review status. Verify builder/validator failure and output disagreement remain failures, unchanged HEAD performs no GET/build/write, and a controlled successful test candidate never edits repository data or runtime selection. Add the documented npm commands and developer runbook with later release gates.

## 3. Historical plan regression

- [x] 3.1 Extend `TripsHostFixture` with optional test-only source/clock inputs and add `TripsPackageLifecycleTests`: generate/save through actual Host and PostgreSQL using v1, switch to a distinct synthetic test package and restart Host, then exercise expiry/corruption and explicit editing. Verify full response and raw owner/revision/timestamps/ciphertext equality before any explicit edit; prove new generation observes the changed version and later becomes unavailable while old plans remain readable/editable. Keep fixture defaults and the real-Keycloak browser suite unchanged; document the fictional identity/transport boundary.

## 4. Delivery evidence and CI

- [x] 4.1 Wire offline Node maintenance tests into ordinary CI alongside the existing offline builder checks and keep Host regression in its existing Integration lane. Verify test discovery, focused Node/tool/Host tests, relevant existing suites, formatting, strict OpenSpec and AI-harness/inventory checks without live source calls in CI.
- [x] 4.2 Record a real unchanged-publication check and byte-exact v1 reproduction from the actual retained/downloaded source, with two build hashes, source/header binding and measured runtime/memory. Verify v1 canonical bytes, runtime pin and public URLs remain unchanged; retain no-candidate result and explicitly defer v2. Do not rerun/tune the frozen Q corpus.
- [x] 4.3 Complete independent source review and scoped fixes/recheck; verify no open material findings and all task evidence is coherent with source-only/fictional/current-data boundaries.
- [ ] 4.4 Publish a separate PR into dev and await green ordinary CI on its final head; record exact head/run/attempt and leave the PR unmerged with no auto-merge or real-user enablement.
