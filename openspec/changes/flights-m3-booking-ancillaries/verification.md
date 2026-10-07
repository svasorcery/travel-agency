# Verification and acceptance map

## Current implementation edition — 2026-10-07

User approved the simpler route with more manual handling and deferred automation, then explicitly authorized implementation. Source and local fictional acceptance are implemented; persisted/Host/Aspire runtime acceptance and delivery remain pending. No local Host/AppHost/DB/schema/key operations, real APIs or paid evals were run.

Base is fetched `31f4a1da3571386044946240e957088e5ea21714`; the existing managed worktree is reused, no archive restored. Pinned launcher remains OpenSpec 1.14.0; no tooling changes. The earlier 2026-10-06 draft passed strict validation and independent review, then the broader audit found scope/business gaps. That PASS does not certify this replacement edition.

## Observed source and local evidence

The source/local checkpoint was verified in the managed checkout at base 31f4a1da3571386044946240e957088e5ea21714. User subsequently authorized source publication and integration into dev; exact PR/CI delivery evidence is recorded below as it arrives. The preceding local results do not establish published CI or deployed-runtime acceptance.

| Check | Observed result and limit |
|---|---|
| Audited isolated .NET tests | 92 passed, 0 failed after the fresh user-requested audit: pure domain, offline wire mapper/transport, fake-session admission/recovery, direct endpoint and projection cases. No regular Unit assembly/key fixture, DB or Host runtime. |
| Shared client | 135 passed, 0 failed in the preceding final run; client sources unchanged by the fresh audit, suite not repeated. |
| Angular | 394 passed, 0 failed after audit repairs, Nx cache disabled; the final test-only lint cleanup was followed by the 2 affected cases passing again. |
| Fictional Node API | 58 passed, 0 failed in the preceding final run; Node sources unchanged by the fresh audit, suite not repeated. Startup presets use fictional fixtures and retained in-memory admission only. |
| Fictional browser | New full audit run: 49 passed, 0 failed, including all three presets, existing flows, lost responses/reload and traffic guards. Only Node demo API and Angular server ran. |
| Production Angular build | PASS, cache disabled. Existing unchanged flights-page.component.scss budget warning: 4.50kB versus 4.00kB. |
| C# source compilation | Unit, Flights Integration and Host Integration build sequentially with 0 errors after audit repairs; no fixture execution. Standalone Verify discovery warning retained. Historical Host RouteHandlerAnalyzer 24 AD0001 IndexOutOfRangeException warnings remain unexplained and unsuppressed; the new incremental Host build emitted no warnings, which does not establish a fix. |
| Formatting | CSharpier whole-tree check PASS (753 files); Biome CI PASS (223 files, 42 warnings/448 infos, no errors). Own formatting/import/a11y errors repaired without changing tool/configuration. |
| Documentation and harness | README example/loopback smoke-helper tests: 12 passed; generated examples unchanged. Harness/inventory tests: 195 passed, 1 existing Windows symlink prerequisite skip. Harness inventory and .NET inventory validators PASS; authenticated verifier/runtime smoke not run. |
| Reviews | The earlier independent reviewer found five P2 defects and persisted-source gaps. The subsequent user-requested whole-solution audit found seven further defects (one P1, six P2). All were repaired with behavioral RED→GREEN. The later audit was performed by the primary agent, not another independent agent; details in review.md. |

Exact local logs and the retired runner source are retained in ignored .nx/cache/flights-ancillaries-proof. The new audit uses astra-backend-green.log and astra-*-final.log; preceding evidence remains under its original names. Retiring the temporary csproj to Proof.csproj.source.txt restored the unchanged inventory gate; no repository project/tooling exception was added.

Existing-CI source was added for Marten start/finalization rollback and CAS, before-wallet atomic closure/actual facts, operator attachment/projection, real Wolverine scheduled-check host replacement/redelivery/DLQ, and signed-JWT HTTP policy. These sources compile only. Actual persisted/Host/OpenAPI/architecture/Aspire/whole-stack E2E remains pending in tasks7.2–7.3 after separately authorized publication. Accept any changed full Host snapshot only from actual CI received output. Nothing in local evidence closes whole M3 or establishes live hold+services support.

## Requirement-to-task coverage

Every exact title maps to the numbered groups in [tasks](tasks.md). The matrix describes required acceptance; actual local results and CI-only boundaries are listed above. A checked source task does not certify pending persisted or production-Host execution.

