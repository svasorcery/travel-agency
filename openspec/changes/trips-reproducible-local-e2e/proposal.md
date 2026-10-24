# Proposal

## Why

The real local Trips walkthrough exposed a Host transaction-composition failure that the mocked browser contour did not catch. PR69 fixed that defect, but the full Keycloak/Host/PostgreSQL/restart proof still depends on ignored one-off files and manual browser actions.

## What Changes

- Add a maintained, opt-in local stack with fresh fictional users, separate encrypted key rings and PostgreSQL data, loopback-only ingress and no external provider/model access.
- Provide explicit up/status/restart/stop/destroy and E2E commands with ownership checks and persistent local state across ordinary stops/restarts.
- Run the ordinary Angular client-rendered Trips UI against real Keycloak code+PKCE, actual Host endpoints, the pinned Moscow generator, real EF/protection and PostgreSQL.
- Add a browser journey covering create, preview/adopt, edits, explicit save, reopening, complete Host/Keycloak/PostgreSQL process restart and a new authenticated read of the same state; also check foreign ownership and stale revision refusal.
- Keep interactive demo on wall-clock time; use a documented fixed application clock only in the isolated E2E runner so data expiry does not silently turn the positive scenario into a skip. JWT validation stays real-time and normal Host has no clock override setting.
- Add the full-stack test to ordinary PR/dev CI and update the Trips completion documentation with the exact acceptance boundary.

## Capabilities

### New Capabilities
- `trips-local-verification`: owned local stack lifecycle and repeatable real-stack Trips browser/restart evidence.

### Modified Capabilities
None. Existing trip-plan-workspace and trip-itinerary-candidates business contracts, data pins, production defaults and frozen route-quality corpus are unchanged.

## Impact

Tools under `tools/trips`, one tool-only .NET project, dedicated Playwright config/spec, narrowly allowlisted local OIDC origins, CI/inventory/package commands and documentation. No database migration is added; the existing schema is explicitly provisioned only into a newly owned demo database. No production deployment, merge, real user, supplier or paid model enablement is included. The user authorized implementation and a separate PR into dev without merge.
