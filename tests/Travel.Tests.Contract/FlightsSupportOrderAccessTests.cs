using System.Reflection;
using ErrorOr;
using Marten;
using Shouldly;
using Travel.Modules.Flights.Application.Support;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Xunit;

namespace Travel.Tests.Contract;

[Trait("Category", "Contract")]
public sealed class FlightsSupportOrderAccessTests
{
    private static readonly DateTimeOffset Now = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(BookingStatus.Held)]
    [InlineData(BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Ticketed)]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Refunded)]
    public async Task Owned_source_history_returns_authoritative_version_without_writes(
        BookingStatus status
    )
    {
        var owner = Guid.NewGuid();
        var order = Guid.NewGuid();
        var booking = Held(order, owner, status);
        var before = (
            booking.Id,
            booking.Version,
            booking.Status,
            booking.OwnerUserId,
            booking.ProtectedPassenger
        );
        var probe = new Probe(booking, 41);
        var service = new FlightsSupportOrderAccess(probe.Session, new Clock(Now.AddTicks(7)));
        var result = await service.VerifyOwnershipAsync(
            owner,
            order,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        result.Value.OrderId.ShouldBe(order);
        result.Value.OwnerId.ShouldBe(owner);
        result.Value.StreamVersion.ShouldBe(41);
        result.Value.VerifiedAt.ShouldBe(Now);
        (
            booking.Id,
            booking.Version,
            booking.Status,
            booking.OwnerUserId,
            booking.ProtectedPassenger
        ).ShouldBe(before);
        probe.Fetches.ShouldBe(1);
        probe.Order.ShouldBe(order);
        probe.AggregateType.ShouldBe(typeof(BookingAggregate));
        probe.Token.ShouldBe(TestContext.Current.CancellationToken);
        probe.Forbidden.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("wrong-id")]
    [InlineData("ownerless")]
    [InlineData("quote")]
    [InlineData("inconsistent")]
    public async Task Unowned_source_is_concealed_before_version_check(string scenario)
    {
        var owner = Guid.NewGuid();
        var order = Guid.NewGuid();
        BookingAggregate? booking = Held(order, owner, BookingStatus.Held);
        switch (scenario)
        {
            case "missing":
                booking = null;
                break;
            case "foreign":
                booking = Held(order, Guid.NewGuid(), BookingStatus.Held);
                break;
            case "wrong-id":
                booking = Held(Guid.NewGuid(), owner, BookingStatus.Held);
                break;
            case "ownerless":
                booking.Apply(new OfferHeld("ord_fixture", null!, Now.AddHours(1), Now, null));
                break;
            case "quote":
                booking = new BookingAggregate();
                Identity(booking, order);
                booking.Apply(
                    new OfferQuoted(
                        OfferId.New(),
                        null!,
                        Money.Create(0m, CurrencyCode.Create("USD").Value).Value,
                        Now.AddHours(1),
                        "off_fixture",
                        Now
                    )
                );
                break;
            case "inconsistent":
                booking.Apply(
                    new CancellationPreparationStarted(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        "ord_fixture",
                        new string('a', 64),
                        new string('b', 64),
                        Guid.NewGuid(),
                        Now
                    )
                );
                break;
        }
        var probe = new Probe(booking, 0);
        var service = new FlightsSupportOrderAccess(probe.Session, new Clock(Now));
        var result = await service.VerifyOwnershipAsync(
            owner,
            order,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeTrue();
        result.FirstError.Type.ShouldBe(ErrorType.NotFound);
        result.FirstError.Code.ShouldBe("Flights.OrderAccessNotFound");
        probe.Forbidden.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task Missing_or_invalid_source_version_is_unavailable(long? version)
    {
        var owner = Guid.NewGuid();
        var order = Guid.NewGuid();
        var probe = new Probe(Held(order, owner, BookingStatus.Held), version);
        var result = await new FlightsSupportOrderAccess(
            probe.Session,
            new Clock(Now)
        ).VerifyOwnershipAsync(owner, order, TestContext.Current.CancellationToken);
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe("Flights.OrderAccessUnavailable");
        probe.Forbidden.ShouldBeEmpty();
    }

    [Fact]
    public async Task Source_failure_has_fixed_diagnostics_and_requested_cancellation_propagates()
    {
        var owner = Guid.NewGuid();
        var order = Guid.NewGuid();
        var probe = new Probe(Held(order, owner, BookingStatus.Held), 41)
        {
            Failure = new InvalidOperationException("Private synthetic diagnostic"),
        };
        var result = await new FlightsSupportOrderAccess(
            probe.Session,
            new Clock(Now)
        ).VerifyOwnershipAsync(owner, order, TestContext.Current.CancellationToken);
        result.FirstError.Code.ShouldBe("Flights.OrderAccessUnavailable");
        result.FirstError.Description.ShouldBe("Order ownership is unavailable.");
        probe.Forbidden.ShouldBeEmpty();
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        await cancelled.CancelAsync();
        var error = new OperationCanceledException(cancelled.Token);
        probe.Failure = error;
        var observed = await Should.ThrowAsync<OperationCanceledException>(() =>
            new FlightsSupportOrderAccess(probe.Session, new Clock(Now)).VerifyOwnershipAsync(
                owner,
                order,
                cancelled.Token
            )
        );
        observed.CancellationToken.ShouldBe(cancelled.Token);
        probe.Forbidden.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Empty_identity_never_reads_source(bool emptyOwner)
    {
        var owner = Guid.NewGuid();
        var order = Guid.NewGuid();
        var probe = new Probe(Held(order, owner, BookingStatus.Held), 41);
        var result = await new FlightsSupportOrderAccess(
            probe.Session,
            new Clock(Now)
        ).VerifyOwnershipAsync(
            emptyOwner ? Guid.Empty : owner,
            emptyOwner ? order : Guid.Empty,
            TestContext.Current.CancellationToken
        );
        result.FirstError.Code.ShouldBe("Flights.OrderAccessNotFound");
        probe.Fetches.ShouldBe(0);
        probe.Forbidden.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(BookingStatus.Held)]
    [InlineData(BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Ticketed)]
    public async Task Triage_reads_next_future_segment_from_owned_source_without_effects(
        BookingStatus status
    )
    {
        var owner = Guid.NewGuid();
        var order = Guid.NewGuid();
        var booking = Held(order, owner, status);
        SetSchedule(booking);
        var probe = new Probe(booking, 41);
        var result = await new FlightsSupportTriageContext(probe.Session, new Clock(Now)).ReadAsync(
            owner,
            order,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        result.Value.Journey.ShouldBe(
            Travel.IntegrationContracts.Flights.FlightSupportJourneyState.Active
        );
        result.Value.NextScheduledDepartureAt.ShouldBe(Now.AddHours(2));
        result.Value.OwnerId.ShouldBe(owner);
        result.Value.OrderId.ShouldBe(order);
        result.Value.StreamVersion.ShouldBe(41);
        probe.Fetches.ShouldBe(1);
        probe.Forbidden.ShouldBeEmpty();
        booking.Status.ShouldBe(status);
    }

    [Theory]
    [InlineData("missing-schedule")]
    [InlineData("expired-hold")]
    [InlineData("supplier-report")]
    [InlineData("cancellation-pending")]
    [InlineData("cancellation-unknown")]
    public async Task Triage_does_not_turn_uncertain_source_lifecycle_into_active(string scenario)
    {
        var owner = Guid.NewGuid();
        var order = Guid.NewGuid();
        var booking = Held(order, owner, BookingStatus.Held);
        SetSchedule(booking);
        if (scenario == "missing-schedule")
            typeof(BookingAggregate)
                .GetProperty(nameof(BookingAggregate.Itinerary))!
                .SetValue(booking, null);
        if (scenario == "expired-hold")
            typeof(BookingAggregate)
                .GetProperty(nameof(BookingAggregate.ExpiresAt))!
                .SetValue(booking, Now);
        if (scenario == "supplier-report")
            booking.Apply(
                new SupplierCancellationReported(Guid.NewGuid(), owner, "ord_fixture", Now)
            );
        if (scenario.StartsWith("cancellation-", StringComparison.Ordinal))
        {
            var op = Guid.NewGuid();
            booking.Apply(
                new CancellationPreparationStarted(
                    op,
                    owner,
                    "ord_fixture",
                    new string('a', 64),
                    new string('b', 64),
                    Guid.NewGuid(),
                    Now
                )
            );
            if (scenario == "cancellation-unknown")
            {
                booking.Apply(
                    new CancellationPreparationDispatched(
                        op,
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        null!,
                        Now
                    )
                );
                booking.Apply(
                    new CancellationOutcomeBecameUnknown(
                        op,
                        Travel
                            .Modules
                            .Flights
                            .Core
                            .Cancellation
                            .CancellationUnknownStage
                            .Preparation,
                        Travel.Modules.Flights.Core.Providers.Dtos.CancellationReason.Pending,
                        Now
                    )
                );
            }
        }
        var probe = new Probe(booking, 41);
        var result = await new FlightsSupportTriageContext(probe.Session, new Clock(Now)).ReadAsync(
            owner,
            order,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBeFalse();
        result.Value.Journey.ShouldBe(
            Travel.IntegrationContracts.Flights.FlightSupportJourneyState.Unknown
        );
        result.Value.NextScheduledDepartureAt.ShouldBeNull();
        probe.Forbidden.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Refunded)]
    public async Task Triage_closed_is_only_journey_metadata(BookingStatus status)
    {
        var owner = Guid.NewGuid();
        var order = Guid.NewGuid();
        var probe = new Probe(Held(order, owner, status), 41);
        var result = await new FlightsSupportTriageContext(probe.Session, new Clock(Now)).ReadAsync(
            owner,
            order,
            TestContext.Current.CancellationToken
        );
        result.Value.Journey.ShouldBe(
            Travel.IntegrationContracts.Flights.FlightSupportJourneyState.Closed
        );
        result.Value.NextScheduledDepartureAt.ShouldBeNull();
        probe.Forbidden.ShouldBeEmpty();
    }

    [Fact]
    public async Task Triage_foreign_owner_and_invalid_version_remain_concealed_or_unavailable()
    {
        var owner = Guid.NewGuid();
        var order = Guid.NewGuid();
        var booking = Held(order, owner, BookingStatus.Held);
        SetSchedule(booking);
        var foreign = await new FlightsSupportTriageContext(
            new Probe(booking, 41).Session,
            new Clock(Now)
        ).ReadAsync(Guid.NewGuid(), order, TestContext.Current.CancellationToken);
        foreign.FirstError.Code.ShouldBe("Flights.OrderAccessNotFound");
        var invalid = await new FlightsSupportTriageContext(
            new Probe(booking, 0).Session,
            new Clock(Now)
        ).ReadAsync(owner, order, TestContext.Current.CancellationToken);
        invalid.FirstError.Code.ShouldBe("Flights.OrderAccessUnavailable");
    }

    private static void SetSchedule(BookingAggregate booking)
    {
        var airports = new[] { "SVO", "LED", "DME", "JFK" }
            .Select(v => IataCode.Create(v).Value)
            .ToArray();
        var first = Segment
            .Create(
                airports[0],
                airports[1],
                Now.AddHours(-4),
                Now.AddHours(-3),
                "SU",
                "1",
                CabinClass.Economy
            )
            .Value;
        var next = Segment
            .Create(
                airports[1],
                airports[2],
                Now.AddHours(2),
                Now.AddHours(3),
                "SU",
                "2",
                CabinClass.Economy
            )
            .Value;
        var later = Segment
            .Create(
                airports[3],
                airports[0],
                Now.AddHours(12),
                Now.AddHours(14),
                "SU",
                "3",
                CabinClass.Economy
            )
            .Value;
        var itinerary = Itinerary
            .Create([Slice.Create([first, next]).Value, Slice.Create([later]).Value])
            .Value;
        typeof(BookingAggregate)
            .GetProperty(nameof(BookingAggregate.Itinerary))!
            .SetValue(booking, itinerary);
    }

    private static BookingAggregate Held(Guid order, Guid owner, BookingStatus status)
    {
        var booking = new BookingAggregate();
        Identity(booking, order);
        booking.Apply(
            new OfferHeldV2(
                "ord_fixture",
                ProtectedPassengerSnapshot.Create(1, "opaque").Value,
                Now.AddHours(1),
                Now,
                owner
            )
        );
        if (status is BookingStatus.Confirmed or BookingStatus.Ticketed)
            booking.Apply(new OrderConfirmed("ord_fixture", PaymentRef.New(), Now));
        if (status == BookingStatus.Ticketed)
            booking.Apply(new OrderTicketed(new(["tkt_fixture"]), Now));
        if (status is BookingStatus.Cancelled or BookingStatus.Refunded)
            booking.Apply(new OrderCancelled(CancelReason.User, Now));
        if (status == BookingStatus.Refunded)
            booking.Apply(
                new OrderRefunded(
                    RefundRef.New(),
                    Money.Create(0m, CurrencyCode.Create("USD").Value).Value,
                    RefundInitiator.Airline,
                    Now
                )
            );
        return booking;
    }

    private static void Identity(BookingAggregate booking, Guid order)
    {
        typeof(BookingAggregate).GetProperty(nameof(BookingAggregate.Id))!.SetValue(booking, order);
        typeof(BookingAggregate)
            .GetProperty(nameof(BookingAggregate.Version))!
            .SetValue(booking, 99);
    }

    private sealed class Clock(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class Probe
    {
        public IDocumentSession Session { get; }
        public List<string> Forbidden { get; } = [];
        public Exception? Failure { get; set; }
        public int Fetches { get; private set; }
        public Guid Order { get; private set; }
        public Type? AggregateType { get; private set; }
        public CancellationToken Token { get; private set; }

        public Probe(BookingAggregate? booking, long? version)
        {
            Session = (IDocumentSession)
                Proxy.Make(
                    typeof(IDocumentSession),
                    (method, _) =>
                        method.Name == "get_Events"
                            ? Proxy.Make(
                                method.ReturnType,
                                (member, args) =>
                                {
                                    if (member.Name != "FetchForWriting")
                                        return Reject("events." + member.Name);
                                    Fetches++;
                                    Order = (Guid)args![0]!;
                                    AggregateType = member.GetGenericArguments().Single();
                                    Token = (CancellationToken)args[^1]!;
                                    if (Failure is { } failure)
                                        throw failure;
                                    var type = member.ReturnType.GenericTypeArguments[0];
                                    var stream = Proxy.Make(
                                        type,
                                        (get, _) =>
                                            get.Name switch
                                            {
                                                "get_Aggregate" => booking,
                                                "get_CurrentVersion" => version,
                                                _ => Reject("stream." + get.Name),
                                            }
                                    );
                                    return typeof(Task)
                                        .GetMethod(nameof(Task.FromResult))!
                                        .MakeGenericMethod(type)
                                        .Invoke(null, [stream]);
                                }
                            )
                            : Reject("session." + method.Name)
                );
        }

        private object? Reject(string name)
        {
            Forbidden.Add(name);
            throw new InvalidOperationException("Unexpected source operation");
        }
    }

    public class Proxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;

        public static object Make(Type type, Func<MethodInfo, object?[]?, object?> handler)
        {
            var proxy = (Proxy)DispatchProxy.Create(type, typeof(Proxy));
            proxy.Handler = handler;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            Handler(method!, args);
    }
}
