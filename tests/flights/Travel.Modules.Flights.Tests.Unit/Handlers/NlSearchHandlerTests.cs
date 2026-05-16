using System.Diagnostics;
using ErrorOr;
using Microsoft.Extensions.Logging;
using Shouldly;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Handlers.NlSearch;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.Errors;
using Wolverine;
using Xunit;

namespace Travel.Modules.Flights.Tests.Unit.Handlers;

/// <summary>
/// Unit tests for <see cref="NlSearchHandler" />.
/// All dependencies are hand-rolled fakes — no Wolverine runtime, no containers.
/// </summary>
public sealed class NlSearchHandlerTests
{
    private static readonly NlSearchParsed CannedParsed = new(
        CorrelationId: Guid.NewGuid(),
        Origin: "LED",
        Destination: "SVO",
        DepartureDate: new DateOnly(2026, 6, 15),
        ReturnDate: null,
        PassengerCount: 1,
        CabinClass: "economy",
        Currency: "RUB",
        InputTokens: 100,
        OutputTokens: 50,
        CostUsd: 0.005m,
        ModelId: "claude-opus-4-7"
    );

    // ── Cancellation_propagates ──────────────────────────────────────────────────

    [Fact]
    public async Task Cancellation_propagates()
    {
        // Arrange — bus throws OperationCanceledException (cancelled token path)
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var bus = new ThrowingBus(new OperationCanceledException(cts.Token));
        var metrics = new NullFlightsMetrics();
        var log = new CapturingLogger<NlSearchQuery>();

        // Act + Assert
        await Should.ThrowAsync<OperationCanceledException>(() =>
            NlSearchHandler.Handle(
                new NlSearchQuery("из Москвы в Питер"),
                bus,
                metrics,
                log,
                cts.Token
            )
        );
    }

    // ── Unexpected_failure_is_logged ─────────────────────────────────────────────

    [Fact]
    public async Task Unexpected_failure_is_logged()
    {
        // Arrange — bus throws an unexpected infrastructure exception
        var bus = new ThrowingBus(new InvalidOperationException("bus exploded"));
        var metrics = new NullFlightsMetrics();
        var log = new CapturingLogger<NlSearchQuery>();

        // Act
        var result = await NlSearchHandler.Handle(
            new NlSearchQuery("из Москвы в Питер"),
            bus,
            metrics,
            log,
            CancellationToken.None
        );

        // Assert — typed error returned (not an exception)
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe(FlightsErrors.NlSearchUnparseable.Code);

        // And the failure must have been logged at Warning or above
        log.HasWarningOrAbove.ShouldBeTrue(
            "NlSearchHandler must log unexpected failures at Warning/Error before swallowing them."
        );
    }

    // ── Nl_search_correlation_id_follows_trace ──────────────────────────────────

    [Fact]
    public async Task Nl_search_correlation_id_follows_trace()
    {
        // Arrange — start an activity so Activity.Current.TraceId is set
        using var source = new ActivitySource("test");
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = source.StartActivity("test-operation");
        activity.ShouldNotBeNull(
            "Activity.Current must be non-null for this test to be meaningful."
        );

        var expectedTraceId = activity.TraceId.ToString();

        NlSearchRequested? capturedRequest = null;
        var bus = new CapturingBus(
            CannedParsed,
            req =>
            {
                capturedRequest = req;
            }
        );
        var metrics = new NullFlightsMetrics();
        var log = new CapturingLogger<NlSearchQuery>();

        // Act
        await NlSearchHandler.Handle(
            new NlSearchQuery("из Москвы в Питер"),
            bus,
            metrics,
            log,
            CancellationToken.None
        );

        // Assert — the CorrelationId set on the request must equal the trace id, not a random GUID.
        // ActivityTraceId is 128 bits (16 bytes), same as Guid; format as "N" (no dashes) to compare.
        capturedRequest.ShouldNotBeNull();
        capturedRequest.CorrelationId.ToString("N").ShouldBe(expectedTraceId);
    }

