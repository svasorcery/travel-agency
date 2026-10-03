using System.Text.Json.Serialization;
using ErrorOr;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record QuotePassengerSlot(
    BookingPassengerId Id,
    SupplierPassengerReference SupplierReference,
    BookingPassengerKind Kind
);

public sealed record QuoteBinding
{
    public Guid Revision { get; }
    public BookableOfferParty Party { get; }
    public EquatableArray<QuotePassengerSlot> Slots { get; }

    [JsonConstructor]
    private QuoteBinding(
        Guid revision,
        BookableOfferParty party,
        EquatableArray<QuotePassengerSlot> slots
    )
    {
        Revision = revision;
        Party = party;
        Slots = new(slots.ToArray());
    }

    public static ErrorOr<QuoteBinding> Create(
        Guid revision,
        BookableOfferParty party,
        IEnumerable<QuotePassengerSlot> slots
    )
    {
        var binding = new QuoteBinding(revision, party, new(slots?.ToArray() ?? []));
        var validation = binding.Validate();
        return validation.IsError ? validation.Errors : binding;
    }

    public ErrorOr<Success> Validate()
    {
        if (
            Revision == Guid.Empty
            || Party is null
            || Party.Validate().IsError
            || Slots.Count != Party.PassengerCount
            || Slots.Any(s =>
                s is null
                || s.Id.Value == Guid.Empty
                || s.SupplierReference is null
                || SupplierPassengerReference.Create(s.SupplierReference.Value).IsError
                || s.Kind != BookingPassengerKind.Adult
            )
            || Slots.Select(s => s.Id).Distinct().Count() != Slots.Count
            || Slots.Select(s => s.SupplierReference.Value).Distinct(StringComparer.Ordinal).Count()
                != Slots.Count
            || !Slots
                .Select(s => s.SupplierReference.Value)
                .ToHashSet(StringComparer.Ordinal)
                .SetEquals(Party.Passengers.Select(s => s.Reference.Value))
        )
            return Error.Validation(
                "Flights.QuoteBindingInvalid",
                "Quote passenger binding is invalid."
            );
        return Result.Success;
    }

    /// <summary>Post-decryption check; call only after owner, state, expiry and revision/count guards.</summary>
    public ErrorOr<Success> ValidatePassengers(
        EquatableArray<BookingPassenger> passengers,
        DateOnly today
    )
    {
        var validation = Validate();
        if (validation.IsError)
            return validation.Errors;
        if (
            passengers.Count != Slots.Count
            || passengers.Any(p => p is null || p.Id.Value == Guid.Empty)
            || passengers.Select(p => p.Id).Distinct().Count() != Slots.Count
            || !passengers.Select(p => p.Id).ToHashSet().SetEquals(Slots.Select(s => s.Id))
        )
            return Error.Conflict(
                "Flights.PassengerSlotsMismatch",
                "Passenger slots must match the current quote exactly."
            );
        for (var index = 0; index < passengers.Count; index++)
        {
            var details = passengers[index].Details;
            var result = details is null
                ? ErrorOr<Success>.From([
                    Error.Validation(
                        "Flights.PassengerDetailsInvalid",
                        "Passenger details are required."
                    ),
                ])
                : details.ValidateForTravel(Party.FirstDepartureLocalDate, today);
            if (result.IsError)
                return BookingPassengerDetails.ForPassenger(result.Errors, passengers[index].Id);
        }
        return Result.Success;
    }
}
