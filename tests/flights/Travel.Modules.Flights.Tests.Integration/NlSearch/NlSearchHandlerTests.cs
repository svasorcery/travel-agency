using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Handlers.NlSearch;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Wolverine;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.NlSearch;

public sealed class NlSearchHandlerTests
{
    private static readonly RecordingMetrics NoMetrics = new();

    private static NlSearchParsed ValidParsed(Guid correlationId) =>
        new(
            CorrelationId: correlationId,
            Origin: "LED",
            Destination: "DME",
            DepartureDate: new DateOnly(2026, 6, 15),
            ReturnDate: null,
            PassengerCount: 1,
            CabinClass: "economy",
            Currency: "RUB"
        );

    private static readonly SearchResult EmptyResult = new(
        Array.Empty<Offer>(),
        Array.Empty<ProviderFailure>()
    );

    // ─── Happy path ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ValidQuery_ReturnsMappedSearchResult()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var parsed = ValidParsed(correlationId);

        Func<object, Task<object?>> invoke = msg =>
            msg switch
            {
                NlSearchRequested => Task.FromResult<object?>(parsed),
                SearchFlightsQuery => Task.FromResult<object?>((ErrorOr<SearchResult>)EmptyResult),
                _ => throw new NotSupportedException(msg.GetType().Name),
            };

        var bus = new FakeMessageBus(invoke);
        var query = new NlSearchQuery("LED DME 15 Jun");

        // Act
        var result = await NlSearchHandler.Handle(query, bus, NoMetrics, CancellationToken.None);

        // Assert
        result.IsError.ShouldBeFalse();
        result.Value.ShouldBe(EmptyResult);
    }

    // ─── Metrics ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_ValidQuery_RecordsNlSearchUsageFromParsedReply()
    {
        // Arrange — Travel.AI reports model usage on the NlSearchParsed reply.
        var correlationId = Guid.NewGuid();
        var parsed = ValidParsed(correlationId) with
        {
            InputTokens = 100,
            OutputTokens = 50,
            CostUsd = 0.00525m,
            ModelId = "claude-opus-4-7",
        };

        Func<object, Task<object?>> invoke = msg =>
            msg switch
            {
                NlSearchRequested => Task.FromResult<object?>(parsed),
                SearchFlightsQuery => Task.FromResult<object?>((ErrorOr<SearchResult>)EmptyResult),
                _ => throw new NotSupportedException(msg.GetType().Name),
            };

        var bus = new FakeMessageBus(invoke);
        var metrics = new RecordingMetrics();
        var query = new NlSearchQuery("LED DME 15 Jun");

        // Act
        await NlSearchHandler.Handle(query, bus, metrics, CancellationToken.None);

        // Assert — usage forwarded to the flights.nl_search.* metric
        metrics.NlSearchUsage.ShouldHaveSingleItem();
        metrics.NlSearchUsage[0].ShouldBe((100, 50, 0.00525m));
    }

    // ─── Timeout from AI ────────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_NlSearchTimeout_ReturnsNlSearchUnparseable()
    {
        // Arrange
        Func<object, Task<object?>> invoke = msg =>
            msg is NlSearchRequested
                ? throw new TimeoutException("AI timed out")
                : throw new NotSupportedException();

        var bus = new FakeMessageBus(invoke);
        var query = new NlSearchQuery("хочу на море");

        // Act
        var result = await NlSearchHandler.Handle(query, bus, NoMetrics, CancellationToken.None);

        // Assert
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe(FlightsErrors.NlSearchUnparseable.Code);
    }

    // ─── Bad IATA from AI ───────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_InvalidIataInParsed_ReturnsValidationError()
    {
        // Arrange — AI returns "XX" as origin (invalid: only 2 chars)
        var badParsed = new NlSearchParsed(
            CorrelationId: Guid.NewGuid(),
            Origin: "XX",
            Destination: "DME",
            DepartureDate: new DateOnly(2026, 6, 15),
            ReturnDate: null,
            PassengerCount: 1,
            CabinClass: "economy",
            Currency: "RUB"
        );

        Func<object, Task<object?>> invoke = msg =>
            msg is NlSearchRequested
                ? Task.FromResult<object?>(badParsed)
                : throw new NotSupportedException();

        var bus = new FakeMessageBus(invoke);
        var query = new NlSearchQuery("some query");

        // Act
        var result = await NlSearchHandler.Handle(query, bus, NoMetrics, CancellationToken.None);

        // Assert
        result.IsError.ShouldBeTrue();
        result.FirstError.Type.ShouldBe(ErrorType.Validation);
    }

    // ─── Generic exception from AI ───────────────────────────────────────────────

    [Fact]
    public async Task Handle_NlSearchGenericException_ReturnsNlSearchUnparseable()
    {
        // Arrange
        Func<object, Task<object?>> invoke = _ =>
            throw new InvalidOperationException("AI unavailable");

        var bus = new FakeMessageBus(invoke);
        var query = new NlSearchQuery("anything");

        // Act
        var result = await NlSearchHandler.Handle(query, bus, NoMetrics, CancellationToken.None);

        // Assert
        result.IsError.ShouldBeTrue();
        result.FirstError.Code.ShouldBe(FlightsErrors.NlSearchUnparseable.Code);
    }

    // ─── Fake IMessageBus ───────────────────────────────────────────────────────

    /// <summary>
    /// Minimal IMessageBus stub backed by a delegate so we only override what the handler uses.
    /// </summary>
    private sealed class FakeMessageBus(Func<object, Task<object?>> invokeFunc) : IMessageBus
    {
        public string? TenantId { get; set; }

        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        )
        {
            var result = invokeFunc(message).GetAwaiter().GetResult();
            return Task.FromResult((T)result!);
        }

        public Task InvokeAsync(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => invokeFunc(message);

        public Task InvokeAsync(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => invokeFunc(message);

        public Task<T> InvokeAsync<T>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        )
        {
            var result = invokeFunc(message).GetAwaiter().GetResult();
            return Task.FromResult((T)result!);
        }

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

    // ─── Recording IFlightsMetrics ──────────────────────────────────────────────

    /// <summary>
    /// Captures <see cref="RecordNlSearchUsage"/> calls; every other instrument is a no-op.
    /// </summary>
    private sealed class RecordingMetrics : IFlightsMetrics
    {
        public List<(int Input, int Output, decimal Cost)> NlSearchUsage { get; } = [];

        public void RecordNlSearchUsage(int inputTokens, int outputTokens, decimal costUsd) =>
            NlSearchUsage.Add((inputTokens, outputTokens, costUsd));

        public void RecordSearchLatency(double elapsedMs, string provider, string status) { }

        public void RecordSearchError(string provider) { }

        public void RecordPaymentOutcome(bool success) { }

        public void RecordAggregateEventsAppended(string eventType, long count = 1) { }

        public void RecordWebhookReceived(string eventType) { }

        public void RecordWebhookProcessingLag(double ms, string eventType) { }

        public void RecordAirlineInitiatedChange() { }
    }
}
