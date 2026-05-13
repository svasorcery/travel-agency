# Flights M1 (Backend) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the production-grade backend for Flights flagship M1 — search (mixed bookable + deeplink), single-passenger booking saga (`OfferQuoted → Held → Confirmed → Ticketed → Refunded/Cancelled`), Duffel webhooks with inbox/outbox, idempotency, NL-search via Travel.AI cross-service, email + SSE notifications, full observability.

**Architecture:** Modular monolith (`Travel.Host`) — domain in `modules/flights/Core` (Marten ES `BookingAggregate` + Offer-hierarchy), application handlers in `Application`, providers + persistence + integrations in `Infrastructure`, WolverineFx.Http endpoints in `Api`. Travel.AI consumes `NlSearchRequested` via NATS and replies with parsed `SearchCriteria`. EF Core 10 owns read-models + idempotency + webhook inbox + deeplink-offer cache in schema `flights`; Marten owns BookingAggregate event stream.

**Tech Stack:** .NET 10, Wolverine 5 + Marten 8 + WolverineFx.Http, EF Core 10 + Npgsql, PostgreSQL 17 (Aspire), Redis (Aspire), NATS JetStream (Aspire), Duffel SDK via HttpClient, Travelpayouts Aviasales Data API v3 via HttpClient, MailKit + Mailpit, OpenTelemetry, Microsoft.Extensions.AI + Anthropic SDK, Polly, Pact.NET, xUnit v3 + Testcontainers + ArchUnitNET + Shouldly + Alba + WireMock.NET.

**Spec reference:** [`docs/superpowers/specs/2026-05-13-flights-m1-design.md`](../specs/2026-05-13-flights-m1-design.md) — full design rationale. This plan references spec sections by number.

**Scope split:** This is the **backend** plan. Angular UI + Playwright E2E + visual regression have their own plan ([`2026-05-13-flights-m1-frontend.md`](2026-05-13-flights-m1-frontend.md) — written separately). Backend plan completion = all API endpoints functional, integration / architecture / AI-eval / contract / WebhookSimulator tests green; spec §21 acceptance items 1-10 verified end-to-end via integration tests (item 11 README is in this plan; FE-dependent verification is in the FE plan).

---

## File Structure (high level)

```
shared/dotnet/Travel.Shared.Abstractions/
└── TestOnlyAttribute.cs                                      ← new in M1

modules/flights/
├── Travel.Modules.Flights.Core/
│   ├── ValueObjects/
│   │   ├── IataCode.cs, CurrencyCode.cs, Duration.cs, Gender.cs, PhoneNumber.cs
│   │   ├── Money.cs, CabinClass.cs, DateRange.cs, FareConditions.cs
│   │   ├── Segment.cs, Slice.cs, Itinerary.cs, PassengerInfo.cs, SearchCriteria.cs
│   │   ├── Identifiers/{OfferId,OrderId,PaymentRef,RefundRef,ProviderId,AggregateId}.cs
│   │   └── Offer/{Offer.cs, BookableOffer.cs, DeeplinkOffer.cs}
│   ├── DomainEvents/{OfferQuoted, OfferReQuoted, OfferHeld, PaymentAuthorized,
│   │                 OrderConfirmed, OrderTicketed, OrderCancelled, OrderRefunded}.cs
│   ├── Aggregates/BookingAggregate.cs
│   ├── Providers/{IFlightSearchProvider, IFlightBookingProvider, IPaymentGateway}.cs
│   ├── Providers/Dtos/{HeldOrder, ConfirmedOrder, OrderStatus, PaymentResult}.cs
│   ├── Errors/FlightsErrors.cs
│   └── Exceptions/InvalidBookingStateException.cs
│
├── Travel.Modules.Flights.Application/
│   ├── Commands/{QuoteOfferCommand, HoldOfferCommand, ConfirmOrderCommand,
│   │             CancelOrderCommand, ProcessDuffelWebhookCommand}.cs
│   ├── Queries/{SearchFlightsQuery, GetOrderQuery, ListOrdersQuery, NlSearchQuery}.cs
│   ├── Contracts/{NlSearchRequested, NlSearchParsed}.cs           ← cross-service via Wolverine
│   ├── Handlers/Search/{SearchFlightsHandler, OfferRanker, OfferDeduplicator, SearchCacheKey}.cs
│   ├── Handlers/Booking/{QuoteOfferHandler, HoldOfferHandler, ConfirmOrderHandler,
│   │                     CancelOrderHandler, GetOrderHandler, ListOrdersHandler}.cs
│   ├── Handlers/NlSearch/NlSearchHandler.cs                       ← in Host
│   ├── Handlers/Webhooks/DuffelWebhookHandler.cs
│   ├── Handlers/Notifications/{SendOrderConfirmationEmailHandler,
│   │                           SendOrderCancellationEmailHandler, PublishOrderSseHandler}.cs
│   └── Idempotency/{IIdempotencyStore, IdempotencyKey}.cs
│
├── Travel.Modules.Flights.Infrastructure/
│   ├── Providers/Duffel/
│   │   ├── DuffelOptions.cs, DuffelClient.cs, DuffelOfferMapper.cs, DuffelWebhookVerifier.cs
│   │   ├── DuffelFlightSearchProvider.cs, DuffelFlightBookingProvider.cs
│   │   └── Dto/{DuffelOfferDto, DuffelOrderDto, DuffelSliceDto, DuffelSegmentDto, DuffelWebhookEventDto}.cs
│   ├── Providers/Travelpayouts/
│   │   ├── TravelpayoutsOptions.cs, TravelpayoutsClient.cs
│   │   ├── TravelpayoutsSearchProvider.cs, TravelpayoutsOfferMapper.cs, TravelpayoutsDeeplinkBuilder.cs
│   │   └── Dto/PricesForDatesResponseDto.cs
│   ├── Payments/DuffelTestWalletPaymentGateway.cs
│   ├── ExternalServices/FrankfurterClient.cs                      ← FX rates side-car
│   ├── Cache/{SearchCacheRedis, FrankfurterRatesCache}.cs
│   ├── Persistence/
│   │   ├── FlightsDbContext.cs
│   │   ├── Entities/{IdempotencyKeyEntity, WebhookInboxEntity,
│   │   │              DeeplinkOfferCacheEntity, OrderReadModelEntity}.cs
│   │   ├── Configurations/{IdempotencyKeyConfig, WebhookInboxConfig,
│   │   │                   DeeplinkOfferCacheConfig, OrderReadModelConfig}.cs
│   │   ├── Repositories/{IdempotencyStore, DeeplinkOfferCacheRepository}.cs
│   │   └── Migrations/20260513_FlightsM1Init.cs
│   ├── Marten/BookingAggregateConfig.cs
│   ├── Notifications/Email/
│   │   ├── MailKitEmailSender.cs
│   │   └── Templates/{OrderConfirmation.ru, OrderConfirmation.en,
│   │                  OrderCancellation.ru, OrderCancellation.en}.cshtml
│   ├── Notifications/Sse/OrderSseConnectionRegistry.cs
│   ├── Observability/FlightsMetrics.cs
│   ├── BackgroundJobs/PurgeExpiredDeeplinkOffersHandler.cs
│   └── FlightsModuleStartup.cs                                    ← DI registration
│
└── Travel.Modules.Flights.Api/
    ├── Contracts/{SearchRequest, SearchResponse, NlSearchRequest,
    │              QuoteOfferRequest, QuotedOfferResponse,
    │              HoldOfferRequest, HeldOrderResponse,
    │              ConfirmOrderRequest, ConfirmedOrderResponse,
    │              CancelOrderRequest, OrderResponse, OrderListResponse,
    │              PartialFailureDto, OfferDto, ItineraryDto}.cs
    ├── Endpoints/{SearchEndpoint, NlSearchEndpoint, QuoteOfferEndpoint,
    │              HoldOfferEndpoint, ConfirmOrderEndpoint, CancelOrderEndpoint,
    │              GetOrderEndpoint, ListOrdersEndpoint,
    │              OrderEventsSseEndpoint, DuffelWebhookEndpoint}.cs
    └── Middleware/IdempotencyKeyMiddleware.cs

apps/Travel.AI/
├── NlSearch/
│   ├── NlSearchAiHandler.cs                                       ← consumer of NlSearchRequested
│   ├── NlSearchPrompts.cs
│   └── ParsedSearchCriteriaDto.cs
└── Persistence/
    ├── AiDbContext.cs
    ├── Entities/CostLedgerEntry.cs
    └── Migrations/20260513_CostLedgerInit.cs

tests/
├── flights/Travel.Modules.Flights.Tests.Unit/                    (scaffolded by Foundation)
│   ├── ValueObjects/, DomainEvents/, Aggregates/, Errors/, Search/
├── flights/Travel.Modules.Flights.Tests.Integration/             (scaffolded by Foundation)
│   ├── Search/, Booking/, Webhooks/, NlSearch/, Notifications/, Idempotency/
├── flights/Travel.Modules.Flights.Tests.AiEvals/                 ← new project for AI-evals
│   ├── NlSearchEvalRunner.cs
│   └── Cases/nl-search-cases.json
├── flights/Travel.Modules.Flights.WebhookSimulator/              ← new mini-service for E2E
│   └── Program.cs
├── Travel.Tests.Architecture/Flights/                            (extend scaffold)
└── Travel.Tests.Contract/Flights/NlSearchContract.cs             (extend scaffold)

prompts/v1/flights/nl-search.system.md

docs/adr/
├── 0013-flights-provider-abstraction.md
├── 0014-mixed-aggregation-bookable-deeplink.md
├── 0015-booking-aggregate-event-model.md
├── 0016-booking-saga-via-marten-es.md
├── 0017-flights-idempotency-strategy.md
├── 0018-duffel-webhook-inbox-outbox.md
├── 0019-payment-gateway-abstraction.md
└── 0020-nl-search-cross-service-contract.md

infra/keycloak/travel-realm.json                                  ← update: add flights:book scope
```

---

## Conventions

- **Working directory:** `d:\_Projects\_github\travel-agency`. All paths in tasks are relative to this root.
- **Branch:** `flights-m1` (already created from `master` at 02508ac).
- **Commit format:** Conventional commits. Allowed scopes per `commitlint.config.mjs`: `flights`, `host`, `ai`, `shared`, `arch`, `test`, `docs`, `infra`, `tooling`, `ci`, `deps`.
- **TDD:** Each task starts with the failing test. Run it, see it fail, implement, run, see it pass, commit.
- **`TimeProvider`:** Inject; never `DateTime.UtcNow` in production code. Tests use `FakeTimeProvider` (Microsoft.Extensions.TimeProvider.Testing — already pinned in Foundation Directory.Packages.props).
- **Result type:** `ErrorOr<T>` for all handler returns. Maps to `ProblemDetails` via `Travel.Shared.Web.ErrorOrExtensions.ToProblemDetails(List<Error>)`.
- **Naming:** Past-tense domain events. Handlers end with `Handler`. One class per file. Files mirror namespace.
- **External DTOs:** never leave `Infrastructure`. Each provider has its own `Dto/` folder for raw responses.

---


# Phase 1 — TestOnly marker

## Task 1: Add `TestOnlyAttribute` to Shared.Abstractions

**Files:**
- Create: `shared/dotnet/Travel.Shared.Abstractions/TestOnlyAttribute.cs`
- Test: `tests/Travel.Tests.Architecture/Flights/TestOnlyAttributeTests.cs`

- [ ] **Step 1: Write the failing architecture test**

Create `tests/Travel.Tests.Architecture/Flights/TestOnlyAttributeTests.cs`:

```csharp
using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using Travel.Shared.Abstractions;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Travel.Tests.Architecture.Flights;

public sealed class TestOnlyAttributeTests : ArchitectureTestBase
{
    [Fact]
    public void TestOnly_attribute_exists_in_Shared_Abstractions()
    {
        var classes = Classes()
            .That().HaveName(nameof(TestOnlyAttribute))
            .And().ResideInAssembly(typeof(IDomainEvent).Assembly);

        classes.Should().Exist().Check(Architecture);
    }

    [Fact]
    public void TestOnly_attribute_inherits_from_System_Attribute()
    {
        var rule = Classes()
            .That().HaveName(nameof(TestOnlyAttribute))
            .Should().BeAssignableTo(typeof(Attribute));

        rule.Check(Architecture);
    }
}
```

- [ ] **Step 2: Run the tests; expect compile failure**

```bash
dotnet test tests/Travel.Tests.Architecture --filter "FullyQualifiedName~TestOnlyAttributeTests" --no-restore
```

Expected: build error — `TestOnlyAttribute` not found.

- [ ] **Step 3: Create the attribute**

Create `shared/dotnet/Travel.Shared.Abstractions/TestOnlyAttribute.cs`:

```csharp
namespace Travel.Shared.Abstractions;

/// <summary>
/// Marks a class as test-only / sandbox-only. ArchUnit tests forbid registration
/// of [TestOnly] classes in production DI configurations.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class TestOnlyAttribute : Attribute;
```

- [ ] **Step 4: Run the tests; expect green**

```bash
dotnet test tests/Travel.Tests.Architecture --filter "FullyQualifiedName~TestOnlyAttributeTests"
```

Expected: 2 tests pass.

- [ ] **Step 5: Commit**

```bash
git add shared/dotnet/Travel.Shared.Abstractions/TestOnlyAttribute.cs tests/Travel.Tests.Architecture/Flights/TestOnlyAttributeTests.cs
git commit -m "feat(shared): add [TestOnly] attribute marker for sandbox-only types"
```

---

> **Task convention (applies to every task below).** Task 1 is the canonical fully-expanded TDD task. Every subsequent task follows the same rhythm — write failing test → run (FAIL) → implement → run (PASS) → commit — but is presented condensed: code shapes, test commands, commit messages are given inline. Treat each `- [ ] **Step …**` as one bite-sized action (2-5 min). If a task lists multiple types/files in one step, apply the same TDD rhythm per file.

---

# Phase 2 — Domain primitives (value objects)

All value objects live in `modules/flights/Travel.Modules.Flights.Core/ValueObjects/`. Pattern: `sealed record`, private ctor, `static ErrorOr<T> Create(...)` factory. Tests in `tests/flights/Travel.Modules.Flights.Tests.Unit/ValueObjects/` (xUnit v3 + Shouldly).

## Task 2: Atomic value objects — IataCode, CurrencyCode, Duration, Gender, PhoneNumber

- [ ] **Step 1: Implement `IataCode`** — wraps `string`. Validation: non-empty, length=3, A-Z only. Error codes: `IataCode.Empty`, `IataCode.Length`, `IataCode.Format`. Tests: valid `"LED"`/`"DME"`/`"JFK"`; invalid `"led"`, `"LE"`, `"LEDX"`, `"LE1"`, `""`, `"   "`. Structural equality.
- [ ] **Step 2: Implement `CurrencyCode`** — wraps `string` (ISO 4217). Validation: non-empty, length=3, A-Z. Same error-code shape. Tests: `"RUB"`/`"USD"`/`"EUR"` valid; `"rub"`/`"RU"`/`"RUBS"`/`""` invalid.
- [ ] **Step 3: Implement `Duration`** — wraps `TimeSpan`. `Create(TimeSpan)`: require `> 0` and `< 48h`. Errors: `Duration.NonPositive`, `Duration.TooLong`.
- [ ] **Step 4: Implement `Gender`** — closed type with static `Male`, `Female`, `Unspecified` (Duffel terminology). `static ErrorOr<Gender> Parse(string)` maps `m|male → Male`, `f|female → Female`, `u|unspecified → Unspecified` case-insensitively. Anything else → `Error.Validation("Gender.Unknown", ...)`.
- [ ] **Step 5: Implement `PhoneNumber`** — wraps E.164 string. Validation: regex `^\+[1-9]\d{7,14}$`. Tests: `"+79161234567"` ok; `"79161234567"`, `"+0"`, `"abc"`, `""` invalid.
- [ ] **Step 6: Run tests**

```bash
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit --filter "FullyQualifiedName~ValueObjects" --no-restore
```

- [ ] **Step 7: Commit**

```bash
git add modules/flights/Travel.Modules.Flights.Core/ValueObjects/{IataCode,CurrencyCode,Duration,Gender,PhoneNumber}.cs tests/flights/Travel.Modules.Flights.Tests.Unit/ValueObjects/{IataCode,CurrencyCode,Duration,Gender,PhoneNumber}Tests.cs
git commit -m "feat(flights): add atomic value objects (IataCode, CurrencyCode, Duration, Gender, PhoneNumber)"
```

---

## Task 3: `Money` value object with currency-safe arithmetic

