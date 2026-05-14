namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel;

public sealed class DuffelOptions
{
    public string BaseUrl { get; set; } = "https://api.duffel.com";
    public string ApiVersion { get; set; } = "v2";
    public string ApiKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 10;
}
