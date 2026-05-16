# Flights M1 Remediation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close every Critical, Important and Minor finding from the 8-agent audit of the `flights-m1` branch so it passes acceptance criteria §21 of the M1 design spec with no regressions.

**Architecture:** Modular monolith (`Travel.Host`). The remediation rests on 4 cross-cutting fixes: (1) a real transactional outbox — Marten↔Wolverine for booking handlers, EF↔Wolverine for the webhook endpoint; (2) DB-arbitrated concurrency — Marten optimistic versioning, `23505` unique-violation handling, an in-flight idempotency row; (3) a Duffel webhook HMAC verifier built to the *verified* Duffel v2 scheme; (4) per-typed-client Polly resilience tuned to spec §19. The rest are localized correctness, observability, test-coverage and documentation fixes.

**Tech Stack:** .NET 10, Wolverine 5 + Marten 8 + WolverineFx.Http, EF Core 10 + Npgsql, PostgreSQL 17, Redis, NATS, OpenTelemetry, Polly (`Microsoft.Extensions.Http.Resilience`), MailKit, `Microsoft.Extensions.AI` + Anthropic, xUnit v3 + Testcontainers + ArchUnitNET + Shouldly + Alba + WireMock.NET.

**Design reference:** [`docs/superpowers/specs/2026-05-14-flights-m1-remediation-design.md`](../specs/2026-05-14-flights-m1-remediation-design.md) — workstreams WS0–WS10, decisions D1–D8, architectural approaches A1–A4. The original M1 spec [`2026-05-13-flights-m1-design.md`](../specs/2026-05-13-flights-m1-design.md) remains the source of truth for behaviour; section refs (§N) below point to it.

**Audit reference:** the 8 code-review reports (agent codes DOM/DUF/SRCH/SAGA/WHK/NOB/API/PER/BLD) over range `02508ac..20c2c6e`. Each task cites the finding it closes.

---

## Conventions

- **Working directory:** `d:\_Projects\_github\travel-agency`. All paths relative to this root. Shell: PowerShell.
- **Branch:** continue on `flights-m1`.
- **TDD:** every task starts with a failing test. Run it, see it fail, implement, run, see it pass, commit. Where a task is pure config/docs with no runtime behaviour, the "test" is the build + the relevant existing suite staying green (stated explicitly in that task).
- **Commit format:** Conventional Commits. Allowed scopes: `flights`, `host`, `ai`, `shared`, `arch`, `test`, `docs`, `infra`, `ci`. End commit messages with `Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>`.
- **`TimeProvider`:** inject everywhere; never `DateTime.UtcNow`/`DateTimeOffset.UtcNow`/`DateTime.Now` in production code. Tests use `FakeTimeProvider`.
- **Result type:** `ErrorOr<T>` for handler returns; map to `ProblemDetails` via `Travel.Shared.Web.ErrorOrExtensions.ToProblemDetails`.
- **Build gate:** `dotnet build Travel.slnx` must stay at **0 warnings** (solution-wide `TreatWarningsAsErrors`). Run it after any non-trivial task.
- **Test commands:** unit/arch — no Docker. Integration — needs Docker (Testcontainers). If a step's integration test cannot run locally for lack of Docker, state that explicitly; do not mark the task done on a skipped test.
- **Phase order:** WS0 → WS1 sequentially (foundation). WS2–WS9 may run in parallel (subagent-driven). WS10 last. Code-review subagent after each workstream.

---

# Phase WS0 — Architecture guard rails + `DateTime.UtcNow`

Closes: DOM-C1, PER-C1, PER-C2, PER-C3, BLD-I1. **Runs first** — the new arch rules guard all later work.

## Task 0.1: Arch rule — no `DateTime.UtcNow` in Flights Core/Application/Infrastructure

**Files:**
- Modify: `tests/Travel.Tests.Architecture/Flights/FlightsArchitectureTests.cs`

- [ ] **Step 1: Write the failing test.** Append to `FlightsArchitectureTests.cs` (inside the class):

```csharp
// ─── Test 9: no ambient clock in Flights production code ──────────────────
// Spec §17/§19: production code must use injected TimeProvider, never DateTime.UtcNow.

[Theory]
[InlineData(typeof(DateTime), "get_UtcNow")]
[InlineData(typeof(DateTime), "get_Now")]
[InlineData(typeof(DateTimeOffset), "get_UtcNow")]
[InlineData(typeof(DateTimeOffset), "get_Now")]
public void Flights_production_code_does_not_use_ambient_clock(Type clock, string getter)
{
    Classes()
        .That()
        .ResideInNamespaceMatching(@"Travel\.Modules\.Flights\.(Core|Application|Infrastructure).*")
        .Should()
        .NotCallMethod(clock, getter)
        .Check(Arch);
}
```

If ArchUnitNET 0.13.x exposes `NotCallMethod` with a different signature, adapt to the available API (e.g. `NotCallMethod(MethodMembers().That().HaveName(getter)...)`); the *intent* — fail if any Flights production class reads `DateTime.UtcNow`/`Now` / `DateTimeOffset.UtcNow`/`Now` — is fixed. No `.WithoutRequiringPositiveResults()` here: the rule must be live.

- [ ] **Step 2: Run — expect FAIL.**

```powershell
dotnet test tests/Travel.Tests.Architecture --filter "FullyQualifiedName~Flights_production_code_does_not_use_ambient_clock"
```

Expected: FAIL — `PassengerInfo` calls `DateTime.UtcNow` (`PassengerInfo.cs:57`). (If it does not yet fail, the rule is not wired correctly — fix the rule before proceeding.)

- [ ] **Step 3: Leave the test failing.** It is fixed by Task 0.4. Do NOT commit a red test alone — Task 0.1 and Task 0.4 commit together (see Task 0.4 Step 5). Proceed to Task 0.2.

## Task 0.2: Arch rule — Marten used only in Flights and Trips

**Files:**
- Modify: `tests/Travel.Tests.Architecture/Flights/FlightsArchitectureTests.cs`

- [ ] **Step 1: Write the test.** Append:

```csharp
// ─── Test 10: Marten isolation ────────────────────────────────────────────
// Spec §17: Marten (event store) is used only by Flights and Trips modules.

[Fact]
public void Only_Flights_and_Trips_depend_on_Marten()
{
    Classes()
        .That()
        .ResideInNamespaceMatching(@"Travel\.Modules\.(Hotels|Rail|Identity)\..*")
        .Should()
        .NotDependOnAnyTypesThat()
        .ResideInNamespaceMatching(@"(Marten|JasperFx)\..*")
        .Check(Arch);
}
```

- [ ] **Step 2: Run — expect PASS** (no Hotels/Rail/Identity code touches Marten today).

```powershell
dotnet test tests/Travel.Tests.Architecture --filter "FullyQualifiedName~Only_Flights_and_Trips_depend_on_Marten"
```

If it fails on an empty-subject error, add `Classes().That().ResideInNamespaceMatching(...).Should().Exist()` companion or accept the rule is dormant until those modules have classes — but prefer the rule live. Commit happens in Task 0.3.

## Task 0.3: Remove vacuous `.WithoutRequiringPositiveResults()` where subjects exist

**Files:**
- Modify: `tests/Travel.Tests.Architecture/Flights/FlightsArchitectureTests.cs`
- Modify: `tests/Travel.Tests.Architecture/ArchitectureTestBase.cs` (comment at lines 16-17 is now stale — update it)

- [ ] **Step 1:** In `FlightsArchitectureTests.cs`, the Flights `Core`/`Application`/`Infrastructure`/`DomainEvents`/`ValueObjects`/`Duffel.Dto`/`Travelpayouts.Dto` namespaces now all contain real classes. Remove `.WithoutRequiringPositiveResults()` from Tests 1–3, 5, 6, 7 (the `Flights_Core_*`, `Flights_Application_*`, `Flights_Infrastructure_*`, `Flights_domain_events_*`, `Flights_value_objects_*`, `*_Dto` facts). **Keep** it on Tests 4 (`Flights_module_does_not_depend_on_Hotels/Rail/Trips`) — those target modules may still be empty scaffolds; instead add a companion existence assert is not needed since the *subject* (`Travel.Modules.Flights.*`) is non-empty — so these can also drop the flag. Verify by running. Net: drop `.WithoutRequiringPositiveResults()` from every rule whose `.That()` subject is `Travel.Modules.Flights.*` (all of Tests 1-7); keep only where the *subject* namespace could be empty.

- [ ] **Step 2:** Update the stale guidance comment in `ArchitectureTestBase.cs:16-17` to: `// Subproject 1 (Flights) has real classes — Flights rules no longer use .WithoutRequiringPositiveResults(). Other modules' rules stay permissive until those modules are implemented.`

- [ ] **Step 3: Run the whole arch suite — expect all green except the Task 0.1 ambient-clock test (still red until Task 0.4).**

```powershell
dotnet test tests/Travel.Tests.Architecture --filter "Category=Architecture"
```

- [ ] **Step 4:** Do not commit yet — Task 0.4 fixes the one red test, then commits 0.1–0.4 together.

## Task 0.4: Fix `PassengerInfo.Create` — remove `DateTime.UtcNow`

**Files:**
- Modify: `modules/flights/Travel.Modules.Flights.Core/ValueObjects/PassengerInfo.cs`
- Modify: every caller of `PassengerInfo.Create` (find with grep — known callers: `modules/flights/Travel.Modules.Flights.Api/Contracts/Contracts.cs` mapper, `HoldOfferHandler`, test builders)
- Test: `tests/flights/Travel.Modules.Flights.Tests.Unit/ValueObjects/PassengerInfoTests.cs`

- [ ] **Step 1: Write/extend the failing test.** In `PassengerInfoTests.cs` add a test that pins the clock and asserts a DOB equal to "today" is accepted and a DOB of "tomorrow" is rejected — using an explicit date, not the ambient clock:

```csharp
[Fact]
public void Create_rejects_date_of_birth_after_the_supplied_today()
{
    var today = new DateOnly(2026, 5, 14);
    var tomorrow = today.AddDays(1);

    var result = PassengerInfo.Create(
        "Ann", "Lee", tomorrow, Gender.Female, "ann@example.com",
        PhoneNumber.Create("+79161234567").Value, today);

    result.IsError.ShouldBeTrue();
    result.FirstError.Code.ShouldBe("PassengerInfo.DateOfBirthFuture");
}

[Fact]
public void Create_accepts_date_of_birth_equal_to_the_supplied_today()
{
    var today = new DateOnly(2026, 5, 14);

    var result = PassengerInfo.Create(
        "Ann", "Lee", today, Gender.Female, "ann@example.com",
        PhoneNumber.Create("+79161234567").Value, today);

    result.IsError.ShouldBeFalse();
}
```

- [ ] **Step 2: Run — expect compile FAIL** (`Create` has no `today` parameter yet).

- [ ] **Step 3: Implement.** Change `PassengerInfo.Create` signature to add a trailing `DateOnly today` parameter and replace line 57:

```csharp
public static ErrorOr<PassengerInfo> Create(
    string givenName,
    string familyName,
    DateOnly dateOfBirth,
    Gender gender,
    string email,
    PhoneNumber phone,
    DateOnly today)
{
    // ... unchanged GivenName/FamilyName checks ...

    if (dateOfBirth > today)
        return Error.Validation(
            "PassengerInfo.DateOfBirthFuture",
            "Date of birth must not be in the future.");

    // ... unchanged email check + return ...
}
```

Update every caller: production callers compute `today` from injected `TimeProvider` — `DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime)`. The DTO mapper in `Contracts.cs` must receive a `TimeProvider` (thread it from the endpoint, which already has access). `HoldOfferHandler` already injects `TimeProvider`. Update test builders to pass an explicit date.

- [ ] **Step 4: Run — expect PASS** for the new tests, the full `PassengerInfoTests`, and the Task 0.1 ambient-clock arch test:

```powershell
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit --filter "FullyQualifiedName~PassengerInfoTests"
dotnet test tests/Travel.Tests.Architecture --filter "Category=Architecture"
dotnet build Travel.slnx
```

All green, 0 warnings.

- [ ] **Step 5: Commit WS0.**

```powershell
git add tests/Travel.Tests.Architecture/ modules/flights/ tests/flights/
git commit -m "fix(flights): remove ambient clock from PassengerInfo; add arch rules for clock + Marten isolation"
```

---

# Phase WS1 — Transactional outbox infrastructure (A1)

Closes: SAGA-C1 (infra), WHK-C1 (infra). **Runs after WS0, before WS2/WS3/WS6.**

## Task 1.1: Investigate the existing Foundation Wolverine setup

**Files:** read-only investigation.

- [ ] **Step 1:** Read `apps/Travel.Host/Program.cs` (Wolverine block lines 65-83), the Marten registration (lines 41-46), and search the Foundation for any existing `IntegrateWithWolverine`, `AddDbContextWithWolverineIntegration`, `[Transactional]`, durability/outbox config:

```powershell
Select-String -Path "apps/**/*.cs","shared/**/*.cs" -Pattern "IntegrateWithWolverine|WolverineIntegration|PersistMessagesWithMarten|AutoApplyTransactions|\[Transactional\]"
```

- [ ] **Step 2:** Write findings as a short comment block at the top of the WS1 work or in the PR description: does Wolverine already have a durability backing store? Is NATS the only transport? Confirm `WolverineFx.Marten` and `WolverineFx.EntityFrameworkCore` packages are referenced (check `Directory.Packages.props` and the relevant `.csproj`); if not, they must be added in Task 1.2/1.3. **No commit** — this gates the next two tasks.

## Task 1.2: Marten ↔ Wolverine transactional outbox

**Files:**
- Modify: `apps/Travel.Host/Program.cs`
- Modify: `Directory.Packages.props` + `apps/Travel.Host/Travel.Host.csproj` (add `WolverineFx.Marten` if missing)
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/Marten/MartenWolverineOutboxTests.cs` (new)

- [ ] **Step 1: Write the failing integration test.** With a Postgres Testcontainer + Wolverine host, append an event to a `BookingAggregate` stream *and* enqueue a message through the Marten-enrolled session in one `SaveChangesAsync`; assert the message is delivered (use a recording handler / Wolverine tracked session `IHost.InvokeMessageAndWaitAsync` or `TrackActivity`). Test name: `Marten_session_acts_as_transactional_outbox`. (If a full Wolverine host fixture already exists for integration tests, reuse it; otherwise add an `IClassFixture` that boots the real `Program`.)

- [ ] **Step 2: Run — expect FAIL** (messages published via `IDocumentSession` are not currently enrolled).

- [ ] **Step 3: Implement.** In `Program.cs` Wolverine config block, add Marten integration. Change the Marten registration (lines 41-46) to chain `.IntegrateWithWolverine()` and enable auto-transactions in the `UseWolverine` block:

```csharp
builder
    .Services.AddMarten(opts =>
    {
        opts.Connection(builder.Configuration.GetConnectionString("travel")!);
        opts.ConfigureFlightsBooking();
    })
    .UseLightweightSessions()
    .IntegrateWithWolverine();
