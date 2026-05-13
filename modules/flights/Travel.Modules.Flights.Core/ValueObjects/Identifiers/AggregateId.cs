namespace Travel.Modules.Flights.Core.ValueObjects.Identifiers;

public readonly record struct AggregateId(Guid Value)
{
    public static AggregateId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}
