# Spec Delta

## Purpose

Let a developer inspect, prepare and reproduce a bounded Moscow data package with explicit provenance and expiry, while preserving historical saved trips and keeping release activation separate.

## ADDED Requirements

### Requirement: Explicit publication inspection
Maintenance SHALL report the reviewed package identity and validity separately from the upstream publication metadata. Inspection SHALL be read-only, bounded, and restricted to the configured public Moscow source. It SHALL distinguish unchanged, newer and older publication from request failure; metadata alone SHALL NOT establish new accepted data.

#### Scenario: No new snapshot
- **WHEN** the source still reports the reviewed publication
- **THEN** inspection reports unchanged publication and preparation produces no new package or renewed validity.

#### Scenario: Unusable source response
- **WHEN** publication metadata are missing, malformed, future-dated or the source redirects/fails
- **THEN** inspection fails explicitly without releasing data or treating failure as an unchanged successful source.

### Requirement: Bound acquisition and candidate preparation
Preparation SHALL require a new version, a strictly newer publication and different source bytes. Publication time, download time, size and SHA256 SHALL bind the same successful download. Download and build resources SHALL be bounded. Output SHALL use a new owned location; existing releases and unrelated files SHALL NOT be overwritten.

#### Scenario: New publication with old bytes
- **WHEN** the publication advances but the downloaded SHA256 matches the reviewed source
- **THEN** no candidate is emitted and source validity is not renewed.

#### Scenario: Bounded valid input
- **WHEN** a different, newer snapshot fits the accepted size, graph, inventory and freshness constraints
- **THEN** preparation retains exact input provenance and emits only an unreviewed candidate, with validity derived from source publication and original identity evidence.

#### Scenario: Unsafe output or interrupted acquisition
- **WHEN** output already exists, crosses a symlink/junction, is outside the owned area, or download truncates/exceeds its limit
- **THEN** preparation fails without overwriting existing files or presenting partial output as an accepted candidate.

### Requirement: Reproduction before acceptance
Preparation SHALL build twice from identical retained source bytes, inventory, helper identity and a fixed build instant, compare every offered output byte and validate the candidate with the actual strict package reader. A bounded receipt SHALL identify inputs, outputs, elapsed time and observed resource facts. Reproduction SHALL NOT relax freshness rules or fabricate new source dates.

#### Scenario: Different build outputs
- **WHEN** the two builds disagree or strict package validation fails
- **THEN** the operation remains failed and no ready candidate is reported.

#### Scenario: Expiry during preparation
- **WHEN** the source or original identity evidence reaches its validity limit before preparation completes
- **THEN** the operation fails even if validation at the frozen build instant succeeded.

#### Scenario: Reproduce the current release
- **WHEN** the developer supplies the exact retained current raw snapshot before its permitted preparation expiry
- **THEN** two builds using the current release's original inputs and build instant reproduce its offered files exactly, without changing the installed package.

### Requirement: Historical trip compatibility
Verification SHALL prove that saved candidate-derived intentions retain their full content, IDs, order, revision, owner, timestamps and protected payload across a package-version change, source expiry/unavailability and Host restart. Source changes SHALL NOT rewrite historical plans; explicit manual edits SHALL remain possible under ordinary revision rules.

#### Scenario: Source changes after save
- **WHEN** a saved v1-derived trip is reopened after selecting a distinct test release or making generation unavailable
- **THEN** its authoritative representation and protected row remain identical, and a later explicit manual edit succeeds without source access being required for that edit.

### Requirement: Separate candidate and release authority
Tooling SHALL NOT change runtime release pins, public asset selection, installed packages, schema, keys or feature flags. Candidate preparation SHALL NOT claim human quality or release approval. Ordinary CI SHALL run offline maintenance and historical-compatibility regressions without querying the live source. A real v2 release SHALL remain deferred until new source data and a separate accepted release exist.

#### Scenario: Prepared candidate
- **WHEN** all preparation checks pass
- **THEN** the result is labelled candidate-needs-review and identifies the evidence required for a later source/data release PR; current users and saved plans remain unaffected.

#### Scenario: Unchanged current source
- **WHEN** this change is delivered while the real source is unchanged
- **THEN** v1 bytes, runtime pin, original validity and default-off behavior stay unchanged and no v2 package is installed.
