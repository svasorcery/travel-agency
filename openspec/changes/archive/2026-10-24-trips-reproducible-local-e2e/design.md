# Design

This archived design retains the original implementation and publication plan. The later PR70/PR71 merge authorizations and accepted final local/PR/post-merge results are recorded in [verification](verification.md); [archive verification](archive-verification.md) records the specification sync and remaining boundaries.

## Context

See proposal.md. Base is fresh dev `14c3cc17de9a41fd006e7e9a8493d141d659c7c6`. The previous manual stack proved actual Host and Keycloak/PostgreSQL composition and restart. PR69 added a real-Host M1 transaction regression, while existing Trips browser tests still intercept API responses. Trips routes are already client-rendered in `apps/web/src/app/app.routes.server.ts`.

The user authorized implementation, owned local provisioning, a separate PR into dev and no merge/real-user enablement. They selected fixed time only in E2E; interactive demo uses wall clock. Existing pilot-specific OpenSpec context does not negate that explicit authorization. One canonical plan lives here, with no duplicate feature plan.

## Goals / Non-Goals

Goals: repeatable local start/resume/stop/restart; fresh isolated CI stack; real OIDC/Host/generator/store/protection; complete browser save/restart proof; safe, bounded ownership and secret handling.

Non-goals: production deployment, shared target migrations, public exposure, supplier/model/payment calls, route-quality evaluation, refresh/tuning of Moscow data, generic environment orchestrator, disaster recovery or multi-node claims.

## Decisions

### 1. Bounded Compose stack and static Angular client

Use the proven PostgreSQL + Keycloak + Redis + NATS + Host topology with a Caddy ingress. Backend services use only an internal Docker network. Host shares Keycloak's network namespace so the issuer's loopback address is also valid for JWT metadata retrieval. Caddy alone joins ingress/internal networks and publishes explicit 127.0.0.1 bindings. The normal compiled Angular CSR browser output (`index.csr.html`) is served by Caddy with SPA fallback and `/api/*` proxying; private Trips routes already require client rendering. No long-running host Node process is needed.

Interactive URLs: `http://127.0.0.1:4205` and Keycloak `http://127.0.0.1:8185`. E2E uses 4206/8186, so it can coexist with both interactive demo and the older localhost:4200 stand. The shared auth resolver adds only these exact loopback-origin pairs; it retains real Keycloak code+PKCE and rejects other origins/ports. Using 127.0.0.1 also separates cookies from the older localhost realm.

Alternatives: default AppHost includes AI and fixed/shared development ports and volumes; changing its ordinary topology broadens this slice. A detached Nx process adds OS-specific process ownership/cleanup; static CSR removes it without changing Trips rendering semantics. A new fake API would repeat the original evidence gap.

### 2. Owned state and lifecycle

Node entrypoint `tools/trips/local-stack.mjs` provides `up`, `status`, `restart`, `stop`, `destroy --yes`, and `e2e`. No operation runs on import. Default demo state is `TestResults/trips-local-stack/demo`; E2E always creates a fresh `e2e-<id>` directory. All state must be physically inside this checkout's TestResults/trips-local-stack, with no symlink/reparse traversal.

Public `state.json` schema: `{schemaVersion:1,kind:'travel-trips-local-stack',id:<32 lowercase hex>,mode:'demo'|'e2e',repoRoot:<absolute>,projectName:<derived travel-trips-MODE-ID prefix>,webPort:4205|4206,oidcPort:8185|8186,applicationAsOfUtc:null|'2026-10-24T12:00:00Z',createdAtUtc:<UTC>,dockerEndpoint:<local endpoint>,dockerId:<daemon identity>}`. Project name/ports/clock must be derived and validated, not arbitrary values accepted from edited state.

Only a local Linux Docker daemon is supported (local unix socket or local Windows named pipe; remote TCP/SSH endpoints are refused). Pin image references/digests and record actual image IDs. Every resource receives `travel.trips.local-stack-id=<id>`. Before reuse/restart/stop/destroy, verify daemon identity and both Compose-project and owner labels. A foreign directory, daemon, resource or occupied port fails before mutation. No prune or broad cleanup.

`up` on an empty owned state prepares binaries/assets, fresh secrets and schema, then starts detached containers. Repeated up on a running owned stack returns status without regenerating keys/users or overwriting mounted binaries. Stopped state can be resumed; source rebuild happens only while its Host/ingress are stopped. `stop` preserves all data/keys. `restart` stops Host, Keycloak and PostgreSQL, verifies they exited, starts them with the same volume/keys and verifies new process identities/readiness. `destroy --yes` is explicit destructive cleanup of this owned state only. E2E cleanup is automatic only for the fresh state created by that invocation; sanitized evidence is exported before private state deletion.

### 3. Tool-only provisioning and E2E clock

Add `tools/trips/Travel.Trips.LocalStack/Travel.Trips.LocalStack.csproj`, a console tool referencing the actual Host (not its test assembly), the existing centrally pinned MVC Testing package and no new production dependency. Add it to solution/inventory as a tool. Unit tests live in the existing Host test project and reference this tool; production Host never references it.

