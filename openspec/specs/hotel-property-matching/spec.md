# hotel-property-matching Specification

## Purpose

Group verified references to the same physical hotel without turning similar descriptions or shared cards into interchangeable rooms, rates or purchases, while retaining provenance and uncertainty.

## Requirements

### Requirement: Separate property room rate and offer identity
The system SHALL distinguish canonical physical property, namespaced source property, source-specific room/rate and temporary stay offer. Source identity SHALL include provider, account boundary, environment and source ID. A canonical group SHALL NOT be a purchase key or evidence that its rates are equivalent.

#### Scenario: Same property different obligations
- **WHEN** two verified references identify one property but have different board, room description, cancellation or payment terms
- **THEN** one property card retains separately selectable source-specific offers and makes no interchangeability claim.

#### Scenario: Same ID across namespaces
- **WHEN** textual source IDs coincide across accounts/environments/providers
- **THEN** they remain distinct until the exact namespaces have valid reviewed correspondence.

### Requirement: Reviewed bounded matching evidence
Grouping SHALL require valid reviewed correspondence with both exact source references, independent physical-property evidence, reviewer/reason, checkedAt/validUntil, revision and permitted-use scope. The initial module-owned crosswalk SHALL be bounded to100 pairs/200 source properties; fictional evidence SHALL not imply real supplier usage rights.

#### Scenario: Verified fictional pair
- **WHEN** a current reviewed pair has consistent independent address/location/property evidence and a fictional-use scope
- **THEN** the fictional sources form one canonical card with attributable correspondence evidence and distinct offers.

#### Scenario: Real content without permission
- **WHEN** a candidate uses actual source data without applicable permitted mapping scope
- **THEN** it is not activated as an approved cross-source mapping or committed as a public fictional fixture.

### Requirement: Conservative doubt and conflict handling
Names, proximity, ratings and supplier text SHALL only suggest candidates. Missing, ambiguous, contradictory or expired evidence SHALL leave references separate with fixed status. Conflicting/duplicate mapping membership SHALL be rejected rather than creating transitive equivalence or choosing the cheaper interpretation.

#### Scenario: Nearby similarly named hotels
- **WHEN** two hotels share a name or close coordinates but verified correspondence is absent
- **THEN** both cards remain separate, without a silently merged price or inherited amenities.

#### Scenario: Ambiguous or expired evidence
- **WHEN** evidence is expired, one reference matches two physical properties or current facts contradict the correspondence
- **THEN** affected references remain unmerged and the reason is retained for API/UI/verification.

#### Scenario: Conflicting chain
- **WHEN** a proposed A-B and B-C chain conflicts with A-C or duplicates membership in the bounded pair model
- **THEN** the invalid mapping set is rejected and affected properties are rendered separately, without transitive collapse.

### Requirement: Attributed facts and preserved offer variants
Displayed facts SHALL retain source and scope. Card presentation SHALL choose a stable attributed source reference without synthesizing superior combined facts. Cross-source offers SHALL never be deduplicated by property identity. Within-source deduplication SHALL require equal identity and material facts; conflicting duplicate keys SHALL be quarantined with Limited/Conflict.

#### Scenario: Conflicting scores or benefits
- **WHEN** suppliers report different rating scales, beds or breakfast coverage
- **THEN** their facts retain attribution/unknowns and one source's benefit is not promised for another source's offer.

#### Scenario: Duplicate versus conflicting rate
- **WHEN** an exact source rate is repeated with identical facts or the same source key is repeated with different money/terms
- **THEN** only the identical repeat may collapse; conflicting versions are not selectable, the source is Limited/Conflict and no cheapest version is silently chosen.

### Requirement: Mapping revisions cannot rewrite purchase history
Search/group selections SHALL retain mapping revision and source references. Before quote or unsubmitted admission, changed/expired material mapping SHALL require fresh selection/review. Accepted admitted facts SHALL remain source-bound historical facts independent of later grouping, expiry or removal.

#### Scenario: Correspondence changes before purchase
- **WHEN** a reviewed mapping changes or expires before an unsubmitted quote is admitted
- **THEN** stale selection/consent is rejected with a refresh reason and no create occurs.

#### Scenario: Correspondence removed after admission
- **WHEN** the mapping is removed after a booking attempt was admitted
- **THEN** the original source property/context/barrier and authoritative private outcome survive, with no replacement purchase or historical regrouping.
