using System.Text.Json;
using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Application.Privacy;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Shared.Abstractions;
using Travel.Trips.LocalStack;
using Xunit;

namespace Travel.Host.Tests.Integration.Trips;

public sealed class FlightLinksSeedTests
{
    [Theory]
    [InlineData(false, "SVO", "LED")]
    [InlineData(true, "LED", "SVO")]
    public void Source_events_replay_one_matched_quote_bound_held_order(
        bool reverse,
        string origin,
        string destination
    )
    {
        var now = FixedE2eTimeProvider.Instant;
        var owner = Guid.NewGuid();
        var id = Guid.NewGuid();
        var protection = new Capture();
        var events = FlightLinksSeed.HeldEvents(
            id,
            owner,
            "ord_fictional",
            reverse,
            now,
            protection
        );
        var b = new BookingAggregate();
        foreach (var e in events)
            typeof(BookingAggregate).GetMethod("Apply", [e.GetType()])!.Invoke(b, [e]);
        events.Length.ShouldBe(6);
        b.HasConsistentMutationOwner.ShouldBeTrue();
        b.OwnerUserId.ShouldBe(owner);
        b.CurrentCreation!.CreationCompleted.ShouldBeTrue();
        b.CurrentCreation.Outcome.ShouldBe(BookingCreationOutcome.Matches);
        b.HasUnresolvedCreation.ShouldBeFalse();
        b.CreationBlocksConfirmation.ShouldBeFalse();
        b.MutationCoordinationEnabled.ShouldBeTrue();
        b.Status.ShouldBe(BookingStatus.Held);
        b.ExpiresAt.ShouldBe(now.AddHours(1));
        b.Itinerary!.Slices.Single().Origin.Value.ShouldBe(origin);
        b.Itinerary.Slices.Single().Destination.Value.ShouldBe(destination);
        protection.Context.ShouldBe(new(id, owner, b.HeldQuoteRevision!.Value, 1));
        protection.Passenger.ShouldBe(b.QuoteBinding!.Slots[0].Id.Value);
        b.ProtectedPassengerParty.ShouldBe(events.OfType<OfferHeldV3>().Single().PassengerSnapshot);
        typeof(BookingAggregate).GetProperty(nameof(b.Id))!.SetValue(b, id);
        bool Append(bool close) =>
            FlightLinksSeed.RequiresAppend(b, 6, owner, id, "ord_fictional", reverse, close);
        Append(false).ShouldBeFalse();
        if (reverse)
            Should.Throw<LocalStackRefusal>(() => Append(true));
        else
        {
            Append(true).ShouldBeTrue();
            b.Apply(new OrderCancelled(CancelReason.Airline, now));
            FlightLinksSeed
                .RequiresAppend(b, 7, owner, id, "ord_fictional", false, true)
                .ShouldBeFalse();
            FlightLinksSeed
                .RequiresAppend(b, 7, owner, id, "ord_fictional", false, false)
                .ShouldBeFalse();
        }
        Should.Throw<LocalStackRefusal>(() =>
            FlightLinksSeed.RequiresAppend(
                b,
                6,
                Guid.NewGuid(),
                id,
                "ord_fictional",
                reverse,
                false
            )
        );
        Should.Throw<LocalStackRefusal>(() =>
            FlightLinksSeed.RequiresAppend(null, null, owner, id, "ord_fictional", reverse, true)
        );
    }

    [Fact]
    public void Fixture_refuses_unready_and_foreign_profiles_before_opening_a_database()
    {
        var state = new LocalStackState(
            1,
            "travel-trips-local-stack",
            new string('a', 32),
            "e2e",
            "/repo",
            "test",
            4206,
            8186,
            "2026-10-24T12:00:00Z",
            FixedE2eTimeProvider.Instant,
            "unix:///var/run/docker.sock",
            "fictional",
            "flight-links"
        );
        FlightLinksSeed
            .ReadyState(
                false,
                state,
                _ => throw new InvalidOperationException("premature mount access")
            )
            .ShouldBeNull();
        Should.Throw<LocalStackRefusal>(() =>
            FlightLinksSeed.ReadyState(true, state, _ => state with { Id = new string('b', 32) })
        );
        Should.Throw<LocalStackRefusal>(() =>
            FlightLinksSeed.ReadyState(true, state, _ => state with { DockerId = "foreign" })
        );
        var city = state with { E2eProfile = "moscow" };
        Should.Throw<LocalStackRefusal>(() => FlightLinksSeed.ReadyState(true, city, _ => city));
        FlightLinksSeed.ReadyState(true, state, _ => state).ShouldBe(state);
    }

    [Theory]
    [InlineData(true, 2L)]
    [InlineData(true, null)]
    [InlineData(false, null)]
    [InlineData(false, 1L)]
    public void Facts_distinguish_retained_virtual_deleted_and_orphaned_collections(
        bool planExists,
        long? revision
    )
    {
        var hash = revision.HasValue ? new string('a', 64) : null;
        var keys = new SortedDictionary<string, string> { ["private-key-file"] = "key-hash" };
        var result = JsonSerializer
            .SerializeToNode(LocalDatabase.FlightLinkFacts(planExists, revision, hash, 13, keys))!
            .AsObject();
        result
            .Select(p => p.Key)
            .Order()
            .ShouldBe([
                "collectionExists",
                "eventCount",
                "linksRevision",
                "payloadSha256",
                "planExists",
                "protectionKeyFingerprint",
            ]);
        result["planExists"]!.GetValue<bool>().ShouldBe(planExists);
        result["collectionExists"]!.GetValue<bool>().ShouldBe(revision.HasValue);
        (result["linksRevision"]?.GetValue<string>()).ShouldBe(revision?.ToString());
        result["eventCount"]!.GetValue<long>().ShouldBe(13);
        (result["payloadSha256"]?.GetValue<string>()).ShouldBe(hash);
        result["protectionKeyFingerprint"]!.GetValue<string>().Length.ShouldBe(64);
        var serialized = result.ToJsonString();
        serialized.ShouldNotContain("private-key-file");
    }

    private sealed class Capture : IBookingPassengerPartyProtector
    {
        public BookingPassengerPartyProtectionContext? Context { get; private set; }
        public Guid Passenger { get; private set; }

        public ErrorOr<ProtectedPassengerPartySnapshot> Protect(
            BookingPassengerPartyProtectionContext context,
            EquatableArray<BookingPassenger> passengers
        )
        {
            Context = context;
            Passenger = passengers.Single().Id.Value;
            return ProtectedPassengerPartySnapshot.Create(1, "opaque-fictional-snapshot");
        }

        public ErrorOr<EquatableArray<BookingPassenger>> Unprotect(
            BookingPassengerPartyProtectionContext context,
            ProtectedPassengerPartySnapshot snapshot
        ) => throw new NotSupportedException();
    }
}
