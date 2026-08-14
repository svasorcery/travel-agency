namespace Travel.Modules.Flights.Infrastructure.ExternalServices;

public sealed class FrankfurterOptions
{
    public const string SectionName = "Flights:Providers:Frankfurter";

    /// <summary>
    /// Base address of the Frankfurter FX API. Defaults to the public endpoint.
    /// Override in appsettings or environment variables to point at a stub in tests/staging.
    /// </summary>
    public string BaseAddress { get; set; } = "https://api.frankfurter.app/";

    public int TimeoutSeconds { get; set; } = 2;
}
