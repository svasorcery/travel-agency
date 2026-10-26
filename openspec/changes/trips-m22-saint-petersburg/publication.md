# Trips M2.2 publication

The user explicitly authorized self-review, commit, push, a PR and ordinary CI
monitoring/repair on 2026-10-26. Merge is explicitly forbidden for this phase.

The branch retains requested base `11ba5a1572e53cd901ae4ec56b77b482ec95f699`;
freshly fetched `origin/dev` still has that SHA. Target is `dev`, branch
`codex/trips-m22-saint-petersburg-d`. The existing D: checkout remains the working
directory; no branch ancestry was rewritten.

Fresh independent runtime review found no actionable issue. Tooling review found
and rechecked the completion-gate P2 described in implementation-review.md. The
real skipped-test regression, all 23 controller tests and full stack test target
passed. No raw archives, private credentials, keys or TestResults are included
in the publication file set.

The PR description and GitHub checks will retain the actual PR URL, published
head and ordinary CI run identity. This document records authorization and
pre-publication evidence; it does not claim a CI result before one is observed.
Task8.1 remains open for the separately authorized future merge/post-merge part.
Human A/B, field calibration, shared target/real-user enablement and Moscow v2
remain separate.

PR79 initial CI run40379952598 on `99fceb234832a64f60af8f83fbec5cc0cd425452`
found an Nx module-boundary lint violation: the v2 fixture re-exported v1's
unchanged error JSON through a relative cross-project import. The repair uses
the existing `@travel/trips-itinerary-fixtures` public export. Uncached focused
Nx verification passed all four tasks: v2 fixture lint and API client build,
lint and tests. The failed run remains failed; acceptance requires the new
published head's complete ordinary CI.
