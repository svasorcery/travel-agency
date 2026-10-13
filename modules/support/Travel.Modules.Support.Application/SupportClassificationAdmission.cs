namespace Travel.Modules.Support.Application;

/// <summary>Single-process admission; no queue or retained text and no claim that canceled remote compute stopped.</summary>
public sealed class SupportClassificationAdmission(TimeProvider time)
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, Window> actors = [];
    private readonly HashSet<(Guid Actor, Guid Case, int Revision)> active = [];
    private DateTimeOffset processStart;
    private int processAttempts;

    public IDisposable? TryAcquire(Guid actor, Guid caseId, int revision)
    {
        if (actor == Guid.Empty || caseId == Guid.Empty || revision <= 0)
            return null;
        lock (gate)
        {
            var now = time.GetUtcNow();
            if (now < processStart)
                return null;
            if (now - processStart >= TimeSpan.FromMinutes(1))
            {
                processStart = now;
                processAttempts = 0;
                foreach (
                    var id in actors
                        .Where(item => now - item.Value.Start >= TimeSpan.FromMinutes(1))
                        .Select(item => item.Key)
                        .ToArray()
                )
                    actors.Remove(id);
            }
            var key = (actor, caseId, revision);
            if (active.Count >= 2 || active.Contains(key) || processAttempts >= 12)
                return null;
            if (!actors.TryGetValue(actor, out var window))
            {
                if (actors.Count >= 1000)
                    return null;
                window = new(now);
                actors.Add(actor, window);
            }
            if (now < window.Start)
                return null;
            if (now - window.Start >= TimeSpan.FromMinutes(1))
            {
                window.Start = now;
                window.Attempts = 0;
            }
            if (window.Attempts >= 3)
                return null;
            window.Attempts++;
            processAttempts++;
            active.Add(key);
            return new Lease(this, key);
        }
    }

    private void Release((Guid Actor, Guid Case, int Revision) key)
    {
        lock (gate)
            active.Remove(key);
    }

    private sealed class Window(DateTimeOffset start)
    {
        public DateTimeOffset Start { get; set; } = start;
        public int Attempts { get; set; }
    }

    private sealed class Lease(
        SupportClassificationAdmission owner,
        (Guid Actor, Guid Case, int Revision) key
    ) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                owner.Release(key);
        }
    }
}
