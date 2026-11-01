using Microsoft.Extensions.Options;
using Travel.Modules.Hotels.Application;

namespace Travel.Modules.Hotels.Infrastructure.Providers.Duffel;

public sealed record HotelCapacitySnapshot(
    DateTimeOffset WindowUntil,
    int ForegroundStarts,
    int RecoveryStarts,
    int ForegroundActive,
    int RecoveryActive,
    int TotalStarts,
    int MaxActive
);

public sealed record HotelSourceCapacitySnapshot(
    string Provider,
    HotelCapacitySnapshot Capacity,
    int Releases
);

public sealed class HotelCapacity(IOptions<HotelsOptions> options, TimeProvider time)
    : IHotelCapacity
{
    private readonly object gate = new();

    private sealed class SourceBudget
    {
        public DateTimeOffset Window;
        public int[] Starts = [0, 0];
        public int[] Active = [0, 0];
        public DateTimeOffset BlockedUntil;
        public int TotalStarts;
        public int MaxActive;
    }

    private readonly Dictionary<string, SourceBudget> budgets = new(StringComparer.Ordinal)
    {
        ["duffel-stays"] = new(),
        ["hotelbeds"] = new(),
    };

    public int TotalStarts { get; private set; }
    public int MaxActive { get; private set; }
    public int Active
    {
        get
        {
            lock (gate)
                return budgets.Values.Sum(b => b.Active.Sum());
        }
    }

    public HotelCapacitySnapshot GetSnapshot()
    {
        lock (gate)
            return new(
                budgets.Values.Max(b => b.Window),
                budgets.Values.Sum(b => b.Starts[0]),
                budgets.Values.Sum(b => b.Starts[1]),
                budgets.Values.Sum(b => b.Active[0]),
                budgets.Values.Sum(b => b.Active[1]),
                TotalStarts,
                MaxActive
            );
    }

    // Bounded readonly observation for the Hotels load harness. A source's real window
    // cannot be reconstructed from the legacy aggregate Max(Window)/Sum(Starts).
    public HotelSourceCapacitySnapshot[] GetSourceSnapshots()
    {
        lock (gate)
            return budgets
                .Select(pair =>
                {
                    var budget = pair.Value;
                    var active = budget.Active.Sum();
                    return new HotelSourceCapacitySnapshot(
                        pair.Key,
                        new(
                            budget.Window,
                            budget.Starts[0],
                            budget.Starts[1],
                            budget.Active[0],
                            budget.Active[1],
                            budget.TotalStarts,
                            budget.MaxActive
                        ),
                        budget.TotalStarts - active
                    );
                })
                .ToArray();
    }

    public IDisposable? TryAcquire(string provider, bool recovery = false)
    {
        lock (gate)
        {
            if (!budgets.TryGetValue(provider, out var budget))
                return null;
            var now = time.GetUtcNow();
            var settings = options.Value;
            if (now >= budget.Window)
            {
                Array.Clear(budget.Starts);
                budget.Window = now.AddSeconds(settings.WindowSeconds);
            }
            var lane = recovery ? 1 : 0;
            if (
                now < budget.BlockedUntil
                || budget.Starts[lane]
                    >= (recovery ? settings.RecoveryStarts : settings.ForegroundStarts)
                || budget.Active[lane]
                    >= (recovery ? settings.RecoveryConcurrency : settings.ForegroundConcurrency)
            )
                return null;
            budget.Starts[lane]++;
            budget.Active[lane]++;
            budget.TotalStarts++;
            budget.MaxActive = Math.Max(budget.MaxActive, budget.Active.Sum());
            TotalStarts++;
            MaxActive = Math.Max(MaxActive, budgets.Values.Sum(b => b.Active.Sum()));
            return new Lease(() =>
            {
                lock (gate)
                    budget.Active[lane]--;
            });
        }
    }

    public void TightenUntil(DateTimeOffset until, string provider = "duffel-stays")
    {
        lock (gate)
        {
            if (budgets.TryGetValue(provider, out var budget) && until > budget.BlockedUntil)
                budget.BlockedUntil = until;
        }
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private int released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref released, 1) == 0)
                release();
        }
    }
}