```csharp
// modules/flights/Travel.Modules.Flights.Core/ValueObjects/Money.cs
using ErrorOr;
namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record Money
{
    public decimal Amount { get; }
    public CurrencyCode Currency { get; }
    private Money(decimal amount, CurrencyCode currency) { Amount = amount; Currency = currency; }

    public static ErrorOr<Money> Create(decimal amount, CurrencyCode currency)
    {
        if (amount < 0m) return Error.Validation("Money.Negative", "Amount must be non-negative.");
        return new Money(amount, currency);
    }

    public ErrorOr<Money> Add(Money other) => Currency != other.Currency
        ? Error.Validation("Money.CurrencyMismatch", $"Cannot add {Currency} and {other.Currency}.")
        : new Money(Amount + other.Amount, Currency);

    public override string ToString() => $"{Amount:N2} {Currency.Value}";
}
```

- [ ] **Step 1: Write `MoneyTests` covering** positive ok, zero ok, negative → `Money.Negative`, `Add` same-currency sums, `Add` different-currency → `Money.CurrencyMismatch`.
- [ ] **Step 2: FAIL → implement (above) → PASS → commit**

```bash
git commit -m "feat(flights): add Money value object with currency-safe arithmetic"
```

---

## Task 4: Geographic / temporal value objects — CabinClass, DateRange, FareConditions, Segment, Slice, Itinerary

- [ ] **Step 1: `CabinClass`** — closed type with static `Economy`, `PremiumEconomy`, `Business`, `First`. `Parse(string)` maps Duffel cabin codes (`economy`, `premium_economy`, `business`, `first`) case-insensitively; `basic_economy` collapses to `Economy`; unknown → `Error.Validation("CabinClass.Unknown", ...)`.
- [ ] **Step 2: `DateRange`** — `record DateRange(DateOnly From, DateOnly To)` with `Create(from, to)` requiring `to >= from` (error `DateRange.Inverted`). Expose `int LengthInDays`.
- [ ] **Step 3: `FareConditions`** — `record FareConditions(bool ChangeAllowed, bool RefundAllowed, string? FareBasisCode, string? CabinClassMarketing)`. No invariants in M1; structural record; no `Create`.
- [ ] **Step 4: `Segment`** — fields `IataCode Origin, IataCode Destination, DateTimeOffset DepartAt, DateTimeOffset ArriveAt, string CarrierCode, string FlightNumber, CabinClass Cabin`. `Create(...)` errors: `Segment.NonPositiveDuration` (arrive ≤ depart), `Segment.SameOriginDestination`, `Segment.CarrierEmpty`, `Segment.FlightNumberEmpty`. Computed property `TimeSpan Duration => ArriveAt - DepartAt`.
- [ ] **Step 5: `Slice`** — `Create(IReadOnlyList<Segment>)`: require non-empty; for each `i ≥ 1` validate `segments[i-1].Destination == segments[i].Origin` (`Slice.Discontinuous`) and `segments[i].DepartAt >= segments[i-1].ArriveAt` (`Slice.TimeInversion`). Computes `Origin`, `Destination`, total `Duration` from first depart to last arrive.
- [ ] **Step 6: `Itinerary`** — `Create(IReadOnlyList<Slice>)`: require 1 or 2 slices (`Itinerary.NoSlices`, `Itinerary.TooManySlices` — 3+ rejected as M2). `TotalDuration` = sum of slice durations. Helpers `IsOneWay`, `IsRoundTrip`.
- [ ] **Step 7: Run tests, commit**

```bash
git commit -m "feat(flights): add geographic/temporal value objects (CabinClass, DateRange, FareConditions, Segment, Slice, Itinerary)"
```

---

## Task 5: `PassengerInfo` and `SearchCriteria`

- [ ] **Step 1: `PassengerInfo`** — fields `string GivenName, string FamilyName, DateOnly DateOfBirth, Gender Gender, string Email, PhoneNumber Phone`. Validation in `Create`: trim and reject blank `GivenName`/`FamilyName` (`PassengerInfo.GivenNameEmpty`, `...FamilyNameEmpty`); reject DOB in future (`PassengerInfo.DateOfBirthFuture`); reject invalid email via `System.Net.Mail.MailAddress` (`PassengerInfo.EmailInvalid`).
- [ ] **Step 2: `SearchCriteria`** — fields `IataCode Origin, IataCode Destination, DateOnly DepartureDate, DateOnly? ReturnDate, int PassengerCount, CabinClass CabinClass, CurrencyCode Currency`. Validation: `Origin != Destination`, `ReturnDate >= DepartureDate` if set, `PassengerCount == 1` for M1 (keep numeric space `1..9` for future). Helper `IsRoundTrip`.
- [ ] **Step 3: Tests** — `PassengerInfo`: valid build, blank name, future DOB, invalid email. `SearchCriteria`: one-way ok, round-trip ok, return-before-departure rejected, same airport rejected, pax 0/10 rejected.
- [ ] **Step 4: Run, commit**

```bash
git commit -m "feat(flights): add PassengerInfo and SearchCriteria value objects"
```

---

## Task 6: Strongly-typed identifiers and Offer hierarchy

- [ ] **Step 1: Five Guid-based identifiers under `ValueObjects/Identifiers/`** — `OfferId`, `OrderId`, `PaymentRef`, `RefundRef`, `AggregateId`. Pattern:

```csharp
namespace Travel.Modules.Flights.Core.ValueObjects.Identifiers;

public readonly record struct OfferId(Guid Value)
{
    public static OfferId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString("N");
}
```

And `ProviderId`:

```csharp
public readonly record struct ProviderId(string Value)
{
    public static ProviderId Duffel { get; } = new("duffel");
    public static ProviderId Travelpayouts { get; } = new("travelpayouts");
    public override string ToString() => Value;
}
```

- [ ] **Step 2: Tests** — for each Guid-based id: `New()` produces unique values; equality structural. For `ProviderId`: `ProviderId.Duffel == new ProviderId("duffel")`.
- [ ] **Step 3: Implement `Offer` hierarchy under `ValueObjects/Offer/`**

```csharp
// Offer.cs
public abstract record Offer(
    OfferId Id, Itinerary Itinerary, Money TotalAmount, ProviderId Provider, DateTimeOffset FetchedAt);

// BookableOffer.cs
public sealed record BookableOffer(
    OfferId Id, Itinerary Itinerary, Money TotalAmount, ProviderId Provider, DateTimeOffset FetchedAt,
    DateTimeOffset ExpiresAt, FareConditions FareConditions, string ProviderOfferRef)
    : Offer(Id, Itinerary, TotalAmount, Provider, FetchedAt);

// DeeplinkOffer.cs
public sealed record DeeplinkOffer(
    OfferId Id, Itinerary Itinerary, Money TotalAmount, ProviderId Provider, DateTimeOffset FetchedAt,
    Uri DeeplinkUrl, string PartnerName)
    : Offer(Id, Itinerary, TotalAmount, Provider, FetchedAt);
```

- [ ] **Step 4: `OfferTests`** — assert `BookableOffer is Offer`, `DeeplinkOffer is Offer`, pattern-match distinguishes subtypes.
- [ ] **Step 5: Run all VO tests, commit**

```bash
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit --filter "FullyQualifiedName~ValueObjects"
git commit -m "feat(flights): add typed identifiers and discriminated Offer hierarchy"
```

---

# Phase 3 — Domain events

## Task 7: Eight `IDomainEvent`-implementing records

**Files:** `modules/flights/Travel.Modules.Flights.Core/DomainEvents/{OfferQuoted, OfferReQuoted, OfferHeld, PaymentAuthorized, OrderConfirmed, OrderTicketed, OrderCancelled, OrderRefunded}.cs`

All events implement `IDomainEvent` from `Travel.Shared.Abstractions`. Past-tense naming. Stored in Marten stream `BookingAggregate-{id}`. Per spec §4.2.

```csharp
// modules/flights/Travel.Modules.Flights.Core/DomainEvents/OfferQuoted.cs
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.DomainEvents;

public sealed record OfferQuoted(
    OfferId OfferId,
    Itinerary Itinerary,
    Money TotalAmount,
    DateTimeOffset ExpiresAt,
    string ProviderRef,
    DateTimeOffset QuotedAt) : IDomainEvent;
```

Apply the same shape (one file per event, `sealed record … : IDomainEvent`) for the remaining seven. Payload fields per spec §4.2:

- `OfferReQuoted(OfferId OfferId, Money OldAmount, Money NewAmount, DateTimeOffset ReQuotedAt)`
- `OfferHeld(string OrderId, PassengerInfo Passenger, DateTimeOffset HeldUntil, DateTimeOffset HeldAt)`
- `PaymentAuthorized(PaymentRef PaymentRef, Money Amount, DateTimeOffset AuthorizedAt)`
- `OrderConfirmed(string OrderId, PaymentRef PaymentRef, DateTimeOffset ConfirmedAt)`
- `OrderTicketed(IReadOnlyList<string> TicketNumbers, DateTimeOffset TicketedAt)`
- `OrderCancelled(CancelReason Reason, DateTimeOffset CancelledAt)` — `enum CancelReason { User, Airline, System }` defined alongside in same folder
- `OrderRefunded(RefundRef RefundRef, Money RefundedAmount, RefundInitiator InitiatedBy, DateTimeOffset RefundedAt)` — `enum RefundInitiator { Airline }` (M1 only allows airline-initiated refunds — see ADR 0015)

- [ ] **Step 1: Add `IDomainEvent` is already provided by Foundation** — verify `shared/dotnet/Travel.Shared.Abstractions/IDomainEvent.cs` exists. No new code there.
- [ ] **Step 2: Write `DomainEventsTests.cs`** asserting each event type is a `record`, implements `IDomainEvent`, and equality is structural. One single test class with one fact per event type.
- [ ] **Step 3: Run (FAIL) → implement each event file → run (PASS).**
- [ ] **Step 4: Commit**

```bash
git add modules/flights/Travel.Modules.Flights.Core/DomainEvents/ tests/flights/Travel.Modules.Flights.Tests.Unit/DomainEvents/
git commit -m "feat(flights): add 8 BookingAggregate domain events"
```

---

# Phase 4 — `BookingAggregate` (Marten event-sourced)

Per spec §4.1, §4.2 and ADR `0015`/`0016`. Aggregate state lives in events. Marten will apply events via `Apply(...)` conventions.

## Task 8: `InvalidBookingStateException` and `BookingAggregate` skeleton

**Files:**
- Create: `modules/flights/Travel.Modules.Flights.Core/Exceptions/InvalidBookingStateException.cs`
- Create: `modules/flights/Travel.Modules.Flights.Core/Aggregates/BookingAggregate.cs`

- [ ] **Step 1: Exception**

```csharp
namespace Travel.Modules.Flights.Core.Exceptions;

public sealed class InvalidBookingStateException(string message) : Exception(message);
```

- [ ] **Step 2: Aggregate skeleton with `BookingStatus` enum**

```csharp
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Exceptions;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;

namespace Travel.Modules.Flights.Core.Aggregates;

public enum BookingStatus
{
    None = 0,
    OfferQuoted,
    Held,
    Confirmed,
    Ticketed,
    Cancelled,
    Refunded
}

public sealed class BookingAggregate
{
    public Guid Id { get; private set; }
    public int Version { get; private set; }
    public BookingStatus Status { get; private set; } = BookingStatus.None;
    public OfferId? OfferId { get; private set; }
    public string? ProviderOrderId { get; private set; }
    public Itinerary? Itinerary { get; private set; }
    public Money? TotalAmount { get; private set; }
    public DateTimeOffset? ExpiresAt { get; private set; }
    public PassengerInfo? Passenger { get; private set; }
    public PaymentRef? PaymentRef { get; private set; }
    public IReadOnlyList<string> TicketNumbers { get; private set; } = Array.Empty<string>();

    // Marten convention: parameterless ctor + Apply methods for replay
    public BookingAggregate() { }
}
```

- [ ] **Step 3: Commit**

```bash
git add modules/flights/Travel.Modules.Flights.Core/{Aggregates,Exceptions}/
git commit -m "feat(flights): add BookingAggregate skeleton with BookingStatus"
```

---

## Task 9: `Apply` methods for each domain event (Marten convention)

Marten's event sourcing calls `void Apply(EventType evt)` for each event type during stream rebuild. Add one `Apply` per event. The `Apply` methods update state but **do not validate** (validation is in command handlers — Apply runs on replay where invariants are by-construction).

- [ ] **Step 1: Write `BookingAggregateApplyTests`** that, for each event, creates a fresh aggregate, calls `Apply`, asserts state transitions. Example:

```csharp
using Shouldly;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
// ... usings for VO

namespace Travel.Modules.Flights.Tests.Unit.Aggregates;

public sealed class BookingAggregateApplyTests
{
    [Fact]
    public void Apply_OfferQuoted_sets_status_and_offer_fields()
    {
        var agg = new BookingAggregate();
        var evt = Sample.OfferQuoted();
        agg.Apply(evt);

        agg.Status.ShouldBe(BookingStatus.OfferQuoted);
        agg.OfferId.ShouldBe(evt.OfferId);
        agg.Itinerary.ShouldBe(evt.Itinerary);
        agg.TotalAmount.ShouldBe(evt.TotalAmount);
        agg.ExpiresAt.ShouldBe(evt.ExpiresAt);
    }

    [Fact]
    public void Apply_OfferHeld_after_OfferQuoted_transitions_to_Held()
    {
        var agg = new BookingAggregate();
        agg.Apply(Sample.OfferQuoted());
        agg.Apply(Sample.OfferHeld());
        agg.Status.ShouldBe(BookingStatus.Held);
        agg.ProviderOrderId.ShouldNotBeNullOrEmpty();
    }

    // … one fact per remaining event covering its state transitions
}
```

Put fixture builders in a private static `Sample` class within the test file (DRY).

- [ ] **Step 2: Implement `Apply` methods** (add to `BookingAggregate.cs`)

```csharp
public void Apply(OfferQuoted e)
{
    Status = BookingStatus.OfferQuoted;
    OfferId = e.OfferId;
    Itinerary = e.Itinerary;
    TotalAmount = e.TotalAmount;
    ExpiresAt = e.ExpiresAt;
}

public void Apply(OfferReQuoted e) { TotalAmount = e.NewAmount; }

public void Apply(OfferHeld e)
{
    Status = BookingStatus.Held;
    ProviderOrderId = e.OrderId;
    Passenger = e.Passenger;
    ExpiresAt = e.HeldUntil;
}

public void Apply(PaymentAuthorized e) { PaymentRef = e.PaymentRef; }

public void Apply(OrderConfirmed e)
{
    Status = BookingStatus.Confirmed;
    ProviderOrderId = e.OrderId;
    PaymentRef = e.PaymentRef;
}

public void Apply(OrderTicketed e)
{
    Status = BookingStatus.Ticketed;
    TicketNumbers = e.TicketNumbers;
}

public void Apply(OrderCancelled e) { Status = BookingStatus.Cancelled; }

public void Apply(OrderRefunded e) { Status = BookingStatus.Refunded; }
```

- [ ] **Step 3: Add transition guards (`Guard*` methods used by handlers, not by Apply)**

```csharp
public void GuardCanHold()
{
    if (Status is not BookingStatus.OfferQuoted)
        throw new InvalidBookingStateException(
            $"Cannot hold an offer when booking is in state {Status}.");
}

public void GuardCanConfirm()
{
    if (Status is not BookingStatus.Held)
        throw new InvalidBookingStateException(
            $"Cannot confirm when booking is in state {Status}.");
}

public void GuardCanCancel()
{
    if (Status is BookingStatus.Cancelled or BookingStatus.Refunded)
        throw new InvalidBookingStateException(
            $"Cannot cancel when booking is in state {Status}.");
}

public void GuardOfferNotExpired(TimeProvider time)
{
    if (ExpiresAt is { } e && e <= time.GetUtcNow())
        throw new InvalidBookingStateException("Offer has expired.");
}
```

Add `GuardCanCancel`-style facts: cancel from `Cancelled` throws, cancel from `Refunded` throws, but `Ticketed → Cancelled` is allowed in M1 (no business rule yet against it).

- [ ] **Step 4: Run tests; expect green; commit**

```bash
dotnet test tests/flights/Travel.Modules.Flights.Tests.Unit --filter "FullyQualifiedName~Aggregates"
git commit -m "feat(flights): add BookingAggregate Apply methods and transition guards"
```

---

# Phase 5 — Provider abstractions (Core)

Per spec §5.1 and ADR `0013`/`0019`.

## Task 10: `IFlightSearchProvider`, `IFlightBookingProvider`, `IPaymentGateway` + supporting DTOs

**Files:**
- Create: `modules/flights/Travel.Modules.Flights.Core/Providers/{IFlightSearchProvider,IFlightBookingProvider,IPaymentGateway}.cs`
- Create: `modules/flights/Travel.Modules.Flights.Core/Providers/Dtos/{HeldOrder,ConfirmedOrder,OrderStatus,PaymentResult}.cs`

- [ ] **Step 1: Provider supporting DTOs** (still in Core — these are domain-side return types, not external DTOs)

