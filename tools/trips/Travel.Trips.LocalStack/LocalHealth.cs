using System.Text.Json;

namespace Travel.Trips.LocalStack;

public static class LocalHealth
{
    public static Dictionary<string, string> ParseDependencies(string json)
    {
        if (json.Length > 65536)
            throw new LocalStackRefusal("Health response exceeds the diagnostic bound.");
        using var document = JsonDocument.Parse(json);
        var facts = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!document.RootElement.TryGetProperty("checks", out var checks))
            return facts;
        foreach (var name in new[] { "trips", "postgres", "redis", "nats", "duffel" })
            if (
                checks.TryGetProperty(name, out var check)
                && check.TryGetProperty("status", out var status)
                && status.GetString() is "Healthy" or "Degraded" or "Unhealthy"
            )
                facts.Add(name, status.GetString()!);
        return facts;
    }

    public static bool RequiredHealthy(IReadOnlyDictionary<string, string> facts) =>
        new[] { "trips", "postgres", "redis", "nats" }.All(name =>
            facts.TryGetValue(name, out var status) && status == "Healthy"
        );
}
