using System.Diagnostics;

namespace Travel.Modules.Flights.Application.Observability;

/// <summary>
/// Shared <see cref="ActivitySource"/> for the Flights module.
/// Registered with OTel tracing via <c>.AddSource(FlightsActivitySource.Name)</c>.
/// Lives in the Application layer so handlers can start spans without taking an
/// Infrastructure dependency.
/// </summary>
public static class FlightsActivitySource
{
    public const string Name = "Travel.Flights";

    public static readonly ActivitySource Source = new(Name);
}