```csharp
// Dtos/HeldOrder.cs
public sealed record HeldOrder(
    string ProviderOrderId, DateTimeOffset HeldUntil);

// Dtos/ConfirmedOrder.cs
public sealed record ConfirmedOrder(
    string ProviderOrderId, DateTimeOffset ConfirmedAt);

// Dtos/OrderStatus.cs
public sealed record OrderStatus(
    string ProviderOrderId, string Status, IReadOnlyList<string> TicketNumbers);
```

- [ ] **Step 2: `IFlightSearchProvider`**

```csharp
using ErrorOr;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;

namespace Travel.Modules.Flights.Core.Providers;

public interface IFlightSearchProvider
{
    ProviderId Id { get; }
    Task<ErrorOr<IReadOnlyList<Offer>>> SearchAsync(SearchCriteria criteria, CancellationToken ct);
}
```

- [ ] **Step 3: `IFlightBookingProvider`**

```csharp
public interface IFlightBookingProvider
{
    ProviderId Id { get; }
    Task<ErrorOr<BookableOffer>> RefreshOfferAsync(string providerOfferRef, CancellationToken ct);
    Task<ErrorOr<HeldOrder>> HoldOfferAsync(BookableOffer offer, PassengerInfo passenger, CancellationToken ct);
    Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(string providerOrderId, PaymentRef payment, CancellationToken ct);
    Task<ErrorOr<Success>> CancelOrderAsync(string providerOrderId, CancellationToken ct);
    Task<ErrorOr<OrderStatus>> GetOrderStatusAsync(string providerOrderId, CancellationToken ct);
}
```

`Success` is from `ErrorOr` package (`ErrorOr.Success`).

- [ ] **Step 4: `IPaymentGateway`**

```csharp
public interface IPaymentGateway
{
    Task<ErrorOr<PaymentRef>> AuthorizeAsync(Money amount, string idempotencyKey, CancellationToken ct);
    Task<ErrorOr<Success>> CaptureAsync(PaymentRef payment, CancellationToken ct);
    Task<ErrorOr<RefundRef>> RefundAsync(PaymentRef payment, Money amount, CancellationToken ct);
}
```

- [ ] **Step 5: No tests needed** (interfaces). Commit.

```bash
git add modules/flights/Travel.Modules.Flights.Core/Providers/
git commit -m "feat(flights): add IFlightSearchProvider / IFlightBookingProvider / IPaymentGateway contracts"
```

---

# Phase 6 — Error catalog

## Task 11: `FlightsErrors` static catalog

**Files:** `modules/flights/Travel.Modules.Flights.Core/Errors/FlightsErrors.cs`

```csharp
using ErrorOr;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Core.Errors;

public static class FlightsErrors
{
    public static Error OfferExpired => Error.Validation("Flights.OfferExpired",
        "Offer has expired, please refresh.");
    public static Error OfferNotFound(string offerRef) => Error.NotFound("Flights.OfferNotFound",
        $"Offer '{offerRef}' not found.");
    public static Error PriceChanged(Money old, Money @new) => Error.Conflict("Flights.PriceChanged",
        $"Price changed from {old} to {@new}.");
    public static Error ProviderUnavailable(string provider) => Error.Failure("Flights.ProviderUnavailable",
        $"Provider {provider} unavailable.");
    public static Error ProviderRateLimited(string provider) => Error.Failure("Flights.ProviderRateLimited",
        $"Provider {provider} rate limited.");
    public static Error PaymentFailed(string reason) => Error.Failure("Flights.PaymentFailed", reason);
    public static Error PassengerInvalid(string detail) => Error.Validation("Flights.PassengerInvalid", detail);
    public static Error OrderNotCancellable(string reason) => Error.Conflict("Flights.OrderNotCancellable", reason);
    public static Error IdempotencyConflict => Error.Conflict("Flights.IdempotencyConflict",
        "Idempotency key reused with different payload.");
    public static Error NlSearchUnparseable => Error.Validation("Flights.NlSearchUnparseable",
        "Could not parse the query.");
}
```

- [ ] **Step 1: Implement file above.**
- [ ] **Step 2: Smoke test** — assert all error codes start with `"Flights."` and resolve to the documented `ErrorType`.
- [ ] **Step 3: Commit**

```bash
git commit -m "feat(flights): add FlightsErrors catalog"
```

---

# Phase 7 — ADRs for Core (parallel to Core work)

## Task 12: Write four ADRs covering Core decisions

**Files:**
- Create: `docs/adr/0013-flights-provider-abstraction.md`
- Create: `docs/adr/0014-mixed-aggregation-bookable-deeplink.md`
- Create: `docs/adr/0015-booking-aggregate-event-model.md`
- Create: `docs/adr/0019-payment-gateway-abstraction.md`

Each ADR follows project format (see `docs/adr/0001-modular-monolith.md` for shape — Title, Status, Context, Decision, Consequences). Use the `adr-writer` subagent if convenient: dispatch with the relevant spec section as input.

Content checklist:
- `0013` — references spec §3 #1 and §5. Decision: capability-segregated. Alternatives rejected. Consequences: ISP, Travelpayouts honestly read-only.
- `0014` — references spec §3 #2, §6. Decision: discriminated `Offer` hierarchy + single mixed list with `партнёр` badge. Consequences: type-safe booking, UX honesty.
- `0015` — references spec §4. Decision: BookingAggregate event-sourced via Marten; PII plaintext in M1, encryption in M2.
- `0019` — references spec §3 #11, §8. Decision: `IPaymentGateway` with `[TestOnly]` Duffel implementation. Consequences: extensible to Stripe later; ArchUnit enforces no production registration.

- [ ] **Step 1: Write ADRs (one commit each, or one combined commit).**
- [ ] **Step 2: Commit**

```bash
git add docs/adr/{0013,0014,0015,0019}-*.md
git commit -m "docs(adr): add ADRs 0013/0014/0015/0019 for Core domain decisions"
```

---

# Phase 8 — Persistence (EF Core + Marten)

