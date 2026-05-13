using Shouldly;
using Travel.Modules.Flights.Core.Aggregates;

namespace Travel.Modules.Flights.Tests.Unit.Aggregates;

public sealed class BookingAggregateSkeletonTests
{
    [Fact]
    public void NewBookingAggregate_HasNoneStatusAndEmptyTickets()
    {
        // Arrange & Act
        var booking = new BookingAggregate();

        // Assert
        booking.Status.ShouldBe(BookingStatus.None);
        booking.TicketNumbers.Count.ShouldBe(0);
    }
}
