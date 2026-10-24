# Inspect and prepare Moscow data

Run from the repository root with Node 22+ and .NET SDK 10. On Windows use
`npm.cmd` instead of `npm`. This tool prepares local evidence and candidates;
it never selects a runtime release, provisions a stack or updates saved trips.

## Check publication and expiry

```sh
npm run trips:data -- check
```

The only network target is the public BBBike Moscow XML/gzip URL. `check` sends
one anonymous HEAD, refuses redirects, and separates `publicationStatus`
(`unchanged`, `newer`, `older`) from the installed v1's `currentStatus`
(`not-yet-valid`, `valid`, `expired`). Missing/future/invalid metadata or a failed
request returns nonzero; it is never reported as a successful unchanged source.
`newer` is only a publication hint until an actual download is hashed.

The current reviewed v1 expires at **2026-11-17T03:55:13Z**. Reading or rebuilding
does not renew that date. After expiry, generation becomes unavailable while
ordinary saved-plan reading/editing remains available. POI identity evidence has
its own original dates; maintenance does not automatically refresh them.

## Prepare a candidate when a new snapshot exists

Choose a new absolute directory below this checkout's `TestResults`. For example,
in PowerShell:

```powershell
npm.cmd run trips:data -- prepare --version 2 --output (Join-Path (Get-Location) 'TestResults/moscow-v2-candidate')
```

On POSIX:

```sh
npm run trips:data -- prepare --version 2 --output "$PWD/TestResults/moscow-v2-candidate"
```

If publication is unchanged or older, the command returns `no-new-publication`
without GET, a new output directory or a candidate. With a newer publication it:

1. Downloads once, with a ten-minute/200 MiB cap. The actual GET's publication,
   length, completion time and SHA256 are bound together; HEAD/GET disagreement,
   truncation, redirects and future metadata fail.
2. Requires different source bytes. A newer timestamp over the same SHA256
   produces `unchanged-source-bytes`, not renewed validity or a candidate.
3. Freezes one build instant and reuses the reviewed inventory with its original
   identity dates. Runs the existing offline builder twice under its unchanged
   graph, time, memory, rights and freshness rules.
4. Compares all six offered files byte-for-byte and checks the helper stayed
   unchanged during both builds. The strict production reader validates the result
   via the existing tool's read-only `package-check` command.
5. Writes `receipt.json` with input/helper/output hashes, exact as-of, measured
   build counts/time/peak working set and reader result. Before reporting success,
   it rechecks the reader's validity limit against actual completion time; expiry
   during preparation fails. Successful output is
   **`candidate-needs-review`**, never an accepted/released package.

The output contains `source.osm.gz`, retained build inputs, two build directories
and the receipt. Existing directories, foreign paths and reparse/junction paths
are refused. Failures stay failed; owned partial files are retained for inspection
and cannot be reused as a ready candidate. Use a different new output directory
for a separately requested attempt. There is no automatic retry or cleanup.

## Reproduce the current release

Keep the exact original raw snapshot outside Git. Its SHA256 must match v1's
input manifest; today's mutable download URL cannot promise an older snapshot.
Supply an existing local gzip and a new absolute output directory:

```powershell
npm.cmd run trips:data -- reproduce-current --osm 'D:/source-cache/Moscow.osm.gz' --output (Join-Path (Get-Location) 'TestResults/moscow-v1-reproduction')
```

This command is offline. It reuses the original manifest/inventory/build instant
and compares both builds with every reviewed v1 file. The original preparation
helper's real-clock expiry refusal remains enforced: historical `--as-of` is not
an expiry bypass. `moscow-v1.json` and `moscow-v1.manifest.json` are the existing
file-format names; candidate package identity is separately versioned in its
manifest/payload and eventual version directory.

`package-check` requires an absolute directory, explicit version, expected
manifest SHA256 and UTC `--as-of`; it only reads files and validates at that instant.
It does not load stack state, Host, a database or protection keys. Its
`validated-not-released` result is not human review or release permission.

## Later release PR

An actual v2 release remains deferred. A later PR must retain v1 unchanged and
review the new source, rights/attribution, path/anchor changes, coverage, expiry,
two-build identity and resource measurements. Update the trusted release descriptor,
composition/data copy path, public assets, API/client/UI derived-data links and this
maintenance tool's reviewed baseline coherently. Keep the old source offer and old
saved plans readable. Run relevant package, HTTP, browser and saved-plan regressions;
new data need their own acceptance evidence. Never repin v1, renew timestamps by
editing metadata, publish raw input as private-plan data or treat a candidate receipt
as target/human/rollout acceptance.

## Regression commands and evidence boundary

```sh
npm run test:trips-data
dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --maxcpucount:1 --filter "FullyQualifiedName~TripsPackageToolTests|FullyQualifiedName~TripsPackageLifecycleTests|FullyQualifiedName~TripsHostTests"
```

Node tests use synthetic HTTP/build seams and never query the live source in CI.
The lifecycle regression uses actual Program/Wolverine/EF/PostgreSQL/protection,
with fictional test identities/transports and a synthetic next package identity.
Application/source time changes while owned key validity keeps wall time. It
checks full saved JSON and protected-row equality after version change, expiry,
corruption and Host restart, then successful explicit manual editing. It does not
release a v2 or claim new map/human quality. Existing real-Keycloak full-stack CI
continues unchanged. The active scope and receipts are in
[the maintenance change](../../openspec/changes/trips-moscow-data-refresh/verification.md).
