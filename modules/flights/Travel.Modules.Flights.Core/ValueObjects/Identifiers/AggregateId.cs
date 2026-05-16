namespace Travel.Modules.Flights.Core.ValueObjects.Identifiers;

public readonly record struct AggregateId(Guid Value)
{
    public static AggregateId New() => new(Guid.NewGuid());

    public static readonly AggregateId None = new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString() => Value.ToString("N");
}
