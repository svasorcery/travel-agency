namespace Travel.Modules.Flights.Core.ValueObjects.Identifiers;

public readonly record struct RefundRef(Guid Value)
{
    public static RefundRef New() => new(Guid.NewGuid());

    public static readonly RefundRef None = new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString() => Value.ToString("N");
}
