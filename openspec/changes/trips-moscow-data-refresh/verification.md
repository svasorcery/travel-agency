# Verification

## Scope and source

Base `6f50ce5796f2ed3d559ce69b8ec5adc579d732a1`; branch `codex/trips-moscow-package-refresh`. The user explicitly selected mechanism/checks now and actual v2 only after a new snapshot. Source/tests/PR and green CI are authorized, without merge or real-user enablement. The builder check reported no material unresolved questions.

The canonical v1 package, runtime descriptor/composition/public URLs and expiry remain unchanged. A synthetic next-release identity exists only in disposable tests. No schema migration, new dependencies, target deployment, retained-demo mutation or Q-corpus tuning is part of this change.

## Source observation

BBBike HEAD on `2026-10-24T20:08:26Z` reported `Last-Modified: Sun, 18 Oct 2026 03:55:13 GMT` and 162060454 bytes. The public CHECKSUM still reported MD5 `7ab8c932ede8c847b326d292a1fba61c` for Moscow.osm.gz. A subsequent bounded GET completed at `2026-10-24T20:51:13.517Z`; its actual header/length/SHA256 were checked together. SHA256 `f03e23186dbcfb6e1add78e75425cd8654dc36a79fa08c546da7abf448e00c97` exactly matched the accepted v1 input. The raw source and acquisition receipt stay ignored on D; source URL: https://download.bbbike.org/osm/bbbike/Moscow/Moscow.osm.gz.

The real `prepare --version 2` call at `2026-10-24T20:54:19.795Z` returned `no-new-publication`, `candidatePrepared=false`, `currentStatus=valid`. It created no candidate/output directory. Original v1 release validity remains `2026-11-17T03:55:13Z`; the observed unchanged source is not a fresh-data release.

## Local checks

- `package-check`: initial valid-package test failed (exit 1 instead of 0) before implementation. The initial new+existing local tool suite then passed 17 tests. The final combined tool/Host run passed 21/21 (7 new package-tool cases, 11 existing local-tool cases and 3 Host cases), 50 seconds.
- Node maintenance tests passed 14/14. Red-before corrections include response cancellation, pre-build clock classification, source-time binding, helper identity changing during reproduction, observation time after HEAD and expiry during preparation. The final guard checks the strict reader's validity limit against actual completion time; the exact expiry boundary leaves a failed receipt. These are tool regressions, not extra real-world route evaluations.
- Actual Host/PostgreSQL: the new package lifecycle test and two existing Trips Host tests passed (3 tests, 51 seconds). The test uses real persisted protection, a synthetic next source identity and fictional auth/transports, then proves unchanged full saved JSON/row across source replacement, expiry, corruption and Host restart. A normal explicit manual edit succeeds afterward. Protection readiness uses real time while the test changes application/source time, so package expiry is isolated from key expiry.
- Architecture: 208/208 passed, 55 seconds. The first `--no-restore` attempt failed with NETSDK1004 because this worktree lacked its assets file; the normal restore/test command succeeded. No source workaround or test exclusion was used.
- AI harness: 134 passed; inventory: 64 passed and one platform skip. Strict OpenSpec validation, explicit five-file CSharpier check, Biome and `git diff --check` passed. The new Node suite runs in ordinary lint CI without live source requests; the Host regression is discovered by its existing Integration lane.

## Actual v1 reproduction

Run output `TestResults/moscow-refresh-v1-proof` started `2026-10-24T20:54:20.949Z` and finished `2026-10-24T20:55:03.588Z`. It reused the accepted original metadata/as-of after the separately recorded GET verified identical raw bytes. Both runs reproduced all six reviewed files exactly and strict package validation succeeded at the original build instant; this does not alter the new acquisition receipt or claim a new publication.

Manifest SHA256: `8f364b38540a3d8dfd21272021c50f3f18ca6c01d2863617368c12d2e6f60e43`. Helper SHA256: `b9f5ad331d54772c2eacc4f21311bdd1de0f117434368e59c15ebf1f76702ba8`. Both builds retained 20 POIs, 90 paths, 597838 raw coordinate records, 71507 graph nodes and 23566 ways. Build durations were 14020/14346 ms; peak working sets were 222711808/219979776 bytes. These are actual sampled-process measurements, not a hard OS containment claim or measured maintenance labour cost.

The first reproduction above predates added helper-in-flight stability and HEAD completion-clock guards; its receipt remains historical rather than being relabelled as the final run. An intermediate verification at `2026-10-24T21:06:18.549Z`–`21:06:57.045Z` also passed, with build durations 14180/14178 ms and peak working sets 212639744/209391616 bytes.

The final code, including completion-time expiry refusal, reproduced v1 at `2026-10-24T21:13:42.027Z`–`21:14:17.589Z` in `TestResults/moscow-refresh-v1-proof-release-guard`. Both builds again matched every reviewed file and the manifest/helper hashes above; durations were 13983/14095 ms and peaks 212942848/208322560 bytes. Strict validation reported 20 POIs, 90 edges and the unchanged `2026-11-17T03:55:13Z` expiry. This was an offline reuse of the already verified exact source, not another acquisition or a new data release.

## Review and delivery

[Independent review and scoped recheck](review.md) closed the completion-time finding and reported no open Critical/Important/Minor findings. Runtime package/pin/composition, public assets and existing builder remain byte-unchanged against base. V2 is deferred pending a genuinely new snapshot and separately accepted data release.

[PR73](https://github.com/svasorcery/travel-agency/pull/73) targets dev and remains open without auto-merge. Implementation head `fd2f38acc9bb3d86f6c6ea970d2005bd19f3fd40` passed [CI40133418353](https://github.com/svasorcery/travel-agency/actions/runs/40133418353), attempt 1, completed `2026-10-24T21:29:28Z`: all 20 ordinary jobs succeeded, including the general PR E2E. Paid AI evaluation was the single expected skip. The Host Integration lane passed 121/121 tests without skips.

The Trips full-stack artifact `12576047064` (`trips-full-stack-evidence`, archive digest `sha256:9cc3832724c80e98e254f79824a5f8c187cfd1ad641241b9549852d85627a8ef`) records stack `4894228d91387346bb9a22e651493a00`. All 13 browser stages, startup and cleanup passed. Host/Keycloak/PostgreSQL all exited and restarted with storage preserved; the fresh login compared the complete plan/protected row/key fingerprint. There were three real PKCE logins and zero external browser requests. The artifact's clean checkout is GitHub's PR merge ref `ef7d1956a1b6ed813da116e8ee750c86da756522`, whose verified parents are the base and implementation head above; this is not a merge into dev.

The receipt/summary update must retain green ordinary checks on its final head as shown in [PR73 checks](https://github.com/svasorcery/travel-agency/pull/73/checks). Its exact final head/run is recorded in the PR description after completion, avoiding a self-referential receipt-commit loop. Merge, post-merge CI and real-user enablement remain outside authorization. Actual v2 is still deferred; these receipts establish mechanism/current-v1 compatibility only.