Per spec §16. Five EF tables in schema `flights` + Marten aggregate stream config. Foundation provided `FlightsDbContext`-ready hook in `apps/Travel.Host/Program.cs` (or per Foundation's module-startup pattern — check `FlightsModuleStartup` placeholder).

## Task 13: EF entities + configurations

**Files:**
- Create: `modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/Entities/{IdempotencyKeyEntity, WebhookInboxEntity, DeeplinkOfferCacheEntity, OrderReadModelEntity}.cs`
- Create: `modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/Configurations/{IdempotencyKeyConfig, WebhookInboxConfig, DeeplinkOfferCacheConfig, OrderReadModelConfig}.cs`
- Create: `modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/FlightsDbContext.cs`

- [ ] **Step 1: Entities** — POCO classes mirroring spec §16. Snake-case naming applied via `UseSnakeCaseNamingConvention()` in DbContext.

```csharp
namespace Travel.Modules.Flights.Infrastructure.Persistence.Entities;

public sealed class IdempotencyKeyEntity
{
    public string Key { get; set; } = default!;
    public Guid UserId { get; set; }
    public string Route { get; set; } = default!;
    public string BodyHash { get; set; } = default!;
    public string? ResponseHash { get; set; }
    public int ResponseStatus { get; set; }
    public string? ResponseBody { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class WebhookInboxEntity
{
    public Guid Id { get; set; }
    public string Source { get; set; } = default!;       // "duffel"
    public string EventId { get; set; } = default!;      // Duffel event.id; unique with Source
    public string EventType { get; set; } = default!;
    public string RawPayload { get; set; } = default!;   // jsonb
    public string Signature { get; set; } = default!;
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

public sealed class DeeplinkOfferCacheEntity
{
    public Guid Id { get; set; }
    public string CriteriaHash { get; set; } = default!;
    public string OffersJson { get; set; } = default!;   // jsonb
    public DateTimeOffset FetchedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public sealed class OrderReadModelEntity
{
    public Guid Id { get; set; }
    public Guid AggregateId { get; set; }
    public Guid? UserId { get; set; }
    public string? ProviderOrderId { get; set; }    // Duffel order id; used by webhook handler to map back to aggregate
    public string Status { get; set; } = default!;
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = default!;
    public string ItineraryJson { get; set; } = default!;
    public string PassengerInfoJson { get; set; } = default!;
    public string[] TicketNumbers { get; set; } = Array.Empty<string>();
    public DateTimeOffset BookedAt { get; set; }
    public DateTimeOffset? TicketedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
}
```

- [ ] **Step 2: `IEntityTypeConfiguration<T>` per entity** with keys, unique indexes, jsonb columns. Example:

```csharp
public sealed class WebhookInboxConfig : IEntityTypeConfiguration<WebhookInboxEntity>
{
    public void Configure(EntityTypeBuilder<WebhookInboxEntity> b)
    {
        b.ToTable("webhook_inbox", "flights");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.Source, x.EventId }).IsUnique();
        b.Property(x => x.RawPayload).HasColumnType("jsonb");
    }
}
```

Apply equivalent configs for the other three entities (PK on Key/Id; jsonb where flagged; indexes on `(ExpiresAt)` for idempotency cleanup and `(CriteriaHash, ExpiresAt)` for deeplink cache; index on `AggregateId UNIQUE` and `(UserId, BookedAt DESC)` for order read model).

- [ ] **Step 3: `FlightsDbContext`**

```csharp
using EFCore.NamingConventions;
using Microsoft.EntityFrameworkCore;
using Travel.Modules.Flights.Infrastructure.Persistence.Entities;

namespace Travel.Modules.Flights.Infrastructure.Persistence;

public sealed class FlightsDbContext(DbContextOptions<FlightsDbContext> options) : DbContext(options)
{
    public DbSet<IdempotencyKeyEntity> IdempotencyKeys => Set<IdempotencyKeyEntity>();
    public DbSet<WebhookInboxEntity> WebhookInbox => Set<WebhookInboxEntity>();
    public DbSet<DeeplinkOfferCacheEntity> DeeplinkOffersCache => Set<DeeplinkOfferCacheEntity>();
    public DbSet<OrderReadModelEntity> Orders => Set<OrderReadModelEntity>();

    protected override void OnConfiguring(DbContextOptionsBuilder b) =>
        b.UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasDefaultSchema("flights");
        b.ApplyConfigurationsFromAssembly(typeof(FlightsDbContext).Assembly);
    }
}
```

- [ ] **Step 4: Smoke test** — `FlightsDbContextTests` creates an in-memory `DbContextOptions` and ensures `Database.EnsureCreated()` succeeds (uses `Testcontainers.PostgreSql` from Foundation `Travel.Shared.TestInfrastructure`). One fact per entity insert/read.
- [ ] **Step 5: Commit**

```bash
git add modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/
git commit -m "feat(flights): add FlightsDbContext with 4 EF entities (idempotency, webhook inbox, deeplink cache, order read-model)"
```

---

## Task 14: First EF migration

- [ ] **Step 1: Generate migration**

```bash
dotnet ef migrations add FlightsM1Init \
  --project modules/flights/Travel.Modules.Flights.Infrastructure \
  --startup-project apps/Travel.Host \
  --output-dir Persistence/Migrations \
  --context FlightsDbContext
```

- [ ] **Step 2: Inspect generated `20260513…FlightsM1Init.cs`** and verify: schema `flights`, all four tables present, unique index on `webhook_inbox (source, event_id)`, jsonb columns.
- [ ] **Step 3: Run the AppHost once locally** (`dotnet run --project apps/Travel.AppHost`) to confirm migration applies cleanly against the Aspire-provisioned Postgres. Verify with `\dt flights.*` via Aspire psql exec.
- [ ] **Step 4: Commit**

```bash
git add modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/Migrations/
git commit -m "feat(flights): add initial EF migration for M1 schema"
```

---

## Task 15: Marten config for `BookingAggregate`

**Files:** `modules/flights/Travel.Modules.Flights.Infrastructure/Marten/BookingAggregateConfig.cs`

- [ ] **Step 1: Implement Marten configurator extension**

```csharp
using JasperFx.Events;
using Marten;
using Marten.Events.Projections;
using Travel.Modules.Flights.Core.Aggregates;

namespace Travel.Modules.Flights.Infrastructure.Marten;

public static class BookingAggregateConfig
{
    public static StoreOptions ConfigureFlightsBooking(this StoreOptions opts)
    {
        opts.Events.AddEventType(typeof(Core.DomainEvents.OfferQuoted));
        opts.Events.AddEventType(typeof(Core.DomainEvents.OfferReQuoted));
        opts.Events.AddEventType(typeof(Core.DomainEvents.OfferHeld));
        opts.Events.AddEventType(typeof(Core.DomainEvents.PaymentAuthorized));
        opts.Events.AddEventType(typeof(Core.DomainEvents.OrderConfirmed));
        opts.Events.AddEventType(typeof(Core.DomainEvents.OrderTicketed));
        opts.Events.AddEventType(typeof(Core.DomainEvents.OrderCancelled));
        opts.Events.AddEventType(typeof(Core.DomainEvents.OrderRefunded));

        opts.Projections.LiveStreamAggregation<BookingAggregate>();
        opts.Events.StreamIdentity = StreamIdentity.AsGuid;
        return opts;
    }
}
```

- [ ] **Step 2: Wire into Travel.Host startup** — find the `AddMarten(...)` registration created by Foundation and add `.UseFlightsBookingConfig()` or call `ConfigureFlightsBooking` inside the lambda.
- [ ] **Step 3: Integration test** — `BookingAggregateMartenTests` (in Integration project): start session, append `OfferQuoted` + `OfferHeld` to a new stream, `AggregateStream<BookingAggregate>(streamId)` and assert `Status == Held`.
- [ ] **Step 4: Commit**

```bash
git add modules/flights/Travel.Modules.Flights.Infrastructure/Marten/ apps/Travel.Host/
git commit -m "feat(flights): wire BookingAggregate event stream into Marten"
```

---

# Phase 9 — Idempotency store + cache repositories

## Task 16: `IIdempotencyStore` + EF impl

**Files:**
- Create: `modules/flights/Travel.Modules.Flights.Application/Idempotency/{IIdempotencyStore,IdempotencyKey}.cs`
- Create: `modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/Repositories/IdempotencyStore.cs`

- [ ] **Step 1: Application interface**

```csharp
using ErrorOr;
namespace Travel.Modules.Flights.Application.Idempotency;

public readonly record struct IdempotencyKey(string Value);

public sealed record IdempotencyRecord(
    string ResponseHash, int ResponseStatus, string ResponseBody);

public interface IIdempotencyStore
{
    Task<IdempotencyRecord?> TryGetAsync(IdempotencyKey key, Guid userId, string route, CancellationToken ct);
    Task<ErrorOr<Success>> SaveAsync(IdempotencyKey key, Guid userId, string route,
        string bodyHash, string responseHash, int responseStatus, string responseBody, CancellationToken ct);
    Task<ErrorOr<Success>> CheckOrConflictAsync(IdempotencyKey key, Guid userId, string route,
        string bodyHash, CancellationToken ct);
    Task PurgeExpiredAsync(CancellationToken ct);
}
```

- [ ] **Step 2: EF implementation**

```csharp
public sealed class IdempotencyStore(FlightsDbContext db, TimeProvider time) : IIdempotencyStore
{
    public async Task<IdempotencyRecord?> TryGetAsync(IdempotencyKey key, Guid userId, string route, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var row = await db.IdempotencyKeys
            .Where(x => x.Key == key.Value && x.UserId == userId && x.Route == route && x.ExpiresAt > now)
            .FirstOrDefaultAsync(ct);
        return row?.ResponseHash is null ? null
            : new IdempotencyRecord(row.ResponseHash, row.ResponseStatus, row.ResponseBody!);
    }

    public async Task<ErrorOr<Success>> CheckOrConflictAsync(IdempotencyKey key, Guid userId, string route,
        string bodyHash, CancellationToken ct)
    {
        var existing = await db.IdempotencyKeys
            .Where(x => x.Key == key.Value && x.UserId == userId && x.Route == route)
            .FirstOrDefaultAsync(ct);
        if (existing is null) return Result.Success;
        return existing.BodyHash == bodyHash ? Result.Success : FlightsErrors.IdempotencyConflict;
    }

    public async Task<ErrorOr<Success>> SaveAsync(IdempotencyKey key, Guid userId, string route,
        string bodyHash, string responseHash, int responseStatus, string responseBody, CancellationToken ct)
    {
        db.IdempotencyKeys.Add(new IdempotencyKeyEntity
        {
            Key = key.Value, UserId = userId, Route = route,
            BodyHash = bodyHash, ResponseHash = responseHash,
            ResponseStatus = responseStatus, ResponseBody = responseBody,
            CreatedAt = time.GetUtcNow(),
            ExpiresAt = time.GetUtcNow().AddHours(24)
        });
        await db.SaveChangesAsync(ct);
        return Result.Success;
    }

    public async Task PurgeExpiredAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await db.IdempotencyKeys.Where(x => x.ExpiresAt <= now).ExecuteDeleteAsync(ct);
    }
}
```

- [ ] **Step 3: Integration tests** — `IdempotencyStoreTests`:
    - Save then `TryGet` returns record.
    - `CheckOrConflict` returns `Success` for first call.
    - `CheckOrConflict` with same body returns `Success`.
    - `CheckOrConflict` with different body returns `IdempotencyConflict`.
    - `PurgeExpired` removes only expired records.

Use `FakeTimeProvider` to control TTL.

- [ ] **Step 4: Commit**

```bash
git add modules/flights/Travel.Modules.Flights.Application/Idempotency/ modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/Repositories/IdempotencyStore.cs
git commit -m "feat(flights): add idempotency store with EF backing"
```

---

## Task 17: Deeplink offer cache repository

**Files:** `modules/flights/Travel.Modules.Flights.Infrastructure/Persistence/Repositories/DeeplinkOfferCacheRepository.cs`

- [ ] **Step 1: Interface + impl**

```csharp
namespace Travel.Modules.Flights.Application.Search;

public interface IDeeplinkOfferCache
{
    Task<IReadOnlyList<DeeplinkOffer>?> TryGetAsync(string criteriaHash, CancellationToken ct);
    Task SetAsync(string criteriaHash, IReadOnlyList<DeeplinkOffer> offers, CancellationToken ct);
    Task PurgeExpiredAsync(CancellationToken ct);
}
```

```csharp
// Infrastructure/Persistence/Repositories/DeeplinkOfferCacheRepository.cs
public sealed class DeeplinkOfferCacheRepository(FlightsDbContext db, TimeProvider time) : IDeeplinkOfferCache
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<DeeplinkOffer>?> TryGetAsync(string criteriaHash, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var row = await db.DeeplinkOffersCache
            .Where(x => x.CriteriaHash == criteriaHash && x.ExpiresAt > now)
            .OrderByDescending(x => x.FetchedAt)
            .FirstOrDefaultAsync(ct);
        return row is null ? null : JsonSerializer.Deserialize<List<DeeplinkOffer>>(row.OffersJson, Json);
    }

    public async Task SetAsync(string criteriaHash, IReadOnlyList<DeeplinkOffer> offers, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        db.DeeplinkOffersCache.Add(new DeeplinkOfferCacheEntity
        {
            Id = Guid.NewGuid(),
            CriteriaHash = criteriaHash,
            OffersJson = JsonSerializer.Serialize(offers, Json),
            FetchedAt = now,
            ExpiresAt = now.AddHours(1)
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task PurgeExpiredAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        await db.DeeplinkOffersCache.Where(x => x.ExpiresAt <= now).ExecuteDeleteAsync(ct);
    }
}
```

- [ ] **Step 2: Background job** for hourly purge.

```csharp
// modules/flights/Travel.Modules.Flights.Infrastructure/BackgroundJobs/PurgeExpiredDeeplinkOffersHandler.cs
using Wolverine.Attributes;

public sealed record PurgeExpiredDeeplinkOffers;

public static class PurgeExpiredDeeplinkOffersHandler
{
    [WolverineHandler]
    public static Task Handle(PurgeExpiredDeeplinkOffers _, IDeeplinkOfferCache cache, CancellationToken ct)
        => cache.PurgeExpiredAsync(ct);
}
```

Schedule via Wolverine recurring message every hour (in `FlightsModuleStartup`).

- [ ] **Step 3: Integration tests + commit**

```bash
git commit -m "feat(flights): add deeplink offer cache repository with hourly purge job"
```

---

# Phase 10 — Side-cars

## Task 18: `FrankfurterClient` + Redis-backed rate cache

**Files:**
- Create: `modules/flights/Travel.Modules.Flights.Infrastructure/ExternalServices/FrankfurterClient.cs`
- Create: `modules/flights/Travel.Modules.Flights.Infrastructure/Cache/FrankfurterRatesCache.cs`

- [ ] **Step 1: `IFxRates` interface in Application**

```csharp
namespace Travel.Modules.Flights.Application.Search;

public interface IFxRates
{
    Task<ErrorOr<decimal>> GetRateAsync(CurrencyCode from, CurrencyCode to, CancellationToken ct);
    Task<ErrorOr<Money>> ConvertAsync(Money amount, CurrencyCode to, CancellationToken ct);
}
```

- [ ] **Step 2: `FrankfurterClient`** — wraps `https://api.frankfurter.dev/latest?from={FROM}&to={TO}` (free, no auth). Returns ECB midmarket rate. Polly: timeout 2s, retry 3x.
- [ ] **Step 3: `FrankfurterRatesCache : IFxRates`** — reads from Redis (key `flights:fx:{from}:{to}`, TTL=24h); on miss → `FrankfurterClient`, populates cache.
- [ ] **Step 4: Tests** with `WireMock.Net` stubbing Frankfurter; assert cache hit second time.
- [ ] **Step 5: Commit**

```bash
git commit -m "feat(flights): add Frankfurter FX-rates client with Redis-backed daily cache"
```

---

## Task 19: Redis-backed search cache

**Files:** `modules/flights/Travel.Modules.Flights.Infrastructure/Cache/SearchCacheRedis.cs`

- [ ] **Step 1: Interface in Application**

```csharp
namespace Travel.Modules.Flights.Application.Search;

public interface ISearchCache
{
    Task<IReadOnlyList<Offer>?> TryGetAsync(string key, CancellationToken ct);
    Task SetAsync(string key, IReadOnlyList<Offer> offers, TimeSpan ttl, CancellationToken ct);
}
```

- [ ] **Step 2: `SearchCacheKey` helper in Application**

```csharp
namespace Travel.Modules.Flights.Application.Search;

public static class SearchCacheKey
{
    public static string Build(SearchCriteria c)
    {
        var raw = $"{c.Origin}|{c.Destination}|{c.DepartureDate:O}|{c.ReturnDate?.ToString("O") ?? "-"}" +
                  $"|{c.PassengerCount}|{c.CabinClass.Code}|{c.Currency.Value}";
        return $"flights:search:{Hash(raw)}";
    }

    private static string Hash(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes).ToLowerInvariant()[..16];
    }
}
```

- [ ] **Step 3: Redis impl using `StackExchange.Redis`** (the Aspire-injected `IConnectionMultiplexer`). JSON-serialize `IReadOnlyList<Offer>` with `JsonStringEnumConverter` and a `JsonDerivedType` polymorphism setup so `BookableOffer`/`DeeplinkOffer` round-trip.
- [ ] **Step 4: Integration tests** — set/get round-trip; ttl honored (FakeTimeProvider + Redis SETEX assertion).
- [ ] **Step 5: Commit**

```bash
git commit -m "feat(flights): add Redis-backed search cache with polymorphic offer serialization"
```

---

# Phase 11 — Duffel integration

Per spec §5 and ADR `0013`. Pin `Duffel-Version: v2`. Sandbox-only in M1.

## Task 20: `DuffelOptions` + `DuffelClient` (HttpClient with Polly)

**Files:**
- Create: `modules/flights/Travel.Modules.Flights.Infrastructure/Providers/Duffel/{DuffelOptions, DuffelClient}.cs`
- Modify: `apps/Travel.Host/appsettings.json` — add `Flights:Duffel` section
- Modify: `apps/Travel.Host/appsettings.Development.json` — placeholders + BYO-keys reference

- [ ] **Step 1: `DuffelOptions`**

```csharp
namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel;

public sealed class DuffelOptions
{
    public string BaseUrl { get; set; } = "https://api.duffel.com";
    public string ApiVersion { get; set; } = "v2";
    public string ApiKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 10;
}
```

- [ ] **Step 2: `DuffelClient`** — typed `HttpClient` wrapper with Polly: retry 3x with jitter (50-500ms) on transient HTTP failures; circuit breaker (5 fails / 30s); per-request timeout 4s for search, 10s for orders.

```csharp
public sealed class DuffelClient
{
    private readonly HttpClient _http;
    public DuffelClient(HttpClient http, IOptions<DuffelOptions> opts)
    {
        _http = http;
        _http.BaseAddress = new Uri(opts.Value.BaseUrl);
        _http.DefaultRequestHeaders.Add("Duffel-Version", opts.Value.ApiVersion);
        _http.DefaultRequestHeaders.Authorization = new("Bearer", opts.Value.ApiKey);
        _http.DefaultRequestHeaders.Accept.Add(new("application/json"));
    }

    public async Task<HttpResponseMessage> PostAsync(string path, object body, CancellationToken ct)
        => await _http.PostAsJsonAsync(path, new { data = body }, ct);

    public async Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct)
        => await _http.GetAsync(path, ct);
}
```

DI in `FlightsModuleStartup`:

```csharp
services.AddHttpClient<DuffelClient>()
    .AddStandardResilienceHandler(o =>
    {
        o.Retry.MaxRetryAttempts = 3;
        o.Retry.UseJitter = true;
        o.CircuitBreaker.FailureRatio = 0.5;
        o.CircuitBreaker.MinimumThroughput = 5;
        o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
    });
```

- [ ] **Step 3: Smoke unit test** stubbed with WireMock: `DuffelClient.GetAsync("/air/offer_requests/x")` returns 200 with stub body; verify headers include `Authorization: Bearer ...` and `Duffel-Version: v2`.
- [ ] **Step 4: Commit**

```bash
git commit -m "feat(flights): add DuffelClient with Polly resilience and pinned Duffel-Version: v2"
```

---

## Task 21: Duffel DTOs and mapper

**Files:**
- Create: `modules/flights/Travel.Modules.Flights.Infrastructure/Providers/Duffel/Dto/{DuffelOfferDto, DuffelOrderDto, DuffelSliceDto, DuffelSegmentDto, DuffelWebhookEventDto}.cs`
- Create: `modules/flights/Travel.Modules.Flights.Infrastructure/Providers/Duffel/DuffelOfferMapper.cs`

- [ ] **Step 1: DTOs** — `record DuffelOfferDto(string Id, decimal TotalAmount, string TotalCurrency, DateTimeOffset ExpiresAt, DuffelSliceDto[] Slices, …)`. Match Duffel API shape (snake_case via `JsonPropertyName` or `JsonNamingPolicy.SnakeCaseLower`). Place under `Dto/` — never leave Infrastructure.
- [ ] **Step 2: `DuffelOfferMapper`** — pure mapping; takes `DuffelOfferDto` → `ErrorOr<BookableOffer>`. Domain construction goes through `Create` factories (so mapper surfaces validation errors). Tests with sample JSON fixtures saved under `tests/flights/Travel.Modules.Flights.Tests.Unit/Providers/Duffel/Fixtures/*.json` (use real Duffel sandbox response shapes — fetch one via curl during setup).

```csharp
public static class DuffelOfferMapper
{
    public static ErrorOr<BookableOffer> Map(DuffelOfferDto dto, TimeProvider time)
    {
        // (a) Money
        var currency = CurrencyCode.Create(dto.TotalCurrency);
        if (currency.IsError) return currency.FirstError;
        var amount = Money.Create(dto.TotalAmount, currency.Value);
        if (amount.IsError) return amount.FirstError;

        // (b) Slices → Itinerary
        var slices = new List<Slice>(dto.Slices.Length);
        foreach (var s in dto.Slices)
        {
            var seg = MapSegments(s);
            if (seg.IsError) return seg.FirstError;
            var slice = Slice.Create(seg.Value);
            if (slice.IsError) return slice.FirstError;
            slices.Add(slice.Value);
        }
        var itinerary = Itinerary.Create(slices);
        if (itinerary.IsError) return itinerary.FirstError;

        // (c) BookableOffer
        return new BookableOffer(
            Id: OfferId.New(),
            Itinerary: itinerary.Value,
            TotalAmount: amount.Value,
            Provider: ProviderId.Duffel,
            FetchedAt: time.GetUtcNow(),
            ExpiresAt: dto.ExpiresAt,
            FareConditions: new FareConditions(
                ChangeAllowed: dto.Conditions?.ChangeBeforeDeparture?.Allowed ?? false,
                RefundAllowed: dto.Conditions?.RefundBeforeDeparture?.Allowed ?? false,
                FareBasisCode: dto.Slices.FirstOrDefault()?.FareBrand,
                CabinClassMarketing: dto.Slices.FirstOrDefault()?.Segments.FirstOrDefault()?.CabinClassMarketingName),
            ProviderOfferRef: dto.Id);
    }

    private static ErrorOr<IReadOnlyList<Segment>> MapSegments(DuffelSliceDto s)
    {
        var list = new List<Segment>(s.Segments.Length);
        foreach (var seg in s.Segments)
        {
            var origin = IataCode.Create(seg.OriginIataCode);
            if (origin.IsError) return origin.FirstError;
            var dest = IataCode.Create(seg.DestinationIataCode);
            if (dest.IsError) return dest.FirstError;
            var cabin = CabinClass.Parse(seg.CabinClass);
            if (cabin.IsError) return cabin.FirstError;
            var built = Segment.Create(origin.Value, dest.Value,
                seg.DepartingAt, seg.ArrivingAt,
                seg.MarketingCarrierIataCode, seg.MarketingCarrierFlightNumber,
                cabin.Value);
            if (built.IsError) return built.FirstError;
            list.Add(built.Value);
        }
        return list;
    }
}
```

- [ ] **Step 3: Unit tests** loading a saved Duffel sandbox response, mapping it, asserting the resulting `BookableOffer` has expected slice count, total, currency.
- [ ] **Step 4: Commit**

```bash
git commit -m "feat(flights): add Duffel DTOs and DuffelOfferMapper with full validation"
```

---

## Task 22: `DuffelFlightSearchProvider`

**Files:** `modules/flights/Travel.Modules.Flights.Infrastructure/Providers/Duffel/DuffelFlightSearchProvider.cs`

- [ ] **Step 1: Implementation** — POST `/air/offer_requests` with `{ cabin_class, slices, passengers }`, parse `offers[]`, map each via `DuffelOfferMapper`.

```csharp
public sealed class DuffelFlightSearchProvider(DuffelClient client, TimeProvider time, ILogger<DuffelFlightSearchProvider> log)
    : IFlightSearchProvider
{
    public ProviderId Id => ProviderId.Duffel;

    public async Task<ErrorOr<IReadOnlyList<Offer>>> SearchAsync(SearchCriteria c, CancellationToken ct)
    {
        var body = BuildRequest(c);
        try
        {
            var resp = await client.PostAsync("/air/offer_requests?return_offers=true", body, ct);
            if (!resp.IsSuccessStatusCode)
            {
                log.LogWarning("Duffel search failed: {Status}", resp.StatusCode);
                return resp.StatusCode == HttpStatusCode.TooManyRequests
                    ? FlightsErrors.ProviderRateLimited("Duffel")
                    : FlightsErrors.ProviderUnavailable("Duffel");
            }
            var dto = await resp.Content.ReadFromJsonAsync<DuffelOfferRequestResponseDto>(ct)
                ?? throw new InvalidOperationException("Empty Duffel response");

            var results = new List<Offer>(dto.Data.Offers.Length);
            foreach (var o in dto.Data.Offers)
            {
                var mapped = DuffelOfferMapper.Map(o, time);
                if (mapped.IsError) { log.LogWarning("Skipping offer {Id}: {Error}", o.Id, mapped.FirstError); continue; }
                results.Add(mapped.Value);
            }
            return results;
        }
        catch (TaskCanceledException) { return FlightsErrors.ProviderUnavailable("Duffel"); }
    }

    private static object BuildRequest(SearchCriteria c) => new
    {
        cabin_class = c.CabinClass.Code,
        passengers = new[] { new { type = "adult" } },
        slices = c.IsRoundTrip
            ? new[] {
                new { origin = c.Origin.Value, destination = c.Destination.Value, departure_date = c.DepartureDate.ToString("yyyy-MM-dd") },
                new { origin = c.Destination.Value, destination = c.Origin.Value, departure_date = c.ReturnDate!.Value.ToString("yyyy-MM-dd") }
              }
            : new[] {
                new { origin = c.Origin.Value, destination = c.Destination.Value, departure_date = c.DepartureDate.ToString("yyyy-MM-dd") }
              }
    };
}
```

- [ ] **Step 2: Integration test** with WireMock stub returning a known Duffel sandbox response; assert returns N offers with correct shape.
- [ ] **Step 3: Commit**

```bash
git commit -m "feat(flights): add DuffelFlightSearchProvider"
```

---

## Task 23: `DuffelFlightBookingProvider`

**Files:** `modules/flights/Travel.Modules.Flights.Infrastructure/Providers/Duffel/DuffelFlightBookingProvider.cs`

- [ ] **Step 1: Implement five methods**:
    - `RefreshOfferAsync(string ref)` — `GET /air/offers/{ref}` → map → `BookableOffer`. If 404 → `FlightsErrors.OfferNotFound`. If `expires_at <= now` → `FlightsErrors.OfferExpired`.
    - `HoldOfferAsync(BookableOffer, PassengerInfo)` — `POST /air/orders` with `type: "pay_later"` (Duffel hold) + passenger + selected_offers. Returns `HeldOrder(provider_id, hold_expires_at)`. If carrier doesn't support hold → fall back to direct confirm flow (see Step 2).
    - `ConfirmOrderAsync(string orderId, PaymentRef payment)` — `POST /air/orders/{id}/payments` with `{ type: "balance", amount, currency }` (Duffel test wallet). Returns `ConfirmedOrder(orderId, time.GetUtcNow())`.
    - `CancelOrderAsync(string orderId)` — `POST /air/order_cancellations` → `Success` or `OrderNotCancellable`.
    - `GetOrderStatusAsync(string orderId)` — `GET /air/orders/{id}` → map status + ticket numbers from `documents[]`.
- [ ] **Step 2: Hold-fallback note** — if `POST /air/orders` returns `available_actions` without `hold`, the impl creates the order with `type: "instant"` and skips the `Held` state in the saga (handler decides to chain `OfferQuoted → OrderConfirmed`). This is logged as a metric `flights.hold.unavailable_total`.
- [ ] **Step 3: Integration tests** with WireMock stubs for each of the five paths + the hold-unavailable fallback.
- [ ] **Step 4: Commit**

```bash
git commit -m "feat(flights): add DuffelFlightBookingProvider (refresh, hold, confirm, cancel, status)"
```

---

## Task 24: `DuffelWebhookVerifier`

**Files:** `modules/flights/Travel.Modules.Flights.Infrastructure/Providers/Duffel/DuffelWebhookVerifier.cs`

- [ ] **Step 1: Implementation** — HMAC-SHA256 verify of `Duffel-Signature` header over raw body using `DuffelOptions.WebhookSecret`. Constant-time comparison via `CryptographicOperations.FixedTimeEquals`.

```csharp
public sealed class DuffelWebhookVerifier(IOptions<DuffelOptions> opts)
{
    public bool Verify(byte[] body, string signatureHeader)
    {
        var key = Encoding.UTF8.GetBytes(opts.Value.WebhookSecret);
        var computed = HMACSHA256.HashData(key, body);
        var expected = Convert.FromHexString(signatureHeader.Replace("sha256=", "", StringComparison.OrdinalIgnoreCase));
        return CryptographicOperations.FixedTimeEquals(computed, expected);
    }
}
```

- [ ] **Step 2: Unit tests** — valid signature, tampered body, wrong header format, empty secret.
- [ ] **Step 3: Commit**

```bash
git commit -m "feat(flights): add Duffel webhook HMAC verifier"
```

---

## Task 25: `DuffelTestWalletPaymentGateway`

**Files:** `modules/flights/Travel.Modules.Flights.Infrastructure/Payments/DuffelTestWalletPaymentGateway.cs`

- [ ] **Step 1: Implementation** — Duffel test wallet uses `balance` payment type; sandbox always succeeds. Mark `[TestOnly]`.

```csharp
using Travel.Shared.Abstractions;

[TestOnly]
public sealed class DuffelTestWalletPaymentGateway(TimeProvider time) : IPaymentGateway
{
    public Task<ErrorOr<PaymentRef>> AuthorizeAsync(Money amount, string idempotencyKey, CancellationToken ct)
        => Task.FromResult<ErrorOr<PaymentRef>>(new PaymentRef(Guid.NewGuid()));

    public Task<ErrorOr<Success>> CaptureAsync(PaymentRef payment, CancellationToken ct)
        => Task.FromResult<ErrorOr<Success>>(Result.Success);

    public Task<ErrorOr<RefundRef>> RefundAsync(PaymentRef payment, Money amount, CancellationToken ct)
        => Task.FromResult<ErrorOr<RefundRef>>(new RefundRef(Guid.NewGuid()));
}
```

> Note: this in-memory impl skips actually calling Duffel's payment endpoint — the real Duffel confirm-order call in `DuffelFlightBookingProvider.ConfirmOrderAsync` is what triggers the sandbox `balance` payment. `IPaymentGateway` provides the abstraction surface for the saga (Authorize → store ref → Capture → use ref in confirm-order). When a real gateway (Stripe) is added, this gateway becomes a thin wrapper that translates to actual payment calls.

- [ ] **Step 2: Unit tests** — happy path for each method; ArchUnit test from Task 1 covers `[TestOnly]` enforcement (will be extended in Phase 20).
- [ ] **Step 3: Commit**

```bash
git commit -m "feat(flights): add DuffelTestWalletPaymentGateway (sandbox-only IPaymentGateway impl)"
```

---

## Task 26: ADR 0018 — Duffel webhook inbox/outbox

Reference spec §11 and ADR template. Commit:

```bash
git add docs/adr/0018-duffel-webhook-inbox-outbox.md
git commit -m "docs(adr): add ADR 0018 for Duffel webhook inbox/outbox pattern"
```

---

# Phase 12 — Travelpayouts integration

Per spec §5.4. Aviasales Data API v3 `prices_for_dates`. Cached prices, no booking, deeplink with partner marker.

## Task 27: `TravelpayoutsOptions` + `TravelpayoutsClient`

**Files:**
- Create: `modules/flights/Travel.Modules.Flights.Infrastructure/Providers/Travelpayouts/{TravelpayoutsOptions, TravelpayoutsClient}.cs`

- [ ] **Step 1: Options**

```csharp
public sealed class TravelpayoutsOptions
{
    public string BaseUrl { get; set; } = "https://api.travelpayouts.com";
    public string ApiVersion { get; set; } = "v3";
    public string ApiToken { get; set; } = string.Empty;
    public string PartnerMarker { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 4;
}
```

- [ ] **Step 2: Client** — typed HttpClient. Uses query-string token (`?token={ApiToken}`). Polly identical to Duffel client.
- [ ] **Step 3: Smoke test + commit**

```bash
git commit -m "feat(flights): add TravelpayoutsClient with Polly resilience"
```

---

## Task 28: `TravelpayoutsSearchProvider` + mapper + deeplink builder

**Files:**
- Create: `Providers/Travelpayouts/{TravelpayoutsSearchProvider, TravelpayoutsOfferMapper, TravelpayoutsDeeplinkBuilder}.cs`
- Create: `Providers/Travelpayouts/Dto/PricesForDatesResponseDto.cs`

- [ ] **Step 1: DTO** — matches `https://api.travelpayouts.com/aviasales/v3/prices_for_dates?origin=LED&destination=DME&...` response shape.
- [ ] **Step 2: `TravelpayoutsDeeplinkBuilder`** — produces partner URL. Format per Travelpayouts docs: `https://www.aviasales.ru/search/{origin}{date}{destination}{return_date}1{?marker}`. Embed `PartnerMarker` and a referral subid (e.g., `&utm_source=travel-platform`).
- [ ] **Step 3: `TravelpayoutsOfferMapper`** — maps each price entry to `DeeplinkOffer`. Build a minimal `Itinerary` (one slice with one synthetic `Segment` — Travelpayouts doesn't give segment-level detail; use airline IATA + flight number from the response, fall back to "?" if unset).
- [ ] **Step 4: `TravelpayoutsSearchProvider`**

```csharp
public sealed class TravelpayoutsSearchProvider(TravelpayoutsClient client, IOptions<TravelpayoutsOptions> opts,
    TravelpayoutsDeeplinkBuilder deeplink, IDeeplinkOfferCache cache, TimeProvider time,
    ILogger<TravelpayoutsSearchProvider> log) : IFlightSearchProvider
{
    public ProviderId Id => ProviderId.Travelpayouts;

    public async Task<ErrorOr<IReadOnlyList<Offer>>> SearchAsync(SearchCriteria c, CancellationToken ct)
    {
        var hash = SearchCacheKey.Build(c);
        var cached = await cache.TryGetAsync(hash, ct);
        if (cached is not null) return cached.Cast<Offer>().ToList();

        try
        {
            var resp = await client.GetAsync(
                $"/aviasales/v3/prices_for_dates" +
                $"?origin={c.Origin}&destination={c.Destination}" +
                $"&departure_at={c.DepartureDate:yyyy-MM-dd}" +
                (c.ReturnDate.HasValue ? $"&return_at={c.ReturnDate:yyyy-MM-dd}" : "") +
                $"&currency={c.Currency.Value.ToLowerInvariant()}&token={opts.Value.ApiToken}&limit=30",
                ct);
            if (!resp.IsSuccessStatusCode)
                return FlightsErrors.ProviderUnavailable("Travelpayouts");

            var dto = await resp.Content.ReadFromJsonAsync<PricesForDatesResponseDto>(ct)
                ?? throw new InvalidOperationException("Empty Travelpayouts response");

            var offers = new List<DeeplinkOffer>();
            foreach (var d in dto.Data)
            {
                var mapped = TravelpayoutsOfferMapper.Map(d, c, deeplink, time);
                if (mapped.IsError) { log.LogWarning("Skipping TP offer: {Error}", mapped.FirstError); continue; }
                offers.Add(mapped.Value);
            }

            await cache.SetAsync(hash, offers, ct);
            return offers.Cast<Offer>().ToList();
        }
        catch (TaskCanceledException) { return FlightsErrors.ProviderUnavailable("Travelpayouts"); }
    }
}
```

- [ ] **Step 5: Integration tests** with WireMock stub. Assert deeplink URL contains partner marker.
- [ ] **Step 6: Commit**

```bash
git commit -m "feat(flights): add TravelpayoutsSearchProvider with deeplink builder and EF cache"
```

---

# Phase 13 — Search pipeline (Application)

## Task 29: `OfferDeduplicator` + `OfferRanker`

**Files:** `modules/flights/Travel.Modules.Flights.Application/Handlers/Search/{OfferDeduplicator, OfferRanker}.cs`

- [ ] **Step 1: `OfferDeduplicator`** — key per spec §6.3: `(primary_carrier_code, primary_flight_number, departure_date_utc)`. Where `primary` = first segment of first slice (one-way) or both slices combined (round-trip composite key). When duplicates collide, keep the minimum-price offer; on tie, prefer `BookableOffer` over `DeeplinkOffer`.

```csharp
public static class OfferDeduplicator
{
    public static IReadOnlyList<Offer> Dedup(IEnumerable<Offer> offers) =>
        offers.GroupBy(KeyOf)
              .Select(g => g.OrderBy(o => o.TotalAmount.Amount)
                            .ThenBy(o => o is DeeplinkOffer ? 1 : 0)
                            .First())
              .ToList();

    private static string KeyOf(Offer o)
    {
        var primary = o.Itinerary.Slices[0].Segments[0];
        var dateKey = primary.DepartAt.UtcDateTime.Date.ToString("yyyy-MM-dd");
        return $"{primary.CarrierCode}|{primary.FlightNumber}|{dateKey}";
    }
}
```

- [ ] **Step 2: `OfferRanker`** — spec §3 #10: price asc, tie-break duration asc.

```csharp
public static class OfferRanker
{
    public static IReadOnlyList<Offer> Rank(IEnumerable<Offer> offers, int top = 200) =>
        offers.OrderBy(o => o.TotalAmount.Amount)
              .ThenBy(o => o.Itinerary.TotalDuration.Value)
              .Take(top)
              .ToList();
}
```

- [ ] **Step 3: Unit tests** — duplicates collapsed (cheapest wins, bookable-over-deeplink on tie), top-N applied.
- [ ] **Step 4: Commit**

```bash
git commit -m "feat(flights): add OfferDeduplicator and OfferRanker"
```

---

## Task 30: `SearchFlightsHandler` (fan-out + dedup + rank + cache)

**Files:**
- Create: `Application/Queries/SearchFlightsQuery.cs`
- Create: `Application/Handlers/Search/SearchFlightsHandler.cs`

- [ ] **Step 1: Query record**

```csharp
public sealed record SearchFlightsQuery(SearchCriteria Criteria);

public sealed record SearchResult(
    IReadOnlyList<Offer> Offers,
    IReadOnlyList<ProviderFailure> PartialFailures);

public sealed record ProviderFailure(string Provider, string ErrorCode, long ElapsedMs);
```

- [ ] **Step 2: Handler**

```csharp
using Wolverine.Attributes;

public static class SearchFlightsHandler
{
    [WolverineHandler]
    public static async Task<ErrorOr<SearchResult>> Handle(
        SearchFlightsQuery query,
        IEnumerable<IFlightSearchProvider> providers,
        ISearchCache cache,
        FlightsMetrics metrics,
        TimeProvider time,
        ILogger<SearchFlightsQuery> log,
        CancellationToken ct)
    {
        var key = SearchCacheKey.Build(query.Criteria);

        var cached = await cache.TryGetAsync(key, ct);
        if (cached is not null) return new SearchResult(cached, Array.Empty<ProviderFailure>());

        var tasks = providers.Select(p => RunWithTimeout(p, query.Criteria, time, metrics, log, ct)).ToList();
        var results = await Task.WhenAll(tasks);

        var allOffers = results.Where(r => r.Offers is not null).SelectMany(r => r.Offers!).ToList();
        var failures = results.Where(r => r.Failure is not null).Select(r => r.Failure!).ToList();

        if (allOffers.Count == 0)
            return failures.Count == providers.Count()
                ? FlightsErrors.ProviderUnavailable("all")
                : (ErrorOr<SearchResult>)new SearchResult(Array.Empty<Offer>(), failures);

        var deduped = OfferDeduplicator.Dedup(allOffers);
        var ranked = OfferRanker.Rank(deduped);

        await cache.SetAsync(key, ranked, TimeSpan.FromMinutes(5), ct);
        return new SearchResult(ranked, failures);
    }

    private static async Task<(IReadOnlyList<Offer>? Offers, ProviderFailure? Failure)> RunWithTimeout(
        IFlightSearchProvider provider, SearchCriteria c, TimeProvider time, FlightsMetrics metrics,
        ILogger log, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(4));

        try
        {
            var result = await provider.SearchAsync(c, cts.Token);
            var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
            metrics.SearchLatency.Record(elapsedMs, new("provider", provider.Id.Value),
                new("status", result.IsError ? "error" : "ok"));
            if (result.IsError)
                return (null, new ProviderFailure(provider.Id.Value, result.FirstError.Code, (long)elapsedMs));
            return (result.Value, null);
        }
        catch (OperationCanceledException)
        {
            var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
            metrics.SearchLatency.Record(elapsedMs, new("provider", provider.Id.Value), new("status", "timeout"));
            return (null, new ProviderFailure(provider.Id.Value, "Timeout", (long)elapsedMs));
        }
    }
}
```

- [ ] **Step 3: Integration test (Testcontainers)** — happy-path search hits both providers (WireMock-stubbed), returns merged + sorted list with no partial failures. Cache populated; second call returns from cache.
- [ ] **Step 4: Integration test** — Travelpayouts stub returns 500; SearchResult has Duffel offers + 1 partial failure entry.
- [ ] **Step 5: Integration test** — both providers timeout; returns `FlightsErrors.ProviderUnavailable("all")`.
- [ ] **Step 6: Commit**

```bash
git commit -m "feat(flights): add SearchFlightsHandler with parallel fan-out, dedup, rank, cache"
```

---

# Phase 14 — Booking saga handlers

Per spec §7. The saga is the Marten event stream; each command handler loads the aggregate, validates via `Guard*`, calls provider, appends domain events.

## Task 31: `QuoteOfferCommand` + handler

**Files:**
- Create: `Application/Commands/QuoteOfferCommand.cs`
- Create: `Application/Handlers/Booking/QuoteOfferHandler.cs`

- [ ] **Step 1: Command + result**

```csharp
public sealed record QuoteOfferCommand(string ProviderOfferRef, ProviderId Provider);

public sealed record QuotedOfferResult(Guid AggregateId, BookableOffer Offer);
```

- [ ] **Step 2: Handler**

```csharp
public static class QuoteOfferHandler
{
    [WolverineHandler]
    public static async Task<ErrorOr<QuotedOfferResult>> Handle(
        QuoteOfferCommand cmd,
        IEnumerable<IFlightBookingProvider> bookingProviders,
        IDocumentSession marten,
        TimeProvider time,
        CancellationToken ct)
    {
        var provider = bookingProviders.FirstOrDefault(p => p.Id == cmd.Provider);
        if (provider is null) return FlightsErrors.ProviderUnavailable(cmd.Provider.Value);

        var refreshed = await provider.RefreshOfferAsync(cmd.ProviderOfferRef, ct);
        if (refreshed.IsError) return refreshed.FirstError;

        var aggregateId = Guid.NewGuid();
        marten.Events.StartStream<BookingAggregate>(aggregateId,
            new OfferQuoted(
                OfferId: refreshed.Value.Id,
                Itinerary: refreshed.Value.Itinerary,
                TotalAmount: refreshed.Value.TotalAmount,
                ExpiresAt: refreshed.Value.ExpiresAt,
                ProviderRef: refreshed.Value.ProviderOfferRef,
                QuotedAt: time.GetUtcNow()));
        await marten.SaveChangesAsync(ct);

        return new QuotedOfferResult(aggregateId, refreshed.Value);
    }
}
```

- [ ] **Step 3: Integration test** — WireMock-stubbed Duffel `/air/offers/{id}`; assert stream created with `OfferQuoted`.
- [ ] **Step 4: Commit**

```bash
git commit -m "feat(flights): add QuoteOfferHandler (re-fetch offer, start aggregate stream)"
```

---

## Task 32: `HoldOfferCommand` + handler

```csharp
public sealed record HoldOfferCommand(Guid AggregateId, PassengerInfo Passenger);
```

- [ ] **Step 1: Handler logic**:
    1. Load aggregate via `marten.Events.AggregateStream<BookingAggregate>(id)`.
    2. `agg.GuardCanHold()`.
    3. `agg.GuardOfferNotExpired(time)`.
    4. Resolve provider; call `HoldOfferAsync(offer, passenger)`.
    5. On success append `OfferHeld` to the stream.
    6. On Duffel "hold unavailable" return — emit metric, return `Errors.Flights.OrderNotCancellable` with hint (saga handler will route to immediate confirm in M2; for M1 this is a deferred path — log + 422).

- [ ] **Step 2: Integration test**: full saga step 2 against WireMock; success appends `OfferHeld`.
- [ ] **Step 3: Compensation test**: offer expired → handler returns `OfferExpired`, no event appended; stream remains in `OfferQuoted`.
- [ ] **Step 4: Commit**

```bash
git commit -m "feat(flights): add HoldOfferHandler"
```

---

## Task 33: `ConfirmOrderCommand` + handler

```csharp
public sealed record ConfirmOrderCommand(Guid AggregateId);
```

- [ ] **Step 1: Handler logic** (full compensation per spec §7.2):
    1. Load aggregate; `GuardCanConfirm`.
    2. `IPaymentGateway.AuthorizeAsync(agg.TotalAmount, idempotencyKey)`.
    3. On success append `PaymentAuthorized`.
    4. `IPaymentGateway.CaptureAsync(paymentRef)`.
       - On failure: append `OrderCancelled(Reason=System)`; refund/void via `RefundAsync`; return `PaymentFailed`.
    5. `provider.ConfirmOrderAsync(agg.ProviderOrderId!, paymentRef)`.
       - On failure: append `OrderCancelled(Reason=System)`; emit OTel alert log.
    6. On success append `OrderConfirmed`.
    7. Publish `OrderConfirmed` as an outbox-side notification message (separate from the domain event in the stream) so email handler + SSE handler subscribe — Wolverine cascading.
    8. Project to `OrderReadModelEntity` (call `OrderReadModelProjector.Project(agg, db, time)`).

- [ ] **Step 2: Projector helper**

```csharp
public static class OrderReadModelProjector
{
    public static async Task Project(BookingAggregate agg, FlightsDbContext db, Guid userId, TimeProvider time, CancellationToken ct)
    {
        var entity = await db.Orders.FirstOrDefaultAsync(o => o.AggregateId == agg.Id, ct);
        if (entity is null)
        {
            entity = new OrderReadModelEntity { Id = Guid.NewGuid(), AggregateId = agg.Id, UserId = userId };
            db.Orders.Add(entity);
        }
        entity.Status = agg.Status.ToString();
        entity.ProviderOrderId = agg.ProviderOrderId;
        entity.TotalAmount = agg.TotalAmount!.Amount;
        entity.Currency = agg.TotalAmount!.Currency.Value;
        entity.ItineraryJson = JsonSerializer.Serialize(agg.Itinerary, JsonSerializerOptions.Default);
        entity.PassengerInfoJson = JsonSerializer.Serialize(agg.Passenger, JsonSerializerOptions.Default);
        entity.TicketNumbers = agg.TicketNumbers.ToArray();
        entity.BookedAt = entity.BookedAt == default ? time.GetUtcNow() : entity.BookedAt;
        if (agg.Status == BookingStatus.Ticketed) entity.TicketedAt = time.GetUtcNow();
        if (agg.Status == BookingStatus.Cancelled) entity.CancelledAt = time.GetUtcNow();
        if (agg.Status == BookingStatus.Refunded) entity.RefundedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 3: Integration tests** — happy path through `Confirmed`; capture fails → `Cancelled` + refund attempted; confirm-order fails → `Cancelled` with log assertion.
- [ ] **Step 4: Commit**

```bash
git commit -m "feat(flights): add ConfirmOrderHandler with payment + provider compensation paths"
```

---

## Task 34: `CancelOrderCommand` + handler

```csharp
public sealed record CancelOrderCommand(Guid AggregateId);
```

- [ ] **Step 1: Handler logic**:
    1. Load aggregate; `GuardCanCancel`.
    2. If `Status >= Held` call `provider.CancelOrderAsync(agg.ProviderOrderId!)`.
    3. Append `OrderCancelled(Reason=User)`.
    4. Update OrderReadModel.
- [ ] **Step 2: Integration test** — cancel from `Confirmed`, cancel from `Cancelled` is idempotent (returns current state).
- [ ] **Step 3: Commit**

```bash
git commit -m "feat(flights): add CancelOrderHandler"
```

---

## Task 35: `GetOrderQuery` + `ListOrdersQuery` handlers

**Files:** `Application/Handlers/Booking/{GetOrderHandler, ListOrdersHandler}.cs`

- [ ] **Step 1: `GetOrderQuery(Guid AggregateId, Guid UserId)` → `OrderReadModelEntity`** — query EF; 404 → `Errors.Flights.OfferNotFound` (reused).
- [ ] **Step 2: `ListOrdersQuery(Guid UserId, int Limit, int Offset)` → paged list**, sorted by `BookedAt DESC`.
- [ ] **Step 3: Integration tests** — confirm read-model returns shape; UserId scoping enforced.
- [ ] **Step 4: Commit**

```bash
git commit -m "feat(flights): add GetOrder and ListOrders queries with EF projections"
```

---

## Task 36: ADRs 0016 + 0017

- [ ] **Step 1: Write** `docs/adr/0016-booking-saga-via-marten-es.md` and `docs/adr/0017-flights-idempotency-strategy.md` per spec §3 #4 and §7.3.
- [ ] **Step 2: Commit**

```bash
git commit -m "docs(adr): add ADRs 0016 (booking saga via Marten ES) and 0017 (idempotency strategy)"
```

---

# Phase 15 — Idempotency middleware

## Task 37: `IdempotencyKeyMiddleware`

**Files:** `modules/flights/Travel.Modules.Flights.Api/Middleware/IdempotencyKeyMiddleware.cs`

- [ ] **Step 1: Middleware** — for `POST` on `/api/flights/orders/(hold|confirm|cancel)`, require `Idempotency-Key` header (UUIDv4 expected). Compute body hash. Call `IIdempotencyStore.TryGetAsync` — if present, return cached response with `Idempotency-Replay: true` header. Else call `CheckOrConflictAsync(bodyHash)` — on conflict, return 409 `FlightsErrors.IdempotencyConflict`. After handler runs, capture response body + status, call `SaveAsync`.

```csharp
public sealed class IdempotencyKeyMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx, IIdempotencyStore store, TimeProvider time)
    {
        if (!IsTargetedRoute(ctx.Request)) { await next(ctx); return; }

        if (!ctx.Request.Headers.TryGetValue("Idempotency-Key", out var keyHeader) ||
            !Guid.TryParse(keyHeader, out var keyGuid))
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsJsonAsync(new { code = "Flights.IdempotencyKey.Missing" });
            return;
        }

        var userId = ctx.User.GetUserId(); // extension that reads "sub" claim
        var key = new IdempotencyKey(keyGuid.ToString("N"));
        ctx.Request.EnableBuffering();
        var bodyHash = await HashBody(ctx.Request);

        var cached = await store.TryGetAsync(key, userId, ctx.Request.Path, ctx.RequestAborted);
        if (cached is not null)
        {
            ctx.Response.StatusCode = cached.ResponseStatus;
            ctx.Response.Headers["Idempotency-Replay"] = "true";
            await ctx.Response.WriteAsync(cached.ResponseBody);
            return;
        }

        var check = await store.CheckOrConflictAsync(key, userId, ctx.Request.Path, bodyHash, ctx.RequestAborted);
        if (check.IsError) { ctx.Response.StatusCode = StatusCodes.Status409Conflict;
            await ctx.Response.WriteAsJsonAsync(check.FirstError); return; }

        // Capture response
        var originalBody = ctx.Response.Body;
        using var ms = new MemoryStream();
        ctx.Response.Body = ms;
        await next(ctx);
        ms.Position = 0;
        var responseBody = await new StreamReader(ms).ReadToEndAsync();
        ms.Position = 0;
        await ms.CopyToAsync(originalBody);
        ctx.Response.Body = originalBody;

        var respHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(responseBody)));
        await store.SaveAsync(key, userId, ctx.Request.Path, bodyHash, respHash,
            ctx.Response.StatusCode, responseBody, ctx.RequestAborted);
    }

    private static bool IsTargetedRoute(HttpRequest r) =>
        r.Method == "POST" && r.Path.StartsWithSegments("/api/flights/orders")
        && (r.Path.Value!.EndsWith("/hold") || r.Path.Value.EndsWith("/confirm") || r.Path.Value.Contains("/cancel"));

    private static async Task<string> HashBody(HttpRequest r)
    {
        r.Body.Position = 0;
        using var ms = new MemoryStream();
        await r.Body.CopyToAsync(ms);
        r.Body.Position = 0;
        return Convert.ToHexString(SHA256.HashData(ms.ToArray()));
    }
}
```

- [ ] **Step 2: Integration test (Alba)** — POST `/hold` twice with same key + body returns cached on 2nd; same key + different body returns 409.
- [ ] **Step 3: Wire into pipeline** in `apps/Travel.Host/Program.cs`: `app.UseMiddleware<IdempotencyKeyMiddleware>();` after `UseAuthentication`.
- [ ] **Step 4: Commit**

```bash
git commit -m "feat(flights): add idempotency middleware for booking POST endpoints"
```

---

# Phase 16 — Webhooks

## Task 38: `ProcessDuffelWebhookCommand` + handler

**Files:**
- Create: `Application/Commands/ProcessDuffelWebhookCommand.cs`
- Create: `Application/Handlers/Webhooks/DuffelWebhookHandler.cs`

- [ ] **Step 1: Command**

```csharp
public sealed record ProcessDuffelWebhookCommand(Guid InboxId);
```

- [ ] **Step 2: Handler** — load inbox row by id; parse event_type; based on type:
    - `order.created` with `documents` issued → load aggregate by `provider_order_id` lookup in EF read model → append `OrderTicketed(TicketNumbers, time.GetUtcNow())`.
    - `order.airline_initiated_change.cancelled` → load aggregate, append `OrderRefunded(refundRef, amount, RefundInitiator.Airline, now)`.
    - Other types → mark processed (no domain action in M1).
    - After processing, set `inbox.ProcessedAt = now`.
- [ ] **Step 3: Map provider_order_id to aggregateId** — `OrderReadModelEntity.ProviderOrderId` (already on the entity from Task 13) is populated by `OrderReadModelProjector` (Task 33) on each `OfferHeld`/`OrderConfirmed`. Webhook lookup: `db.Orders.Where(o => o.ProviderOrderId == id).Select(o => o.AggregateId)`.
- [ ] **Step 4: Integration tests**: feed an inbox row with each event type, assert correct domain event appended; idempotency — same inbox row processed twice produces only one append.
- [ ] **Step 5: Commit**

```bash
git commit -m "feat(flights): add DuffelWebhookHandler with inbox processing"
```

---

## Task 39: `DuffelWebhookEndpoint`

**Files:** `modules/flights/Travel.Modules.Flights.Api/Endpoints/DuffelWebhookEndpoint.cs`

- [ ] **Step 1: Endpoint**

```csharp
using WolverineFx.Http;

