# trips-local-verification Specification

## Purpose

Provide repeatable, isolated local and CI verification of the real Trips browser journey, identity boundary and protected persistence across service restarts without enabling real-user traffic.

## Requirements

### Requirement: Explicit isolated stack lifecycle
The verification tooling SHALL start only explicitly requested, owned local resources with fictional identities, separate data/keys and loopback ingress. It SHALL preserve data on ordinary stop/restart and SHALL refuse foreign state, resources, remote daemons and occupied ports before mutation.

#### Scenario: Fresh start
- **WHEN** the developer explicitly starts a fresh local demo with prerequisites installed
- **THEN** the real services and ordinary browser UI become ready using newly provisioned fictional users and keys, and safe access instructions identify the local URLs and private credential file.

#### Scenario: Resume and stop
- **WHEN** the owner stops and later starts the same valid demo state
- **THEN** the same database, users and protection keys remain in use and saved plans remain readable.

#### Scenario: Foreign target or port collision
- **WHEN** state ownership, daemon identity, resource labels or required port availability do not match
- **THEN** the command fails without changing that state, deleting resources or stopping another service.

### Requirement: Real application boundaries
The full-stack test SHALL use the ordinary UI, actual Host composition, real OIDC code with PKCE, real JWT validation, the shared generator with the selected pinned city package and PostgreSQL/protection. It SHALL NOT substitute API responses, authentication, stores or provider data to make the journey pass.

#### Scenario: Authenticated flow
- **WHEN** a fictional owner signs in and creates a trip
- **THEN** Keycloak issues the identity and the real Host persists the protected plan; anonymous private access receives 401.

#### Scenario: Default installation
- **WHEN** the application is run without explicitly starting the verification profile
- **THEN** this tooling does not provision resources or enable Trips, suppliers or models for real users.

### Requirement: Honest deterministic test time
Interactive demo SHALL use real time. Each city E2E profile SHALL identify its package-compatible fixed application/browser time separately from real wall time and SHALL retain real-time identity validation. It SHALL preserve the immutable dataset and freshness rules, and SHALL NOT present fixed-time integration success as current-data or route-quality acceptance.

#### Scenario: Later CI execution
- **WHEN** the same E2E runs after the real-world dataset expiry
- **THEN** its positive journey still uses the declared fixed test instant and valid fresh owned keys, without editing source dates, extending dataset TTL or skipping generation.

#### Scenario: Expired interactive data
- **WHEN** the real-time demo package expires
- **THEN** generation reports the existing unavailable outcome while saved manual plans remain usable and status explains the data boundary.

### Requirement: Complete explicit browser journey
E2E SHALL verify creation, real candidate generation, review/adoption, user edits, explicit save and authoritative reopening. It SHALL compare stable IDs, day/order placement, text, time and exact money and SHALL prove generation/adoption did not persist changes before Save.

#### Scenario: Moscow edited plan
- **WHEN** the test creates a two-adult three-day Moscow plan, adopts nine stops, edits text/time/amount and reorders/moves blocks
- **THEN** only explicit Save advances revision and reopening reproduces the complete accepted state.

### Requirement: Persistence after complete service restart
The test SHALL stop and restart Host, Keycloak and PostgreSQL with the same owned storage and keys, prove that all three processes stopped and restarted, and authenticate in a fresh browser context before reading the saved plan. It SHALL compare the authoritative plan and protected-row/key fingerprints across the restart.

#### Scenario: Fresh login after restart
- **WHEN** the three services have exited and started again
- **THEN** the original fictional user can log in again and read identical accepted fields, block IDs/order, revision, owner, timestamps, ciphertext fingerprint and protection keys.

### Requirement: Real owner and revision refusals
Full-stack acceptance SHALL include a second real OIDC identity and a stale revision. Foreign reads/mutations and stale writes SHALL retain their existing refusals and SHALL NOT change the owner's saved plan.

#### Scenario: Foreign user
- **WHEN** the second fictional identity lists, reads, updates or deletes the first owner's plan
- **THEN** lists exclude it and detail/PUT/DELETE return 404 without changing the plan.

#### Scenario: Stale update
- **WHEN** the owner submits an older If-Match after a successful save
- **THEN** the Host returns 412 and the saved snapshot remains unchanged.

### Requirement: Private evidence and owned cleanup
The runner SHALL export bounded, sanitized evidence with exact source/profile identity and stage outcomes. Credentials, bearer tokens, cookies, key material, private state and browser traces SHALL NOT appear in published artifacts. Fresh E2E resources SHALL be cleaned up on success/failure; failed verification or cleanup SHALL produce a nonzero result.

#### Scenario: Failed test
- **WHEN** a browser or restart assertion fails
- **THEN** the run remains failed, preserves sanitized diagnostics, removes only resources created for that run and does not retry private writes or relabel the result.

#### Scenario: Explicit local destruction
- **WHEN** the developer explicitly confirms destruction of a valid owned local demo
- **THEN** only its proven resources and private state are removed; unrelated resources are untouched.

### Requirement: Required ordinary CI coverage
Ordinary PR and development-branch CI SHALL execute the real Trips full-stack verification. The existing final PR gate SHALL depend on its success, and structural CI validation SHALL detect accidental omission. Paid/model execution SHALL remain outside this workflow.

#### Scenario: Full-stack regression
- **WHEN** a real Trips integration or restart behavior regresses
- **THEN** the ordinary CI run fails even if the mocked browser tests and component tests pass.

### Requirement: Two city real service acceptance
Ordinary full-stack verification SHALL retain the complete Moscow journey and add a Saint Petersburg journey using actual pinned packages, original responses, real Host/Keycloak/PostgreSQL and explicit save. Both city profiles and owned cleanup SHALL be required by the CI aggregate gate. The existing interactive demo and retained human materials SHALL NOT be altered by these tests.

#### Scenario: Saint Petersburg adoption and save
- **WHEN** the fictional owner generates a three-day balanced Saint Petersburg candidate at its valid fixed time
- **THEN** nine distinct eligible stops are observed from one original candidate POST without replay/substitution, adoption produces no write, and explicit save/reopen preserves edits, order, exact money and IDs.

#### Scenario: Both cities after restart
- **WHEN** the Saint Petersburg run saves a historical Moscow plan through real M1 endpoints and the generated Saint Petersburg plan, then fully restarts Host, Keycloak and PostgreSQL
- **THEN** fresh OIDC login recovers both identical authoritative plans and protected rows/keys, and foreign-owner and stale-revision refusals are proven for both cities.

#### Scenario: Nonoverlapping package windows
- **WHEN** the retained Moscow test instant precedes the Saint Petersburg build or the two package validity windows do not overlap
- **THEN** the Moscow profile retains its original fixed instant and assertions, Saint Petersburg uses a separate declared valid instant and dates, and historical Moscow preservation in that run does not claim new Moscow generation.

#### Scenario: One city or cleanup fails
- **WHEN** either full-stack city profile or owned cleanup fails
- **THEN** ordinary CI acceptance fails even when the other profile or all mocked browser checks pass.
