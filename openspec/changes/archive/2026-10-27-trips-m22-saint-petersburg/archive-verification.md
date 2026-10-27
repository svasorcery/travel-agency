# Trips M2.2 archive receipt

Date: 2026-10-27 (Europe/Moscow). Change: `trips-m22-saint-petersburg`.
Schema: `spec-driven`. All27 tasks, including publication task8.1, are complete;
proposal, design, specs and tasks are done.

## Authority and implementation acceptance

The user separately authorized source work, publication, merge of PR79 and
post-merge CI verification, then explicitly requested this documentary closure,
spec sync and archive. Documentation branch `codex/trips-m22-documentation` starts
at actual merge `0930dd5cc2cdbd635c38255ec53a9c97fd967d7b`. This record does not
claim CI on the later documentation working tree.

On 2026-10-27 the user separately authorized documentary self-review, commit,
push, a separate PR into `dev` and ordinary CI monitoring/repair. Merge of this
documentation PR is explicitly excluded. Its published head/run/result will be
retained in that PR's metadata, separately from implementation acceptance below.

[Publication](publication.md) retains the requested base, PR head, actual merge
parents/tree, PR validation checkout and final successful run identities. PR79
head `718c5beea85db873896e1917579a7714c8f6e2d9` passed PR CI40380820167 (20/20
applicable jobs); actual merge `0930dd5cc2cdbd635c38255ec53a9c97fd967d7b` passed
push CI40392188955 (19/19). Both were attempt1, with expected paid-AI skips and
the general PR-only E2E expected-skipped only on push. The initial failed CI run
and its public-fixture-import repair remain recorded.

Both final runs passed the required two-city aggregate: Moscow13/SPb14 stages,
one original candidate POST, three PKCE logins and zero external requests per
profile, complete service restart, saved-state/protection preservation, owner/CAS
refusals and cleanup. Fourteen original CI JSON files and both artifact digests
are retained in [the CI index](receipts/ci/index.json). Earlier local failures,
interrupted attempt and source-fingerprint correction remain unchanged in
[the original index](receipts/index.json).

## Synchronized contracts

Nine requirements were modified and nine added across these five main specs:

- [trip-data-package-maintenance](../../../specs/trip-data-package-maintenance/spec.md): city-scoped inspection/preparation, historical compatibility, explicit first release and existing-release preservation.
- [trip-itinerary-candidates](../../../specs/trip-itinerary-candidates/spec.md): explicit v2, selected-city availability and strict identity/adoption, bounded SPb data and independent acceptance evidence.
- [trip-plan-workspace](../../../specs/trip-plan-workspace/spec.md): existing catalogue/protected snapshot compatibility.
- [trip-rail-intentions](../../../specs/trip-rail-intentions/spec.md): Rail scope remains independent of walking protocol/city coverage.
- [trips-local-verification](../../../specs/trips-local-verification/spec.md): separate city clocks, actual shared generator and both required real-service profiles.

Established titles/Purpose text and all unmentioned requirements/scenarios are
preserved. Delta operations remain in this archive as historical change evidence;
main specs contain canonical requirements and scenarios, without delta headers.
Frozen run-binding paths identify the pre-archive source layout; their exact
bytes/hashes are preserved, with the referenced corpus now beside that binding
inside this archive. Formatter and Git attributes follow the archive paths.

## Documentary validation

The pinned CLI reported all27 tasks complete before the move and no active changes
after it. Strict validation passed all19 main specs. A separate comparison checked
all18 delta requirement blocks against the synchronized specs, preserved54 other
requirements and every preexisting scenario, and left established Purpose/title
text untouched. All22 preexisting JSON files retain their original SHA256;
all14 new CI JSON files match their artifact bytes. The original receipt index
also verifies unchanged. All116 local Markdown file links resolve.

Only two executable test references changed: the corpus and completion-receipt
paths now point at this archive. Their expectations and the immutable binding
remain unchanged. The existing SPb corpus regression passed (one test evaluating
twelve frozen cases), and controller tests passed23/23 without skips. These are
regressions, not a new untouched quality run or human evaluation.

AI harness validation passed134/134 and canonical instruction checks. Biome CI
passed over492 files with no errors (warnings/informational findings retained),
and changed-file CSharpier plus `git diff --check` passed. Runtime sources,
dependency locks, data packages and user demo resources were not changed. Earlier
immutable implementation CI is recorded separately from this local documentary
validation; no new publication or CI result is implied for this working tree.

## Pre-publication review

Fresh self-review and an independent read-only documentary review found no
actionable P1/P2/P3 issue. The reviewer independently verified merge ancestry/tree,
the two ZIP digests and all fourteen JSON entries against original ZIP bytes,
all22 unchanged original JSON files, all18 synchronized delta requirements,
54 untouched requirements and176 preexisting scenario headings,27 checked tasks,
116 local links and byte-preserving Git attributes. Executable changes remain
limited to the two archive-path substitutions.

Before publication, the complete local Trips stack target passed again:
TypeScript,23 controller tests,21 Vitest checks and13 Chromium regressions.
The existing twelve-case SPb corpus regression, strict19-spec validation,
Biome492-file CI and134 harness tests also passed. The independent review used
retained source/API/artifact evidence and did not claim a fresh live runtime,
human-quality or target-readiness audit.

## Remaining boundaries

Archive accepts implementation and ordinary PR/post-merge CI; it does not enable
real users, refresh Moscow v1, create Moscow v2, change the retained user demo or
claim field calibration. Human A/B scores remain blank. Target provisioning and
[original M2 rollout task6.3](../2026-10-22-trips-m2-moscow-itinerary/tasks.md)
remain pending. No provider, model, paid API, Host, database or deployment action
is part of this documentary operation.
