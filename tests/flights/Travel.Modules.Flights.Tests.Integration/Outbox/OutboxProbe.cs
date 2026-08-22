using Wolverine.Attributes;

namespace Travel.Modules.Flights.Tests.Integration.Outbox;

/// <summary>
/// A test-only message used to prove that a Wolverine outbox actually delivers a
/// message after the surrounding store transaction commits. It is enqueued through a
/// transactional session/DbContext, never published directly, so its arrival at
/// <see cref="OutboxProbeHandler"/> is evidence the outbox flushed.
/// </summary>
public sealed record OutboxProbeMessage(Guid CorrelationId);

/// <summary>
/// Records every <see cref="OutboxProbeMessage"/> Wolverine delivers. Registered as a
/// singleton on the test host so a test can assert which correlation ids were handled.
/// </summary>
public sealed class OutboxProbeRecorder
{
    private readonly Dictionary<Guid, int> _handled = [];
    private readonly Lock _gate = new();

    public void Record(Guid correlationId)
    {
        lock (_gate)
        {
            _handled.TryGetValue(correlationId, out var count);
            _handled[correlationId] = count + 1;
        }
    }

    public bool WasHandled(Guid correlationId)
    {
        lock (_gate)
        {
            return _handled.ContainsKey(correlationId);
        }
    }

    public int HandledCount(Guid correlationId)
    {
        lock (_gate)
        {
            return _handled.GetValueOrDefault(correlationId);
        }
    }

    public int TotalHandledCount
    {
        get
        {
            lock (_gate)
            {
                return _handled.Values.Sum();
            }
        }
    }
}

public static class OutboxProbeHandler
{
    [WolverineHandler]
    public static void Handle(OutboxProbeMessage message, OutboxProbeRecorder recorder) =>
        recorder.Record(message.CorrelationId);
}
