using ErrorOr;

namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record CabinClass
{
    public string Code { get; }

    private CabinClass(string code) => Code = code;

    public static CabinClass Economy { get; } = new("economy");
    public static CabinClass PremiumEconomy { get; } = new("premium_economy");
    public static CabinClass Business { get; } = new("business");
    public static CabinClass First { get; } = new("first");

    public static ErrorOr<CabinClass> Parse(string code) =>
        code?.ToLowerInvariant() switch
        {
            "economy" or "basic_economy" => Economy,
            "premium_economy" => PremiumEconomy,
            "business" => Business,
            "first" => First,
            _ => Error.Validation("CabinClass.Unknown", $"Unknown cabin class '{code}'."),
        };

    public override string ToString() => Code;
}
