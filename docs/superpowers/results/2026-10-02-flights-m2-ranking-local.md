# Flights M2.1: explainable ranking verification

**Scope:** user approved the specification/plan on 2026-10-02 subject to a second self-review; that review found no blocking contradictions. This report tracks local evidence before publication. Independent review and local verification passed; delivery still requires mandatory CI on the published HEAD, merge and cleanup.

**Base:** fresh `origin/dev = d75186054a33de0e1cb688da7e3e7cc42927dadf`, checkpoint ancestry verified; task branch `codex/flights-m2-explainable-ranking` starts at that exact SHA in the managed `flights-m2-design` worktree. No archived worktree was restored.

## Behavior

- Deterministic price-first ordering has separate actual-currency groups and no cross-currency winner. Equal prices compare supported duration/transfers, then a stable canonical key. Source price and conversion state are preserved.
- Partner duration/transfers remain unknown despite synthetic mapper fields. Conservative dedup preserves distinct references, purchase paths, route segments, currencies, prices and fare terms.
- Search responses add optional ranking metadata. Structured/NL endpoints share mapping; no AI is called by ranking. Runtime TS validation rejects contradictory metadata; legacy responses retain server order without invented explanations.
- Versioned Redis cache stores complete search evidence and provider failures. Invalid or old payloads are misses; no database schema or booking event changes.
- Existing result cards disclose factors through keyboard-accessible details; FX limitations and currency groups are visible. Demo fixtures deliberately exercise equal-price known/unknown factors and separate-currency failures. The stub does not implement an independent ranking algorithm.

## Local verification

| Gate | Evidence |
| --- | --- |
| Baseline Flights unit | 483/483 before implementation |
| Ranking regressions | 6 behavioral failures observed before implementation; expanded Flights unit suite 497/497 |
| Cache regression | Missing warm-hit ranking reproduced against actual disposable Redis; complete-result equality covered after change |
| API client | 14 new validation failures reproduced; 63/63 after implementation and fixture additions |
| Lightweight HTTP | Missing ranking reproduced on structured/NL routes; 51/51 after mapping, fake bus/store, no database |
| Web unit | 4 absent behavior failures reproduced; 235/235 after implementation |
| Fictional demo | Node missing-ranking regression reproduced; 25/25 Node and 13/13 Chromium journeys pass |
| Visual inspection | Desktop and 360px screenshots inspected; disclosure focus retained, no horizontal overflow |

Final local gates also passed: selected real Redis/fake-provider suites **23/23**, architecture **167/167**, contract **9/9**, .NET Release solution build (0 errors), Angular production and flights-demo builds, web/API-client ESLint, AI harness/inventory/README validators, CSharpier, Biome without error-level diagnostics and diff whitespace checks. Unit **497/497** and web **235/235** were rerun after review corrections. Existing AD0001/Verify discovery and informational/non-null diagnostics in untouched files remain; no check was disabled.

## Independent review and correction

A fresh-context reviewer inspected the complete tracked diff and untracked implementation/tests/docs. One P2 was found: JSON-restored converted money could have a negative displayed amount despite a nonnegative source price, pass the cache validator, and then fail browser decoding repeatedly until TTL. The same constructor/factory gap applied to restored currencies and negative duration evidence.

Three actual Redis corrupted-envelope tests reproduced the old behavior (3 RED failures). Validation now checks both prices, both currency codes and nonnegative factors before recomputing ranking. The complete Redis suite then passed 23/23; the reviewer independently inspected the correction and reported no remaining findings. No live API or schema execution was used for review.

## Delivery evidence boundary

This report records local evidence before publication. Remote CI and merge must be verified on the actual PR HEAD; this document does not claim they have already happened.

## Rulings and evidence limits

- Whole seconds are the shared duration precision for sorting and explanation. Zero prices remain valid because the existing Money contract permits zero.
- Current OpenAPI responses for search describe `IResult`; the additive DTO does not introduce a new OpenAPI response schema automatically. Real HTTP serialization plus TS decoding prove the new fields. The existing Host OpenAPI snapshot remains a CI regression gate, not a claim of generated typed responses.
- Selected Redis fixtures start only a disposable Redis with fake suppliers/FX. The HTTP fixture uses ASP.NET TestServer/fake bus/store. Full Host/AppHost/PostgreSQL fixtures apply schemas and were not run locally. Required CI still owns those lanes.
- No supplier, payment, Anthropic, paid evaluation, local schema application or deployment was invoked. No auth, booking events, migrations or CI/CD configuration changed. Historical PII and B5 limits remain as documented; M2.1 does not implement saved travelers, multi-passenger or multi-leg search.
- Fake-auth browser success does not establish real JWT issuance, provider behavior, payment or ticket delivery. Existing OIDC redirect draft code was not extended; no new token, operation-state or PII browser persistence was added.