| Requirement | Groups | Acceptance case |
|---|---|---|
| Complete pre-booking scope | 2,5,6,7 | Empty extras, 1–9 adults/1–4 legs, no post-booking writes |
| Temporary catalog and retained purchase | 2 | No catalog/maps in stream; selected snapshot sufficient |
| One current quote and preserved draft | 2,5,6 | More than two minutes, changed identities/terms with draft retained and consent reset |
| Ownership and current scope | 1,2,4 | Claimed anonymous/foreign and owner-after-start zero-provider, owner refresh/start CAS preserves binding, wrong passenger/segment denied |
| Baggage allowance and extra quantity | 2,3,5 | Included vs extra, missing allowance, q2 spanning segments costs twice unit |
| Passenger-specific seats and disclosures | 2,3,6 | Different adult prices, unique physical seat, free seat and safe text/accessibility |
| Exact accepted total | 2,3,5 | 73.50 exact, pricing intent/currency/overflow rejection, old-client extra acceptance refused |
| Separate offer and order identities | 3 | Changed IDs map once; ambiguous graph no guessing; booked line quantity already included |
| Durable single creation attempt | 1,7 | Failed commit zero effects, two callers one creator, restart no resend |
| Retained identity and compatible hold response | 1,4,5 | Exact old replay, no false 200, 15s timeout then later status |
| Distinct outcomes and cancellation-only differences | 3,4,5,7 | Known unpaid missing bag can cancel; unknown sender/identity/money cannot |
| Limited checking and explicit manual fallback | 1,4,7 | One known-ID logical check, lost ID manual, fixed deadline/DLQ, no list endpoint |
| Privileged manual creation review | 4,7 | Existing operator-only policy, exact attachment and no-effects/quiescence, retained audit |
| Confirmation verifies accepted services | 3,4,7 | Same-price changed seat before/after capture, no money-only manual bypass |
| Authoritative status and resource-based recovery | 4,5,7 | Reload before EF, resource route only, no recent discovery promise, owner epoch/stale cached hold |
| Protected details and private operation state | 1,2,4,5,7 | Protected command, keyless metadata GET, no PII/tokens/operation IDs in storage/URLs/logs |
| Readable purchase summary and differences | 5,6,7 | Adult/leg summary, segment exceptions and unique charges; same-price seat diff visible, unknown/currency change not fabricated |
| Reproducible fictional demonstrations | 7 | Three presets and README steps, isolated starts, no active reset, no production fault controls or external calls |
| Historical and evidence compatibility | 1,4,7 | V1/V2/V3, old receipt bytes and no-service DTO, explicit fictional/CI/live boundary |
| Whole-order eligibility and ownership | 4,7 | Actual known service differences allow consent-bound cancellation once creation resolved |
| Separate supplier refund and customer payout | 4,5,7 | Exact supplier quote, no fabricated free cancellation/refund split/customer payout |
| Historical compatibility and safe activation | 1,4,7 | Old terms/scope/notice/hash preserved, no old-bodyless cancel bypass |

## Self-review exit matrix

- Known created result with missing baggage: save actual Held state, Confirm disabled, owner cancellation allowed after identity/completion/no-money checks.
- Uncorrelated result: manual barrier stays; neither a timeout nor a query miss becomes no-effect.
- Crash immediately after start: manual may be required with zero actual effects. This is the user's accepted simplicity trade-off.
- Old HTTP client: 200 remains HeldOrderResponse; no extra success shape hidden behind it. Unknown/conflict responses or network timeout use authoritative status; no automatic POST retry.
- No extras: every fresh hold shares admission; current HTTP callers need no new acceptance field unless their quote contains services.
- Owner refresh/start race: accepted quote freezes at start, every quote writer observes the same barrier/CAS; no quote-binding mutation until a positive no-create closure.
- Known-ID check: readonly redelivery allowed within fixed deadline, never another create; no automated lists or correlation-by-newest.
- Body identity: key/digest metadata only, randomized ciphertext cannot produce a new operation. Missing keys may reject plaintext POST but never status reads/history.
- Catalog refresh: retain draft, compare current facts, invalidate acceptance for the new quote; no 120s blanket deletion.
- New manual fields: existing v1 normalized bytes/hash unchanged when absent; target-specific guards retained; no owner access.
- Lifecycle proof: current source's explicit outbox/AllowMultiples and eventual projection contracts retained.
- Demonstration packaging: success/difference-cancellation/unknown-manual presets share the existing planned fixtures and do not imply automatic lost-ID recovery or acceptance of replacements.
- Summary/diff: selected and actual stages remain distinct; optional seats, unknown allowance and cross-currency amounts are shown honestly; no added passenger PII reads or duplicated per-segment charges.

## Execution boundary

Local after explicit implementation approval: audited pure Core, offline mapper/loopback, lean HTTP with fakes, TS/Angular, Node/browser fictional demo. Inspect project linked files, fixtures, static initializers, factory startup and selected filters before running. .NET sequential on Windows. No hidden local Host/DB/key setup to make a test pass.

Existing CI-only: persisted Marten/outbox/restart/rollback/concurrency and projection; production JWT/DI/full Host OpenAPI; architecture/Aspire/whole-stack E2E. Source compile/fake-session results are not that proof. Do not modify CI/CD/skips; if Aspire exit134 recurs, keep unchanged-run evidence and explain options without asserting a cause.

## Historical planning checks and review

Strict OpenSpec validation, UTF-8/fences/local links, requirement coverage, unchanged original modified-capability scenarios, one active change and scope diff are required after rewrite. Actual results and current independent review are recorded in [review](review.md) and [process log](process-log.md); prior counts 26/54/46 belong to the superseded draft.

Core results before the demo/UI additions: strict validation PASS; 9 Markdown/23 local links/20 requirements/42 scenarios/25 unchecked checkpoints, no structural/coverage errors. AI harness read-only inventory PASS (8 instruction pairs,8 Travel skills,5 agent pairs,5 legacy commands,6 pinned OpenSpec skills). Independent pass found the owner re-quote race; fixed and focused re-review PASS, no remaining P1/P2. Product tests and authenticated/native harness verifier were not run.

After the approved demo/UI additions: own light logic review and fresh strict validation PASS;9Markdown/23local links/22requirements/46scenarios/25unchecked checkpoints, zero audit errors. Known browser-response loss and unknown supplier-result loss are separate fictional cases. Presets/summary/diff add presentation and repeatability only; no new runtime acceptance claimed. Unchanged harness/product suites were not repeated for this documentation-only update.

## Product completion boundary

Source/focused tests → fictional combined proof → separately authorized publication/existing CI → human merge/postmerge → separately authorized main-spec sync/archive/docs delivery. A1 is a documented implementation assumption, not a request to run live calls and not an offline writer gate. This feature does not close the whole M3 roadmap.
