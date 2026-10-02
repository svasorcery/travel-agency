using System.Text.Json;
using Shouldly;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Tests.Unit.Aggregates;

public sealed class ProtectedBookingReplayTests
{
    [Fact]
    public void Protected_and_legacy_events_replay_without_keys_or_new_command_validation()
    {
        var at = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var owner = Guid.NewGuid();
        var payload = ProtectedPassengerSnapshot.Create(1, "opaque-test-ciphertext").Value;
        var source = new OfferHeldV2("ord_fictional", payload, at.AddMinutes(10), at, owner);
        var restored = JsonSerializer.Deserialize<OfferHeldV2>(JsonSerializer.Serialize(source))!;
        var aggregate = new BookingAggregate();
        aggregate.Apply(restored);
        aggregate.Status.ShouldBe(BookingStatus.Held);
        aggregate.OwnerUserId.ShouldBe(owner);
        aggregate.ExpiresAt.ShouldBe(source.HeldUntil);
        aggregate.BookedAt.ShouldBe(at);
        aggregate.Passenger.ShouldBeNull();
        aggregate.ProtectedPassenger.ShouldBe(payload);
        var legacyPassenger = PassengerInfo
            .Create(
                "Legacy",
                "Fictional",
                new DateOnly(1980, 1, 1),
                Gender.Male,
                "legacy@example.test",
                PhoneNumber.Create("+12025550111").Value,
                new DateOnly(2030, 1, 1)
            )
            .Value;
        aggregate.Apply(new OfferHeld("ord_legacy", legacyPassenger, at.AddMinutes(20), at));
        aggregate.Passenger.ShouldBe(legacyPassenger);
        aggregate.ProtectedPassenger.ShouldBeNull();
        aggregate.OwnerUserId.ShouldBeNull();
    }
}
