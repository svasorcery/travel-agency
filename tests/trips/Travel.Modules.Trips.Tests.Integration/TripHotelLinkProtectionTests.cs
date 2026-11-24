using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Trips.Application.FlightLinks;
using Travel.Modules.Trips.Application.HotelLinks;
using Travel.Modules.Trips.Core;
using Travel.Modules.Trips.Infrastructure.Privacy;
using Xunit;

namespace Travel.Modules.Trips.Tests.Integration;

public sealed class TripHotelLinkProtectionTests
{
    private readonly TripHotelLinkBinding binding = new(
        TripsCodecProtectionTests.Owner,
        Guid.NewGuid(),
        1
    );

    [Fact]
    public void Protects_one_id_or_an_empty_tombstone_at_the_same_size()
    {
        using var ring = new TripsTestRing();
        var protector = new TripHotelLinkProtector(ring.Provider);
        var reservation = Guid.NewGuid();
        var linked = protector.Protect(reservation, binding).Value;
        var empty = protector.Protect(null, binding).Value;

        linked.Length.ShouldBe(empty.Length);
        linked.ShouldNotContain(reservation.ToString());
        protector.Unprotect(linked, binding).Value.ShouldBe(reservation);
        protector.Unprotect(empty, binding).Value.ShouldBeNull();
        protector.Protect(Guid.Empty, binding).FirstError.ShouldBe(TripErrors.Unavailable);
        new TripHotelLinkRecord(binding, linked).ToString().ShouldBe(nameof(TripHotelLinkRecord));
    }

    [Fact]
    public void Rejects_copied_damaged_oversized_and_cross_purpose_envelopes()
    {
        using var ring = new TripsTestRing();
        var protector = new TripHotelLinkProtector(ring.Provider);
        var envelope = protector.Protect(Guid.NewGuid(), binding).Value;
        var copiedBindings = new[]
        {
            binding with
            {
                Revision = 2,
            },
            binding with
            {
                TripId = Guid.NewGuid(),
            },
            binding with
            {
                Owner = OwnerIdentity.Create(binding.Owner.Issuer, Guid.NewGuid()).Value,
            },
            binding with
            {
                Owner = OwnerIdentity
                    .Create("https://other.fictional.test", binding.Owner.Subject)
                    .Value,
            },
        };
        foreach (var copiedBinding in copiedBindings)
            protector
                .Unprotect(envelope, copiedBinding)
                .FirstError.ShouldBe(TripErrors.Unavailable);

        var damaged = Convert.FromBase64String(envelope);
        damaged[^1] ^= 1;
        protector
            .Unprotect(Convert.ToBase64String(damaged), binding)
            .FirstError.ShouldBe(TripErrors.Unavailable);
        protector
            .Unprotect(new string('x', 8193), binding)
            .FirstError.ShouldBe(TripErrors.Unavailable);
        protector.Unprotect(envelope + " ", binding).FirstError.ShouldBe(TripErrors.Unavailable);

        var flightProtector = new TripFlightLinkProtector(ring.Provider);
        var flightBinding = new TripFlightLinkBinding(
            binding.Owner,
            binding.TripId,
            binding.Revision
        );
        var flight = flightProtector.Protect("fictional flight", flightBinding).Value;
        protector.Unprotect(flight, binding).FirstError.ShouldBe(TripErrors.Unavailable);
        flightProtector
            .Unprotect(envelope, flightBinding)
            .FirstError.ShouldBe(TripErrors.Unavailable);
    }

    [Fact]
    public void Missing_keys_fail_closed()
    {
        using var missing = new TripsProtectionProvider(
            Options.Create(new TripsProtectionOptions()),
            TimeProvider.System
        );
        var protector = new TripHotelLinkProtector(missing);

        protector.IsReady.ShouldBeFalse();
        protector.Protect(null, binding).FirstError.ShouldBe(TripErrors.Unavailable);
        protector.Unprotect("AAAA", binding).FirstError.ShouldBe(TripErrors.Unavailable);
    }
}
