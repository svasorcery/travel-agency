using ErrorOr;
using JasperFx;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Testcontainers.PostgreSql;
using Travel.Modules.Flights.Api.Composition;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Handlers.Booking;
using Travel.Modules.Flights.Application.Privacy;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Shared.Abstractions;
using Travel.Tests.Fixtures;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Booking;

[Trait("Category", "Integration")]
public sealed class HoldOfferHandlerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder(
        "pgvector/pgvector:pg17"
    ).Build();
    private DocumentStore _store = default!;

    private static readonly IataCode Led = IataCode.Create("LED").Value;
    private static readonly IataCode Dme = IataCode.Create("DME").Value;
    private static readonly CurrencyCode Rub = CurrencyCode.Create("RUB").Value;

    public async ValueTask InitializeAsync()
    {
        await _pg.StartAsync();
        _store = DocumentStore.For(opts =>
        {
            opts.Connection(_pg.GetConnectionString());
            opts.AutoCreateSchemaObjects = AutoCreate.All;
            FlightsModule.ConfigureMarten(opts);
        });
    }

    public async ValueTask DisposeAsync()
    {
        _store.Dispose();
        await _pg.DisposeAsync();
    }

    // ─── helpers ───────────────────────────────────────────────────────────────

    private static Itinerary BuildItinerary()
    {
        var seg = Segment
            .Create(
                Led,
                Dme,
                new DateTimeOffset(2026, 7, 15, 9, 20, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 15, 12, 40, 0, TimeSpan.Zero),
                "SU",
                "100",
                CabinClass.Economy
            )
            .Value;
        var slice = Slice.Create(new[] { seg }).Value;
        return Itinerary.Create(new[] { slice }).Value;
    }

    private static PassengerInfo BuildPassenger() =>
        PassengerInfo
            .Create(
                "Ivan",
                "Petrov",
                new DateOnly(1990, 1, 1),
                Gender.Male,
                "ivan@example.com",
                PhoneNumber.Create("+79161234567").Value,
                new DateOnly(2026, 5, 14)
            )
            .Value;

    private async Task<Guid> SeedOfferQuotedStream(
        DateTimeOffset expiresAt,
        QuoteBinding? binding = null
    )
    {
        var streamId = Guid.NewGuid();
        await using var session = _store.LightweightSession();
        var ct = TestContext.Current.CancellationToken;

        session.Events.StartStream<BookingAggregate>(
            streamId,
            new OfferQuoted(
                OfferId: OfferId.New(),
                Itinerary: BuildItinerary(),
                TotalAmount: Money.Create(5420m, Rub).Value,
                ExpiresAt: expiresAt,
                ProviderRef: "off_test_" + Guid.NewGuid(),
                QuotedAt: DateTimeOffset.UtcNow,
                QuoteBinding: binding ?? TestPii.Binding()
            )
        );
        await session.SaveChangesAsync(ct);
        return streamId;
    }

    // ─── fake providers ─────────────────────────────────────────────────────────

    private sealed class SuccessHoldProvider(string orderId, DateTimeOffset heldUntil)
        : IFlightBookingProvider
    {
        public int HoldCalls { get; private set; }
        public ProviderId Id => ProviderId.Duffel;

        public Task<ErrorOr<BookableOffer>> RefreshOfferAsync(
            string providerOfferRef,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<ErrorOr<HeldOrder>> HoldOfferAsync(
            BookableOffer offer,
            QuoteBinding binding,
            EquatableArray<BookingPassenger> passengers,
            CancellationToken ct
        )
        {
            HoldCalls++;
            return Task.FromResult<ErrorOr<HeldOrder>>(new HeldOrder(orderId, heldUntil));
        }

        public Task<ErrorOr<Success>> ValidateConfirmationAsync(
            string providerOrderId,
            Money expectedTotal,
            CancellationToken ct
        ) => Task.FromResult<ErrorOr<Success>>(Result.Success);

        public Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
            string providerOrderId,
            PaymentRef payment,
            Money expectedTotal,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<ErrorOr<Success>> CancelOrderAsync(
            string providerOrderId,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<ErrorOr<OrderStatus>> GetOrderStatusAsync(
            string providerOrderId,
            CancellationToken ct
        ) => throw new NotImplementedException();
    }

    // ─── tests ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Captures the fare conditions handed to the provider on hold, so the test can
    /// assert they match what was set on the offer at quote time.
    /// </summary>
    private sealed class CapturingHoldProvider(string orderId, DateTimeOffset heldUntil)
        : IFlightBookingProvider
    {
        public FareConditions? CapturedFareConditions { get; private set; }
        public PassengerInfo? CapturedPassenger { get; private set; }

        public ProviderId Id => ProviderId.Duffel;

        public Task<ErrorOr<BookableOffer>> RefreshOfferAsync(
            string providerOfferRef,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<ErrorOr<HeldOrder>> HoldOfferAsync(
            BookableOffer offer,
            QuoteBinding binding,
            EquatableArray<BookingPassenger> passengers,
            CancellationToken ct
        )
        {
            CapturedFareConditions = offer.FareConditions;
            CapturedPassenger = passengers.Single().Details.Passenger;
            return Task.FromResult<ErrorOr<HeldOrder>>(new HeldOrder(orderId, heldUntil));
        }

        public Task<ErrorOr<Success>> ValidateConfirmationAsync(
            string providerOrderId,
            Money expectedTotal,
            CancellationToken ct
        ) => Task.FromResult<ErrorOr<Success>>(Result.Success);

        public Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
            string providerOrderId,
            PaymentRef payment,
            Money expectedTotal,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<ErrorOr<Success>> CancelOrderAsync(
            string providerOrderId,
            CancellationToken ct
        ) => throw new NotImplementedException();

        public Task<ErrorOr<OrderStatus>> GetOrderStatusAsync(
            string providerOrderId,
            CancellationToken ct
        ) => throw new NotImplementedException();
    }

    [Fact]
    public async Task Unprotect_failure_does_not_call_provider_or_append_events()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var id = await SeedOfferQuotedStream(now.AddMinutes(20));
        await using var session = _store.LightweightSession();
        var result = await HoldOfferHandler.Handle(
            TestPii.HoldCommand(id, Guid.NewGuid(), BuildPassenger(), TestPii.Binding()),
            Array.Empty<IFlightBookingProvider>(),
            session,
            new RecordingMartenOutbox(),
            NullFlightsMetricsImpl.Instance,
            new FakeTimeProvider(now),
            NullLogger<HoldOfferCommand>.Instance,
            new UnavailableProtector(),
            ct
        );
        result.FirstError.Code.ShouldBe("Flights.PiiPayloadUnavailable");
        (await session.Events.FetchStreamAsync(id, token: ct)).Count.ShouldBe(1);
    }

    private sealed class UnavailableProtector : IBookingPassengerPartyProtector
    {
        public ErrorOr<ProtectedPassengerPartySnapshot> Protect(
            BookingPassengerPartyProtectionContext context,
            EquatableArray<BookingPassenger> passengers
        ) => PiiProtectionErrors.Unavailable;

        public ErrorOr<EquatableArray<BookingPassenger>> Unprotect(
            BookingPassengerPartyProtectionContext context,
            ProtectedPassengerPartySnapshot snapshot
        ) => PiiProtectionErrors.PayloadUnavailable;
    }

    [Fact]
    public async Task Hold_uses_fare_conditions_captured_at_quote()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var streamId = Guid.NewGuid();

        // Seed an OfferQuoted that carries non-default FareConditions.
        var fareConditions = new FareConditions(
            ChangeAllowed: true,
            RefundAllowed: false,
            FareBasisCode: "EOWPRU",
            CabinClassMarketing: "Economy"
        );
        await using (var session = _store.LightweightSession())
        {
            session.Events.StartStream<BookingAggregate>(
                streamId,
                new OfferQuoted(
                    OfferId: OfferId.New(),
                    Itinerary: BuildItinerary(),
                    TotalAmount: Money.Create(5420m, Rub).Value,
                    ExpiresAt: now.AddMinutes(20),
                    ProviderRef: "off_test_" + Guid.NewGuid(),
                    QuotedAt: now,
                    FareConditions: fareConditions,
                    QuoteBinding: TestPii.Binding()
                )
            );
            await session.SaveChangesAsync(ct);
        }

        var captured = new CapturingHoldProvider("ord_" + Guid.NewGuid(), now.AddHours(2));
        var time = new FakeTimeProvider(now);

        await using var holdSession = _store.LightweightSession();
        var result = await HoldOfferHandler.Handle(
            TestPii.HoldCommand(streamId, Guid.NewGuid(), BuildPassenger(), TestPii.Binding()),
            new IFlightBookingProvider[] { captured },
            holdSession,
            new RecordingMartenOutbox(),
            NullFlightsMetricsImpl.Instance,
            time,
            NullLogger<HoldOfferCommand>.Instance,
            TestPii.PartyProtector,
            ct
        );

        result.IsError.ShouldBeFalse();
        captured.CapturedFareConditions.ShouldBe(fareConditions);
        captured.CapturedPassenger.ShouldBe(BuildPassenger());
    }

    [Fact]
    public async Task ValidOffer_AppendsOfferHeld_AggregateIsHeld()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var streamId = await SeedOfferQuotedStream(now.AddMinutes(20));

        var expectedOrderId = "ord_" + Guid.NewGuid();
        var heldUntil = now.AddHours(2);
        var provider = new SuccessHoldProvider(expectedOrderId, heldUntil);
        var time = new FakeTimeProvider(now);

        await using var session = _store.LightweightSession();

        var command = TestPii.HoldCommand(
            streamId,
            Guid.NewGuid(),
            BuildPassenger(),
            TestPii.Binding()
        );
        var result = await HoldOfferHandler.Handle(
            command,
            new IFlightBookingProvider[] { provider },
            session,
            new RecordingMartenOutbox(),
            NullFlightsMetricsImpl.Instance,
            time,
            NullLogger<HoldOfferCommand>.Instance,
            TestPii.PartyProtector,
            ct
        );

        result.IsError.ShouldBeFalse();
        result.Value.AggregateId.ShouldBe(streamId);
        result.Value.ProviderOrderId.ShouldBe(expectedOrderId);
        result.Value.HeldUntil.ShouldBe(heldUntil);

        var agg = await session.Events.AggregateStreamAsync<BookingAggregate>(streamId, token: ct);
        agg.ShouldNotBeNull();
        agg.Status.ShouldBe(BookingStatus.Held);
        agg.ProviderOrderId.ShouldBe(expectedOrderId);
        agg.Passenger.ShouldBeNull();
        agg.ProtectedPassengerParty.ShouldBe(command.ProtectedPassengerParty);
        var events = await session.Events.FetchStreamAsync(streamId, token: ct);
        var held = events.Last().Data.ShouldBeOfType<OfferHeldV3>();
        held.PassengerSnapshot.ShouldBe(command.ProtectedPassengerParty);
        events.Last().EventTypeName.ShouldBe("offer_held_v3");
        await using var sql = session.Connection.CreateCommand();
        sql.CommandText =
            "select data::text from public.mt_events where stream_id = @id and version = 2";
        sql.Parameters.AddWithValue("id", streamId);
        var stored = (string)(await sql.ExecuteScalarAsync(ct))!;
        stored.ShouldNotContain("Ivan");
        stored.ShouldNotContain("ivan@example.com");
        stored.ShouldNotContain("1990-01-01");
    }

    [Fact]
    public async Task ExpiredOffer_ReturnsOfferExpired_StreamUnchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        // Seed with an already-expired ExpiresAt
        var streamId = await SeedOfferQuotedStream(now.AddMinutes(-5));

        var provider = new SuccessHoldProvider("wont_be_used", now.AddHours(2));
        var time = new FakeTimeProvider(now);

        await using var session = _store.LightweightSession();

        var result = await HoldOfferHandler.Handle(
            TestPii.HoldCommand(streamId, Guid.NewGuid(), BuildPassenger(), TestPii.Binding()),
            new IFlightBookingProvider[] { provider },
            session,
            new RecordingMartenOutbox(),
            NullFlightsMetricsImpl.Instance,
            time,
            NullLogger<HoldOfferCommand>.Instance,
            TestPii.PartyProtector,
            ct
        );

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.OfferExpired");

        // Aggregate should still be OfferQuoted (no OfferHeld appended)
        var agg = await session.Events.AggregateStreamAsync<BookingAggregate>(streamId, token: ct);
        agg.ShouldNotBeNull();
        agg.Status.ShouldBe(BookingStatus.OfferQuoted);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(9)]
    public async Task Group_hold_appends_exact_command_cipher_once_with_authoritative_count(
        int count
    )
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var binding = TestPii.Binding(count);
        var id = await SeedOfferQuotedStream(now.AddMinutes(20), binding);
        var cmd = TestPii.HoldCommand(id, Guid.NewGuid(), BuildPassenger(), binding);
        var provider = new SuccessHoldProvider("ord_group", now.AddHours(1));
        await using var session = _store.LightweightSession();
        var result = await HoldOfferHandler.Handle(
            cmd,
            [provider],
            session,
            new RecordingMartenOutbox(),
            NullFlightsMetricsImpl.Instance,
            new FakeTimeProvider(now),
            NullLogger<HoldOfferCommand>.Instance,
            TestPii.PartyProtector,
            ct
        );
        result.IsError.ShouldBeFalse();
        provider.HoldCalls.ShouldBe(1);
        var events = await session.Events.FetchStreamAsync(id, token: ct);
        events.Count.ShouldBe(2);
        var held = events.Last().Data.ShouldBeOfType<OfferHeldV3>();
        held.PassengerSnapshot.ShouldBe(cmd.ProtectedPassengerParty);
        held.PassengerCount.ShouldBe(count);
        held.QuoteRevision.ShouldBe(binding.Revision);
        var agg = await session.Events.AggregateStreamAsync<BookingAggregate>(id, token: ct);
        agg!.PassengerCount.ShouldBe(count);
    }

    [Theory]
    [InlineData("revision", "Flights.QuoteRevisionMismatch")]
    [InlineData("count", "Flights.PassengerCountMismatch")]
    [InlineData("expiry", "Flights.OfferExpired")]
    public async Task Quote_guards_run_before_unprotect_and_supplier_effect(
        string guard,
        string code
    )
    {
        var ct = TestContext.Current.CancellationToken;
        var now = DateTimeOffset.UtcNow;
        var binding = TestPii.Binding(2);
        var id = await SeedOfferQuotedStream(
            guard == "expiry" ? now.AddMinutes(-1) : now.AddMinutes(20),
            binding
        );
        var cmd = new HoldOfferCommand(
            id,
            Guid.NewGuid(),
            guard == "revision" ? Guid.NewGuid() : binding.Revision,
            guard == "count" ? 1 : 2,
            ProtectedPassengerPartySnapshot.Create(1, "opaque-test-cipher").Value
        );
        var provider = new SuccessHoldProvider("not-used", now.AddHours(1));
        await using var session = _store.LightweightSession();
        var result = await HoldOfferHandler.Handle(
            cmd,
            [provider],
            session,
            new RecordingMartenOutbox(),
            NullFlightsMetricsImpl.Instance,
            new FakeTimeProvider(now),
            NullLogger<HoldOfferCommand>.Instance,
            new GuardProtector(),
            ct
        );
        result.FirstError.Code.ShouldBe(code);
        provider.HoldCalls.ShouldBe(0);
        (await session.Events.FetchStreamAsync(id, token: ct)).Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("foreign", "Flights.PassengerSlotsMismatch")]
    [InlineData("minor", "Flights.PassengerAdultRequiredInvalid")]
    public async Task Decrypted_party_membership_and_adult_age_are_checked_before_one_provider_effect(
        string invalid,
        string code
    )
    {
        var ct = TestContext.Current.CancellationToken;
        var now = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var binding = TestPii.Binding(2);
        var id = await SeedOfferQuotedStream(now.AddMinutes(20), binding);
        var owner = Guid.NewGuid();
        var people = TestPii.Passengers(binding, BuildPassenger()).ToArray();
        if (invalid == "foreign")
            people[1] = BookingPassenger
                .Create(BookingPassengerId.Create(Guid.NewGuid()).Value, people[1].Details)
                .Value;
        else
        {
            var minor = PassengerInfo
                .Create(
                    "Young",
                    "Person",
                    new DateOnly(2020, 1, 1),
                    Gender.Male,
                    "young@example.test",
                    PhoneNumber.Create("+79161234567").Value,
                    new DateOnly(2026, 6, 1)
                )
                .Value;
            people[1] = BookingPassenger
                .Create(
                    binding.Slots[1].Id,
                    BookingPassengerDetails.Create(minor, PassengerTitle.Create("mr").Value).Value
                )
                .Value;
        }
        var snapshot = TestPii
            .PartyProtector.Protect(
                new(id, owner, binding.Revision, 2),
                new EquatableArray<BookingPassenger>(people)
            )
            .Value;
        var command = new HoldOfferCommand(id, owner, binding.Revision, 2, snapshot);
        var provider = new SuccessHoldProvider("not-used", now.AddHours(1));
        await using var session = _store.LightweightSession();
        var result = await HoldOfferHandler.Handle(
            command,
            [provider],
            session,
            new RecordingMartenOutbox(),
            NullFlightsMetricsImpl.Instance,
            new FakeTimeProvider(now),
            NullLogger<HoldOfferCommand>.Instance,
            TestPii.PartyProtector,
            ct
        );
        result.FirstError.Code.ShouldBe(code);
        provider.HoldCalls.ShouldBe(0);
        (await session.Events.FetchStreamAsync(id, token: ct)).Count.ShouldBe(1);
    }

    private sealed class GuardProtector : IBookingPassengerPartyProtector
    {
        public ErrorOr<ProtectedPassengerPartySnapshot> Protect(
            BookingPassengerPartyProtectionContext context,
            EquatableArray<BookingPassenger> passengers
        ) => throw new InvalidOperationException("Must not protect in handler.");

        public ErrorOr<EquatableArray<BookingPassenger>> Unprotect(
            BookingPassengerPartyProtectionContext context,
            ProtectedPassengerPartySnapshot snapshot
        ) => throw new InvalidOperationException("Must guard before decrypt.");
    }

    [Fact]
    public async Task NonExistentAggregate_ReturnsOfferNotFound()
    {
        var ct = TestContext.Current.CancellationToken;
        var nonExistentId = Guid.NewGuid();
        var provider = new SuccessHoldProvider("wont_be_used", DateTimeOffset.UtcNow.AddHours(2));
        var time = new FakeTimeProvider();

        await using var session = _store.LightweightSession();

        var result = await HoldOfferHandler.Handle(
            TestPii.HoldCommand(nonExistentId, Guid.NewGuid(), BuildPassenger(), TestPii.Binding()),
            new IFlightBookingProvider[] { provider },
            session,
            new RecordingMartenOutbox(),
            NullFlightsMetricsImpl.Instance,
            time,
            NullLogger<HoldOfferCommand>.Instance,
            TestPii.PartyProtector,
            ct
        );

        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.OfferNotFound");
    }
}
