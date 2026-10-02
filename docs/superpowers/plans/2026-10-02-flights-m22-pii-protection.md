# Flights M2.2 Protected Passenger Data Implementation Plan

> **For agentic workers:** after joint specification/plan approval, use `superpowers:executing-plans` for inline execution with TDD, followed by independent whole-change review. This plan is not implementation authorization by itself.

**Goal:** protect new one-passenger booking snapshots and webhook payloads while preserving existing booking behavior, historical replay and owner isolation.

**Architecture:** Core holds an opaque protected snapshot and a new versioned event; Application owns protection/decoded-webhook ports; Infrastructure owns a dedicated Data Protection provider, key-ring operations and provider decoding. Existing jsonb columns hold versioned envelopes. Host owns only early CLI dispatch and existing process composition.

**Tech stack:** existing .NET 10, Marten 9.14.0, Wolverine 6.17.0, EF/Npgsql, Angular, xUnit/Shouldly, fake suppliers/SMTP and isolated Playwright. Add the .NET shared-framework reference needed for Data Protection to Flights Infrastructure; no new paid service, crypto algorithm implementation or project.

**Spec:** [M2.2 design](../specs/2026-10-02-flights-m22-pii-protection-design.md). Its P1–P8 acceptance criteria are binding.

**Status:** approved on 2026-10-02; executed on `codex/flights-m22-pii-protection` from freshly fetched `11cbb657f3cfe4f24a2d712be0c1246e1522378c`. See the [execution report](../results/2026-10-02-flights-m22-pii-protection.md) for evidence and implementation rulings; checkboxes below remain the original acceptance checklist.

## Global constraints

- Only fictional passengers and temporary test keys. No real supplier, payment, Anthropic/paid API, actual email recipient, paid AI-eval, or `run_paid_ai_evals`.
- No local migration generation/application, schema creation or deployment. Inspect every selected fixture before execution. Full Host/AppHost/DB/outbox suites run in existing mandatory CI; do not change CI/CD.
- Production key provisioning/rotation/recovery is not executed by implementation. Key tests use only owned disposable filesystem directories/certificates.
- Keep one passenger, existing HTTP DTOs/auth/scope/idempotency/status behavior and B5 boundaries. No saved travelers, multi-leg, refunds/ancillaries/SSE/Support or OpenSpec.
- New passenger data is ciphertext in persistent/event/message boundaries. No plaintext fallback, raw exception/body/recipient logging, key material in source, or new browser operation persistence.
- Preserve legacy event JSON and ownerless fail-closed semantics. No historical backfill, reset, purge or automatic old-command execution.

## Review focus

1. Encryption can be correct in the event while plaintext escapes through a command, exception, inbox or notification (Tasks 1/3/5/6, P1/P4/P6).
2. Prefix-owner validation can reject V2 at an already-advanced checkpoint despite a correct event applier (Task 4, P2).
3. A missing ring may silently generate replacement keys, or missing old certificates may look like successful recovery (Task 1, P3/P5).
4. A webhook key failure may be incorrectly acknowledged, or routing metadata may be swapped across ciphertext (Task 5, P4).
5. Login/storage cleanup or a fresh privacy-service error may clear an earlier unknown write barrier (Task 6, P7).

## Task 0: approved execution setup

- [ ] Fetch `origin dev`, record exact SHA and checkpoint ancestry. Check main checkout and active artifacts before any branch switch. If dev advanced, create a fresh managed worktree from the new SHA and carry only the reviewed documents after comparing their hashes; never force-move the old branch or restore an archive.
- [ ] Create a task branch from the verified base after approval. Read root/module instructions, the spec, ADRs 0001/0006/0007/0015–0018/0023, and implementation/test/security-relevant skill instructions.
- [ ] Create an ignored per-plan execution ledger. Record interface rulings and real RED/GREEN evidence. Baseline safe unit/HTTP/frontend checks; do not blanket-run the solution test command.
- [ ] Confirm that the planned jsonb envelopes require no EF model/SQL migration. A discovered DDL need is a scope gate to explain, not permission to generate/apply a migration.

