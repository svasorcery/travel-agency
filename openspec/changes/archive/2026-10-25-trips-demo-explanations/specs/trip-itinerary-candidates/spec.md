# Spec Delta

## ADDED Requirements

### Requirement: Daily selected-interest explanation
The preview SHALL distinguish whole-trip interest coverage from each day's represented and absent selected interests, derived from that candidate's selected-interest set and stop topics. Each topic SHALL appear once in stable catalogue order. Empty selections and days without matching stops SHALL be explicit. This explanation SHALL NOT change coverage outcomes, selection or saved content, or imply human quality.

#### Scenario: An interest appears on a different day
- **WHEN** a selected interest is covered somewhere in the candidate but no stop in one day has that topic
- **THEN** the whole-trip summary retains its coverage and that day explicitly lists the interest as absent without replacing it with unselected topics.

#### Scenario: Repeated topics
- **WHEN** several stops in a day share a selected topic
- **THEN** the day lists that topic once in stable catalogue order.

#### Scenario: No selected interests or no eligible stops
- **WHEN** the saved brief selected no interests or the candidate contains an empty day
- **THEN** the former displays that interests were not selected and the latter represents no selected interest, without inventing route facts or permitting adoption of an empty outcome.

### Requirement: Precise existing walking scenario explanation
The preview SHALL explain the existing model: lower is rounded-up walking time at 75 metres/minute with a one-minute minimum; upper uses 45 metres/minute and adds one minute per mapped road crossing only to that upper scenario. It SHALL retain the modelled/uncalibrated caveats and original package assumptions, without rewriting released data or implying measured or guaranteed walking time.

#### Scenario: A mapped crossing
- **WHEN** a preview explains an edge with one or more mapped road crossings
- **THEN** the UI states that the crossing allowance applies only to the upper scenario while preserving the supplied interval and original provenance text.

#### Scenario: Unchanged released package
- **WHEN** the UI explanation is updated
- **THEN** v1 bytes, hashes, validity, strict loading, API data, candidate selection and explicit adoption/save behavior remain unchanged.
