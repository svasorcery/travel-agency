namespace Travel.Modules.Flights.Core.ValueObjects.Identifiers;

public readonly record struct OfferId(Guid Value)
{
    public static OfferId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}
