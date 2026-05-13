using Microsoft.Extensions.Time.Testing;
using Shouldly;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Exceptions;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;

namespace Travel.Modules.Flights.Tests.Unit.Aggregates;

public sealed class BookingAggregateApplyTests
{
    // ── Apply tests ───────────────────────────────────────────────────────────

    [Fact]
    public void Apply_OfferQuoted_sets_status_and_offer_fields()
    {
        var booking = new BookingAggregate();
        var e = Sample.OfferQuoted();

        booking.Apply(e);

        booking.Status.ShouldBe(BookingStatus.OfferQuoted);
        booking.OfferId.ShouldBe(e.OfferId);
        booking.Itinerary.ShouldBe(e.Itinerary);
        booking.TotalAmount.ShouldBe(e.TotalAmount);
        booking.ExpiresAt.ShouldBe(e.ExpiresAt);
    }

    [Fact]
    public void Apply_OfferReQuoted_updates_total_amount()
    {
        var booking = new BookingAggregate();
        booking.Apply(Sample.OfferQuoted());

        var rub = CurrencyCode.Create("RUB").Value;
        var newAmount = Money.Create(6000m, rub).Value;
        var reQuoted = new OfferReQuoted(
            OfferId.New(),
            booking.TotalAmount!,
            newAmount,
            new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero)
        );

        booking.Apply(reQuoted);

