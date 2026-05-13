namespace Travel.Modules.Flights.Core.ValueObjects.Identifiers;

public readonly record struct PaymentRef(Guid Value)
{
    public static PaymentRef New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString("N");
}
