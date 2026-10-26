# Run the local Trips stack

For publication/expiry inspection and reproducible package preparation, see
[City data maintenance](data-maintenance.md). It does not activate a new release
or alter existing demo data.

This opt-in stack runs the ordinary Angular Trips UI, real Travel.Host, Keycloak,
PostgreSQL, Redis and NATS. It creates fictional users and separate protected data.
It does not enable a shared environment or call real travel suppliers or models.

## Start and sign in

Use Node 22+, .NET SDK 10 and Docker Compose v2 with a local Linux Docker daemon.
Run commands from the repository root. On Windows PowerShell use `npm.cmd` and
`npx.cmd` if execution policy blocks the `.ps1` shims. Allow room for dependencies,
published .NET binaries, the web build and six Docker images.

If Nx reports a socket path that is too long on Windows, set `NX_SOCKET_DIR` to a
short, dedicated directory owned by your user (for example `$env:NX_SOCKET_DIR =
'D:/tmp/trips-nx'` in PowerShell). Do not point it at a shared directory or the
entire temporary directory: Nx manages and removes its socket directory.

```sh
npm ci
npm run trips:local -- up
```

Open <http://127.0.0.1:4205/trips>. The login page uses Keycloak at
<http://127.0.0.1:8185>. Open the local file
`TestResults/trips-local-stack/demo/private/credentials.json` and use either
generated user's username and password. Each user has a separate private workspace.
Do not commit, attach or copy this file into CI logs.

Create a Moscow or Saint Petersburg trip, generate a candidate, review and adopt it, then edit the
calendar and press Save. Adoption changes the draft; Save persists it. Return to
the trip list and reopen the plan to read the saved version.

The normal demo uses the real date. The reviewed Moscow package expires on
**2026-11-17 at 03:55:13 UTC**. After expiry, generation becomes unavailable;
existing saved plans and manual editing remain available. Current access/hours/costs
and walking-time calibration are not established by this demonstration.

Saint Petersburg uses a separate 20-place/62-path outdoor package, valid until
**2026-11-24 at 04:08:52 UTC**. Museum interiors, courtyards, museum gardens and
Neva crossings are outside that slice. Kaliningrad remains available for manual
plans; walking proposals there are explicitly unsupported. Data readiness and
expiry are independent for each supported city. Adoption edits only the local
draft; Save remains a separate action. The source link shown belongs to the saved
city. No route promises opening hours, free admission, safe passage or total cost.

Ordinary `npm run test:trips-full-stack` runs Moscow then Saint Petersburg on
separate owned stacks; the aggregate requires both browser runs and cleanup.
`node tools/trips/local-stack.mjs e2e` retains the legacy Moscow profile; add
`--profile saint-petersburg` for a focused diagnostic run. Moscow application time
stays `2026-10-24T12:00:00Z`; SPb uses `2026-10-27T12:00:00Z` and October28–30.
JWT/Keycloak/key validity use wall clock. The SPb run additionally persists a
historical Moscow manual plan through real M1 endpoints and compares both plans,
protected rows and key fingerprints after complete service restart. This proves
integration and preservation, not current source freshness or human route quality.

## Stop, resume and restart

```sh
npm run trips:local -- status
npm run trips:local -- restart
npm run trips:local -- stop
npm run trips:local -- up
```

`restart` stops Host, Keycloak and PostgreSQL, checks that their processes exited,
then starts them using the same database and protection keys. `stop` preserves data;
`up` resumes that state. Calling `up` on a running stack does not regenerate users or
keys or replace mounted application binaries. Stop before rebuilding changed source.

The state belongs to this checkout and Docker daemon. Commands refuse copied or
tampered state, remote Docker endpoints, foreign resource labels and occupied ports.
They do not stop whatever owns a conflicting port. The existing `localhost:4200`
demo can remain running alongside this stack.