public sealed class DuffelWebhookEndpoint
{
    [WolverinePost("/webhooks/duffel"), AllowAnonymous]
    public static async Task<IResult> Receive(
        HttpRequest req,
        DuffelWebhookVerifier verifier,
        FlightsDbContext db,
        IMessageBus bus,
        TimeProvider time,
        ILogger<DuffelWebhookEndpoint> log,
        CancellationToken ct)
    {
        // 1. Read raw body
        req.EnableBuffering();
        using var ms = new MemoryStream();
        await req.Body.CopyToAsync(ms, ct);
        var raw = ms.ToArray();

        // 2. Verify signature
        if (!req.Headers.TryGetValue("Duffel-Signature", out var sig) || !verifier.Verify(raw, sig!))
            return Results.Unauthorized();

        // 3. Parse minimal event header for dedup
        var dto = JsonSerializer.Deserialize<DuffelWebhookEventDto>(raw,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (dto is null) return Results.BadRequest();

        // 4. Dedup via unique (source, event_id)
        var existing = await db.WebhookInbox
            .FirstOrDefaultAsync(x => x.Source == "duffel" && x.EventId == dto.Id, ct);
        if (existing is not null) return Results.Ok();

        var row = new WebhookInboxEntity
        {
            Id = Guid.NewGuid(),
            Source = "duffel",
            EventId = dto.Id,
            EventType = dto.Type,
            RawPayload = Encoding.UTF8.GetString(raw),
            Signature = sig!,
            ReceivedAt = time.GetUtcNow(),
        };
        db.WebhookInbox.Add(row);
        await db.SaveChangesAsync(ct);

        // 5. Publish via Wolverine outbox
        await bus.PublishAsync(new ProcessDuffelWebhookCommand(row.Id));

        return Results.Ok();
    }
}
```

- [ ] **Step 2: Integration test (Alba + WireMock — but webhook is incoming so we POST directly)**:
    - Valid signature → 200, inbox row inserted, command published.
    - Invalid signature → 401, no row inserted.
    - Duplicate `event_id` → 200, second insert skipped (idempotent).
- [ ] **Step 3: Commit**

```bash
git commit -m "feat(flights): add Duffel webhook endpoint with HMAC verification and inbox dedup"
```

---

# Phase 17 — NL-search cross-service

Per spec §9 and ADR `0020`. Travel.Host publishes `NlSearchRequested`; Travel.AI consumes via NATS, replies `NlSearchParsed` with structured `SearchCriteria`. NATS subject `travel.ai.nl_search`. 6s timeout. Cost ledger writes per call.

## Task 40: Cross-service contracts in shared abstractions

**Files:**
- Create: `modules/flights/Travel.Modules.Flights.Application/Contracts/{NlSearchRequested, NlSearchParsed}.cs`

These contracts are duplicated as compatible record types in `Travel.AI` for now (M1) — a future shared `Travel.Contracts.Flights` package could centralize, but per concept §4.3 we avoid premature shared-package coupling.

```csharp
namespace Travel.Modules.Flights.Application.Contracts;

public sealed record NlSearchRequested(string Query, Guid CorrelationId, string Locale = "ru");

public sealed record NlSearchParsed(
    Guid CorrelationId,
    string Origin,           // IATA
    string Destination,      // IATA
    DateOnly DepartureDate,
    DateOnly? ReturnDate,
    int PassengerCount,
    string CabinClass,       // serialized cabin code
    string Currency);
```

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Mirror identical types in `apps/Travel.AI/NlSearch/Contracts/`** — Pact verification in Phase 17.4 will catch any drift.
- [ ] **Step 3: Commit**

```bash
git commit -m "feat(flights): add NlSearchRequested / NlSearchParsed cross-service contracts"
```

---

## Task 41: NL-search system prompt + cost ledger

**Files:**
- Create: `prompts/v1/flights/nl-search.system.md`
- Create: `apps/Travel.AI/Persistence/{AiDbContext,Entities/CostLedgerEntry}.cs`
- Create: `apps/Travel.AI/Persistence/Migrations/20260513_CostLedgerInit.cs`

- [ ] **Step 1: Prompt** — system message telling Claude to extract `{origin, destination, departure_date, return_date, passenger_count, cabin_class, currency}` from a free-form RU/EN flight-search query. Use ISO dates. Default `cabin_class=economy`, `currency=RUB` if not specified, `passenger_count=1`. Examples (RU + EN). Anti-pattern examples (don't infer destinations from nicknames).
- [ ] **Step 2: `AiDbContext`** mirroring `FlightsDbContext` shape but in schema `ai`. Adds `DbSet<CostLedgerEntry>`.
- [ ] **Step 3: Entity**

```csharp
namespace Travel.AI.Persistence.Entities;

public sealed class CostLedgerEntry
{
    public Guid Id { get; set; }
    public string Feature { get; set; } = default!;       // "flights.nl_search"
    public string Model { get; set; } = default!;         // "claude-opus-4-7"
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
    public Guid? UserId { get; set; }
    public Guid CorrelationId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
```

Configuration: schema `ai`, table `cost_ledger`. Migration via `dotnet ef migrations add CostLedgerInit --project apps/Travel.AI --output-dir Persistence/Migrations --context AiDbContext`.

- [ ] **Step 4: Commit**

```bash
git commit -m "feat(ai): add cost_ledger entity, migration, and NL-search system prompt"
```

---

## Task 42: Travel.AI `NlSearchAiHandler`

**Files:** `apps/Travel.AI/NlSearch/{NlSearchAiHandler, NlSearchPrompts, ParsedSearchCriteriaDto}.cs`

- [ ] **Step 1: `ParsedSearchCriteriaDto`** — structured-output schema record (matches contract above).
- [ ] **Step 2: `NlSearchPrompts`** — loads prompt from `prompts/v1/flights/nl-search.system.md` (embedded resource or runtime read).
- [ ] **Step 3: Handler — consumes `NlSearchRequested`**

```csharp
using Microsoft.Extensions.AI;
using Wolverine.Attributes;
using Travel.Modules.Flights.Application.Contracts;

namespace Travel.AI.NlSearch;

public static class NlSearchAiHandler
{
    [WolverineHandler]
    public static async Task<NlSearchParsed> Handle(
        NlSearchRequested req,
        IChatClient chat,
        AiDbContext db,
        TimeProvider time,
        ILogger<NlSearchRequested> log,
        CancellationToken ct)
    {
        var system = NlSearchPrompts.NlSearchSystem;
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, system),
            new(ChatRole.User, req.Query)
        };

