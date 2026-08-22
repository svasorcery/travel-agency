using Travel.Shared.Infrastructure.Telemetry;

namespace Travel.Modules.Flights.Infrastructure.Observability;

internal sealed class TravelpayoutsUrlRedactionContributor : IHttpUrlRedactionContributor
{
    public IReadOnlyCollection<string> SensitiveQueryParameterNames { get; } = ["token"];
}
