using System.Reflection;
using ErrorOr;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Handlers.Booking;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.Privacy;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Travel.Modules.Flights.Tests.Unit.Ancillaries;
using Travel.Shared.Abstractions;
using Wolverine;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Tests.Unit.Booking;

public sealed class BookingCreationCommitTests
{
    [Fact]
    public async Task Failed_admission_commit_has_zero_supplier_effects_and_no_applied_start()
    {
        var (booking, actual, _) = AncillaryConfirmationTests.Held(false);
        // Use the already retained receipt only as fixture data; a fresh quoted stream admits this command.
        booking = QuotedFrom(booking);
        var calls = new Provider(actual);
        var events = new List<object>();
        var messages = new List<(object Message, DeliveryOptions? Options)>();
        var session = Session(
            booking,
            events,
            () => throw new InvalidOperationException("Synthetic commit failure")
        );
        await Should.ThrowAsync<InvalidOperationException>(() =>
            HoldOfferHandler.Handle(
                Command(booking),
                [calls],
                session,
                Outbox(messages),
                Metrics(),
                AncillaryCatalogTests.Clock,
                NullLogger<HoldOfferCommand>.Instance,
                new Party(booking),
                TestContext.Current.CancellationToken,
                new Sender()
            )
        );
        calls.Holds.ShouldBe(0);
        booking.CurrentCreation.ShouldBeNull();
        messages.Select(m => m.Message).OfType<CheckBookingCreation>().Count().ShouldBe(1);
    }

    [Fact]
    public async Task Admitted_operation_survives_browser_abort_and_exact_replay_sends_nothing()
    {
        var (original, actual, now) = AncillaryConfirmationTests.Held(false);
        var booking = QuotedFrom(original);
        var provider = new Provider(actual);
        var events = new List<object>();
        var messages = new List<(object Message, DeliveryOptions? Options)>();
        using var browser = new CancellationTokenSource();
        var commits = 0;
        var session = Session(
            booking,
            events,
            () =>
            {
                if (++commits == 1)
                    browser.Cancel();
            }
        );
        var command = Command(booking);
        var result = await HoldOfferHandler.Handle(
            command,
            [provider],
            session,
            Outbox(messages),
            Metrics(),
            AncillaryCatalogTests.Clock,
            NullLogger<HoldOfferCommand>.Instance,
            new Party(booking),
            browser.Token,
            new Sender()
        );
        result.IsError.ShouldBeFalse();
        commits.ShouldBe(2);
        provider.Holds.ShouldBe(1);
        provider.CancelledAtDispatch.ShouldBeFalse();
        messages
            .Single(m => m.Message is CheckBookingCreation)
            .Options!.ScheduledTime.ShouldBe(now.AddSeconds(150));
        var replay = await HoldOfferHandler.Handle(
            command with
            {
                ProtectedPassengerParty = ProtectedPassengerPartySnapshot
                    .Create(1, "new-opaque-cipher")
                    .Value,
            },
            [provider],
            Session(booking, events, () => throw new InvalidOperationException("No replay commit")),
            Outbox([]),
            Metrics(),
            AncillaryCatalogTests.Clock,
            NullLogger<HoldOfferCommand>.Instance,
            new Party(booking),
            TestContext.Current.CancellationToken,
            new Sender()
        );
        replay.IsError.ShouldBeFalse();
        provider.Holds.ShouldBe(1);
        events.OfType<Core.DomainEvents.BookingCreationStarted>().Count().ShouldBe(1);
    }

    private static BookingAggregate QuotedFrom(BookingAggregate prior)
    {
        var b = new BookingAggregate();
        typeof(BookingAggregate).GetProperty(nameof(BookingAggregate.Id))!.SetValue(b, prior.Id);
        b.Apply(
            new Core.DomainEvents.OfferQuoted(
                prior.OfferId!.Value,
                prior.Itinerary!,
                prior.Purchase!.BaseFare,
                prior.Purchase.ExpiresAt,
                prior.ProviderOfferRef!,
                AncillaryCatalogTests.Clock.GetUtcNow(),
                prior.FareConditions,
                prior.QuoteBinding
            )
        );
        b.Apply(
            new Core.DomainEvents.BookingPurchaseQuoted(
                prior.Purchase,
                AncillaryCatalogTests.Clock.GetUtcNow()
            )
        );
        return b;
    }

    private static HoldOfferCommand Command(BookingAggregate b) =>
        new(
            b.Id,
            b.OwnerUserId!.Value,
            b.QuoteBinding!.Revision,
            b.PassengerCount,
            ProtectedPassengerPartySnapshot.Create(1, "opaque-fixture").Value,
            Guid.NewGuid(),
            new string('a', 64),
            true
        );

    private sealed class Party(BookingAggregate b) : IBookingPassengerPartyProtector
    {
        public ErrorOr<ProtectedPassengerPartySnapshot> Protect(
            BookingPassengerPartyProtectionContext context,
            EquatableArray<BookingPassenger> passengers
        ) => throw new NotSupportedException();