        var response = await chat.GetResponseAsync<ParsedSearchCriteriaDto>(messages,
            new ChatOptions { Temperature = 0.1f }, ct);

        // Cost ledger
        var usage = response.Usage;
        var costUsd = ComputeCost(usage?.InputTokenCount ?? 0, usage?.OutputTokenCount ?? 0);
        db.CostLedger.Add(new CostLedgerEntry
        {
            Id = Guid.NewGuid(),
            Feature = "flights.nl_search",
            Model = response.ModelId ?? "claude-opus-4-7",
            InputTokens = usage?.InputTokenCount ?? 0,
            OutputTokens = usage?.OutputTokenCount ?? 0,
            CostUsd = costUsd,
            CorrelationId = req.CorrelationId,
            OccurredAt = time.GetUtcNow()
        });
        await db.SaveChangesAsync(ct);

        var p = response.Result;
        return new NlSearchParsed(
            CorrelationId: req.CorrelationId,
            Origin: p.Origin,
            Destination: p.Destination,
            DepartureDate: p.DepartureDate,
            ReturnDate: p.ReturnDate,
            PassengerCount: p.PassengerCount,
            CabinClass: p.CabinClass,
            Currency: p.Currency);
    }

    private static decimal ComputeCost(int input, int output)
    {
        // Anthropic Opus 4.7 pricing per spec §13.4 — pin in NlSearchPrompts constants
        const decimal inputPer1M = 15m;
        const decimal outputPer1M = 75m;
        return (input * inputPer1M + output * outputPer1M) / 1_000_000m;
    }
}
```

- [ ] **Step 4: Wire `IChatClient` in `apps/Travel.AI/Program.cs`** via `Anthropic` SDK adapter (pinned 12.x). `services.AddChatClient(b => b.UseAnthropic(o => o.ApiKey = config["Anthropic:ApiKey"]).Build());`
- [ ] **Step 5: Wolverine listener config** for Travel.AI — bind to NATS subject `travel.ai.nl_search`.
- [ ] **Step 6: Integration test (Travel.AI side, mocked IChatClient)** — feed a `NlSearchRequested`, mock returns a known structured object, assert response shape + cost ledger row.
- [ ] **Step 7: Commit**

```bash
git commit -m "feat(ai): add NlSearchAiHandler with IChatClient + Anthropic + cost ledger"
```

---

## Task 43: Travel.Host `NlSearchHandler` + endpoint

**Files:**
- Create: `modules/flights/Travel.Modules.Flights.Application/Handlers/NlSearch/NlSearchHandler.cs`

- [ ] **Step 1: Handler** — Wolverine `IMessageBus.InvokeAsync<NlSearchParsed>(NlSearchRequested)` with 6s timeout. On timeout/error → `FlightsErrors.NlSearchUnparseable`. On parse success, build `SearchCriteria` (re-using value-object factories), call `SearchFlightsHandler` directly.

```csharp
public sealed record NlSearchQuery(string Query, string Locale = "ru");

