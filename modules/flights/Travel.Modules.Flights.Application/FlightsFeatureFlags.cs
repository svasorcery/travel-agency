namespace Travel.Modules.Flights.Application;

/// <summary>
/// Runtime feature flags for the Flights module, read from <c>Flights:FeatureFlags</c>
/// in application configuration. Injected as <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/>
/// so hot-reload picks up changes without an application restart.
/// </summary>
public sealed class FlightsFeatureFlags
{
    public const string SectionName = "Flights:FeatureFlags";

    /// <summary>Travelpayouts deeplink provider toggle (WS5 Task 5.8 wires the search gate).</summary>
    public ProviderFlag Travelpayouts { get; set; } = new();

    /// <summary>Natural-language search endpoint toggle.</summary>
    public ProviderFlag NlSearch { get; set; } = new();

    public sealed class ProviderFlag
    {
        public bool Enabled { get; set; } = true;
    }
}