```

```csharp
builder.Host.UseWolverine(opts =>
{
    opts.UseNats(natsUrl);
    opts.Policies.AutoApplyTransactions();           // handlers wrap in a transaction
    opts.Policies.UseDurableLocalQueues();           // durable local outbox
    // ... existing PublishMessage / Discovery config ...
});
```

- [ ] **Step 4: Run — expect PASS.** Also run the existing booking integration tests — they must stay green.

```powershell
dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration --filter "FullyQualifiedName~Marten"
```

- [ ] **Step 5: Commit.**

```powershell
git add apps/Travel.Host/ Directory.Packages.props tests/flights/
git commit -m "feat(host): enroll Marten as Wolverine transactional outbox"
```

## Task 1.3: EF (`FlightsDbContext`) ↔ Wolverine transactional outbox

**Files:**
- Modify: `apps/Travel.Host/Program.cs`
- Modify: `Directory.Packages.props` + `apps/Travel.Host/Travel.Host.csproj` (add `WolverineFx.EntityFrameworkCore` if missing)
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/Webhooks/EfWolverineOutboxTests.cs` (new)

- [ ] **Step 1: Write the failing test.** With Postgres Testcontainer: in one `FlightsDbContext` transaction, insert a `WebhookInboxEntity` and publish `ProcessDuffelWebhookCommand` via the EF-enrolled outbox; assert both committed atomically and the command was delivered. Test name: `FlightsDbContext_acts_as_transactional_outbox`.

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement.** In `Program.cs`, replace the `builder.AddNpgsqlDbContext<FlightsDbContext>(...)` call's registration so Wolverine knows about it — add after it:

```csharp
builder.Services.AddDbContextWithWolverineIntegration<FlightsDbContext>();
```

(If `AddNpgsqlDbContext` already registers the context, use the Wolverine EF integration form that *augments* an existing registration — `opts.UseEntityFrameworkCoreTransactions()` inside `UseWolverine`, plus the Aspire `AddNpgsqlDbContext`. Pick whichever the WolverineFx.EntityFrameworkCore version supports; the goal is `[Transactional]` on a handler that takes `FlightsDbContext` commits the DbContext + outbox together.) Verify against the Task 1.1 findings.

- [ ] **Step 4: Run — expect PASS.** Existing webhook integration tests stay green.

- [ ] **Step 5: Commit.**

```powershell
git add apps/Travel.Host/ Directory.Packages.props tests/flights/
git commit -m "feat(host): enroll FlightsDbContext as Wolverine transactional outbox"
```

## Task 1.4: Crash-safety integration test

