# Task 4.1 independent specification and quality review

**Spec compliance: FAIL — one P2. Task quality: Needs fixes.**

Scope: task 4.1 only, fixed base/HEAD `e654c76915103a82083ef2abc01c0579117c9749`, no new commits. Reviewed the complete six-file frozen `task-4.1-review-package.txt` against the entire task brief including its final ArrayBuffer/fatal-UTF8 ruling, accepted `api-contract.md`, design sections 4/4.1 and public interface. Implementer report SHA256 independently checked: `a9d59b2554eb77409d853f6e3e13ff0238da0385dd0f00f6df598f4462433b52`. The controller supplied verification of all thirteen source/interface/vector hashes in `task-4.1-hashes.json`; this review does not replace that receipt.

## Strengths

- `shared/ts/api-client/src/trips-itinerary.decoder.ts:24`: bounded byte decoding precedes fatal UTF8 parsing; the recursive parser rejects BOM, invalid surrogates, malformed grammar, nonfinite numbers, excessive container depth and duplicate decoded keys, including escaped spellings. Null-prototype objects avoid prototype setter effects.
- `shared/ts/api-client/src/trips-itinerary.decoder.ts:152`, `:212`, `:319`, `:391`: seven-digit UTC precision, exact expiry/model leases, evidence type/scope/date binding, directed adjacency, known/unknown access, null money/times and modelled/not-evaluated attribution are checked independently. Revisions and path/anchor Int64 values remain strings.
- `shared/ts/api-client/src/trips-itinerary-api.service.ts:32`, `:50`, `:68`: success/error bodies retain bytes, requests preserve exact If-Match, no-store and disabled transfer cache; finite timeout and transport errors remain distinct without retry. Typed problem status/code pairs are checked; GET404 disabled and POST404 not-found retain the required route context.
- `shared/ts/api-client/src/trips-itinerary.decoder.spec.ts:5` reads the six actual API fixtures as raw bytes. `apps/web/src/app/trips/trips-itinerary-api.service.spec.ts:14` uses explicitly reencoded semantic imports and a test-owned ArrayBuffer; it makes no raw-fixture byte-equality claim and adds no production realm workaround.

## Required fix

**P2 — unknown empty-name properties pass the exact-schema validator.**

Location: `shared/ts/api-client/src/trips-itinerary.decoder.ts:115-121`, especially line119.

The default `optional = ''` becomes `['']` through `optional.split(' ')`. Consequently every ordinary schema allows a property named `""`, at the root and within nested schema objects. A ready response with eleven fields and a candidate with an unvalidated empty-name subtree are returned as valid. This violates the accepted contract's rejection of every unknown property and the exact ten-field availability boundary. The problem schema supplies `traceId` explicitly and is not affected by this particular default.

A single read-only in-memory probe compiled the frozen diff's decoder with the installed TypeScript compiler and used the retained M1 helpers and actual byte-encoded fixtures. Results:

- ready + unknown empty root key: **ACCEPTED**
- candidate + unknown empty root key: **ACCEPTED**
- candidate + empty key in preparation: **ACCEPTED**
- candidate + empty key in a fact: **ACCEPTED**
- control ready + ordinary unknown `extra`: **TripsItineraryContractError**

Fix the key-list construction so an absent optional list contributes no allowed key, e.g. filter empty tokens or use an explicit empty array. Add raw-byte regression cases for empty-name unknown properties at ready/candidate roots and nested candidate objects. Preserve optional `traceId` acceptance and existing strict duplicate/byte boundaries. This is a source fix required before task4.1 acceptance, not merely broader coverage advice.

No additional P1/P2 findings identified in this task scope.

## Verification evidence

Read saved logs; no package suites were rerun:

- `task-4.1-api-client-final.log:91-94`: new decoder69/69; aggregate340/340 in17files, no failed/skipped tests in the reported totals.
- `task-4.1-http-final.log:21-22`:32/32 in1file.
- `task-4.1-web-final.log:140-141`:614/614 in52files.
- `task-4.1-red-node-meaningful.log:53-107`: seven missing-feature positive failures and38throwing-guard passes. Those early guards do not establish negative RED behavior.
- `task-4.1-red-edge-precision.log:63-97`: seven behavioral failures,48passes; `task-4.1-red-404-context.log:24-41`: one failure,31passes; `task-4.1-red-identity-lease.log:77-91`: one failure,68passes.
- `task-4.1-tsc.log` exists and is empty, consistent with the reported production compile exit0; exit status is supplied by the implementer receipt, not independently reconstructed from an empty log.
- `task-4.1-biome-final.log:423-424`: six checked files,192infos; no warnings/errors reported, consistent with reported exit0. `tsconfig.base.json:8` confirms noPropertyAccessFromIndexSignature, explaining bracket-access diagnostics. Existing NO_COLOR/FORCE_COLOR process warnings are visible in Nx logs and acknowledged; no unrelated root configuration fix is requested.

Named outside-diff checks: canonical-date/Int64 helper behavior in `shared/ts/api-client/src/trips.decoder.ts`; zero-path/formula compatibility in `modules/trips/Travel.Modules.Trips.Core/Itineraries/ItineraryFacts.cs:173-203`; bracket-access configuration at `tsconfig.base.json:8`. Core permits zero length exactly when anchors match, while POIs differ; the implementation matches that authority over the brief's earlier nonzero shorthand. Initial combined output truncated the diff; only the missing package portions were recovered, with no changed-source reread. No Git calls, source edits, broad crawl, subagents, Host/DB/network calls or Q outputs occurred.

## Limits and remaining gates

The decoder can validate only invariants visible on the wire. Original brief/pace/budget/origin, expected saved revision/days, current-clock expiry and current availability identity belong to retained state/adoption checks in task4.2. Geometry/path-hash recomputation, real-source truth, human route quality, downstream UI adoption, Q_demo/Q_live, CI and target/live acceptance are not established by this review. Parent-owned README integration remains outside these six source files. Existing M1 sources are outside the change; their untouched status is the controller's supplied scope receipt.

**Assessment: Needs fixes.** The implementation provides the intended bounded transport and semantic validation, but the shared unknown-key check has a confirmed escape that must be closed and rechecked.
