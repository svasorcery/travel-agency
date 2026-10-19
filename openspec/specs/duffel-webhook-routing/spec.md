# Duffel webhook routing Specification

## Purpose

Receive current Duffel notifications at one public URL and route them to isolated module owners while preserving durable acceptance and compatible reading of historical Flights inbox evidence.

## Requirements

### Requirement: One bounded compatible public entry
Flights and Hotels SHALL share the existing `/webhooks/duffel` URL per organisation/mode. It SHALL accept one signature and bounded exact raw bytes in RAM without disk spill. Established signature/payload error responses SHALL remain compatible; a second supplier URL SHALL not be required. Transport SHALL not parse provider business data.

#### Scenario: Existing URL and bad transport
- **WHEN** shared ingress replaces Flights-only transport, or receives an oversized body or absent/repeated signature
- **THEN** exactly one original route remains and invalid transport is rejected before persistence without private disk buffering.

### Requirement: Authenticated unique module ownership
Exactly one module SHALL own a supported authenticated notification before any persistence. Authenticated unrelated family SHALL differ from failed authentication, unknown purchase or disabled processing. Two owners SHALL fail before writes; an unmatched Hotels purchase SHALL remain Hotels-owned evidence rather than silently ignored or attached heuristically.

#### Scenario: One owner or conflicting owners
- **WHEN** an authenticated event belongs to one family or two handlers claim it
- **THEN** only the unique owner may persist, while conflicting ownership produces zero writes and explicit failure.

#### Scenario: Unknown Hotels reference
- **WHEN** an authentic Stays event contains an unknown source purchase reference
- **THEN** bounded unmatched evidence remains with Hotels and cannot release another reservation or become a generic ignored success.

### Requirement: Atomic owner acceptance and truthful acknowledgement
Handled acknowledgement SHALL require committed owner inbox/processing work or its durable duplicate. Owning protection/store failure SHALL fail for redelivery. Authenticated unsupported families SHALL be explicitly ignored without payload persistence. There SHALL be no shared durable inbox or cross-module transaction requirement.

#### Scenario: Failed commit or lost acknowledgement
- **WHEN** owner acceptance cannot commit or its successful acknowledgement is lost
- **THEN** the first is not acknowledged as accepted and later delivery of the second resolves the durable duplicate without repeated effects.

#### Scenario: Authenticated unsupported family
- **WHEN** source-authenticated event semantics are explicitly outside both supported families
- **THEN** ignored acknowledgement is distinguishable from failed delivery and neither module stores private payload.

### Requirement: Receiver mode and account binding
Authenticity SHALL bind exact bytes to trusted receiver/account/mode configuration, never a client-selected header. Signed mode and available organisation fields SHALL be checked; Hotels M1 accepts only Test. Optional missing fields SHALL not be fabricated or used to select another account.

#### Scenario: Wrong source binding
- **WHEN** signature, mode or available signed organisation contradicts receiver configuration
- **THEN** the callback cannot attach or change an owned booking.

#### Scenario: Optional organisation absent
- **WHEN** a supported authenticated event has no optional organisation field
- **THEN** account trust remains the configured signing-key association, without inventing source evidence.

### Requirement: Current wire format and historical saved entries
Every network delivery, including a known event ID, SHALL pass current v2 validation before deduplication. Historical saved Flights entries SHALL remain readable under original root-object/envelope semantics without rewriting bytes/bindings or inventing missing fields. Historical parsing SHALL apply only to saved inbox content and SHALL NOT bypass network validation.

#### Scenario: Current fictional v2 message
- **WHEN** a signed fictional v2 delivery contains data.object, mode and a supported current Flights or Stays event
- **THEN** the owning decoder/codec reads actual nested business facts before and after restart, rather than silently ignoring them because object is absent at root.

#### Scenario: Saved historical Flights entry
- **WHEN** an old protected or legacy inbox entry contains root object and historical event names
- **THEN** its original identity, purpose binding and existing transition meaning remain readable without a new-network fallback or history rewrite.

#### Scenario: Malformed or conflicting shapes
- **WHEN** a new delivery omits required v2/mode fields or supplies conflicting root and nested objects
- **THEN** it fails fixed validation rather than borrowing a historical parser to guess its meaning.

### Requirement: Isolated module evidence and historical compatibility
Each module SHALL own its inbox, protected content and processing. Hotels payload SHALL not be persisted in Flights or vice versa. Supported existing Flights business behavior/deduplication SHALL remain; current-format corrections SHALL be limited to ingress/decoder/codec and tests rather than all Flights APIs or all provider events.

#### Scenario: Unrelated outage
- **WHEN** a Hotels event is accepted while Flights PII keys/store are unavailable
- **THEN** unrelated Flights private work is skipped and a healthy Hotels owner can accept it with zero Flights inbox/outbox rows.

#### Scenario: Valid v2 duplicate of a saved Flights event
- **WHEN** a network delivery passes current v2 validation and matches the Source/event ID of a retained historical Flights event
- **THEN** deduplication produces no new effect or Hotels copy and does not rewrite the saved envelope, whose historical reading retains its original purpose and semantics.

#### Scenario: Historical root-object replay over the network
- **WHEN** old root-object bytes arrive over the network with an already known Flights event ID
- **THEN** current v2 validation rejects the delivery before deduplication; the known ID cannot authorize a historical-parser bypass.

### Requirement: Receiver continuity after writer disablement
Disabling new purchases SHALL not disable receiving/draining observations for admitted attempts. Source subscription/rollout/rollback SHALL retain required owners. Absent required receiver SHALL cause refused activation or explicit failed delivery, not an owned-family ignored acknowledgement.

#### Scenario: Purchases switched off while booking pending
- **WHEN** a previous attempt still needs an observation but new bookings are disabled
- **THEN** the owning receiver continues processing the observation and retains the original creation barrier.

#### Scenario: Necessary receiver removed
- **WHEN** configuration would remove an owner needed by subscribed or retained observations
- **THEN** activation or delivery fails explicitly instead of acknowledging discard.
