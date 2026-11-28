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

If the local Docker Engine 29+ has exhausted its default address pools, use
`npm run trips:local -- up --small-subnets` when creating a fresh demo. Docker chooses
two separate /24 networks. The ordinary Host, wall clock, ports and ownership checks
are the same. Resume an existing demo with plain `up`; it reads the saved network selection.

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

Ordinary `npm run test:trips-full-stack` runs Moscow, Saint Petersburg and
`flight-links` in that order on separate owned stacks. Each profile must pass
its browser checks and clean up its resources for the aggregate to pass.
The optional local `hotel-links` profile runs in Testing and uses a fictional
confirmed Hotels order and a fictional return flight order. Run it from the repo
root with `node tools/trips/local-stack.mjs e2e --profile hotel-links`.
Chromium signs in through the owned loopback Keycloak, attaches both orders to a
Petersburg trip with a manually entered excursion, then reopens the trip after
restarting Host, Keycloak and PostgreSQL. It removes the hotel link and compares
the original order and protected plan afterwards. The owned stack blocks Hotels
supplier transport and exports only sanitized evidence before cleanup.
`node tools/trips/local-stack.mjs e2e` retains the legacy Moscow profile; add
`--profile saint-petersburg` for a focused diagnostic run. Moscow application time
stays `2026-10-24T12:00:00Z`; SPb uses `2026-10-27T12:00:00Z` and October28–30.
JWT/Keycloak/key validity use wall clock. The SPb run additionally persists a
historical Moscow manual plan through real M1 endpoints and compares both plans,
protected rows and key fingerprints after complete service restart. This proves
integration and preservation, not current source freshness or human route quality.

## Inspect the historical Saint Petersburg trip option

From an interactive terminal in the repository root, start the disposable Testing profile.

```sh
node tools/trips/local-stack.mjs e2e --profile spb-option --inspect
```

Open the printed loopback URL (port 4206) and sign in with either fictional user from
the printed local credentials file. Leave the plan title blank. In the new plan form
choose Moscow as the origin, Saint Petersburg as the destination, 28–30 October 2026,
two adults, RUB, architecture and relaxed pace. Leave the budget empty and choose
**Подобрать вариант**. The reference date shown before selection is 27 October 2026.
With catalogue A the outbound arrives at 09:00 and the first walk starts at 14:20.
Change the end date to 29 October to inspect a one-night stay with a supplier net
price. The Host, login and browser use wall time throughout.

Keep the terminal open during inspection. Press Enter or Ctrl+C to close the
session and remove its owned containers, private files and database. The separate
ordinary demo uses ports 4205/8185. If the terminal was forcibly killed, use
`node tools/trips/local-stack.mjs destroy --yes --state ABSOLUTE_STATE_JSON` with
the `state.json` in this inspection's printed state directory.

For automated acceptance run the same command without `--inspect`. It runs the
form against A and B on successive fresh stacks, checks the original Host responses
and visible cards, and exports sanitized receipts and card screenshots before cleanup.
The late arrival (20:00) leaves no first-day walk. The automated run also checks the
one-night net-only stay and a date pair without a matching room. On Docker Engine
29+ either command accepts `--small-subnets` for a local daemon with limited address pools.

## Save a Petersburg trip and return after a stop

The retained Petersburg Testing profile gives you a private workspace you can return
to tomorrow. Start it explicitly with `node tools/trips/local-stack.mjs up --profile saved-spb-option`.
On Docker Engine 29+ you can add `--small-subnets` to a fresh start. Open the printed
loopback URL on port 4207, sign in through port 8187 using the printed fictional
credentials file, and fill in 28–30 October 2026, Moscow → Saint Petersburg,
two adults, architecture and relaxed pace. Choose **Подобрать вариант**, then
**Сохранить поездку**. Catalogue B arrives at 20:00 and checks in at 21:30.
The selection's 15 minutes, authentication and the save timestamp use wall time.

```sh
node tools/trips/local-stack.mjs stop --profile saved-spb-option
node tools/trips/local-stack.mjs up --profile saved-spb-option --read-saved-only
node tools/trips/local-stack.mjs status --profile saved-spb-option
```

Sign in again and open the trip from the list. Historical mode keeps Trips enabled
and switches off walking generation, preview, selection issue and save. Stop and
plain `up --profile saved-spb-option` reuse the stopped Host's verified mode, stack ID,
database volume and protection keys. If an interrupted restart removed the Host
container, plain `up` reports that the previous mode is unknown. Stop any surviving
services with the profile's `stop` command, then use `--read-saved-only` to reopen
the history or `--with-saved-preview` to enable saving with catalogue B. Both choices
check the owned Compose files, original volume and keys, ownership labels, available
ports and the resumed Host's effective flags. To discard this particular workspace use
`node tools/trips/local-stack.mjs destroy --profile saved-spb-option --yes`.
To save more trips in catalogue B, stop the same workspace and run
`node tools/trips/local-stack.mjs up --profile saved-spb-option --with-saved-preview`.

For the automated acceptance run, use `node tools/trips/local-stack.mjs e2e --profile saved-spb-option --small-subnets`
while the retained profile's ports are free. It uses this same retained profile
with its own random project, data, keys and state directory. It forwards real saves
to Host while exercising a lost downstream response and an explicitly held database
commit, cold historical reading, catalogue A and an expired historical source.
It cleans up its own workspace on success or failure. Its receipts contain counts,
hashes and fictional trip facts. The existing `spb-option --inspect` remains disposable.

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
On a local Docker Engine 29+ with limited address pools, run
`npm run test:trips-full-stack -- --small-subnets`. Each profile asks Docker to allocate
two free IPv4 /24 networks. The internal network, loopback ingress and owned cleanup
use the same topology as the ordinary run.
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

A fresh E2E stack gives Host dependencies and the web page three minutes to become
ready. When both are ready on time, the initial Keycloak configuration build
and realm import have up to six minutes from the start of the same readiness
check. Success requires healthy Host dependencies and successful responses
from both web and OIDC discovery in the same check. A service restart uses
the ordinary three-minute budget.

The `flight-links` profile exercises the real Trips picker with two fictional owners. Run
`node tools/trips/local-stack.mjs e2e --profile flight-links` with permission to start and
delete the disposable stack. The existing schema helper checks the database ownership
marker and prepares Trips EF. Development Host initializers prepare Flights EF, Marten
and Wolverine during startup. After readiness the test seeds one held order per owner,
waits for projection and attaches each owner's order through the picker. In the June 13–15
Saint Petersburg calendar, the overnight KGD → LED leg appears above the 09:00 excursion
on June 13. The June 16 return appears after the last day. The browser checks both placements
again after restart. The second owner sees only their own order and gets 404 when trying to
attach the first owner's order. The test also verifies source cancellation and explicit unlink.
The receipt counts all four PATCH requests, including the refusal. The ordinary aggregate runs both city
profiles and this profile with independent owned resources and cleanup.

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
