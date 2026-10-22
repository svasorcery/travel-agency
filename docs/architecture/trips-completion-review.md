# Trips completion review

Updated: 2026-10-22. Trips M2 is accepted for its bounded source/demo scope and ordinary PR/post-merge CI. It remains default-off. This review separates that result from incomplete target, human-quality and rollout work.

M1 provides the private structured brief and editable calendar, protected EF snapshots and owner/revision-checked operations. Its existing [canonical change](../../openspec/changes/trips-m1-structured-planning/verification.md) remains active and is not archived by M2 finalization.

M2 adds a read-only Moscow outdoor candidate for 1–3 days over a clean saved empty M1 plan. A pinned offline package contains 20 POIs and 90 directed modelled walking connections. Approximate walking times are uncalibrated; access, opening hours and costs retain their unknowns. The owner explicitly transfers a preview, edits and separately saves through ordinary M1 CAS. No runtime routing service, model, provider call or new persistence/schema is introduced.

Trips also reuses public Rail search state and strict contracts. One explicitly fictional journey can become a bounded historical M1 intention in a selected existing day, with null time and estimate. Real Rail remains transient/view-only; a real-source flag does not authorize durable projection. Existing standalone Rail M2 behavior is retained.

Current capability contracts are [trip-itinerary-candidates](../../openspec/specs/trip-itinerary-candidates/spec.md) and [trip-rail-intentions](../../openspec/specs/trip-rail-intentions/spec.md). The [archived change](../../openspec/changes/archive/2026-10-22-trips-m2-moscow-itinerary/archive-verification.md) retains the plan, implementation/audit history, frozen evaluation evidence and exact CI identities. It records 26/27 completed tasks, with target/enablement task 6.3 explicitly incomplete.

[PR #65](https://github.com/svasorcery/travel-agency/pull/65) merged into dev as `62713d11aece29c86a2fc5c96b17f2010721d73d`. [Final PR CI39760749900](https://github.com/svasorcery/travel-agency/actions/runs/39760749900), attempt 1, passed all 19 ordinary jobs. [Post-merge CI39762931740](https://github.com/svasorcery/travel-agency/actions/runs/39762931740), attempt 1, passed all 18 applicable jobs. Paid AI evaluation was conditionally skipped in both; PR-only E2E was correctly skipped on push. Earlier failed runs are preserved, not relabelled.

The independent full-source audit and scoped fixes/rechecks have no open findings. Mechanical Q_demo accepted 24 case records with 12/12 filled Q targets; human reviewers/scores and field calibration remain not evaluated. Ordinary CI success does not establish real itinerary suitability or target readiness.

Before any real-user enablement, task 6.3 requires separate authorization and provisioned Host/Keycloak/protection/schema/ingress acceptance, current package validation and rollback checks. Human field/Q_live and real Rail rights/banner/quota are separate conditions. No such environment or feature activation was performed by sync/archive.

Geographic expansion is a separate bounded change after a manual Moscow demo and data-maintenance check. Start with Saint Petersburg or Kaliningrad, reuse the generator and add a city-specific immutable package, availability/API selection and independent quality evidence while preserving old saved plans. M3 free-text extraction or an AI framework is not a prerequisite; see the [retained design](../../openspec/changes/archive/2026-10-22-trips-m2-moscow-itinerary/design.md#8-расширение-географии-после-этого-m2--направление-не-текущие-задачи).