        booking.TotalAmount.ShouldBe(newAmount);
    }

    [Fact]
    public void Apply_OfferHeld_after_OfferQuoted_transitions_to_Held()
    {
        var booking = new BookingAggregate();
        booking.Apply(Sample.OfferQuoted());

        var held = Sample.OfferHeld();
        booking.Apply(held);

        booking.Status.ShouldBe(BookingStatus.Held);
        booking.ProviderOrderId.ShouldBe(held.OrderId);
        booking.Passenger.ShouldBe(held.Passenger);
        booking.ExpiresAt.ShouldBe(held.HeldUntil);
    }

    [Fact]
    public void Apply_PaymentAuthorized_sets_payment_ref()
    {
        var booking = new BookingAggregate();
        booking.Apply(Sample.OfferQuoted());
        booking.Apply(Sample.OfferHeld());

        var payRef = PaymentRef.New();
        var rub = CurrencyCode.Create("RUB").Value;
        var amount = Money.Create(5420m, rub).Value;
        var e = new PaymentAuthorized(
            payRef,
            amount,
            new DateTimeOffset(2026, 7, 15, 13, 0, 0, TimeSpan.Zero)
        );

        booking.Apply(e);

        booking.PaymentRef.ShouldBe(payRef);
    }

    [Fact]
    public void Apply_OrderConfirmed_transitions_to_Confirmed()
    {
        var booking = new BookingAggregate();
        booking.Apply(Sample.OfferQuoted());
        booking.Apply(Sample.OfferHeld());

        var payRef = PaymentRef.New();
        var e = new OrderConfirmed(
            "ord_123",
            payRef,
            new DateTimeOffset(2026, 7, 15, 14, 0, 0, TimeSpan.Zero)
        );

        booking.Apply(e);

        booking.Status.ShouldBe(BookingStatus.Confirmed);
        booking.ProviderOrderId.ShouldBe("ord_123");
        booking.PaymentRef.ShouldBe(payRef);
    }

    [Fact]
    public void Apply_OrderTicketed_transitions_to_Ticketed_with_ticket_numbers()
    {
        var booking = new BookingAggregate();
        booking.Apply(Sample.OfferQuoted());
        booking.Apply(Sample.OfferHeld());
        booking.Apply(
            new OrderConfirmed(
                "ord_123",
                PaymentRef.New(),
                new DateTimeOffset(2026, 7, 15, 14, 0, 0, TimeSpan.Zero)
            )
        );

        IReadOnlyList<string> tickets = ["TKT001", "TKT002"];
        var e = new OrderTicketed(
            tickets,
            new DateTimeOffset(2026, 7, 15, 15, 0, 0, TimeSpan.Zero)
        );

        booking.Apply(e);

        booking.Status.ShouldBe(BookingStatus.Ticketed);
        booking.TicketNumbers.ShouldBe(tickets);
    }

    [Fact]
    public void Apply_OrderCancelled_transitions_to_Cancelled()
    {
        var booking = new BookingAggregate();
        booking.Apply(Sample.OfferQuoted());

        booking.Apply(
            new OrderCancelled(
                CancelReason.User,
                new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero)
            )
        );

        booking.Status.ShouldBe(BookingStatus.Cancelled);
    }

    [Fact]
    public void Apply_OrderRefunded_transitions_to_Refunded()
    {
        var booking = new BookingAggregate();
        booking.Apply(Sample.OfferQuoted());
        booking.Apply(Sample.OfferHeld());
        booking.Apply(
            new OrderConfirmed(
                "ord_123",
                PaymentRef.New(),
                new DateTimeOffset(2026, 7, 15, 14, 0, 0, TimeSpan.Zero)
            )
        );
        booking.Apply(
            new OrderCancelled(
                CancelReason.Airline,
                new DateTimeOffset(2026, 7, 16, 10, 0, 0, TimeSpan.Zero)
            )
        );

        var rub = CurrencyCode.Create("RUB").Value;
        var refundAmount = Money.Create(5420m, rub).Value;
        var e = new OrderRefunded(
            new RefundRef(Guid.NewGuid()),
            refundAmount,
            RefundInitiator.Airline,
            new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero)
        );

        booking.Apply(e);

        booking.Status.ShouldBe(BookingStatus.Refunded);
    }

    // ── Guard tests ───────────────────────────────────────────────────────────

    [Fact]
    public void GuardCanHold_throws_when_not_in_OfferQuoted()
    {
        var booking = new BookingAggregate(); // Status = None

        Should.Throw<InvalidBookingStateException>(() => booking.GuardCanHold());
    }

    [Fact]
    public void GuardCanHold_does_not_throw_in_OfferQuoted()
    {
        var booking = new BookingAggregate();
        booking.Apply(Sample.OfferQuoted());

        Should.NotThrow(() => booking.GuardCanHold());
    }

    [Fact]
    public void GuardCanConfirm_throws_when_not_in_Held()
    {
        var booking = new BookingAggregate();
        booking.Apply(Sample.OfferQuoted()); // Status = OfferQuoted, not Held

        Should.Throw<InvalidBookingStateException>(() => booking.GuardCanConfirm());
    }

    [Fact]
    public void GuardCanCancel_throws_when_in_Cancelled()
    {
        var booking = new BookingAggregate();
        booking.Apply(Sample.OfferQuoted());
        booking.Apply(
            new OrderCancelled(
                CancelReason.User,
                new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero)
            )
        );

        Should.Throw<InvalidBookingStateException>(() => booking.GuardCanCancel());
    }

    [Fact]
    public void GuardCanCancel_throws_when_in_Refunded()
    {
        var booking = new BookingAggregate();
        booking.Apply(Sample.OfferQuoted());
        booking.Apply(Sample.OfferHeld());
        booking.Apply(
            new OrderConfirmed(
                "ord_123",
                PaymentRef.New(),
                new DateTimeOffset(2026, 7, 15, 14, 0, 0, TimeSpan.Zero)
            )
        );
        booking.Apply(
            new OrderCancelled(
                CancelReason.Airline,
                new DateTimeOffset(2026, 7, 16, 10, 0, 0, TimeSpan.Zero)
            )
        );
        var rub = CurrencyCode.Create("RUB").Value;
        booking.Apply(
            new OrderRefunded(
                new RefundRef(Guid.NewGuid()),
                Money.Create(5420m, rub).Value,
                RefundInitiator.Airline,
                new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero)
            )
        );

        Should.Throw<InvalidBookingStateException>(() => booking.GuardCanCancel());
    }

    [Fact]
    public void GuardOfferNotExpired_throws_when_expired()
    {
        // Offer expires at 09:40; fake clock is at 12:00 — already past
        var booking = new BookingAggregate();
        booking.Apply(Sample.OfferQuoted()); // ExpiresAt = 2026-07-15T09:40Z

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));

        Should.Throw<InvalidBookingStateException>(() => booking.GuardOfferNotExpired(time));
    }

    [Fact]
    public void GuardOfferNotExpired_does_not_throw_when_in_future()
    {
        // Offer expires at 09:40; fake clock is at 09:00 — still valid
        var booking = new BookingAggregate();
        booking.Apply(Sample.OfferQuoted()); // ExpiresAt = 2026-07-15T09:40Z

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero));

        Should.NotThrow(() => booking.GuardOfferNotExpired(time));
    }

    // ── Sample fixtures ───────────────────────────────────────────────────────

    private static class Sample
    {
        private static IataCode Iata(string c) => IataCode.Create(c).Value;

        private static CurrencyCode Rub => CurrencyCode.Create("RUB").Value;

        public static OfferQuoted OfferQuoted()
        {
            var money = Money.Create(5420m, Rub).Value;
            var seg = Segment
                .Create(
                    Iata("LED"),
                    Iata("DME"),
                    new DateTimeOffset(2026, 7, 15, 9, 20, 0, TimeSpan.Zero),
                    new DateTimeOffset(2026, 7, 15, 12, 40, 0, TimeSpan.Zero),
                    "SU",
                    "100",
                    CabinClass.Economy
                )
                .Value;
            var slice = Slice.Create(new[] { seg }).Value;
            var itinerary = Itinerary.Create(new[] { slice }).Value;

            return new OfferQuoted(
                OfferId.New(),
                itinerary,
                money,
                new DateTimeOffset(2026, 7, 15, 9, 40, 0, TimeSpan.Zero), // ExpiresAt
                "off_DUF_123",
                new DateTimeOffset(2026, 7, 15, 9, 20, 0, TimeSpan.Zero) // QuotedAt
            );
        }

        public static OfferHeld OfferHeld()
        {
            var phone = PhoneNumber.Create("+79161234567").Value;
            var passenger = PassengerInfo
                .Create(
                    "Ivan",
                    "Ivanov",
                    new DateOnly(1990, 6, 15),
                    Gender.Male,
                    "ivan@example.com",
                    phone
                )
                .Value;

            return new OfferHeld(
                "ord_DUF_456",
                passenger,
                new DateTimeOffset(2026, 7, 15, 11, 20, 0, TimeSpan.Zero), // HeldUntil
                new DateTimeOffset(2026, 7, 15, 9, 50, 0, TimeSpan.Zero) // HeldAt
            );
        }
    }
}
