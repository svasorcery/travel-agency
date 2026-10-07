using Shouldly;
using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Tests.Unit.Ancillaries;

public sealed class AncillaryPurchaseTests
{
    private static readonly Guid Passenger = Guid.Parse("00000000-0000-0000-0000-000000000101");

    private static Money Amount(decimal value, string currency = "GBP") =>
        Money.Create(value, CurrencyCode.Create(currency).Value).Value;

    [Fact]
    public void Multi_segment_bag_line_is_charged_once_and_grand_total_is_exact()
    {
        var bag = Line(
            "bag",
            BookingServiceKind.CheckedBaggage,
            2,
            10m,
            new([new(0, 0), new(0, 1)])
        );
        var seat = Line("seat", BookingServiceKind.Seat, 1, 3.5m, new([new(0, 0)]), "12A");
        var purchase = BookingPurchase.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Amount(50),
            DateTimeOffset.UtcNow.AddMinutes(10),
            [bag, seat]
        );
        purchase.IsError.ShouldBeFalse();
        purchase.Value.Extras.Amount.ShouldBe(23.5m);
        purchase.Value.Total.Amount.ShouldBe(73.5m);
        purchase.Value.Services[0].Quantity.ShouldBe(2);
    }

    [Fact]
    public void Different_passengers_cannot_select_same_physical_seat()
    {
        var first = Line("seat-p1", BookingServiceKind.Seat, 1, 0, new([new(0, 0)]), "12A");
        var second = first with { Reference = "seat-p2", PassengerId = Guid.NewGuid() };
        BookingPurchase
            .Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                Amount(50),
                DateTimeOffset.UtcNow.AddMinutes(10),
                [first, second]
            )
            .IsError.ShouldBeTrue();
    }

    [Fact]
    public void Invalid_line_money_scope_or_quantity_does_not_become_purchase()
    {
        var bag = Line("bag", BookingServiceKind.CheckedBaggage, 1, 10m, new([new(0, 0)]));
        foreach (
            var invalid in new[]
            {
                bag with
                {
                    Quantity = 0,
                },
                bag with
                {
                    PassengerId = Guid.Empty,
                },
                bag with
                {
                    LineTotal = Amount(10, "EUR"),
                },
                bag with
                {
                    LineTotal = Amount(11),
                },
            }
        )
            BookingPurchase
                .Create(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Amount(50),
                    DateTimeOffset.UtcNow.AddMinutes(10),
                    [invalid]
                )
                .IsError.ShouldBeTrue();
    }

    internal static BookingService Line(
        string reference,
        BookingServiceKind kind,
        int quantity,
        decimal unit,
        EquatableArray<BookingSegmentAddress> segments,
        string? seat = null
    ) =>
        new(
            reference,
            kind,
            Passenger,
            segments,
            quantity,
            Amount(unit * quantity),
            Amount(unit),
            seat,
            seat is null ? null : "deck0-economy:" + seat,
            Disclosures: new([])
        );
}
