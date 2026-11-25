# trip-option-preview Specification

## Purpose

Путешественник может увидеть в форме Trips один воспроизводимый демонстрационный вариант поездки, в котором согласованы перелёт туда и обратно, ночи в гостинице и прогулки по дням.

## Requirements

### Requirement: Preview from the current trip form

The system SHALL offer an explicit private preview from the current unsaved creation form, using origin, destination, inclusive dates, adult count, currency, optional total budget, interests and pace. A supported request is Moscow to Saint Petersburg and back, one room, 1–2 adults, 2–3 days within the existing itinerary horizon. Preview SHALL preserve the form and require no saved plan or title.

#### Scenario: First request from the form
- **WHEN** an authenticated traveler leaves the title blank and changes the visible dates to 28–30 October 2026 for two adults, architecture and relaxed pace without applying the fields to a calendar
- **THEN** choosing «Подобрать вариант» sends the displayed brief once and displays the result. The form stays in creation mode with its edits. There are zero plan-create, apply or save actions.

#### Scenario: Missing origin or a day trip
- **WHEN** the origin is absent
- **THEN** the form asks for the departure city without silently choosing Moscow.

#### Scenario: Valid manual trip outside the pilot
- **WHEN** the traveler selects a one-day trip, three adults, another city pair or dates outside the supported horizon
- **THEN** the form identifies the unsupported condition and keeps the values available for ordinary manual planning.

#### Scenario: Pilot limits are known before reading evidence
- **WHEN** a valid brief is outside the supported direction, day count, group size or horizon and the evidence was withdrawn or both computation slots are busy
- **THEN** the request returns the same unsupported reasons as in a healthy profile, without reading evidence or occupying a slot. The declared release identity remains available for wire compatibility. A supported brief still requires admitted computation and valid evidence.

### Requirement: One compatible round trip and hotel stay

The system SHALL select at most one intact fictional round-trip offer and one fictional whole-stay offer. A pair SHALL match cities, local dates, party, occupancy, all nights, and modelled arrival/check-in and departure/check-out constraints. A returned option SHALL include both flight legs, hotel, stay dates, nights, calendar days and a reason for the selection. Its offers SHALL be labelled fictional.

#### Scenario: Two nights for two adults
- **WHEN** one room covers two adults on the nights of the 28th and 29th and another offer covers only the first night
- **THEN** only the whole stay is eligible and the calendar shows arrival and check-in on the 28th and check-out and departure on the 30th.

#### Scenario: No compatible pair
- **WHEN** a return flight is absent, rooms cannot accommodate the party, or the only hotel's check-in closes before the modelled arrival
- **THEN** the traveler sees the specific missing or incompatible component and receives no complete option.

### Requirement: Walks fit local time windows

The system SHALL select outdoor stops within each day's local available window after flight, hotel and declared transfer allowances. It SHALL use the traveler's interests and pace, unique stops, reviewed directed paths and conservative modelled durations. The result SHALL expose window assumptions, walking coverage and unmet interests; unknown access hours SHALL remain visible.

#### Scenario: Early and late fixtures through the same form
- **WHEN** the October example uses RT1/H1, the early flight arrives at 09:00, check-in is at 14:00, and the daily walking period is 10:00–18:00 with 20 minutes reserved at each hotel end
- **THEN** on the 28th the stop window is 14:20–17:40 and the ordered stops are spb-general-staff and spb-pevchesky-bridge. On the 29th the window is 10:20–17:40 with spb-green-bridge and spb-stroganov-palace. On the 30th check-out at 08:00 for the 11:00 flight leaves zero stops.
- **WHEN** only the outbound flight times change to an arrival at 20:00
- **THEN** check-in moves to 21:30 and the 28th has zero stops with a reason. The 29th and 30th, the winning offer IDs, two nights and 30,000 RUB known subtotal stay the same.

#### Scenario: Independent verification of a finished route
- **WHEN** a proposed route within the relaxed daily cap needs 80 minutes inside a 50-minute stop window, or a 30-minute visit plus a 20-minute return is proposed with 40 minutes until mandatory hotel departure
- **THEN** independent verification rejects that finished option before publication. A valid route returning exactly at the deadline is accepted; known access intervals constrain the entire visit, and unknown hours are disclosed.

#### Scenario: No eligible walk on any day
- **WHEN** a valid package has no eligible stops in any available window
- **THEN** the result identifies missing walking coverage and does not present a complete trip option.

### Requirement: Reproducible choice and honest costs

The system SHALL first exclude infeasible pairs and known subtotals above the entered budget. It SHALL rank by distinct requested interests covered, then stops within pace, then price completeness for flight and stay, then lower comparable complete price, then stable flight/hotel IDs. Equal inputs, source versions and evaluation time SHALL yield the same selection and explanation under reordered input.

