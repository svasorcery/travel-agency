# Flights M2.4 — saved-traveler verification

## Scope, approval and base

The user approved the specification/plan and source-only migration on2026-10-03, with delivery through normal PR/CI/merge/dev sync/own cleanup. Fresh fetched `origin/dev` remained `f143e360241311b521e70153ba33998b4bff6109` (PR32 merge); ancestry to M1 checkpoint `d75186054a33de0e1cb688da7e3e7cc42927dadf` and M2.3b was verified. The managed `flights-m24-design` worktree was reused from that exact base on `codex/flights-m24-saved-travelers`; no archive was restored. Main dev remained clean.

M2.4 adds private fictional profiles, conditional CRUD and explicit save/copy into one booking slot. Profile ID, booking slot and supplier passenger reference remain distinct. Seven fields are protected under owner/profile/revision; edits/deletion cannot change copied or held booking snapshots. Current1–9-adult booking, deterministic ranking and M2.3 unknown-outcome guards remain. Multi-leg/open-jaw M2.5 is subsequent work.

## Implemented boundaries

- One Flights EF table stores ciphertext envelope plus ID/owner/revision/timestamps. No plaintext label/search index, event stream, booking foreign key, durable plaintext message or cached profile response.
- Existing Data Protection ring/certificates, new purpose `saved-traveler.v1 / owner / profile / revision`; actual crypto tests use separately owned temporary fictional rings. New envelope caps32,768 Base64 characters; old booking/inbox purposes remain unchanged.
- Owner/existence/current revision checked before encryption. Atomic owner/ID/revision update/delete; insert-only create; delete without keys. Complete owner page reads21 opaque rows and decrypts only20 returned items; unreadable owned data fails closed rather than returning partial data.
- CRUD uses JWT/`flights:book`, strong ETags and strict preconditions. Metadata-only write receipts; foreign/missing404, stale412, required428, malformed400, unavailable503. Bodyless DELETE204. The backend never reads a demo-owner header.
- Header-only facade before auth supplies `no-store` on failures without changing auth order. After-auth body guard reuses16KiB RAM reader, zeroes bytes, rejects unsafe JSON before binder logging and preserves booking raw fingerprints/idempotency semantics.
- Memory-only client protects owner/epoch/auth and quote/slot/selection/manual-edit generations. A profile can be saved before adulthood, but booking still checks18 at first departure. Unknown or malformed writes do not auto-retry; a fresh read shows current state without proving historical execution. New owners receive independent mutation gates; current-generation pending review is always released.

## Executed local evidence

| Check | Final observed result | Evidence boundary |
|---|---|---|
| Full Flights unit, Release | 772 passed,0 failed/skip | Pure/fake providers/storage, actual temp-ring crypto; no DB |
| Safe Host HTTP/metadata/JWT suite | 213 passed | Former123 +90 profile cases; no real Host |
| Pure generated migration/SQL | 3 passed | Context model/operations/SQL only, no connection |
| Architecture, Debug | 167 passed | Project/type/reference gates; no services |
| Full solution Release build | 0 errors | Known AD0001 analyzer warnings (13), no suppression |
| Full web unit | 345 passed | Final behavior including UI review fixes |
| API-client unit | 92 passed | Unchanged library reused verified cache after UI-only fixes |
| Production web build/lint | Passed | No new component-budget warnings; seven existing lint warnings |
| Fictional Node API | 42 passed | In-memory only; unchanged during final proof fixes |
| Fictional Playwright | 37 passed,0 retries/skip | Existing23 plus profile/proof cases, one worker |
| Repository Biome/CSharpier | Passed | CSharpier592 files; source formatting only |
| AI-harness/README/inventory | Passed | Dependency-free/mocked checks; no authenticated model verifier |

Core commands:

```text
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit --configuration Release --no-restore --filter "Category!=AiEval&Category!=AiEvals" --maxcpucount:1
dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration --no-restore --maxcpucount:1 --filter FullyQualifiedName~SavedTravelerMigrationTests
dotnet build Travel.slnx --configuration Release --maxcpucount:1
dotnet test tests/Travel.Tests.Architecture --configuration Debug --filter Category=Architecture --maxcpucount:1 --no-restore
npx nx test web --watch=false
npx nx test api-client --run
npx nx build web --configuration=production
node --test tools/demo/flights-search-api.test.mjs
npx playwright test -c tests/travel-e2e/playwright.flights-demo.config.ts
npx biome ci .
dotnet csharpier check .
npm run check:ai-harness
npm run check:readme-examples
npm run check:dotnet-inventory
```

The safe HTTP filter names only the nine former noDB classes and the new profile HTTP/middleware/metadata/OpenAPI/Wolverine/fingerprint/JWT classes. Every fixture was inspected first. The four-type real Wolverine fixture uses explicit registry discovery, mediator-only operation, controlled services and no Npgsql/Marten/transport/initializer registration. Its static-discovery stage switches publicly to Auto code generation after precisely those four handlers are selected. No unbounded runtime fallback or schema initialization was performed.

