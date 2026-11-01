# L fixture and due-work correction plan

2026-11-01. Owner requested autonomous completion of all expected steps after the read-only L diagnosis, including source/tests/review and a newly frozen single S/L campaign. This supersedes the preceding source-only/no-new-load pause. No publication, deployment, supplier/model calls or unchanged-package retries.

Use executing-plans inline, test-authoring and TDD. Existing isolated worktree/base retained. One fresh final source review before runtime.

## Confirmed evidence and design

LoadDuffel stores two complete independent JsonNode trees for each property; L ends with14600aliases. LoadHotelbeds retains8626raw2048char keys; key UTF16 payload alone35332096B is absent from ValueBytes. Keep all logical entries and lookup history, caps, dates, output JSON, fault recipes and purchase semantics. No fixture clear/TTL purge/forcedGC.

Use one immutable serialized UTF8 rate payload per Duffel result, shared by its two aliases and parsed into independent response/quote nodes when consumed. Ordinary non-load fixture behavior stays unchanged. Store SHA256 lookup identities for load Hotelbeds raw keys; continue emitting/accepting the original opaque wire key. No authentication/provider production code changes. Retention telemetry separately reports stored key UTF16 payload and unique serialized rate payload bytes; ValueBytes keeps its prior logical serialized-value scope.

The pending L work is most consistent with an already-confirmed Hotelbeds read successor. Producer schedules it before provider read for durability; the confirmed-state guard makes it a no-op. Do not alter this product durability path. Collect bounded raw due-envelope IDs/type/dueTime/size and owner-row state, without logging bodies. Only exact HotelWork payloads for an owned confirmed/cancelled attempted reservation with booking identity and Escalation=false qualify as redundant. Unknown type/body/owner/state, unconfirmed rows and all escalations remain required/unknown.

Future acceptanceVersion3 replaces the raw-empty-queue gate with m2ActionableDueWorkAndInboxSettled, requiring pendingInbox=0, requiredDue=0, unknownDue=0 and complete bounded evidence at terminal360. Original raw-empty boolean/counts/lag remain diagnostic in the receipt. Versions1/2 retain their original raw gate; version2's final5s RSS diagnostic is inherited. This is an explicit classification change, not a numeric threshold adjustment or historical waiver. Unknown/incomplete evidence fails closed. All mandatory memory bounds and30s windows remain unchanged.

## Steps

- [x] 1. Meaningful RED/GREEN for compact immutable fixtures, original key lookup, concurrent isolation and key/payload accounting. Run permitted standalone suite.
- [x] 2. Meaningful RED/GREEN for exact due-work classification, unknown/required failures, frame/receipt roundtrip, version1/2 preservation and actual evaluator version3. Preserve M1 source sections.
- [x] 3. One independent source review; fix important findings with tests, verify historical raw hashes and new freeze/archive.
- [x] 4. Execute one new S/L campaign, stop on failure/no repeated unchanged package, cleanup and analyze. Advance acceptance only when both profiles pass every mandatory gate with full evidence.
- [x] 5. Reconcile final report/status. Failed attempts remain immutable; publication/archive/rollout remain outside scope.

## Review focus
Data/response equivalence and immutable alias isolation; no history removal; digest lookup preserves exact opaque keys; key accounting scope; actual Wolverine type/payload decoding; owner and terminal guards; no raw payload logging; complete denominator/raw queue preservation; unknown/incomplete/error/cleanup failures still block; versions cannot silently adopt new rules.



Completion: v5failedretained; subsequentv7 completedbothprofiles under explicitly approvedversion5, with the same compactstorage and due-work classifier. Finalreport records the accepted scope.
