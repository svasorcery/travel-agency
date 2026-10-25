# Design

## Context

The approved scope is mechanism and verification now, v2 later. Fresh base is `6f50ce5796f2ed3d559ce69b8ec5adc579d732a1`; the existing isolated D worktree is reused on `codex/trips-moscow-package-refresh`. BBBike HEAD and CHECKSUM on 2026-10-24 still identify the 2026-10-18 03:55:13Z / 162060454-byte input (SHA256 `f03e23186dbcfb6e1add78e75425cd8654dc36a79fa08c546da7abf448e00c97`). No new release can be claimed from that observation.

`build-moscow.cs` already supports explicit version/as-of, immutable absent output, input hashes, fixed profiles and limits. It rejects expired snapshots against both declared and real build time. `CuratedMoscowRelease` protects the exact v1 manifest and permits an explicit distinct caller-owned descriptor. `CuratedMoscowFacts` is the strict reader. Production composition and UI currently select v1; their constants/URLs and all six offered v1 files stay unchanged. Existing snapshot data contains historical notes, not a live foreign key to the source package.

## Goals / Non-Goals

**Goals:** one bounded developer workflow, reproducible evidence, honest unchanged/expiry behavior and real persistence regression. Reuse existing offline builder, strict reader and actual-Host fixture.

**Non-goals:** ship/activate v2, extend TTL, refresh POI identities without checking them, change routing/model/policy, create a source registry/scheduler, add dependencies or migrations, modify retained demos, run providers/models or claim human quality. The user authorized public source inspection/acquisition and owned disposable verification, followed by a PR without merge. The generic old OpenSpec pilot context is not a new prohibition on those explicitly authorized checks.

## Decisions

### Commands and source boundary

Add `tools/trips/moscow-refresh.mjs` and `npm run trips:data -- <command>`:

- `check`: HEAD only to the exact BBBike Moscow XML/gzip URL; reports current reviewed identity/expiry, current clock and `publicationStatus=unchanged|newer|older`. Missing/invalid/future Last-Modified, non-200, redirects and invalid/missing or >200MiB Content-Length fail. A newer HEAD is only a publication hint, not proof of changed bytes.
- `prepare --version 2 --output ABSOLUTE_NEW_TESTRESULTS_DIRECTORY`: require a positive numeric version greater than current `1`. If HEAD is not newer, return an explicit no-candidate result without GET, directory creation or building. Otherwise download once (redirect refusal, timeout 10 minutes, 200MiB stream and length caps), bind actual GET Last-Modified/download time/SHA256, and require the GET publication still be newer and unexpired. Reject future/missing metadata and changed publication during HEAD/GET. If its SHA256 matches v1, report unchanged bytes with no candidate. Preserve current inventory bytes and original identity timestamps.
- `reproduce-current --osm ABSOLUTE_LOCAL_GZIP --output ABSOLUTE_NEW_TESTRESULTS_DIRECTORY`: offline, verifies the exact current source hash/size, reuses v1 input manifest/inventory/as-of/version and requires both builds to match all six reviewed files. Existing helper wall-clock expiry refusal stays intact; this is not an after-expiry bypass.

Use native Node modules and fetch only. Fixed source URL has no configurable host, redirects, credentials or cookies. Test injection is a module-call seam, not CLI/environment URL or acceptance overrides. Source inspection has a 15-second deadline. Output is restricted below this checkout's physical `TestResults`, all existing ancestors must be non-reparse directories, target must not exist; create exclusively and preserve failed owned output for diagnosis. No recursive deletion, activation, Git write or global cleanup command is added. Raw source and preparation outputs stay ignored.

### Preparation and proof

The Node module pins the existing v1 manifest identity, validates its file digest map before using metadata, and tests that the pin agrees with the existing reviewed descriptor. Freeze one UTC as-of after acquisition; metadata order is publication <= download <= build. Keep original inventory identity ages and all existing graph/model/rights constraints. Run `build-moscow.cs` twice into new child directories with identical arguments; compare the exact six files and their manifest identities, not only parsed JSON. Filenames `moscow-v1.*` describe the existing format; a future package version lives in its own versioned directory and is not silently renamed here.

Add read-only `package-check --directory ... --version ... --manifest-sha ... --as-of ...` to `Travel.Trips.LocalStack`. Dispatch it before stack state loading. It validates named arguments/UTC/hash/version, invokes `CuratedMoscowFacts` with an explicit descriptor and clock, and emits bounded identity/count/expiry facts or a fixed refusal. It does not resolve Host, DB, keys or external transports. This validates an unreviewed candidate's integrity/semantics, not release authorization. Existing provision/schema/serve commands retain their state gates.

Record source metadata/hash, frozen as-of, inventory/helper hashes, output hashes, build stdout's bounded measured counts/timing/memory, strict-reader result and actual wall start/end. Child processes receive argument arrays, hidden windows and bounded duration/output; failures remain failures. No raw exception/body/header dump is public. Candidate output is `candidate-needs-review`; later acceptance still needs source/rights/path/coverage review, compatibility evidence and an explicit release-pin/asset change in a later PR. No frozen Q corpus is retuned or relabelled by maintenance checks.

Before reporting either candidate or current-reproduction success, compare actual completion time with the strict reader's `releaseValidUntil`. Reaching the source/identity expiry during either build or validation is a failed operation even though the frozen validation instant was valid.

### Compatibility proof

Extend the existing actual Program/Wolverine/PostgreSQL/protection fixture with optional test-only source/clock registration. Preserve its current default behavior. A new integration test starts with actual v1 files, generates and saves an itinerary through HTTP, captures the full response and raw protected row, switches to an explicitly synthetic next-release package, restarts Host, and verifies a newly empty plan can observe the selected version while the old saved plan is unchanged. Advance the test clock beyond expiry, then corrupt/withdraw the package: generation/availability fail, old GET and row remain exact. Finally a normal owner PUT edits the old plan with CAS and succeeds. Identity/transports remain fictional in this fixture, as before; this is actual Host/DB persistence proof, not additional Keycloak/target acceptance. Existing real Keycloak full-stack CI remains required and unchanged.

The synthetic release is test-only and never enters the canonical data directory or acceptance receipts as v2. Its different identity tests source replacement, not new city facts or increased freshness.

## Risks / Trade-offs

- Upstream can change between HEAD and GET: compare metadata and bind hash/publication to that GET; reject races and require an explicit new invocation.
- Metadata may advance without new bytes: require a different hash before candidate preparation; no TTL renewal from a repack.
- Inventory identity evidence can age out before a new map: preserve its timestamps; the existing builder/reader must refuse rather than refresh dates automatically.
- File/path races: validate real ancestors and exclusive target creation; this is an owner-local developer tool, not a hostile multi-user filesystem service.
- A source-free CI cannot prove future source coverage: use synthetic acquisition/build cases in CI and a real current-input reproduction receipt now. Actual v2 still requires its own preparation/review evidence.

## Delivery and rollout

Keep one canonical OpenSpec corpus. Run focused Node/tool/Host checks and strict OpenSpec/harness validation, independent review, real unchanged-source inspection and byte-exact current release reproduction. Then publish a separate PR into dev and await green ordinary CI. No merge, deployment, target activation, scheduled check or real v2 release. Preserve the original source observations and any failed attempt.
