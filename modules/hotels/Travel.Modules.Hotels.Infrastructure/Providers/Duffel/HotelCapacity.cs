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

public sealed class HotelCapacity(IOptions<HotelsOptions> options, TimeProvider time)
    : IHotelCapacity
{
    private readonly object gate = new();
    private DateTimeOffset window;
    private readonly int[] starts = [0, 0];
    private readonly int[] active = [0, 0];
    private DateTimeOffset blockedUntil;
    public int TotalStarts { get; private set; }
    public int MaxActive { get; private set; }
    public int Active
    {
        get
        {
            lock (gate)
                return active.Sum();
        }
    }

    public HotelCapacitySnapshot GetSnapshot()
    {
        lock (gate)
            return new(window, starts[0], starts[1], active[0], active[1], TotalStarts, MaxActive);
    }

    public IDisposable? TryAcquire(string provider, bool recovery = false)
    {
        lock (gate)
        {
            var now = time.GetUtcNow();
            var settings = options.Value;
            if (now >= window)
            {
                Array.Clear(starts);
                window = now.AddSeconds(settings.WindowSeconds);
            }
            var lane = recovery ? 1 : 0;
            if (
                now < blockedUntil
                || starts[lane] >= (recovery ? settings.RecoveryStarts : settings.ForegroundStarts)
                || active[lane]
                    >= (recovery ? settings.RecoveryConcurrency : settings.ForegroundConcurrency)
            )
                return null;
            starts[lane]++;
            active[lane]++;
            TotalStarts++;
            MaxActive = Math.Max(MaxActive, active.Sum());
            return new Lease(() =>
            {
                lock (gate)
                    active[lane]--;
            });
        }
    }

    public void TightenUntil(DateTimeOffset until)
    {
        lock (gate)
        {
            if (until > blockedUntil)
                blockedUntil = until;
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