## Task 1: protection, explicit key initialization and availability

**Create:** Core `ValueObjects/ProtectedPassengerSnapshot.cs`; Application `Privacy/IBookingPassengerProtector.cs`, `Privacy/PiiProtectionErrors.cs`; Infrastructure `Privacy/FlightsPiiProtectionOptions.cs`, `Privacy/FlightsPiiProtectionProvider.cs`, `Privacy/DataProtectionBookingPassengerProtector.cs`, `Privacy/FlightsPiiKeyInitializer.cs`, `HealthChecks/FlightsPiiProtectionHealthCheck.cs`; Api `Composition/FlightsPiiKeys.cs`; Host `Commands/FlightsPiiKeysCommand.cs`; unit `Privacy/PiiProtectionTests.cs`, `Privacy/PiiKeyLifecycleTests.cs`; Host no-DB `Commands/FlightsPiiKeysCommandTests.cs`.

**Modify:** Infrastructure csproj and `FlightsInfrastructureServiceCollectionExtensions.cs`; Api Composition registration; Host `Program.cs` early command dispatch. Keep Host imports limited to Api.Composition. Do not reconfigure process-global Data Protection/auth.

**Interfaces** (paths under `modules/flights/Travel.Modules.Flights.*` unless stated otherwise):

```csharp
// Core: immutable opaque value; factory returns ErrorOr<ProtectedPassengerSnapshot>.
// Serialized fields: FormatVersion (1), Ciphertext (nonempty protected bytes encoded as text).
public interface IBookingPassengerProtector
{
    ErrorOr<ProtectedPassengerSnapshot> Protect(Guid aggregateId, Guid ownerUserId, PassengerInfo passenger);
    ErrorOr<PassengerInfo> Unprotect(Guid aggregateId, Guid ownerUserId, ProtectedPassengerSnapshot snapshot);
}
// Infrastructure private provider is reused by booking and webhook adapters only.
// Public errors: Flights.PiiProtectionUnavailable / Flights.PiiPayloadUnavailable, HTTP 503.
// Never include original cryptographic/configuration exception text in descriptions.
```

- [ ] RED tests: real protection round-trip using a generated disposable certificate/ring; serialized snapshot contains no name/DOB/gender/contact sentinels; same plaintext produces different ciphertext; wrong aggregate/owner/purpose, modified bytes and unknown format are refused. Use fixed purpose prefixes and GUID `N` format, not unvalidated user strings.
- [ ] RED tests: no configuration, unreadable certificate, missing/empty ring and inaccessible directory produce unavailable without creating a fallback ring or invoking a provider. Verify filesystem contents before/after. Protect/unprotect failure must not leak secrets through `Error.Description`, logs or exception chains.
- [ ] Implement dedicated provider configuration with stable `Travel.Flights.Pii.v1`, explicit filesystem persistence, `ProtectKeysWithCertificate` and retained read certificates. Own/dispose its private crypto-only service provider; use `IKeyManager.GetAllKeys()` to guard missing/empty/revoked-only rings before normal Protect, not an XML filename count or a call that bootstraps implicitly. Inject `TimeProvider` for owned timestamps. Use framework-authenticated encryption; Core remains dependency-free except approved primitives/ErrorOr.
- [ ] Add the **key-only** `flights-pii-keys initialize --execute` dispatch before normal Host registration, following the existing early `booking-read-model` dispatch pattern. No execute flag means no mutation. Refuse an initialized ring; validate active certificate before key creation. The command registers neither EF/Marten/Wolverine nor schema initializers and never starts HTTP. API facade delegates to Infrastructure. Unit tests assert invalid arguments, explicit execution, no replacement and sanitized output.
- [ ] RED/GREEN lifecycle tests: new provider instance decrypts prior data from the same ring; a new key can protect while retained keys decrypt old snapshots; active/read-certificate rotation supports both generations; copied ring plus retained private certificate restores into a new owned temp directory; missing key/certificate fails without fallback. No real-key reset/delete command.
- [ ] Local tests only create/delete their own randomized temp directories. Record crypto-instance restart evidence accurately; do not call it a live multi-node rollout. Deliver P3/P5 foundation.

