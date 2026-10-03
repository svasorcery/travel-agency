# Flights M2.5 local verification and M2 capability audit

**Date:** 2026-10-03
**Boundary:** local/source acceptance for the approved M2.5 increment; remote delivery is verified through the attached PR and CI runs. This report does not infer merge or real supplier/payment behavior from local tests.

Fresh `origin/dev` base was `df7708716161750f053711fc3b663267833f0351`. M1 `d75186054a33de0e1cb688da7e3e7cc42927dadf` and the accepted M2 checkpoint ancestry were verified. A new managed worktree/branch starts at that exact SHA; no archive was restored. The user approved the specification/plan and full delivery cycle, then required one more self-review before code. That review clarified nested defensive copies, malformed fresh-route validation, first-local-date cache/party binding and exact fixture/test names.

## Implemented contracts

- One complete offer/order for 1–9 adults across 1–4 ordered legs, including open-jaw. No split booking/payment.
- New anonymous search/v2 uses exact airports and nondecreasing requested local dates. Old search/NL city semantics remain.
- Duffel airport-local timestamps resolve through IANA rules with explicit DST/offset rejection; no assumed UTC/host fallback.
- Cache v4 includes route mode/all legs and current sorted capabilities; cold/warm route/party/provider metadata validates. V3 naturally misses/expires.
- Historical event/DTO reads preserve stored instants/durations and unconditional replay. Core kind stays computed/ignored; unified HTTP/TS geometry readers add kind without using it as history version.
- UI renders every leg/cabin/ground gap/local offset and requires acceptance after any route fact changes. Profiles, slot IDs, owner/edit generations and unknown-write barriers retain their contracts.

## Local evidence

All .NET build/test commands were serialized. Pure Unit and audited fake TestServer/mediator-only metadata fixtures ran locally; actual DB/full Host/Aspire fixtures remain existing CI suites.

| Check | Observed result |
| --- | --- |
| Core domain/replay/projection | Task1 final825 unit;100 focused; independent spec/quality PASS |
| Duffel resolver/mapper/stub requests | Task2 final894 unit;100 focused; Integration project compile only; independent PASS |
| Search/cache/ranking | Task3 Search149;full Unit948; independent PASS; source-only existing Integration fixture alignment |
| Current HTTP/history/actual generated metadata handler | Task4 final130 safe cases; initial50 expected RED; independent P2 fixed with4 RED→GREEN null-history cases |
| Shared TS readers |103/103, including kindless old and current kind-bearing historical overlap; rerun after current fixture variants |
| Angular |360/360, including late-leg/cabin acceptance, offset routes, stale profile/criteria and unknown hold; rerun after fixtures |
| Production web build | Passed; initial297.54kB, Flights lazy112.06kB |
| Fictional Node protocol |50/50 after fixing all3 review P2 body-validation cases |
| Fictional Playwright | Targeted7/7 and full43/43; desktop/mobile screenshots independently inspected; owned servers stopped/ports4201/5100 free |
| Dependency-free AI harness |90/90 tests and instruction/skill/role inventory validation |
| Offline README checks |12/12 tests and catalog match; no real Host smoke |

Full solution Release compilation passed without schema/runtime execution and was repeated after the history repair;948 Release Unit passed on that final build. Complete architecture suite passed167/167. Csharpier checked608 files; full Biome checked184 files with zero errors. .NET inventory passed62 tests with one existing environment skip and validated the manifest.

The first complete architecture attempt reported165 passes and two matrix failures; a diagnostic run of both passed without source changes, followed by a complete167/167 pass with normal logging. No architecture rule or evaluation timeout changed. Full Biome initially caught two new fixture formatting errors and the Angular bound-label attribute; fixture formatting preserved all legacy response raw text, and the ordinary interpolated label retained runtime IDs/layout. Focused HTML lint and70 existing form/page tests passed afterward. Independent whole-branch review at source `70fef7137835b222d142059b6141ca41baa23f0d` passed specification and quality with no P0–P2. Its single P3 stale approval handoff was explicitly marked historical before publication; no product code changed after that review.

## Reviews and repairs

Independent per-task reviews cover Core, Duffel, cache, UI, demo and HTTP. Demo review found non-string airport coercion, duplicate JSON fields and minimum date acceptance; actual HTTP RED47pass/3fail preceded fixes and Node50/50, then scoped PASS re-review. HTTP review found a new null-slice dereference in historical flat geometry normalization; four detail/list regressions reproduced it before the minimal guard preserved the malformed payload/client contract-error path. No fresh history validation or data rewrite was introduced.

Historical `.response` payloads remain unchanged across 12 search and 5 booking fixture cases; additive `currentResponse` variants support current exact HTTP assertions. Actual isolated Wolverine OpenAPI paths,15 schemas and all 5 global tags were observed and matched to the corresponding full Host snapshot sections. Actual full Host snapshot execution remains CI.

## Setup incidents and retained limits

The installed Playwright package required Chromium 1234; only 1208 existed. Normal exact browser installation supplied the required version, with no executable override/dependency/config change. Test-helper failures were fixed by awaiting actual render counts and preserving the existing post-login re-quote/acceptance flow.

C: became full during Host-test dependency copying before tests. Root measured 413627851 bytes of owned generated outputs, verified copies to D: and made bin/obj junctions preserving normal C: paths/commands; remaining project outputs were prepared likewise. No source/CI/settings or foreign caches changed. All output junctions and the task-specific D: artifact directory are cleanup resources.

Existing AD0001 ASP.NET analyzer and Verify solution-discovery warnings, pre-existing non-null/accessibility warnings and informational literal-key suggestions (full Biome14 warnings/198 infos), and the new Flights page 4.50kB style warning against 4kB warning/8kB error thresholds are documented without suppression or budget changes. These are local limits, not hidden failures.

No real provider/payment/Anthropic/paid API, paid eval, local Host/AppHost, schema apply/migration, key provisioning, deployment or authenticated Codex verifier ran. Backend auth, booking fingerprint/idempotency, PII purposes, V1–V3 identities/replay, CI/CD and Duffel cancellation-before-HTTP behavior remain. M3/OpenSpec, refunds, ancillaries, new SSE and Support remain outside scope.

## Four-capability M2 audit

| Capability from concept M2 | Source/acceptance evidence |
| --- | --- |
| Explainable ranking | Free deterministic price-first policy retained; full-leg/dedup/60h/ground-gap regressions pass |
| Multi-passenger booking | Existing1–9-adult quote-bound protected party/one aggregate retained; two-adult open-jaw/four-leg one-order browser acceptance |
| Private saved travelers | Existing encrypted owner-scoped CRUD/copy remains; two saved adults fill unchanged slots with stale-response/owner/edit guards |
| Multi-leg/open-jaw | New ordered domain/provider/cache/v2/shared-reader/UI flow and fictional full-order acceptance above |

M2 closure is conditional on actual merge, mandatory exact-head PR checks including E2E, postmerge checks, clean synchronized dev, no unpublished commits and own resource cleanup. These are explicit delivery gates, not claims made by this local report.
