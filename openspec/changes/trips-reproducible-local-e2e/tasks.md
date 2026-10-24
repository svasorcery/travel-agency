# Tasks

## 1. Owned local lifecycle

- [x] 1.1 Implement the bounded Node controller, pinned Compose topology and owned state validation; verify pure tests reject foreign paths, state, daemon, resource labels and port collisions before mutation.
- [x] 1.2 Implement build/start/status/resume/stop/restart/destroy and safe evidence export with credential redaction; verify pure redaction tests and document exact commands, prerequisites, ownership and finite key lifetime in tools/trips/README.md.

## 2. Real Host tooling

- [x] 2.1 Add the tool-only provisioning/schema/snapshot/probe console project with fresh fictional users and protected keys; verify focused tests for state/configuration refusal, provisioning boundaries and safe snapshot serialization.
- [x] 2.2 Add the E2E-only real Kestrel/Program wrapper with fixed application time and real JWT time; verify tool tests and build without production clock hooks, fake authentication or storage.
- [x] 2.3 Document helper boundaries and local-only schema ownership in tools/trips/README.md; verify solution and CI inventory classify the project as a tool.

## 3. Real browser acceptance

- [x] 3.1 Add only the two exact loopback auth origin pairs and regression coverage; verify existing auth behavior and refusal of other origins/ports remain intact.
- [x] 3.2 Implement dedicated Playwright config and full real login/create/generate/adopt/edit/save/reopen journey; verify generation/adoption leave the protected row unchanged until explicit Save.
- [x] 3.3 Extend the scenario with complete service restart, fresh login, full plan/row/key comparison, second-owner refusal and stale revision refusal; verify all stages pass with no API mocks and sanitized evidence only.

## 4. CI and completion documentation

- [x] 4.1 Add package commands and ordinary PR/dev full-stack job with final E2E dependency, safe artifact allowlist and inventory regression tests; verify structural CI tests and existing harness checks.
- [x] 4.2 Update Trips completion review with reproducible instructions and exact clock/data/quality/rollout boundaries; verify links and retain default-off real-user behavior.

## 5. Integration and delivery

- [x] 5.1 Run the owned local stack and full E2E through restart, validate stop/resume/cleanup and capture exact source/image/stage receipts; verify older demo is unaffected.
- [x] 5.2 Complete focused regression checks and independent whole-change review, address material findings and record evidence in verification.md; verify strict OpenSpec validation.
- [x] 5.3 Publish a separate PR into dev and await successful ordinary CI on the final head; retain the PR unmerged and record its exact head and CI receipt.
