# Execution receipts

Started 2026-10-26 after the user's instruction to self-review and implement. Base remains `11ba5a1572e53cd901ae4ec56b77b482ec95f699`; no commit/push, target enablement or existing-demo changes.

## Active checkout after disk failure

The C: volume filled during recovery of the historical Moscow raw extract. The partial 79036416-byte file was removed and only this task's newly installed root node_modules was removed after checking exact paths/no reparse points. All 76 source-file hashes remained unchanged. No other cache, demo or archive was removed.

Active root is now `D:/_Projects/_github/travel-agency/.worktrees/trips-m22-saint-petersburg`, branch `codex/trips-m22-saint-petersburg-d`, from the exact original base. The native worktree creator has no disk-selection parameter, so an ordinary Git worktree was created in the already ignored .worktrees directory. App attachment reports that it is not a managed worktree; use this explicit D: root for all subsequent commands/reviews. The original C: worktree/branch remain as a checkpoint.

Before further edits, all 76 source files, the complete binary tracked diff, raw SPb hash and d/e candidate identities were verified identical on D:. Receipt: `TestResults/trips-m22/worktree-transfer.json`; independent safety copy: `D:/_Projects/_github/travel-agency/TestResults/trips-m22-recovery-20261026`. No branch was rewritten and no HEAD was substituted for the requested base. Dependencies were reinstalled offline on D:; both root and pinned OpenSpec lockfiles remain unchanged.

Subsequent receipts before this move: 298/298 Trips Unit; 118/118 reader+composition; 93/93 retained/v2 HTTP+codec; 16/16 v2 HTTP including fictional candidate responses; 111/111 v1/v2 decoder+frozen-corpus; 37/37 Angular v1/v2 transport. Real SPb policy/Q execution still has not run.

The first Angular state run found the new same-plan saved-destination A→B→A bug and v1-era fixture assumptions; 40/78 failed including cascading TestBed failures after an unexpected pending request. The destination epoch and scoped fixtures were corrected on D:; the subsequent run passed78/78. The original failed run remains a failure. Remaining backend/maintenance/full-stack tasks are not claimed complete.

## Historical early milestones (superseded by later receipts)

- Task 1.2 complete: `quality-cases.json` freezes 12 real-package and 12 separate structural/negative cases; SHA256 `fc12bc22c49ebfa7498cd4f4a865be297c22aef73081aecd11eef1846fb9409b`. No real policy evaluation or human scoring has run. Bind the producer/API/decoder closure before the first real evaluation; run Q before exposing real E2E candidates if needed to preserve that boundary.
- Baseline: Trips Unit 294/294; selected existing codec/protection/response codec 21/21. Full legacy fixture/TS preservation task 1.1 still needs its remaining evidence.
- Common offline helper supports explicit city and bounded grouped exclusions. RED observed for new CLI arguments and then grouped profile shape; original+new helper suite passed 19/19 before the grouping change, focused grouping/Moscow tests passed 4/4 afterwards. Final expanded helper suite/formatting/docs remain.
- Core profile factory/destination tests observed RED, then full Trips Unit passed 296/296. Shared policy/minute model are unchanged.
- Generalized single reader/release descriptor named `CuratedItineraryFacts/CuratedItineraryRelease`. RED was the prior Moscow-only descriptor; current real SPb and retained Moscow strict-reader suites pass 110/110. PackageVerification, destination selection and API/client integration are still pending.
- Pinned npm dependencies installed from local cache with ignore-scripts/no-audit/no-fund; lockfiles unchanged. No paid API/model executed.

## Data acquisition and preparation

One SPb OSM GET: 101559015 bytes, SHA256 `5cb4baf36d15f9e41ddf1356383e18c2598191b35c80deee6ec307d0765069e4`, publication `2026-10-25T04:08:52Z`, download completion `2026-10-26T10:24:19.735Z`. Raw/source metadata are retained in ignored `TestResults/trips-m22/source`.

Exact Wikidata revisions/coordinate claims and original retrieval dates are retained in `entities.json`, `entities-bridges.json`, `entities-replacement.json`. Combined inspection does not renew old retrieval dates. No coordinates or 50m radius were changed.

