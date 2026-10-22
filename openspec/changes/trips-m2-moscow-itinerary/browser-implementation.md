# Task 4.4 — frozen fictional browser and actual local static-delivery receipt

Ready for independent task review; canonical completion remains the controller's responsibility. Fixed fresh-base HEAD `e654c76915103a82083ef2abc01c0579117c9749`, branch `codex/trips-m2-moscow-itinerary`, worktree `C:/Users/Vladimir_sva/.codex/worktrees/trips-m2-implementation/travel-agency` remain unchanged.

## Result and ownership

Final existing Trips Playwright command passed **35/35 cases: 5 retained M1 +30 new fictional M2, 0 failed, 0 skipped, 0 retries**, one worker. Final raw log: `task-4.4-full-frozen.log`; start `2026-10-21T16:35:35.3540592+03:00`; Playwright reports `35 passed (1.3m)`. No subsequent source edit or browser rerun was made after this successful exact-source run.

Only three browser files changed: existing `tests/travel-e2e/trips-demo/trips-workspace.spec.ts`, extracted/extended `trips-fixture.ts`, and new `trips-itinerary.spec.ts`. All five original M1 test bodies are byte-identical to HEAD; the existing spec only imports the extracted helper and explicitly registers its receipt hook. Exact baseline/body hashes are in `task-4.4-biome-provenance.json`.

No product component/state/client/backend/data, root or Playwright configuration, README, canonical task checkbox or progress file changed. The unchanged accepted 4.2/4.3 owned sources were verified against their frozen manifests. Current 4.3 report SHA256 is `59ca4a0900dfc2a644cab8acbedbc400bea38fd1211632b1400b011ed5ef1bcf`; recheck is `aaa773539fbbe373432f7690b3f41c7faaeb4d7c58e4776cbd1eef8377644f5c`.

## Actual browser behaviors

The main case saves an empty Moscow foundation, explicitly generates a Partial preview, opens a source disclosure using actual Enter/Tab traversal, checks focused preview/source anchors, safe HTTPS `noopener noreferrer` links and no embedded images/iframes, then transfers with exactly one owner GET plus one availability GET even after a double click. It checks unknown hours/cost/arrival, unmet food interest, modelled 12–22 minute walk, absent field verification/human quality/calibration, and separate persistent OSM/ODbL credit.

Transfer only stages two ordinary M1 blocks. Original title/brief/revision and the actual partial vector UUIDs survive; time and money are null. Before explicit Save there is no PUT and the server plan remains byte-equivalent to its captured foundation. The user edits a block title and a source-looking historical note, explicitly saves with If-Match `"1"`, and reloads ordinary intentions. No trusted preview/fact panel returns. The edited HTML-looking title/note remains input/textarea text; no interpreted source badge is created. Historical reference in the other note remains editable. Generic source attribution remains outside notes. Private text/preview is absent from returned document HTML, and localStorage/sessionStorage/IndexedDB/service workers are empty.

Other new cases cover disabled M2 with working manual M1 Save; filled/empty/unsupported wire branches; stale generation revision; saved-nonempty generation refusal; same-revision package version/hash switch, unknown policy, package withdrawal, flag-off and actual aborted availability read; late candidate after form-only dirty and dirty-then-revert; owner A/B logout privacy; candidate-derived ordinary 412 conflict/no automatic rebase; committed lost PUT uncertainty/no resend; later form edit during reread; and later form edit surviving an accepted delayed Save reply without autosave.

Twelve actual adoption races cross both/owner-only/availability-only pending reads with manual title edit, logout, new-plan navigation and explicit Close. Late replies do not stage or PUT. All three pending-Close cases keep Close enabled, cancel the operation, recover generation-button focus and retain it after late completion. Test gates expose started/settled promises; final assertions wait for route completion and render frames. No timeout, retry or coverage relaxation was used.

A separate 390px mobile case opens disclosure, closes/regenerates, transfers, edits and explicitly saves using actual controls. Both the main mobile screenshot and the operability case assert no horizontal overflow.

## Fixture boundary and exact requests

