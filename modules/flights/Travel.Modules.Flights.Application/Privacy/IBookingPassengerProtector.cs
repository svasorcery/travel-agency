using ErrorOr;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Application.Privacy;

public interface IBookingPassengerProtector
{
    ErrorOr<ProtectedPassengerSnapshot> Protect(
        Guid aggregateId,
        Guid ownerUserId,
        PassengerInfo passenger
    );
    ErrorOr<PassengerInfo> Unprotect(
        Guid aggregateId,
        Guid ownerUserId,
        ProtectedPassengerSnapshot snapshot
    );
}