#### Scenario: Explain the winning option
- **WHEN** two eligible pairs have equal price coverage but one covers more requested interests
- **THEN** that pair wins and the response identifies the preference it meets. An exact tie resolves by stable offer IDs.

#### Scenario: Preferences and partial costs compete
- **WHEN** A covers two requested interests and has 30,000 RUB known plus an unknown mandatory fee, while B covers one interest and has a complete flight and stay price of 24,000 RUB
- **THEN** A wins without a budget and with a 30,000 RUB budget, with the unknown fee and indeterminate final fit visible. With a 25,000 RUB budget A is excluded and B wins.

### Requirement: Component-level traveler costs

The system SHALL count every known mandatory traveler component in the requested currency once as a lower bound for the whole party and stay. Included taxes SHALL NOT be re-added. Unknown fees, net prices, other currencies and unpriced trip expenses SHALL retain their individual meanings. A known subtotal within budget SHALL NOT imply a complete trip price.

#### Scenario: Base price known and fee unknown
- **WHEN** a flight is 18,000 RUB, the room costs 12,000 RUB for both nights and a separate local fee is unknown
- **THEN** the known subtotal is 30,000 RUB and the fee remains unknown. At a budget of 25,000 RUB this pair is excluded. At 30,000 RUB it remains eligible with indeterminate total cost.

#### Scenario: Unknown traveler price
- **WHEN** the flight costs 18,000 RUB for the party and the hotel has only a supplier net amount
- **THEN** the result identifies 18,000 RUB of known traveler costs, unknown hotel price and indeterminate budget fit; it never treats the hotel as free.

#### Scenario: Traveler prices are known in a different currency
- **WHEN** the flight costs 18,000 RUB and the room costs 12,000 RUB for the whole stay, but the traveler enters a budget of 1 EUR
- **THEN** the card shows 30,000 RUB in known traveler charges and explains that comparison with 1 EUR requires an exchange rate. It makes no within-budget claim and never presents a zero EUR price for the trip. Net, included charges and unpriced expenses retain their individual meanings.

#### Scenario: Known costs exceed budget
- **WHEN** every compatible pair already exceeds the entered budget on known comparable costs alone
- **THEN** the result explains the budget conflict and presents no option as affordable.

### Requirement: Independent demo and fresh evidence

The preview SHALL require Trips, walking generation and its own switch together in the named isolated Testing profile. An authenticated traveler SHALL see source and reference date before selection. Manual inspection and automated acceptance SHALL use the same historical scenario and actual Host with zero model or supplier calls. The ordinary interactive stack SHALL retain wall-clock behavior.

#### Scenario: Open and inspect the example manually
- **WHEN** the developer starts the local spb-option inspection profile and opens its loopback URL
- **THEN** the actual form stays open for human input. It identifies the fictional catalog and 27 October 2026 reference date; the preview evaluates the immutable package at the scenario instant. Authentication, certificates and execution deadlines use real clocks.

#### Scenario: Feature discovery before selection
- **WHEN** the owner opens the new form in the enabled profile
- **THEN** availability metadata provides the server-owned profile ID, reference instant and example dates without creating an option. The button appears after successful discovery.
- **WHEN** either walking generation or the preview flag is disabled, or the ordinary demo or Production is used
- **THEN** discovery and preview are both 404, the button stays hidden and the ordinary form can still create a plan wherever Trips itself is enabled.

#### Scenario: Historical profile and expired package
- **WHEN** the named profile evaluates the released Petersburg package at its supported historical instant
- **THEN** it can produce a labelled historical demonstration with attribution and a working link to the offered data.
- **WHEN** that package is expired at the server's evaluation instant, withdrawn or damaged
- **THEN** the result reports unavailable data without substituting a fixture date, city or package.

### Requirement: Transient private result

The preview SHALL have no persistence or commercial side effects. Changing any input or leaving the creation context SHALL invalidate the displayed result. Only the latest matching response within the same authenticated session SHALL be displayed. Responses SHALL be private and non-cacheable.

#### Scenario: Edit during selection
- **WHEN** the traveler changes dates while a request is in flight, navigates to an existing trip or signs out
- **THEN** the former result and any late response disappear without modifying the draft, creating a plan, linking an order, reserving a room or initiating payment.

#### Scenario: Original Host response is visible in the browser
- **WHEN** the acceptance run exercises the reference brief against the actual Host with each catalog version
- **THEN** each click yields exactly one original preview response. Flight/hotel IDs, local flight times, nights, ordered stop IDs, windows, known subtotal and reported uncertainty in that response agree with the reference case and its visible representation. No browser fixture can fulfil the preview request.

#### Scenario: Disabled or unauthenticated
- **WHEN** the feature is off or an active profile receives a request without a valid owner session
- **THEN** the route returns 404 or 401 respectively before selection and shows no private result.
