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
            BookingRejectionCode.QuoteBindingRequired => Error.Validation(
                "Flights.QuoteBindingRequired",
                "A fresh passenger quote binding is required."
            ),
            BookingRejectionCode.QuoteBindingInvalid => Error.Validation(
                "Flights.QuoteBindingInvalid",
                "Quote passenger binding is invalid."
            ),
            BookingRejectionCode.QuoteRevisionMismatch => Error.Conflict(
                "Flights.QuoteRevisionMismatch",
                "Passenger quote revision has changed."
            ),
            BookingRejectionCode.PassengerCountMismatch => Error.Conflict(
                "Flights.PassengerCountMismatch",
                "Passenger count must match the current quote."
            ),
            BookingRejectionCode.HoldNotSupported => Error.Validation(
                "Flights.HoldNotSupported",
                "Offer does not explicitly support a hold."
            ),
            BookingRejectionCode.IdentityDocumentsRequired => Error.Validation(
                "Flights.IdentityDocumentsRequired",
                "Offer requires identity documents or its requirement is unknown."
            ),
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
