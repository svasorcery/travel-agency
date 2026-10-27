# Spec Delta

## MODIFIED Requirements

### Requirement: Explicit Rail request within Trips
The system SHALL require explicit source-qualified station selections, departure date and transport mode for Rail search, and a separately selected existing plan day for staging. It SHALL preserve existing M1 city and day bounds and the separately declared city and protocol scope of walking generation.

#### Scenario: Independent travel and plan dates
- **WHEN** the owner selects a departure date and a different existing plan day
- **THEN** both meanings are displayed distinctly, with no inferred connection or feasibility claim and no automatic brief edit.

#### Scenario: No station guessing or private brief transfer
- **WHEN** the owner opens Rail in a plan
- **THEN** opening alone issues no source request, no city or implicit date is inferred, and no private title, wishes, adult count or budget is sent to Rail; existing source defaults remain unchanged.

#### Scenario: Existing manual geography remains valid
- **WHEN** a saved plan uses a city or duration outside the selected walking protocol scope
- **THEN** Rail can be inspected under its published RF station scope while walking generation remains unsupported.
