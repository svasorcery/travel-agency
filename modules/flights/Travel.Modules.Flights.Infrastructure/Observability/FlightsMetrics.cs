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
    private readonly Histogram<double> _paymentDurationMs;

    // NL search (defined; wired from Travel.AI in M2 when cross-service contract carries usage)
    private readonly Counter<long> _nlSearchTokensUsed;
    private readonly Counter<double> _nlSearchCostUsd;
    private readonly Histogram<double> _nlSearchDurationMs;

    // Aggregate events
    private readonly Counter<long> _aggregateEventsAppended;

    // Rolling-window counters for ObservableGauge instruments.
    // Thread-safe via Interlocked; no lock needed for simple increment/read.
    private long _searchTotal;
    private long _searchPartial;
    private long _paymentOutcomeTotal;
    private long _paymentOutcomeSuccess;
    private long _offersShown;
    private long _ordersBooked;

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
        _paymentDurationMs = m.CreateHistogram<double>("flights.payment.duration_ms", unit: "ms");

        _nlSearchTokensUsed = m.CreateCounter<long>("flights.nl_search.tokens_used");
        _nlSearchCostUsd = m.CreateCounter<double>("flights.nl_search.cost_usd");
        _nlSearchDurationMs = m.CreateHistogram<double>(
            "flights.nl_search.duration_ms",
            unit: "ms"
        );

        _aggregateEventsAppended = m.CreateCounter<long>("flights.aggregate.events_appended_total");

        // ObservableGauge — callbacks run synchronously when RecordObservableInstruments() is called.
        m.CreateObservableGauge<double>(
            "flights.search.partial_fill_rate",
            () =>
            {
                var total = Interlocked.Read(ref _searchTotal);
                var partial = Interlocked.Read(ref _searchPartial);
                return total == 0 ? 0.0 : (double)partial / total;
            }
        );

        m.CreateObservableGauge<double>(
            "flights.payment.success_rate",
            () =>
            {
                var total = Interlocked.Read(ref _paymentOutcomeTotal);
                var success = Interlocked.Read(ref _paymentOutcomeSuccess);
                return total == 0 ? 0.0 : (double)success / total;
            }
        );

        m.CreateObservableGauge<double>(
            "flights.offer_to_book_conversion",
            () =>
            {
                var offers = Interlocked.Read(ref _offersShown);
                var booked = Interlocked.Read(ref _ordersBooked);
                return offers == 0 ? 0.0 : (double)booked / offers;
            }
        );
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
        Interlocked.Increment(ref _paymentOutcomeTotal);
        if (success)
        {
            _paymentSuccessTotal.Add(1);
            Interlocked.Increment(ref _paymentOutcomeSuccess);
        }
        else
        {
            _paymentFailureTotal.Add(1);
        }
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

    public void RecordPaymentDuration(double ms, string outcome) =>
        _paymentDurationMs.Record(ms, new KeyValuePair<string, object?>("outcome", outcome));

    public void RecordNlSearchDuration(double ms) => _nlSearchDurationMs.Record(ms);

    public void RecordSearchPartialFill(bool partial)
    {
        Interlocked.Increment(ref _searchTotal);
        if (partial)
            Interlocked.Increment(ref _searchPartial);
    }

    public void RecordOfferShown() => Interlocked.Increment(ref _offersShown);

    public void RecordOrderBooked() => Interlocked.Increment(ref _ordersBooked);
}
