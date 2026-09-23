using System.Text.Json;
using Shouldly;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;

namespace Travel.Modules.Flights.Tests.Integration.Marten;

[Trait("Category", "Integration")]
public sealed class BookingEventCompatibilityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Legacy_OfferHeld_payload_deserializes_with_unknown_owner()
    {
        var legacy = new LegacyOfferHeldPayload(
            "ord_legacy",
            BuildPassenger(),
            new DateTimeOffset(2026, 9, 16, 14, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero)
        );

        var json = JsonSerializer.Serialize(legacy, JsonOptions);
        var current = JsonSerializer.Deserialize<OfferHeld>(json, JsonOptions);

        current.ShouldNotBeNull();
        current.OrderId.ShouldBe("ord_legacy");
        current.OwnerUserId.ShouldBeNull();
    }

    [Fact]
    public void Legacy_reader_ignores_the_new_OfferHeld_owner_field()
    {
        var owner = Guid.NewGuid();
        var current = new OfferHeld(
            "ord_current",
            BuildPassenger(),
            new DateTimeOffset(2026, 9, 16, 14, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero),
            owner
        );

        var json = JsonSerializer.Serialize(current, JsonOptions);
        var legacy = JsonSerializer.Deserialize<LegacyOfferHeldPayload>(json, JsonOptions);

        legacy.ShouldNotBeNull();
        legacy.OrderId.ShouldBe("ord_current");
        legacy.Passenger.ShouldBe(current.Passenger);
    }

    [Fact]
    public void Legacy_OfferReQuoted_payload_deserializes_without_a_refreshed_snapshot()
    {
        var usd = CurrencyCode.Create("USD").Value;
        var legacy = new LegacyOfferReQuotedPayload(
            OfferId.New(),
            Money.Create(100m, usd).Value,
            Money.Create(120m, usd).Value,
            new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero)
        );

        var json = JsonSerializer.Serialize(legacy, JsonOptions);
        var current = JsonSerializer.Deserialize<OfferReQuoted>(json, JsonOptions);

        current.ShouldNotBeNull();
        current.NewAmount.Amount.ShouldBe(120m);
        current.RefreshedOffer.ShouldBeNull();
    }

    [Fact]
    public void Legacy_reader_ignores_the_new_OfferReQuoted_snapshot()
    {
        var usd = CurrencyCode.Create("USD").Value;
        var at = new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
        var refreshed = new BookableOffer(
            OfferId.New(),
            BuildItinerary(at),
            Money.Create(120m, usd).Value,
            new ProviderId("duffel"),
            at,
            at.AddMinutes(30),
            new FareConditions(false, false, null, null),
            "off_test"
        );
        var current = new OfferReQuoted(
            refreshed.Id,
            Money.Create(100m, usd).Value,
            refreshed.TotalAmount,
            at,
            refreshed
        );

        var json = JsonSerializer.Serialize(current, JsonOptions);
        var legacy = JsonSerializer.Deserialize<LegacyOfferReQuotedPayload>(json, JsonOptions);

        legacy.ShouldNotBeNull();
        legacy.NewAmount.Amount.ShouldBe(120m);
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
                new DateOnly(2026, 9, 16)
            )
            .Value;

    private static Itinerary BuildItinerary(DateTimeOffset at)
    {
        var segment = Segment
            .Create(
                IataCode.Create("LED").Value,
                IataCode.Create("DME").Value,
                at.AddHours(1),
                at.AddHours(2),
                "SU",
                "100",
                CabinClass.Economy
            )
            .Value;
        return Itinerary.Create([Slice.Create([segment]).Value]).Value;
    }

    private sealed record LegacyOfferHeldPayload(
        string OrderId,
        PassengerInfo Passenger,
        DateTimeOffset HeldUntil,
        DateTimeOffset HeldAt
    );

    private sealed record LegacyOfferReQuotedPayload(
        OfferId OfferId,
        Money OldAmount,
        Money NewAmount,
        DateTimeOffset ReQuotedAt
    );
}
