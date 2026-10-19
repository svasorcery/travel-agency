# Hotel offer comparison and ranking

## Purpose

Compare stay obligations and order offers reproducibly using exact known evidence, with clear explanations and explicit limits when currencies, price basis or conditions cannot be compared.

## ADDED Requirements

### Requirement: Explicit price basis and completeness
Money SHALL retain exact decimal/currency, stay/room/guest scope, provenance and pricing basis. Supplier-payable debit, optional traveller price and local obligations SHALL remain distinct. Net SHALL NOT be labelled retail or assigned invented markup. Missing tax/basis/amount evidence SHALL remain unknown; ranking SHALL not replace consent debit.

#### Scenario: Hotelbeds Net amount
- **WHEN** the supported Net profile reports whole-stay net200 with no established traveller selling price
- **THEN** supplier-payable is200, retail remains unknown, and UI labels the sum to the supplier without claiming a final retail price or subtracting commission again.

#### Scenario: Pricing model or tax feature absent
- **WHEN** pricing profile or mandatory-local-obligation evidence is unavailable, including an inactive tax feature
- **THEN** missing evidence is not zero, the offer lacks a complete comparable total and unsupported purchase has a visible reason.

### Requirement: Whole-stay comparison without implicit conversion
Comparable totals SHALL require equal requested dates/occupancy, proven equal pricing basis and complete known obligations in one currency. Included components SHALL not be re-added or multiplied by nights/guests. Separate mandatory obligations SHALL retain actual currencies; unknown/mixed-currency totals SHALL not receive a fabricated grand total or cheapest badge.

#### Scenario: Included tax and separate same-currency payment
- **WHEN** GBP200 includes GBP20 tax and a separate known GBP10 is due at the property for the whole stay
- **THEN** debit remains GBP200, comparison total is GBP210, and two guests/two nights do not multiply it.

#### Scenario: Mixed currencies and unknown payment
- **WHEN** GBP200 has a separate EUR20 obligation or a mandatory unknown amount
- **THEN** each actual amount/unknown is shown and neither is ranked as a complete GBP200 stay or treated as a free supplement.

#### Scenario: Net versus selling price
- **WHEN** one offer is supplier Net and another is known traveller selling price
- **THEN** they remain in different basis sections without a lower-cost claim across them, even in the same currency.

### Requirement: Structured and scoped condition comparison
Board/benefits and cancellation SHALL retain structured source evidence, scope and unknowns. Cancellation deadlines SHALL retain supplied offsets/timezone and monetary scope. Display text SHALL not be parsed into stronger rules. Missing policy SHALL differ from explicit non-refundable; comparison SHALL not imply refund payout or cancellation capability.

#### Scenario: Unknown breakfast coverage
- **WHEN** one rate establishes breakfast for one guest and another omits guest coverage
- **THEN** neither is displayed as equivalent breakfast for two adults, and no pricing/ranking bonus invents that coverage.

#### Scenario: Missing cancellation and source-local deadline
- **WHEN** rates have missing policy, explicit non-refundable evidence or a supplied offset deadline
- **THEN** those cases stay distinct, the deadline is not converted using browser assumptions and UI makes no customer-payout promise.

### Requirement: Deterministic offer order
Policy `hotel-cost-first-v1` SHALL rank within a comparable basis/currency bucket by complete whole-stay obligation total, then stable ordinal semantic source identity/material fingerprint. Incomparable offers SHALL have a stable identity order without hidden price ordering. Arrival, random tokens, culture, commission, brand, bookability and review scales SHALL not create ranking bonuses.

#### Scenario: Reordered inputs and equal totals
- **WHEN** the same semantic offers arrive in different source order, use newly issued tokens or run under another culture with equal totals
- **THEN** semantic ordering and reasons are identical, with stable tie-break distinguished from a factual advantage.

#### Scenario: Cheaper but different conditions
- **WHEN** a lower known total has less inclusive board or cancellation than another rate
- **THEN** price order may put it first only within its comparable money bucket, while both condition differences remain visible and no equivalence or best-overall claim is made.

### Requirement: Group prices retain comparison scope
Each physical group SHALL appear as one card with all offers and one minimum per comparable bucket. Its primary section SHALL be the ordinal-minimum comparable (basis,currency) key, or incomparable when none exists. Section order SHALL be navigation only; within a section use its primary minimum then stable group identity. Other scoped minima SHALL remain visible without one cross-currency from-price.

#### Scenario: Group contains GBP and EUR
- **WHEN** one property has complete GBP and EUR alternatives
- **THEN** separate scoped minima are displayed and navigation order does not claim the currencies are economically ordered or converted.

#### Scenario: Unmatched or incomplete property
- **WHEN** another property has only unknown/mixed obligations
- **THEN** it remains visible in a labelled incomparable section in stable identity order rather than disappearing or being called more expensive.

#### Scenario: One card across multiple currencies
- **WHEN** one group has complete EUR and GBP buckets and another has only GBP
- **THEN** the first appears once under its ordinal EUR primary section with both minima visible, the second under GBP, and neither section order nor the primary choice claims that EUR is cheaper or better than GBP.

### Requirement: Explainable versioned ranking snapshot
Snapshots/API/UI SHALL retain ranking version, comparison scope and factual reasons from the same normalized data. Reasons SHALL explain known total, limits and ties in plain language. Any comparator change SHALL version the policy and its golden scenarios; old snapshots SHALL not be silently reranked.

#### Scenario: Known total and limitation reasons
- **WHEN** ranking uses190 GBP supplier amount plus20 GBP known separate payment or cannot establish the payment
- **THEN** UI explains210 GBP for the first and inability to compare a total for the second, without claiming that190 GBP is the full cost.

#### Scenario: Policy changes after search
- **WHEN** a different ranking policy is installed after a snapshot was issued
- **THEN** the old snapshot retains its recorded order/version until expiry, and a new search uses the new policy without rewriting an admitted purchase.