## Task 2: protected event and pure replay

**Create:** Core `DomainEvents/OfferHeldV2.cs`. **Modify:** Core `Aggregates/BookingAggregate.cs`; Infrastructure `Marten/BookingAggregateConfig.cs`; unit `Aggregates/BookingAggregateApplyTests.cs`; integration `Marten/BookingEventCompatibilityTests.cs` and `BookingAggregateMartenTests.cs`.

**Produces:**

```csharp
public sealed record OfferHeldV2(
    string OrderId, ProtectedPassengerSnapshot PassengerSnapshot,
    DateTimeOffset HeldUntil, DateTimeOffset HeldAt, Guid OwnerUserId) : IDomainEvent
{
    public DateTimeOffset OccurredAt => HeldAt;
}
// BookingAggregate gains ProtectedPassengerSnapshot? ProtectedPassenger.
// Apply(V2): same status/order/owner/expiry/booked timestamps; Passenger = null.
// Apply(V1): same historical behavior; ProtectedPassenger = null.
```

- [ ] RED serialized-fixture tests retain V1 with/without owner and old re-quote payload behavior. Add V2 serialization/replay and aggregate decisions with no crypto provider present. Keep old event identity/namespace; test actual registered V2 identity in real Marten CI rather than guessing its alias.
- [ ] Implement the new Apply/registration. Do not decrypt, enforce today's booking rules during replay, infer owner from PII/EF, or prebuild a passenger party/provider-ID model.
- [ ] Run pure Apply and exact serialized-compatibility classes locally after fixture inspection; real Marten stream tests remain CI-only. P2 event portion complete only after those CI tests pass.

## Task 3: protect before dispatch; unprotect after guards

**Modify:** Api `Endpoints/HoldOfferEndpoint.cs`; Application `Commands/HoldOfferCommand.cs`, `Handlers/Booking/HoldOfferHandler.cs`; relevant Composition service-location policy; HTTP `FlightsApiFixture.cs`, `FlightsEndpointsHttpTests.cs`, `Documentation/ReadmeRequestExamplesTests.cs`; Flights unit/controlled handler tests and integration `Booking/HoldOfferHandlerTests.cs`.

**Interface change:** `HoldOfferCommand(Guid AggregateId, Guid UserId, ProtectedPassengerSnapshot ProtectedPassenger)` replaces the plaintext member. HTTP `HoldOfferRequest` remains unchanged. No new routing/subscription and no plaintext command adapter.

- [ ] RED HTTP tests: existing one-passenger validation and auth remain; captured bus command contains ciphertext only; protection failure returns safe 503 and does not call the bus; body/response never adds ciphertext or PII. Fake-bus fixture uses an explicit test protector, not a production fake registration.
- [ ] RED handler tests: missing envelope/invalid IDs/foreign owner/expired quote → zero decrypt/provider calls where the guard applies; failed unprotect → zero provider/save/outbox; success → original passenger reaches fake provider and the same envelope appears in V2. Legacy command JSON missing the envelope fails before side effects. New protected message serialization must lack all passenger sentinels, even if routed durably in a controlled CI fixture.
- [ ] Protect validated passenger before `InvokeAsync`; unprotect only after existing owner/state decisions. Append V2 using the already-protected snapshot through the unchanged `SaveOrConcurrencyConflictAsync` path. Avoid crypto/file operations after provider success, and preserve existing ambiguous-provider/commit behavior.
- [ ] Keep raw HTTP exact-body hashing and replay responses unchanged. No key expiry, scope, TTL, replacement-key or provider idempotency changes. Run no-DB HTTP/unit tests locally, actual commit/outbox assertions in existing CI lanes. Deliver P1/P3 and relevant P6.

## Task 4: projection, normal reads and maintenance compatibility

