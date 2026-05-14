namespace Travel.Modules.Flights.Infrastructure.Providers.Travelpayouts;

public sealed class TravelpayoutsOptions
{
    public string BaseUrl { get; set; } = "https://api.travelpayouts.com";
    public string ApiVersion { get; set; } = "v3";
    public string ApiToken { get; set; } = string.Empty;
    public string PartnerMarker { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 4;
}