**Files:**
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/Webhooks/OutboxCrashSafetyTests.cs` (new)

- [ ] **Step 1: Write the test.** Simulate the persist-then-publish gap: enroll the EF outbox, persist the inbox row, throw before Wolverine flushes the outgoing message buffer, then restart the Wolverine durability agent and assert the message is eventually delivered (Wolverine durable outbox recovery). If full crash simulation is impractical, assert instead that with `AutoApplyTransactions` an *exception thrown after the publish call but before handler return* rolls back the inbox row too (atomicity in the other direction). Test name: `Webhook_inbox_and_outbox_message_commit_atomically`.

- [ ] **Step 2-4:** Run (FAIL if atomicity broken — should PASS given Tasks 1.2/1.3), then commit.

```powershell
git add tests/flights/
git commit -m "test(flights): assert webhook inbox + outbox commit atomically"
```

---

# Phase WS2 — Booking saga correctness

Closes: SAGA-C1/C2/C3, SAGA-I1–I8 + minors, DOM-I1, DOM-I7. **Depends on WS1.** Files: `modules/flights/Travel.Modules.Flights.Application/Handlers/Booking/*`, `Commands/*`, `Core/Aggregates/BookingAggregate.cs`, `Infrastructure/Persistence/OrderReadModelProjectorImpl.cs`, `Api/Middleware/IdempotencyKeyMiddleware.cs`, `Infrastructure/Persistence/Repositories/IdempotencyStore.cs`.

## Task 2.1: Marten optimistic concurrency in booking command handlers

**Files:**
- Modify: `ConfirmOrderHandler.cs`, `HoldOfferHandler.cs`, `CancelOrderHandler.cs`, `QuoteOfferHandler.cs`
- Modify: `Core/Errors/FlightsErrors.cs` (add `ConcurrencyConflict`)
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/BookingConcurrencyTests.cs` (new)

- [ ] **Step 1: Write the failing test.** Two concurrent `ConfirmOrderCommand` against the same `Held` stream (distinct idempotency keys so the middleware doesn't dedupe): exactly one succeeds, the other returns `Flights.ConcurrencyConflict`; the stream has exactly one `OrderConfirmed` event; payment `CaptureAsync` was invoked exactly once. Use a fake `IPaymentGateway`/`IFlightBookingProvider` that records call counts. Test name: `Concurrent_confirm_appends_only_once`.

- [ ] **Step 2: Run — expect FAIL** (today both confirms append; capture runs twice).

- [ ] **Step 3: Implement.** Add to `FlightsErrors.cs`:

```csharp
public static Error ConcurrencyConflict =>
    Error.Conflict("Flights.ConcurrencyConflict", "The booking was modified concurrently; retry.");
```

In each booking handler, replace `marten.Events.AggregateStreamAsync<BookingAggregate>(id)` + `marten.Events.Append(id, ...)` + `marten.SaveChangesAsync()` with the optimistic-write pattern. For `ConfirmOrderHandler`:

```csharp
// Load with version tracking
var stream = await marten.Events.FetchForWriting<BookingAggregate>(cmd.AggregateId, ct);
var agg = stream.Aggregate;
if (agg is null)
    return FlightsErrors.OfferNotFound(cmd.AggregateId.ToString());
if (agg.Status != BookingStatus.Held)
    return Error.Conflict("Flights.InvalidState", $"Cannot confirm in state {agg.Status}.");

// ... payment + provider calls unchanged ...

stream.AppendOne(new PaymentAuthorized(...));
stream.AppendOne(new OrderConfirmed(...));
try
{
    await marten.SaveChangesAsync(ct);
}
catch (Marten.Exceptions.ConcurrencyException)
{
    return FlightsErrors.ConcurrencyConflict;
}
```

`FetchForWriting` enforces the expected version captured at load time. Apply the same pattern to `HoldOfferHandler`, `CancelOrderHandler` and `QuoteOfferHandler`'s re-quote path (Task 2.3). Map `Flights.ConcurrencyConflict` to HTTP 409 — verify `ToProblemDetails` already maps `Error.Conflict` to 409 (it does).

> **Note for the executor:** the compensation branches in `ConfirmOrderHandler` (capture-fail, confirm-fail) currently do a *second and third* `AggregateStreamAsync` re-load to project. With `FetchForWriting` you already hold `stream.Aggregate`; after `SaveChangesAsync` re-fetch once for projection or apply the appended events to a local copy. Eliminate the redundant reloads (SAGA-M2).

- [ ] **Step 4: Run — expect PASS.** Run the full `Booking/` integration folder — all green.

- [ ] **Step 5: Commit.**

```powershell
git add modules/flights/ tests/flights/
git commit -m "fix(flights): add Marten optimistic concurrency to booking command handlers"
```

## Task 2.2: Transactional notification publish via outbox

**Files:**
- Modify: `ConfirmOrderHandler.cs`, `CancelOrderHandler.cs`
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/ConfirmOrderHandlerTests.cs` (extend)

- [ ] **Step 1: Write the failing test.** Confirm an order through the real Wolverine host; assert `OrderConfirmedNotification` is delivered AND was published through the outbox (i.e. if the handler's transaction rolls back, the notification is not delivered). Add a companion test: a handler exception after the events are appended but before return leaves *no* `OrderConfirmed` in the stream AND *no* notification delivered. Test names: `Confirm_publishes_notification_through_outbox`, `Confirm_rollback_suppresses_notification`.

- [ ] **Step 2: Run — expect FAIL** (`bus.PublishAsync` at `ConfirmOrderHandler.cs:148` is non-transactional).

- [ ] **Step 3: Implement.** With `AutoApplyTransactions` (WS1), the handler's `IMessageBus` is already enrolled in the Marten session. Two options — prefer **cascaded return messages** for clarity:

Change `ConfirmOrderHandler.Handle` to return a tuple of `(ErrorOr<ConfirmedOrderResult>, OrderConfirmedNotification)` is awkward with `ErrorOr`; instead keep `IMessageBus bus` but rely on it being outbox-enrolled — with `AutoApplyTransactions` the `bus.PublishAsync` call is buffered and flushed only on transaction commit. Verify this is the case in the Task 1.2 test. If Wolverine's `IMessageBus` is *not* auto-enrolled for a `[WolverineHandler]` static method, switch the parameter to `IMessageContext` (which *is* enrolled) and call `context.PublishAsync(...)`. Move the publish call to *before* `SaveChangesAsync` is not required — with the outbox, ordering within the handler doesn't matter; atomicity does. Remove the now-incorrect comment "8. Publish notification" implying fire-and-forget.

Same change in `CancelOrderHandler.cs`.

- [ ] **Step 4: Run — expect PASS.** Full `Booking/` folder green.

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): publish booking notifications through the transactional outbox"
```

## Task 2.3: Implement `OfferReQuoted` re-quote path (D1)

**Files:**
- Modify: `Application/Commands/QuoteOfferCommand.cs`
- Modify: `Application/Handlers/Booking/QuoteOfferHandler.cs`
- Modify: `Api/Endpoints/QuoteOfferEndpoint.cs`, `Api/Contracts/Contracts.cs` (`QuoteOfferRequest`)
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/QuoteOfferHandlerTests.cs` (extend)

- [ ] **Step 1: Write the failing test.** Quote an offer (creates stream in `OfferQuoted`), then quote *again* passing the returned `AggregateId`: assert no *new* stream is created, the existing stream gains an `OfferReQuoted` event, and `BookingAggregate.TotalAmount` reflects the new amount. A re-quote on a stream not in `OfferQuoted` (e.g. `Held`) returns `Flights.InvalidState`. Test names: `Requote_with_aggregate_id_appends_OfferReQuoted`, `Requote_on_non_quoted_stream_is_rejected`.

- [ ] **Step 2: Run — expect compile FAIL** (`QuoteOfferCommand` has no `AggregateId`).

- [ ] **Step 3: Implement.** Add `Guid? AggregateId` to `QuoteOfferCommand` and `QuoteOfferRequest` (optional, default null — forward-compatible). In `QuoteOfferHandler`:

```csharp
if (cmd.AggregateId is { } existingId)
{
    var stream = await marten.Events.FetchForWriting<BookingAggregate>(existingId, ct);
    var agg = stream.Aggregate;
    if (agg is null) return FlightsErrors.OfferNotFound(existingId.ToString());
    if (agg.Status != BookingStatus.OfferQuoted)
        return Error.Conflict("Flights.InvalidState", $"Cannot re-quote in state {agg.Status}.");

    var refreshed = await provider.RefreshOfferAsync(agg.ProviderOfferRef!, ct);
    if (refreshed.IsError) return refreshed.Errors;

    stream.AppendOne(new OfferReQuoted(
        agg.OfferId!.Value, agg.TotalAmount!, refreshed.Value.TotalAmount, time.GetUtcNow()));
    try { await marten.SaveChangesAsync(ct); }
    catch (Marten.Exceptions.ConcurrencyException) { return FlightsErrors.ConcurrencyConflict; }
    // project + return QuotedOfferResult with existingId
}
// else: existing StartStream path (new aggregate)
```

The endpoint passes `request.AggregateId` into the command.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "feat(flights): implement OfferReQuoted re-quote path on existing streams"
```

## Task 2.4: Forbid cancel from `Ticketed`

**Files:**
- Modify: `Core/Aggregates/BookingAggregate.cs` (`GuardCanCancel`)
- Modify: `Application/Handlers/Booking/CancelOrderHandler.cs`
- Test: `tests/flights/Travel.Modules.Flights.Tests.Unit/Aggregates/BookingAggregateApplyTests.cs` (extend), `tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/CancelOrderHandlerTests.cs` (extend)

- [ ] **Step 1: Write the failing tests.** Unit: a `BookingAggregate` in `Ticketed` state — `GuardCanCancel()` throws `InvalidBookingStateException`. Integration: `CancelOrderCommand` against a `Ticketed` stream returns `Flights.OrderNotCancellable` and appends no `OrderCancelled` event. Test names: `GuardCanCancel_rejects_Ticketed`, `Cancel_on_ticketed_order_is_rejected`.

- [ ] **Step 2: Run — expect FAIL** (`GuardCanCancel` at `BookingAggregate.cs:108-114` allows `Ticketed`).

- [ ] **Step 3: Implement.** `BookingAggregate.GuardCanCancel`:

```csharp
public void GuardCanCancel()
{
    if (Status is BookingStatus.Cancelled or BookingStatus.Refunded or BookingStatus.Ticketed)
        throw new InvalidBookingStateException(
            $"Cannot cancel when booking is in state {Status}.");
}
```

In `CancelOrderHandler`, after loading the aggregate, return `FlightsErrors.OrderNotCancellable($"Order in state {agg.Status} cannot be cancelled.")` when `agg.Status is BookingStatus.Ticketed` (in addition to the existing `Cancelled`/`Refunded` short-circuit), and call `agg.GuardCanCancel()` before appending (wrap the `InvalidBookingStateException` into the typed error, or rely on the explicit status check — be consistent with the other handlers).

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): forbid cancelling a ticketed order (spec §4.1)"
```

## Task 2.5: `OrderReadModelProjectorImpl` — timestamps from events, replay-safe

**Files:**
- Modify: `Infrastructure/Persistence/OrderReadModelProjectorImpl.cs`
- Modify: `Core/Aggregates/BookingAggregate.cs` (expose `TicketedAt`/`CancelledAt`/`RefundedAt`/`ConfirmedAt`/`BookedAt` from the events)
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/OrderReadModelProjectorTests.cs` (new or extend)

- [ ] **Step 1: Write the failing test.** Project a stream containing `OrderCancelled` with a known `CancelledAt`; then project the *same* stream again (replay) with the `TimeProvider` advanced; assert `OrderReadModelEntity.CancelledAt` still equals the event's timestamp, not the second projection time. Test name: `Projection_uses_event_timestamps_and_is_replay_stable`.

- [ ] **Step 2: Run — expect FAIL** (`OrderReadModelProjectorImpl.cs:49-54` stamps `time.GetUtcNow()`).

- [ ] **Step 3: Implement.** Add nullable timestamp properties to `BookingAggregate` populated in the `Apply` methods from the event payloads — `BookedAt` (from `OfferHeld.HeldAt` or first event), `ConfirmedAt` (`OrderConfirmed.ConfirmedAt`), `TicketedAt` (`OrderTicketed.TicketedAt`), `CancelledAt` (`OrderCancelled.CancelledAt`), `RefundedAt` (`OrderRefunded.RefundedAt`). Then `OrderReadModelProjectorImpl.Project` copies those straight from the aggregate; it no longer needs `TimeProvider` for timestamps. Keep `time` only if used elsewhere; otherwise drop the parameter (update callers).

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): project read-model timestamps from events, not the projection clock"
```

## Task 2.6: Idempotency middleware hardening (A2)

**Files:**
- Modify: `Api/Middleware/IdempotencyKeyMiddleware.cs`
- Modify: `Infrastructure/Persistence/Repositories/IdempotencyStore.cs`
- Modify: `Application/Idempotency/IIdempotencyStore.cs` (if signature changes)
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/Idempotency/IdempotencyKeyMiddlewareTests.cs` (extend)

- [ ] **Step 1: Write the failing tests.**
  - `Concurrent_first_time_requests_execute_once`: two concurrent requests, same key, same body — exactly one reaches the handler; the other gets 409 (in-progress) or the cached 2xx.
  - `Failure_responses_are_not_cached`: a request whose handler returns 500 — a retry with the same key re-executes the handler (not a replayed 500).
  - `Hash_includes_http_method`: same route/user/body but different HTTP method produce different keys (defensive — all current routes are POST, but pin it).
  Test names as above.

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement.**
  - **In-flight row:** before invoking `next(ctx)`, `IdempotencyStore.TryBeginAsync(key, ...)` inserts a row with `ResponseHash = null`, `ResponseStatus = null`. The unique constraint on the PK (`key`) makes a concurrent insert throw `DbUpdateException` (Npgsql `PostgresException.SqlState == "23505"`) → middleware returns `Results.Conflict()` with a ProblemDetails "request already in progress" (or, if a *completed* row exists, replay it). After the handler completes, `IdempotencyStore.CompleteAsync(key, status, responseHash, body)` updates the row — **only when `status` is 2xx**; if non-2xx, `IdempotencyStore.AbandonAsync(key)` deletes the in-flight row so a retry can proceed.
  - **Method in hash:** include `ctx.Request.Method` in the hash input alongside route/user/body (`IdempotencyKeyMiddleware.cs:29-34`).
  - **Route targeting:** replace `p.Contains("/cancel")` etc. (`IdempotencyKeyMiddleware.cs:91-101`) with `EndsWith` / a route-pattern check so `/orders/cancel/foo` cannot match.

- [ ] **Step 4: Run — expect PASS.** Full `Idempotency/` folder green.

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): idempotency — in-flight row, 2xx-only cache, method in hash"
```

## Task 2.7: `HoldOfferHandler` — propagate real `FareConditions`

**Files:**
- Modify: `Core/DomainEvents/OfferQuoted.cs` (add `FareConditions`), `Core/Aggregates/BookingAggregate.cs` (`Apply(OfferQuoted)` stores it)
- Modify: `Application/Handlers/Booking/QuoteOfferHandler.cs` (write it), `HoldOfferHandler.cs` (read it instead of fabricating)
- Modify: `Infrastructure/Marten/BookingAggregateConfig.cs` if event registration needs nothing — it doesn't, just payload change
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/HoldOfferHandlerTests.cs` (extend)

- [ ] **Step 1: Write the failing test.** Quote an offer with non-default `FareConditions` (e.g. `ChangeAllowed = true`); hold it; assert the `BookableOffer` handed to `IFlightBookingProvider.HoldOfferAsync` carries the *same* `FareConditions`, not `new FareConditions(false, false, null, null)`. Test name: `Hold_uses_fare_conditions_captured_at_quote`.

- [ ] **Step 2: Run — expect FAIL** (`HoldOfferHandler.cs:53-62` fabricates).

- [ ] **Step 3: Implement.** Add `FareConditions FareConditions` to the `OfferQuoted` record. `QuoteOfferHandler` populates it from the refreshed `BookableOffer`. `BookingAggregate.Apply(OfferQuoted)` stores it in a new `FareConditions? FareConditions` property. `HoldOfferHandler` reads `agg.FareConditions!` instead of fabricating. (This is an event-schema change — acceptable: no production streams exist yet. Note in the commit body.)

- [ ] **Step 4: Run — expect PASS.** Run `Marten/BookingAggregateMartenTests` — event round-trips.

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): carry FareConditions from quote into hold"
```

## Task 2.8: `BookingAggregate` — `Version` support + terminal-state Apply tests

**Files:**
- Modify: `Core/Aggregates/BookingAggregate.cs`
- Test: `tests/flights/Travel.Modules.Flights.Tests.Unit/Aggregates/BookingAggregateApplyTests.cs` (extend), `tests/flights/Travel.Modules.Flights.Tests.Integration/Marten/BookingAggregateMartenTests.cs` (extend)

- [ ] **Step 1: Write the tests.**
  - Integration: rebuild a stream of N events via `AggregateStreamAsync`; assert `agg.Version == N` and `agg.Id == streamId` (Marten populates these by convention — confirm the convention works; if `Version` stays 0, add the metadata hook).
  - Unit: document `Apply` is intentionally unguarded — a corrupt sequence `[OrderCancelled, OrderTicketed]` leaves `Status == Ticketed`; assert that, with a comment that terminal enforcement is the handler's/`Guard`'s job. Test name: `Apply_is_unguarded_terminal_enforcement_is_in_guards`.

- [ ] **Step 2: Run** — `Version` test may FAIL.

- [ ] **Step 3: Implement.** If Marten does not auto-populate `Version`, add the metadata convention: a `public void Apply(Marten.Events.IEvent e) => Version = (int)e.Version;` or set `[Version]`-style — use whatever Marten 8 supports for `LiveStreamAggregation`. `Id` is auto-set from the stream id for Guid-keyed aggregates. The `Version` property is needed by Task 2.1's optimistic concurrency reporting.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): maintain BookingAggregate.Version; document Apply is unguarded"
```

## Task 2.9: Command-field validation + consolidate test fakes

**Files:**
- Modify: `QuoteOfferHandler.cs`, `HoldOfferHandler.cs`, `ConfirmOrderHandler.cs`, `CancelOrderHandler.cs` (guard empty command fields)
- Create: `tests/flights/Travel.Modules.Flights.Tests.Shared/` test-infra project OR a shared file in the Integration test project — `RecordingMessageBus.cs`, `NullFlightsMetrics.cs`
- Test: extend the relevant handler tests; remove the duplicated fakes from `ConfirmOrderHandlerTests`, `CancelOrderHandlerTests`, etc.

- [ ] **Step 1: Write the failing test.** `Quote_with_empty_provider_offer_ref_is_rejected` → `QuoteOfferCommand` with `ProviderOfferRef = ""` returns `Flights.OfferNotFound` (or a new `Error.Validation`) without calling the provider.

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement.** Add guard clauses at the top of each handler for empty/whitespace required string fields and `Guid.Empty` ids. Extract `RecordingMessageBus` and `NullFlightsMetrics` into one shared location; update the 4 test files to use it (delete the `file`-scoped duplicates).

- [ ] **Step 4: Run — expect PASS.** Full `Booking/` folder green; `dotnet build` 0 warnings.

- [ ] **Step 5: Commit.**

```powershell
git commit -am "test(flights): consolidate booking test fakes; guard empty command fields"
```

---

# Phase WS3 — Webhooks

Closes: WHK-C1/C2/C3/C4, WHK-I1/I7 + minors, DUF-C3. **Depends on WS1.**

## Task 3.1: Verify and implement the real Duffel webhook HMAC scheme (A3)

**Files:**
- Modify: `Infrastructure/Providers/Duffel/DuffelWebhookVerifier.cs`
- Modify: `Api/Endpoints/DuffelWebhookEndpoint.cs` (if the verifier needs the timestamp header)
- Test: `tests/flights/Travel.Modules.Flights.Tests.Unit/Providers/Duffel/DuffelWebhookVerifierTests.cs` (rewrite fixtures)

- [ ] **Step 1: Investigate.** WebFetch the current Duffel webhook security docs (`https://duffel.com/docs/guides/receiving-webhooks` and the API reference for webhook signature verification). Determine the exact `Duffel-Signature` header format and what string is HMAC-signed (header layout, whether a timestamp is part of the signed payload, the algorithm). Record the findings in a comment in `DuffelWebhookVerifier.cs`.

- [ ] **Step 2: Write the failing tests** using a **captured real header sample** (from the Duffel docs example, or a documented test vector). Cover: valid signature → `true`; tampered body → `false`; tampered/missing/malformed header → `false`; missing secret → `false`; (if the scheme includes a timestamp) a stale timestamp beyond tolerance → `false`. Replace the current self-generated `sha256=<hex>` fixtures.

- [ ] **Step 3: Run — expect FAIL** (current verifier assumes `sha256=<hex>`, `DuffelWebhookVerifier.cs:19`).

- [ ] **Step 4: Implement** `DuffelWebhookVerifier.Verify` to the verified scheme: parse the header components, build the exact signed string (e.g. `"{timestamp}.{body}"` if that is the scheme), compute `HMACSHA256` with the configured secret, compare with `CryptographicOperations.FixedTimeEquals`. Keep the existing constant-time-compare and null/empty guards. If the endpoint must pass the timestamp header through, add that.

- [ ] **Step 5: Run — expect PASS.** Commit.

```powershell
git add modules/flights/ tests/flights/
git commit -m "fix(flights): verify Duffel webhook HMAC against the real Duffel v2 scheme"
```

## Task 3.2: Webhook endpoint — outbox publish + atomic dedup

**Files:**
- Modify: `Api/Endpoints/DuffelWebhookEndpoint.cs`
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/Webhooks/DuffelWebhookEndpointTests.cs` (extend)

- [ ] **Step 1: Write the failing tests.**
  - `Concurrent_duplicate_webhooks_both_return_200`: two identical deliveries (same `event.id`) concurrently — both responses are 200, exactly one inbox row exists, exactly one `ProcessDuffelWebhookCommand` is published.
  - `Inbox_insert_and_command_publish_are_atomic`: assert the publish rides the EF outbox (WS1) — already covered by Task 1.4 but add an endpoint-level assertion.

- [ ] **Step 2: Run — expect FAIL** (current code: `DuffelWebhookEndpoint.cs:61-79` check-then-insert race; second concurrent insert throws `DbUpdateException` → 500; `bus.PublishAsync` at `:82` is non-transactional and ct-less).

- [ ] **Step 3: Implement.** Mark the endpoint method `[Transactional]` (Wolverine EF integration from WS1). Restructure:

```csharp
// after HMAC verify + deserialize:
var row = new WebhookInboxEntity { /* ... as today ... */ };
db.WebhookInbox.Add(row);
await bus.PublishAsync(new ProcessDuffelWebhookCommand(row.Id));  // buffered in outbox
try
{
    await db.SaveChangesAsync(ct);   // commits inbox row + outbox message atomically
}
catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505" })
{
    // concurrent duplicate delivery — already ingested by the other request
    return Results.Ok();
}
metrics.RecordWebhookReceived(dto.Type);
return Results.Ok();
```

Keep the pre-check `FirstOrDefaultAsync` as a fast path (returns 200 early on the common sequential-duplicate case) but the `23505` catch is the real correctness guarantee. Pass `ct` everywhere.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): webhook endpoint — atomic dedup and outbox publish"
```

## Task 3.3: Terminal-state guards in `DuffelWebhookHandler`

**Files:**
- Modify: `Core/Aggregates/BookingAggregate.cs` (`GuardCanTicket`, `GuardCanRefund`)
- Modify: `Application/Handlers/Webhooks/DuffelWebhookHandler.cs`
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/Webhooks/DuffelWebhookHandlerTests.cs` (extend)

- [ ] **Step 1: Write the failing tests.**
  - `Ticketed_webhook_on_already_ticketed_stream_is_noop`: a second `order.created`-documents-issued event (different `event.id`) for an already-`Ticketed` stream appends no second `OrderTicketed`.
  - `Refund_webhook_on_terminal_stream_is_noop`: an `order.airline_initiated_change.cancelled` for a `Cancelled`/`Refunded` stream appends no `OrderRefunded`.
  Both still mark the inbox row processed (poison-message avoidance).

- [ ] **Step 2: Run — expect FAIL** (`DuffelWebhookHandler.cs:201,272` append unconditionally).

- [ ] **Step 3: Implement.** Add to `BookingAggregate`:

```csharp
public void GuardCanTicket()
{
    if (Status is not BookingStatus.Confirmed)
        throw new InvalidBookingStateException($"Cannot ticket in state {Status}.");
}

public void GuardCanRefund()
{
    if (Status is BookingStatus.Cancelled or BookingStatus.Refunded)
        throw new InvalidBookingStateException($"Cannot refund in state {Status}.");
}
```

In `DuffelWebhookHandler`, before appending `OrderTicketed`/`OrderRefunded`, load the aggregate and check the guard *softly*: if the guard would reject, log at Information, mark the inbox row processed, and return — do not throw, do not append. (Use a `try/catch (InvalidBookingStateException)` around the guard, or an explicit status check — be consistent with WS2's style.)

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): guard terminal states before appending webhook-driven events"
```

## Task 3.4: Explicit `order.airline_initiated_change` case + metric

**Files:**
- Modify: `Application/Handlers/Webhooks/DuffelWebhookHandler.cs`
- Test: `DuffelWebhookHandlerTests.cs` (extend)

- [ ] **Step 1: Write the failing test.** An `order.airline_initiated_change` (non-cancelled) event: handler logs it, marks it processed, appends no domain event, AND records an OTel metric for it (assert via the metrics fake / `MeterListener`). Test name: `Airline_initiated_change_records_metric_and_no_event`.

- [ ] **Step 2: Run — expect FAIL** (currently falls into `default`, `DuffelWebhookHandler.cs:113-119`, no metric).