This is the existing in-memory terminal Playwright fixture, not another CLI/API server. Every API effect ends in its private Map. The only server is the unchanged Angular webServer at `127.0.0.1:4203`; no Host/AppHost/DB/Keycloak/model/provider process ran. The fixture retains M1 lost-PUT-after-commit, delayed owner reads, owner isolation and CAS. Its former broad method allowance is narrowed to exact M1 method/path combinations plus only GET `/api/trips/itinerary-availability` and POST `/api/trips/{tripId}/itinerary-candidate`. Candidate requests require literal `{}`, a strong revision header and fictional bearer owner. Unknown API method/path/query and any external page request are recorded, aborted and fail the receipt assertion. Non-API same-origin assets continue to the actual Angular server.

Responses derive from all six actual frozen production HTTP vectors under `tests/fixtures/trips-itinerary-v1`. Browser clock is explicitly fictional `2026-10-21T00:01:00Z`; vector timestamps, sources, facts, path and proposed UUIDs remain unchanged. Candidate revision/destination/timezone/catalogue/day dates bind to the fictional saved foundation. Named negative tests mutate ready package identity/policy or error transport/status. Typed errors copy the frozen error object's exact shape/traceId and explicitly replace status/code/title with the required terminal 404/412/503 outcomes. These are fictional negative fixtures, not changed real data or an alternative DTO contract. No canonical Moscow file is replaced.

`task-4.4-expected-discovery.json` records discovery ONLY (its unexecuted status fields are not test results). The post-run audit requires exactly its 35 unique Playwright IDs, matching title/file/retry0/repeatEach0 and 35 unique ledger files: 5 M1 and 30 M2. `task-4.4-ledger-summary.json` records 0 missing/duplicate/unexpected IDs, 35 passed, **1697 page requests, 479 terminal API calls, 0 external page requests, 0 unknown-request failures, 0 model/provider calls**, plus six separately recorded explicit same-origin public GETs. It also confirms the own 4203 listener is closed after Playwright cleanup. Network was fenced at page routing; this is not a claim of globally disabled networking.

| Method | API path (UUID normalized only in this aggregate) | Count |
|---|---|---:|
| DELETE | `/api/trips/{tripId}` | 1 |
| GET | `/api/trips` | 133 |
| GET | `/api/trips/destinations` | 85 |
| GET | `/api/trips/itinerary-availability` | 107 |
| GET | `/api/trips/{tripId}` | 72 |
| POST | `/api/trips` | 35 |
| POST | `/api/trips/{tripId}/itinerary-candidate` | 30 |
| PUT | `/api/trips/{tripId}` | 16 |

Raw per-case paths/owners/If-Match/bodies/status attempts are retained in the 35 `TestResults/trips-m2/browser/ledger-*.json` files, each bound to TestInfo.testId. Terminal status0 denotes the three intentional transport aborts; attempted 200 replies to canceled requests are not server acceptance claims. No hidden candidate/adoption retry was observed in bounded per-case assertions.

## Six actual public asset GETs

The main case uses APIRequestContext for exactly six explicit loopback GETs, redirects disabled, and compares each response Buffer with canonical Data bytes and SHA256. Page routing does not fake these responses. The manifest's exact five child hashes also match. No external source or walking-evidence URL is followed, and the page does not automatically fetch these files. `actual-public-assets.json` retains URLs/status200/bytes/both digests.

| File | Bytes | Actual response SHA256 = canonical SHA256 |
|---|---:|---|
| moscow-v1.manifest.json | 488 | `8f364b38540a3d8dfd21272021c50f3f18ca6c01d2863617368c12d2e6f60e43` |
| ATTRIBUTION.txt | 869 | `0c8bca1abb555d22fc9a30133287abb9a11cc8554169ede7c379b9473cb01172` |
| input-manifest.json | 295 | `d5a3025a6670d7aaccfaaff8c15a085197c2c9734fec50339a6ecf2322870ac8` |
| inventory.json | 7711 | `c04bc32ca50be5fa5caa6057bf89948fee874c01ecdc418d4bd92059695f01ae` |
| moscow-v1.json | 101163 | `631fd5cd9650e3228af87ef8a31219d7ea59a2e4af42bc4328453f32268ffb08` |
| walking-evidence.json | 137293 | `196c5cd39481bdb55b6c123a4555571a0aa6f2eabc720e24ef3d70f63e3b397b` |

