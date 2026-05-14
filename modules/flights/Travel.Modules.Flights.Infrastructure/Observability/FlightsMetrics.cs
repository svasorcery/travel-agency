using System.Diagnostics.Metrics;
using Travel.Modules.Flights.Application.Observability;

namespace Travel.Modules.Flights.Infrastructure.Observability;

public sealed class FlightsMetrics : ISearchMetrics
{
    public const string MeterName = "Travel.Flights";
    public Histogram<double> SearchLatency { get; }

    public FlightsMetrics(IMeterFactory factory)
    {
        var m = factory.Create(MeterName);
        SearchLatency = m.CreateHistogram<double>("flights.search.duration_ms", unit: "ms");
    }

    public void RecordSearchLatency(double elapsedMs, string provider, string status) =>
        SearchLatency.Record(
            elapsedMs,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("status", status)
        );
}
