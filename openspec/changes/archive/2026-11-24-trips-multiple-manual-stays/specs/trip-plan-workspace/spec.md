# Trip plan workspace delta

## MODIFIED Requirements

### Requirement: Manual stays in a saved trip
The owner SHALL be able to add, edit and remove multiple manual stays in one saved plan. Each stay SHALL contain a plain-text place name of 1–100 Unicode scalars and destination-local check-in and checkout dates. Checkout SHALL follow check-in. Both dates SHALL lie inside the inclusive plan range. A night SHALL belong to at most one manual stay. These entries SHALL cause zero model, geocoding, supplier or booking operations.

#### Scenario: One night or several nights
- **WHEN** the owner applies valid stay dates to the open plan
- **THEN** consecutive dates represent one night and a longer interval represents the calendar-date difference in nights, regardless of browser time zone. The owner can leave nights without a manual entry.

#### Scenario: Invalid stay input
- **WHEN** the name is blank or invalid, or the dates are missing, equal, reversed or outside the applied date range
- **THEN** the editor keeps the last applied list and calendar visible, retains the inputs for correction and directs the owner to the invalid field. A one-day trip cannot contain a valid stay.

#### Scenario: Keep unapplied inputs out of the plan
- **WHEN** the owner types into a stay form and has yet to apply or cancel the changes
- **THEN** the calendar still shows the last applied list, saving requires applying or cancelling the form, and the navigation warning includes those inputs. Cancelling the form restores its last applied values.

#### Scenario: Editing a stay would duplicate a night
- **WHEN** the owner moves the start of the second of two adjacent stays into the first stay
- **THEN** the editor identifies the conflicting entries, keeps the last applied list and preserves the input for correction. It compares the new dates with the other entries, so a valid edit can keep nights from the entry being edited.
- **AND** a well-formed PUT at the current revision with overlapping stays returns `409 Trips.ManualStaysOverlap` with the previously saved plan and revision still authoritative.

### Requirement: Show stay dates alongside the trip calendar
The calendar SHALL show each manual place and check-in on its arrival date, ongoing coverage on intervening nights and checkout on its last date. Nights SHALL run from check-in inclusive to checkout exclusive. Entries SHALL come from the current applied plan and remain distinguishable from Flights, Hotels and day blocks.

#### Scenario: Fictional Saint Petersburg calendar
- **WHEN** a 13–15 June 2027 plan has the linked KGD → LED leg arriving on the 13th, an excursion that morning, a second excursion on the 14th, a linked return leg on the 15th and a stay from the 13th to the 15th
- **THEN** the 13th shows the incoming flight, check-in and the excursion, the 14th shows the continuation of the stay and the second excursion, and the 15th shows checkout and the return flight. The stay summary shows two nights. The checkout day has no overnight coverage.

#### Scenario: Checkout and check-in on the same date
- **WHEN** the owner applies a stay with friends for June 13–14 and an apartment for June 14–16 to a June 13–16 trip
- **THEN** June 14 shows both the checkout from the first place and the check-in at the second place. The manual total is three nights, with one night assigned to the first place and two to the second.

#### Scenario: Coexist with a confirmed Hotels stay
- **WHEN** a trip also has an eligible linked Hotels reservation whose dates overlap a manual stay
- **THEN** the calendar shows the reservation as separate confirmed information. The reservation neither changes the manual night count nor blocks a manual edit or a valid shortening. Its own displayed dates follow the Hotels link contract.

#### Scenario: Plan without private flight facts
- **WHEN** manual stays are present and Flights linking is disabled, unavailable or has no attached order
- **THEN** the owner still sees the manual stays and editable plan. Rendering creates no extra requests and manual stays contribute neither to day-block caps nor to estimated amounts, unknown estimate counts or timed overlap warnings.

#### Scenario: Use the editor by keyboard
- **WHEN** a keyboard user adds or changes a stay
- **THEN** each input and action is labelled, invalid values are announced with focus available for correction, and dated entries can be understood from their text. Markup-looking place names render literally.

### Requirement: Preserve the stay through plan changes
The editor and plan updates SHALL preserve entered dates for all retained stays when the trip range changes. A new range excluding an endpoint of any resulting stay SHALL fail atomically. Before a saved trip with manual stays changes destination, the owner SHALL remove all stays and save that removal separately.

#### Scenario: Shorten with a stay endpoint outside
- **WHEN** the owner shortens a June 13–16 trip to June 15 while one entry still checks out on June 16, or moves the trip start past the first retained check-in
- **THEN** the editor keeps the applied range and list and identifies entries needing adjustment or explicit removal. A conflicting PUT returns `409 Trips.DateRangeWouldExcludeStay` with the previous snapshot and revision still authoritative.
- **AND** once the remaining endpoints fit, the corrected list and range can be applied and saved together in one PUT if the other trip-date guards pass.

#### Scenario: Extend or edit the trip without an accommodation change
- **WHEN** the owner extends the dates while keeping all stay endpoints inside the range, or edits valid preferences while retaining the destination
- **THEN** the list preserves all entered places and dates. Currency may change on a stay-only plan because there is no stay amount. Existing rules for populated day blocks still apply.

#### Scenario: Change destination with a saved stay
- **WHEN** a single PUT changes the destination of a saved plan with any stay, even if that PUT explicitly clears the list
- **THEN** the change is rejected with a typed conflict and no revision change. The owner can clear and save the list first, then submit the destination change separately.

### Requirement: Save and reopen the stay with the private plan
The full manual list SHALL use the existing owner-scoped protected detail, exact revision precondition and explicit save. Each accepted update SHALL preserve the whole list after reopening. Historical protected plans with zero or one manual stay SHALL be readable with their original content and revision. Reads SHALL preserve their saved snapshot.

