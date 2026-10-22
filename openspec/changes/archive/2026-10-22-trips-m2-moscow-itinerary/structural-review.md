# Task 5.1 independent spec and quality review

## Spec Compliance

**PASS for task 5.1 structural integration verification.** The report, all 62 proposed scenario rows and all six verification-only CJS scripts meet the scoped prerequisite. This verdict permits the controller to accept 5.1 and proceed to 5.2; it does not accept Q_demo, Q_live, the later whole-source audit, CI, target integration or rollout.

- Reviewed the complete 162105-byte package, SHA256 `71eaa9479b5eaf8daadca06ce8d60b8389d819e03cef8078a14f44da558df5c9`. After the initial oversized line-based output was truncated, read the entire package in contiguous bounded character slices through its end, with no omitted section. This covers the report, every row and complete build-map.cjs, final-audit.cjs, followup-checks.cjs, run-checks.cjs, verify-inventory.cjs and write-report.cjs source. No script was executed by this reviewer.
- Binding: unchanged requested fresh base/HEAD `e654c76915103a82083ef2abc01c0579117c9749`, branch `codex/trips-m2-moscow-itinerary`; source inventory 134 files = 32 tracked changes + 102 new, zero deletions/drift; tree SHA256 `6ce5914770699ef633d41eb6c1ed27bc0942227fb9909ed8a75e68b74f17ddf5`. Controller independently verified the branch and all 243 current manifest entries before dispatch; I used that verification without repeating Git commands. The retained binding agrees (`TestResults/trips-m2/final-structural/final-source-binding.json:2`).
- All 21 requirements / 62 scenario statements were compared with the complete proposed ownership map. Independent read-only comparison of every mapped .NET name against actual passed TRX results and every mapped browser title against its passed/retry-zero ledger found zero mismatches. Every mapped TS semantic title is present in its bound current source; suite logs prove counts, not invented individual reporter outcomes (`build-map.cjs:5`, `build-map.cjs:7`). There are 58 rows with source execution evidence and four expressly pending protocol rows 16/60/61/62 (`task-5.1-scenario-map.md:22`, `:66`, `:67`, `:68`).
- Fresh uncached production build is an actual successful build, not an Nx cache replay: requested production argv, start/end, exit 0, compiled browser/server bundles, three public prerenders and explicit cache-skipped footer are retained (`production-build-start.json:2`, `production-build-result.json:4`, `production-build-raw.log:91`, `:100`, `:106`). One production build is evidenced; no build was rerun during review.
- Independently recomputed all 73 recorded dist file digests: zero mismatches. All six source files match the actual production artifacts, and all five manifest child digests match. Only `/`, `/hotels`, `/hotels/reservations` are prerendered. This proves build-time local artifacts, not deployed HTTP availability (`production-static-assets.json:1`, `production-dist-manifest.json:1`).
- Actual effective Nx project JSON contains the external Data web-build input, Data asset copy and both shared-wire test inputs; the fresh affected raw outputs are Data => [web] and wire => [api-client, travel-e2e, web] (`effective-nx-input-proof.json:2`, `nx-final-affected-data.stdout.log:1`, `nx-final-affected-wire.stdout.log:1`). Superseded loader-era apps/web/project.json is explicitly identified rather than treated as unchanged (`task-5.1-report.md:66`); current effective queries, formatter and fresh production build cover its replacement.
- Raw formatter/validator receipts agree with the report: CSharpier 44 checked/exit 0; Biome 33 unchanged, zero errors, 50 warnings/209 infos, zero omitted diagnostics; all 50 warning keys match accepted 4.x receipts; OpenSpec 15/15 plus M2 issues=[]; complete dependency-free harness 134 pass/zero skips; inventory 62 pass and one documented existing Windows symlink skip. The four old-bin-path Nx setup failures remain visible and are superseded by four successful installed-bin queries (`checks-results.json:1`, `followup-check-results.json:1`, `biome-warning-provenance.json:1`, `dotnet-inventory-tests.stdout.log:38`).
- Actual retained TRX counters/results are Unit 294, loader 98, itinerary API/codec/composition 85, M1 HTTP 30, architecture 208, all passed with zero unexecuted results. Retained helper is 15/15; complete api-client is 356/17 files, web 735/54 files and browser 35 passed. The TS raw suite counts and current semantic source names are correctly distinguished (`task-4.1-fix1-results/api-client-full.log:93`, `task-4.3-fix1-web.log:144`, `task-4.4-full-frozen.log:1`). Reuse is based on accepted current-source closures, not a new runtime execution.
- The four pending rows faithfully retain frozen case/binding, separate Q12/S-edge6/S-negative6 denominators, every-case two-human thresholds and failure/tuning/new-set rules (`quality-protocol.md:15`, `:21`, `:23`, `:27`, `:37`). They are valid pending protocols for this prerequisite; they are not structural test failures or accepted quality results.

## Strengths

- Provenance is attached to exact source bytes, semantic test names and raw outcome receipts. .NET theories expand to executed names and browser evidence expands to concrete ledgers; TS limitations are explicit (`build-map.cjs:2`, `:5`, `:7`, `:10`).
- The report openly distinguishes source evidence, actual local build/static bytes, earlier loopback delivery and unexecuted quality/live gates (`task-5.1-report.md:60`, `:84`, `:86`).
- The new Trips stylesheet warning is measured, identified as new and retained for the controller's final 6.1 fix wave; no budget increase or source workaround was used (`task-5.1-report.md:41`, `style-baseline.json:1`).
- Verification code writes into its assigned ignored evidence surface, records unsuccessful setup diagnostics and command exits, and uses bounded output capture plus same-directory atomic report/manifest replacement. None introduces runtime product behavior.

