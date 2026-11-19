# Trip Flight Calendar Delta

## Purpose

Show the owner of a saved trip where the scheduled legs of their attached flight orders fall relative to the trip dates and editable plan.

## ADDED Requirements

### Requirement: Automatic placement of linked flight legs
The trip calendar SHALL automatically show one read-only Flights card for each eligible leg of a saved attached order when existing Trips flight links are available to the owner. Flights cards SHALL be visually and semantically distinct from editable plan blocks. Calendar placement SHALL have no user-selectable mode, scenario switch, date override or drag action.

#### Scenario: View the fictional Saint Petersburg trip
- **WHEN** the owner opens a June 13–15 calendar with an attached leg departing KGD on June 12 at 23:40 UTC+02:00, arriving at LED on June 13 at 02:10 UTC+03:00, a June 13 excursion at 09:00, and an attached return leg wholly on June 16
- **THEN** the incoming leg appears on June 13 above the excursion, the return appears after the last day, and both flight cards are visible with no placement control.
- **AND** the excursion retains its ordinary edit controls and the Flights cards do not count toward the plan block cap or any expense total.

#### Scenario: Whole order with several legs
- **WHEN** an attached round trip, multi-leg or open-jaw order has several eligible source legs
- **THEN** each source leg is placed independently and shown once, retaining its own two endpoints and link to the same order. The existing attachment remains one whole order and the calendar does not combine its first departure and last arrival into a single leg.

### Requirement: Preserve source dates and validate chronology
Placement SHALL use the date part of each validated airport-local timestamp as recorded by Flights, compared to the inclusive displayed plan date range. The card SHALL preserve both airport codes, local dates, times and offsets. Chronology SHALL be assessed by instants with offsets under the existing Flights and Trips link contract.

#### Scenario: Dates at the calendar boundaries
- **WHEN** either endpoint is on the first or last day, at midnight or another local time, even when its UTC or browser-local date is different
- **THEN** that endpoint counts as inside and the card displays the original airport-local date, time and offset.

#### Scenario: Arrival with an earlier local date
- **WHEN** the arrival instant follows the departure instant but its recorded local date is earlier
- **THEN** the leg is classified using both original local dates, and neither is changed to force ascending calendar dates.

#### Scenario: Duration or chronology invalid under the source contract
- **WHEN** a source leg has nonpositive elapsed duration, reaches or exceeds 48 hours, or otherwise fails the existing observation contract
- **THEN** the invalid route is not accepted as a timed calendar observation. The existing unavailable or stale behavior applies to that order and other accepted orders retain their own valid placements.

### Requirement: Place the pair of endpoint dates
For every eligible leg the calendar SHALL select exactly one destination using the nine-case matrix below. Before and after mean strictly outside the inclusive range. The before and cross-boundary sections SHALL precede the calendar days in that order. The after section SHALL follow the last day. Empty flight sections SHALL be omitted.

#### Scenario: All nine date pairs
- **WHEN** validated observations exercise all nine combinations in the matrix, across one-day and multi-day plans where required
- **THEN** each leg appears only in the named day or section. The calendar contains only its original plan days and omits empty flight sections.

  | Departure date / arrival date | Before | Inside | After |
  | --- | --- | --- | --- |
  | Before | Before the trip | Arrival day | Across both date boundaries |
  | Inside | Departure day | Apply the inside-date rule | Departure day |
  | After | Across both date boundaries | Arrival day | After the trip |

#### Scenario: Both dates are outside on opposite sides
- **WHEN** a validated leg has one endpoint before the first day and one after the last day, in either local-date order
- **THEN** its single card appears above the days in «По обе стороны дат поездки», with both original dates visible. The group describes the endpoint dates and conveys no day-by-day occupancy or feasibility claim.

### Requirement: Prefer the known trip-city endpoint when both dates are inside
When both endpoint dates are inside, the card SHALL appear on the date of the only endpoint reliably identified as serving the trip destination. If the choice is ambiguous or unknown, the card SHALL appear on the departure date. City evidence SHALL come from reviewed local airport-to-city data.

#### Scenario: Overnight arrival and departure around the same trip city
- **WHEN** a June 13–15 Saint Petersburg plan has KGD → LED from June 13 to 14 and LED → KGD from June 14 to 15, with both airport-to-city mappings known
- **THEN** both cards appear exactly once on June 14, by the arrival of the first leg and the departure of the second.

#### Scenario: Uncertain match or two matching endpoints
- **WHEN** both endpoint dates are inside and the mapping is missing, ambiguous, neither endpoint matches, or both match
- **THEN** the card appears on the departure date.

#### Scenario: Two endpoints on the same included day
- **WHEN** a validated leg departs and arrives on the same included local date
- **THEN** that day contains one card with both events regardless of the city match.

### Requirement: Recompute the view without side effects
The calendar SHALL recompute placement from the valid dates and destination shown by the existing editor and the accepted owner-scoped observations for the current link collection. Projection and rendering SHALL cause zero writes, extra reads or supplier calls. Existing plan and link editing, revisions and order navigation SHALL retain their own contracts.

#### Scenario: Change the dates locally and save or reject
- **WHEN** the owner applies valid new dates to the displayed calendar, or the editor restores another valid displayed range
- **THEN** cards are placed against the visible days and ordinary unsaved-state feedback remains accurate. Changing raw date inputs or rejecting an invalid range does not place cards against days the editor has not applied.
- **AND** only an explicit existing save can persist a plan edit, and no flight order or attachment is changed by relocation.

#### Scenario: New observation or removed attachment
- **WHEN** the existing refresh flow accepts a newer schedule, a confirmed unlink removes an order, or the trip selection changes
- **THEN** the calendar replaces placements from the prior data, removes affected cards on unlink or trip change and never restores them from a late incompatible reply.

### Requirement: Reflect uncertainty and preserve private visibility
Calendar cards SHALL retain the accepted Flights status and freshness semantics. Stale, review-required and closed legs with usable facts SHALL carry their corresponding status. An order without usable route facts SHALL have its existing unavailable or concealed state in the associated-orders area and no dated placement. Projection SHALL respect existing Trips authorization and capability gating.

#### Scenario: Partial failure and restored observations
- **WHEN** one attached order becomes unavailable while another succeeds, or an accepted route becomes unavailable or inaccessible
- **THEN** successful cards remain usable. An explicitly stale observation retained by the existing flow keeps its stale label, while a concealed not-found result clears the related route from the calendar. The editable plan remains usable.

#### Scenario: No eligible private observation
- **WHEN** no orders are attached, flight links are disabled, or the owner no longer has the current trip open and authorized
- **THEN** no private flight cards or empty flight groups appear and no additional status reads are introduced by the calendar. The view does not persist private route data in storage, browser persistence or SSR output.

#### Scenario: Closed order with usable historical route
- **WHEN** a still-linked order has a usable historical route and Flights reports it as cancelled or refunded
- **THEN** its dated cards have the existing closed status and give no impression that a flight is active.

#### Scenario: Understand schedule and plan by keyboard
- **WHEN** the owner reads the calendar with a keyboard or assistive technology
- **THEN** visible headings, source, local time and relevant warning labels distinguish the schedule from plan editing, and an existing order link uses its ordinary validated navigation and access rules.