public static class NlSearchHandler
{
    [WolverineHandler]
    public static async Task<ErrorOr<SearchResult>> Handle(
        NlSearchQuery q,
        IMessageBus bus,
        CancellationToken ct)
    {
        var corrId = Guid.NewGuid();
        var req = new NlSearchRequested(q.Query, corrId, q.Locale);
        NlSearchParsed parsed;
        try
        {
            parsed = await bus.InvokeAsync<NlSearchParsed>(req,
                cancellation: ct, timeout: TimeSpan.FromSeconds(6));
        }
        catch (TimeoutException) { return FlightsErrors.NlSearchUnparseable; }

        // Build domain SearchCriteria
        var origin = IataCode.Create(parsed.Origin);
        if (origin.IsError) return origin.FirstError;
        var dest = IataCode.Create(parsed.Destination);
        if (dest.IsError) return dest.FirstError;
        var cabin = CabinClass.Parse(parsed.CabinClass);
        if (cabin.IsError) return cabin.FirstError;
        var currency = CurrencyCode.Create(parsed.Currency);
        if (currency.IsError) return currency.FirstError;
        var sc = SearchCriteria.Create(origin.Value, dest.Value,
            parsed.DepartureDate, parsed.ReturnDate, parsed.PassengerCount, cabin.Value, currency.Value);
        if (sc.IsError) return sc.FirstError;

        // Run regular search
        var searchResult = await bus.InvokeAsync<ErrorOr<SearchResult>>(new SearchFlightsQuery(sc.Value), ct);
        return searchResult;
    }
}
```

- [ ] **Step 2: Integration test (Travel.Host side, with WireMock stubbing Anthropic via Travel.AI test double, or by mocking the Wolverine reply directly)** — assert that NL query "из Москвы в Питер на завтра" results in search with correct IATA codes.
- [ ] **Step 3: Commit**

```bash
git commit -m "feat(flights): add NlSearchHandler in Host with Wolverine request/reply"
```

---

## Task 44: AI-eval suite

**Files:**
- Create project: `tests/flights/Travel.Modules.Flights.Tests.AiEvals/`
- Create: `tests/flights/Travel.Modules.Flights.Tests.AiEvals/Cases/nl-search-cases.json`
- Create: `tests/flights/Travel.Modules.Flights.Tests.AiEvals/NlSearchEvalRunner.cs`

- [ ] **Step 1: Create project**

```bash
dotnet new xunit3 -o tests/flights/Travel.Modules.Flights.Tests.AiEvals
dotnet sln Travel.sln add tests/flights/Travel.Modules.Flights.Tests.AiEvals/Travel.Modules.Flights.Tests.AiEvals.csproj
```

Reference `Travel.AI` project and `Microsoft.Extensions.AI`.

- [ ] **Step 2: Write `nl-search-cases.json`** — 15 cases covering:
    - 5 RU clear ("из Москвы в Санкт-Петербург 25 июня", "из Сочи в Стамбул на завтра", "из Питера в Калининград на выходные")
    - 5 EN clear ("from JFK to LHR on July 4th", "Moscow to Istanbul next weekend round trip", …)
    - 3 ambiguity ("из Москвы куда-нибудь тёплое") — assert `NlSearchUnparseable` or low-confidence flag
    - 2 date-inference ("на выходные", "следующая пятница") — assert date within ±1 day window of expected

Format:
```json
[
  {
    "query": "из Москвы в Санкт-Петербург 25 июня",
    "expected": {
      "origin": "DME|SVO|VKO",
      "destination": "LED",
      "departure_date": "2026-06-25",
      "return_date": null
    },
    "tolerance_days": 0
  }
]
```

- [ ] **Step 3: `NlSearchEvalRunner`** — xUnit `Theory` parameterized by file loader; runs each case against the real `IChatClient` (using `[Trait("Category","AiEval")]` so CI can skip if no API key), assesses pass/fail per case, prints a summary.
- [ ] **Step 4: Add CI workflow step** in `.github/workflows/ci.yml` (Foundation): on `main` only (`if: github.ref == 'refs/heads/master'`), run AI-evals with secrets `ANTHROPIC_API_KEY`. Branch builds skip (cost control).
- [ ] **Step 5: Commit**

```bash
git commit -m "test(flights): add NL-search AI-eval suite with 15 cases"
```

---

## Task 45: Pact contract Host↔AI + ADR 0020

**Files:**
- Create: `tests/Travel.Tests.Contract/Flights/NlSearchContract.cs`
- Create: `docs/adr/0020-nl-search-cross-service-contract.md`

- [ ] **Step 1: Pact consumer test (Travel.Host)** — verify the consumer expects `NlSearchParsed` shape after publishing `NlSearchRequested`. Pact mock server stubs the AI side. Generates pact file in `tests/Travel.Tests.Contract/pacts/`.
- [ ] **Step 2: Pact provider verification (Travel.AI)** — replays pact file against real handler.
- [ ] **Step 3: ADR 0020** — record decision: separate-process AI for cost/cadence/scalability, contract pinned via Pact.
- [ ] **Step 4: Commit**

```bash
git commit -m "test(flights): add Pact Host↔AI contract for NL-search; docs(adr): 0020"
```

---

# Phase 18 — Notifications

## Task 46: MailKit email sender + Razor template engine setup

**Files:**
- Create: `Infrastructure/Notifications/Email/MailKitEmailSender.cs`
- Create: `Infrastructure/Notifications/Email/IEmailSender.cs` (in `Application/Notifications/`)
- Add packages: `MailKit`, `RazorLight` (lightweight Razor template engine; or use `Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation` if Razor pages framework is preferred)

- [ ] **Step 1: Interface**

```csharp
namespace Travel.Modules.Flights.Application.Notifications;

