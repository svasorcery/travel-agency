namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record FareConditions(
    bool ChangeAllowed,
    bool RefundAllowed,
    string? FareBasisCode,
    string? CabinClassMarketing
);
