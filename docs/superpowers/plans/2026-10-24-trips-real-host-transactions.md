# Trips real Host transaction fix

## Problem and scope

On dev `8fc9f8e2e05563b7dc8a6f80f273dfc2b0fc518d`, `POST /api/trips` returned 503 in the real Host and committed no plan. Wolverine automatic transaction middleware opened an EF transaction before `TripPlanStore.TryCreateAsync` attempted its own transaction. The observed exception was `The connection is already in a transaction and cannot participate in another transaction.`

The lightweight HTTP fixture did not include the actual Host transaction policies. The regression therefore belongs in the actual-Host integration project, with real PostgreSQL and protection.

Contract: [trip-plan-workspace](../../../openspec/specs/trip-plan-workspace/spec.md).

## Transaction ownership

Only `CreateTripPlanEndpoint.Post` opts out of Wolverine automatic transactional middleware with `[NonTransactional]`. `TripPlanStore` retains its PostgreSQL transaction, transaction-scoped owner-capacity advisory lock, 100-plan limit and explicit commit before success. Other endpoints and global Host policies are unchanged.

## Completed implementation

- [x] Add an actual-Host fixture using `Program.cs`, real EF/PostgreSQL and the existing test-owned encrypted key ring. Clear configuration to isolated in-memory values; substitute only fictional identity and external transports.
- [x] Reproduce the failure before changing production code: create expected 201 but returned 503; concurrent last-slot admission accepted zero requests.
- [x] Add the scoped endpoint annotation and document store transaction ownership.
- [x] Verify protected create/edit/save/reopen, stale PUT 412, foreign GET 404 and identical response/ciphertext after a new Host/service provider.
- [x] Verify that two concurrent creates at 99 plans yield one 201, one capacity 409 and exactly 100 rows.
- [x] Complete independent read-only review: no substantive findings.

## Local verification, 2026-10-24

| Check | Result |
|---|---|
| New actual-Host regressions before fix | 0/2; expected failures |
| Same regressions after fix | 2/2 |
| Trips Unit | 294/294 |
| Trips Integration | 226/226; unchanged frozen route-evaluation corpus excluded locally |
| AI harness | 134/134; validation passed |
| CSharpier and git diff check | Passed |

The Host test project retains its known RouteHandlerAnalyzer AD0001 warnings; analyzers were not disabled. Ordinary PR CI is tracked separately by the pull request.

## Real local demo acceptance

An isolated loopback-only environment used ordinary Angular authentication, real Keycloak Authorization Code + PKCE S256, the actual Host, PostgreSQL and new demo-owned encrypted key rings. Existing user data, credentials and services were not reused.

The Moscow 25–27 October plan for two adults passed create, actual nine-stop generation, explicit adoption, title/note/time/amount edits, block reorder/day move, save and reopening. Generation/adoption left revision 1 in the database until explicit Save, which produced revision 2.

Host, Keycloak and PostgreSQL were stopped with confirmed PID 0 and then started with the same persistent volume and keys. After browser reload and a fresh OIDC code exchange, all 68 form controls, nine block IDs, order, revision 2, ciphertext SHA256, owner, updated timestamp and Trips key hashes matched.

This proves graceful restart of the isolated demo. It does not establish disaster recovery, target rollout, real-user enablement, supplier integration, field calibration or human route quality. The implementation and demo remain separate from merge approval; this change is published as a PR into dev without merging.