- [ ] **Step 3: Implement.** Add an explicit `case "order.airline_initiated_change":` that logs at Information and calls a metric (reuse `IFlightsMetrics.RecordWebhookReceived` is already called by the endpoint for *all* types — add a dedicated counter `flights.webhook.airline_change_total` or a tagged increment; align with WS7's metrics work — coordinate the metric name there). Keep `default` for the truly-ignored "other" bucket.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): handle order.airline_initiated_change with explicit metric"
```

## Task 3.5: `WebhookInboxStore.MarkProcessedAsync` — warn on missing row

**Files:**
- Modify: `Infrastructure/Persistence/WebhookInboxStore.cs`
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/Webhooks/` — extend an existing store test or add `WebhookInboxStoreTests.cs`

- [ ] **Step 1: Write the failing test.** `MarkProcessedAsync` with an id that has no row — assert it logs a warning (inject a test logger / `FakeLogger`). Test name: `MarkProcessed_on_missing_row_logs_warning`.

- [ ] **Step 2: Run — expect FAIL** (`WebhookInboxStore.cs:33-35` silent `return`).

- [ ] **Step 3: Implement.** Inject `ILogger<WebhookInboxStore>`; on `entity is null`, `log.LogWarning("Webhook inbox row {InboxId} not found when marking processed.", id);` then return.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): log warning when webhook inbox row is missing on mark-processed"
```

---

# Phase WS4 — Providers & resilience (A4)

Closes: DUF-C1/C2 (+C3 via WS3), DUF-I4–I10 + minors, SRCH-I5. **Depends on WS0.**

## Task 4.1: Per-typed-client Polly resilience + wire `TimeoutSeconds`

**Files:**
- Modify: `Infrastructure/FlightsModuleServiceCollectionExtensions.cs` (HTTP client registrations, lines 72-77)
- Modify: `Infrastructure/Providers/Duffel/DuffelClient.cs`, `DuffelOptions.cs`; `Providers/Travelpayouts/TravelpayoutsClient.cs`, `TravelpayoutsOptions.cs`
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/Providers/Duffel/DuffelClientResilienceTests.cs` (new)

- [ ] **Step 1: Write the failing test.** Using WireMock: a Duffel endpoint that returns 500 twice then 200 — assert the client retries and succeeds (retry policy active). A WireMock endpoint that hangs longer than the configured per-request timeout — assert the call fails fast with a timeout, not after 100s. Test names: `Duffel_client_retries_transient_failures`, `Duffel_client_times_out_per_request_budget`.

- [ ] **Step 2: Run — expect FAIL** (only the Aspire stock `AddStandardResilienceHandler` global default applies; `TimeoutSeconds` is dead config).

- [ ] **Step 3: Implement.** In `FlightsModuleServiceCollectionExtensions`, replace the bare `AddHttpClient<T>()` calls with explicitly configured resilience per §19:

```csharp
services.AddHttpClient<DuffelClient>()
    .AddResilienceHandler("duffel", (pipeline, ctx) =>
    {
        pipeline.AddRetry(new HttpRetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            UseJitter = true,
            Delay = TimeSpan.FromMilliseconds(50),
            MaxDelay = TimeSpan.FromMilliseconds(500),
            BackoffType = DelayBackoffType.Exponential,
        });
        pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
        {
            MinimumThroughput = 5,
            SamplingDuration = TimeSpan.FromSeconds(30),
            BreakDuration = TimeSpan.FromSeconds(30),
        });
        pipeline.AddTimeout(TimeSpan.FromSeconds(
            ctx.GetOptions<DuffelOptions>()?.TimeoutSeconds ?? 10)); // total per-request budget
    });
```

Repeat for `TravelpayoutsClient` (timeout 4s default) and `FrankfurterClient` (timeout 2s default). Read `TimeoutSeconds` from the bound options. For Duffel, the spec wants 4s for *search* and 10s for *orders* — since both go through one `DuffelClient`, set the client default to 10s and have `DuffelFlightSearchProvider` pass a per-call `CancellationTokenSource(TimeSpan.FromSeconds(4))` linked token for search calls (or split into two named clients — simpler: linked CTS in the search provider). Document the choice.

Add `TimeoutSeconds` properties to `DuffelOptions`/`TravelpayoutsOptions` if absent (default 10 / 4). Note: this supersedes relying on the global `AddStandardResilienceHandler` for these three clients.

- [ ] **Step 4: Run — expect PASS.** `dotnet build` 0 warnings.

- [ ] **Step 5: Commit.**

```powershell
git add modules/flights/ tests/flights/
git commit -m "fix(flights): tune Polly resilience per provider client to spec §19"
```

## Task 4.2: Idempotency key on the Duffel payment path

**Files:**
- Modify: `Core/Providers/IFlightBookingProvider.cs` (add idempotency key param to `ConfirmOrderAsync`), `Core/Providers/IPaymentGateway.cs` (already has `idempotencyKey` on `AuthorizeAsync` — extend to `CaptureAsync`/`RefundAsync` if Duffel needs it)
- Modify: `Infrastructure/Providers/Duffel/DuffelFlightBookingProvider.cs`, `DuffelClient.cs` (per-request header API), `Infrastructure/Payments/DuffelTestWalletPaymentGateway.cs`
- Modify: `Application/Handlers/Booking/ConfirmOrderHandler.cs` (pass the key)
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/Providers/Duffel/DuffelFlightBookingProviderTests.cs` (extend), `tests/flights/Travel.Modules.Flights.Tests.Unit/Payments/DuffelTestWalletPaymentGatewayTests.cs` (extend)

- [ ] **Step 1: Write the failing tests.** `ConfirmOrderAsync` sends an `Idempotency-Key` header to the Duffel payments endpoint (assert via WireMock request matching). `DuffelTestWalletPaymentGateway.AuthorizeAsync` records/echoes the supplied `idempotencyKey` (assert it is not ignored). Test names: `Confirm_sends_idempotency_key_to_duffel`, `TestWallet_honours_idempotency_key`.

- [ ] **Step 2: Run — expect FAIL** (`DuffelTestWalletPaymentGateway.cs:12-16` ignores the key; `ConfirmOrderAsync` sends none).

- [ ] **Step 3: Implement.** Add a per-request header capability to `DuffelClient` (e.g. an overload accepting `IReadOnlyDictionary<string,string> headers` or an `Idempotency-Key` parameter). `DuffelFlightBookingProvider.ConfirmOrderAsync` gains an `idempotencyKey` parameter (thread it from `ConfirmOrderCommand` — the command/middleware already has the client's `Idempotency-Key`; pass `cmd.AggregateId.ToString("N")` if no client key is available, but prefer the real header). `DuffelTestWalletPaymentGateway` stores the key (a `ConcurrentDictionary` keyed by idempotency key returning the same `PaymentRef` on repeat — true idempotency in the sandbox impl).

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): thread idempotency key through the Duffel payment path"
```

## Task 4.3: `RefreshOfferAsync` — surface `PriceChanged` in the re-quote path

**Files:**
- Modify: `Application/Handlers/Booking/QuoteOfferHandler.cs` (re-quote branch from Task 2.3)
- Test: `QuoteOfferHandlerTests.cs` (extend)

- [ ] **Step 1: Write the failing test.** Re-quote (Task 2.3 path) where the refreshed offer has a *higher* amount than the stream's `TotalAmount` — assert the handler still appends `OfferReQuoted` (re-quote is allowed) but the returned result flags the price change, OR returns `FlightsErrors.PriceChanged(old, new)` — **decision: append `OfferReQuoted` AND return the new amount in the result with a `PriceChanged` boolean** (re-quote's whole purpose is to accept a new price; a hard error would block the happy re-quote flow). If the spec's intent is a hard 409, re-read §7.3 — it says re-quote "просто append `OfferReQuoted`", so: append + surface the delta in the response, do not error. Test name: `Requote_with_higher_price_reports_price_change`.

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement.** In the re-quote branch, compare `agg.TotalAmount` with `refreshed.Value.TotalAmount`; include a `bool PriceChanged` (and old/new amounts) on `QuotedOfferResult`/`QuotedOfferResponse`. `OfferReQuoted` already carries `OldAmount`/`NewAmount`.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "feat(flights): surface price change on re-quote"
```

## Task 4.4: `HoldOfferAsync` error mapping + hold-expiry fix

**Files:**
- Modify: `Infrastructure/Providers/Duffel/DuffelFlightBookingProvider.cs`
- Modify: `Core/Errors/FlightsErrors.cs` (add `HoldUnavailable` if needed)
- Test: `DuffelFlightBookingProviderTests.cs` (extend)

- [ ] **Step 1: Write the failing tests.** `HoldOfferAsync` against a WireMock 422 returns `Flights.OfferExpired` (or new `Flights.HoldUnavailable`), **not** `Flights.OrderNotCancellable`. When the Duffel hold response omits `payment_required_by`, the provider returns an error (or falls back to the offer's `ExpiresAt`) — **not** a fabricated `now + 20min`. Test names: `Hold_422_maps_to_offer_expired`, `Hold_without_payment_required_by_does_not_fabricate_expiry`.

- [ ] **Step 2: Run — expect FAIL** (`DuffelFlightBookingProvider.cs:87-91` returns `OrderNotCancellable`; `:105-106` fabricates `+20min`).

- [ ] **Step 3: Implement.** Map 422 on hold to `FlightsErrors.OfferExpired` (or add `FlightsErrors.HoldUnavailable`). For the missing `payment_required_by`: prefer falling back to the offer's own `ExpiresAt` (the offer is in scope at the hold call); if that is also unavailable, return an error. Remove the magic `+20min`.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): correct Duffel hold error mapping and expiry handling"
```

## Task 4.5: No raw provider error strings in domain errors

**Files:**
- Modify: `Infrastructure/Providers/Duffel/DuffelFlightBookingProvider.cs` (`ConfirmOrderAsync` ~:152-159, `CancelOrderAsync` ~:181-188)
- Test: `DuffelFlightBookingProviderTests.cs` (extend)

- [ ] **Step 1: Write the failing test.** `ConfirmOrderAsync` against a WireMock 500 with a raw JSON body — assert the returned `FlightsErrors.PaymentFailed` description is a *sanitized* message (a fixed string + status code), not the raw Duffel JSON. Test name: `Confirm_failure_does_not_leak_raw_provider_body`.

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement.** Log the raw body at `Warning` with the status code; return `FlightsErrors.PaymentFailed($"Provider returned {(int)resp.StatusCode}.")` (or similar). Same for `CancelOrderAsync`.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): sanitize Duffel provider errors before surfacing to the domain"
```

## Task 4.6: `GetOrderStatusAsync` — typed status enum + `ticketed`

**Files:**
- Modify: `Core/Providers/Dtos/OrderStatus.cs` (status → enum)
- Modify: `Infrastructure/Providers/Duffel/DuffelFlightBookingProvider.cs` (`GetOrderStatusAsync`)
- Test: `DuffelFlightBookingProviderTests.cs` (extend)

- [ ] **Step 1: Write the failing test.** `GetOrderStatusAsync` for a Duffel order whose payload contains ticket `documents` returns `OrderStatus.Status == OrderStatusKind.Ticketed`. Cancelled → `Cancelled`; otherwise `Confirmed`. Test name: `GetOrderStatus_reports_ticketed_when_documents_present`.

- [ ] **Step 2: Run — expect compile FAIL** (status is a magic string today).

- [ ] **Step 3: Implement.** Add `enum OrderStatusKind { Confirmed, Cancelled, Ticketed }`; change `OrderStatus.Status` to that enum. In `GetOrderStatusAsync`, inspect the Duffel order DTO: cancelled flag → `Cancelled`; presence of `documents` of type ticket → `Ticketed`; else `Confirmed`. Update any consumers (the §7.2 polling fallback, if implemented; otherwise just the provider + tests).

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): type OrderStatus and report ticketed state from Duffel"
```

## Task 4.7: `DuffelOfferMapper` — baggage summary + all-slice fare + edge cases

**Files:**
- Modify: `Infrastructure/Providers/Duffel/DuffelOfferMapper.cs`, relevant DTOs in `Providers/Duffel/Dto/` (add baggage fields)
- Modify: `Core/ValueObjects/FareConditions.cs` only if a baggage field belongs there (spec §5.3 says "summary in M1" — add `string? BaggageSummary` to `FareConditions` or a dedicated field)
- Test: `tests/flights/Travel.Modules.Flights.Tests.Unit/Providers/Duffel/DuffelOfferMapperTests.cs` (extend), fixtures in `Fixtures/`

- [ ] **Step 1: Write the failing tests.** Mapper maps a baggage summary from the Duffel offer (add a fixture with baggage data). Mapper edge cases: unparseable `total_amount` → `ErrorOr` error (not exception); empty/missing `slices` → error; null `conditions` → `FareConditions(false, false, ...)`; round-trip offer with differing inbound fare → fare basis reflects both slices (or document first-slice-only as accepted — but spec §5.3 wants per-direction; minimum: don't silently drop). Test names: `Mapper_maps_baggage_summary`, `Mapper_rejects_unparseable_amount`, `Mapper_handles_null_conditions`, `Mapper_reads_fare_from_all_slices`.

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement.** Add baggage DTO fields + mapping. Guard the `decimal.TryParse` / empty-slices / null-conditions paths to return `ErrorOr` errors. Read fare basis per slice.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): map Duffel baggage summary and harden offer mapper edge cases"
```

## Task 4.8: Booking-provider + search-provider failure-path tests

**Files:**
- Test: `DuffelFlightBookingProviderTests.cs`, `DuffelFlightSearchProviderTests.cs` (extend)

- [ ] **Step 1: Write the tests** (no production change expected — pure coverage of existing branches; if a branch is found broken, fix it). Cover: `HoldOfferAsync` 500; `ConfirmOrderAsync` GET-fails and payment 4xx/5xx; `CancelOrderAsync` non-2xx; `DuffelFlightSearchProvider` `TaskCanceledException` and `HttpRequestException` branches; `DuffelClient` POST body wrapping (`{ data = body }`).

- [ ] **Step 2-4:** Run; if any test reveals a real bug, fix it (TDD). All green.

- [ ] **Step 5: Commit.**

```powershell
git add tests/flights/ modules/flights/
git commit -m "test(flights): cover Duffel provider failure paths"
```

---

# Phase WS5 — Search pipeline & caching

Closes: SRCH-C1/C2/C3, SRCH-I4/I6–I11 + minors. **Depends on WS0.** (Task 5.8 also needs WS6's feature-flags class — sequence 5.8 after WS6 Task 6.2, or stub the flag locally.)

## Task 5.1: Deeplink EF cache is audit-only

**Files:**
- Modify: `Infrastructure/Providers/Travelpayouts/TravelpayoutsSearchProvider.cs`
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/Providers/Travelpayouts/TravelpayoutsSearchProviderTests.cs` (extend)