Tool commands:
- `provision --state <absolute> --repo <absolute>`: validate owned state; create `private/provision.json` with hex `databasePassword` and `certificatePassword`; fresh three certificates/encrypted rings under `private/keys/{trips,flights,support}/{certificate.pfx,ring/}`; cloned realm under `private/realm/travel-realm.json`; `private/credentials.json` is an array of two `{username,password,subject}` fictional users. Refuse overwrite. Keep canonical real public client/PKCE; only use current profile origins and fresh fictional identities. Passwords are never printed.
- `schema --state /state`: only inside the owned container, validate fixed local database coordinates and `public.trips_demo_owner.id` against state.id, then apply the existing Trips EF migration. PostgreSQL init creates that marker and separate Keycloak database. No new product migration.
- `serve-e2e --state /state --host /host`: E2E-only container entrypoint, executing actual Program.cs through WebApplicationFactory and real Kestrel on 5099, with explicit published content root and ordinary Host configuration. Replace only application TimeProvider with a tool-only TestOnly fixed clock. Keep actual JWT handler, issuer/audience/signature/lifetime validation, real transports, stores and protection; JWT options use TimeProvider.System. Normal demo directly runs `/host/Travel.Host.dll`, with no override hook or setting in production Host. No diagnostic startup hooks.
- `probe --state /state`: read internal Host readiness/dependency endpoints and emit bounded safe readiness facts.
- `snapshot --state /state --trip-id <guid>`: read only the owned database row, returning tripId, ownerIssuer/ownerSubject, revision, timestamps, payload length/SHA256 and key-file hashes, never ciphertext/plaintext or secrets.

Generated keys must be active for both the selected application instant and real wall clock, so frozen E2E does not fail when run later. Interactive keys/certificates have an explicitly documented finite lifetime (90 days); E2E keys are disposable. Reusing state never renews/overwrites keys silently. Diagnostics distinguish wall UTC from fixed application UTC. Provisioning uses no saved credentials/user secrets, and container mounts expose only this state's files/binaries.

Base Compose runs the normal Host. A separate E2E override selects the tool's serve-e2e entrypoint. Both mount the helper for explicit schema/probe/snapshot commands. Explicit configuration disables unrelated providers, NL search, Hotels/Rail and classification; source data remains the reviewed immutable package. No production flags/configuration defaults change.

### 4. Clock and browser acceptance

E2E uses application instant 2026-10-24T12:00:00Z and trip dates 2026-10-25..27. Playwright sets only browser Date to the same fixed instant, leaving timers operational; otherwise the real UI would correctly reject an expired preview against wall time. Keycloak and Host JWT validation remain on wall time. The ordinary demo always uses current time and honestly reports expired package unavailability. Do not alter package hashes, extend source TTL or rebind the frozen quality corpus.

Dedicated Playwright config: one worker, retries 0, no API routes/mocks, no trace/video/storageState capture. A starts with a fresh browser context and real login. Observe real API responses; create two-adult Moscow foundation, generate nine stops, verify no write on preview/adoption, change title/note/time/exact amount/order/day, save, list/reopen and compare full accepted snapshot/DOM controls. Capture database/key fingerprint. Close the browser context, restart the three real processes via the owned CLI, assert exited PID0/new StartedAt and PIDs, create a fresh context, log in with the same user/password, reopen and compare the complete saved data, IDs/order/revision/ciphertext/keys. B logs in through real Keycloak in its own context; list/detail/PUT/DELETE cannot access A. A stale PUT must return 412 without mutation.

Browser test imports only explicit controller functions: `readState(statePath)`, `restartStack(state)`, `captureDatabaseSnapshot(state, tripId)`. `readState` returns validated state plus `stateDir`. Runtime configuration is passed by `TRIPS_STACK_STATE`, never arbitrary remote BASE_URL. Bearer tokens stay only in memory; no direct password grant or fabricated identity. Assert external requests absent and successful payloads remain real.

### 5. CI and evidence

Add an ordinary `test-trips-full-stack` job for PR and dev/master pushes. Existing final PR E2E depends on it; update CI inventory validator/tests to prevent accidental omission. Use existing pinned setup actions, Node22/.NET10, pre-pulled images and Chromium. Node lifecycle/ownership/redaction tests run without Docker; tool unit tests run in existing Host HTTP lane. Full-stack command uses the same stack definition as local demo.

The E2E controller exports a bounded receipt with source identity, image IDs, wall/application times, named stage outcomes and restart/snapshot comparison. Only sanitized receipts and safe application screenshots are eligible CI artifacts; never upload state/private dirs, env files, PFX/XML keys, tokens, cookies or browser traces. Redact credential values and authorization/OIDC query material before any child output is printed or retained. Test failure or cleanup failure returns nonzero; no retries or skipped positive scenario to create a green result.

## Risks / Trade-offs

- Static CSR does not test the Express SSR server; Trips already uses RenderMode.Client. The existing application E2E remains intact.
- Fixed E2E clock proves integration, not current data freshness or human itinerary quality. The interactive mode and explicit status make that boundary visible.
- Local ports may be occupied: fail with a clear message, never stop another process/stand.
- A copied/tampered state or changed Docker context must fail closed rather than act on unrelated resources; ownership tests are required.
- Windows disk C is constrained. Worktree, npm cache, build output, temporary files and browser downloads for this task are on D; no broad disk cleanup is part of the implementation.

## Migration Plan

Add tooling/tests/docs only; existing production configuration stays default-off. Validate pure tests, actual container stack, positive browser journey/restart/owner guards, existing relevant frontend/Host tests and full PR CI. Publish separate PR into dev, retain it unmerged. Keep the older user demo untouched; use separate ports and resource identity. Archive/sync of this OpenSpec change and real-user rollout are not part of this PR.
