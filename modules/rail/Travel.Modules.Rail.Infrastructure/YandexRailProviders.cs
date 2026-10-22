using System.Text.Json;
using ErrorOr;
using Travel.Modules.Rail.Application;
using Travel.Modules.Rail.Core;

namespace Travel.Modules.Rail.Infrastructure;

public sealed class YandexRailReferenceLoader(YandexRailClient client, TimeProvider clock)
    : IRailReferenceLoader
{
    public async Task<ErrorOr<ReferenceData>> LoadAsync(
        RailProfile profile,
        CancellationToken cancellationToken
    )
    {
        DateTimeOffset firstReceived;
        ErrorOr<RailAttribution> attribution;
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5), clock))
        using (
            var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                deadline.Token,
                profile.Revoked
            )
        )
        {
            var copyright = await client.GetAsync("copyright", "", profile, linked.Token);
            if (copyright.IsError)
                return copyright.Errors;
            using var message = copyright.Value;
            firstReceived = clock.GetUtcNow();
            var bytes = await YandexRailClient.ReadBoundedAsync(message, 128 * 1024, linked.Token);
            if (bytes.IsError)
                return bytes.Errors;
            attribution = YandexRailAttributionParser.Parse(bytes.Value);
            if (attribution.IsError)
                return attribution.Errors;
        }
        var catalogue = await client.GetAsync(
            "stations_list",
            "&lang=ru_RU",
            profile,
            cancellationToken
        );
        if (catalogue.IsError)
            return catalogue.Errors;
        using var response = catalogue.Value;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var parsed = await YandexStationCatalogueParser.ParseAsync(stream, cancellationToken);
        return parsed.IsError
            ? parsed.Errors
            : new ReferenceData(parsed.Value, attribution.Value, firstReceived);
    }
}

public sealed class YandexRailScheduleProvider(YandexRailClient client, TimeProvider clock)
    : IRailScheduleProvider
{
    public async Task<ModeResult> SearchAsync(
        RailRequest request,
        string mode,
        RailProfile profile,
        CancellationToken cancellationToken
    )
    {
        var query =
            $"&system=yandex&lang=ru_RU&transfers=false&from={Uri.EscapeDataString(request.From.Code)}&to={Uri.EscapeDataString(request.To.Code)}&date={request.DepartureDate}&transport_types={mode}&offset=0&limit=50";
        var result = await client.GetAsync("search", query, profile, cancellationToken);
        if (result.IsError)
            return ModeResult.Unavailable(
                mode,
                result.FirstError.Code is "source-disabled" or "quota" ? "skipped" : "failed",
                result.FirstError.Code
            );
        using var response = result.Value;
        var received = clock.GetUtcNow();
        var bytes = await YandexRailClient.ReadBoundedAsync(
            response,
            2 * 1024 * 1024,
            cancellationToken
        );
        return bytes.IsError
            ? ModeResult.Unavailable(mode, "failed", bytes.FirstError.Code)
            : YandexRailScheduleParser.Parse(bytes.Value, request, mode, received);
    }
}

public sealed class RailSearchCache : IRailSearchCache, IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<
        string,
        (RailSearchResult Value, int Bytes, LinkedListNode<string> Node)
    > entries = new(StringComparer.Ordinal);
    private readonly LinkedList<string> order = [];
    private readonly ITimer timer;
    private long bytes;
    private readonly TimeProvider clock;
    private readonly IRailRuntimeProfile? profiles;
    private CancellationTokenRegistration revoked;
    private string? epoch;
    private bool disposed;

    public RailSearchCache(TimeProvider clock, IRailRuntimeProfile? profiles = null)
    {
        this.clock = clock;
        this.profiles = profiles;
        timer = clock.CreateTimer(
            _ =>
            {
                lock (gate)
                {
                    if (disposed)
                        return;
                    Prune(clock.GetUtcNow());
                    Schedule(clock.GetUtcNow());
                }
            },
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan
        );
    }

    public RailSearchResult? Get(string key, DateTimeOffset now)
    {
        lock (gate)
        {
            if (disposed)
                return null;
            Prune(now);
            Schedule(now);
            return entries.TryGetValue(key, out var entry) ? entry.Value : null;
        }
    }

    public void Set(string key, RailSearchResult result)
    {
        if (result.Kind != "complete" || result.ExpiresAt is null)
            return;
        var size = JsonSerializer.SerializeToUtf8Bytes(result).Length;
        if (size > 256 * 1024)
            return;
        lock (gate)
        {
            if (disposed || result.ExpiresAt <= clock.GetUtcNow())
                return;
            if (profiles is not null)
            {
                var current = profiles.Current;
                if (
                    !current.Enabled
                    || current.Epoch != result.ProfileEpoch
                    || current.Revoked.IsCancellationRequested
                )
                    return;
                if (epoch != current.Epoch)
                {
                    Clear();
                    revoked.Unregister();
                    epoch = current.Epoch;
                    revoked = current.Revoked.Register(Clear);
                }
                if (
                    current.Revoked.IsCancellationRequested
                    || profiles.Current.Epoch != current.Epoch
                )
                    return;
            }
            Prune(clock.GetUtcNow());
            Remove(key);
            while (entries.Count >= 128 || bytes + size > 16 * 1024 * 1024)
                Remove(order.First!.Value);
            entries.Add(key, (result, size, order.AddLast(key)));
            bytes += size;
            Schedule(clock.GetUtcNow());
        }
    }

    public bool RemoveIfSame(string key, RailSearchResult captured)
    {
        lock (gate)
        {
            if (
                disposed
                || !entries.TryGetValue(key, out var entry)
                || !ReferenceEquals(entry.Value, captured)
            )
                return false;
            Remove(key);
            Schedule(clock.GetUtcNow());
            return true;
        }
    }

    private void Schedule(DateTimeOffset now)
    {
        if (disposed)
            return;
        if (entries.Count == 0)
        {
            timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }
        var due = entries.Values.Min(x => x.Value.ExpiresAt!.Value) - now;
        timer.Change(due < TimeSpan.Zero ? TimeSpan.Zero : due, Timeout.InfiniteTimeSpan);
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (
            var key in entries
                .Where(x => x.Value.Value.ExpiresAt <= now)
                .Select(x => x.Key)
                .ToArray()
        )
            Remove(key);
    }

    private void Remove(string key)
    {
        if (entries.Remove(key, out var entry))
        {
            bytes -= entry.Bytes;
            order.Remove(entry.Node);
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            entries.Clear();
            order.Clear();
            bytes = 0;
            if (!disposed)
                timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            timer.Dispose();
            revoked.Unregister();
            Clear();
        }
    }
}
