namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel;

public sealed class DuffelOptions
{
    public const string SectionName = "Flights:Duffel";

    public string BaseUrl { get; set; } = "https://api.duffel.com";
    public string ApiVersion { get; set; } = "v2";
    public string ApiKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    /// Total resilience-pipeline budget for order and booking calls, including retries
    /// and their delays. Default: 10 s.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Per-call timeout applied inside <see cref="DuffelFlightSearchProvider.SearchAsync"/>
    /// via a linked <see cref="System.Threading.CancellationTokenSource"/>. Search responses
    /// must be fast; spec §19 / §3 dec.3 / §6.1 mandate a 4 s budget. Default: 4 s.
    /// </summary>
    public int SearchTimeoutSeconds { get; set; } = 4;
}