Data iterations before any real itinerary/quality output:
1. Palace Square and Mikhailovsky Castle lacked eligible outdoor anchors within 50m (65.9m/62.2m); no radius relaxation. Isaac Garden lacked the exact required entity; Catherine Garden is a distinct researched replacement within the same district.
2. The first 18-POI build failed: 52 selected directed paths, below 60. This failure is retained in build-a.log.
3. Replaced the two removed points with independently identified Pevchesky/Green bridges, within the declared district. b/c built 20/62 with equal bytes, but independent audit rejected their Anichkov Palace garden path (P1). These candidates are not accepted.
4. Excluded the entire Anichkov garden and replaced that POI with Malaya Sadovaya. d/e built 20/62 with all seven files identical. The independent scoped recheck closed P1 and found no further actionable data findings; see [data audit](data-audit.md).

Current source bundle is copied from d into `Infrastructure/Itineraries/Data/saint-petersburg-outdoor/1` for strict-reader source tests. It is selected by the new explicit default-off/Test-only city composition in source. Actual package-check passed (8/8 tool tests); it is not deployed or enabled. Remaining full-stack/source acceptance is pending.

d/e build facts: 104395 raw coordinate records, 10846 retained graph nodes, 3413 retained ways; build times 10636/9130ms, peak working sets 79773696/80633856 bytes. Profile: 204 groups/851 unique excluded ways, 27794 offered bytes. Grouping avoids repeating area URLs and preserves the 64KiB/4096-ID bounds.

## Rulings and honest failures

- Resolve data feasibility before broad UI/API integration; allowed inventory replacements remain inside the approved district, with independent audit and unchanged frozen Q inputs. No Q-driven tuning has occurred.
- Existing `profile-v1` remains city-neutral for old fictional zone-policy fixtures. The new SPb profile is restricted to Saint Petersburg/Europe-Moscow/catalogue1 in Core; exact real-city source/release tuples are enforced by Infrastructure and later API/TS. This preserves prior generic Core behavior instead of breaking Kaliningrad test fixtures.
- Earlier scratch connectivity output `anchor-connectivity.json` is invalid: the inspector reused a mutable Shortest target set. The corrected inspector passes a new set per source like the actual builder; only `anchor-connectivity-fresh-targets.json` is evidence. The actual 52-path failed build did not use the flawed inspector.
- Scratch inspector AOT and Python console-encoding failures were corrected in the diagnostic scripts, without relaxing product/data rules. Their original logs remain. A quiet file-app build produced its DLL but returned nonzero without diagnostics; it is not counted as a successful build receipt. Actual `dotnet run`/tests and both real data builds have explicit successful receipts.
- No historical failed run is relabelled. Human A/B, field calibration and target/rollout task6.3 remain pending.


## D: continuation and first registered Q_demo

- Full common builder suite21/21, including grouped profile caps/duplicates/permutation and all retained Moscow synthetic resource/access/direction tests.
- Moscow real retained raw recovered from the named evidence archive only; two builds reproduce all six v1 files byte-for-byte. No expiry/pin/public path changed. Logs moscow-parity-a/b; 14340/14666ms; peak208986112/212082688 bytes.
- Maintenance18/18 including first-city lifecycle, city source selection and every retained acquisition guard. Windows temp path was canonicalized in the test harness because tmpdir used an8.3 alias; product path guards were not relaxed.
- Actual package-check tool8/8. Renamed Host test interface required the missing Core import; the first failed compile is not acceptance.
- Angular state78/78; initial component integration93 tests had35 failures after an old SPb-unsupported fixture and cascading TestBed cleanup. Fixture now uses unsupported four-day Moscow, and cleanup resets TestBed in finally; the next run40/41 passed, exposing the intentional no-source-call rule for unsaved foundations. That assertion was updated, full Trips rerun is in progress. Rail component52/52 was already green in the first run.
- Independent pre-Q review foundP2: claimed coverage was not independently recomputed. Before any real generation, the test gained selected-source-tags covered/unmet checks, exact tags/dwell and expansion caps. Scoped recheck closedP2 with no further findings.
- First real Q registered2026-10-26T14:06:30.994Z, base11ba5a1572e53cd901ae4ec56b77b482ec95f699, 137 source/input paths in quality-run-binding.json (SHA2565a66ebb8a6ff192bb5ff48b3b90784d052f30c36ef8812ac5000f9fd1905017c). One original run:12/12 real Q cases passed, no discarded cases or tuning/rerun. Application instant2026-10-27T12:00:00Z.
- Raw HTTP responses captured before assertions from original TRX and retained in shared/ts/api-client/tests/fixtures/trips-m22-spb-q-demo-v1. Responses SHA2561d1750c5f0e22dd28d2d426ea16be6e396b0f749037baf8879bae78ed014dc29. All13 raw-decoder/corpus checks passed. Q09 filled12 stops; foodQ10/Q11 retain partial/unmet food. These are mechanical results; human scores null. Structural12-case suite remains separate and pending its aggregate receipt.
- Both named E2E profiles/controller checks16/16; C# and actual browser/full-stack work are still in progress. Existing interactive demo has not been touched.

