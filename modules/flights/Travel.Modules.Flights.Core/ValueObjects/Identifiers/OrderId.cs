namespace Travel.Modules.Flights.Core.ValueObjects.Identifiers;

public readonly record struct OrderId(Guid Value)
{
    public static OrderId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}
