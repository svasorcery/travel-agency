namespace Travel.AI.NlSearch;

// Per-process rolling limits. Unknown usage retains its full reservation; restart/replicas
// do not provide a distributed or provider-billing guarantee.
public sealed class NlSearchAdmission
{
    private readonly object gate = new();
    private readonly Queue<Lease> admitted = new();
    private readonly TimeProvider time;
    private int active;
    public NlSearchOptions Options { get; }

    public NlSearchAdmission(NlSearchOptions options, TimeProvider time)
    {
        options.Validate();
        Options = options;
        this.time = time;
    }

    public Lease? TryEnter(decimal upperEstimate)
    {
        lock (gate)
        {
            var now = time.GetUtcNow();
            while (admitted.TryPeek(out var first) && now - first.At >= TimeSpan.FromHours(1))
                admitted.Dequeue();
            if (
                upperEstimate < 0
                || upperEstimate > Options.MaxCostUsd
                || active >= Options.MaxConcurrentRequests
                || admitted.Count(item => now - item.At < TimeSpan.FromMinutes(1))
                    >= Options.RequestsPerMinute
                || admitted.Sum(item => item.Charged) + Options.MaxCostUsd > Options.HourlyBudgetUsd
            )
                return null;
            var lease = new Lease(this, now, Options.MaxCostUsd);
            admitted.Enqueue(lease);
            active++;
            return lease;
        }
    }

    public sealed class Lease : IDisposable
    {
        private readonly NlSearchAdmission owner;
        private Task? pending;
        private int disposed;
        internal DateTimeOffset At { get; }
        internal decimal Charged { get; private set; }

        internal Lease(NlSearchAdmission owner, DateTimeOffset at, decimal reserved)
        {
            this.owner = owner;
            At = at;
            Charged = reserved;
        }

        public void Settle(decimal? knownCost)
        {
            if (knownCost is not { } cost)
                return;
            lock (owner.gate)
                Charged = Math.Max(0, cost);
        }

        public void DeferUntil(Task task) => pending = task;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;
            var task = pending;
            if (task is not null && !task.IsCompleted)
                _ = task.ContinueWith(
                    completed =>
                    {
                        _ = completed.Exception;
                        lock (owner.gate)
                            owner.active--;
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                );
            else
            {
                _ = task?.Exception;
                lock (owner.gate)
                    owner.active--;
            }
        }
    }
}
