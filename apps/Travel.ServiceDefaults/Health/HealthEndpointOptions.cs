namespace Travel.ServiceDefaults.Health;

public sealed class HealthEndpointOptions
{
    public const string SectionName = "HealthEndpoints";

    public int InternalPort { get; set; }
}
