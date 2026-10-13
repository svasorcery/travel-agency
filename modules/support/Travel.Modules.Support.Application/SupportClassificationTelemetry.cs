using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Travel.Modules.Support.Application;

/// <summary>Bounded operational metadata; input, caller and resource identifiers never become labels.</summary>
public static class SupportClassificationTelemetry
{
    public const string MeterName = "Travel.Support.Classification";
    private static readonly Meter Meter = new(MeterName);
    private static readonly Counter<long> Outcomes = Meter.CreateCounter<long>(
        "support.classification.outcomes"
    );
    private static readonly Histogram<double> Latency = Meter.CreateHistogram<double>(
        "support.classification.duration",
        "ms"
    );
    private static readonly Histogram<long> InputBytes = Meter.CreateHistogram<long>(
        "support.classification.input_bytes",
        "By"
    );
    private static readonly Histogram<long> InputTokens = Meter.CreateHistogram<long>(
        "support.classification.input_tokens",
        "{token}"
    );

    public static void Record(SupportClassificationResult result, double elapsedMilliseconds)
    {
        var tags = new TagList
        {
            { "status", result.Status.ToString() },
            { "reason", result.Reason.ToString() },
            { "category", result.Category?.ToString() ?? "None" },
            { "profile", result.ServingProfileId ?? "None" },
        };
        Outcomes.Add(1, tags);
        Latency.Record(Math.Max(0, elapsedMilliseconds), tags);
    }

    public static void RecordError(double elapsedMilliseconds)
    {
        var tags = new TagList
        {
            { "status", "Rejected" },
            { "reason", "Rejected" },
            { "category", "None" },
            { "profile", "None" },
        };
        Outcomes.Add(1, tags);
        Latency.Record(Math.Max(0, elapsedMilliseconds), tags);
    }

    public static void RecordInput(long bytes, int? tokens)
    {
        InputBytes.Record(bytes);
        if (tokens is >= 0)
            InputTokens.Record(tokens.Value);
    }
}
