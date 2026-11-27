## ADDED Requirements

### Requirement: Retained local Petersburg saved-plan demonstration

An explicit local testing profile SHALL let an operator stop and resume the same owned Petersburg saved-plan workspace with its original database and protection keys. The ordinary demo and disposable inspection/E2E lifecycles SHALL retain their current semantics. Authentication and selection expiry SHALL use real time.

#### Scenario: Resume the same saved workspace with generation off
- **WHEN** the owner saves B, closes the browser, stops Host, Keycloak and PostgreSQL, then explicitly resumes the same retained profile in its supported historical-read mode
- **THEN** ownership, the database volume and protection keys are preserved. Trips reads are enabled while walking generation, option preview and the issue/save path are disabled. A new login opens the same ID from the list without a token; the saved revision, canonical snapshot digest and calendar agree before and after restart.
- **AND** the launcher verifies the owned configuration on both sides of the transition and rejects foreign or manually modified files. A catalogue change with generation still running does not satisfy this scenario.

#### Scenario: Cleanup is explicit in the retained profile
- **WHEN** the operator stops the retained profile
- **THEN** the database and keys persist for resume. Destruction requires an explicit owner-checked action. Existing disposable profiles still clean up their own resources on completion.
