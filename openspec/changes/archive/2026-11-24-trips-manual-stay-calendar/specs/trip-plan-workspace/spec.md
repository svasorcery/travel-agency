## ADDED Requirements

### Requirement: One manual stay in a saved trip
The owner SHALL be able to add, edit and remove one optional manual stay in the saved trip editor. A stay SHALL contain a plain-text place name of 1–100 Unicode scalars and two destination-local dates. Check-in SHALL be strictly before checkout and both dates SHALL be within the inclusive plan range. A stay SHALL represent the owner's own entry and SHALL cause zero model, geocoding, hotel supplier or booking operations.

#### Scenario: One night or several nights
- **WHEN** the owner applies valid stay dates to the open plan
- **THEN** consecutive dates represent one night and a longer interval represents the calendar-date difference in nights, regardless of browser time zone.

#### Scenario: Invalid stay input
- **WHEN** the name is blank or invalid, or the dates are missing, equal, reversed or outside the applied date range
- **THEN** the editor keeps the last applied stay and calendar visible, retains the inputs for correction and directs the owner to the invalid field. A one-day trip cannot contain a valid stay.

#### Scenario: Keep unapplied inputs out of the plan
- **WHEN** the owner types into the stay form and has yet to apply or cancel the changes
- **THEN** the calendar still shows the last applied stay, saving requires applying or cancelling the form, and the navigation warning includes those inputs. Cancelling the form restores the last applied values.

### Requirement: Show stay dates alongside the trip calendar
The calendar SHALL show the place and check-in on the arrival date of the stay, ongoing overnight coverage on intervening dates and checkout on the last date of the stay. Nights SHALL cover dates from check-in inclusive to checkout exclusive. Entries SHALL derive from the current applied editor values and SHALL be visually and semantically distinct from Flights and day blocks.

#### Scenario: Fictional Saint Petersburg calendar
- **WHEN** a 13–15 June 2027 plan has the linked KGD → LED leg arriving on the 13th, an excursion that morning, a second excursion on the 14th, a linked return leg on the 15th and a stay from the 13th to the 15th
- **THEN** the 13th shows the incoming flight, check-in and the excursion, the 14th shows the continuation of the stay and the second excursion, and the 15th shows checkout and the return flight. The stay summary shows two nights. The checkout day has no overnight coverage.

#### Scenario: Plan without private flight facts
- **WHEN** a stay is present and Flights linking is disabled, unavailable or has no attached order
- **THEN** the owner still sees the stay and editable plan. Rendering creates no extra requests and the stay contributes neither to day-block caps nor to estimated amounts, unknown estimate counts or timed overlap warnings.

#### Scenario: Use the editor by keyboard
- **WHEN** a keyboard user adds or changes a stay
- **THEN** each input and action is labelled, invalid values are announced with focus available for correction, and dated entries can be understood from their text. Markup-looking place names render literally.

### Requirement: Preserve the stay through plan changes
The editor and plan updates SHALL preserve a valid stay's entered dates when the trip range changes. A new range excluding an endpoint of the resulting stay SHALL fail without truncating, moving or dropping it. A saved stay SHALL be explicitly removed and that removal saved before changing the trip destination.

#### Scenario: Shorten with a stay endpoint outside
- **WHEN** the owner moves the end of a 13–15 June trip to the 14th while checkout remains on the 15th
- **THEN** the editor keeps the old applied range and stay and offers to adjust or remove the stay. A conflicting PUT receives a typed conflict and the saved snapshot and revision remain unchanged.
- **AND** after the owner moves checkout to the 14th or explicitly removes the stay, the new range can be applied and saved with that edit in one PUT if the other trip-date guards pass.

#### Scenario: Extend or edit the trip without an accommodation change
- **WHEN** the owner extends the dates without excluding either stay endpoint, or edits valid preferences while retaining the destination
- **THEN** the stay remains exactly as entered. Currency may change on a stay-only plan because there is no stay amount. Existing rules for populated day blocks still apply.

#### Scenario: Change destination with a saved stay
- **WHEN** a single PUT changes the destination of a saved plan with a stay, even if the same PUT explicitly removes the stay
- **THEN** the change is rejected with a typed conflict and no revision change. The owner can remove and save the stay first, then submit the destination change separately.

### Requirement: Save and reopen the stay with the private plan
The stay SHALL use the existing owner-scoped protected detail, revision precondition and explicit save. The detail SHALL include `manualStay` only when present. A PUT with no `manualStay` SHALL preserve the prior value, `null` SHALL explicitly remove it and an object SHALL replace it in the same atomic plan update. Earlier snapshots missing the field SHALL be readable as plans without a stay.

#### Scenario: Save, reopen and remove
- **WHEN** the owner explicitly saves a stay, closes the plan and reopens it, including after a service restart with the same storage and keys
- **THEN** the accepted place and dates return at the saved revision and reproduce the same stay entries. A later explicit removal and save clears all stay entries on reopen. An unchanged save keeps its revision and update time.

#### Scenario: Reject unsupported payload shapes
- **WHEN** POST includes `manualStay`, or PUT includes a stay array, extra or duplicate fields, an invalid date format or a non-null value other than the defined stay object
- **THEN** the server returns 400 and stores no change. A syntactically valid PUT using a stale revision retains the existing 412 handling.

#### Scenario: Reconcile delayed save and newer edits
- **WHEN** a save adds a stay and the owner removes it from the draft before receiving the successful response
- **THEN** the accepted revision contains the stay, the visible draft still shows it as removed and dirty, and the next explicit save removes it from the accepted revision. The same rule preserves a later restoration when a saved removal response arrives.

#### Scenario: Discard changes or reconcile a conflict or lost response
- **WHEN** the owner discards a draft, or a save conflicts or may have committed without a reply
- **THEN** discard restores the saved stay on reopen. Conflict or uncertainty retains the local edits for explicit reconciliation through the existing plan flow without automatically repeating the write.

#### Scenario: Adopt other plan content without changing the stay
- **WHEN** an otherwise eligible plan already has a saved stay and the owner adopts walking-candidate blocks or a Rail intention
- **THEN** the new blocks and the stay coexist after saving. Future-detail size checks count the stay, and its presence alone does not count as a day block for walking-candidate eligibility.

#### Scenario: Keep the stay private
- **WHEN** another owner sends a well-formed detail or update request, the editor changes owner or open trip, or Trips emits a response or diagnostic
- **THEN** foreign requests retain the existing concealed not-found behavior. Switching context clears the old stay and any unapplied inputs, including while a reply is pending. Stay values remain absent from summaries, logs, traces, error bodies, browser storage and SSR output. Private responses retain no-store and complete detail retains the 128 KiB limit.
