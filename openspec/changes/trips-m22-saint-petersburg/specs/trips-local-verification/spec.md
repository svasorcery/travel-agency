# Spec Delta

## MODIFIED Requirements

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

## ADDED Requirements

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