- [ ] **Step 1: Write the failing test.** Seed the deeplink EF cache with offers for a criteria hash; call `TravelpayoutsSearchProvider.SearchAsync` with matching criteria — assert it still calls the Travelpayouts API (does NOT serve from the EF cache) and *writes* the fresh result to the cache. Test name: `Search_does_not_serve_from_deeplink_audit_cache`.

- [ ] **Step 2: Run — expect FAIL** (`TravelpayoutsSearchProvider.cs:32` reads `cache.TryGetAsync`).

- [ ] **Step 3: Implement.** Remove the `IDeeplinkOfferCache.TryGetAsync` read path; always call the API; keep only the `SetAsync` write (audit per §6.4).

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): deeplink EF cache is audit-only, never served as fresh results"
```

## Task 5.2: `FrankfurterRatesCache` — invariant-culture serialization

**Files:**
- Modify: `Infrastructure/Cache/FrankfurterRatesCache.cs`
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/` — `FrankfurterRatesCacheTests.cs` (new or extend)

- [ ] **Step 1: Write the failing test.** Set `CultureInfo.CurrentCulture` to `ru-RU` for the test; write a rate (e.g. `0.92m`) to the cache and read it back — assert it round-trips to `0.92m`, not `92m`. Test name: `Rate_round_trips_under_non_invariant_culture`.

- [ ] **Step 2: Run — expect FAIL** (`FrankfurterRatesCache.cs:28,36` use culture-sensitive `ToString`/`TryParse`).

- [ ] **Step 3: Implement.** Use `rate.ToString(CultureInfo.InvariantCulture)` and `decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var rate)`.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): serialize FX rate cache with invariant culture"
```

## Task 5.3: Fan-out exception isolation

**Files:**
- Modify: `Application/Handlers/Search/SearchFlightsHandler.cs` (`RunWithTimeout`)
- Modify: `Infrastructure/Providers/Travelpayouts/TravelpayoutsSearchProvider.cs`
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/Search/SearchFlightsHandlerTests.cs` (extend)

- [ ] **Step 1: Write the failing test.** A search where one provider throws `JsonException` (malformed body) — assert the *other* provider's offers are still returned and the throwing provider appears in `partial_failure[]`; the search does not fault. Test name: `Provider_throwing_non_cancellation_exception_becomes_partial_failure`.

- [ ] **Step 2: Run — expect FAIL** (`SearchFlightsHandler.RunWithTimeout` only catches `OperationCanceledException`; a `JsonException` faults `Task.WhenAll`).

- [ ] **Step 3: Implement.** Broaden the `catch` in `RunWithTimeout` to `catch (Exception ex)` → log + return a `ProviderFailure` tuple (re-throwing is explicitly *not* wanted — fan-out isolation is the point). In `TravelpayoutsSearchProvider.SearchAsync`, catch `JsonException` and the empty-body case → return `FlightsErrors.ProviderUnavailable("travelpayouts")` instead of throwing `InvalidOperationException`.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): isolate provider exceptions in search fan-out"
```

## Task 5.4: `SearchCacheRedis` — guarded deserialize + enum converter

**Files:**
- Modify: `Infrastructure/Cache/SearchCacheRedis.cs`
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/Search/SearchCacheRedisTests.cs` (extend)

- [ ] **Step 1: Write the failing test.** Store a deliberately malformed/old-schema JSON blob under a search key; `TryGetAsync` returns `null` (cache miss), does **not** throw. Test name: `Corrupt_cache_entry_is_treated_as_a_miss`.

- [ ] **Step 2: Run — expect FAIL** (`SearchCacheRedis.cs:23` unguarded `Deserialize`).

- [ ] **Step 3: Implement.** Wrap the deserialize in `try { ... } catch (JsonException ex) { log.LogWarning(...); return null; }`. Add `JsonStringEnumConverter` to the `JsonSerializerOptions` (`SearchCacheRedis.cs:10-13`). Inject `ILogger<SearchCacheRedis>`.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): treat corrupt search-cache entries as misses"
```

## Task 5.5: Wire FX conversion into the search pipeline

**Files:**
- Modify: `Application/Handlers/Search/SearchFlightsHandler.cs`
- Test: `SearchFlightsHandlerTests.cs` (extend)

- [ ] **Step 1: Write the failing test.** Two providers return offers in *different* currencies (e.g. one EUR, one RUB) for a `criteria.Currency = RUB` search; with a fake `IFxRates` returning a known rate — assert all offers in the response are normalized to RUB and ranking reflects the converted amounts. Test name: `Offers_are_normalized_to_requested_currency_before_ranking`.

- [ ] **Step 2: Run — expect FAIL** (`IFxRates` is never called; `OfferRanker` sorts raw decimals).

- [ ] **Step 3: Implement.** Inject `IFxRates` into `SearchFlightsHandler`. After aggregation, before dedup/rank, map each offer's `TotalAmount` to `criteria.Currency` via `IFxRates.ConvertAsync`. If conversion fails for an offer, log and keep the original (or drop — prefer keep + log). Dedup and rank then operate on normalized amounts.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): normalize offer currency via FX before dedup and ranking"
```

## Task 5.6: Round-trip dedup key

**Files:**
- Modify: `Application/Search/OfferDeduplicator.cs`
- Test: `tests/flights/Travel.Modules.Flights.Tests.Unit/Search/OfferPipelineTests.cs` (extend)

- [ ] **Step 1: Write the failing test.** Two round-trip offers sharing the *outbound* flight but with *different inbound* flights — assert they are NOT deduped into one. A genuine duplicate (same outbound AND inbound) IS deduped. Test names: `Roundtrips_with_different_inbound_are_not_deduped`, `Identical_roundtrips_are_deduped`.

- [ ] **Step 2: Run — expect FAIL** (`OfferDeduplicator.KeyOf` reads `Slices[0].Segments[0]` only).

- [ ] **Step 3: Implement.** `KeyOf`: for `Itinerary.IsRoundTrip`, build a composite key from the primary segment of *each* slice (`(carrier, flightNo, departDateUtc)` per slice, concatenated). One-way keeps the single-slice key.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): use a per-slice composite dedup key for round-trips"
```

## Task 5.7: Deterministic final tie-break in ranker and deduplicator

**Files:**
- Modify: `Application/Search/OfferRanker.cs`, `Application/Search/OfferDeduplicator.cs`
- Test: `OfferPipelineTests.cs` (extend)

- [ ] **Step 1: Write the failing test.** Two offers with identical converted price AND identical duration, presented in two different input orders — assert `OfferRanker.Rank` produces the *same* output order both times. Same idea for the deduplicator's "which one wins" choice. Test name: `Ranking_is_deterministic_on_full_ties`.

- [ ] **Step 2: Run — expect FAIL** (no final tie-break; order depends on fan-out arrival).

- [ ] **Step 3: Implement.** Add `.ThenBy(o => o.Id.Value)` (or `provider id + offer ref`) as the final ordering key in `OfferRanker.Rank` and in the deduplicator's winner-selection.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): make offer ranking and dedup deterministic on ties"
```

## Task 5.8: `flights.travelpayouts.enabled` feature flag (provider side)

> **Depends on WS6 Task 6.2** (`FlightsFeatureFlags` options class). Sequence after 6.2.

**Files:**
- Modify: `Infrastructure/Providers/Travelpayouts/TravelpayoutsSearchProvider.cs`
- Test: `TravelpayoutsSearchProviderTests.cs` (extend)

- [ ] **Step 1: Write the failing test.** With `FlightsFeatureFlags.Travelpayouts.Enabled = false`, `TravelpayoutsSearchProvider.SearchAsync` returns an empty list (success, **not** a `ProviderFailure` — a deliberate disable must not pollute `partial_failure[]`). Test name: `Disabled_travelpayouts_returns_empty_not_failure`.

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement.** Inject `IOptionsMonitor<FlightsFeatureFlags>`; short-circuit `SearchAsync` to `new List<Offer>()` when disabled.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "feat(flights): honour flights.travelpayouts.enabled feature flag"
```

## Task 5.9: Travelpayouts token redaction + search minors

**Files:**
- Modify: `apps/Travel.ServiceDefaults/Extensions.cs` (OTel HTTP instrumentation enrich/filter) OR a Flights-local `HttpClient` configuration
- Modify: `Infrastructure/FlightsModuleServiceCollectionExtensions.cs:75-77` (Frankfurter base address from config)
- Modify: `Application/Search/SearchCacheKey.cs` (use explicit `.Value`), `Infrastructure/Providers/Travelpayouts/TravelpayoutsOfferMapper.cs` (skip + log malformed entries instead of fabricating `XX0`)
- Test: relevant unit tests + a test asserting the token is not in the recorded span attributes (if feasible) or at least a unit test that the redaction filter strips a `token` query param.

- [ ] **Step 1: Write the failing test** for the redaction filter: given a URL `https://.../prices_for_dates?token=SECRET&origin=LED`, the OTel enrichment produces a span `url.full` with `token=REDACTED`. If testing the OTel pipeline directly is impractical, extract the redaction into a small pure function `UrlRedactor.Redact(string url)` and unit-test that. Test name: `Redactor_strips_token_query_param`.

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement.** Add a `UrlRedactor` + wire it into the HTTP client OTel instrumentation `EnrichWithHttpRequestMessage` for the Travelpayouts client (or globally, redacting known secret params). Move the Frankfurter base address into `appsettings.json` config. `SearchCacheKey` uses explicit `.Value` on `IataCode`/`CurrencyCode`. `TravelpayoutsOfferMapper` skips entries missing carrier/flight (logs `Warning`) instead of synthesizing `XX0`.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): redact provider token from telemetry; search-pipeline minors"
```

## Task 5.10: Search timeout-path + end-to-end coverage tests

**Files:**
- Test: `SearchFlightsHandlerTests.cs`, `SearchCacheRedisTests.cs` (extend)

- [ ] **Step 1: Write the tests.** A `SlowProvider` fake that delays beyond the 4s budget — assert it becomes a `ProviderFailure` with code `Timeout` and the search still returns the fast provider's offers. A mixed bookable+deeplink list run through the *handler* end-to-end asserting dedup + rank + truncate. A Redis cache-miss-then-populate test that inspects the key TTL ≈ 5 min. Test names: `Slow_provider_times_out_as_partial_failure`, `Handler_dedups_and_ranks_mixed_list`, `Search_cache_entry_has_five_minute_ttl`.

- [ ] **Step 2-4:** Run; fix any real bug surfaced. All green.

- [ ] **Step 5: Commit.**

```powershell
git add tests/flights/
git commit -m "test(flights): cover search timeout, mixed-list ranking, cache TTL"
```

---

# Phase WS6 — API & composition

Closes: API-C1, API-I2–I8 + minors, PER-I7. **Depends on WS1.**

## Task 6.1: Enforce the `flights:book` scope (D3)

**Files:**
- Modify: `apps/Travel.Host/Program.cs` (authorization policy)
- Modify: `Api/Endpoints/HoldOfferEndpoint.cs`, `ConfirmOrderEndpoint.cs`, `CancelOrderEndpoint.cs` (`[Authorize("flights:book")]`)
- Modify: `infra/keycloak/travel-realm.json` (move `flights:book` to `defaultClientScopes`)
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/` — extend the HTTP-pipeline tests (or `Travel.Host.Tests.Integration`)

- [ ] **Step 1: Write the failing test.** A JWT *without* the `flights:book` scope claim calls `POST /api/flights/orders/hold` → 403. A JWT *with* the scope → not 403 (200/400 depending on body). Test names: `Hold_without_flights_book_scope_is_forbidden`, `Hold_with_flights_book_scope_is_allowed`.

- [ ] **Step 2: Run — expect FAIL** (bare `[Authorize]` accepts any authenticated user).

- [ ] **Step 3: Implement.** In `Program.cs` `AddAuthorization`:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    options.AddPolicy("flights:book", p =>
        p.RequireAuthenticatedUser()
         .RequireClaim("scope", "flights:book"));   // adjust claim type to the realm's token shape
});
```

Change the three booking endpoints to `[Authorize("flights:book")]`. In `travel-realm.json`, move `flights:book` from `optionalClientScopes` to `defaultClientScopes` for the `travel-web`/`travel-host` client so a registered user gets it (D3, §10.2). Verify the claim type — Keycloak emits `scope` as a space-delimited string; if `RequireClaim("scope", "flights:book")` doesn't match a space-delimited value, use a custom requirement that splits the `scope` claim.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git add apps/Travel.Host/ modules/flights/ infra/keycloak/ tests/
git commit -m "fix(flights): enforce flights:book scope on booking endpoints"
```

## Task 6.2: `FlightsFeatureFlags` options + `IOptionsMonitor` wiring

**Files:**
- Create: `modules/flights/Travel.Modules.Flights.Infrastructure/FlightsFeatureFlags.cs` (or `Application` if the handlers need it without an Infrastructure dependency — put it in `Application` to keep `SearchFlightsHandler`/`NlSearchHandler` clean)
- Modify: `FlightsModuleServiceCollectionExtensions.cs` (`services.Configure<FlightsFeatureFlags>(...)`)
- Modify: `Application/Handlers/Search/SearchFlightsHandler.cs` (already wired via the provider in 5.8 — handler-side only if needed), `Api/Endpoints/NlSearchEndpoint.cs` + `Application/Handlers/NlSearch/NlSearchHandler.cs`
- Modify: `apps/Travel.Host/appsettings.json` (add `Flights:FeatureFlags` section)
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/NlSearch/NlSearchHandlerTests.cs` (extend)

- [ ] **Step 1: Write the failing test.** With `FlightsFeatureFlags.NlSearch.Enabled = false`, `POST /api/flights/search/nl` returns a ProblemDetails (e.g. 503 / `Flights.NlSearchDisabled`) without calling the AI. Test name: `Nl_search_disabled_returns_problem_details`.

- [ ] **Step 2: Run — expect compile/behaviour FAIL.**

- [ ] **Step 3: Implement.**

```csharp
namespace Travel.Modules.Flights.Application;

