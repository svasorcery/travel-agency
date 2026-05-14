using Wolverine;

namespace Travel.Host.Tests.Integration.Flights;

/// <summary>
/// A configurable <see cref="IMessageBus"/> test double. HTTP-pipeline tests register a
/// canned response per message type; the Wolverine.Http endpoints then run through the real
/// ASP.NET pipeline (routing, authentication, authorization, model binding) while the
/// downstream handlers are stubbed. Only <c>InvokeAsync&lt;T&gt;</c> is implemented — the
/// endpoints under test use nothing else.
/// </summary>
public sealed class FakeMessageBus : IMessageBus
{
    private readonly Dictionary<Type, object> _responses = new();

    /// <summary>Clears all configured responses (call between tests sharing one host).</summary>
    public void Reset() => _responses.Clear();

    /// <summary>Configures the response returned when a message of type <typeparamref name="TMessage"/> is invoked.</summary>
    public FakeMessageBus On<TMessage>(object response)
    {
        _responses[typeof(TMessage)] = response;
        return this;
    }

    private object Resolve(object message) =>
        _responses.TryGetValue(message.GetType(), out var response)
            ? response
            : throw new InvalidOperationException(
                $"FakeMessageBus: no response configured for {message.GetType().Name}."
            );

    public Task<T> InvokeAsync<T>(
        object message,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => Task.FromResult((T)Resolve(message));

    public Task<T> InvokeAsync<T>(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => Task.FromResult((T)Resolve(message));

    public Task InvokeAsync(
        object message,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    )
    {
        Resolve(message);
        return Task.CompletedTask;
    }

    public Task InvokeAsync(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    )
    {
        Resolve(message);
        return Task.CompletedTask;
    }

    // ── unused surface ──────────────────────────────────────────────────────────

    public string? TenantId { get; set; }

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

    public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options) =>
        throw new NotSupportedException();
}
