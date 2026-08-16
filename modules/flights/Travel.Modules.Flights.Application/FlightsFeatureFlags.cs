namespace Travel.Modules.Flights.Application;

/// <summary>
/// Runtime feature flags for the Flights module, read from <c>Flights:FeatureFlags</c>
/// in application configuration. Request-time consumers may observe monitor changes, but
/// provider service registration is a startup decision and requires an application restart.
/// </summary>
public sealed class FlightsFeatureFlags
{
    public const string SectionName = "Flights:FeatureFlags";

    /// <summary>
    /// Travelpayouts deeplink provider startup toggle. Changing it requires an application restart.
    /// </summary>
    public ProviderFlag Travelpayouts { get; set; } = new();

    /// <summary>Natural-language search endpoint toggle.</summary>
    public ProviderFlag NlSearch { get; set; } = new();

    public sealed class ProviderFlag
    {
        public bool Enabled { get; set; } = true;
    }
}
