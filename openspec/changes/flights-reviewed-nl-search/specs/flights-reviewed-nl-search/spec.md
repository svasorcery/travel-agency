# Flights — review a flight description before searching

## Purpose

Let a traveler describe a flight, check or correct the suggested route, dates and party in the existing Flights form, and choose when to request offers.

## ADDED Requirements

### Requirement: A complete description fills the existing form

Flights SHALL provide an explicit fill action in its search form. Recognized one-way or round-trip parameters SHALL be editable there. Filling, showing a complete draft and editing it SHALL make zero flight-search requests and zero flight-supplier calls. A valid explicit search SHALL send the current structured form values once per submission.

#### Scenario: Complete round trip, corrected before search

- **WHEN** the traveler submits «Из Москвы в Сочи 12 декабря 2026, обратно 15 декабря 2026, двое взрослых» for recognition and receives the corresponding fictional reply
- **THEN** the form shows «Москва (MOW)», «Сочи (AER)», departure 12 December, return 15 December and 2 adults. The traveler changes the return to 16 December without starting a search.
- **AND** only after «Найти рейсы» does one structured search request carry MOW → AER, 2026-12-12 / 2026-12-16 and 2 adults to the search path. Offers are displayed by the existing results view.

### Requirement: A partial description asks for the missing information

Flights SHALL accept partial recognition, expose missing or ambiguous required fields next to their controls and prevent search until resolved. A city SHALL have evidence in its stated route role. Location, date and passenger guesses SHALL NOT fill missing facts. Relative weekend dates SHALL be labeled as editable suggestions. An explicit one-way instruction or explicit return date SHALL take precedence over the weekend return suggestion.

#### Scenario: Sochi for the weekend, two travelers and no origin

- **WHEN** the local reference day is Monday 2026-11-23 and the traveler enters «Хочу в Сочи на выходные, нас двое» on a new form and selects «Заполнить форму»
- **THEN** the destination shows «Сочи (AER)», the adult count is 2, and the round-trip form proposes Saturday 2026-11-28 and Sunday 2026-11-29. The origin stays empty and shows «Из какого города вылетаете?». There are zero searches and zero supplier calls, including an attempted submission while the origin is empty.
- **AND** the traveler answers «Санкт-Петербург» in that field, selects the local match and moves the return to 2026-11-30. «Найти рейсы» sends one structured search for LED → AER and the edited dates. Entering LED directly is also supported. Clarification needs no new recognition or supplier lookup.

#### Scenario: A plausible city with no evidence

- **WHEN** the fictional reply assigns MOW to origin for the Sochi phrase, or the input offers two alternative origins
- **THEN** the unsupported origin stays unresolved, the form asks for an origin and preserves independently grounded fields. No search runs.

#### Scenario: Local weekend boundary

- **WHEN** two travelers request «на выходные», one on local Saturday 2026-11-28 and one on local Sunday 2026-11-29
- **THEN** the Saturday request proposes 28–29 November and the Sunday request proposes 5–6 December. An explicit «только туда на выходные» proposes only the applicable Saturday.

#### Scenario: Explicit round trip without a return date

- **WHEN** the input says «Из Москвы в Сочи туда-обратно, вылет 12 декабря 2026, один взрослый» and the responder omits the trip-type fragment
- **THEN** the form shows a round trip with a blank return control and asks «Когда возвращаетесь?». Search stays blocked until the traveler supplies a valid return date or explicitly chooses a one-way trip.

#### Scenario: An explicit date overrides the weekend suggestion

- **WHEN** the traveler requests «на выходные, обратно 30 ноября 2026» on 23 November
- **THEN** the form shows 28 November as suggested and 30 November as explicit. Editing 28 November removes its suggestion label.

### Requirement: Clarifications use the current description and manual choices

Flights SHALL keep unresolved fields visible and block submission until the traveler resolves them or explicitly resumes manual entry. Input defaults and values recognized from an earlier description SHALL NOT count as answers. A previously entered manual value SHALL survive an omitted field. If new evidence conflicts with a manual value, the traveler SHALL choose the value used.

#### Scenario: Passenger count was never given

- **WHEN** a fresh form recognizes «Из Москвы в Сочи 12 декабря 2026» with no party evidence
- **THEN** it asks «Сколько взрослых летит?» and allows selection of 1–9. Trying to search with the question open makes zero searches. Selecting 1 permits the subsequent explicit search. If the traveler had manually selected 2 before recognition, that choice remains visible and usable.

#### Scenario: Repeated description or conflict with a manual edit

- **WHEN** the previous successful recognition filled MOW and the new phrase says only «Хочу в Сочи на выходные, нас двое»
- **THEN** the previous suggested origin clears and the origin question appears. If LED had instead been entered manually, it is retained. If a later phrase explicitly says «из Москвы», the traveler chooses between LED and MOW before a search can run.

#### Scenario: Unsupported composition omitted by the responder

- **WHEN** the request says «нас двое, я с ребёнком» but a fictional responder returns only «нас двое» as party evidence and no restriction
- **THEN** the form explains that the search supports adults and blocks a two-adult submission. An explicit return to manual input preserves manual edits, clears the review and issues zero searches.

### Requirement: Manual search remains available during AI failure

Flights SHALL keep ordinary manual entry and structured submission usable when recognition is unavailable, disabled, times out or returns an unreadable reply. A failed attempt SHALL leave existing fields intact and explain how to continue. A late reply SHALL NOT overwrite subsequent edits or initiate a search.

#### Scenario: Unavailable AI with a manual route in progress

- **WHEN** the form already has LED and 2 adults, the traveler asks to fill it, and the fictional AI times out
- **THEN** the traveler sees «Не удалось разобрать запрос. Заполните поля вручную.», still sees LED and 2 adults and can edit every search field. Recognition makes zero search or flight-supplier calls and does not retry automatically.
- **AND** the traveler completes AER and departure 2026-12-12, then presses «Найти рейсы». One ordinary one-way search runs with those values even while AI is down.

#### Scenario: A late reply after manual submission

- **WHEN** preview is still pending, the traveler finishes the valid form manually and presses «Найти рейсы», then the old reply arrives
- **THEN** one structured search uses the manual values. The late reply changes neither those values nor the new results. Double activation of fill for one pending attempt sends only one preview request.

### Requirement: Existing search clients retain their contract

The new extraction operation SHALL return review fields or a typed failure. It SHALL have no flight-supplier effects. Existing clients of `POST /api/flights/search/nl` SHALL retain the request, response, error and immediate-search behavior they already consume.

#### Scenario: Both operations coexist

- **WHEN** a contract test sends a complete fictional description to each operation separately
- **THEN** the review operation returns editable fields with zero search dispatches. The original endpoint accepts its original request, runs the search after successful parsing and returns the original offer response.

### Requirement: Mechanical acceptance at zero external-model spend

Acceptance of these form and API behaviors SHALL use fixed fictional AI outcomes and flight suppliers. Real-model recognition quality SHALL require a separate evaluation and budget decision.

#### Scenario: No paid-client fallback

- **WHEN** preview is enabled in the fictional acceptance profile but its dedicated fake responder is missing, even if the legacy NL client is configured
- **THEN** preview is unavailable, the legacy model client receives zero calls, no flight supplier is contacted and the ordinary manual form remains usable.

#### Scenario: Reproducible acceptance

- **WHEN** the complete, partial and unavailable cases run with a frozen calendar, a fake AI responder and instrumented search and supplier boundaries
- **THEN** their field values, user actions, dispatched requests and supplier counts can be checked without external model credentials or charges. Passing these cases records only the behavior exercised by the fixtures.
