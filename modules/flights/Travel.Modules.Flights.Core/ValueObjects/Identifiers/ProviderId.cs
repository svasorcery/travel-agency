namespace Travel.Modules.Flights.Core.ValueObjects.Identifiers;

public readonly record struct ProviderId(string Value)
{
    public static ProviderId Duffel { get; } = new("duffel");
    public static ProviderId Travelpayouts { get; } = new("travelpayouts");

    public override string ToString() => Value;
}
