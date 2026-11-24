# Trip hotel reservation links

## Purpose

Let a traveler see the confirmed Hotels stay that they explicitly attached to a private Trips plan, using retained reservation facts for the calendar and the existing Hotels detail for the order.

## ADDED Requirements

### Requirement: One explicit link to an eligible Hotels reservation
The owner SHALL be able to attach one existing Hotels reservation to a saved trip. The attachment SHALL require an explicit choice of that trip and an owner-scoped local Hotels observation of an active, confirmed reservation with usable actual property and stay dates and no review required. Trips SHALL provide an explicit unlink action.

#### Scenario: Attach from a confirmed order
- **WHEN** the owner opens an eligible order, selects a saved trip from their own trips and confirms attachment
- **THEN** the trip has one link, can be opened from the confirmation and can display the retained order facts on load. Attachment leaves the reservation unchanged.

#### Scenario: Ineligible source at attachment
- **WHEN** the order is a quote, pending, unknown, awaiting details, cancelled, under review or missing valid actual stay facts at the time of the owner-scoped check
- **THEN** no new link is saved. The action reports that the order is unavailable for linking without promoting quote terms to a confirmed stay.

#### Scenario: One link per trip
- **WHEN** the owner requests an eligible order and the selected trip already has a link to a different order
- **THEN** the action returns a conflict and the existing link stays in place. The owner can unlink it explicitly before attaching another.
- **AND** this limit applies to the selected trip. Other trips owned by the same traveler can independently link the same reservation.

### Requirement: Respect access and retained source facts
Displaying or attaching a reservation SHALL require access to both resources and the existing Hotels read permission. The trip owner SHALL be checked before Hotels access. Trip reads SHALL show dated entries only from current eligible Hotels facts. Source failures and loss of access SHALL remove dated entries and previous private details from the view.

#### Scenario: Foreign or missing resources
- **WHEN** the caller targets another owner's trip or attempts to attach another owner's reservation
- **THEN** the foreign resource is concealed in the same way as a missing resource, regardless of whether the link slot is occupied. No property, order identifier or dates are disclosed. A foreign trip causes no Hotels read.

#### Scenario: Cancellation, review or incomplete facts after attachment
- **WHEN** an authorized read finds a linked order cancelled, review required or otherwise no longer eligible
- **THEN** the date projection disappears and the link section shows the corresponding current availability or review state. A confirmed order needing review is still described as requiring review by Hotels. Trips makes no booking or financial changes.

#### Scenario: Read unavailable or private data no longer accessible
- **WHEN** the owner rereads a linked trip after Hotels is disabled, a transient Hotels failure, retention expiry, missing order or loss of Hotels read permission
- **THEN** the trip remains usable and the affected link has a non-disclosing unavailable state without previous details or a navigation identifier. The trip owner can still unlink the reference using Trips authority. A late reply from a superseded view cannot restore private details.

#### Scenario: Open the trip in a different browser session
- **WHEN** attachment succeeds in Hotels and the owner opens the trip
- **THEN** Trips uses its own authenticated session and its normal sign-in when needed. Another subject receives the ordinary concealed missing-trip outcome. A Trips session without Hotels read permission can view and remove the association while its Hotels facts remain private.

### Requirement: Place the confirmed stay on the existing calendar
Trips SHALL show the Hotels property name and original local dates, with a link to the existing private order. The calendar SHALL mark check-in, intervening stay dates and checkout on corresponding displayed days. Overnight coverage SHALL end before the checkout date. Calendar projection SHALL use the current applied trip dates and SHALL cause no additional reads or writes.

#### Scenario: The traveler sees one confirmed stay and their plans
- **WHEN** the owner links a fictional Petersburg order with check-in on June 13 and checkout on June 15 to a June 13–15 trip with flights and excursions
- **THEN** check-in, overnight continuation and checkout appear on their respective dates. The source and navigation distinguish the linked booking from flights and editable entries. The trip's displayed subtotal and block limits are unchanged.

#### Scenario: Existing manual stay and simultaneous calendar edits
- **WHEN** the owner has a manual stay or changes the displayed trip dates
- **THEN** the manual entry keeps its values and its existing edit and date constraints. The Hotels dates are taken from the source, shown only on displayed calendar days, and still shown in full in the link summary. A linked stay wholly outside the plan receives an explicit out-of-range label in the link section.

### Requirement: Persist the association with independent revision checks
Link changes SHALL use an exact independent revision with atomic owner-scoped conflict checks. The association SHALL retain only the protected reference and metadata required for its lifecycle. Attaching or removing a link SHALL leave the saved plan revision untouched. Removing a link or deleting the trip SHALL have no effect on the Hotels order.

#### Scenario: Reopen or restart
- **WHEN** the owner opens the same trip after successful attachment and after restart with the same owned data and keys
- **THEN** one saved association is restored, Hotels facts are derived from the current local reservation and the plan's manual entries and revision are preserved.

#### Scenario: Competing changes and a lost response
- **WHEN** two updates use the same revision or a response is lost after dispatch
- **THEN** at most one effective update commits per revision. The UI rereads the authoritative association before an explicit follow-up and never automatically resends the mutation.

#### Scenario: Repeat the same association at its current revision
- **WHEN** the owner requests attachment of the already-linked ID with its current exact revision
- **THEN** an authorized current check of an eligible order allows a no-op with the same revision. Missing access, expiry or a newly ineligible order refuses the action without changing the association.

#### Scenario: Source changes without a link revision change
- **WHEN** a read follows a change to the source booking, access or retention while the association revision is still the same
- **THEN** the link metadata and its ETag still describe only that association. A separate no-store status read evaluates current facts. It cannot reuse a conditional response to restore previously seen private data.

#### Scenario: Unlink or delete
- **WHEN** the owner explicitly removes the link or deletes the trip
- **THEN** the calendar projection disappears. Unlink advances the independent revision and keeps the trip and its plan. Trip deletion removes its association as part of the Trips lifecycle. No cancellation, booking, payment or supplier call occurs.

### Requirement: Keep the fictional first slice private and opt in
The capability SHALL be default-off and restricted to an explicitly enabled fictional Test environment for this slice. Reads SHALL use retained local Hotels data. Responses SHALL be private and no-store. Guest data, payment details and supplier references SHALL stay outside Trips. The browser SHALL clear private data on owner, plan or permission changes.

#### Scenario: Disabled capability
- **WHEN** the new capability is disabled or the environment is outside its allowed profile
- **THEN** Hotels offers no attach action, the calendar offers no linked Hotels content, and the capability resolves no new store or Hotels reader.

#### Scenario: Enabled but unready
- **WHEN** the capability is enabled in the allowed profile but its Trips storage or protection is unavailable
- **THEN** it reports unavailable and the ordinary trip and reservation remain usable under their existing rules. When only the Hotels reader is unavailable, the association can still be removed.

#### Scenario: Inspect a permitted response and edit by keyboard
- **WHEN** the owner attaches, reads or removes the link by keyboard
- **THEN** the UI exposes a labelled choice and confirmation, focus recovery and status, and the link shows only the permitted minimal fields. Property names render as text. No private facts enter SSR, browser persistence or logs.
