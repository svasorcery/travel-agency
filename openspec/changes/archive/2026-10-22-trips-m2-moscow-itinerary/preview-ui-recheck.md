# Task 4.3 fix round 1 — scoped independent recheck

## Finding Verdicts

- **P2-1: canceled adoption steals explicit dismissal focus — ADDRESSED.** The component captures a focus intent before adoption and rejects a superseded continuation after await at `apps/web/src/app/trips/trips-page.component.ts:274` and `:279`. Explicit dismissal advances that intent before cancellation and schedules generation-button recovery at `:285`. The shared helper checks the captured intent both before scheduling and immediately before DOM focus at `:409` and `:413`. The canceled false-result continuation can no longer override a later dismissal, even when the clean M1 foundation remains current.
- **Regression evidence:** `apps/web/src/app/trips/trips-page-itinerary.component.spec.ts:468` adds three actual component/state/client cases with both mandatory reads, only the owner read, or only the availability read pending. They focus and click the enabled Close button, settle cancellation/render, and assert final generation-button focus, no preview/pending operation, unchanged buffer, canceled unfinished reads and no PUT. The same-cause generation cancellation case at `:499` verifies the newer dismissal focus without widening state or provider scope.

## New Breakage in the Fix Diff

- **None found (P1/P2/P3).** The two-file delta adds a small component focus-intent guard and four real behavioral regressions. It leaves state cancellation/adoption/ticket/CAS ownership intact; no state/client/HTML change, Close disabling, extra read, retry, hidden Save or baseline advancement was introduced.
- **Named fix-risk check — shared M1 focus helper:** the new default intent at `apps/web/src/app/trips/trips-page.component.ts:409` lets a subsequent ordinary M1 focus action supersede an older M2 callback; the callback also rejects a destroyed component. Existing focus callers retain their targets and use the latest requested intent. No change to M1 validation or write logic appears in the delta.
- **Named fix-risk check — no-op/double submission:** `canGenerate()/canAdopt()` checks at `apps/web/src/app/trips/trips-page.component.ts:266` and `:275` return before advancing intent for a rejected duplicate action. Accepted generation, availability refresh and route entry advance the focus intent at `:265`, `:249` and `:241`; their old callbacks cannot override a later action. No new correctness issue was found in these guard changes.

## Out-of-Scope Observations

- **None newly observed.** The three warnings and ten infos remain the previously reviewed M1 baseline noise, not new breakage. Current `.superpowers/sdd/tasks/task-4.3-fix1-biome.json:1` reports 0 errors/3 warnings/10 infos; its provenance binds all 13 to exact unchanged M1 HEAD lines and baseline SHA256s. No suppression or unrelated refactor is needed to accept this fix. NO_COLOR/FORCE_COLOR environment warnings remain visible in the raw test logs.
- Parent-owned README/focus/status documentation, browser keyboard/visual acceptance (4.4), production build/budget (5.1), whole-M2 audit, human route quality, target enablement and publication remain separate controller gates. This recheck does not re-open untouched implementation or claim those gates passed.

## Checks and Evidence

- Read the current report's fix section first (`.superpowers/sdd/tasks/task-4.3-report.md:1`), current hash manifest and only the exact two-file fix package against the create-only `task-4.3-fix1-old` snapshot. Read that full package in two sequential bounded portions; no output was truncated. Package: 48170 bytes, independently verified SHA256 `145bdbb5cba23adfac0b57358e1df866736ff104ed889615f7f11943a485e7c7`.
- Controller supplied verification of all 44 manifest entries, fixed fresh-dev base/unchanged HEAD `e654c76915103a82083ef2abc01c0579117c9749`, branch `codex/trips-m2-moscow-itinerary`, exactly two changed source/test files and byte-identical HTML/SCSS/original M1 spec. This is an uncommitted snapshot-to-current fix review, not HEAD-to-HEAD. No Git commands were run.
- Read raw semantic RED: `.superpowers/sdd/tasks/task-4.3-fix1-red-final.log:34` and `:46` show the old wrong focus target; `:57` and `:58` show 4 failed/42 passed of 46. Each added adoption permutation reaches the final focus assertion after the cancellation/no-write assertions. The exploratory discarded expectation described by the report is not used as acceptance evidence.
- Read raw frozen focused GREEN: `.superpowers/sdd/tasks/task-4.3-fix1-green.log:25`–`:32`: 46/46 tests in 2 files, successful Nx target, local start 16:11:40; run duration 12.7 s. Read final full-web outcome: `task-4.3-fix1-web.log:144`–`:151`: 735/735 tests in 54 files, successful Nx target, local start 16:13:00; run duration 16.0 s. These totals and the current manifest agree on 0 failures/0 skips.
- No suite or focused test was rerun. No untouched source/client/state was re-reviewed; no product/test/config/Git state mutation, subagent, network/model/provider, package, browser, Host, database or cleanup action occurred. Only the two requested identical recheck reports are written atomically. C free space before writing remained above the required 1.5 GiB threshold.

## Verdict

**Spec compliance after the scoped fix: PASS.** P2-1, the sole original blocking Task 4.3 finding, is addressed.

**Task quality/current fix readiness: PASS — approved for controller acceptance.** All findings addressed; no new Critical/Important breakage. The original task-scoped review otherwise stands. Canonical completion and the later independent gates remain the controller's responsibility.