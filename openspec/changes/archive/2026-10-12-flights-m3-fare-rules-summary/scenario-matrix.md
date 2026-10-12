# Scenario -> evidence

Это карта source/local acceptance. CI-only означает написанный и скомпилированный тест, а не выполненный CI. Paid/live сценарии остаются отдельными gates. Полные пути классов можно найти по `rg --files tests apps/web shared/ts`.

| Spec scenario | Evidence | Gate |
|---|---|---|
| Missing allowed member | DuffelFareWireTests.Missing_or_invalid_permission_is_unknown_instead_of_false | Local |
| Explicit true or false | DuffelFareWireTests + DuffelFareMappingTests.Brand_is_not_a_code_and_root_is_not_derived_from_leg | Local |
| Allowed with unknown penalty | FareCoreTests.Known_zero_is_not_unknown; formatter controlled-text tests | Local |
| Zero and a different currency | DuffelFareMappingTests + FareSummaryFormatterTests.Controlled_text_keeps_exact_fee_root_leg_and_mandatory_notices | Local |
| Invalid penalty pair | DuffelFareMappingTests.Empty_allowance_array_and_invalid_money_stay_unknown | Local |
| One leg allows change | DuffelFareMappingTests.Brand_is_not_a_code_and_root_is_not_derived_from_leg | Local |
| No whole-offer condition | Same mapping test; frozen packet cases | Local |
| Brand without code | Mapping tests; UI brand/code component test | Local |
| Nine adults with different allowances | Four_legs_and_nine_adults_retain_all_scoped_pairs; full demo Playwright | Local |
| Missing baggage type | An_unassignable_baggage_type_does_not_confirm_an_allowance | Local |
| Missing or invalid quantity and malformed labels | Wire/mapping tests; duplicate root/mandatory regressions; TS Int32 catalog cases | Local |
| Full text not provided | Formatter completeness/notices; all48packet cases | Local |
| First flight departed | Formatter expiry; quote earliest-departure UI test | Local |
| Same price and new revision | Frozen_snapshot_maps_adults_roundtrips_and_is_cleared_by_incomplete_requote; quote generation tests | Local |
| Late AI result | Late_reply_after_offer_expiry_has_only_stale_deterministic_facts; noncooperative model test | Local |
| Expiry while inference is pending | Organizer fakeclock expiry test; formatter late-plan test | Local |
| Concurrent replacement or unknown source freshness | Competing_mutation_or_failed_final_read_cannot_claim_current | Local |
| Historical bool-only quote | Optional snapshot replay/source fallback; TS missing history contracts | Local; full persisted suite CI |
| Old recalculation lacks updated offer | Frozen snapshot incomplete re-quote clears facts | Local |
| Saved conditions after hold | Frozen snapshot test applies OfferHeldV3; original expiry/fingerprint retained | Local; old V1/V2/V3 suites CI |
| Reader or normalization version changes | JSON version7 replay copies stored fingerprint; Unsupported_history_preserves_parent_count_and_selected_extras_notice | Local |
| No AI key or model disabled | Extractor Disabled/model-price gate; explicit UI fallback notices | Local |
| Complex scoped data | All48packet cases;12fixed eligible mixed holdout actual catalog byte bounds; full route demo | Local; usefulness paid/human |
| Invented fee or claim |16adversarial outputs; strict plan validator | Local |
| Incorrect grouping or omitted restriction | Validator duplicate/omitted/unequal/root-leg cases | Local |
| Malicious supplier label | Wire invalid-label tests; captured model payload excludes raw labels; UI plain text | Local |
| Passenger details exist elsewhere | Captured payload allowlist; shared contract tests have no profile fields | Local |
| Budget or concurrency exceeded | Extractor price/token/rolling-window/admission tests; accounting inflight test | Local |
| Timeout or unknown usage | Model/ledger noncooperative deadline tests; missing usage/no false zero | Local |
| Failure after a possible model call | Fake accounting failure one-call; production policy NATS faults after inference/accounting/delivery | Local fake; transport CI-only |
| Anonymous organized request | Anonymous_organized_request_never_dispatches | Local |
| Refunds are allowed | Controlled formatter financial notice; packet exact refund knowledge | Local |
| Unknown operation and repeated reads | Existing order guards retained; browser reload/SSE/unknown-operation regressions | Local browser; source HTTP CI |
| Refundable fare with selected extras | Formatter mandatory extras notice; summary component | Local |
| Zero fare penalty and priced services | Same formatter tests plus existing ancillary/demo pricing tests | Local |
| Foreign or ownerless retained resource | Existing_owner_guard_precedes_historical_summary; unchanged source owner guards | Local fake; HTTP/EF-lag CI |
| Owner changes during pending response | Existing Angular epoch/navigation tests; demo lost-response guards | Local |
| Old API response | Quote/creation decoder missing optional summary | Local |
| Invalid added summary | Strict summary decoder suppression preserves otherwise valid quote | Local |
| Fake model tests pass | Frozen packet and scorer tests; no model-quality claim | Local only |
| Model adds no measured benefit | Guarded paid runner + two-reader rubric; feature default-off | Paid/human pending |
| Source CI passes | Existing ordinary CI/PR E2E mandatory; no jobs/policy changes | CI pending publication |
| Safe fallback in an eligible evaluation case | Scorer keeps fallback/timeout/skipped/missing eligible cases in denominator | Local scoring; actual evaluation pending |
| Insufficient or postselected comparison cases | Fixed32/16manifest,12eligible mixed holdout/4ineligible, scorer protocol validation | Local protocol; real reader results pending |

This mapping does not convert compiled CI tests or fixed fixtures into provider, payment, model or deployment proof.
