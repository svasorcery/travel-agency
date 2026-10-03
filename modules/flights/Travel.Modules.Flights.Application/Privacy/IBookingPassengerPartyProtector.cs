using ErrorOr;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Application.Privacy;

public sealed record BookingPassengerPartyProtectionContext(
    Guid AggregateId,
    Guid OwnerUserId,
    Guid QuoteRevision,
    int PassengerCount
);

public interface IBookingPassengerPartyProtector
{
    ErrorOr<ProtectedPassengerPartySnapshot> Protect(
        BookingPassengerPartyProtectionContext context,
        EquatableArray<BookingPassenger> passengers
    );
    ErrorOr<EquatableArray<BookingPassenger>> Unprotect(
        BookingPassengerPartyProtectionContext context,
        ProtectedPassengerPartySnapshot snapshot
    );
}