**Modify:** Infrastructure `Persistence/OrderReadModelEventApplier.cs`, `OrderReadModelReconciler.cs`, `OrderReadModelQueries.cs`; Application `Queries/OrderQueries.cs`; unit event-applier tests; existing integration `Booking/OrderReadModelReconcilerTests.cs`, `OrderReadModelRebuildRunnerTests.cs`, `BookingProjectionConvergenceTests.cs`, `MixedProjectionWriterCompatibilityTests.cs` and affected query/notification fixtures.

- [ ] RED tests cover V1 owned hold, V1 ownerless rejection, V2 hold, quote-before-new-hold, incremental catch-up starting **after** a V2 checkpoint, conflicting owners and corrupted row ciphertext. Assert source ciphertext is copied exactly on repeat apply/rebuild; a key provider is never needed for projection or metadata reads.
- [ ] Update both the event switch and `ValidateCheckpointOwnershipAsync` prefix scan. Keep stream versions, owner checks and materialization requirements. A malformed V2 envelope is source-invalid; no plaintext fallback. Projection equality remains structural equality of source-derived JSON.
- [ ] Remove `PassengerInfoJson` from the unused Application `OrderView` member. Normal Get/List SQL should select only required fields instead of pulling passenger JSON into DTOs. Public order DTOs, owner filters, sorting, no-store and notification rendering remain unchanged.
- [ ] Keep EF entity/config/migration/model snapshot unchanged. Update constructors/tests to the smaller internal DTO. Run pure applier/DTO tests locally; EF/Marten/rebuild and actual SQL field-selection proof run in CI. Never execute the maintenance rebuild against a local/shared database. Deliver P2/P8.

V1-derived rows retain their old plaintext representation during compatibility rebuild; this is an explicit historical exception, not a backfill/encryption claim. Test it separately from the no-plaintext assertions for new V2 bookings.

## Task 5: encrypted inbox and normalized webhook facts

**Create:** Application `Webhooks/IWebhookPayloadReader.cs`, `BookingWebhookFacts.cs`, `WebhookPiiExceptions.cs`; Infrastructure `Webhooks/ProtectedWebhookPayloadCodec.cs`, `DuffelWebhookPayloadReader.cs`; focused unit `Webhooks/ProtectedWebhookPayloadTests.cs`, `DuffelWebhookPayloadReaderTests.cs`.

**Modify:** Infrastructure `Webhooks/DuffelWebhookIngestionPort.cs`, `Persistence/WebhookInboxStore.cs`, `Diagnostics/BookingConsistencyDiagnostics.cs`; Application `Webhooks/IWebhookInboxStore.cs`, `Handlers/Webhooks/DuffelWebhookHandler.cs`; Api `Composition/BookingConsistencyHandlerPolicy.cs`; existing webhook port/handler/outbox/race/recovery integration tests.

**Interfaces:**

```csharp
public enum BookingWebhookKind { TicketsAvailable, AirlineCancelled, AirlineChanged, Ignored }
public sealed record BookingWebhookFacts(
    BookingWebhookKind Kind, string? ProviderOrderId, EquatableArray<string> TicketNumbers);
public interface IWebhookPayloadReader
{
    ErrorOr<BookingWebhookFacts> Read(WebhookInboxEntry entry);
}
// WebhookInboxEntry gains Source/EventId; StoredPayload replaces its raw-body semantic.
// New storage JSON format: travel.flights.webhook.v1; metadata + protected original bytes.
// Reader expected failures map in Application to safe typed unavailable/terminal exceptions.
```

