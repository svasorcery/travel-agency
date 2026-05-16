namespace Travel.Modules.Flights.Core.ValueObjects.Identifiers;

public readonly record struct PaymentRef(Guid Value)
{
    public static PaymentRef New() => new(Guid.NewGuid());

    public static readonly PaymentRef None = new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString() => Value.ToString("N");
}
