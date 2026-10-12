# Fictional fare-summary demonstration

Use the existing isolated Flights demo commands. `TRAVEL_FLIGHTS_FARE_SUMMARY_PRESET` selects fixed `mixed` (default), `known`, `unknown`, or `expired` facts before Node startup. No HTTP fault-control endpoint exists. An explicit ordinary re-quote with `explanationMode: organized` models rejected AI output: `InvalidOutput` and the complete deterministic explanation.

The normal demo supports one to nine adults and one to four legs. Open “Условия по участкам, пассажирам и сегментам” with mouse or keyboard: the disclosure only expands local text. Equal facts are grouped by segment; different adults retain separate exact allowances. Zero allowance differs from unavailable allowance. Brand labels are separate from fare codes. The existing private creation panel labels recorded conditions as historical and retains the original offer deadline.

This demonstrates fictional application behavior only. No actual model, supplier, payment, credentials, deployment, or model-quality acceptance is involved. Financial actions remain separate existing controls.

## UI scenario to test mapping

| Scenario / task | Source evidence |
|---|---|
| Missing allowed/penalty, explicit zero fee/allowance, exact money; 6.1 | `shared/ts/api-client/src/flights-fare-summary.decoder.spec.ts`: exact zero, unknown, malformed permission/money, decimal.MaxValue and smallest scale-28 amount, decimal overflow, informational quantity100/Int32.MaxValue and overflow; selected purchase limits unchanged |
| Old API response and malformed added summary; 6.1 | same decoder suite and `flights-quote.decoder.spec.ts`: quote survives malformed addition with notice, absent legacy fields stay readable |
| Same-price replacement, stale binding and complete route/party; 6.1, 6.3 | new summary decoder suite: exact offer/revision/count/segment bindings, stale retained history, four legs/nine adults, complete scoped pairs; existing Flights page tests preserve quote acceptance and callback generations |
| Earliest departure / original expiry / historical conditions; 6.2, 6.3 | `flight-fare-summary.component.spec.ts`: deadline cannot be extended by viewing, historical state remains stale; existing Flights order-page tests preserve owner/epoch/navigation/late-response guards |
| Brand versus code, equal grouping, supplier markup; 6.2 | component suite: brand label rendered as plain text, actual fare code separately, identical facts grouped, changed allowance splits group |
| Keyboard/mobile scope details and no explanation HTTP/financial actions; 6.2, 6.4 | `tests/travel-e2e/demo/flights-fare-summary.spec.ts`: four legs/nine adults, 390px viewport, keyboard disclosure, unchanged API count, zero hold/confirm/cancel/refund |
| Rejected fake AI plan and expired/historical presets; 6.4 | `tools/demo/flights-fare-summary.test.mjs`: deterministic fallback, unchanged exact zero fee, original deadline, no current history |
| Honest optional AI fallback and organized label; 6.2, reviewP2 | `flight-fare-summary.component.spec.ts`: Busy/BudgetExceeded/InvalidOutput/UsageUnknown/AccountingUnavailable/DeadlineExceeded reasons, explicit disabled fallback, default quiet baseline and historical suppression; `flights-fare-summary.spec.ts`: rejected fictional plan reason visible after explicit re-quote, mode choice and disclosure zero requests, original exact fee preserved |
| Included allowances share rich informational Int32 contract; reviewP2 | `flights-ancillaries.decoder.spec.ts`: full catalog quantity892/Int32.MaxValue accepted, checked/carry-on overflow rejected; purchased quantity100 remains rejected by independent selected-service cap |
| Existing fictional booking/cancellation/refund behavior; 6.3, 6.4 | full existing Node/demo/browser regression suites; fixed expectations remove bool-only authority, preserve financial action guards |

Scenario mapping includes preserved regression guards, not a claim that every possible live interleaving or model output has been exercised. AI organization remains fake in this demonstration.