Real signed-JWT evidence is14 new GET/PUT tests using local fictional keys/issuer/audience/lifetime and no synthetic user headers. Wrong signature/audience/issuer/expiry/scope/GUID subject is refused; conflicting demo-owner header cannot change captured owner. A test-only disabled-audience mutation produced the expected2 failures/12 passes, then validation was restored and all213 passed. Earlier199-case ownership proof used synthetic claims; its initial report overstated JWT verification and was explicitly corrected. This is real framework verification with fictional tokens, not current Keycloak token issuance.

## Reviews and repairs

Independent Task1/2/3/4/5 source reviews and scoped re-reviews completed. Important findings were repaired with specific evidence:

- Npgsql nonretrying strategy wraps transient errors: narrow wrapper classification and failed-create detach, actual provider interceptor before network open. RED24 failed/9 passed then33 green; cancellation/programming/nontransient errors still propagate.
- Owner/epoch switch retained profile mutation gate, and same-slot re-quote could leave review pending: RED7 then22 green, full web345. Same-session auth-error uncertainty remains; stale completions cannot clear a new pending review.
- Later browser `route.fetch` handlers bypassed the global guard, and some stale-response assertions finished before delivery: exact origin/path/method/no Authorization before every fetch, redirects disabled, negative synthetic URL aborted before any network; handler/browser/client callback completion barriers precede assertions. Final37 browser cases passed.
- Required signed-JWT proof was absent and unused fixed TEMP export remained: added actual Bearer/negative controls, corrected evidence, removed export from source. The exact task-created file was deleted only after hash verification.

Browser setup failures were investigated separately: hard navigation erased fake memory auth, authenticated checkout still required a fresh accepted quote, and a detached response observer raced body disposal. Test helpers now preserve SPA identity, await both quote stages and read the actual Node response before delivery. Existing prices/group/slot/safety assertions stayed. The initial29/36 and35/36 runs are failures; only the final37/37 run is acceptance evidence.

Screenshots at desktop and360px were regenerated and inspected. A/B demo owner isolation, CRUD/CAS/current-state review, future-adult save/minor booking block, two-profile copying, snapshot independence, response loss, malformed success, late edit/quote/owner/auth/navigation completion and no storage/URL/history/console PII were exercised. Logout uses the existing public Angular handler through development debug API because the demo intentionally hides its button; this is lifecycle proof, not visible logout UX. Fake Node memory/captured hold and demo auth do not prove real EF/crypto/JWT/provider/payment acceptance; those layers have separate tests.

## Schema, compatibility and rollback

`20261003115745_AddSavedTravelers` adds only `flights.saved_travelers`: six required columns, UUID checks, PK and owner/createdAt/id index. Offline schema-qualified idempotent Up and Down SQL were reviewed. No SQL was executed; no local database/key provisioning/Host/AppHost/deployment occurred. Source-ready is distinct from applied schema.

The additive table leaves M2.3 event identities, ciphertext purposes and order projection unchanged. Rolling back application binaries can leave unused encrypted profiles. Explicit Down would delete profiles but not booking evidence; it requires separate authorization and must never be implicit. Profile deletion is not erasure of backups or copied booking history. Existing key retention/restore and safe unavailable behavior apply.

Twelve real PostgreSQL tests compile and remain CI-only: ciphertext rows, owner-filtered reads, concurrent create/update/delete, detach, stale/foreign writes, key-free delete, paging, malformed data and rollback/reapply. Full actual Host route/auth/header/DI/OpenAPI snapshot and Aspire are also CI-only. Paid evals, real providers/payments/Anthropic/other paid APIs, local migrations and deployment were excluded throughout.

## Delivery gate

At this checkpoint whole-branch independent review and mandatory exact-head CI precede publication/merge. All normal checks, including dependent PR E2E, must succeed; no bypass or CI/CD change. All source/tests/docs belong to this increment and will merge together. Main checkout synchronization and only owned cleanup follow actual merge and post-merge verification. M2.4 is not completion of all M2; M2.5 remains.

The user separately approved exactly three factual AGENTS sentences after automatic review required named-file authorization. No architecture/authority/commands/auth instructions changed. Compatibility adapters remain imports, and harness checks verify their pairing.


## First PR CI and precise expectation repair

PR33 initial head `076c31698a99dffe405724c508f49462887dd826`, [CI37129624793](https://github.com/svasorcery/travel-agency/actions/runs/37129624793), failed and was not merged. Host integration51 passed/1 failed: all OpenAPI paths, methods, schemas, headers and non-tag metadata matched; exactly four new endpoint tags were absent from the full snapshot. Only those twelve lines were added after structural comparison with the actual CI document. A new isolated four-handler regression checks the global tag inventory: old snapshot produced1 failed/1 passed, corrected tags2/2 passed without Host/DB.

Flights integration342 passed/1 failed, including all twelve new profile PostgreSQL cases. The old count-upgrade test intentionally stops at M2.3; its assertion that no later migration was pending became stale. It now asserts exactly `[AddSavedTravelers]` pending, while keeping the three applied migrations and checkpoint/ciphertext preservation assertions unchanged. Fresh/latest migration tests still require all four migrations and no pending ones. No production change or check weakening.

Other normal jobs succeeded; dependent PR E2E was skipped because mandatory prerequisites failed, not counted as passed. Paid evaluations remained skipped. Compile-only validation, scoped independent review and a new complete exact-head CI are required before merge.
