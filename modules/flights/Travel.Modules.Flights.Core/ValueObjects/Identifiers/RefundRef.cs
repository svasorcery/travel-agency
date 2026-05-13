namespace Travel.Modules.Flights.Core.ValueObjects.Identifiers;

public readonly record struct RefundRef(Guid Value)
{
    public static RefundRef New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}
