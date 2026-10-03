using ErrorOr;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;

namespace Travel.Modules.Flights.Application.Booking;

public static class QuoteBindingFactory
{
    public static ErrorOr<QuoteBinding> Create(
        BookableOfferParty? party,
        int expectedCount,
        QuoteBinding? previous
    )
    {
        if (party is null)
            return Error.Validation(
                "Flights.QuoteBindingRequired",
                "Refresh the offer to obtain passenger bindings."
            );
        if (party.Validate().IsError)
            return Error.Validation(
                "Flights.QuoteBindingInvalid",
                "Provider passenger binding is invalid."
            );
        if (
            party.PassengerCount != expectedCount
            || previous is not null && previous.Party.PassengerCount != expectedCount
        )
            return Error.Conflict(
                "Flights.PassengerCountMismatch",
                "Passenger count changed; start a new search."
            );
        var refs = party
            .Passengers.Select(p => p.Reference.Value)
            .ToHashSet(StringComparer.Ordinal);
        var reuse =
            previous is not null
            && !previous.Validate().IsError
            && refs.SetEquals(previous.Slots.Select(s => s.SupplierReference.Value));
        var byRef = party.Passengers.ToDictionary(p => p.Reference.Value, StringComparer.Ordinal);
        var slots = reuse
            ? previous!.Slots.Select(s => new QuotePassengerSlot(
                s.Id,
                byRef[s.SupplierReference.Value].Reference,
                BookingPassengerKind.Adult
            ))
            : party.Passengers.Select(p => new QuotePassengerSlot(
                BookingPassengerId.Create(Guid.NewGuid()).Value,
                p.Reference,
                p.Kind
            ));
        return QuoteBinding.Create(Guid.NewGuid(), party, slots);
    }
}
