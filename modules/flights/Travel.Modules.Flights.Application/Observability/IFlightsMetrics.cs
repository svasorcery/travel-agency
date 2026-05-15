namespace Travel.Modules.Flights.Application.Observability;

/// <summary>
/// Metrics abstraction for the Flights module. Application-layer handlers depend on this
/// interface; the Infrastructure <c>FlightsMetrics</c> implements it.
/// Extends <see cref="ISearchMetrics"/> so existing <c>SearchFlightsHandler</c> contracts continue to work.
/// </summary>
public interface IFlightsMetrics : ISearchMetrics
{
    void RecordSearchError(string provider);
    void RecordPaymentOutcome(bool success);
    void RecordAggregateEventsAppended(string eventType, long count = 1);
    void RecordNlSearchUsage(int inputTokens, int outputTokens, decimal costUsd);
    void RecordWebhookReceived(string eventType);
    void RecordWebhookProcessingLag(double ms, string eventType);

    /// <summary>
    /// Records an airline-initiated change webhook that does not map to a domain
    /// event (i.e. anything other than the <c>.cancelled</c> subtype). Logged at
    /// Information by the handler; this counter makes the frequency observable
    /// for ops without parsing logs.
    /// </summary>
    void RecordAirlineInitiatedChange();
}
