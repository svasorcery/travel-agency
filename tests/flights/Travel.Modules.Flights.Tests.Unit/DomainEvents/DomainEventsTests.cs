using Shouldly;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Tests.Unit.DomainEvents;

public sealed class DomainEventsTests
{
    [Fact]
    public void OfferQuoted_ImplementsIDomainEvent_And_HasStructuralEquality()
    {
        // Arrange
        var offerId = new OfferId(Guid.NewGuid());
        var currency = CurrencyCode.Create("USD").Value;
        var origin = IataCode.Create("JFK").Value;
        var destination = IataCode.Create("LAX").Value;
        var baseTime = new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero);
        var segment = Segment
            .Create(
                origin,
                destination,
                baseTime,
                baseTime.AddHours(6),
                "AA",
                "100",
                CabinClass.Economy
            )
            .Value;
        var slice = Slice.Create(new[] { segment }).Value;
        var itinerary = Itinerary.Create(new[] { slice }).Value;
        var amount = Money.Create(500m, currency).Value;
        var expiresAt = DateTimeOffset.UtcNow.AddHours(24);
        var quotedAt = DateTimeOffset.UtcNow;

        // Act
        var event1 = new OfferQuoted(
            offerId,
            itinerary,
            amount,
            expiresAt,
            "provider-ref-123",
            quotedAt
        );
        var event2 = new OfferQuoted(
            offerId,
            itinerary,
            amount,
            expiresAt,
            "provider-ref-123",
            quotedAt
        );

        // Assert
        event1.ShouldBeAssignableTo<IDomainEvent>();
        event1.ShouldBe(event2);
    }

    [Fact]
    public void OfferReQuoted_ImplementsIDomainEvent_And_HasStructuralEquality()
    {
        // Arrange
        var offerId = new OfferId(Guid.NewGuid());
        var currency = CurrencyCode.Create("USD").Value;
        var oldAmount = Money.Create(500m, currency).Value;
        var newAmount = Money.Create(520m, currency).Value;
        var reQuotedAt = DateTimeOffset.UtcNow;

        // Act
        var event1 = new OfferReQuoted(offerId, oldAmount, newAmount, reQuotedAt);
        var event2 = new OfferReQuoted(offerId, oldAmount, newAmount, reQuotedAt);

        // Assert
        event1.ShouldBeAssignableTo<IDomainEvent>();
        event1.ShouldBe(event2);
    }

    [Fact]
    public void OfferHeld_ImplementsIDomainEvent_And_HasStructuralEquality()
    {
        // Arrange
        var orderId = "order-123";
        var dob = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-30));
        var gender = Gender.Male;
        var phone = PhoneNumber.Create("+1234567890").Value;
        var passenger = PassengerInfo
            .Create("John", "Doe", dob, gender, "john@example.com", phone)
            .Value;
        var heldUntil = DateTimeOffset.UtcNow.AddHours(2);
        var heldAt = DateTimeOffset.UtcNow;

        // Act
        var event1 = new OfferHeld(orderId, passenger, heldUntil, heldAt);
        var event2 = new OfferHeld(orderId, passenger, heldUntil, heldAt);

        // Assert
        event1.ShouldBeAssignableTo<IDomainEvent>();
        event1.ShouldBe(event2);
    }

    [Fact]
    public void PaymentAuthorized_ImplementsIDomainEvent_And_HasStructuralEquality()
    {
        // Arrange
        var paymentRef = new PaymentRef(Guid.NewGuid());
        var currency = CurrencyCode.Create("USD").Value;
        var amount = Money.Create(500m, currency).Value;
        var authorizedAt = DateTimeOffset.UtcNow;

        // Act
        var event1 = new PaymentAuthorized(paymentRef, amount, authorizedAt);
        var event2 = new PaymentAuthorized(paymentRef, amount, authorizedAt);

        // Assert
        event1.ShouldBeAssignableTo<IDomainEvent>();
        event1.ShouldBe(event2);
    }

    [Fact]
    public void OrderConfirmed_ImplementsIDomainEvent_And_HasStructuralEquality()
    {
        // Arrange
        var orderId = "order-123";
        var paymentRef = new PaymentRef(Guid.NewGuid());
        var confirmedAt = DateTimeOffset.UtcNow;

        // Act
        var event1 = new OrderConfirmed(orderId, paymentRef, confirmedAt);
        var event2 = new OrderConfirmed(orderId, paymentRef, confirmedAt);

        // Assert
        event1.ShouldBeAssignableTo<IDomainEvent>();
        event1.ShouldBe(event2);
    }

    [Fact]
    public void OrderTicketed_ImplementsIDomainEvent_And_HasStructuralEquality()
    {
        // Arrange
        IReadOnlyList<string> ticketNumbers = new[] { "TKT001", "TKT002" };
        var ticketedAt = DateTimeOffset.UtcNow;

        // Act
        var event1 = new OrderTicketed(ticketNumbers, ticketedAt);
        var event2 = new OrderTicketed(ticketNumbers, ticketedAt);

        // Assert
        event1.ShouldBeAssignableTo<IDomainEvent>();
        event1.ShouldBe(event2);
    }

    [Fact]
    public void OrderCancelled_ImplementsIDomainEvent_And_HasStructuralEquality()
    {
        // Arrange
        var reason = CancelReason.User;
        var cancelledAt = DateTimeOffset.UtcNow;

        // Act
        var event1 = new OrderCancelled(reason, cancelledAt);
        var event2 = new OrderCancelled(reason, cancelledAt);

        // Assert
        event1.ShouldBeAssignableTo<IDomainEvent>();
        event1.ShouldBe(event2);
    }

    [Fact]
    public void OrderRefunded_ImplementsIDomainEvent_And_HasStructuralEquality()
    {
        // Arrange
        var refundRef = new RefundRef(Guid.NewGuid());
        var currency = CurrencyCode.Create("USD").Value;
        var refundedAmount = Money.Create(500m, currency).Value;
        var initiatedBy = RefundInitiator.Airline;
        var refundedAt = DateTimeOffset.UtcNow;

        // Act
        var event1 = new OrderRefunded(refundRef, refundedAmount, initiatedBy, refundedAt);
        var event2 = new OrderRefunded(refundRef, refundedAmount, initiatedBy, refundedAt);

        // Assert
        event1.ShouldBeAssignableTo<IDomainEvent>();
        event1.ShouldBe(event2);
    }
}
