using System.Diagnostics.Metrics;
using Travel.Modules.Flights.Application.Observability;

namespace Travel.Modules.Flights.Infrastructure.Observability;

public sealed class FlightsMetrics : IFlightsMetrics
{
    public const string MeterName = "Travel.Flights";

    // Search
    public Histogram<double> SearchLatency { get; }
    private readonly Counter<long> _searchErrors;

    // Webhooks
    private readonly Counter<long> _webhookReceived;
    private readonly Histogram<double> _webhookProcessingLag;
    private readonly Counter<long> _airlineInitiatedChange;

    // Payments
    private readonly Counter<long> _paymentSuccessTotal;
    private readonly Counter<long> _paymentFailureTotal;

    // NL search (defined; wired from Travel.AI in M2 when cross-service contract carries usage)
    private readonly Counter<long> _nlSearchTokensUsed;
    private readonly Counter<double> _nlSearchCostUsd;

    // Aggregate events
    private readonly Counter<long> _aggregateEventsAppended;

    public FlightsMetrics(IMeterFactory factory)
    {
        var m = factory.Create(MeterName);

        SearchLatency = m.CreateHistogram<double>("flights.search.duration_ms", unit: "ms");
        _searchErrors = m.CreateCounter<long>("flights.search.errors");

        _webhookReceived = m.CreateCounter<long>("flights.webhook.received_total");
        _webhookProcessingLag = m.CreateHistogram<double>(
            "flights.webhook.processing_lag_ms",
            unit: "ms"
        );
        _airlineInitiatedChange = m.CreateCounter<long>("flights.webhook.airline_change_total");

        _paymentSuccessTotal = m.CreateCounter<long>("flights.payment.success_total");
        _paymentFailureTotal = m.CreateCounter<long>("flights.payment.failure_total");

        _nlSearchTokensUsed = m.CreateCounter<long>("flights.nl_search.tokens_used");
        _nlSearchCostUsd = m.CreateCounter<double>("flights.nl_search.cost_usd");

        _aggregateEventsAppended = m.CreateCounter<long>("flights.aggregate.events_appended_total");
    }

    // ── ISearchMetrics ────────────────────────────────────────────────────────

    public void RecordSearchLatency(double elapsedMs, string provider, string status) =>
        SearchLatency.Record(
            elapsedMs,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("status", status)
        );

    // ── IFlightsMetrics ───────────────────────────────────────────────────────

    public void RecordSearchError(string provider) =>
        _searchErrors.Add(1, new KeyValuePair<string, object?>("provider", provider));

    public void RecordPaymentOutcome(bool success)
    {
        if (success)
            _paymentSuccessTotal.Add(1);
        else
            _paymentFailureTotal.Add(1);
    }

    public void RecordAggregateEventsAppended(string eventType, long count = 1) =>
        _aggregateEventsAppended.Add(
            count,
            new KeyValuePair<string, object?>("event_type", eventType)
        );

    public void RecordNlSearchUsage(int inputTokens, int outputTokens, decimal costUsd)
    {
        _nlSearchTokensUsed.Add(inputTokens + outputTokens);
        _nlSearchCostUsd.Add((double)costUsd);
    }

    public void RecordWebhookReceived(string eventType) =>
        _webhookReceived.Add(1, new KeyValuePair<string, object?>("event_type", eventType));

    public void RecordWebhookProcessingLag(double ms, string eventType) =>
        _webhookProcessingLag.Record(
            ms,
            new KeyValuePair<string, object?>("event_type", eventType)
        );

    public void RecordAirlineInitiatedChange() => _airlineInitiatedChange.Add(1);
}
