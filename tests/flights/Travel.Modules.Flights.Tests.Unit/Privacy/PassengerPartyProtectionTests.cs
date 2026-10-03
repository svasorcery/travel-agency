using System.Globalization;
using System.Text;
using System.Text.Json;
using Shouldly;
using Travel.Modules.Flights.Application.Privacy;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Infrastructure.Privacy;
using Travel.Shared.Abstractions;
using Travel.Tests.Fixtures;

namespace Travel.Modules.Flights.Tests.Unit.Privacy;

public sealed class PassengerPartyProtectionTests
{
    [Fact]
    public void Whole_party_roundtrips_across_restart_and_is_bound_to_all_context_fields()
    {
        using var ring = new FlightPiiTestRing();
        var protector = new DataProtectionBookingPassengerPartyProtector(ring.Crypto);
        var context = new BookingPassengerPartyProtectionContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            2
        );
        var party = new EquatableArray<BookingPassenger>([Passenger(), Passenger()]);
        var snapshot = protector.Protect(context, party).Value;
        var json = JsonSerializer.Serialize(snapshot);
        foreach (
            var sentinel in new[]
            {
                "FictionalGiven",
                "Family",
                "fictional@example.test",
                "1980-01-01",
                "12025550123",
            }
        )
            json.ShouldNotContain(sentinel);
        snapshot.ToString().ShouldNotContain(snapshot.Ciphertext);
        foreach (
            var wrong in new[]
            {
                context with
                {
                    AggregateId = Guid.NewGuid(),
                },
                context with
                {
                    OwnerUserId = Guid.NewGuid(),
                },
                context with
                {
                    QuoteRevision = Guid.NewGuid(),
                },
                context with
                {
                    PassengerCount = 1,
                },
            }
        )
            protector.Unprotect(wrong, snapshot).IsError.ShouldBeTrue();
        using var restarted = ring.NewProvider();
        var reader = new DataProtectionBookingPassengerPartyProtector(restarted);
        reader
            .Unprotect(context, JsonSerializer.Deserialize<ProtectedPassengerPartySnapshot>(json)!)
            .Value.ShouldBe(party);
        var bytes = Convert.FromBase64String(snapshot.Ciphertext);
        bytes[^1] ^= 1;
        reader
            .Unprotect(
                context,
                ProtectedPassengerPartySnapshot.Create(1, Convert.ToBase64String(bytes)).Value
            )
            .IsError.ShouldBeTrue();
    }

    [Fact]
    public void Invalid_context_count_duplicate_ids_and_json_shapes_fail_closed()
    {
        using var ring = new FlightPiiTestRing();
        var protector = new DataProtectionBookingPassengerPartyProtector(ring.Crypto);
        var context = new BookingPassengerPartyProtectionContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            1
        );
        var passenger = Passenger();
        protector
            .Protect(context with { OwnerUserId = Guid.Empty }, new([passenger]))
            .IsError.ShouldBeTrue();
        protector
            .Protect(context with { PassengerCount = 10 }, new([passenger]))
            .IsError.ShouldBeTrue();
        protector.Protect(context, new([])).IsError.ShouldBeTrue();
        protector
            .Protect(context with { PassengerCount = 2 }, new([passenger, passenger]))
            .IsError.ShouldBeTrue();
        foreach (
            var json in new[]
            {
                "null",
                "[]",
                "[null]",
                JsonSerializer
                    .Serialize(new[] { passenger })
                    .Replace("male", "raw-sentinel@example.test"),
                JsonSerializer.Serialize(new[] { passenger, passenger }),
            }
        )
        {
            var encrypted = ring
                .Crypto.Protect(
                    Encoding.UTF8.GetBytes(json),
                    "booking-party.v1",
                    context.AggregateId.ToString("N"),
                    context.OwnerUserId.ToString("N"),
                    context.QuoteRevision.ToString("N"),
                    context.PassengerCount.ToString(CultureInfo.InvariantCulture)
                )
                .Value;
            var result = protector.Unprotect(
                context,
                ProtectedPassengerPartySnapshot.Create(1, Convert.ToBase64String(encrypted)).Value
            );
            result.IsError.ShouldBeTrue();
            result.FirstError.Code.ShouldBe("Flights.PiiPayloadUnavailable");
            result.FirstError.Description.ShouldNotContain("raw-sentinel");
        }
    }

    private static BookingPassenger Passenger() =>
        BookingPassenger
            .Create(
                BookingPassengerId.Create(Guid.NewGuid()).Value,
                BookingPassengerDetails
                    .Create(
                        ValueObjects.PassengerPartyTests.Info(),
                        PassengerTitle.Create("mr").Value
                    )
                    .Value
            )
            .Value;
}
