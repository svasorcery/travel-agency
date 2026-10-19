using System.Text;
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
)
{
    public int SchemaVersion { get; init; } = 2;
    public HotelPropertyGroup[] Groups { get; init; } = [];
    public string RankingPolicyVersion { get; init; } = HotelCostRanking.Version;
};

public sealed record HotelRatesSnapshot(
    Guid Id,
    Guid SearchId,
    SelectedStay Selection,
    HotelExecution<HotelOffer> Result,
    DateTimeOffset ExpiresAt
)
{
    public int SchemaVersion { get; init; } = 2;
    public ProviderStatus[] Statuses { get; init; } = [];
    public HotelPropertyGroup[] Groups { get; init; } = [];
    public string RankingPolicyVersion { get; init; } = HotelCostRanking.Version;
};

// Stores complete immutable replies, including failure/unsupported/limited metadata.
public sealed class HotelSnapshotCache(
    TimeProvider time,
    int maxEntries = 128,
    int maxBytes = 16 * 1024 * 1024
)
{
    public HotelReadCoordinator Reads { get; } = new(time);

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
        var offers = snapshot switch
        {
            HotelSearchSnapshot search => search.Result.Items,
            HotelRatesSnapshot rates => rates.Result.Items.Append(rates.Selection.Offer).ToArray(),
            _ => [],
        };
        if (
            offers.Any(o =>
                o.SelectionContext is { } h && Encoding.UTF8.GetByteCount(h.Value) > 4096
            )
        )
            return false;
        // The handle lives on the same immutable leaf/entry, is ignored by public JSON, and is
        // accounted explicitly with its token/version/binding. Eviction removes the entire owner.
        var privateBytes = JsonSerializer
            .SerializeToUtf8Bytes(
                offers
                    .Where(o => o.SelectionContext is not null)
                    .Select(o => new
                    {
                        o.SelectionKey,
                        o.SelectionContext,
                        o.AdmissionBinding,
                    })
            )
            .Length;
        privateBytes += JsonSerializer
            .SerializeToUtf8Bytes(
                snapshot switch
                {
                    HotelSearchSnapshot search => search.Groups.Select(g => g.Mapping).ToArray(),
                    HotelRatesSnapshot rates => rates.Groups.Select(g => g.Mapping).ToArray(),
                    _ => Array.Empty<ReviewedHotelPair?>(),
                }
            )
            .Length;
        var size =
            JsonSerializer.SerializeToUtf8Bytes(snapshot, snapshot.GetType()).Length
            + privateBytes
            + 36;
        lock (gate)
        {
            Purge();
            if (
                maxEntries < 1
                || size > maxBytes
                || size > 2 * 1024 * 1024
                || expiry <= time.GetUtcNow()
                || expiry > time.GetUtcNow().AddSeconds(300)
            )
                return false;
            Remove(id);
            while (entries.Count >= maxEntries || bytes + size > maxBytes)
                Remove(entries.MinBy(entry => entry.Value.Order).Key);
            entries.Add(id, new(Freeze(snapshot), expiry, size, checked(++nextOrder)));
            bytes += size;
            return true;
        }
    }

    public HotelSearchSnapshot? Get(Guid id)
    {
        lock (gate)
        {
            Purge();
            return entries.GetValueOrDefault(id)?.Snapshot is HotelSearchSnapshot snapshot
                ? (HotelSearchSnapshot)Freeze(snapshot)
                : null;
        }
    }

    public HotelRatesSnapshot? GetRates(Guid id)
    {
        lock (gate)
        {
            Purge();
            return entries.GetValueOrDefault(id)?.Snapshot is HotelRatesSnapshot snapshot
                ? (HotelRatesSnapshot)Freeze(snapshot)
                : null;
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

    private static object Freeze(object snapshot)
    {
        var original = snapshot switch
        {
            HotelSearchSnapshot search => search.Result.Items,
            HotelRatesSnapshot rates => rates.Result.Items.Append(rates.Selection.Offer).ToArray(),
            _ => [],
        };
        var handles = original
            .GroupBy(o => o.SelectionKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var originals = snapshot switch
        {
            HotelSearchSnapshot search => search.Groups,
            HotelRatesSnapshot rates => rates.Groups,
            _ => [],
        };
        var mappings = originals.ToDictionary(g => g.Key, g => g.Mapping, StringComparer.Ordinal);
        HotelOffer Restore(HotelOffer offer) =>
            handles.TryGetValue(offer.SelectionKey, out var source)
                ? offer with
                {
                    SelectionContext = source.SelectionContext,
                    AdmissionBinding = source.AdmissionBinding,
                }
                : offer;
        HotelPropertyGroup[] RestoreGroups(HotelPropertyGroup[] groups) =>
            groups
                .Select(g =>
                    g with
                    {
                        Offers = g.Offers.Select(Restore).ToArray(),
                        Mapping = mappings.GetValueOrDefault(g.Key),
                    }
                )
                .ToArray();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, snapshot.GetType());
        if (snapshot is HotelSearchSnapshot)
        {
            var copy = JsonSerializer.Deserialize<HotelSearchSnapshot>(bytes)!;
            return copy with
            {
                Result = copy.Result with { Items = copy.Result.Items.Select(Restore).ToArray() },
                Groups = RestoreGroups(copy.Groups),
            };
        }
        var ratesCopy = JsonSerializer.Deserialize<HotelRatesSnapshot>(bytes)!;
        return ratesCopy with
        {
            Result = ratesCopy.Result with
            {
                Items = ratesCopy.Result.Items.Select(Restore).ToArray(),
            },
            Selection = ratesCopy.Selection with { Offer = Restore(ratesCopy.Selection.Offer) },
            Groups = RestoreGroups(ratesCopy.Groups),
        };
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
    HotelPolicy? policy = null,
    HotelReviewedMap? reviewedMap = null
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
        var freezeDeadline = time.GetUtcNow().AddSeconds(17);
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
        var selectedSources = sources.ToArray();
        var executions = await cache.Reads.RunAsync(
            selectedSources
                .Select<
                    IHotelSearchProvider,
                    Func<CancellationToken, Task<HotelExecution<HotelOffer>>>
                >(source =>
                    token =>
                    {
                        var support = source.GetSupport(request);
                        return support.Supported
                            ? source.SearchAsync(request, token)
                            : Task.FromResult(
                                HotelExecution<HotelOffer>.Unsupported(support.Reason!)
                            );
                    }
                )
                .ToArray(),
            ct
        );
        if (executions.IsError)
            return executions.Errors;
        for (var i = 0; i < selectedSources.Length; i++)
        {
            var source = selectedSources[i];
            var execution = executions.Value[i];
            offers.AddRange(execution.Items.Select(Issue));
            statuses.Add(Status(source.Key, execution));
        }
        offers = Normalize(offers.ToArray(), statuses).ToList();
        var kind = HotelAggregation.Kind(statuses);
        var reason =
            statuses.Count == 1 ? statuses[0].Reason
            : kind == ExecutionKind.Complete ? null
            : "SourcesIncomplete";
        var expiry = offers
            .Where(o => o.SourceExpiresAt is not null)
            .Select(o => o.SourceExpiresAt!.Value)
            .Append(time.GetUtcNow().AddSeconds(settings.SnapshotSeconds))
            .Min();
        var groups = BindGroups(
            HotelCostRanking.Rank(
                HotelPropertyMatching.Group(
                    offers.ToArray(),
                    reviewedMap?.Pairs ?? [],
                    time.GetUtcNow()
                )
            )
        );
        var snapshot = new HotelSearchSnapshot(
            Guid.NewGuid(),
            request,
            new(kind, groups.SelectMany(g => g.Offers).ToArray(), reason),
            statuses.ToArray(),
            expiry
        )
        {
            Groups = groups,
        };
        var bounded = Bound(groups);
        if (bounded.Length != groups.Length)
            snapshot = SearchGroups(snapshot, bounded, "AggregateResultLimit");
        ct.ThrowIfCancellationRequested();
        if (time.GetUtcNow() >= freezeDeadline)
            return HotelErrors.Unavailable;
        while (!cache.Put(snapshot))
        {
            if (snapshot.Groups.Length == 0)
                return HotelErrors.Unavailable;
            snapshot = SearchGroups(snapshot, snapshot.Groups[..^1], "AggregateByteLimit");
        }
        return time.GetUtcNow() >= freezeDeadline ? HotelErrors.Unavailable : snapshot;
    }

    public async Task<ErrorOr<HotelRatesSnapshot>> RatesAsync(
        Guid searchId,
        string? selectionKey,
        CancellationToken ct,
        string? propertyGroupKey = null
    )
    {
        var freezeDeadline = time.GetUtcNow().AddSeconds(17);
        if (string.IsNullOrWhiteSpace(selectionKey) == string.IsNullOrWhiteSpace(propertyGroupKey))
            return HotelErrors.Invalid;
        var search = cache.Get(searchId);
        if (search is null)
            return HotelErrors.Expired;
        var offers = propertyGroupKey is null
            ? search.Result.Items.Where(o => o.SelectionKey == selectionKey).ToArray()
            : search
                .Groups.SingleOrDefault(g => g.Key == propertyGroupKey)
                ?.Offers.DistinctBy(HotelPropertyMatching.Reference)
                .Take(2)
                .ToArray()
                ?? [];
        if (offers.Length == 0)
            return HotelErrors.Expired;
        var executions = await cache.Reads.RunAsync(
            offers
                .Select<HotelOffer, Func<CancellationToken, Task<HotelExecution<HotelOffer>>>>(
                    offer =>
                        token =>
                        {
                            var source = sources.SingleOrDefault(s => s.Key == offer.Provider);
                            if (source is null)
                                return Task.FromResult(
                                    HotelExecution<HotelOffer>.Unsupported("NotConfigured")
                                );
                            var support = source.GetSupport(search.Request);
                            return support.Supported
                                ? source.LoadRatesAsync(new(search.Request, offer), token)
                                : Task.FromResult(
                                    HotelExecution<HotelOffer>.Unsupported(support.Reason!)
                                );
                        }
                )
                .ToArray(),
            ct
        );
        if (executions.IsError)
            return executions.Errors;
        var statuses = executions.Value.Select((e, i) => Status(offers[i].Provider, e)).ToList();
        var normalized = Normalize(
            executions.Value.SelectMany(e => e.Items).Select(Issue).ToArray(),
            statuses
        );
        var groups = BindGroups(
            HotelCostRanking.Rank(
                HotelPropertyMatching.Group(normalized, reviewedMap?.Pairs ?? [], time.GetUtcNow())
            )
        );
        var kind = HotelAggregation.Kind(statuses);
        var result = new HotelExecution<HotelOffer>(
            kind,
            groups.SelectMany(g => g.Offers).ToArray(),
            statuses.Count == 1 ? statuses[0].Reason
                : kind == ExecutionKind.Complete ? null
                : "SourcesIncomplete"
        );
        var expiry = result
            .Items.Where(o => o.SourceExpiresAt is not null)
            .Select(o => o.SourceExpiresAt!.Value)
            .Append(search.ExpiresAt)
            .Min();
        var snapshot = new HotelRatesSnapshot(
            Guid.NewGuid(),
            searchId,
            new(search.Request, offers[0]),
            result,
            expiry
        )
        {
            Groups = groups,
            Statuses = statuses.ToArray(),
        };
        var bounded = Bound(groups);
        if (bounded.Length != groups.Length)
            snapshot = RatesGroups(snapshot, bounded, "AggregateResultLimit");
        ct.ThrowIfCancellationRequested();
        if (time.GetUtcNow() >= freezeDeadline)
            return HotelErrors.Unavailable;
        while (!cache.Put(snapshot))
        {
            if (snapshot.Groups.Length == 0)
                return HotelErrors.Unavailable;
            snapshot = RatesGroups(snapshot, snapshot.Groups[..^1], "AggregateByteLimit");
        }
        return time.GetUtcNow() >= freezeDeadline ? HotelErrors.Unavailable : snapshot;
    }

    public ErrorOr<SelectedStay> Select(Guid ratesId, string selectionKey)
    {
        var rates = cache.GetRates(ratesId);
        var offer = rates?.Result.Items.SingleOrDefault(o => o.SelectionKey == selectionKey);
        return rates is null || offer is null
            ? HotelErrors.Expired
            : new SelectedStay(rates.Selection.Request, offer);
    }

    private static HotelOffer Issue(HotelOffer offer) =>
        offer with
        {
            SelectionKey = Guid.NewGuid().ToString("N"),
            SelectionContext = offer.SelectionContext ?? new(0, offer.SelectionKey),
        };

    private static ProviderStatus Status(string key, HotelExecution<HotelOffer> execution) =>
        new(
            key,
            execution.Kind,
            execution.Kind == ExecutionKind.Complete && execution.Items.Length == 0
                ? "NoInventory"
                : execution.Reason,
            execution.Attempted,
            execution.Items.Length
        );

    private HotelOffer[] Normalize(HotelOffer[] offers, List<ProviderStatus> statuses)
    {
        void Limit(string provider, string reason)
        {
            var index = statuses.FindIndex(s => s.Provider == provider);
            if (index >= 0)
                statuses[index] = statuses[index] with
                {
                    Kind = ExecutionKind.Limited,
                    Reason = reason,
                };
        }
        var retained = new List<HotelOffer>();
        foreach (var offer in offers)
        {
            if (offer.SourceExpiresAt <= time.GetUtcNow())
            {
                Limit(offer.Provider, "Expired");
                continue;
            }
            if (
                offer.SelectionContext is { } context
                && Encoding.UTF8.GetByteCount(context.Value) > 4096
            )
            {
                Limit(offer.Provider, "ContextTooLarge");
                continue;
            }
            retained.Add(offer);
        }
        return retained
            .GroupBy(o => new
            {
                Property = HotelPropertyMatching.Reference(o),
                Room = o.RoomIdentity ?? o.Terms.Room,
                Key = o.SelectionContext?.Value ?? o.Terms.RateId,
            })
            .SelectMany(group =>
            {
                var first = group.First();
                var facts = group
                    .Select(HotelCostRanking.MaterialFacts)
                    .Distinct(StringComparer.Ordinal)
                    .Take(2)
                    .Count();
                if (facts > 1)
                {
                    Limit(first.Provider, "Conflict");
                    return Array.Empty<HotelOffer>();
                }
                return new[] { first };
            })
            .ToArray();
    }

    private static HotelPropertyGroup[] Bound(HotelPropertyGroup[] groups)
    {
        var retained = new List<HotelPropertyGroup>();
        var leaves = 0;
        foreach (var group in groups)
        {
            if (retained.Count >= 100 || leaves + group.Offers.Length > 200)
                break;
            retained.Add(group);
            leaves += group.Offers.Length;
        }
        return retained.ToArray();
    }

    private static HotelSearchSnapshot SearchGroups(
        HotelSearchSnapshot snapshot,
        HotelPropertyGroup[] groups,
        string reason
    ) =>
        snapshot with
        {
            Groups = groups,
            Result = snapshot.Result with
            {
                Kind = ExecutionKind.Limited,
                Reason = reason,
                Items = groups.SelectMany(g => g.Offers).ToArray(),
            },
        };

    private static HotelRatesSnapshot RatesGroups(
        HotelRatesSnapshot snapshot,
        HotelPropertyGroup[] groups,
        string reason
    ) =>
        snapshot with
        {
            Groups = groups,
            Result = snapshot.Result with
            {
                Kind = ExecutionKind.Limited,
                Reason = reason,
                Items = groups.SelectMany(g => g.Offers).ToArray(),
            },
        };

    private static HotelPropertyGroup[] BindGroups(HotelPropertyGroup[] groups) =>
        groups
            .Select(g =>
                g.Mapping is not { } mapping
                    ? g
                    : g with
                    {
                        Offers = g
                            .Offers.Select(o =>
                                o.AdmissionBinding is not { } binding
                                    ? o
                                    : o with
                                    {
                                        AdmissionBinding = binding with
                                        {
                                            MappingPair = mapping.Id,
                                            MappingRevision = mapping.Revision,
                                            MappingEvidenceDigest =
                                                HotelPropertyMatching.MappingDigest(mapping),
                                            ValidUntil =
                                                binding.ValidUntil < mapping.ValidUntil
                                                    ? binding.ValidUntil
                                                    : mapping.ValidUntil,
                                        },
                                    }
                            )
                            .ToArray(),
                    }
            )
            .ToArray();
}
