using System.Text.Json;
using ErrorOr;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Application;

public sealed record HotelSearchSnapshot(
    Guid Id,
    StaySearch Request,
    HotelExecution<HotelOffer> Result,
    ProviderStatus[] Statuses,
    DateTimeOffset ExpiresAt
);

public sealed record HotelRatesSnapshot(
    Guid Id,
    Guid SearchId,
    SelectedStay Selection,
    HotelExecution<HotelOffer> Result,
    DateTimeOffset ExpiresAt
);

// Stores complete immutable replies, including failure/unsupported/limited metadata.
public sealed class HotelSnapshotCache(
    TimeProvider time,
    int maxEntries = 128,
    int maxBytes = 16 * 1024 * 1024
)
{
    private sealed record Entry(object Snapshot, DateTimeOffset ExpiresAt, int Bytes, long Order);

    private readonly object gate = new();
    private readonly Dictionary<Guid, Entry> entries = [];
    private int bytes;
    private long nextOrder;
    public int Count
    {
        get
        {
            lock (gate)
            {
                Purge();
                return entries.Count;
            }
        }
    }
    public int PayloadBytes
    {
        get
        {
            lock (gate)
            {
                Purge();
                return bytes;
            }
        }
    }

    public bool Put(HotelSearchSnapshot snapshot) => Put(snapshot.Id, snapshot, snapshot.ExpiresAt);

    public bool Put(HotelRatesSnapshot snapshot) => Put(snapshot.Id, snapshot, snapshot.ExpiresAt);

    private bool Put(Guid id, object snapshot, DateTimeOffset expiry)
    {
        var size = JsonSerializer.SerializeToUtf8Bytes(snapshot, snapshot.GetType()).Length + 36;
        lock (gate)
        {
            Purge();
            if (maxEntries < 1 || size > maxBytes || expiry <= time.GetUtcNow())
                return false;
            Remove(id);
            while (entries.Count >= maxEntries || bytes + size > maxBytes)
                Remove(entries.MinBy(entry => entry.Value.Order).Key);
            entries.Add(id, new(snapshot, expiry, size, checked(++nextOrder)));
            bytes += size;
            return true;
        }
    }

    public HotelSearchSnapshot? Get(Guid id)
    {
        lock (gate)
        {
            Purge();
            return entries.GetValueOrDefault(id)?.Snapshot as HotelSearchSnapshot;
        }
    }

    public HotelRatesSnapshot? GetRates(Guid id)
    {
        lock (gate)
        {
            Purge();
            return entries.GetValueOrDefault(id)?.Snapshot as HotelRatesSnapshot;
        }
    }

    private void Purge()
    {
        foreach (
            var key in entries
                .Where(e => e.Value.ExpiresAt <= time.GetUtcNow())
                .Select(e => e.Key)
                .ToArray()
        )
            Remove(key);
    }

    private void Remove(Guid id)
    {
        if (entries.Remove(id, out var old))
            bytes -= old.Bytes;
    }
}

public sealed class HotelSearchService(
    IEnumerable<IHotelSearchProvider> sources,
    HotelSnapshotCache cache,
    TimeProvider time,
    HotelPolicy? policy = null
)
{
    private readonly HotelPolicy settings = policy ?? new();
    public static HotelArea[] Areas =>
        [
            new("london", 51.5074m, -0.1278m, 5),
            new("paris", 48.8566m, 2.3522m, 5),
            new("rome", 41.9028m, 12.4964m, 5),
            new("fictional", 51.5m, -0.1m, 5, true),
        ];

    public async Task<ErrorOr<HotelSearchSnapshot>> SearchAsync(
        StaySearch request,
        CancellationToken ct
    )
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        if (
            request.Dates.CheckIn <= today
            || request.Dates.CheckIn > today.AddDays(330)
            || request.Dates.Nights is < 1 or > 99
            || request.Area.RadiusKm is < 1 or > 20
            || request.Area.Latitude is < -90 or > 90
            || request.Area.Longitude is < -180 or > 180
            || !Areas.Contains(request.Area)
        )
            return HotelErrors.Invalid;
        var offers = new List<HotelOffer>();
        var statuses = new List<ProviderStatus>();
        foreach (var source in sources)
        {
            var support = source.GetSupport(request);
            var execution = support.Supported
                ? await source.SearchAsync(request, ct)
                : HotelExecution<HotelOffer>.Unsupported(support.Reason!);
            offers.AddRange(execution.Items);
            statuses.Add(new(source.Key, execution.Kind, execution.Reason));
        }
        var kind =
            statuses.Count == 0 || statuses.All(s => s.Kind == ExecutionKind.Unsupported)
                ? ExecutionKind.Unsupported
            : statuses.All(s => s.Kind == ExecutionKind.Complete) ? ExecutionKind.Complete
            : offers.Count > 0 ? ExecutionKind.Limited
            : statuses.Any(s => s.Kind == ExecutionKind.Failed) ? ExecutionKind.Failed
            : ExecutionKind.Limited;
        var reason =
            statuses.Count == 1 ? statuses[0].Reason
            : kind == ExecutionKind.Complete ? null
            : "SourcesIncomplete";
        var expiry = offers
            .Where(o => o.SourceExpiresAt is not null)
            .Select(o => o.SourceExpiresAt!.Value)
            .Append(time.GetUtcNow().AddSeconds(settings.SnapshotSeconds))
            .Min();
        var snapshot = new HotelSearchSnapshot(
            Guid.NewGuid(),
            request,
            new(kind, offers.ToArray(), reason),
            statuses.ToArray(),
            expiry
        );
        return cache.Put(snapshot) ? snapshot : HotelErrors.Unavailable;
    }

    public async Task<ErrorOr<HotelRatesSnapshot>> RatesAsync(
        Guid searchId,
        string selectionKey,
        CancellationToken ct
    )
    {
        var search = cache.Get(searchId);
        var offer = search?.Result.Items.SingleOrDefault(o => o.SelectionKey == selectionKey);
        if (search is null || offer is null)
            return HotelErrors.Expired;
        var source = sources.SingleOrDefault(s => s.Key == offer.Provider);
        if (source is null)
            return HotelErrors.Unsupported;
        var selection = new SelectedStay(search.Request, offer);
        var support = source.GetSupport(search.Request);
        var result = support.Supported
            ? await source.LoadRatesAsync(selection, ct)
            : HotelExecution<HotelOffer>.Unsupported(support.Reason!);
        var expiry = result
            .Items.Where(o => o.SourceExpiresAt is not null)
            .Select(o => o.SourceExpiresAt!.Value)
            .Append(search.ExpiresAt)
            .Min();
        var snapshot = new HotelRatesSnapshot(Guid.NewGuid(), searchId, selection, result, expiry);
        return cache.Put(snapshot) ? snapshot : HotelErrors.Unavailable;
    }

    public ErrorOr<SelectedStay> Select(Guid ratesId, string selectionKey)
    {
        var rates = cache.GetRates(ratesId);
        var offer = rates?.Result.Items.SingleOrDefault(o => o.SelectionKey == selectionKey);
        return rates is null || offer is null
            ? HotelErrors.Expired
            : new SelectedStay(rates.Selection.Request, offer);
    }
}