#### Scenario: Save, reopen and remove
- **WHEN** the owner explicitly saves manual stays, closes the plan and reopens it, including after a service restart with the same storage and keys
- **THEN** the accepted places and dates return at the saved revision in check-in order and reproduce the same calendar. An explicit removal and save keeps the other entries, and clearing the list removes all manual entries. An unchanged save, including a change in input order alone, keeps its revision and update time.

#### Scenario: Reject unsupported payload shapes
- **WHEN** POST includes either stay field, v1 PUT supplies a stay array, v2 PUT omits its required array, or a request includes mixed stay fields, invalid date formats, unexpected or duplicate fields, or invalid entry types
- **THEN** the server returns `400` with no stored change. A syntactically valid PUT using a stale revision retains the existing `412` handling.

#### Scenario: Reconcile delayed save and newer edits
- **WHEN** a save adds a stay and the owner removes it from the draft before receiving the successful response
- **THEN** the accepted revision contains the stay, the visible draft still shows it as removed and dirty, and the next explicit save removes it from the accepted revision. The same rule preserves a later restoration when a saved removal response arrives and preserves edits to other entries.

#### Scenario: Discard changes or reconcile a conflict or lost response
- **WHEN** the owner discards a draft, or a save conflicts or may have committed without a reply
- **THEN** discard restores the saved list on reopen. Conflict or uncertainty retains local edits for explicit reconciliation through the existing plan flow without automatically repeating the write.

#### Scenario: Adopt other plan content without changing the stay
- **WHEN** an otherwise eligible plan already has manual stays and the owner adopts walking-candidate blocks or a Rail intention
- **THEN** the new blocks and the full list coexist after saving. Future-detail size checks count the list. Its entries alone do not count as day blocks for walking-candidate eligibility.

#### Scenario: Keep the stay private
- **WHEN** another owner sends a well-formed detail or update request, the editor changes owner or open trip, or Trips emits a response or diagnostic
- **THEN** foreign requests retain the existing concealed not-found behavior. Switching context clears all old stays and unapplied inputs, including while a reply is pending. Stay values remain absent from summaries, logs, traces, error bodies, browser storage and SSR output. Private responses retain no-store and complete detail retains the 128 KiB limit.

#### Scenario: Historical plan with zero or one stay
- **WHEN** the owner opens a pre-change protected plan without `manualStay` or with one `manualStay` object
- **THEN** the updated editor shows an empty list or one entry with the original name and dates, while the saved snapshot and revision are preserved. Editing another field, saving and reopening preserves that list.

## ADDED Requirements

### Requirement: Safe versioned trip detail and replacement
The existing trip detail and PUT SHALL preserve their `manualStay` contract for plans with at most one stay. A distinct GET and PUT contract SHALL support the full list and require an explicit `manualStays` array for replacement. Both contracts SHALL use the same owner and plan revision checks. The legacy contract SHALL reject a plan with multiple stays before emitting a successful detail or applying a replacement.

#### Scenario: An old tab edits a plan with at most one stay
- **WHEN** the owner uses the legacy GET and exact-revision PUT on a zero- or one-stay plan
- **THEN** detail omits `manualStay` for an empty list and returns the object for one stay. PUT omission preserves the entry, `null` clears it, and an object sets it. Existing list, create and explicit trip deletion contracts are available with their existing behavior.

#### Scenario: An old tab encounters a plan with multiple stays
- **WHEN** the legacy client reads such a plan or sends a well-formed replacement at its current revision, with `manualStay` absent, null or an object
- **THEN** it gets `409 Trips.PlanVersionRequired` without partial detail or a write. A well-formed replacement with a stale revision gets `412`. The list continues to show the owner's usual summaries.

#### Scenario: A current tab saves the full list
- **WHEN** an owner loads detail from `/api/trips/{tripId}/workspace-v2` and submits a PUT to that address with its exact current revision and a valid full `manualStays` array
- **THEN** the entire replacement is saved atomically. An empty array explicitly clears the list. Detail uses the historical shape for zero or one entry and supplies the whole `manualStays` array for two or more entries.

#### Scenario: A different revision wins during replacement
- **WHEN** a versioned replacement that adds a second stay and a valid legacy replacement race on the same initial one-stay revision
- **THEN** at most one commits. The loser receives the existing stale revision outcome when the plan still exists, and a legacy retry at the newer revision is rejected if that revision contains multiple stays.

### Requirement: Data-preserving server rollback
The updated server SHALL read both supported protected formats. Plans with at most one stay SHALL have a format readable by the preceding server. A server that supports only the preceding format SHALL be restored on the same data only after writers are quiesced and a read-only check confirms absence of newer formats.

#### Scenario: A plan returns to the preceding format
- **WHEN** the owner explicitly saves a valid change from multiple stays to at most one stay
- **THEN** the owner can reopen it at the new revision on either server with the same storage and keys. Before any effective change, reopening a multiple-stay plan on the updated server preserves all entries and the stored protected snapshot.

#### Scenario: The rollback check passes
- **WHEN** all Trips operations are quiesced and the database confirms that only the preceding format is in use
- **THEN** the preceding Host and web can serve the original protected plans with the same storage and keys. A PUT from a still-open newer tab against the versioned route is rejected and is never replayed to the legacy route by the client.

#### Scenario: The rollback check finds newer data or cannot verify it
- **WHEN** at least one newer protected format is present or the read-only check cannot confirm the result
- **THEN** restoring a server that can read only the preceding format is blocked. The stored plans and keys are preserved for a compatible server.

## RENAMED Requirements

- FROM: `### Requirement: One manual stay in a saved trip`
- TO: `### Requirement: Manual stays in a saved trip`