- [ ] RED codec/reader tests: legacy raw JSON and new envelope yield the same facts; full payload including arbitrary extra passenger fields is absent from stored plaintext; original signed bytes round-trip exactly. Swapped inbox/event/type/provider-order metadata fails authenticated decoding. Unknown envelope format never takes the legacy path.
- [ ] RED ingestion tests: invalid signature precedes protection; signed duplicate remains a no-op without keys; new protection failure yields 503 and no inbox/outbox row; unique-index races retain exactly one durable message. Use existing atomic outbox integration fixtures in CI.
- [ ] Encrypt after signature/header checks and dedup, before the current transaction commit. Persist required routing metadata inside the envelope with cryptographic purpose binding. Do not add a column/migration or log payload/signature/ciphertext.
- [ ] Move provider JSON parsing into Infrastructure reader; return only normalized Application facts. Keep current aggregate ticket/refund decisions, total-amount source, notification counts/version and post-commit acknowledgement ordering. Already-processed rows skip payload reads.
- [ ] Add typed error policy: unsupported/malformed new envelope → DLQ; crypto unavailable → 1/5/30s retries then DLQ. No failure acknowledgement. Preserve current safe handling for malformed legacy raw input and current no-op semantics for recognized events lacking actionable facts; do not reinterpret missing facts as a success event.
- [ ] Update diagnostics to correlate new envelope metadata without keys; actual handler validates binding/decrypted metadata before using it. Keep existing replay allowlist and projection repair requirements. Test missing-key → unprocessed/DLQ → restored key → controlled replay → one event/notification and processed flag, all in CI with fakes. Deliver P4/P6.

## Task 6: prevent log/exception leaks and remove Travel booking storage

**Modify:** Infrastructure `Providers/Duffel/DuffelFlightBookingProvider.cs`, `Notifications/Email/MailKitEmailSender.cs`, `Notifications/KeycloakUserDirectory.cs` and relevant JSON failure paths; add safe Infrastructure exception types only where an exception must cross a framework boundary. Add unit/loopback-only capture tests.

**Frontend:** modify `apps/web/src/app/flights/flights-page.component.ts/.html` and relevant quote copy/tests; remove `flights-booking-draft.ts` and its tests after removing callers; update `flights-page.component.spec.ts` and demo E2E cases. Keep `flights-auth.service.ts`, realm/JWT contract and existing operation-memory ownership unchanged unless a concrete dependency is reported first.