    // ── Fake dependencies ────────────────────────────────────────────────────────

    /// <summary>Bus that always throws the given exception.</summary>
    private sealed class ThrowingBus(Exception ex) : IMessageBus
    {
        public string? TenantId { get; set; }

        public Task InvokeAsync(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => Task.FromException(ex);

        public Task InvokeAsync(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => Task.FromException(ex);

        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => Task.FromException<T>(ex);

        public Task<T> InvokeAsync<T>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => Task.FromException<T>(ex);

        public Task InvokeForTenantAsync(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotSupportedException();

        public Task<T> InvokeForTenantAsync<T>(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotSupportedException();

        public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) =>
            throw new NotSupportedException();

        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null) =>
            throw new NotSupportedException();

        public ValueTask BroadcastToTopicAsync(
            string topicName,
            object message,
            DeliveryOptions? options = null
        ) => throw new NotSupportedException();

        public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotSupportedException();

        public IDestinationEndpoint EndpointFor(string endpointName) =>
            throw new NotSupportedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message) =>
            throw new NotSupportedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(
            object message,
            DeliveryOptions options
        ) => throw new NotSupportedException();
    }

    /// <summary>Bus that captures the first NlSearchRequested and returns canned responses.</summary>
    private sealed class CapturingBus(NlSearchParsed parsed, Action<NlSearchRequested> onRequest)
        : IMessageBus
    {
        public string? TenantId { get; set; }

        private static readonly ErrorOr<SearchResult> EmptySearchResult =
            (ErrorOr<SearchResult>)new SearchResult([], []);

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
        )
        {
            if (message is NlSearchRequested req)
                onRequest(req);

            if (typeof(T) == typeof(NlSearchParsed))
                return Task.FromResult((T)(object)parsed);

            if (typeof(T) == typeof(ErrorOr<SearchResult>))
                return Task.FromResult((T)(object)EmptySearchResult);

            throw new InvalidOperationException($"Unexpected message type {typeof(T).Name}");
        }

        public Task<T> InvokeAsync<T>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => InvokeAsync<T>(message, cancellation, timeout);

        public Task InvokeForTenantAsync(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotSupportedException();

        public Task<T> InvokeForTenantAsync<T>(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotSupportedException();

        public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) =>
            throw new NotSupportedException();

        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null) =>
            throw new NotSupportedException();

        public ValueTask BroadcastToTopicAsync(
            string topicName,
            object message,
            DeliveryOptions? options = null
        ) => throw new NotSupportedException();

        public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotSupportedException();

        public IDestinationEndpoint EndpointFor(string endpointName) =>
            throw new NotSupportedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message) =>
            throw new NotSupportedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(
            object message,
            DeliveryOptions options
        ) => throw new NotSupportedException();
    }

    /// <summary>No-op IFlightsMetrics implementation for tests that don't care about metrics.</summary>
    private sealed class NullFlightsMetrics : IFlightsMetrics
    {
        public void RecordSearchLatency(double ms, string provider, string status) { }

        public void RecordSearchError(string provider) { }

        public void RecordPaymentOutcome(bool success) { }

        public void RecordAggregateEventsAppended(string eventType, long count = 1) { }

        public void RecordNlSearchUsage(int inputTokens, int outputTokens, decimal costUsd) { }

        public void RecordWebhookReceived(string eventType) { }

        public void RecordWebhookProcessingLag(double ms, string eventType) { }

        public void RecordAirlineInitiatedChange() { }

        public void RecordPaymentDuration(double ms, string outcome) { }

        public void RecordNlSearchDuration(double ms) { }

        public void RecordSearchPartialFill(bool partial) { }

        public void RecordOfferShown() { }

        public void RecordOrderBooked() { }
    }

    /// <summary>Logger that records whether any Warning-or-above message was emitted.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public bool HasWarningOrAbove { get; private set; }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel >= LogLevel.Warning)
                HasWarningOrAbove = true;
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;
    }
}
