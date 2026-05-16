namespace Travel.Modules.Flights.Core.ValueObjects.Identifiers;

public readonly record struct OfferId(Guid Value)
{
    public static OfferId New() => new(Guid.NewGuid());

    public static readonly OfferId None = new(Guid.Empty);

    public bool IsEmpty => Value == Guid.Empty;

    public override string ToString() => Value.ToString("N");
}