## Compatibility, structural checks and independent review

- Base-codec writer compiled exclusively from8blobs of11ba5a... with cached ErrorOr2.0.1/net10; no current ProjectReference or network feed. Two canonical old plaintext outputs and provenance retained under tests/fixtures/trips-m1-legacy-codec. They were generated after implementation began from the preserved base, not falsely dated before edits. M1 source closure remains unchanged. Both .NET/protection and TS suites passed2/2 each. Current test-key ciphertext is not represented as historical ciphertext.
- Actual Host lifecycle4/4 passed, including seeded exact old Moscow/SPb plaintext, current test protection, unchanged full detail/rows/keys across SPb expiry, corruption, withdrawal, source replacement and Host factory restart, foreign-owner/stale rejection and explicit manual edit. This fixture restarts Host only; full Host/Keycloak/PostgreSQL restart remains the separate E2E gate.
- Structural first execution9/11 passed. S05 incorrectly instantiated map/model expiry instead of identity-fact expiry and returned an honest disconnected candidate; S06 attempted an invalid shorter walking lease rejected by Core. Corrected synthetic inputs keep all frozen outcomes: S05 identity/release expiry before local day end; S06 two unconnected eligible POI with others explicitly closed. Corrected11/11passed; independent scoped review accepted those fixture corrections. N05 remains the independently tested actual Angular saved-destinationA→B→A case; total structural denominator12. Real12-case Q was not tuned/rerun.
- Final runtime independent review foundP2: inner preview download list stayed Moscow-only. RED1/43 proved it. Fixed to derive the full list from current decoded preview and exact trusted offer, including SPb preparation-profile. GREEN104/104 all3page/component(M1/Rail) suites; same-auditor recheck closedP2, no new actionable findings.
- Independent maintenance/E2E/privacy review: no actionable findings.
- First real both-city command: Moscow state64d0905e039cbf195382fb39f314781f started successfully, verificationfailed, cleanuppassed; SPb unrun. Browser import failed before its receipt because importing decoder through the Angular barrel demanded JIT compilation. Resource-free --list proved the cause. New pure decoder alias follows existing trips-decoder pattern; --list now discovers1test, and controller performs it before build/provision. Independent recheck found no issue. Originalfailed aggregate/receipts retained; second run in progress.
- Dependency-free AI harness134/134 plus validation passed. Final Trips Unit298/298 passed.

## Final source/regression receipts (full-stack still pending)

- Trips Unit298/298; entire Trips Integration271/271 (includes original Moscow corpus regression and the already-opened SPb corpus regression, not a new holdout); Architecture208/208. Actual Host lifecycle4/4. Actual Host OpenAPI filter2/2: new Trips input-contract test plus existing Rail contract test; selected real Trips operations retained in actual-openapi.json. OpenAPI exposes optional header/query for legacy compatibility; v2 conditional requirements and error outcomes remain enforced by HTTP guards/tests and the documented contract.
- Full mocked Trips browser matrix81/81 passed, including public file downloads/parity for both cities, UI adoption/M1/privacy and Rail consumer. StandaloneRail15/15 passed. First81-case attempt failed because the old fixture rejected the new destination query; second73/81 exposed eight stale/inconsistent fixture expectations (unsaved availability call removed, unknown tuple unavailable, new fictional wire identity, Kaliningrad unsupported, recalculated totals). All retained; final full81pass is a distinct run.
- Runtime preview P2 fix plus ordinaryM1/Rail components104/104. Full Trips Angular run had1failure in an untouched old API mock; replacing availability with availabilityFor corrected that9-test component, included in104pass.
- CSharpier38 changed C# files clean. Biome repository459files clean after preserving immutable JSON corpus/goldens and formatting documentary evidence. No unsafe bulk fixes were applied. Standard AI harness134/134 and strict OpenSpec valid.
- Second full-stack state2c34e186e566e56f7da146ef591b23ee: discoverypassed, startupreadinessfailed, cleanuppassed; no browser execution. Last Host probe wasready with Trips/PostgreSQL/Redis/NATS healthy. The old report omitted separate discovery/web statuses, so no exact cause is claimed. New bounded loopback diagnostics report only statuses/known enums, dispose response bodies, forbid redirects and retain all originaltimeouts. Controller17/17passed. Third run is serialized after all other browser/Docker checks.

