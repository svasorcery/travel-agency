using ErrorOr;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Errors;

namespace Travel.Modules.Flights.Application.Booking;

public static class BookingTransitionErrorMapper
{
    public static Error ToError(BookingRejection rejection) =>
        rejection.Code switch
        {
            BookingRejectionCode.OfferExpired => FlightsErrors.OfferExpired,
            BookingRejectionCode.HoldExpired => FlightsErrors.HoldExpired,
            BookingRejectionCode.OrderAlreadyTicketed => FlightsErrors.OrderNotCancellable(
                $"Order in state {rejection.Status} cannot be cancelled."
            ),
            BookingRejectionCode.OfferReferenceMismatch => FlightsErrors.OfferReferenceMismatch,
            _ => FlightsErrors.InvalidState(rejection.Transition, rejection.Status),
        };

    public static Error ToOwnerError(BookingRejection rejection, Guid aggregateId) =>
        rejection.Code is BookingRejectionCode.OwnerMissing or BookingRejectionCode.OwnerConflict
            ? FlightsErrors.OfferNotFound(aggregateId.ToString())
            : ToError(rejection);

    public static void ThrowForDurableMessage(BookingRejection rejection)
    {
        if (rejection.Code is BookingRejectionCode.PrerequisiteNotMet)
            throw new BookingTransitionPrerequisiteException(rejection);

        throw new BookingTransitionRejectedException(rejection);
    }
}