Generated demo certificates and keys have a finite 90-day lifetime. Resuming an old
state does not silently renew keys. Keep the complete private directory with its
database while you need saved fictional plans. To intentionally discard this demo
and its plans/users/keys:

```sh
npm run trips:local -- destroy --yes
```

The next `up` provisions a fresh demo. Destruction only removes resources whose
state and Docker ownership match; it never performs a global Docker prune.

## Run the full browser acceptance test

```sh
npx playwright install chromium
npm run test:trips-stack
npm run test:trips-full-stack
```

Linux CI installs Chromium OS dependencies with `npx playwright install --with-deps chromium`.
The browser test creates a fresh owned stack on ports **4206/8186**, independent of
the interactive demo. It uses real code+PKCE login and original Host responses;
authentication and response payloads are not replaced, and no password grant is used.

`test:trips-stack` checks strict Trips TypeScript, lifecycle/unit checks and synthetic
local HTTP/Chromium regressions. These fixtures test the response observer separately
from the real Host journey. Candidate observation briefly pauses only the exact
candidate response at Chromium's response stage, reads its original bounded JSON,
and continues it without changing status, headers or body. It avoids dependence on
an inspector resource that can disappear after renderer consumption/navigation.
The test performs one candidate POST, with no replay, automatic retry or fallback
to another response. Existing candidate, UI, no-write and restart assertions remain.

The scenario verifies creation, nine-stop generation/adoption without an implicit
database write, text/time/exact-money/order/day edits, explicit save and reopening.
It then stops and restarts Host/Keycloak/PostgreSQL, opens a fresh browser context,
logs in again and compares the full saved plan, protected row and key fingerprints.
It also checks foreign-owner refusals and a stale revision without changing the plan.

Only this E2E Host and browser use application time **2026-10-24T12:00:00Z**, with
trip dates 25–27 October. Keycloak and JWT signature/lifetime validation use the
real clock. The immutable source package and expiry checks are unchanged. This
makes the positive integration test reproducible after package expiry; it is not
a freshness or human route-quality assessment.

Fresh E2E resources are removed on success or failure. Test or cleanup failure
returns a nonzero result. Sanitized receipts and application screenshots are exported
under `TestResults/trips-local-stack/evidence/`; CI uploads only its JSON/PNG files.
Tokens, passwords, cookies, key files, browser traces and private state are excluded.
Do not upload the entire `TestResults` directory when reporting a failure.

## Implementation boundaries

`local-stack.mjs` owns lifecycle and state validation. `images.json` pins image
digests. Caddy publishes loopback-only UI/identity ingress; the backend Docker
network is internal. Trips already uses Angular client rendering, so this profile
serves its compiled CSR output and proxies `/api` to Host.

The `Travel.Trips.LocalStack` console tool provisions fresh keys/fictional identities,
applies the existing Trips migration only after checking the owned database marker,
and emits bounded readiness/snapshot facts. It adds no product migration.
Its E2E entrypoint hosts the actual Program through WebApplicationFactory/Kestrel
and replaces application TimeProvider only. Interactive mode runs Travel.Host
directly. Production Host has no test-clock setting and no dependency on this tool.

The accepted contract is [trips-local-verification](../../openspec/specs/trips-local-verification/spec.md).
The design, 13 completed tasks and exact PR71/post-merge receipts are retained in
[the archived change](../../openspec/changes/archive/2026-10-24-trips-reproducible-local-e2e/archive-verification.md).
The original-response observation repair is accepted through PR77; its five tasks,
13 Chromium regressions, independent review and final PR/post-merge restart receipts
are retained in [the repair archive](../../openspec/changes/archive/2026-10-25-trips-e2e-original-response/archive-verification.md).
It preserves the existing contract and does not claim an upstream Chromium fix or
human route-quality acceptance.
Remaining human-quality and real-user conditions are in the
[Trips completion review](../../docs/architecture/trips-completion-review.md).
