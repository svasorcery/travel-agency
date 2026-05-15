using System.Diagnostics;

namespace Travel.Modules.Flights.Infrastructure.Observability;

/// <summary>
/// Infrastructure-layer facade for the Flights <see cref="ActivitySource"/>.
/// The canonical definition lives in <c>Travel.Modules.Flights.Application.Observability</c>
/// so handlers can start spans without an Infrastructure dependency.
/// This type forwards to the Application source and keeps the existing Infrastructure
/// import path working.
/// </summary>
public static class FlightsActivitySource
{
    /// <summary>Source name: <c>"Travel.Flights"</c>.</summary>
    public const string Name = Application.Observability.FlightsActivitySource.Name;

    /// <summary>The shared <see cref="ActivitySource"/> instance.</summary>
    public static ActivitySource Source => Application.Observability.FlightsActivitySource.Source;
}
