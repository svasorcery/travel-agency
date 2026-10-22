# TASK2.1 independent source review

Date: 2026-10-21. Scope: TASK2.1 only. Verdict: PASS for the reviewed normalized Core fact-model source unit; no confirmed P1/P2/P3 findings. This is not final M2 acceptance or a data-release decision.

## Evidence and scope

Read task-2.1-brief.md and task-2.1-interface.md first, then independently reviewed the complete owned ItineraryFacts.cs, ItineraryFactsTests.cs and ItineraryTestFacts.cs. Read root/module AGENTS.md, existing TripValues.cs validators and DestinationSelection, and the approved trips-m2-moscow-itinerary design/data-freshness/directed-route requirements. Treated task-2.1-report.md as claims to compare against source and receipts.

Reviewed source SHA256:

- ItineraryFacts.cs: DA933D5B833763EA2F64888D550E0ACB408B47DDA038E42AC0D94E2A4CDABB54
- ItineraryFactsTests.cs: 2DE734DE520F3C4AE0156A026B0ABF2188D16AA95236EE9FEF7AEC473DC6372E
- ItineraryTestFacts.cs: D91E39AF78213149DEDCFFCCE865BD7D9B37530DE52E1D07B5C6B039127C1F4F

No source edits, Git mutations, Host/DB/model/provider calls, production package or additional agents. Only this review file is authored by the reviewer.

## Confirmed findings

P1: 0. P2: 0. P3: 0.

The approved whole-package validation boundary is present. Invalid generic evidence cannot enter ValidatedItineraryFacts merely because its standalone enum/string shape is valid: the package rechecks rights, kind, exact scoped references and permitted fields (ItineraryFacts.cs:490-539). No correction requested.

## Checks across the complete task

- Evidence factory checks IDs, defined enum values, nonempty bounded/well-formed text, HTTPS URLs without credentials/fragments, UTC offsets, observed <= reviewed < validUntil, and 90/7/30-day maximum durations without overflow-prone AddDays (ItineraryFacts.cs:79-121). Source length has both scalar/text validation and the explicit 256-character limit. Age is measured from reviewedAt, as specified by the approved field schema; it is not silently measured from read time.
- Knownness uses the supplied saved destination zone without a Moscow default. IsKnownAt is half-open; date-knownness follows the published interface's inclusive review local date and requires the whole requested local day to end by expiry. Null/invalid zone and DateOnly.MaxValue fail closed; invalid/ambiguous next-midnight fails closed (124-143). The inclusive review date is an explicit task interface choice, so it is not reported as an invented full-instant lower bound.
- Access separates unknown from known closed and explicit full-day hours. Null collections/elements, overlap, duplicate exceptions and limits are guarded; absent weekday intervals mean closed only after known evidence passes. GetIntervalsOn checks evidence ID/kind/POI scope/OpeningHours/date before returning any value (190-244).
- POI factories check title/scalar validation, tags, dwell, entity/revision syntax, coordinate bounds and access-to-POI scope (285-329). Walking edges are directed, have no self-edge, require bounded positive ordered durations and bounded explicit restriction text (361-388).
- Package validation checks all collection null/bounds/element paths before dereferencing, unique local IDs/evidence IDs/directed pairs, valid endpoints, tag coverage, exact CC0 entity-revision identity URL, separately scoped permitted access/walking evidence, no orphan evidence and evidence valid at package review (450-539). No claim of geographic continuity or real observation is derived from these structural checks. Disconnected route behavior belongs to the later consumer/quality scenarios, not an implicit new connectivity acceptance rule here.
- Immutable sealed fact objects have private constructors and get-only members. Every collection is copied into a read-only wrapper; nested values are immutable, and DestinationSelection is an immutable record snapshot. No external wire DTO or production dataset was added.
- Fictional builders are explicitly labelled and go through the same minimum 18-POI/60-edge validation. Their repeated placeholder Wikidata entity is fictional test evidence, not proof of distinct real production places or a released coverage matrix.

## Test evidence and limits

Reviewer command:

`dotnet test tests/trips/Travel.Modules.Trips.Tests.Unit/Travel.Modules.Trips.Tests.Unit.csproj --no-restore --no-build`

Result: exit 0; 72 passed, 0 failed, 0 skipped. This independently reran the existing compiled unit assembly; it was not a fresh reviewer build. Inspected task-2.1-green.log also records the author's full --no-restore build/test command with 72/72 and the existing Verify SolutionDir discovery warning. Source/test timestamps precede the corresponding DLL timestamps, but timestamps are not presented as an exact compiled-source attestation.

The focused tests meaningfully cover duplicate IDs/pairs, missing/foreign/wrong-kind/orphan evidence, CC0/revision/field integrity, validity limits to a tick, unknown/closed/full-day access, source/enum/text/null cases, interval overlap and exceptions, defensive copies, saved zones and package lower bounds. Source review also traced individual near-null branches, upper-bound predicates, offset/date ordering and access/walking mapping branches. Those individual branches are not all separately exercised by the current tests; no unsupported claim of exhaustive runtime/property testing is made. No concrete failing behavior in those reviewed branches was established.

The earlier 53-test baseline and RED history remain author/parent receipts, not newly recreated reviewer evidence. Tests of fictional structural records do not prove actual source freshness, reuse permission, real graph quality or human signoff.

## Remaining gates

Later tasks still own generator/result validation, Application destination matching and Moscow pilot policy, Infrastructure exact-byte hash/manifest/path/size/withdrawal checks, README/progress integration and complete M2 verification. Actual curated records, a real immutable payload, curator and independent human rights/route acceptance remain pending. Newly authorized source research does not satisfy those gates or authorize a production package/model/provider/target rollout.
