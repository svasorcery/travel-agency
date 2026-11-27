## ADDED Requirements

### Requirement: Optional save authorization on the current successful preview

For an authenticated owner, an explicitly negotiated save-capable preview SHALL bind the exact validated brief and displayed option to a server-issued selection. Issuance SHALL have zero persistent or commercial side effects. The existing preview contract SHALL remain available to clients using its original request.

#### Scenario: Open the evening result without saving
- **WHEN** a save-capable client requests catalogue B and receives an eligible option
- **THEN** it can display a save action alongside that exact result. Merely generating or viewing it creates zero plans and zero durable receipts.
- **AND** changing the brief, starting a new selection, leaving the form or changing owner invalidates the old action in that client.

#### Scenario: Uncertain save already in flight
- **WHEN** the save of a displayed option becomes uncertain and the owner checks the issued ID or refreshes the plan list
- **THEN** those reads neither request a new preview nor replace the original selection. In the current session the traveler can check again or explicitly retry that selection; choosing to abandon recovery is a separate action.

#### Scenario: Partial availability
- **WHEN** the save capability is unavailable, the server cannot issue a protected selection, or the result is unsupported or noMatch
- **THEN** the existing preview outcome remains usable and the UI offers no actionable save for that result.

#### Scenario: Original client
- **WHEN** the caller requests the original preview format
- **THEN** the response has its original strict shape, outcome semantics, privacy and zero-write behavior.
