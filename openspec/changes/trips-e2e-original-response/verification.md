# Verification

## Scope and diagnosis

Fresh base: `b84287280156f615fa006fb46fa85ee7ca2e7158`; branch `codex/trips-e2e-chromium-response`. The user authorized investigation, repair and a separate PR into dev with green CI, without merge, weaker checks, v2 release or real-user enablement. An existing isolated D-drive worktree was reused because C has limited free space; the retained interactive demo was not changed.

[PR76 CI40215720415](https://github.com/svasorcery/travel-agency/actions/runs/40215720415), attempt 1, failed at `generate-without-write` with `body-resource-missing`; its failed artifact remains `12607681719`, ZIP SHA256 `a1d15da6d463ee94469d3d1f1094d320f8db02cf0749720c24a59899f1941a48`. The failure preceded candidate status/body assertions. An unchanged-head retry and later post-merge run passed; neither established an upstream fix or the precise historical eviction trigger.

Installed Playwright is 1.64.1 with Chromium 156.0.8078.4. Its original response reader invokes `Network.getResponseBody` against a renderer resource. Reading eagerly only narrows the timing window; it does not own that resource's lifetime. The replacement reads the same finite response at Fetch response stage and continues it without overrides. It makes no second HTTP request and changes no production/browser-cache policy or dependencies. The accepted JSON cap is 128 KiB; CDP itself buffers a whole finite response, so this observer is confined to the already-bounded local Host endpoint, not offered as an arbitrary-stream memory limiter.

## Reproduction and regression

| Check | Observed result |
|---|---|
| Isolated actual HTTP/Chromium JSON, text and stream consumption, each with/without abort | 72/72 passed; these cases did not reproduce the original failure. |
| Controlled renderer release with 100 ms delayed observer | Original helper failed 6/6 with the same `Network.getResponseBody` missing-resource error; one POST per trial. This reproduces the lifetime dependency, not a historical navigation in the CI run. |
| Committed browser regression before replacement | Failed with the expected missing-resource error at the original helper's `response.json()` call. |
| Same regression after replacement | Passed; the page parsed the original candidate before its navigation, the observer retained that candidate and exactly one POST occurred. |
| Final browser regression set | 13/13 passed, zero retries: original bytes/headers, resource release, HTTP failure, malformed JSON, declared/chunked oversize, duplicate POST, redirect, action failures, held-body timeout/release, early release and foreign scope. |

Exploratory scripts/results are retained locally under `TestResults/chromium-diagnosis/`; they are ignored local evidence, not published route material. All server payloads in these regressions are explicitly fictional. The full-stack scenario still uses the ordinary application and real services.

The removed three mock JSON-observer unit tests covered eager read, body error/no replay and late action error. Their obsolete helper is removed rather than left as a fallback; those behaviors are now exercised against real Chromium/HTTP. The existing metadata-only observation regression remains intact.

## Source checks

- `npm.cmd run test:trips-stack`: strict scoped TypeScript, 14 lifecycle tests, 21 unit tests and 13 real-browser regressions passed. This exact command is used by the existing ordinary CI Trips lane after Chromium installation.
- The initial broad E2E TypeScript invocation exposed one new unsupported `CDPSession.removeAllListeners` use plus existing failures. Cleanup now uses public `off`/`detach`. A read-only run on pristine dev independently confirmed the remaining 16 pre-existing broad-config errors. The new scoped target inherits `strict` and uses `allowJs` for the existing imported JavaScript lifecycle helper; global settings are unchanged. Broad-config success is not claimed.
- E2E lint passed with zero errors and 23 warnings confined to unchanged files. Two new empty-function lint errors were corrected; no rule was disabled. Biome formatting and `git diff --check` passed.
- CI inventory: 64 passed, one existing Windows symlink-privilege skip; actual inventory validation passed. AI harness: 134 passed and canonical validation succeeded.
- Pinned strict OpenSpec validation passed; the informational `skip_specs` message is expected for this test-instrumentation repair. No capability contract was changed.
- An AST comparison retained every one of the original 143 `expect` calls; the full-stack scenario now has 145. All 13 stage calls and the original timeout/retry configuration remain. Production code, dependency locks and all 98 frozen local audit/material/form files remained unchanged.

## Independent review

The fresh-context read-only reviewer found no defect in original-response integrity or retention of the existing E2E assertions. One Important finding identified a race in the new held-body fixture: release before request arrival could be lost. Release now latches, an early-release test covers that ordering, and the timeout test proves the response started before observation failed. Its five-second deadline belongs only to that new synthetic fixture; the full-stack/default capture deadlines are unchanged.

One Minor finding requested coverage of actual size without Content-Length; the chunked oversized case now covers it. Scoped recheck closed both findings with no new substantial issues. The reviewer did not rerun runtime tests, claim the exact historical Chromium trigger, generalize to other browsers or accept a hard protocol memory bound/target/human quality.

## Integration and delivery

Local `npm.cmd run test:trips-full-stack` passed with stack `cc53bcd6f44079a02d6263c30fc118fb`: startup, all 13 browser stages and owned cleanup. The browser ran from 2026-10-25T18:48:45.625Z to 18:50:41.542Z, with exactly one candidate POST, three real PKCE logins and zero external requests. All three Host/Keycloak/PostgreSQL processes exited and restarted with storage preserved; accepted plan, protected row and key comparisons passed.

Its source receipt records base `b84287280156f615fa006fb46fa85ee7ca2e7158` plus initial dirty-tree delta SHA256 `a56211339fefffbc131cd0037528a4039e777923a44bb86b38701e50d163067b`. Fixture/documentation review changes and a behavior-equivalent rejection-handler lint correction were completed during stack build, before the browser phase. This local result is functional working-tree evidence, not an immutable final-head CI receipt. Final ordinary CI must bind the published commit and run the final regressions/full-stack journey again; its exact head/run/attempt/results are recorded in PR metadata.

Local evidence remains under `TestResults/trips-local-stack/evidence/` with that stack-ID prefix. Accepted-plan fingerprint: `d276f4b777c23d0d939a13a1862cc12943dc052f90a65623f3314c69800520d4`; protected-row fingerprint: `e51a3bf121fc575c82c9532966e892f4f5fdd8e4af4f76c6fcd9128f882f2db5`; key fingerprint: `d50cb0bd4806b130ea85275fd329c9914a65bdffd57a908e8c532dd02bbbdfe5`. These are sanitized hashes, not published private state.

Earlier failed results stay failed. Human A/B scores remain blank, v1 bytes/expiry remain unchanged, and no merge or rollout is authorized by source checks.
