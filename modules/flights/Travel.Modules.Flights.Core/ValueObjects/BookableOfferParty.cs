using System.Text.Json.Serialization;
using ErrorOr;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.ValueObjects;

public enum BookingPassengerKind
{
    Adult = 1,
}

public sealed record SupplierPassengerSlot(
    SupplierPassengerReference Reference,
    BookingPassengerKind Kind
);

public sealed record BookableOfferParty
{
    public EquatableArray<SupplierPassengerSlot> Passengers { get; }
    public DateOnly FirstDepartureLocalDate { get; }
    public bool? SupportsHold { get; }
    public bool? RequiresIdentityDocuments { get; }

    [JsonIgnore]
    public int PassengerCount => Passengers.Count;

    [JsonConstructor]
    private BookableOfferParty(
        EquatableArray<SupplierPassengerSlot> passengers,
        DateOnly firstDepartureLocalDate,
        bool? supportsHold,
        bool? requiresIdentityDocuments
    )
    {
        Passengers = new(passengers.ToArray());
        FirstDepartureLocalDate = firstDepartureLocalDate;
        SupportsHold = supportsHold;
        RequiresIdentityDocuments = requiresIdentityDocuments;
    }

    public static ErrorOr<BookableOfferParty> Create(
        IEnumerable<SupplierPassengerSlot> passengers,
        DateOnly firstDepartureLocalDate,
        bool? supportsHold,
        bool? requiresIdentityDocuments
    )
    {
        var party = new BookableOfferParty(
            new(passengers?.ToArray() ?? []),
            firstDepartureLocalDate,
            supportsHold,
            requiresIdentityDocuments
        );
        var validation = party.Validate();
        return validation.IsError ? validation.Errors : party;
    }

    public ErrorOr<Success> Validate()
    {
        if (
            PassengerCount is < 1 or > 9
            || FirstDepartureLocalDate == DateOnly.MinValue
            || Passengers.Any(p =>
                p is null
                || p.Reference is null
                || SupplierPassengerReference.Create(p.Reference.Value).IsError
                || p.Kind != BookingPassengerKind.Adult
            )
            || Passengers.Select(p => p.Reference.Value).Distinct(StringComparer.Ordinal).Count()
                != PassengerCount
        )
            return Error.Validation(
                "Flights.OfferPartyInvalid",
                "Offer must contain 1 to 9 distinct supported adult passengers and a departure date."
            );
        return Result.Success;
    }
}