This proves local Angular static delivery of the actual accepted source package only. The browser candidate is separately fictional; no real Moscow route/Q generation, human quality, field calibration, publication or target rollout is claimed.

## Visual QA

Three new test-only PNGs are under ignored `TestResults/trips-m2/browser`: `01-fictional-preview-desktop.png`, `02-fictional-preview-mobile.png`, `03-fictional-staged-intentions.png`. Final-run files were opened with view_image. Desktop and 390px layouts form coherent readable screens; controls/source disclosures remain visible, no horizontal clipping was observed, gold focus outlines are visible, and the trusted preview disappears after staging. The controller also independently viewed the three screenshots. Introductory disclosure density/software timestamps can be a later UX polish topic; no UI change was made in this ownership. The original retained M1 screenshot remains at its pre-existing path.

## RED, intermediate corrections and final evidence

- `task-4.4-red.log`: meaningful missing-fixture RED, 1 failed/0 skipped. The initial new browser case reached the strict route guard, which rejected GET itinerary-availability. This proves absent M2 fixture support; it does not claim 30 pre-existing product failures.
- `task-4.4-main-initial.log`: 1/1 main flow passed, including actual static assets and the three screenshots.
- `task-4.4-m2-initial.log`: 29/30 passed. The sole failure was my generic Save-status expectation; actual correct M1 behavior said the submitted version was saved and later edits remain unsaved. Only the assertion changed to that stronger message.
- `task-4.4-full-final.log`: historical preliminary full run, 35/35 in 52.0s. Receipt audit then found only 30 ledgers because a hook registered while an imported helper first loaded did not register again for the second spec. This run is not used as a complete 0-external ledger receipt. The original log is preserved unchanged. The test-only fix exports recordReceipt and explicitly registers it in both specs; each ledger also records actual Playwright ID/file/retry/repeat index.
- `task-4.4-full-frozen.log`: legitimate final exact-source full run after that concrete receipt defect, 35/35, 0 failed/skipped/retries, Playwright 1.3m. Complete ID/ledger audit passed, including original M1 near-limit detail 129072 UTF8 bytes. No broader rerun followed.
- `task-4.4-biome-current.json`: final exact-source check exit0, 0 errors/11 warnings/6 infos, no introduced diagnostics. All17 diagnostics map to exact unchanged original M1 helper/test lines in task-4.4-biome-provenance.json; new itinerary spec is clean. Initial introduced callback-return error/2 infos were fixed, without suppression or unrelated cleanup. Scoped git diff --check passes.

Commands from the fixed worktree:

```text
node node_modules/@playwright/test/cli.js test --config tests/travel-e2e/playwright.trips-demo.config.ts
node node_modules/@biomejs/biome/bin/biome check tests/travel-e2e/trips-demo/trips-fixture.ts tests/travel-e2e/trips-demo/trips-itinerary.spec.ts tests/travel-e2e/trips-demo/trips-workspace.spec.ts --reporter=json --max-diagnostics=200
```

Unchanged topology: workers1, retries0, test timeout30000, webServer timeout120000, reuseExistingServerfalse, loopback4203. Existing Playwright1.64.1 headless shell1248 was used; no browser/package download/install. Per-process TEMP/TMP and Nx cache use the owned D dependency directory; NX_SOCKET_DIR uses owned short `D:/_Projects/_github/travel-agency/TestResults/m2-ipc-01a15a`; NX_DAEMON=false/NX_NO_CLOUD=true. No global environment/configuration/cache change, cleanup, existing user process reuse/kill, extra server or disabling plugin isolation occurred. Existing NO_COLOR/FORCE_COLOR warnings remain visible.

C free was checked before each heavy run and final audit: final receipt 2478632960 bytes (about 2.31 GiB), above the1.5GiB stop threshold. Reports/receipts are written atomically. No Git stage/commit/push/merge/branch mutation, feature enablement, keys, migrations, real supplier/model/paidAPI or external publication was performed. Source default-off and controller-owned documentation remain untouched. Task5.1 production build/budgets, Q/source/whole-M2 review and target/publication remain separate gates.

`task-4.4-hashes.json` freezes the exact three browser sources, accepted input sources/reports/config, six vectors, canonical public files, three final screenshots, all35 raw ledgers and raw logs/receipts/report. Independent task review is the next gate.