public sealed class FlightsFeatureFlags
{
    public const string SectionName = "Flights:FeatureFlags";
    public ProviderFlag Travelpayouts { get; set; } = new();
    public ProviderFlag NlSearch { get; set; } = new();
    public sealed class ProviderFlag { public bool Enabled { get; set; } = true; }
}
```

`services.Configure<FlightsFeatureFlags>(configuration.GetSection(FlightsFeatureFlags.SectionName))`. Inject `IOptionsMonitor<FlightsFeatureFlags>` into `NlSearchEndpoint`/`NlSearchHandler` (short-circuit when disabled) and ensure the `TravelpayoutsSearchProvider` (Task 5.8) reads the same options. Add the config section with both flags `true` by default.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git add modules/flights/ apps/Travel.Host/ tests/flights/
git commit -m "feat(flights): add FlightsFeatureFlags via IOptionsMonitor"
```

## Task 6.3: Flights healthchecks

**Files:**
- Modify: `FlightsModuleServiceCollectionExtensions.cs` (add `AddHealthChecks().AddCheck(...)`)
- Create: `Infrastructure/HealthChecks/DuffelHealthCheck.cs`, `TravelpayoutsHealthCheck.cs`
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/` — `FlightsHealthCheckTests.cs` (new), using WireMock for the provider pings

- [ ] **Step 1: Write the failing test.** With WireMock standing in for Duffel/Travelpayouts: a healthy ping → `HealthStatus.Healthy`; a failing ping → `Unhealthy`. Test names: `Duffel_healthcheck_reports_healthy_on_ping_ok`, `Travelpayouts_healthcheck_reports_unhealthy_on_ping_failure`.

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement.** `DuffelHealthCheck` pings Duffel `/api/identity` (per §19); `TravelpayoutsHealthCheck` pings `prices_for_dates?...&test=1`. Register both via `services.AddHealthChecks().AddCheck<DuffelHealthCheck>("duffel").AddCheck<TravelpayoutsHealthCheck>("travelpayouts")`. Marten/EF healthchecks: confirm the Aspire `AddNpgsqlDbContext` already contributes a DB check; add a Marten check if not present.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "feat(flights): add Duffel and Travelpayouts healthchecks"
```

## Task 6.4: `currency` query param + `locale` Accept-Language header (D2)

**Files:**
- Modify: `Api/Contracts/Contracts.cs` (`SearchRequest`, `NlSearchRequest` — remove `Currency`/`Locale` body fields)
- Modify: `Api/Endpoints/SearchEndpoint.cs`, `NlSearchEndpoint.cs` (bind `[FromQuery] string? currency`, read `Accept-Language`)
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/Search/` HTTP-pipeline tests (extend)

- [ ] **Step 1: Write the failing test.** `POST /api/flights/search?currency=USD` with `Accept-Language: en` → the resulting `SearchCriteria.Currency` is USD and the locale is `en`. Missing `currency` → defaults to `RUB`; missing `Accept-Language` → `ru`. Test names: `Search_reads_currency_from_query`, `Search_reads_locale_from_accept_language`, `Search_defaults_currency_rub_and_locale_ru`.

- [ ] **Step 2: Run — expect FAIL** (currently body fields).

- [ ] **Step 3: Implement.** Remove `Currency` from `SearchRequest` and `Locale` from `NlSearchRequest`. In the endpoints, add `[FromQuery] string? currency` and read `req.Headers.AcceptLanguage` (first value, validated to `ru|en`, default `ru`/`RUB`). Map into `SearchCriteria` / the NL command.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): currency via query param, locale via Accept-Language (spec §19)"
```

## Task 6.5: Clamp `offset` pagination

**Files:**
- Modify: `Infrastructure/Persistence/OrderReadModelQueries.cs` (`ListAsync`)
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/Booking/` — extend `OrderQueriesTests.cs`

- [ ] **Step 1: Write the failing test.** `ListAsync` with `offset = -5` returns the first page (offset treated as 0), does not throw. Test name: `List_with_negative_offset_does_not_throw`.

- [ ] **Step 2: Run — expect FAIL** (`OrderReadModelQueries.cs:38` `.Skip(offset)` with a negative throws).

- [ ] **Step 3: Implement.** `var safeOffset = Math.Max(0, offset);` before `.Skip(...)`.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): clamp negative pagination offset"
```

## Task 6.6: `OrderResponseMapper` — no silent empty `catch`

**Files:**
- Modify: `Api/Contracts/Contracts.cs` (`OrderResponseMapper.From`, ~:192-204)
- Test: `tests/flights/Travel.Modules.Flights.Tests.Unit/Api/ContractMappingTests.cs` (extend)

- [ ] **Step 1: Write the failing test.** `OrderResponseMapper.From` with a malformed `ItineraryJson` — returns a response with an empty `ItineraryDto` AND a warning is logged (inject a `FakeLogger` or pass an `ILogger`). Test name: `Mapper_logs_warning_on_malformed_itinerary_json`.

- [ ] **Step 2: Run — expect FAIL** (empty `catch {}` swallows).

- [ ] **Step 3: Implement.** Catch the specific `JsonException`, log a warning with the `AggregateId`, keep the empty-itinerary fallback. (If `OrderResponseMapper` is static with no logger, pass an `ILogger` parameter from the endpoint, or move the canonicalization into the projector so the dual-deserialize + `catch` disappears — prefer the logger-parameter approach for a smaller change.)

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): log instead of silently swallowing malformed itinerary JSON"
```

## Task 6.7: HTTP-pipeline tests exercise the real Wolverine pipeline

**Files:**
- Modify: `tests/flights/Travel.Modules.Flights.Tests.Integration/` — `FlightsApiFixture.cs`, `FlightsModuleWiringTests.cs`
- Test: same files

- [ ] **Step 1: Rewrite the fixture/tests.** Instead of `FlightsApiFixture` re-declaring routes with hand-copied `.RequireAuthorization()`, boot the real host via Alba (`AlbaHost.For<Program>()` with `MapWolverineEndpoints()`). Move the auth-enforcement assertions (401 without token on every JWT endpoint, 403 without `flights:book` scope on booking endpoints, 200 on anon endpoints) into `FlightsModuleWiringTests` so they run against the *actual discovered* endpoints. Test name additions: `Discovered_booking_endpoints_require_flights_book_scope`.

- [ ] **Step 2: Run — expect FAIL** initially (the real pipeline may differ from the re-declared one — that is the point).

- [ ] **Step 3: Fix** any genuine wiring gap surfaced.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "test(flights): exercise the real Wolverine HTTP pipeline for auth"
```

## Task 6.8: `ContractMappingTests` request→domain + minors

**Files:**
- Modify: `tests/flights/Travel.Modules.Flights.Tests.Unit/Api/ContractMappingTests.cs`
- Modify: `Api/Contracts/Contracts.cs` (use or remove `CancelOrderRequest`)
- Modify: `modules/flights/CLAUDE.md` (document the DTO-bundle-file exception)

- [ ] **Step 1: Write the tests.** request→domain mapping success + validation-error paths: `SearchRequest`→`SearchCriteria` (valid + invalid), `PassengerInfoDto`→`PassengerInfo` (valid + invalid email/future DOB). 

- [ ] **Step 2: Run — expect FAIL** if any mapping path is broken; otherwise these are coverage.

- [ ] **Step 3:** Implement: either wire `CancelOrderRequest` into `CancelOrderEndpoint` (if it should carry a reason) or delete the unused DTO. Add a note to `modules/flights/CLAUDE.md` that `Contracts.cs`/`OrderQueries.cs`/`NlSearchContracts.cs` intentionally bundle cohesive DTO groups (one-class-per-file applies to handlers).

- [ ] **Step 4: Run — expect PASS.** `dotnet build` 0 warnings.

- [ ] **Step 5: Commit.**

```powershell
git add tests/flights/ modules/flights/
git commit -m "test(flights): cover request-to-domain mapping; tidy unused DTO"
```

---

# Phase WS7 — Notifications & observability

Closes: NOB-I1–I9 + minors, §13.2/§13.3/email-content gaps. **Depends on WS0.** Files centre on `Infrastructure/Observability/FlightsMetrics.cs`, `Application/Observability/*`, `Infrastructure/Notifications/*`, `Api/Endpoints/OrderEventsSseEndpoint.cs`.

## Task 7.1: Add missing histogram metrics — `payment.duration_ms`, `nl_search.duration_ms`

**Files:**
- Modify: `Infrastructure/Observability/FlightsMetrics.cs`, `Application/Observability/IFlightsMetrics.cs`
- Modify: callers — `ConfirmOrderHandler` (payment duration), `NlSearchHandler` (nl-search duration)
- Test: `tests/flights/Travel.Modules.Flights.Tests.Unit/Observability/FlightsMetricsTests.cs` (extend)

