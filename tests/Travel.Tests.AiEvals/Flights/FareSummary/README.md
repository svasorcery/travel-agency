# Fictional fare-summary evaluation packet v1

`manifest.json` fixes 48 case IDs before adversarial outputs or model responses: 32 tuning, 16 holdout. Each of the four primary categories has 12 cases; secondary tags may overlap. The holdout has 12 predeclared mixed eligible cases and four expected refusal/no-data cases. Both partitions contain nine-adult, four-leg source graphs. Eligibility is a source/input/configuration decision, never selected from successful model outputs.

Every non-legacy case includes the complete `normalizedEvidence` graph, a fictional frozen snapshot, root and per-leg conditions, all adult–segment pairs, optional code/marketing labels, observation/expiry/departure times, and `expectedKnowledge` with exact permission/money/scope/baggage/unknown state. Legacy cases have an absent snapshot and expect `Unavailable`. `FaultInjectedCatalog` cases deliberately corrupt the leaf input after source construction; the remaining cases use the actual `FareSummaryFormatter.BuildFacts` catalog. No passenger profiles, real supplier references, keys or payment data are present.

The fixed IDs, partitions and holdout eligibility must not be changed based on model outcomes. The packet metadata remains `UNSELECTED`, `UNVERIFIED`, `paidRunExecuted=false`. Selecting a future model/configuration creates a separate receipt containing packet SHA256, exact configured/actual model, prices, prompt/render versions, usage, cost, latency and every holdout outcome; it does not rewrite the frozen source packet.

`FareSummaryPacketTests` reads the real snapshot types and calls the actual formatter and extractor with fictional clients. Its 48-case test checks permission, money, currency, per-leg and adult–segment scopes, explicit zero versus missing quantities, mandatory notices and fallback. The separate captured-payload test builds the actual root catalog for all 12 eligible mixed holdout cases and verifies the same embedded prompt/schema/UTF-8 admission estimate used by dispatch. A failed bound leaves the candidate off; no essential facts may be dropped to fit. `adversarial.json` has 16 preselected invalid responses, including missing/duplicate/invented IDs, wrong sections, changed money fields, duplicated JSON properties, unequal grouping, root/leg union, oversized/free text and tool content. Known SDK usage must survive rejection.

## D7 gate and runner

`FareSummaryEvalPacket.Score` keeps every preselected eligible holdout case in the denominator. Invalid output, deadline, skipped eligible attempt, missing outcome and deterministic fallback are failures for B. The four ineligible cases must have zero dispatch and cannot be counted as model successes. The gate requires at least 95% valid organized results, zero displayed unsupported claims, complete money/scope/unknown and mandatory-fact preservation, p95 measured optional-stage latency at most four seconds, and actual request costs at most USD 0.10. Unknown usage/cost cannot pass.

Two distinct Russian-language readers compare A/B blind on the same 12 eligible cases. The scorer conservatively requires each reader to record at least eight B wins, at most one B loss, and no missing mandatory facts. Fallback cannot become a B win. Synthetic votes in unit tests verify only this arithmetic, never human acceptance or model quality. No LLM-as-judge is used.

`FareSummaryPaidEvalRunner` is source only and has no xUnit attributes or automatic entry point. A future invocation requires separate human approval, `authorized=true`, the exact token `APPROVED_FICTIONAL_FARE_SUMMARY_EVAL` in `TRAVEL_FARE_SUMMARY_PAID_EVAL_AUTHORIZATION`, an explicitly supplied key, and enabled verified exact-model price configuration. A CI key alone cannot run it. Missing any gate throws before constructing a provider client. Neither this packet nor the token grants authority to run the model.

The runner preflights the actual fixed holdout catalogs with the same estimator before any paid call. Its per-request upper estimates are summed into the receipt and must remain under USD 0.10 each (at most USD 1.20 for the 12 eligible attempts). No default model or actual price is claimed. Eligible requests are paced outside measured optional latency to respect six process admissions per rolling minute; the runtime itself never waits in a queue. Ineligible cases make no provider call. The same extractor uses the no-retry SDK client, and a counting wrapper records actual local model invocations instead of inferring dispatch from a status string.

The receipt measures this fictional extractor/controlled-formatter path. Every runner outcome defaults `WholeOptionalStageMeasured=false`, so its partial timing cannot pass the scorer or enable the candidate. Only separate measured whole-stage evidence including production accounting and final authoritative freshness read may set that flag. It does not prove deployed Host final-source-read latency, shared account spending, provider entitlement, payment/supplier behavior, deployed topology or rollout compatibility. Existing source CI and separate authorized real-model/human/live evidence are still required. No model run, human comparison, deployment or quality acceptance has been performed by creating these files. The feature remains off.

## Spec scenario mapping

| Canonical scenario | Cases / source test |
|---|---|
| Missing allowed member; Explicit true or false | FS001–008, FS011–012, FS015, FS021–022, FS025, FS030, FS034 |
| Allowed with unknown penalty; Zero and a different currency; Invalid penalty pair | FS007–008, FS013–024, FS028–029, FS033, FS036 |
| One leg allows change; No whole-offer condition | FS025, FS028–030, FS033–036 |
| Brand without code; Nine adults with different allowances; Missing baggage type | Source labels stay outside captured payload; FS004–006, FS009–010, FS023–027, FS031–032, FS035; raw provider/label fixtures remain in provider tests |
| Full text not provided; First flight departed / stale source | All 48 formatter cases retain completeness notices; FS048 source expiry; application clock/source tests cover pending inference/departure |
| Historical bool-only quote | FS001, FS011 absent snapshot; historical event/owner/replay behavior remains in Core/Application tests |
| No AI key or model disabled; Complex scoped data | Extractor default-off/model-price unit tests; FS021–036, FS042, FS047 |
| Invented fee or claim; Incorrect grouping or omitted restriction | ADV01–16; scope-kind regression and all fact-ID coverage |
| Malicious supplier label; Passenger details exist elsewhere | FS037; all 12 captured actual holdout payloads exclude source code/brand/marketing labels and request correlation |
| Budget or concurrency exceeded; Timeout or unknown usage; Failure after a possible model call | FS041–048; admission boundary, SDK fake HTTP, noncooperative model/ledger bounded wait, no-retry/accounting tests |
| Refunds allowed; Refundable fare with selected extras; Zero penalty and priced services | All non-legacy formatter cases assert controlled financial/selected-extra notices; no mutation dependency is invoked |
| Fake model tests pass; Safe fallback in an eligible evaluation case; Insufficient/postselected comparison cases | Fixed split + actual catalog budget test + pure scoring failure-denominator tests |
| Same-price revision, concurrent replacement, owner/privacy/browser lifetime, additive readers, source CI | Separate Core/Application/HTTP/TS/transport/CI source suites; this packet does not substitute for them |

Canonical requirements: `openspec/changes/flights-m3-fare-rules-summary/specs/flights-fare-rules-summary/spec.md`; D7 and residual limits: the same change's `design.md`. A failed measured or human gate keeps A and the default-off candidate. These source fixtures do not assert economic savings or production quality.
