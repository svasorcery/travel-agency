# L fixture/queue source review

2026-11-01. One fresh read-only review:0Critical,2Important,0Minor. Both Important findings fixed in one RED/GREEN pass; no re-review. Source is ready for finalfreeze and the authorized runtime, not yet runtime-accepted.

1. Rejected queue evidence was thrown away before loadFrames.Add. Actual evaluator RED demonstrated a lost persisted artifact. The frame is now added with raw due/pending counts and bounded queue metadata before rejecting unknown/overflow/count mismatch. Source-proof writes/reads a temporary source-frames artifact for all3negative cases and confirms identity/count preservation.
2. Non-null identity allowed Guid.Empty and empty/whitespace booking strings. Shared HotelM2QueueRow.Capture now checks nonempty Guid and non-whitespace string; sampler uses this conversion.3RED regressions pass after rebuilding the separate Hotels test assembly.

Final checks:397/397standalone,96/96actualevaluator cases,3/3rejected-artifact cases,wrapper0errors. The first no-build post-fix test invocation used the separate project's previous DLL and stayed red; its log is retained, followed by an explicit rebuild and green fullsuite. No accepted result uses the stale invocation.

Review examined immutable payload aliasing, original raw-key lookup, default fixture/restart paths, real Envelope types/JSON, owned terminal classification, unknown/incomplete failures, raw diagnostic preservation and version1/2 behavior.

Declined scopes and main decisions:
- Runtime RSS/S/L: separately measured after sourcefreeze, not inferred from tests.
- Final hash/archive and complete historical verification: main performs before runtime.
- Full inheritedH5/H6/H7 re-audit and real suppliers/CI/provisioning/rollout: outside this slice; touched boundaries reviewed, prior evidence retained.

No deferred minor findings. No product scheduler modification, historical waiver, publication or deployment.
