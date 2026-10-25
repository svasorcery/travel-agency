# Proposal

## Why

The Moscow demo shows interest coverage for the whole trip even when an individual day has no stop tagged with a selected interest. Its walking explanation also fails to say that the mapped-crossing allowance applies only to the upper scenario. The retained Q01–Q12 AI fact audits identified these explanation gaps; they did not establish human route quality.

## What Changes

- Label the existing coverage summary as applying to the whole trip and show selected interests represented and absent in each preview day.
- Explain the lower and upper walking scenarios precisely in the UI, retaining the original package assumptions as source text.
- Preserve selection, response decoding, adoption/save behavior, v1 bytes/expiry and default-off behavior. Human review remains deferred at the user's request, without scores or signoff.

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `trip-itinerary-candidates`: explain daily selected-interest coverage and the existing asymmetric walking formula in the preview.

## Impact

Angular Trips preview and focused component tests; additive requirements in the existing capability. No backend/API/schema/dependency change, route regeneration, data release or target enablement. The user selected this bounded source work on 2026-10-25 and subsequently authorized a separate PR to dev with green CI. Merge remains excluded.