- [ ] RED privacy tests inject fictional names/email/phone into provider failure bodies, JSON exceptions, SMTP exceptions and profile responses. Capture structured log state, exceptions/inner exceptions, HTTP ProblemDetails, Activity tags and controlled durable failure data; none may contain sentinels or secrets. Test failure remains failure and cancellation remains cancellation.
- [ ] Remove raw payment response logging. Sanitize exceptions **before** framework logging/persistence; a wrapper retaining the sensitive inner exception is insufficient. Do not modify notification recipient selection, payment compensation, cancellation support or retry counts except the new explicit crypto policy.
- [ ] RED browser/unit cases: real-flow stub callback has no restored quote; old Travel draft key is deleted without being read; unavailable storage does not block login; already-authenticated re-quote still works; no automatic write on callback/reload; late owner response and existing unknown-operation barrier survive. Observe no new tokens, passenger data, provider refs, aggregate booking intents or keys in storage/URLs.
- [ ] Implement memory-only Travel intent behavior and clear copy: before redirect, selection will need repeating; after callback, “Вход выполнен. Выберите рейс и проверьте цену заново.” Keep current Keycloak issuer/audience/GUID-sub/scope enforcement. Tests prove source/demo behavior, not fresh real token issuance; any live auth acceptance must verify actual claims on an independently authorized issuer without silently starting a schema-applying stack.
- [ ] Run web/API-client/**travel-e2e lint**, full fictional demo and focused error/storage tests. Do not disable Nx boundaries: load shared JSON fixtures as data through filesystem APIs where the test project requires it. Inspect desktop/360px and keyboard flow. Deliver P6/P7.

## Task 7: production composition proof, docs, review and delivery

**Tests/composition:** update affected Host fixtures to inject real temporary protection configuration only for controlled tests that need protected writes. `AspireStackSmokeTests` already sets its own webhook secret through the host resource; similarly supply owned temporary ring/certificate paths to that resource. Its generated secrets are disposable, never checked in or printed. Dispose the app before deleting test keys. The ordinary status E2E may boot with the privacy write capability unconfigured; it must not enable plaintext writes.

**Docs:** add ADR `0024-flights-pii-protection.md` after confirming the next number at execution; amend ADRs 0015/0018 and M2 roadmap to the accepted singular V2 boundary; update current-state/README/module instructions and add `docs/operations/flights-pii-key-recovery.md` plus the dated local result report. Runbook must cover explicit key-only initialization, ACLs, backups, certificate/key retention, restore validation, unavailable capability, bounded DLQ/replay and asymmetric rollout/rollback. Documentation is not evidence that those operations were performed.

- [ ] Verify early key-only dispatch does not register or invoke schema initialization. Unit proof is local; full Host/Marten/EF/outbox/durable/serialization/Aspire proof is CI-only. Do not weaken checks or insert test crypto defaults into normal Host composition.
- [ ] Self-review P1–P8 and all five Review Focus items. Run applicable safe gates below; record actual counts and omitted/CI-only checks. Obtain an independent whole-change review and resolve important findings with failing regressions before publication.
- [ ] Under the user's post-approval lifecycle authorization, commit **all** implementation/tests/docs, push, create and attach PR into dev; provide `/pull/<number>/changes` for user review. Wait for every mandatory CI job on the actual HEAD; paid AI-evals remain skipped. No admin/bypass merge.
- [ ] Merge only after checks/review rules pass. Verify closed/merged PR and SHA, fetch/fast-forward clean primary dev, verify task ancestry/content and no unpublished commits, then archive only the owned managed worktree and remove only the task's local/remote branches/temp artifacts. Preserve needed visual evidence outside the worktree first. Do not announce completion with local-only documents.

### Local verification selection after fixture inspection

```powershell
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit/Travel.Modules.Flights.Tests.Unit.csproj --configuration Release --maxcpucount:1
dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration/Travel.Modules.Flights.Tests.Integration.csproj --configuration Release --maxcpucount:1 --filter FullyQualifiedName~BookingEventCompatibilityTests
dotnet test tests/Travel.Host.Tests.Integration/Travel.Host.Tests.Integration.csproj --configuration Release --maxcpucount:1 --filter "FullyQualifiedName~FlightsPiiKeysCommandTests|FullyQualifiedName~FlightsEndpointsHttpTests|FullyQualifiedName~ReadmeRequestExamplesTests"
dotnet test tests/Travel.Tests.Architecture/Travel.Tests.Architecture.csproj --configuration Release --maxcpucount:1
dotnet test tests/Travel.Tests.Contract/Travel.Tests.Contract.csproj --configuration Release --maxcpucount:1
npx.cmd nx test api-client --skipNxCache
npx.cmd nx test web --skipNxCache --watch=false
npx.cmd nx run-many -t lint -p web api-client travel-e2e
npm.cmd run test:flights-demo
npx.cmd nx build web --configuration=production
npx.cmd nx build web --configuration=flights-demo
npm.cmd run check:ai-harness
npm.cmd run check:dotnet-inventory
npm.cmd run check:readme-examples
dotnet csharpier check .
npx.cmd biome ci .
git diff --check
```

The new key-command/crypto unit tests may use only temporary local files, no normal Host startup. Reinspect class/collection fixtures before selecting any test. Do not run a broad integration filter, `dotnet test Travel.slnx`, actual `flights-pii-keys initialize`, Host/AppHost or local rebuild/migration commands as a shortcut. Key/schema/deployment operations remain distinct authorizations.

## Self-review coverage

P1 → Tasks 1/3/4; P2 → Tasks 2/4; P3 → Tasks 1/3; P4 → Task 5; P5 → Tasks 1/7; P6 → Tasks 3/5/6; P7 → Task 6; P8 → Tasks 0/4/7. No placeholder requires a guessed production secret, database, provider call or schema change. Later multi-passenger/provider-ID work is explicitly absent. The two items requiring user acceptance now are the singular V2 boundary and the memory-only redirect/reselection trade-off, together with the overall specification and implementation plan.
