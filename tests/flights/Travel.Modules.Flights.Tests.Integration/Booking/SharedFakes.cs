using Marten;
using Travel.Modules.Flights.Application.Observability;
using Wolverine;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Tests.Integration.Booking;

/// <summary>
/// Test-only <see cref="IMartenOutbox"/> implementation that records every published
/// message in-memory. Used by the booking handler tests in lieu of a real Wolverine
/// host; the booking handlers reach for <c>IMartenOutbox.PublishAsync</c> directly
/// so we don't need to spin up the full bus to assert outgoing-message intent.
/// </summary>
public sealed class RecordingMartenOutbox : IMartenOutbox
{
    public List<object> Published { get; } = new();

    public IDocumentSession Session { get; private set; } = default!;

    public void Enroll(IDocumentSession session) => Session = session;

    public string? TenantId { get; set; }

    public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
    {
        Published.Add(message!);
        return ValueTask.CompletedTask;
    }

    public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) =>
        ValueTask.CompletedTask;

    public ValueTask BroadcastToTopicAsync(
        string topicName,
        object message,
        DeliveryOptions? options = null
    ) => ValueTask.CompletedTask;

    public IDestinationEndpoint EndpointFor(string endpointName) =>
        throw new NotImplementedException();

    public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotImplementedException();

    public Task InvokeAsync(
        object message,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => Task.CompletedTask;

    public Task InvokeAsync(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => Task.CompletedTask;

    public Task<T> InvokeAsync<T>(
        object message,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => Task.FromResult(default(T)!);

    public Task<T> InvokeAsync<T>(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => Task.FromResult(default(T)!);

    public Task InvokeForTenantAsync(
        string tenantId,
        object message,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => Task.CompletedTask;

    public Task<T> InvokeForTenantAsync<T>(
        string tenantId,
        object message,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => Task.FromResult(default(T)!);

    public IReadOnlyList<Envelope> PreviewSubscriptions(object message) => Array.Empty<Envelope>();

    public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options) =>
        Array.Empty<Envelope>();
}

/// <summary>
/// Test-only <see cref="IFlightsMetrics"/> that ignores every call. Used by the
/// booking handler tests where metrics are not under test.
/// </summary>
public sealed class NullFlightsMetricsImpl : IFlightsMetrics
{
    public static readonly NullFlightsMetricsImpl Instance = new();

    public void RecordSearchLatency(double elapsedMs, string provider, string status) { }

    public void RecordSearchError(string provider) { }

    public void RecordPaymentOutcome(bool success) { }

    public void RecordAggregateEventsAppended(string eventType, long count = 1) { }

    public void RecordNlSearchUsage(int inputTokens, int outputTokens, decimal costUsd) { }

    public void RecordWebhookReceived(string eventType) { }

    public void RecordWebhookProcessingLag(double ms, string eventType) { }

    public void RecordAirlineInitiatedChange() { }

    public void RecordPaymentDuration(double ms, string outcome) { }

    public void RecordNlSearchDuration(double ms) { }
}
