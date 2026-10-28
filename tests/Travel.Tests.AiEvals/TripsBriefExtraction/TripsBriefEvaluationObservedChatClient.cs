using Microsoft.Extensions.AI;

namespace Travel.Tests.AiEvals.TripsBriefExtraction;

public sealed class TripsBriefEvaluationObservedChatClient(
    IChatClient inner,
    TripsBriefEvaluationSchedule sharedSchedule
) : IChatClient
{
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        sharedSchedule.RecordProviderStart();
        return inner.GetResponseAsync(messages, options, cancellationToken);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => throw new NotSupportedException("SingleResponseOnly");

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() => inner.Dispose();
}
