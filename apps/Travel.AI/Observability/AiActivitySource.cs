using System.Diagnostics;

namespace Travel.AI.Observability;

/// <summary>
/// Shared <see cref="ActivitySource"/> for the Travel.AI service.
/// Registered with OTel tracing via <c>.AddSource(AiActivitySource.Name)</c>.
/// </summary>
public static class AiActivitySource
{
    public const string Name = "Travel.AI";

    public static readonly ActivitySource Source = new(Name);
}