        public ErrorOr<EquatableArray<BookingPassenger>> Unprotect(
            BookingPassengerPartyProtectionContext context,
            ProtectedPassengerPartySnapshot snapshot
        ) =>
            new EquatableArray<BookingPassenger>(
                b.QuoteBinding!.Slots.Select(s =>
                        BookingPassenger
                            .Create(
                                s.Id,
                                BookingPassengerDetails
                                    .CreateRaw(
                                        s.Id,
                                        "mr",
                                        "Fictional",
                                        "Traveler",
                                        new(1990, 1, 1),
                                        "male",
                                        "demo@example.test",
                                        "+441234567890",
                                        new(2030, 1, 1)
                                    )
                                    .Value
                            )
                            .Value
                    )
                    .ToArray()
            );
    }

    private sealed class Sender : IDispatchInstanceIdentity
    {
        public Guid Id { get; } = Guid.NewGuid();
    }

    private sealed class Provider(BookedOrderFacts actual) : IFlightBookingProvider
    {
        public int Holds;
        public bool CancelledAtDispatch;
        public ProviderId Id => ProviderId.Duffel;

        public Task<ErrorOr<BookingCreationObservation>> HoldOfferAsync(
            BookableOffer offer,
            QuoteBinding binding,
            EquatableArray<BookingPassenger> passengers,
            BookingPurchase purchase,
            Guid attemptId,
            CancellationToken ct
        )
        {
            Holds++;
            CancelledAtDispatch = ct.IsCancellationRequested;
            return Task.FromResult<ErrorOr<BookingCreationObservation>>(
                new BookingCreationObservation(
                    BookingCreationOutcome.Matches,
                    actual,
                    actual.ProviderOrderId,
                    true,
                    true,
                    "OrderObserved",
                    AncillaryCatalogTests.Clock.GetUtcNow()
                )
            );
        }

        public Task<ErrorOr<BookableOffer>> RefreshOfferAsync(
            string reference,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<ErrorOr<Success>> ValidateConfirmationAsync(
            string order,
            Money total,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<ErrorOr<ConfirmedOrder>> ConfirmOrderAsync(
            string order,
            PaymentRef payment,
            Money total,
            CancellationToken ct
        ) => throw new NotSupportedException();

        public Task<ErrorOr<Success>> CancelOrderAsync(string order, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<ErrorOr<OrderStatus>> GetOrderStatusAsync(string order, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private static IFlightsMetrics Metrics() =>
        (IFlightsMetrics)Proxy.Make(typeof(IFlightsMetrics), (_, _) => null);

    internal static IMartenOutbox Outbox(
        List<(object Message, DeliveryOptions? Options)> messages
    ) =>
        (IMartenOutbox)
            Proxy.Make(
                typeof(IMartenOutbox),
                (method, args) =>
                {
                    if (method.Name == "PublishAsync")
                        messages.Add(
                            (args![0]!, args.Length > 1 ? args[1] as DeliveryOptions : null)
                        );
                    return method.ReturnType == typeof(ValueTask) ? ValueTask.CompletedTask
                        : method.ReturnType == typeof(Task) ? Task.CompletedTask
                        : null;
                }
            );

    internal static IDocumentSession Session(BookingAggregate b, List<object> events, Action save)
    {
        var store = Proxy.Make(
            typeof(IDocumentStore),
            (method, _) =>
                method.Name == "LightweightSession"
                    ? Session(b, events, save)
                    : throw new InvalidOperationException(method.Name)
        );
        return (IDocumentSession)
            Proxy.Make(
                typeof(IDocumentSession),
                (method, _) =>
                {
                    if (method.Name == "get_DocumentStore")
                        return store;
                    if (method.Name == "DisposeAsync")
                        return ValueTask.CompletedTask;
                    if (method.Name == "Dispose")
                        return null;
                    if (method.Name == "SaveChangesAsync")
                    {
                        save();
                        return Task.CompletedTask;
                    }
                    if (method.Name != "get_Events")
                        throw new InvalidOperationException(method.Name);
                    return Proxy.Make(
                        method.ReturnType,
                        (fetch, _) =>
                        {
                            if (fetch.Name != "FetchForWriting")
                                throw new InvalidOperationException(fetch.Name);
                            var streamType = fetch.ReturnType.GetGenericArguments()[0];
                            var stream = Proxy.Make(
                                streamType,
                                (member, args) =>
                                    member.Name switch
                                    {
                                        "get_Aggregate" => b,
                                        "get_CurrentVersion" => 2L + events.Count,
                                        "AppendOne" => Append(events, args![0]!),
                                        _ => throw new InvalidOperationException(member.Name),
                                    }
                            );
                            return typeof(Task)
                                .GetMethod(nameof(Task.FromResult))!
                                .MakeGenericMethod(streamType)
                                .Invoke(null, [stream]);
                        }
                    );
                }
            );
    }

    private static object? Append(List<object> events, object e)
    {
        events.Add(e);
        return null;
    }

    public class Proxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;

        public static object Make(Type type, Func<MethodInfo, object?[]?, object?> handler)
        {
            var proxy = (Proxy)Create(type, typeof(Proxy));
            proxy.Handler = handler;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            Handler(method!, args);
    }
}