- [ ] **Step 1: Write the failing tests.** `RecordPaymentDuration(ms, outcome)` emits `flights.payment.duration_ms` histogram tagged `outcome`; `RecordNlSearchDuration(ms)` emits `flights.nl_search.duration_ms`. Assert via `MeterListener` (the existing test pattern). Test names: `Payment_duration_histogram_is_emitted`, `Nl_search_duration_histogram_is_emitted`.

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement.** Add the two histograms + interface methods; record duration in `ConfirmOrderHandler` (wrap the payment calls in a `Stopwatch`/`time` delta) and `NlSearchHandler`.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "feat(flights): add payment and nl-search duration histograms (§13.1)"
```

## Task 7.2: Observable gauges — `partial_fill_rate`, `offer_to_book_conversion`, `payment.success_rate`

**Files:**
- Modify: `Infrastructure/Observability/FlightsMetrics.cs`, `Application/Observability/*`
- Modify: callers that feed the rolling-window state (search handler, booking handlers)
- Test: `FlightsMetricsTests.cs` (extend)

- [ ] **Step 1: Write the failing tests.** Feed the metrics object N searches (some with partial failures) and assert the `flights.search.partial_fill_rate` `ObservableGauge` callback returns the correct rolling fraction; similarly `payment.success_rate` from recorded outcomes and `offer_to_book_conversion`. Use a deterministic rolling-window (e.g. last-5-min buckets with `TimeProvider`). Test names: `Partial_fill_rate_gauge_reflects_recent_searches`, `Payment_success_rate_gauge_reflects_recent_outcomes`, `Conversion_gauge_reflects_offers_and_bookings`.

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement.** Add three `ObservableGauge` instruments with rolling-window state inside `FlightsMetrics` (a small thread-safe ring/bucket structure keyed by `TimeProvider` — inject `TimeProvider` into `FlightsMetrics`). Existing `RecordPaymentOutcome`, search-partial-failure recording, and a new `RecordOfferShown`/`RecordOrderBooked` feed the windows. Keep the raw `payment.success_total`/`failure_total` counters or remove them in favour of the gauge — spec §13.1 lists the *rate gauge*, so the gauge is required; keep counters only if useful, otherwise drop the extra `flights.search.errors` counter the audit flagged as undocumented.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "feat(flights): add partial-fill, conversion and payment-success-rate gauges (§13.1)"
```

## Task 7.3: `nl_search.tokens_used` — `direction` tag

**Files:**
- Modify: `Infrastructure/Observability/FlightsMetrics.cs` (`RecordNlSearchUsage`)
- Modify: `apps/Travel.AI/Observability/AiMetrics.cs` only if needed (it already tags correctly)
- Test: `FlightsMetricsTests.cs` (extend)

- [ ] **Step 1: Write the failing test.** `RecordNlSearchUsage(inputTokens, outputTokens)` emits two `flights.nl_search.tokens_used` measurements tagged `direction=input` and `direction=output` with the respective counts. Test name: `Nl_search_tokens_are_tagged_by_direction`.

- [ ] **Step 2: Run — expect FAIL** (`FlightsMetrics.cs:79-83` sums and emits untagged).

- [ ] **Step 3: Implement.** Two `Add` calls with `new KeyValuePair<string,object?>("direction", "input"|"output")`. Update the stale comment at `FlightsMetrics.cs:22`.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): tag nl_search.tokens_used by direction (§13.1)"
```

## Task 7.4: Spans — `ActivitySource` for provider calls, saga transitions, Anthropic calls

**Files:**
- Create: `Infrastructure/Observability/FlightsActivitySource.cs` (a shared `ActivitySource`)
- Modify: provider adapters, booking handlers, `apps/Travel.AI/NlSearch/NlSearchExtractor.cs`
- Modify: `apps/Travel.Host/Program.cs` + `apps/Travel.AI/Program.cs` (register the source with OTel tracing)
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/Observability/FlightsTracingTests.cs` (new) — use an `ActivityListener` to assert spans are emitted with the expected names/tags

- [ ] **Step 1: Write the failing test.** A search produces a span per provider HTTP call with `provider.id` + `http.status_code`; a confirm produces a `booking.event.OrderConfirmed` span with `aggregate.id` + `aggregate.version`. Test name: `Provider_calls_and_saga_transitions_emit_spans`.

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement.** A `static readonly ActivitySource FlightsActivitySource = new("Travel.Flights")`. Wrap provider HTTP calls and event-append blocks in `using var activity = FlightsActivitySource.StartActivity(...)` with the §13.2 tags. Register `.AddSource("Travel.Flights")` in both hosts' OTel tracing config. For the Anthropic call in `Travel.AI`, add `gen_ai.*` tags per §13.2.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git add modules/flights/ apps/ tests/flights/
git commit -m "feat(flights): emit OTel spans for provider calls and saga transitions (§13.2)"
```

## Task 7.5: Structured-log `correlation_id` enrichment

**Files:**
- Modify: booking handlers, webhook handler, search handler `BeginScope` calls — add `correlation_id`
- Possibly: a small logging enricher / middleware that puts the W3C trace id into the scope
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/Observability/` — assert log entries carry `correlation_id` (use a `FakeLogger` capturing scopes)

- [ ] **Step 1: Write the failing test.** A booking command logs with a `correlation_id` scope value equal to `Activity.Current?.TraceId` (W3C traceparent). Test name: `Booking_logs_carry_correlation_id`.

- [ ] **Step 2: Run — expect FAIL** (`58e69a3` added only `order_id`/`user_id` scopes).

- [ ] **Step 3: Implement.** Add `["correlation_id"] = Activity.Current?.TraceId.ToString()` to the `BeginScope` dictionaries in the booking/webhook/search handlers (or a single shared helper). Confirm OTel trace context flows (it does — ASP.NET Core sets `Activity.Current`).

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): enrich structured logs with correlation_id (§13.3)"
```

## Task 7.6: SSE 1 MB backpressure + disconnect + unconditional drain

**Files:**
- Modify: `Infrastructure/Notifications/Sse/OrderSseConnectionRegistry.cs`, `Api/Endpoints/OrderEventsSseEndpoint.cs`
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/Notifications/SseRegistryTests.cs` + a new endpoint streaming test

- [ ] **Step 1: Write the failing tests.** A connection whose buffered (unread) SSE payload exceeds 1 MB is disconnected (its channel completed) rather than silently dropping events. The endpoint loop drains the channel every iteration even when the heartbeat timer wins the `WhenAny` race (no event left unread). Test names: `Slow_consumer_exceeding_1mb_is_disconnected`, `Channel_is_drained_every_loop_iteration`.

- [ ] **Step 2: Run — expect FAIL** (`BoundedChannel(32, DropOldest)` silently drops; the loop `continue`s without draining when the timer wins).

- [ ] **Step 3: Implement.** Track per-connection buffered bytes in the registry; when a publish would push the buffer past 1 MB, complete the channel (signal disconnect) instead of dropping. In `OrderEventsSseEndpoint`, restructure the loop so `while (channel.Reader.TryRead(out var ev)) { write }` runs unconditionally each iteration, then awaits `WhenAny(readTask, timerTask)`.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): SSE 1MB backpressure with disconnect; unconditional channel drain"
```

## Task 7.7: Email content — cancellation reason + refund, confirmation disclaimer

**Files:**
- Modify: `Application/Contracts/OrderNotifications.cs` (carry `CancelReason` into `OrderCancelledNotification`), `CancelOrderHandler.cs` (populate it)
- Modify: `Application/Handlers/Notifications/SendOrderCancellationEmailHandler.cs`, `SendOrderConfirmationEmailHandler.cs`
- Modify: the 4 templates in `Infrastructure/Notifications/Email/Templates/`
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/Notifications/EmailNotificationTests.cs` (extend)

- [ ] **Step 1: Write the failing tests.** The cancellation email body contains the cancellation reason and a refund-expectation line; the confirmation email body contains a "ticket follows" disclaimer. Both for `ru` and `en`. Test names: `Cancellation_email_includes_reason_and_refund_text`, `Confirmation_email_includes_ticket_follows_disclaimer`.

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement.** Add `CancelReason Reason` to `OrderCancelledNotification`; `CancelOrderHandler` populates it; the cancellation handler passes a localized reason + refund line into the renderer tokens; templates gain the tokens. Confirmation templates gain the disclaimer line. Keep all token values HTML-encoded (the renderer already does this — verify).

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): cancellation reason/refund and confirmation disclaimer in emails (§12.1)"
```

## Task 7.8: `KeycloakUserDirectory` — cancellation rethrow + null-body fallback

**Files:**
- Modify: `Infrastructure/Notifications/KeycloakUserDirectory.cs`
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/Notifications/KeycloakUserDirectoryTests.cs` (extend)

- [ ] **Step 1: Write the failing tests.** A cancelled `CancellationToken` propagates `OperationCanceledException` (not converted to a fallback profile). A Keycloak 200 with an empty/null body yields a `FallbackProfile`, not `null` (so the email is not silently dropped). Test names: `Cancelled_token_propagates`, `Null_body_yields_fallback_profile`.

- [ ] **Step 2: Run — expect FAIL** (`KeycloakUserDirectory.cs:53` catches all; `:40` returns null on null body).

- [ ] **Step 3: Implement.** `catch (Exception ex) when (ex is not OperationCanceledException)` for the degradation path; on a null deserialized body return `FallbackProfile(userId)`.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): Keycloak directory — propagate cancellation, fallback on empty body"
```

## Task 7.9: Renderer locale fallback → `ru`

**Files:**
- Modify: `Infrastructure/Notifications/Email/HtmlTemplateEmailRenderer.cs` (`ResolveTemplateFile`, `RenderAsync` subject fallback)
- Test: `EmailNotificationTests.cs` (extend)

- [ ] **Step 1: Write the failing test.** Render with an unsupported locale (`fr`) — the rendered template and subject are the `ru` variants, not `en`. Test name: `Unknown_locale_falls_back_to_ru`.

- [ ] **Step 2: Run — expect FAIL** (`HtmlTemplateEmailRenderer.cs:91-101` falls back to `en`).

- [ ] **Step 3: Implement.** Change the fallback constant to `ru` (spec §19).

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): email renderer locale fallback to ru (§19)"
```

## Task 7.10: SSE registry minors — register-in-try, dictionary cleanup, base directory

**Files:**
- Modify: `Api/Endpoints/OrderEventsSseEndpoint.cs` (move `registry.Register` inside the `try`), `Infrastructure/Notifications/Sse/OrderSseConnectionRegistry.cs` (remove empty per-order list on `Unregister`), `Infrastructure/Notifications/Email/HtmlTemplateEmailRenderer.cs` (template root from `AppContext.BaseDirectory`)
- Test: `SseRegistryTests.cs` (extend — assert the dictionary key is removed when the last connection unregisters)

- [ ] **Step 1: Write the failing test.** After the last connection for an order unregisters, the registry's internal dictionary no longer contains that order id (no slow leak). Test name: `Registry_removes_empty_order_entry`.

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement.** In `Unregister`, under the lock, `if (list.Count == 0) _channels.TryRemove(orderId, out _)`. Move `registry.Register` inside the endpoint's `try` so a throw before the `try` cannot leak a registration. `HtmlTemplateEmailRenderer` resolves the template root from `AppContext.BaseDirectory`.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): SSE registry cleanup and renderer base-directory robustness"
```

## Task 7.11: Notification & observability test coverage

**Files:**
- Test: `EmailNotificationTests.cs`, `SseRegistryTests.cs`, `KeycloakAdminTokenProviderTests.cs` (extend)

- [ ] **Step 1: Write the tests** (coverage of existing behaviour; fix any real bug surfaced):
  - XSS: `GivenName = "<script>alert(1)</script>"` → email body contains `&lt;script&gt;`.
  - Locale fallback: missing/`null`/garbage `Locale` claim → `ru`.
  - SSE: concurrent connect/disconnect/publish on the registry under a `Parallel.For` — no exception, consistent state.
  - SSE heartbeat: an endpoint streaming test asserts a `:` comment within ~15s and that `OrderConfirmed` is received within 1s of publish (Alba SSE harness).
  - Keycloak: token caching reuses within expiry; refresh after `ExpirySkew`.

- [ ] **Step 2-4:** Run; fix anything red. All green; `dotnet build` 0 warnings.

- [ ] **Step 5: Commit.**

```powershell
git add tests/flights/
git commit -m "test(flights): cover email XSS escaping, SSE concurrency/heartbeat, Keycloak caching"
```

---

# Phase WS8 — NL-search

Closes: WHK-I2–I6 + minors, BLD-I4. **Depends on WS0.**

## Task 8.1: Inject `[today: yyyy-MM-dd]` into the NL-search prompt

**Files:**
- Modify: `apps/Travel.AI/NlSearch/NlSearchExtractor.cs` (add `DateOnly today` parameter, append the token to the user message)
- Modify: `apps/Travel.AI/NlSearch/NlSearchAiHandler.cs` (pass `time.GetUtcNow()`-derived date)
- Modify: `tests/Travel.Tests.AiEvals/.../NlSearchEvalRunner.cs` (pass a fixed date)
- Test: `tests/flights/Travel.Modules.Flights.Tests.Integration/NlSearch/NlSearchHandlerTests.cs` and/or a `Travel.AI` unit test with a fake `IChatClient` asserting the sent message contains `[today: ...]`

- [ ] **Step 1: Write the failing test.** With a fake `IChatClient` that captures the sent messages, `NlSearchExtractor.ExtractAsync(client, "из Москвы в Питер на выхах", today: new DateOnly(2026,5,14))` sends a user message containing `[today: 2026-05-14]`. Test name: `Extractor_injects_today_token`.

- [ ] **Step 2: Run — expect compile FAIL** (`ExtractAsync` has no `today` parameter).

- [ ] **Step 3: Implement.** Add `DateOnly today` to `ExtractAsync`; append `\n\n[today: {today:yyyy-MM-dd}]` to the user message. `NlSearchAiHandler` passes `DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime)` (it already has `TimeProvider` per `NlSearchAiHandler.cs:59`). The eval runner passes its fixed reference date.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git add apps/Travel.AI/ tests/
git commit -m "fix(ai): inject [today] token so NL-search date inference is deterministic"
```

## Task 8.2: `NlSearchHandler` — logging + cancellation rethrow

**Files:**
- Modify: `modules/flights/Travel.Modules.Flights.Application/Handlers/NlSearch/NlSearchHandler.cs`
- Test: `NlSearchHandlerTests.cs` (extend)

- [ ] **Step 1: Write the failing tests.** A cancelled `CancellationToken` propagates `OperationCanceledException` (not converted to `NlSearchUnparseable`). An unexpected exception (e.g. a fake bus that throws `InvalidOperationException`) is logged at `Warning`/`Error` before the typed error is returned. Test names: `Cancellation_propagates`, `Unexpected_failure_is_logged`.

- [ ] **Step 2: Run — expect FAIL** (`NlSearchHandler.cs:41-44` `catch (Exception)` with no filter, no logging).

- [ ] **Step 3: Implement.** Add `catch (OperationCanceledException) { throw; }` *before* the generic catch; inject `ILogger<...>` and `log.LogWarning(ex, ...)` in the generic catch before returning `FlightsErrors.NlSearchUnparseable`. Keep the explicit `catch (TimeoutException)`.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): NL-search handler — propagate cancellation, log unexpected failures"
```

## Task 8.3: AI-eval suite — real assertions, tighter tolerance, pass-rate

**Files:**
- Modify: `tests/Travel.Tests.AiEvals/.../NlSearchEvalRunner.cs`, `Cases/nl-search-cases.json`
- Test: same

- [ ] **Step 1: Rewrite the assertions.** Keep the skip-without-`ANTHROPIC_API_KEY` guard. For `Clear` cases: exact IATA match + departure date within ±1 day (spec §17). For `DateInference` cases: tighten `tolerance_days` to 1. For `Ambiguous` cases: assert *something* concrete (e.g. origin still resolves when given, or the result is a well-formed `SearchCriteria` with valid IATA codes) rather than `NotNull`. Add an aggregate assertion: across all run cases, pass-rate ≥ a threshold (e.g. 0.9) — a single regression fails the suite.

- [ ] **Step 2: Run** — with no key, suite skips (expected); if a key is available locally, run it and confirm the new assertions are meaningful.

- [ ] **Step 3:** No production code change unless an assertion reveals a prompt bug.

- [ ] **Step 4: Run — green (or skipped without key).**

- [ ] **Step 5: Commit.**

```powershell
git add tests/Travel.Tests.AiEvals/
git commit -m "test(ai): make NL-search eval assertions meaningful with a pass-rate gate"
```

## Task 8.4: Two-sided NL-search contract test

**Files:**
- Modify: `tests/Travel.Tests.Contract/Flights/NlSearchContractShapeTests.cs`
- Test: same

- [ ] **Step 1: Write the failing test.** Add `[Fact]`s that serialize the **Host-side** `Travel.Modules.Flights.Application.Contracts.NlSearchRequested` / `NlSearchParsed` records and assert their shape matches the same snapshot the AI-side records are checked against (or a structural/reflection equality assertion between the two record types — same property names, types, order). Test name: `Host_and_AI_nl_search_contracts_have_identical_shape`.

- [ ] **Step 2: Run — expect FAIL** if there is any drift; PASS if currently identical (then the test is a regression guard).

- [ ] **Step 3:** No production change unless drift exists — if it does, reconcile the two records.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "test(flights): pin both sides of the NL-search contract"
```

## Task 8.5: NL-search minors — correlation id, cost-ledger user id

**Files:**
- Modify: `modules/flights/.../NlSearchHandler.cs` (`CorrelationId` from `Activity.Current?.TraceId`), `apps/Travel.AI/NlSearch/NlSearchAiHandler.cs` (decide `user_id`)
- Test: extend `NlSearchHandlerTests.cs`

- [ ] **Step 1: Write the test.** `NlSearchRequested.CorrelationId` is derived from `Activity.Current?.TraceId` when an activity is present (not a fresh random GUID). Test name: `Nl_search_correlation_id_follows_trace`.

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement.** Use `Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString()` for the correlation id. For `user_id` in `ai.cost_ledger`: NL-search is anonymous in M1, so the column stays null — add a one-line code comment that it is intentionally null until authenticated AI features (M2); no behaviour change needed.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): derive NL-search correlation id from the active trace"
```

---

# Phase WS9 — Domain core minors

Closes: DOM-I2–I6 + minors. **Depends on WS0.**

## Task 9.1: `EquatableArray<T>` + apply to `OrderTicketed`/`OrderStatus`/`BookingAggregate` (D5)

**Files:**
- Create: `shared/dotnet/Travel.Shared.Abstractions/EquatableArray.cs`
- Modify: `Core/DomainEvents/OrderTicketed.cs`, `Core/Providers/Dtos/OrderStatus.cs`, `Core/Aggregates/BookingAggregate.cs`
- Test: `tests/.../Travel.Shared.Abstractions` tests if such a project exists, else `tests/flights/Travel.Modules.Flights.Tests.Unit/DomainEvents/DomainEventsTests.cs` (rewrite the misleading equality test)

- [ ] **Step 1: Write the failing test.** Two `OrderTicketed` events built with **separate arrays of equal contents** are `.ShouldBe(...)` equal; two with different contents are not. (The current test reuses one array instance and so passes trivially — rewrite it.) Plus an `EquatableArray<T>` unit test: structural equality, `GetHashCode` consistency, enumeration.

- [ ] **Step 2: Run — expect FAIL** (records compare `IReadOnlyList<string>` by reference).

- [ ] **Step 3: Implement.** A `readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>` wrapping `T[]` with element-wise equality + hash. Use it for `OrderTicketed.TicketNumbers`, `OrderStatus.TicketNumbers`, `BookingAggregate.TicketNumbers`. Ensure `[JsonConstructor]`/serialization still round-trips (add a `JsonConverter` for `EquatableArray<T>` if needed — verify with the Marten event round-trip test).

- [ ] **Step 4: Run — expect PASS**, including `Marten/BookingAggregateMartenTests`.

- [ ] **Step 5: Commit.**

```powershell
git add shared/ modules/flights/ tests/
git commit -m "fix(shared): add EquatableArray<T> for value-equality of event collections"
```

## Task 9.2: `Itinerary.Create` — round-trip continuity validation

**Files:**
- Modify: `Core/ValueObjects/Itinerary.cs`
- Test: `tests/flights/Travel.Modules.Flights.Tests.Unit/ValueObjects/ItineraryTests.cs` (extend)

- [ ] **Step 1: Write the failing test.** A 2-slice itinerary where `slices[1].Origin != slices[0].Destination` (or `slices[1].Destination != slices[0].Origin`) is rejected with `Itinerary.Discontinuous`; a proper round-trip is accepted. Test name: `Create_rejects_discontinuous_round_trip`.

- [ ] **Step 2: Run — expect FAIL** (`Itinerary.Create` checks only count + duration).

- [ ] **Step 3: Implement.** For a 2-slice itinerary, validate `slices[1].Origin == slices[0].Destination && slices[1].Destination == slices[0].Origin` → else `Error.Validation("Itinerary.Discontinuous", ...)`.

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): validate round-trip continuity in Itinerary.Create"
```

## Task 9.3: Delete `DateRange` (D4)

**Files:**
- Delete: `Core/ValueObjects/DateRange.cs`, `tests/.../ValueObjects/DateRangeTests.cs`
- Modify: base spec `docs/superpowers/specs/2026-05-13-flights-m1-design.md` §4.3 (remove the `DateRange` line) — *defer the spec edit to WS10 Task 10.3 to keep all doc edits together; here only delete the code + test*

- [ ] **Step 1:** Confirm `DateRange` has no references: `Select-String -Path "modules/**/*.cs","apps/**/*.cs" -Pattern "DateRange"`. If any production reference exists, this task is blocked — re-evaluate D4. Expected: only its own file + test.