public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, string textBody, CancellationToken ct);
}
```

- [ ] **Step 2: `MailKitEmailSender`** — uses `SmtpClient` from MailKit; reads SMTP host/port from `IConfiguration` (Aspire injects Mailpit on dev).
- [ ] **Step 3: `EmailRenderer`** — loads `.cshtml` templates from `Infrastructure/Notifications/Email/Templates/` via `RazorLight`. Method: `RenderAsync(string templateName, object model, CultureInfo locale) -> (subject, html, text)`.
- [ ] **Step 4: Commit**

```bash
git commit -m "feat(flights): add MailKit email sender and Razor template renderer"
```

---

## Task 47: Email templates + handlers

**Files:**
- Create: `Infrastructure/Notifications/Email/Templates/{OrderConfirmation.ru, OrderConfirmation.en, OrderCancellation.ru, OrderCancellation.en}.cshtml`
- Create: `Application/Handlers/Notifications/{SendOrderConfirmationEmailHandler, SendOrderCancellationEmailHandler}.cs`

- [ ] **Step 1: Razor templates** — model `OrderEmailModel(string GuestName, string OrderId, string Itinerary, Money Total, string PnrOrBookingRef)`. RU template uses Cyrillic; EN — English. Subject line first via `Layout = null;` + `@ViewBag.Subject = "...";`.
- [ ] **Step 2: Handlers** — subscribe to domain event broadcast (via Wolverine cascading from saga handlers, not the Marten stream). Handler loads order details, picks locale from Keycloak claim, renders template, sends.

```csharp
public static class SendOrderConfirmationEmailHandler
{
    [WolverineHandler]
    public static async Task Handle(
        OrderConfirmed evt,
        FlightsDbContext db,
        IEmailSender sender,
        EmailRenderer renderer,
        IUserDirectory users,
        CancellationToken ct)
    {
        var order = await db.Orders.Where(o => o.AggregateId == evt.AggregateId).FirstOrDefaultAsync(ct);
        if (order is null) return;
        var user = await users.GetAsync(order.UserId!.Value, ct);
        if (user is null) return;

        var model = new OrderEmailModel(
            GuestName: user.GivenName,
            OrderId: order.AggregateId.ToString(),
            Itinerary: order.ItineraryJson,           // formatted in template
            Total: Money.Create(order.TotalAmount, CurrencyCode.Create(order.Currency).Value).Value,
            PnrOrBookingRef: order.AggregateId.ToString().Substring(0, 6).ToUpper());

        var locale = user.Locale ?? "ru";
        var (subject, html, text) = await renderer.RenderAsync("OrderConfirmation", model, new CultureInfo(locale));
        await sender.SendAsync(user.Email, subject, html, text, ct);
    }
}
```

Note: `IUserDirectory` is a thin abstraction over Keycloak (resolves user details by `sub`); add a placeholder interface in `Travel.Modules.Identity.Application` or `Travel.Shared.Web`. M1 minimum: `record UserProfile(Guid Id, string Email, string GivenName, string FamilyName, string? Locale)` and a `KeycloakUserDirectory` implementation that calls the Keycloak admin API.

- [ ] **Step 3: Integration test** — append `OrderConfirmed` event (via direct command); assert Mailpit received message; subject and body contain expected substrings.
- [ ] **Step 4: Commit**

```bash
git commit -m "feat(flights): add OrderConfirmation/OrderCancellation email templates and handlers"
```

---

## Task 48: SSE registry + endpoint + handler

**Files:**
- Create: `Infrastructure/Notifications/Sse/OrderSseConnectionRegistry.cs`
- Create: `Application/Handlers/Notifications/PublishOrderSseHandler.cs`
- Create: `Api/Endpoints/OrderEventsSseEndpoint.cs`

- [ ] **Step 1: Connection registry** — singleton `ConcurrentDictionary<Guid, List<Channel<SseEvent>>>` keyed by orderId. Methods: `Register(orderId, channel)`, `Publish(orderId, evt)`, `Unregister(orderId, channel)`, `Task<Guid?> LookupOrderOwnerAsync(Guid orderId, CancellationToken)` (delegates to `FlightsDbContext` to read `OrderReadModelEntity.UserId` for authorization checks). Bounded channel size 32 (drop-oldest on overflow).
- [ ] **Step 2: `PublishOrderSseHandler`** — subscribes to `OrderConfirmed`, `OrderTicketed`, `OrderCancelled`. Maps domain event to `record SseEvent(string Type, Guid OrderId, JsonElement Payload, DateTimeOffset At)`; calls registry `Publish`.
- [ ] **Step 3: SSE endpoint**

```csharp
public sealed class OrderEventsSseEndpoint
{
    [WolverineGet("/events/flights/orders/{orderId:guid}"), Authorize]
    public static async Task Stream(
        Guid orderId,
        HttpContext ctx,
        OrderSseConnectionRegistry registry,
        TimeProvider time,
        CancellationToken ct)
    {
        var userId = ctx.User.GetUserId();

        // Authorization: order must belong to caller. 404 (not 403) hides existence from non-owners.
        var ownerId = await registry.LookupOrderOwnerAsync(orderId, ct);
        if (ownerId is null || ownerId != userId) { ctx.Response.StatusCode = 404; return; }

        ctx.Response.Headers.ContentType = "text/event-stream";
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.Headers["X-Accel-Buffering"] = "no";

        var channel = Channel.CreateBounded<SseEvent>(new BoundedChannelOptions(32)
            { FullMode = BoundedChannelFullMode.DropOldest });
        registry.Register(orderId, channel);
        try
        {
            using var heartbeat = new PeriodicTimer(TimeSpan.FromSeconds(15));
            var heartbeatTask = HeartbeatLoop(ctx, heartbeat, ct);
            await foreach (var evt in channel.Reader.ReadAllAsync(ct))
            {
                var json = JsonSerializer.Serialize(evt);
                await ctx.Response.WriteAsync($"event: {evt.Type}\n", ct);
                await ctx.Response.WriteAsync($"data: {json}\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
            }
        }
        finally { registry.Unregister(orderId, channel); }
    }

    private static async Task HeartbeatLoop(HttpContext ctx, PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await ctx.Response.WriteAsync(":\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
            }
        }
        catch (OperationCanceledException) { }
    }
}
```

- [ ] **Step 4: Integration test (Alba SSE harness)** — connect to stream, fire `OrderConfirmed` via command, assert event received within 1s.
- [ ] **Step 5: Commit**

```bash
git commit -m "feat(flights): add SSE order events endpoint with bounded-channel registry"
```

---

# Phase 19 — API layer

Per spec §15.1.

## Task 49: API contract DTOs

**Files:** `modules/flights/Travel.Modules.Flights.Api/Contracts/*.cs`

- [ ] **Step 1: Define all request/response records**

```csharp
public sealed record SearchRequest(
    string Origin, string Destination,
    DateOnly DepartureDate, DateOnly? ReturnDate,
    int PassengerCount = 1, string CabinClass = "economy",
    string Currency = "RUB");

public sealed record OfferDto(
    Guid Id, string Provider, decimal TotalAmount, string Currency,
    ItineraryDto Itinerary, DateTimeOffset FetchedAt,
    DateTimeOffset? ExpiresAt, string? ProviderOfferRef,
    string? DeeplinkUrl, string? PartnerName);

public sealed record ItineraryDto(
    SliceDto[] Slices, TimeSpan TotalDuration, bool IsRoundTrip);

public sealed record SliceDto(
    string Origin, string Destination, SegmentDto[] Segments, TimeSpan Duration);

public sealed record SegmentDto(
    string Origin, string Destination, DateTimeOffset DepartAt, DateTimeOffset ArriveAt,
    string CarrierCode, string FlightNumber, string CabinClass);

public sealed record PartialFailureDto(string Provider, string ErrorCode, long ElapsedMs);

public sealed record SearchResponse(OfferDto[] Offers, PartialFailureDto[] PartialFailures);

public sealed record NlSearchRequest(string Query, string Locale = "ru");

public sealed record QuoteOfferRequest(string ProviderOfferRef, string Provider);
public sealed record QuotedOfferResponse(Guid AggregateId, OfferDto Offer);

public sealed record HoldOfferRequest(
    Guid AggregateId, PassengerInfoDto[] Passengers);  // M1: length must be 1

public sealed record PassengerInfoDto(
    string GivenName, string FamilyName, DateOnly DateOfBirth,
    string Gender, string Email, string Phone);

public sealed record HeldOrderResponse(Guid AggregateId, string ProviderOrderId, DateTimeOffset HeldUntil);

public sealed record ConfirmOrderRequest(Guid AggregateId);
public sealed record ConfirmedOrderResponse(Guid AggregateId, string Status, string? PaymentRef);

public sealed record CancelOrderRequest(Guid AggregateId);

public sealed record OrderResponse(
    Guid AggregateId, string Status, decimal TotalAmount, string Currency,
    ItineraryDto Itinerary, string[] TicketNumbers, DateTimeOffset BookedAt,
    DateTimeOffset? TicketedAt, DateTimeOffset? CancelledAt, DateTimeOffset? RefundedAt);

public sealed record OrderListResponse(OrderResponse[] Items, int Limit, int Offset);
```

- [ ] **Step 2: Mapping helpers** — `OfferDto.From(Offer offer)` static, `ItineraryDto.From(...)`, etc.
- [ ] **Step 3: Commit**

```bash
git commit -m "feat(flights): add API contract DTOs (SearchRequest/Response, Offer DTOs, etc.)"
```

---

## Task 50: Anonymous endpoints — Search, NlSearch, QuoteOffer

**Files:** `Api/Endpoints/{SearchEndpoint, NlSearchEndpoint, QuoteOfferEndpoint}.cs`

- [ ] **Step 1: `SearchEndpoint`**

```csharp
public sealed class SearchEndpoint
{
    [WolverinePost("/api/flights/search"), AllowAnonymous]
    public static async Task<IResult> Post(
        SearchRequest req, IMessageBus bus, CancellationToken ct)
    {
        var origin = IataCode.Create(req.Origin);
        if (origin.IsError) return Results.Problem(origin.Errors.ToProblemDetails());
        var dest = IataCode.Create(req.Destination);
        if (dest.IsError) return Results.Problem(dest.Errors.ToProblemDetails());
        var cabin = CabinClass.Parse(req.CabinClass);
        if (cabin.IsError) return Results.Problem(cabin.Errors.ToProblemDetails());
        var currency = CurrencyCode.Create(req.Currency);
        if (currency.IsError) return Results.Problem(currency.Errors.ToProblemDetails());
        var sc = SearchCriteria.Create(origin.Value, dest.Value,
            req.DepartureDate, req.ReturnDate, req.PassengerCount, cabin.Value, currency.Value);
        if (sc.IsError) return Results.Problem(sc.Errors.ToProblemDetails());

        var result = await bus.InvokeAsync<ErrorOr<SearchResult>>(new SearchFlightsQuery(sc.Value), ct);
        if (result.IsError) return Results.Problem(result.Errors.ToProblemDetails());

        return Results.Ok(new SearchResponse(
            result.Value.Offers.Select(OfferDto.From).ToArray(),
            result.Value.PartialFailures.Select(f => new PartialFailureDto(f.Provider, f.ErrorCode, f.ElapsedMs)).ToArray()));
    }
}
```

- [ ] **Step 2: `NlSearchEndpoint`** — same shape, builds `NlSearchQuery`.
- [ ] **Step 3: `QuoteOfferEndpoint`** — anonymous, builds `QuoteOfferCommand`, returns `QuotedOfferResponse`.
- [ ] **Step 4: Alba integration tests** — full round-trip through middleware → handler → Marten/EF; assert response shape.
- [ ] **Step 5: Commit**

```bash
git commit -m "feat(flights): add anonymous endpoints (search, nl-search, quote)"
```

---

## Task 51: Authorized endpoints — Hold, Confirm, Cancel, GetOrder, ListOrders

**Files:** `Api/Endpoints/{HoldOfferEndpoint, ConfirmOrderEndpoint, CancelOrderEndpoint, GetOrderEndpoint, ListOrdersEndpoint}.cs`

- [ ] **Step 1: Hold, Confirm, Cancel** — `[Authorize]`, parse body, build command, dispatch via bus. Hold validates `req.Passengers.Length == 1` for M1.
- [ ] **Step 2: GetOrder, ListOrders** — `[Authorize]`, read from `OrderReadModel` via query. UserId from claim.
- [ ] **Step 3: Keycloak realm update**

Modify `infra/keycloak/travel-realm.json` (Foundation provided this file) — add client scope `flights:book`. Test by running AppHost and verifying `https://localhost:.../realms/travel/.well-known/openid-configuration` exposes the new scope.

- [ ] **Step 4: Integration tests (Alba + JWT)** — issue a JWT via Keycloak (Aspire-provisioned), call each authorized endpoint, assert 200 with body; call without JWT, assert 401.
- [ ] **Step 5: Commit**

```bash
git add modules/flights/Travel.Modules.Flights.Api/Endpoints/{Hold,Confirm,Cancel,GetOrder,ListOrders}*.cs infra/keycloak/travel-realm.json
git commit -m "feat(flights): add authorized booking endpoints; infra(keycloak): add flights:book scope"
```

---

# Phase 20 — Observability

## Task 52: `FlightsMetrics` OTel meter

**Files:** `modules/flights/Travel.Modules.Flights.Infrastructure/Observability/FlightsMetrics.cs`

- [ ] **Step 1: Meter + instruments per spec §13.1**

```csharp
using System.Diagnostics.Metrics;

namespace Travel.Modules.Flights.Infrastructure.Observability;

public sealed class FlightsMetrics
{
    public const string MeterName = "Travel.Flights";

    public Histogram<double> SearchLatency { get; }
    public Counter<long> SearchErrors { get; }
    public ObservableGauge<double> PartialFillRate { get; }
    public Counter<long> WebhookReceived { get; }
    public Histogram<double> WebhookProcessingLag { get; }
    public Counter<long> PaymentSuccessTotal { get; }
    public Counter<long> PaymentFailureTotal { get; }
    public Counter<long> NlSearchTokensUsed { get; }
    public Counter<double> NlSearchCostUsd { get; }
    public Counter<long> AggregateEventsAppended { get; }

    private double _lastPartialFillRate;
    public void RecordPartialFillRate(double rate) => _lastPartialFillRate = rate;

    public FlightsMetrics(IMeterFactory factory)
    {
        var m = factory.Create(MeterName);
        SearchLatency = m.CreateHistogram<double>("flights.search.duration_ms", "ms");
        SearchErrors = m.CreateCounter<long>("flights.search.errors");
        PartialFillRate = m.CreateObservableGauge("flights.search.partial_fill_rate", () => _lastPartialFillRate);
        WebhookReceived = m.CreateCounter<long>("flights.webhook.received_total");
        WebhookProcessingLag = m.CreateHistogram<double>("flights.webhook.processing_lag_ms", "ms");
        PaymentSuccessTotal = m.CreateCounter<long>("flights.payment.success_total");
        PaymentFailureTotal = m.CreateCounter<long>("flights.payment.failure_total");
        NlSearchTokensUsed = m.CreateCounter<long>("flights.nl_search.tokens_used");
        NlSearchCostUsd = m.CreateCounter<double>("flights.nl_search.cost_usd");
        AggregateEventsAppended = m.CreateCounter<long>("flights.aggregate.events_appended_total");
    }
}
```

- [ ] **Step 2: Register meter in `apps/Travel.Host/Program.cs`** OTel config (Foundation has the base):

```csharp
builder.Services.AddSingleton<FlightsMetrics>();
builder.Services.AddOpenTelemetry().WithMetrics(b => b.AddMeter(FlightsMetrics.MeterName));
```

Travel.AI similarly adds `gen_ai.*` instruments via `Microsoft.Extensions.AI` instrumentation (already part of M.E.AI 10.5).

- [ ] **Step 3: Hook metrics into handlers**:
    - `SearchFlightsHandler` already records latency (Task 30).
    - `ConfirmOrderHandler` increments `PaymentSuccessTotal`/`PaymentFailureTotal`.
    - `DuffelWebhookEndpoint` increments `WebhookReceived`.
    - `DuffelWebhookHandler` records `WebhookProcessingLag = ProcessedAt - ReceivedAt`.
    - Marten append-listener increments `AggregateEventsAppended` (use Marten `IEventStore.Append` interceptor or wrap calls).
    - `NlSearchAiHandler` increments `NlSearchTokensUsed` + `NlSearchCostUsd`.

- [ ] **Step 4: Structured logging enrichment** — extend Foundation OTel `Serilog.Enrichers` config to add `correlation_id` (`Activity.Current?.TraceId`), `user_id` (claim), `order_id` (passed via `using LogContext.PushProperty(...)` in handlers).
- [ ] **Step 5: Commit**

```bash
git commit -m "feat(flights): add OTel metrics meter and wire latency/payment/webhook/AI instrumentation"
```

---

# Phase 21 — Architecture tests

## Task 53: Flights architecture tests

**Files:** `tests/Travel.Tests.Architecture/Flights/FlightsArchitectureTests.cs`

- [ ] **Step 1: Test cases (ArchUnit)**

1. `Core` does not depend on `Infrastructure` or `Application` or `Api`.
2. `Application` does not depend on `Infrastructure` or `Api`.
3. `Infrastructure` does not depend on `Api`.
4. No type in `Travel.Modules.Flights.*` namespace references `Travel.Modules.Hotels`, `Travel.Modules.Rail`, `Travel.Modules.Trips` (cross-module isolation).
5. No type in `Core` uses `DateTime.UtcNow` or `DateTimeOffset.UtcNow` (Roslyn-rule check via `Compilation.GetSymbolsWithName` or fluent ArchUnit predicate on `MethodMembers` calling those).
6. No `[TestOnly]` class is registered into a service collection in production-config code. Approach: locate `FlightsModuleStartup.AddProductionFlightsServices(...)` and assert no AddXxx<DuffelTestWalletPaymentGateway>() inside. Pragmatic compromise: add a dedicated method `RegisterTestPayments()` in startup, and the test asserts `RegisterTestPayments` is only called from `Tests` projects via call-graph (use Roslyn semantic model). If too complex for M1, fall back to a runtime check at startup ("if env is Production and `[TestOnly]` is registered, throw").
7. All Duffel/Travelpayouts DTOs live under `Infrastructure/Providers/{Duffel|Travelpayouts}/Dto/` and are not referenced from `Core` or `Application`.
8. Domain events (in `Core/DomainEvents`) all implement `IDomainEvent`.
9. Value objects (in `Core/ValueObjects`) are all `sealed record`.

- [ ] **Step 2: Implement tests using `ArchitectureTestBase` from Foundation.**
- [ ] **Step 3: Run, ensure green**

```bash
dotnet test tests/Travel.Tests.Architecture --filter "FullyQualifiedName~Flights"
```

- [ ] **Step 4: Commit**

```bash
git commit -m "test(arch): add architecture tests for Flights M1 boundaries and conventions"
```

---

# Phase 22 — Wrap-up

## Task 54: README BYO-keys + happy-path instructions

**Files:** `README.md` (modify)

- [ ] **Step 1: Add section "BYO API Keys for M1"** with three subsections:
    - **Duffel:** sign up at duffel.com, get sandbox API key + webhook signing secret, paste into `.env`: `FLIGHTS__DUFFEL__APIKEY=`, `FLIGHTS__DUFFEL__WEBHOOKSECRET=`.
    - **Travelpayouts:** sign up at travelpayouts.com, get API token + partner marker, paste into `.env`: `FLIGHTS__TRAVELPAYOUTS__APITOKEN=`, `FLIGHTS__TRAVELPAYOUTS__PARTNERMARKER=`.
    - **Anthropic:** sign up at console.anthropic.com, paste into `.env`: `ANTHROPIC__APIKEY=`.
- [ ] **Step 2: Add section "M1 happy-path walkthrough"** with concrete curl commands hitting the local AppHost — search LED→DME, NL-search, quote, hold, confirm. Mention that Mailpit UI at `http://localhost:8025` shows the confirmation email; SSE testable with `curl --no-buffer …`.
- [ ] **Step 3: Add "Known limitations in M1"** — RU-from-Duffel-sandbox limited; production booking requires Duffel KYC; PII unencrypted; refunds airline-initiated only; multi-pax not supported.
- [ ] **Step 4: Commit**

```bash
git commit -m "docs(readme): document BYO-keys and M1 happy-path walkthrough"
```

---

## Task 55: Final acceptance check (manual + reportable)

- [ ] **Step 1: Run full BE test pipeline**

```bash
dotnet test Travel.sln --filter "Category!=AiEval"
```

Expected: all green.

- [ ] **Step 2: Run AppHost + happy path manually**

```bash
dotnet run --project apps/Travel.AppHost
# In another shell:
curl -X POST http://localhost:5000/api/flights/search -H "Content-Type: application/json" \
  -d '{"origin":"LED","destination":"DME","departureDate":"2026-07-15"}'
# Pick an offer, run quote → hold → confirm cycle; observe Mailpit at http://localhost:8025
```

- [ ] **Step 3: Run AI-evals locally** (with `ANTHROPIC_API_KEY` env set)

```bash
dotnet test tests/flights/Travel.Modules.Flights.Tests.AiEvals
```

Expected: ≥ 13/15 cases pass.

- [ ] **Step 4: Cross-check spec §21 acceptance items 1-10** against integration test names — ensure each item has a corresponding `[Fact]`. If any gap, add the missing test.
- [ ] **Step 5: No commit** — this task is a verification gate before the PR.

---

## Task 56: Open the PR

- [ ] **Step 1: Push branch and open PR**

```bash
git push -u origin flights-m1
gh pr create --title "Flights M1 (backend): search + single-pax booking + NL-search + observability" --body "$(cat <<'BODY'
## Summary
- Implements spec [`2026-05-13-flights-m1-design.md`](docs/superpowers/specs/2026-05-13-flights-m1-design.md) — backend slice only.
- Full BookingAggregate lifecycle on Marten ES; mixed bookable (Duffel) + deeplink (Travelpayouts) aggregation; webhook inbox/outbox; idempotency middleware; NL-search via Travel.AI; email + SSE notifications; full OTel observability.
- 8 ADRs (0013-0020) added.
- 7 test layers green (Unit, Integration, Architecture, Contract, AI-evals locally, Webhook simulator scaffolded; FE-driven E2E + visual deferred to FE plan).

## Test plan
- [ ] `dotnet test Travel.sln --filter "Category!=AiEval"` green
- [ ] Local happy-path search → book → ticketed via Mailpit + Aspire dashboard
- [ ] AI-evals ≥ 13/15 with personal ANTHROPIC_API_KEY
- [ ] OTel dashboard shows all flights.* metrics
- [ ] Cost ledger populated after NL-search

🤖 Generated with [Claude Code](https://claude.com/claude-code)
BODY
)"
```

- [ ] **Step 2: Return PR URL.**

---

## Self-review (run after this plan completes implementation)

Spec coverage matrix — verify each spec §21 acceptance item is covered:

| Spec §21 item | Tasks |
|---|---|
| 1. Search LED→DME mixed list | T22, T28, T30, T50 |
| 2. End-to-end book (quote → hold → confirm → ticketed) | T31, T32, T33, T38 |
| 3. Cancel from Held and Confirmed | T34, T51 |
| 4. Webhook idempotency + state update | T38, T39 |
| 5. NL-search parses RU+EN | T42, T43, T44 |
| 6. Email + SSE on 3 events | T46-T48 |
| 7. All 7 test layers green | T46/47/48 plus existing infrastructure |
| 8. OTel dashboard with metrics §13.1 | T52 |
| 9. Cost ledger populated | T41, T42 |
| 10. 8 ADRs | T12, T26, T36, T45 |
| 11. README BYO-keys + happy path | T54 |

If any column shows fewer than 1 task — add it before merging.

---

> **End of plan. Total: 56 tasks across 22 phases. Estimated implementation time (single developer with TDD discipline): 6-10 working days.**

