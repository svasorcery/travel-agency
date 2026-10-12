using System.Reflection;
using ErrorOr;
using Marten;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.FareRules;
using Travel.Modules.Flights.Application.Handlers.Booking;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using Wolverine.Marten;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.FareRules;

public sealed class FareQuoteCommitTests
{
    public class Proxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Call = null!;

        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            Call(method!, args);

        public static object Make(Type type, Func<MethodInfo, object?[]?, object?> call)
        {
            var proxy = (Proxy)DispatchProxy.Create(type, typeof(Proxy));
            proxy.Call = call;
            return proxy;
        }
    }

    private sealed class Store(BookingAggregate? booking = null)
    {
        public readonly List<object> Events = [];
        public bool Saved;
        public bool Fail;
        public IDocumentSession Session =>
            (IDocumentSession)
                Proxy.Make(
                    typeof(IDocumentSession),
                    (method, _) =>
                        method.Name switch
                        {
                            "get_Events" => Proxy.Make(
                                method.ReturnType,
                                (member, args) =>
                                {
                                    if (member.Name == "StartStream")
                                    {
                                        Events.AddRange((object[])args![1]!);
                                        return null;
                                    }
                                    if (member.Name != "FetchForWriting")
                                        throw new InvalidOperationException(member.Name);
                                    var streamType = member.ReturnType.GenericTypeArguments[0];
                                    var stream = Proxy.Make(
                                        streamType,
                                        (get, values) =>
                                            get.Name switch
                                            {
                                                "get_Aggregate" => booking,
                                                "get_CurrentVersion" => booking is null
                                                    ? null
                                                    : (object)2L,
                                                "AppendOne" => Append(values![0]!),
                                                _ => throw new InvalidOperationException(get.Name),
                                            }
                                    );
                                    return typeof(Task)
                                        .GetMethod(nameof(Task.FromResult))!
                                        .MakeGenericMethod(streamType)
                                        .Invoke(null, [stream]);
                                }
                            ),
                            "SaveChangesAsync" => Save(),
                            _ => throw new InvalidOperationException(method.Name),
                        }
                );

        private object? Append(object value)
        {
            Events.Add(value);
            return null;
        }

        private Task Save()
        {
            if (Fail)
                throw new InvalidOperationException("Fictional failed commit");
            Saved = true;
            return Task.CompletedTask;
        }
    }

    private sealed class Organizer(Store store) : IFareSummaryOrganizer
    {
        public int Calls;

        public Task<FareSummaryResult> OrganizeAsync(
            QuotedFareSnapshot snapshot,
            string? mode,
            bool authenticated,
            bool services,
            DateTimeOffset started,
            Func<CancellationToken, Task<bool>> current,
            CancellationToken ct
        )
        {
            store.Saved.ShouldBeTrue();
            store
                .Events.OfType<OfferQuoted>()
                .Select(e => e.FareSnapshot)
                .Concat(store.Events.OfType<OfferReQuoted>().Select(e => e.FareSnapshot))
                .Single()
                .ShouldBe(snapshot);
            Calls++;
            return Task.FromResult(
                FareSummaryFormatter.Current(
                    snapshot,
                    DuffelFareMappingTests.Now,
                    services,
                    false,
                    "Disabled"
                )
            );
        }
    }

    private static IMartenOutbox Outbox =>
        (IMartenOutbox)
            Proxy.Make(
                typeof(IMartenOutbox),
                (method, _) =>
                    method.ReturnType == typeof(ValueTask) ? ValueTask.CompletedTask
                    : method.ReturnType == typeof(Task) ? Task.CompletedTask
                    : null
            );
    private static IFlightsMetrics Metrics =>
        (IFlightsMetrics)Proxy.Make(typeof(IFlightsMetrics), (_, _) => null);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Both_quote_writers_freeze_before_commit_and_only_then_organize(bool requote)
    {
        var clock = new FakeTimeProvider(DuffelFareMappingTests.Now);
        var offer = DuffelOfferMapper.Map(DuffelFareMappingTests.Offer(), clock).Value;
        BookingAggregate? booking = null;
        if (requote)
        {
            booking = new BookingAggregate();
            var snap = FareSummaryOrganizerTests.Snapshot();
            booking.Apply(
                new OfferQuoted(
                    offer.Id,
                    offer.Itinerary,
                    offer.TotalAmount,
                    offer.ExpiresAt,
                    offer.ProviderOfferRef,
                    DuffelFareMappingTests.Now,
                    offer.FareConditions
                )
            );
        }
        var store = new Store(booking);
        var organizer = new Organizer(store);
        var provider = (IFlightBookingProvider)
            Proxy.Make(
                typeof(IFlightBookingProvider),
                (method, _) =>
                    method.Name switch
                    {
                        "get_Id" => offer.Provider,
                        "RefreshOfferAsync" => Task.FromResult<
                            ErrorOr<Travel.Modules.Flights.Core.ValueObjects.Offer.BookableOffer>
                        >(offer),
                        _ => throw new InvalidOperationException(method.Name),
                    }
            );
        var result = await QuoteOfferHandler.Handle(
            new(offer.ProviderOfferRef, offer.Provider, requote ? Guid.NewGuid() : null),
            [provider],
            store.Session,
            Outbox,
            Metrics,
            clock,
            NullLogger<QuoteOfferCommand>.Instance,
            TestContext.Current.CancellationToken,
            fareSummaryOrganizer: organizer
        );
        result.IsError.ShouldBeFalse();
        organizer.Calls.ShouldBe(1);
        store.Events.Count.ShouldBe(2);
        result.Value.FareSummary!.Fingerprint.ShouldNotBeNull();
        var snapshot = requote
            ? store.Events.OfType<OfferReQuoted>().Single().FareSnapshot
            : store.Events.OfType<OfferQuoted>().Single().FareSnapshot;
        snapshot!.QuoteRevision.ShouldBe(result.Value.Binding.Revision);
        snapshot.Evidence.ObservedAt.ShouldBe(offer.FareRuleEvidence!.ObservedAt);
    }

    [Fact]
    public async Task Failed_quote_commit_has_zero_ai_and_no_saved_source()
    {
        var clock = new FakeTimeProvider(DuffelFareMappingTests.Now);
        var offer = DuffelOfferMapper.Map(DuffelFareMappingTests.Offer(), clock).Value;
        var store = new Store() { Fail = true };
        var organizer = new Organizer(store);
        var provider = (IFlightBookingProvider)
            Proxy.Make(
                typeof(IFlightBookingProvider),
                (method, _) =>
                    method.Name == "get_Id"
                        ? offer.Provider
                        : Task.FromResult<
                            ErrorOr<Travel.Modules.Flights.Core.ValueObjects.Offer.BookableOffer>
                        >(offer)
            );
        await Should.ThrowAsync<InvalidOperationException>(() =>
            QuoteOfferHandler.Handle(
                new(offer.ProviderOfferRef, offer.Provider),
                [provider],
                store.Session,
                Outbox,
                Metrics,
                clock,
                NullLogger<QuoteOfferCommand>.Instance,
                TestContext.Current.CancellationToken,
                fareSummaryOrganizer: organizer
            )
        );
        organizer.Calls.ShouldBe(0);
        store.Saved.ShouldBeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Existing_owner_guard_precedes_historical_summary(bool owner)
    {
        var user = Guid.NewGuid();
        var snapshot = FareSummaryOrganizerTests.Snapshot();
        var b = new BookingAggregate();
        typeof(BookingAggregate)
            .GetProperty(nameof(BookingAggregate.Id))!
            .SetValue(b, snapshot.AggregateId);
        typeof(BookingAggregate)
            .GetProperty(nameof(BookingAggregate.OwnerUserId))!
            .SetValue(b, user);
        var clock = new FakeTimeProvider(FareSummaryOrganizerTests.Now);
        // Deserialize/apply persisted evidence only; this reader has no provider, bus, key or outbox dependency.
        var offer = DuffelOfferMapper
            .Map(DuffelFareMappingTests.Offer(), new FakeTimeProvider(DuffelFareMappingTests.Now))
            .Value;
        var binding = Travel
            .Modules.Flights.Application.Booking.QuoteBindingFactory.Create(offer.Party, 1, null)
            .Value;
        snapshot = QuotedFareSnapshot.Create(snapshot.AggregateId, offer, binding).Value;
        b.Apply(
            new OfferQuoted(
                offer.Id,
                offer.Itinerary,
                offer.TotalAmount,
                offer.ExpiresAt,
                offer.ProviderOfferRef,
                DuffelFareMappingTests.Now,
                offer.FareConditions,
                binding,
                snapshot
            )
        );
        var result = await GetBookingCreationHandler.Handle(
            new(snapshot.AggregateId, owner ? user : Guid.NewGuid()),
            new Store(b).Session,
            clock,
            TestContext.Current.CancellationToken
        );
        result.IsError.ShouldBe(!owner);
        if (owner)
        {
            result.Value.QuotedFareSummary!.State.ShouldBe("Stale");
            result.Value.QuotedFareSummary.Provenance.ShouldBe("Historical");
            result.Value.QuotedFareSummary.OfferExpiresAt.ShouldBe(offer.ExpiresAt);
        }
    }
}
