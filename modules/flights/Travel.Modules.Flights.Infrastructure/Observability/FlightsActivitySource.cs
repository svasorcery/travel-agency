using System.Diagnostics;

namespace Travel.Modules.Flights.Infrastructure.Observability;

/// <summary>
/// Shared <see cref="ActivitySource"/> for the Flights module.
/// Registered with OTel tracing via <c>.AddSource(FlightsActivitySource.Name)</c>.
/// </summary>
public static class FlightsActivitySource
{
    public const string Name = "Travel.Flights";

    public static readonly ActivitySource Source = new(Name);
}
