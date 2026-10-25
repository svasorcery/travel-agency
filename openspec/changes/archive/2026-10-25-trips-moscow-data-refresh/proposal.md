# Proposal

## Why

The Moscow demo has a reviewed immutable v1, but acquiring and preparing a later package is a manual sequence without one bounded verification receipt. On 2026-10-24 BBBike still publishes the same 2026-10-18 snapshot; the user explicitly chose to implement the mechanism and checks now and defer v2 until a new snapshot exists.

## What Changes

- Add explicit developer commands to inspect publication/freshness, prepare an unreviewed candidate from a strictly newer and different source, and reproduce the current package from its retained raw input.
- Bind metadata to the actual download, preserve source-derived expiry, run the existing offline builder twice, compare exact output bytes and validate through the production package reader.
- Verify that an existing saved itinerary survives a package-version change, package expiry/unavailability and Host restart, and remains manually editable.
- Document acquisition, reproduction, review and later release steps with measurable runtime/size receipts; keep v1 bytes, runtime pin, URLs and defaults unchanged in this PR.

## Capabilities

### New Capabilities

- `trip-data-package-maintenance`: bounded, explicit offline package preparation and reproduction with honest freshness and existing-plan compatibility evidence.

### Modified Capabilities

None. Existing `trip-itinerary-candidates` freshness/immutability and `trip-plan-workspace` historical editing contracts remain unchanged; added regressions exercise them across a source change.

## Impact

Developer tooling under `tools/trips`, the existing LocalStack tool's read-only package validation, focused Node and actual-Host/PostgreSQL tests, package commands/ordinary CI, and documentation. No new dependencies, migrations, production services or automatic updater. No v2 data release, real-user enablement, merge, provider/model/payment calls or changes to retained demos. Public BBBike downloads and explicitly owned disposable tests are within the requested preparation/verification scope.
