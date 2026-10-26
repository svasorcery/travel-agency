# Independent planning review

Date: 2026-10-26. Reviewer: separate read-only agent `spb_plan_review` (`gpt-6.1-sol`, high reasoning), fresh context with the user requirements and worktree path. This is a plan review, not implementation acceptance or human itinerary evaluation.

## Scope and result

Reviewed proposal, design, source dossier, all five delta specifications and tasks against current Trips code, root/module instructions and main specifications.

Final result: no open actionable P1/P2/P3 findings; no unresolved product decision identified.

Verified reasoning:
- Legacy no-header v1 remains Moscow-only; explicit v2 has separate transport/encoder/strict decoder branches. Candidate destination comes from the authoritative owner plan.
- Common builder/loader preserves six Moscow files; SPb exclusions and its seventh offered file have distinct identity.
- Core profile validation is an explicit adaptation task, including destination/profile tests, while the shared policy and minute model remain.
- Initial SPb preparation does not compare against Moscow; repeat bootstrap is refused. Preparation does not activate pins/assets.
- MODIFIED requirement scenarios, catalogue-v1 snapshots, owner privacy, explicit transfer/Save/CAS and source-independent historical reads remain.
- Separate city clocks preserve the old Moscow instant. Both saved plans, real service restart/PKCE, protected rows/keys and required CI aggregation are covered.

## Clarification incorporated before final review

The author's documentary source check found `ItineraryModelSource.SupportedProfileVersion="profile-v1"`. Design section 2 and task 2.3 now explicitly require the bounded Core allowlist/destination validation change and tests. The reviewer reread that amendment and confirmed the barrier is addressed. This was a planning clarification; no source code was changed.

Reviewed design SHA256: `b2aa4340d0032619f8b657be6519d79e2c91104b2442ab687cc4bd9b85c52dc4`.
Reviewed tasks SHA256: `c3564a6ae1a04843edf83ec36605c60b4dd2307c2ab7e027a556380ba9c69fa8`.

## Remaining measured gates

Exact admissible entities/anchors within 50m, completeness of excluded-way evidence, 60–100 real directed edges, resource measurements, offered hashes and release-input sufficiency remain unmeasured. They are explicit data gates, not claimed coverage. Failure requires a visible bounded revision before continuing.

The reviewer changed no files and ran no implementation, Host, database or package build. Human A/B, field calibration, target provisioning and rollout remain separate.
