using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Core.Aggregates;

namespace Travel.Modules.Flights.Tests.Unit.Handlers;

public sealed class BookingTransitionErrorMapperTests
{
    [Theory]
    [InlineData(BookingRejectionCode.OfferExpired, ErrorType.Validation, "Flights.OfferExpired")]
    [InlineData(BookingRejectionCode.HoldExpired, ErrorType.Conflict, "Flights.HoldExpired")]
    [InlineData(BookingRejectionCode.InvalidState, ErrorType.Conflict, "Flights.InvalidState")]
    [InlineData(
        BookingRejectionCode.OfferReferenceMismatch,
        ErrorType.Conflict,
        "Flights.OfferReferenceMismatch"
    )]
    [InlineData(
        BookingRejectionCode.OrderAlreadyTicketed,
        ErrorType.Conflict,
        "Flights.OrderNotCancellable"
    )]
    public void Expected_user_rejections_map_to_stable_ErrorOr_contracts(
        BookingRejectionCode code,
        ErrorType expectedType,
        string expectedCode
    )
    {
        var rejection = new BookingRejection(BookingTransition.Confirm, code, BookingStatus.Held);

        var error = BookingTransitionErrorMapper.ToError(rejection);

        error.Type.ShouldBe(expectedType);
        error.Code.ShouldBe(expectedCode);
    }

    [Theory]
    [InlineData(BookingRejectionCode.OwnerMissing)]
    [InlineData(BookingRejectionCode.OwnerConflict)]
    public void Ownership_rejections_fail_closed_as_not_found(BookingRejectionCode code)
    {
        var rejection = new BookingRejection(
            BookingTransition.Cancel,
            code,
            BookingStatus.Confirmed
        );

        var error = BookingTransitionErrorMapper.ToOwnerError(rejection, Guid.NewGuid());

        error.Type.ShouldBe(ErrorType.NotFound);
        error.Code.ShouldBe("Flights.OfferNotFound");
    }

    [Fact]
    public void Durable_prerequisite_rejection_is_not_mapped_to_a_user_success()
    {
        var rejection = new BookingRejection(
            BookingTransition.Ticket,
            BookingRejectionCode.PrerequisiteNotMet,
            BookingStatus.Held
        );

        var exception = Should.Throw<BookingTransitionPrerequisiteException>(() =>
            BookingTransitionErrorMapper.ThrowForDurableMessage(rejection)
        );

        exception.Rejection.ShouldBe(rejection);
    }
}
