namespace Travel.Modules.Flights.Core.ValueObjects.Identifiers;

public readonly record struct OrderId(Guid Value)
{
    public static OrderId New() => new(Guid.NewGuid());

    public static readonly OrderId None = new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString() => Value.ToString("N");
}