## Two-profile run in progress

Moscow profile in the third aggregate passed discovery/start/browser/cleanup, state `cc2c2db3cecb5e854a9b9e7eaf7fe38c`. Source base `11ba5a1572e53cd901ae4ec56b77b482ec95f699`, dirty-delta SHA256 `244d1a75d58c8e87d41f8259e9729d8a2d11a70c86140258e44780e73c915d31`. Browser ran 2026-10-26T15:13:42.297Z–15:15:33.286Z, application clock remains 2026-10-24T12:00:00Z. All 13 stages passed, one candidate POST, three fresh code/PKCE exchanges, zero external requests, all three services exited and restarted with storage/keys preserved. SPb profile is now running; the aggregate is not yet a pass.

Final TS decoder/corpus/legacy checks: 126/126. Repository Biome: 459 files passed; changed CSharpier: 38 files passed. Frozen inputs/outputs retain exact hashes.

Third aggregate finished failed: Moscow passed all stages and cleanup; SPb state `645287fe73459fd4e5b59f0555735fdf` passed startup, nine-stop generation/adoption/save, historical Moscow save, full three-service restart and authoritative API/row/key comparisons for both. It failed the subsequent browser editor assertion at line263 after a newly added `page.goto` document reload discarded the intentionally in-memory application token. Neither owner/CAS final stages nor the aggregate are counted passed. Both stacks cleaned up.

The test now uses the ordinary list router links for the two post-restart plan transitions. All exact editor checks, fresh post-restart PKCE, three-login count, both owner/stale guards and privacy assertions remain. TypeScript/discovery passed. A fourth full two-profile invocation will verify this correction; no existing failed receipt is overwritten.

Fourth aggregate was interrupted by the chat/model interruption after its Moscow services had started; no browser receipt was produced. On resume the controller was absent and the six owned services were still running. The exact state `c5a72b0a93f38a61f96bd13708db0f4a` was inspected and destroyed by the ordinary ownership-checked command; cleanup passed. It is recorded as interrupted/not-counted, never as successful or silently resumed.

Final source portability check found the retained base-codec writer text has original CRLF and a provenance hash. Explicit `.gitattributes -text` now preserves those exact writer bytes through Git; both writer/project hashes still match provenance. No codec/data/corpus output changed. A fresh full two-profile run follows from this frozen source state.

## Source/local completion

The resumed fifth ordinary two-profile invocation passed both profiles and cleanup. Aggregate `105d44a7f912ebf145828577cab1cb21`; Moscow state `e2110335dab340647a1026c0ed913da2` (13 browser stages), SPb state `fc1a98423afeb50fe8e0a3638b855a32` (14 stages). Each made one original candidate POST, three real PKCE exchanges and zero external browser requests. SPb saved/reopened both its generated plan and historical Moscow plan across full three-service restart; both owner/stale refusals passed.

The independent raw source fingerprint was `c3badaf10bbe768d87bd8081c8b3e517b90f17c765e0315cc3b930cfb317c9f9` before and after. The controller-reported `d592006a...` was reproduced as a pre-existing per-chunk UTF8 decoding defect (four U+FFFD). It is not relabelled as exact bytes. Original receipts plus a separate correction are retained under receipts/.

After E2E, the tooling-only capture repair stores raw buffers, enforces one combined8MiB byte cap, decodes complete streams and hashes raw Git stdout. Three actual-child RED failures reproduced UTF8 corruption, missed Unicode-secret redaction and an incorrectly counted cap. Final21/21 controller tests passed including nonzero/allowFailure and arbitrary raw bytes. Independent execFileSync bytes/fingerprint matched; independent scoped review found no issue. This is separate post-E2E tooling verification, not an exact-source final-tree full-stack claim. Runtime/generator/assertions are unchanged.

All source/local tasks are complete; publication task8.1 remains separately pending. See source-acceptance.md for the final phase boundaries, counts and immutable sanitized receipts. No staging/commit/push/PR/merge/target enablement was performed.