## Issues

### Critical / P1

None found in this task-scoped verification deliverable.

### Important / P2

None found in this task-scoped verification deliverable.

### Minor / P3

1. **Carry the new Trips style-budget regression into 6.1.** `production-build-raw.log:95` reports 4.83 kB, 830 bytes over the 4 kB warning threshold. The standalone diagnostic measures baseline 3984/current 4829 bytes; the actual error threshold remains 8 kB (`apps/web/project.json:44`). Impact is a new source warning/performance-maintenance signal, not failed production compilation or a demonstrated runtime defect. The report already exposes and routes it. In the final unified fix wave, reduce redundant/duplicated Trips styles if confirmed appropriate; retain the existing thresholds and rerun only validation justified by that eventual source change. This does not block the scoped 5.1 prerequisite.
2. **Make row 37's composed proof easier to audit.** `task-5.1-scenario-map.md:43` / `build-map.cjs:49` say "actual M1 codec port" and generically mention retained M1 HTTP30. The M2 Unit test sets a fake ITripPlanCodec ReservedSize (`ItineraryCandidateServiceTests.cs:210`, `:788`, `:828`); it proves pre-preview orchestration at the port, while the HTTP pair proves candidate response bytes. The independent real-codec proof is the already-passed `TripsSizeHttpTests.Near_limit_utf8_full_detail_is_readable_and_next_larger_candidate_never_commits` in m1-http-regression.trx: it uses TripPlanJsonCodec, reserved complete server fields and a multibyte save/reload/next-larger rejection (`TripsSizeHttpTests.cs:36`, `:59`, `:97`, `:111`). Impact is wording/receipt discoverability, not missing composed coverage. When copying the map, explicitly name/link this retained M1 test and call the Unit dependency an ITripPlanCodec test double. No new test run is needed.

## Named risk checks and limits

- **Tautological scenario counts / fabricated names:** read all map rows and generator source, compared all scenario WHEN/THEN statements, checked actual named .NET/browser outcomes and TS current-source titles independently. Zero name/outcome mismatches; counts alone were not used as behavioral proof. Semantic assertions of already independently accepted per-task suites were not re-reviewed wholesale.
- **Candidate-response cap mistaken for saved-detail cap:** inspected the named Unit reserve test and fake, the response-codec tests, the exact retained M1 size HTTP test and its passed TRX result. The proof is composed as described in P3.2, not a claim that the M2 HTTP response-cap pair executes saved-detail preflight serialization.
- **Best-effort withdrawal / historical text overclaimed:** compared spec scenario at `specs/trip-itinerary-candidates/spec.md:223` with map row 44, then checked the actual browser stage/edit/save/reload assertions (`trips-itinerary.spec.ts:121`, `:139`, `:147`, `:163`, `:170`). Both prerequisite reads and ordinary saved intentions are evidenced; there is explicitly no post-observation trusted-current-source guarantee.
- **Manifest without actual build bytes / private prerender:** checked raw build command/result/log, independently hashed all 73 dist files and compared canonical six files and five child digests; inspected actual prerender metadata. No new browser/server was started.
- **Stale root input wiring:** read fresh effective project/affected JSON, its current source binding and documented superseded-loader exception. Current output is explicit; no remote CI result is inferred.
- **Warnings or platform skip hidden:** read actual build warning/footer, Sass diagnostic, formatter summary/provenance, harness/inventory raw summaries, explicit skipped test and OpenSpec output. Trips CSS is new and retained; Flights/Support are reported baseline warnings; one Windows inventory skip remains one skip.
- **Gate inflation:** compared pending rows with the frozen protocol. No Q generator/output/run-binding, human score, model/provider call, Host/DB, live delivery, PR/postmerge CI or rollout acceptance is asserted by this review. Global packet telemetry was not available or claimed.
- Review actions were read-only evidence/source inspection and hashing; no test suite, build, browser, provider/model/network operation, Git mutation, source/config/canonical-document edit or subagent was run. C free before the scoped atomic review write was 2428239872 bytes, above the brief's 1.5 GiB stop threshold. A guessed TripsDetailBudgetTests.cs path did not exist; the actual named TripsSizeHttpTests.cs was then located from the retained TRX and inspected, without regenerating evidence.

## Assessment

**Task quality: PASS / Approved, with two nonblocking P3 follow-ups.** The structural prerequisite is trustworthy within its stated scope. The controller may accept task 5.1 and start 5.2 while preserving the four pending quality protocols and the separately tracked 6.1 style/final-source review gate.

Reviewed report SHA256: `c849d9ed4aad258794d0b35b6c03b410af05b517e1e21c6680f59b18cfe7c29b`.
Reviewed map SHA256: `c72236990e03c520e048b3f1f7b817a5500715ccd9652513383008f5d578d4c4`.
Reviewed hash manifest SHA256 (controller verified): `4da90a3f8c931fe8134234b0d53dbe8e662bc814b96dd71454083af2966cdfef`.
