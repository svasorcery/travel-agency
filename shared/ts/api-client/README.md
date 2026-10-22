# api-client

This library was generated with [Nx](https://nx.dev).

## Building

Run `nx build api-client` to build the library.

## Running unit tests

Run `nx test api-client` to execute the unit tests via [Vitest](https://vitest.dev/).

## Trips M2 candidate and availability contracts

The M2 exports are separate from the ordinary M1 plan/write contracts. TripsItineraryApiService.candidate(tripId, revision, token) sends the read-only empty-object POST with an exact strong Int64 If-Match. availability(token) reads only optional-generation metadata. Both use ArrayBuffer responses, no-store, disabled transfer cache, a finite15second client timeout and no retry. The server's computation deadline is a separate cooperative two-second limit.

decodeTripItineraryCandidate, decodeTripItineraryAvailability and decodeTripItineraryProblem consume original bytes before fatal UTF8/JSON/schema checks. Optional text decoding is available to non-HTTP consumers, but the HTTP service preserves bytes. CandidateVersion1 is strict: unknown or duplicate decoded keys, invalid text, wrong scoped facts, timestamps, model formula, nullable money/times or complete bounds reject the entire response with TripsItineraryContractError. The [actual API contract](../../../openspec/changes/trips-m2-moscow-itinerary/api-contract.md) defines the fields.

| Outcome | Meaning |
| --- | --- |
| candidate | Stops were proposed; filled/partial coverage is independent of unknown hours, costs and human quality. |
| empty | Current source was processed, with no eligible proposed stops; it is not a transport or source failure. |
| unsupported | The saved M1 scope is outside generation; no fake source identity is attached. |
| ready metadata | One current package identity is available; this does not read a private plan or establish live suitability. |

TripsItineraryRequestError preserves typed authentication, precondition, conflict, busy, unavailable, deadline, transport and timeout outcomes. Availability GET404 is disabled; candidate POST404 is not-found because feature-off, deletion and foreign-owner privacy share the server code. A malformed proxy response is a contract failure, not a manufactured disabled/ready result. Int64 revisions, Wikidata revisions, path lengths and anchors remain decimal strings.

The decoder checks only wire-visible consistency. The state/adoption consumer must still bind the response to the saved revision, destination, exact dates, wishes and pace; check its current clock; and reread both owner detail and matching availability before copying ordinary blocks. It must not advance the M1 baseline during that read, silently save/rebase, infer trusted facts from an editable note, or retain private previews in browser storage/SSR output. Runtime source truth, route quality, field calibration, CI and target enablement require their own evidence.