- [ ] **Step 2:** Delete `DateRange.cs` and `DateRangeTests.cs`.

- [ ] **Step 3: Run** the unit suite + `dotnet build` — green, 0 warnings.

```powershell
dotnet build Travel.slnx
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit
```

- [ ] **Step 4: Commit.**

```powershell
git add -A modules/flights/ tests/flights/
git commit -m "refactor(flights): remove unused DateRange value object (D4)"
```

## Task 9.4: Typed identifier `default`/empty guards

**Files:**
- Modify: `Core/ValueObjects/Identifiers/{OfferId,OrderId,PaymentRef,RefundRef,AggregateId}.cs`
- Modify: handler/aggregate boundaries that should reject empty ids
- Test: `tests/flights/Travel.Modules.Flights.Tests.Unit/ValueObjects/IdentifiersTests.cs` (extend)

- [ ] **Step 1: Write the tests.** Each Guid-based id exposes `IsEmpty` (`Value == Guid.Empty`) and a static `None`; `default(OfferId).IsEmpty` is `true`. A handler boundary (e.g. `ConfirmOrderCommand` with `AggregateId == Guid.Empty`) is rejected with a validation error. Test names: `Identifier_default_is_empty`, `Handler_rejects_empty_aggregate_id` (the latter folds into Task 2.9's command validation — coordinate).

- [ ] **Step 2: Run — expect FAIL.**

- [ ] **Step 3: Implement.** Add `public bool IsEmpty => Value == Guid.Empty;` and `public static readonly XId None = new(Guid.Empty);` to each id struct. Guard empty ids at handler entry (consistent with Task 2.9).

- [ ] **Step 4: Run — expect PASS.**

- [ ] **Step 5: Commit.**

```powershell
git commit -am "fix(flights): add IsEmpty/None guards to typed identifiers"
```

## Task 9.5: Negative-branch value-object tests + `FlightsErrors` ErrorType tests

**Files:**
- Test: `tests/flights/Travel.Modules.Flights.Tests.Unit/ValueObjects/*Tests.cs`, `Errors/FlightsErrorsTests.cs`

- [ ] **Step 1: Write the tests** (coverage of existing behaviour; fix any real bug surfaced):
  - `IataCode`/`CurrencyCode`: assert the *specific* error code (`.Empty` / `.Length` / `.Format`), not just `ErrorType.Validation`.
  - `Money`: `Add` overflow behaviour; `ToString` under a non-invariant culture.
  - `PhoneNumber`: 15-digit accepted, 16-digit rejected (regex boundary).
  - `Slice`: discontinuity at segment index ≥ 2 (3+ segment case).
  - `Gender.Parse(null)` / `CabinClass.Parse(null)` — consistent behaviour.
  - `FlightsErrors`: each error resolves to the documented `ErrorType` (per §14).

- [ ] **Step 2-4:** Run; fix any real bug. All green.

- [ ] **Step 5: Commit.**

```powershell
git add tests/flights/
git commit -m "test(flights): cover value-object negative branches and error types"
```

---

# Phase WS10 — Docs, test infra, CI

Closes: PER-I4/I5/I6/I8 + minors, BLD-I2/I3/I6, NOB-I9, ADR drift. **Runs last** — docs reflect the final code.

## Task 10.1: Fix ADR drift (0015, 0017, 0018, 0019)

**Files:**
- Modify: `docs/adr/0015-booking-aggregate-event-model.md`, `0017-flights-idempotency-strategy.md`, `0018-duffel-webhook-inbox-outbox.md`, `0019-payment-gateway-abstraction.md`

- [ ] **Step 1:** Edit each ADR to match the implemented code:
  - **0015:** `OfferHeld` carries a *singular* `PassengerInfo` (D8) — replace the false "`PassengerInfo[]` forward-compat" claim with an honest note that M2 multi-passenger will require an event-schema evolution. Also reconcile the stream-name wording with the Guid stream identity.
  - **0017:** correct the routes to `/api/flights/orders/{hold,confirm,{id}/cancel}`; rename the column `request_hash` → `body_hash`. Note the in-flight-row + 2xx-only-cache behaviour from WS2 Task 2.6.
  - **0018:** replace the non-existent `BookingConfirmed` example with `OrderTicketed`; confirm the inbox/outbox description matches the WS1+WS3 implementation (real Wolverine outbox).
  - **0019:** correct the DI sample to the environment-guarded `AddSingleton` registration; describe enforcement as the runtime `TestOnlyGuard` (+ the marker-presence arch test), not "ArchUnitNET".

- [ ] **Step 2:** Build is unaffected; verify the ADRs render. Commit.

```powershell
git add docs/adr/
git commit -m "docs(arch): correct ADR drift in 0015, 0017, 0018, 0019"
```

## Task 10.2: New ADR 0021 + amend 0020

**Files:**
- Create: `docs/adr/0021-email-rendering-without-razor.md`
- Modify: `docs/adr/0020-nl-search-cross-service-contract.md`

- [ ] **Step 1:** Write ADR 0021 in the project ADR format (Context/Decision/Alternatives/Consequences): RazorLight 2.3.1 does not run on .NET 10; M1 email templates are static; decision is HTML token-replacement via `HtmlTemplateEmailRenderer` with explicit `WebUtility.HtmlEncode` of every token. Amend ADR 0020 with a section ratifying the JSON-snapshot contract test as the M1 mechanism (both contract sides live in one repo; drift caught at build by the two-sided test from WS8 Task 8.4) with full Pact deferred to M2.

- [ ] **Step 2: Commit.**

```powershell
git add docs/adr/
git commit -m "docs(arch): add ADR 0021 (email rendering) and ratify snapshot contract test in 0020"
```

## Task 10.3: Update the base M1 spec for ratified deviations

**Files:**
- Modify: `docs/superpowers/specs/2026-05-13-flights-m1-design.md`

- [ ] **Step 1:** Edit the base spec: §12.1 — note email rendering is HTML token-replacement (ref ADR 0021), not Razor. §4.3 — remove the `DateRange` value-object line (D4). §19/§15 — currency is a `?currency=` query param and locale an `Accept-Language` header (the code now matches the spec text, so confirm §19 already says this; if §19 was ambiguous, make it explicit). Add a one-line "Remediation" note at the top pointing to `2026-05-14-flights-m1-remediation-design.md`.

- [ ] **Step 2: Commit.**

```powershell
git add docs/superpowers/specs/
git commit -m "docs(flights): sync M1 spec with ratified remediation deviations"
```

## Task 10.4: `WebhookSimulator` mini-service

**Files:**
- Create: `tests/flights/Travel.Modules.Flights.WebhookSimulator/Program.cs`, `.csproj`
- Modify: `Travel.slnx` (add the project)
- Test: a smoke test that the simulator produces a webhook payload with a signature the real `DuffelWebhookVerifier` (from WS3) accepts

- [ ] **Step 1: Write the failing test.** In `tests/flights/Travel.Modules.Flights.Tests.Integration/Webhooks/WebhookSimulatorTests.cs`: the simulator emits an `order.created` documents-issued payload whose `Duffel-Signature` header verifies against `DuffelWebhookVerifier` with the shared test secret. Test name: `Simulator_emits_verifiable_signed_webhook`.

- [ ] **Step 2: Run — expect FAIL** (project does not exist).

- [ ] **Step 3: Implement.** A minimal-API mini-service (per spec §17.2) that, given an order id + event type, builds the Duffel-format JSON payload and signs it with the configured webhook secret using the **verified scheme from WS3 Task 3.1**, then POSTs it to `/webhooks/duffel` (or returns it for the test to POST). Add the project to `Travel.slnx`.

- [ ] **Step 4: Run — expect PASS.** `dotnet build Travel.slnx` — 0 warnings.

- [ ] **Step 5: Commit.**

```powershell
git add tests/flights/ Travel.slnx
git commit -m "test(flights): add Duffel WebhookSimulator mini-service (spec §17.2)"
```

## Task 10.5: CI — `test-integration` job

**Files:**
- Modify: `.github/workflows/ci.yml`

- [ ] **Step 1:** Add a `test-integration` job after `build`/`test-unit` that runs the Flights + Host integration suites with Docker available for Testcontainers:

```yaml
  test-integration:
    needs: [build]
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.203' }
      - uses: actions/cache@v4
        with:
          path: ~/.nuget/packages
          key:  nuget-${{ hashFiles('**/*.csproj','Directory.Packages.props') }}
      - run: dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration --filter "Category=Integration" --logger "trx;LogFileName=flights-integration.trx"
      - run: dotnet test tests/Travel.Host.Tests.Integration --filter "Category=Integration" --logger "trx;LogFileName=host-integration.trx"
```

(GitHub-hosted `ubuntu-latest` runners have Docker available for Testcontainers.) Verify the integration tests carry `[Trait("Category","Integration")]`; if some don't, add the trait.

- [ ] **Step 2:** Validate the YAML (`yamllint` or a dry parse). Commit.

```powershell
git add .github/workflows/ci.yml
git commit -m "ci: add integration-test job for Flights and Host suites"
```

## Task 10.6: `FlightsDbContext.OnConfiguring` — snake_case single source of truth

**Files:**
- Modify: `modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/FlightsDbContext.cs`
- Test: existing `FlightsDbContextTests.cs` stays green

- [ ] **Step 1:** Add `protected override void OnConfiguring(DbContextOptionsBuilder b) => b.UseSnakeCaseNamingConvention();` to `FlightsDbContext` (idempotent with the host registration at `Program.cs:36` and `FlightsDbContextFactory`).

- [ ] **Step 2: Run** `FlightsDbContextTests` + the persistence integration tests — green.

- [ ] **Step 3: Commit.**

```powershell
git add modules/flights/
git commit -m "fix(flights): make snake_case naming a FlightsDbContext invariant"
```

## Task 10.7: Final gate — README, format, full build & test

**Files:**
- Modify: `README.md` (note the realm dev password is dev-only; sync any walkthrough detail changed by WS6's currency/locale move)
- All changed files: `dotnet csharpier .`

- [ ] **Step 1:** Update `README.md`: mark `infra/keycloak/travel-realm.json`'s `dev123` as dev-only; if the search walkthrough showed a `currency` body field, change it to `?currency=`. 

- [ ] **Step 2:** Format everything: `dotnet csharpier .` and `npx biome format --write .` (only ts/js/json — likely no-op here).

- [ ] **Step 3: Full gate.**

```powershell
dotnet build Travel.slnx
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit
dotnet test tests/Travel.Tests.Architecture --filter "Category=Architecture"
dotnet test tests/flights/Travel.Modules.Flights.Tests.Integration --filter "Category=Integration"
dotnet test tests/Travel.Host.Tests.Integration --filter "Category=Integration"
dotnet test tests/Travel.Tests.Contract
```

All green; build 0 warnings.

- [ ] **Step 4: Commit.**

```powershell
git add -A
git commit -m "docs(flights): final remediation polish — README, formatting"
```

- [ ] **Step 5: Re-audit gate.** Re-dispatch a slimmed set of the audit reviewer subagents (per `superpowers:requesting-code-review`) against the new HEAD, scoped to the Critical/Important findings, to confirm closure and no regressions. Flip the §21 scorecard. Report results to the user.

---

## Self-Review

**Spec coverage:** every WS0–WS10 workstream and every audit-finding code (DOM/DUF/SRCH/SAGA/WHK/NOB/API/PER/BLD) in the design doc §7 traceability table maps to at least one task above. Decisions D1 (Task 2.3), D2 (Task 6.4), D3 (Task 6.1), D4 (Task 9.3 + 10.3), D5 (Task 9.1), D6 (Task 10.2 + 10.3), D7 (Task 10.2), D8 (Task 10.1) are each implemented. Architectural approaches A1 (WS1 + Tasks 2.2/3.2), A2 (Tasks 2.1/2.6/3.2), A3 (Task 3.1), A4 (Task 4.1) are covered.

**Placeholder scan:** no "TBD"/"implement later"/"add error handling" — every task names exact files, a concrete test with a name, and the shape of the fix. Two tasks (3.1 HMAC, 1.1 Wolverine setup) begin with an explicit *investigation* step because the exact external/foundation detail must be verified before coding — this is a deliberate, bounded step, not a placeholder.

**Type consistency:** `FlightsErrors.ConcurrencyConflict` (Task 2.1) is referenced by Tasks 2.3; `FlightsFeatureFlags` (Task 6.2) is referenced by Task 5.8 (dependency noted); `EquatableArray<T>` (Task 9.1) is used by `OrderTicketed`/`OrderStatus`/`BookingAggregate`; `OrderStatusKind` enum (Task 4.6) is self-contained; `GuardCanTicket`/`GuardCanRefund` (Task 3.3) and `GuardCanCancel` change (Task 2.4) live on `BookingAggregate`. The `today` parameter added to `PassengerInfo.Create` (Task 0.4) is consistently threaded.

**Cross-phase dependencies** are stated in each phase header and on Task 5.8. Executors running WS2–WS9 in parallel must complete WS0+WS1 first.

---

## Execution Handoff

**Plan complete and saved to `docs/superpowers/plans/2026-05-14-flights-m1-remediation.md`. Two execution options:**

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, two-stage review between tasks, fast iteration. Matches the design doc's chosen model (WS0+WS1 sequential, WS2–WS9 parallel, code-review subagent per workstream, three checkpoints).

**2. Inline Execution** — I execute tasks in this session using executing-plans, batch execution with checkpoints for review.

**Which approach?**
