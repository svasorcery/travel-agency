namespace Travel.AI.TripsBriefExtraction;

// Per-process protection only. Restarts/replicas do not enforce an account or invoice ceiling.
public sealed class TripsBriefAdmission(TripsBriefOptions options, TimeProvider time)
{
    private readonly object gate = new();
    private readonly Queue<Lease> reservations = new();
    private bool active;

    public Lease? TryEnter(decimal upperBound)
    {
        lock (gate)
        {
            var now = time.GetUtcNow();
            while (reservations.TryPeek(out var first) && now - first.At >= TimeSpan.FromHours(1))
                reservations.Dequeue();
            if (
                !options.IsConfigured
                || active
                || upperBound < 0
                || upperBound > options.MaxRequestCostUsd
                || reservations.Count(r => now - r.At < TimeSpan.FromMinutes(1)) >= 6
                || reservations.Sum(r => r.Charged) + options.MaxRequestCostUsd
                    > options.MaxHourlyCostUsd
            )
                return null;
            var lease = new Lease(this, now, options.MaxRequestCostUsd);
            reservations.Enqueue(lease);
            active = true;
            return lease;
        }
    }

    public sealed class Lease : IDisposable
    {
        private readonly TripsBriefAdmission owner;
        private readonly List<Task> pending = new();
        private int disposed;
        internal DateTimeOffset At { get; }
        internal decimal Charged { get; private set; }

        internal Lease(TripsBriefAdmission owner, DateTimeOffset at, decimal reservation)
        {
            this.owner = owner;
            At = at;
            Charged = reservation;
        }

        // Only confirmed, committed accounting can release money. Unknown keeps the full reservation.
        public void Settle(decimal? cost)
        {
            if (cost is >= 0)
                lock (owner.gate)
                    Charged = cost.Value;
        }

        public void DeferUntil(Task task) =>
            pending.Add(
                task.ContinueWith(
                    done =>
                    {
                        _ = done.Exception;
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                )
            );

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0)
                return;
            var task = Task.WhenAll(pending);
            if (!task.IsCompleted)
                _ = task.ContinueWith(
                    done =>
                    {
                        _ = done.Exception;
                        lock (owner.gate)
                            owner.active = false;
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                );
            else
            {
                _ = task.Exception;
                lock (owner.gate)
                    owner.active = false;
            }
        }
    }
}
