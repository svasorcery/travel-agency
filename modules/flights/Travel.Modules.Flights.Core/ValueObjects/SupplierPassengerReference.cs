using System.Text.Json.Serialization;
using ErrorOr;

namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record SupplierPassengerReference
{
    public string Value { get; }

    [JsonConstructor]
    private SupplierPassengerReference(string value) => Value = value;

    public static ErrorOr<SupplierPassengerReference> Create(string value) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= 256
        && value == value.Trim()
        && !value.Any(char.IsControl)
            ? new SupplierPassengerReference(value)
            : Error.Validation(
                "Flights.SupplierPassengerReferenceInvalid",
                "Supplier passenger reference is invalid."
            );
}
