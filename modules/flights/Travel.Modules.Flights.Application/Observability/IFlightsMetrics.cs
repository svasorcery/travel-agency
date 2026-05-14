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
}
