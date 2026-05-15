namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record FareConditions(
    bool ChangeAllowed,
    bool RefundAllowed,
    string? FareBasisCode,
    string? CabinClassMarketing,
    int CheckedBaggageQuantity = 0,
    int CarryOnBaggageQuantity = 0
);
