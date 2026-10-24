# Verification

## Scope and source

User authorized a reproducible isolated local stack, full real Host/Keycloak/PostgreSQL browser/restart E2E and a separate PR into dev. No merge or real-user enablement. Fixed application/browser time only in E2E was explicitly selected.

Fresh base: `14c3cc17de9a41fd006e7e9a8493d141d659c7c6` (dev after PR69).
Branch: `codex/trips-reproducible-e2e`. The Windows worktree/build/temp/cache lives on D because C is constrained; the earlier manual demo is retained separately.

The independent builder check found no unresolved material design questions. The independent source review and scoped rechecks have no open findings. Unchecked lifecycle/publication tasks remain pending.

## Local checks

- CI enforcement tests: both new tests failed before validator changes, then 64 passed / 1 platform-dependent skip across the full inventory suite. Coverage includes omitted/echoed/conditional real-stack execution and overbroad artifact paths.
- Actual solution/project inventory validation passed with the new tool classification.
- AI harness: 134 passed; canonical instructions/skills/agent checks passed.
- Lifecycle safety tests: 14 passed, including ownership, junctions, ports, redaction, real installed CLI resolution, POSIX SQL permissions and TCP PostgreSQL readiness.
- Auth/reporter checks: 17 passed; existing auth regression cross-check: 18 passed. Strict TypeScript no-emit and Playwright discovery passed.
- Real-Host helper tests: 11 passed after red-before checks, including owned provisioning, clocks, metadata-only snapshots and readiness. CSharpier checked all nine changed C#/project files using an explicit empty ignore file because this Windows worktree is below an ignored parent TestResults directory.
- Architecture: 208 passed after solution restore. No frozen route-quality corpus was tuned or rerun as a separate quality evaluation.

## Complete real-stack local run

Run `253c8e5de5d373a090b354b6ab310958` passed all 13 browser stages, startup and cleanup. Its pre-commit source identity is base `14c3cc17de9a41fd006e7e9a8493d141d659c7c6` plus worktree-delta SHA256 `d42a5639350c6b617dc8d760cb0cb2806c0d3427f1374416d87529fec33a05df`. Sanitized local receipts and two application screenshots are under the ignored `TestResults/trips-local-stack/evidence/` directory with that run prefix. PR CI will verify the committed source independently.

The run used three real PKCE code logins, two fictional users and zero external browser requests. Creation produced revision 1. Generation/adoption left the protected row unchanged; explicit save produced revision 2 after nine-stop text/time/exact-money/order/day edits. All editor fields and the complete authoritative plan matched. Host, Keycloak and PostgreSQL each exited with PID 0 and restarted with new process identities using unchanged mounts. Fresh login recovered the same plan, owner/revision/timestamps, ciphertext and protection-key fingerprints. Foreign GET/PUT/DELETE returned 404 with an empty foreign list; stale owner PUT returned 412 without mutation. The app/generator timestamps were exactly `2026-10-24T12:00:00Z`; JWT issue time matched the real clock.

Earlier development failures remain failed in retained receipts: an old Nx CLI path, an overlong Windows Nx socket path, an initial schema-start failure whose safe stdout was obscured by a nonfatal GSS warning, an NgModel name locator and a duplicate `.save-status` locator. Corrections resolve the installed CLI from package metadata, document a dedicated short `NX_SOCKET_DIR`, retain both redacted output streams, require the final TCP PostgreSQL listener, and use unique actual UI labels/status selectors. The pinned PostgreSQL entrypoint's temporary bootstrap server has no TCP listener. No Kerberos dependency or authentication relaxation was added. Every fresh failed E2E cleaned up its owned resources.

Review also corrected Linux SQL file permissions (0644 only for the non-secret initializer), retained private files at 0600, and preserved bounded non-ready health facts. The prior `localhost:4200` demo remained running and returned HTTP 200 throughout. A separate real-time profile successfully started all six services with Trips/PostgreSQL/Redis/NATS Healthy and expected blocked-Duffel Degraded; its empty diagnostic state was explicitly removed before the final retained demo.

## Runtime and publication

Pending final retained-demo stop/resume verification and PR CI. No real-user rollout or merge is authorized by this receipt.
