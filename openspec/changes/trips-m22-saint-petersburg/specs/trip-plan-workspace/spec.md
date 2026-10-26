# Spec Delta

## ADDED Requirements

### Requirement: Existing catalogue snapshot compatibility during coverage expansion
Adding itinerary coverage for an existing city SHALL preserve catalogue version 1 and the protected plan schema. Old Moscow and Saint Petersburg manual plans and new Saint Petersburg intentions SHALL remain readable without source access or rewriting historical fields. Coverage changes SHALL NOT weaken strict snapshot/HTTP/client validation, owner privacy or revision rules.

#### Scenario: Preexisting protected snapshots
- **WHEN** snapshots produced before the second itinerary city are read by the updated application and client
- **THEN** full detail, historic destination metadata, IDs/order, exact amounts, owner, revision and timestamps survive canonical/protection/client round trips unchanged.

#### Scenario: Package state and service restart
- **WHEN** either city's package is replaced, expires, is withdrawn or becomes corrupt and Host/Keycloak/PostgreSQL restart with the same owned data and keys
- **THEN** saved plans for both cities retain their authoritative representation and protected-row/key fingerprints, and later explicit manual editing still follows ordinary CAS.
