using Travel.Shared.Infrastructure.Telemetry;

namespace Travel.Modules.Rail.Infrastructure;

public sealed class RailUrlRedactionContributor : IHttpUrlRedactionContributor
{
    public IReadOnlyCollection<string> SensitiveQueryParameterNames { get; } =
    ["from", "to", "date"];
}
