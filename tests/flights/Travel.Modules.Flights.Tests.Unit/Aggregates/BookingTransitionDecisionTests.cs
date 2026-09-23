using Shouldly;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Tests.Unit.Aggregates;

public sealed class BookingTransitionDecisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ReQuote_allows_only_the_same_offer_reference_while_quoted()
    {
        var booking = BuildTo(BookingStatus.OfferQuoted);

        booking.DecideReQuote("off_test").ShouldBeOfType<BookingTransitionDecision.Allowed>();
        AssertRejected(
            booking.DecideReQuote("off_other"),
            BookingRejectionCode.OfferReferenceMismatch
        );
    }

    [Theory]
    [InlineData(BookingStatus.None)]
    [InlineData(BookingStatus.Held)]
    [InlineData(BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Ticketed)]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Refunded)]
    public void ReQuote_rejects_every_non_quoted_state(BookingStatus status)
    {
        AssertRejected(
            BuildTo(status).DecideReQuote("off_test"),
            BookingRejectionCode.InvalidState
        );
    }

    [Theory]
    [InlineData(BookingStatus.None)]
    [InlineData(BookingStatus.Held)]
    [InlineData(BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Ticketed)]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Refunded)]
    public void Hold_rejects_every_non_quoted_state(BookingStatus status)
    {
        AssertRejected(BuildTo(status).DecideHold(Now), BookingRejectionCode.InvalidState);
    }

    [Theory]
    [InlineData(BookingStatus.None)]
    [InlineData(BookingStatus.OfferQuoted)]
    [InlineData(BookingStatus.Confirmed)]
    [InlineData(BookingStatus.Ticketed)]
    [InlineData(BookingStatus.Cancelled)]
    [InlineData(BookingStatus.Refunded)]
    public void Confirm_rejects_every_non_held_state_including_repeat_confirmation(
        BookingStatus status
    )
    {
        AssertRejected(BuildTo(status).DecideConfirm(Now), BookingRejectionCode.InvalidState);
    }

    [Theory]
    [InlineData(-1, typeof(BookingTransitionDecision.Rejected))]
    [InlineData(0, typeof(BookingTransitionDecision.Rejected))]
    [InlineData(1, typeof(BookingTransitionDecision.Allowed))]
    public void Hold_uses_an_inclusive_offer_expiry_boundary(
        int expiryOffsetSeconds,
        Type expectedDecisionType
    )
    {
        var booking = BuildQuoted(Now.AddSeconds(expiryOffsetSeconds));

        var decision = booking.DecideHold(Now);

        decision.GetType().ShouldBe(expectedDecisionType);
        if (decision is BookingTransitionDecision.Rejected rejected)
            rejected.Reason.Code.ShouldBe(BookingRejectionCode.OfferExpired);
    }

    [Theory]
    [InlineData(-1, typeof(BookingTransitionDecision.Rejected))]
    [InlineData(0, typeof(BookingTransitionDecision.Rejected))]
    [InlineData(1, typeof(BookingTransitionDecision.Allowed))]
    public void Confirm_uses_an_inclusive_hold_expiry_boundary(
        int expiryOffsetSeconds,
        Type expectedDecisionType
    )
    {
        var booking = BuildHeld(Now.AddSeconds(expiryOffsetSeconds));

        var decision = booking.DecideConfirm(Now);

        decision.GetType().ShouldBe(expectedDecisionType);
        if (decision is BookingTransitionDecision.Rejected rejected)
            rejected.Reason.Code.ShouldBe(BookingRejectionCode.HoldExpired);
    }

    [Theory]
    [InlineData(
        BookingStatus.None,
        typeof(BookingTransitionDecision.Rejected),
        BookingRejectionCode.InvalidState
    )]
    [InlineData(BookingStatus.Held, typeof(BookingTransitionDecision.Allowed), null)]
    [InlineData(BookingStatus.Confirmed, typeof(BookingTransitionDecision.Allowed), null)]
    [InlineData(BookingStatus.Cancelled, typeof(BookingTransitionDecision.IdempotentNoOp), null)]
    [InlineData(BookingStatus.Refunded, typeof(BookingTransitionDecision.IdempotentNoOp), null)]
    [InlineData(
        BookingStatus.Ticketed,
        typeof(BookingTransitionDecision.Rejected),
        BookingRejectionCode.OrderAlreadyTicketed
    )]
    [InlineData(
        BookingStatus.OfferQuoted,
        typeof(BookingTransitionDecision.Rejected),
        BookingRejectionCode.InvalidState
    )]
    public void Cancel_has_one_decision_for_each_supported_state(
        BookingStatus status,
        Type expectedDecisionType,
        BookingRejectionCode? expectedRejection
    )
    {
        var decision = BuildTo(status).DecideCancel();

        decision.GetType().ShouldBe(expectedDecisionType);
        if (expectedRejection is { } code)
            ((BookingTransitionDecision.Rejected)decision).Reason.Code.ShouldBe(code);
    }

    [Theory]
    [InlineData(
        BookingStatus.None,
        typeof(BookingTransitionDecision.Rejected),
        BookingRejectionCode.InvalidState
    )]
    [InlineData(BookingStatus.Confirmed, typeof(BookingTransitionDecision.Allowed), null)]
    [InlineData(BookingStatus.Ticketed, typeof(BookingTransitionDecision.IdempotentNoOp), null)]
    [InlineData(BookingStatus.Cancelled, typeof(BookingTransitionDecision.IdempotentNoOp), null)]
    [InlineData(BookingStatus.Refunded, typeof(BookingTransitionDecision.IdempotentNoOp), null)]
    [InlineData(
        BookingStatus.Held,
        typeof(BookingTransitionDecision.Rejected),
        BookingRejectionCode.PrerequisiteNotMet
    )]
    [InlineData(
        BookingStatus.OfferQuoted,
        typeof(BookingTransitionDecision.Rejected),
        BookingRejectionCode.InvalidState
    )]
    public void Ticket_has_one_decision_for_each_delivery_state(
        BookingStatus status,
        Type expectedDecisionType,
        BookingRejectionCode? expectedRejection
    )
    {
        var decision = BuildTo(status).DecideTicket();

        decision.GetType().ShouldBe(expectedDecisionType);
        if (expectedRejection is { } code)
            ((BookingTransitionDecision.Rejected)decision).Reason.Code.ShouldBe(code);
    }

    [Theory]
    [InlineData(
        BookingStatus.None,
        typeof(BookingTransitionDecision.Rejected),
        BookingRejectionCode.InvalidState
    )]
    [InlineData(BookingStatus.Confirmed, typeof(BookingTransitionDecision.Allowed), null)]
    [InlineData(BookingStatus.Ticketed, typeof(BookingTransitionDecision.Allowed), null)]
    [InlineData(BookingStatus.Cancelled, typeof(BookingTransitionDecision.IdempotentNoOp), null)]
    [InlineData(BookingStatus.Refunded, typeof(BookingTransitionDecision.IdempotentNoOp), null)]
    [InlineData(
        BookingStatus.Held,
        typeof(BookingTransitionDecision.Rejected),
        BookingRejectionCode.PrerequisiteNotMet
    )]
    [InlineData(
        BookingStatus.OfferQuoted,
        typeof(BookingTransitionDecision.Rejected),
        BookingRejectionCode.InvalidState
    )]
    public void Refund_preserves_confirmed_and_ticketed_delivery(
        BookingStatus status,
        Type expectedDecisionType,
        BookingRejectionCode? expectedRejection
    )
    {
        var decision = BuildTo(status).DecideRefund();

        decision.GetType().ShouldBe(expectedDecisionType);
        if (expectedRejection is { } code)
            ((BookingTransitionDecision.Rejected)decision).Reason.Code.ShouldBe(code);
    }

    [Fact]
    public void Owner_can_be_established_only_by_hold_on_an_unowned_quote()
    {
        var userId = Guid.NewGuid();
        var booking = BuildTo(BookingStatus.OfferQuoted);

        booking
            .DecideOwner(BookingTransition.Hold, userId)
            .ShouldBeOfType<BookingTransitionDecision.Allowed>();
        AssertRejected(
            booking.DecideOwner(BookingTransition.Confirm, userId),
            BookingRejectionCode.OwnerMissing
        );
        AssertRejected(
            booking.DecideOwner(BookingTransition.Cancel, userId),
            BookingRejectionCode.OwnerMissing
        );
    }

    [Fact]
    public void Owner_must_match_for_confirm_and_cancel()
    {
        var owner = Guid.NewGuid();
        var booking = BuildHeld(Now.AddMinutes(30), owner);

        booking
            .DecideOwner(BookingTransition.Confirm, owner)
            .ShouldBeOfType<BookingTransitionDecision.Allowed>();
        booking
            .DecideOwner(BookingTransition.Cancel, owner)
            .ShouldBeOfType<BookingTransitionDecision.Allowed>();
        AssertRejected(
            booking.DecideOwner(BookingTransition.Confirm, Guid.NewGuid()),
            BookingRejectionCode.OwnerConflict
        );
    }

    [Fact]
    public void Empty_user_id_never_establishes_or_matches_an_owner()
    {
        AssertRejected(
            BuildTo(BookingStatus.OfferQuoted).DecideOwner(BookingTransition.Hold, Guid.Empty),
            BookingRejectionCode.OwnerMissing
        );
    }

    [Fact]
    public void Decisions_do_not_mutate_aggregate_state()
    {
        var booking = BuildHeld(Now.AddMinutes(30), Guid.NewGuid());

        _ = booking.DecideReQuote("off_test");
        _ = booking.DecideHold(Now);
        _ = booking.DecideConfirm(Now);
        _ = booking.DecideCancel();
        _ = booking.DecideTicket();
        _ = booking.DecideRefund();
        _ = booking.DecideOwner(BookingTransition.Confirm, booking.OwnerUserId!.Value);

        booking.Status.ShouldBe(BookingStatus.Held);
        booking.Version.ShouldBe(0);
    }

    private static void AssertRejected(
        BookingTransitionDecision decision,
        BookingRejectionCode expectedCode
    )
    {
        var rejected = decision.ShouldBeOfType<BookingTransitionDecision.Rejected>();
        rejected.Reason.Code.ShouldBe(expectedCode);
    }

    private static BookingAggregate BuildTo(BookingStatus status)
    {
        if (status == BookingStatus.None)
            return new BookingAggregate();
        if (status == BookingStatus.OfferQuoted)
            return BuildQuoted(Now.AddMinutes(30));
        if (status == BookingStatus.Held)
            return BuildHeld(Now.AddMinutes(30));

        var booking = BuildHeld(Now.AddMinutes(30));
        var payment = PaymentRef.New();
        booking.Apply(new OrderConfirmed("ord_test", payment, Now.AddMinutes(1)));
        if (status == BookingStatus.Confirmed)
            return booking;

        if (status == BookingStatus.Ticketed)
        {
            booking.Apply(
                new OrderTicketed(new EquatableArray<string>(["TKT-1"]), Now.AddMinutes(2))
            );
            return booking;
        }

        booking.Apply(new OrderCancelled(CancelReason.Airline, Now.AddMinutes(2)));
        if (status == BookingStatus.Cancelled)
            return booking;

        booking.Apply(
            new OrderRefunded(
                RefundRef.New(),
                Money.Create(100m, CurrencyCode.Create("USD").Value).Value,
                RefundInitiator.Airline,
                Now.AddMinutes(3)
            )
        );
        return booking;
    }

    private static BookingAggregate BuildQuoted(DateTimeOffset expiresAt)
    {
        var booking = new BookingAggregate();
        booking.Apply(
            new OfferQuoted(
                OfferId.New(),
                BuildItinerary(),
                Money.Create(100m, CurrencyCode.Create("USD").Value).Value,
                expiresAt,
                "off_test",
                Now
            )
        );
        return booking;
    }

    private static BookingAggregate BuildHeld(DateTimeOffset heldUntil, Guid? ownerUserId = null)
    {
        var booking = BuildQuoted(Now.AddMinutes(30));
        booking.Apply(new OfferHeld("ord_test", BuildPassenger(), heldUntil, Now, ownerUserId));
        return booking;
    }

    private static Itinerary BuildItinerary()
    {
        var segment = Segment
            .Create(
                IataCode.Create("LED").Value,
                IataCode.Create("DME").Value,
                Now.AddHours(1),
                Now.AddHours(2),
                "SU",
                "100",
                CabinClass.Economy
            )
            .Value;
        return Itinerary.Create([Slice.Create([segment]).Value]).Value;
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
}
