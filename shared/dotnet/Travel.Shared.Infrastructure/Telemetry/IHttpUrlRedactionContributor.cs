namespace Travel.Shared.Infrastructure.Telemetry;

/// <summary>
/// Contributes outbound HTTP query-parameter names whose values must not appear in telemetry.
/// </summary>
public interface IHttpUrlRedactionContributor
{
    IReadOnlyCollection<string> SensitiveQueryParameterNames { get; }
}
