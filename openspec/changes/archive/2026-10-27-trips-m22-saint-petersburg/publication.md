# Trips M2.2 publication

The user authorized self-review, commit, push, PR and ordinary CI monitoring/repair
on 2026-10-26, initially without merge. A later explicit instruction authorized
merging PR79 and checking post-merge CI. On 2026-10-27 the user authorized recording
these receipts, closing task8.1, syncing the five specs and archiving this change.

Implementation branch `codex/trips-m22-saint-petersburg-d` retained requested base
`11ba5a1572e53cd901ae4ec56b77b482ec95f699` and targeted `dev`. PR79 merged at
2026-10-26T20:03:05Z as `0930dd5cc2cdbd635c38255ec53a9c97fd967d7b`, whose parents
are that base and final head `718c5beea85db873896e1917579a7714c8f6e2d9`.
Both head and merge have tree `9eed462eaed43ea44c6bbc43fa1dfa300248ecad`.
Documentation branch `codex/trips-m22-documentation` starts at the actual merge;
no branch history was rewritten.

Fresh independent runtime review found no actionable issue. Tooling review found
and rechecked the completion-gate P2 described in implementation-review.md. The
real skipped-test regression, all 23 controller tests and full stack test target
passed. No raw archives, private credentials, keys or TestResults are included
in the publication file set.

## Final PR and post-merge acceptance

[PR79](https://github.com/svasorcery/travel-agency/pull/79) is merged into `dev`.
Both final runs completed successfully on attempt1:

| Phase | Run | Published head | Actual checkout | Applicable jobs |
| --- | --- | --- | --- | ---: |
| PR | [40380820167](https://github.com/svasorcery/travel-agency/actions/runs/40380820167) | `718c5beea85db873896e1917579a7714c8f6e2d9` | `2818f289271b1de8ffb5b50f1a72a5d0bc160501` | 20/20 |
| Push to dev | [40392188955](https://github.com/svasorcery/travel-agency/actions/runs/40392188955) | `0930dd5cc2cdbd635c38255ec53a9c97fd967d7b` | `0930dd5cc2cdbd635c38255ec53a9c97fd967d7b` | 19/19 |

The PR checkout is GitHub's validation merge, not the later actual merge.
Paid `test-ai-evals` was expected-skipped in both runs; general `test-e2e` is
PR-only and was expected-skipped on push. It passed 2/2 in PR CI. Neither accepted
run needed a rerun. The dedicated Trips two-city full-stack job ran in both.

| Phase | Both-city aggregate | Moscow stack | Saint Petersburg stack |
| --- | --- | --- | --- |
| PR | `b3fa9399ce5a8778b008a1912e1efd58` | `33141e20c691d652b84ef606f97ffb36` | `59b5a180fee2a3a7a259c12fe0b1d840` |
| Post-merge | `ae953c8b4fadb809a78a389b75e7febd` | `bb816f53a8a450b3f1559decf93b163a` | `d743764f8f28ec5020806a8f7d2832a2` |

Both aggregates passed. Moscow passed all13 browser stages and Saint Petersburg
all14, including historical Moscow saving. Each profile made one original
candidate POST, three real PKCE logins and zero external requests, then proved
complete Host/Keycloak/PostgreSQL restart, protected row/key preservation,
foreign-owner and stale-revision refusals, and owned cleanup. Source receipts
identify clean checkouts and the empty raw delta
`e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`.
Application/browser instants remain Moscow `2026-10-24T12:00:00Z` and
Saint Petersburg `2026-10-27T12:00:00Z`; identity validation used real time.

## Retained evidence

[CI index](receipts/ci/index.json) records all job outcomes, source identities,
artifact IDs and SHA256 for fourteen original JSON files copied byte-for-byte.
The seven files per run include both controller, browser and restart receipts
and the aggregate. They remain available in Git after GitHub retention expires.
ZIPs/screenshots remain outside Git; both downloaded ZIP digests were verified:

- [PR artifact12679281774](https://github.com/svasorcery/travel-agency/actions/runs/40380820167/artifacts/12679281774): `6f88141d4a1e79fd416c508c7461976a658d014f7be02fa4efc0dd0b75e51d04`.
- [Post-merge artifact12685496377](https://github.com/svasorcery/travel-agency/actions/runs/40392188955/artifacts/12685496377): `06a0630f8fa59891b7b5e412f146d2372e1c7cd61aebbed8fcb3f739202830c2`.

Both GitHub artifacts expire on 2026-11-02. Earlier local receipts and the separate
UTF-8 fingerprint correction remain unchanged; immutable CI closes the later
capture/completion-gate source boundary rather than retroactively changing them.

PR79 initial CI run40379952598 on `99fceb234832a64f60af8f83fbec5cc0cd425452`
found an Nx module-boundary lint violation: the v2 fixture re-exported v1's
unchanged error JSON through a relative cross-project import. The repair uses
the existing `@travel/trips-itinerary-fixtures` public export. Uncached focused
Nx verification passed all four tasks: v2 fixture lint and API client build,
lint and tests. The initial failed run remains failed; the replacement final
run above establishes acceptance of the repaired head.

Task8.1 is complete. Human A/B scores remain blank; field calibration, target
provisioning, real-user enablement, original M2 rollout task6.3 and Moscow v2
remain separate. This archive does not enable a target or claim fresh map data.
