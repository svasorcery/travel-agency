using Travel.Modules.Trips.Core;

namespace Travel.Modules.Trips.Api.Middleware;

public sealed class TripBriefOwnerAdmission(TimeProvider clock)
{
    private readonly object gate = new();
    private readonly Dictionary<OwnerIdentity, Queue<long>> requests = [];

    public bool TryAdmit(OwnerIdentity owner)
    {
        lock (gate)
        {
            var now = clock.GetTimestamp();
            foreach (var key in requests.Keys.ToArray())
            {
                var window = requests[key];
                while (
                    window.TryPeek(out var oldest)
                    && clock.GetElapsedTime(oldest, now) >= TimeSpan.FromMinutes(1)
                )
                    window.Dequeue();
                if (window.Count == 0)
                    requests.Remove(key);
            }
            if (!requests.TryGetValue(owner, out var entries))
            {
                if (requests.Count >= 4096)
                    return false;
                requests[owner] = entries = new();
            }
            if (entries.Count >= 6)
                return false;
            entries.Enqueue(now);
            return true;
        }
    }
}
