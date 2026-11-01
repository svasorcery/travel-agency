using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Travel.Modules.Hotels.Application;
using Travel.Modules.Hotels.Core;

namespace Travel.Modules.Hotels.Tests.Integration;

public sealed record HotelM2CacheProof(
    int EntryCap,
    int ByteCapEntries,
    int ByteCapBytes,
    int TimedStartEntries,
    int TimedStartBytes,
    int SmallEntryBytes,
    int LargeEntryBytes,
    string SmallSpecimenHash,
    string LargeSpecimenHash
);

public static class HotelM2LoadCacheProof
{
    // A separate synthetic cache dataset, never a public S/L offer. Complete typed
    // private handles/bindings are byte-accounted; a separate alias and Purchasable=false
    // prevent treating storage-bound proof entries as available purchase inventory.
    public static HotelM2CacheProof Run(
        HotelM2LoadProfile profile,
        HotelSnapshotCache cache,
        HotelM2SeedResult seed,
        TimeProvider clock
    )
    {
        if (cache.Count != 0)
            throw new InvalidOperationException("Cache proof requires untouched initial cache.");
        var now = clock.GetUtcNow();
        var small = Snapshot(profile, seed, now, false, 0);
        if (!cache.Put(small))
            throw new InvalidOperationException("Small cache specimen refused.");
        var smallBytes = cache.PayloadBytes;
        // The first specimen is reused as the first of exactly140 small inserts.
        for (var i = 1; i < 140; i++)
            Put(false, i);
        var entryCap = cache.Count;
        var large = Snapshot(profile, seed, now, true, 0);
        var before = cache.PayloadBytes;
        if (!cache.Put(large))
            throw new InvalidOperationException("Large cache specimen refused.");
        var largeBytes = cache.PayloadBytes - before + smallBytes;
        for (var i = 1; i < 140; i++)
            Put(true, i);
        var byteCapEntries = cache.Count;
        var byteCapBytes = cache.PayloadBytes;
        for (var i = 140; i < 280; i++)
            Put(false, i);
        if (
            entryCap != 128
            || byteCapEntries >= 128
            || byteCapBytes > 16 * 1024 * 1024
            || cache.Count != 128
            || smallBytes <= 0
            || largeBytes <= 0
            || largeBytes > 2 * 1024 * 1024
        )
            throw new InvalidOperationException("M2 complete-entry cache bound proof failed.");
        return new(
            entryCap,
            byteCapEntries,
            byteCapBytes,
            cache.Count,
            cache.PayloadBytes,
            smallBytes,
            largeBytes,
            SpecimenHash(small),
            SpecimenHash(large)
        );
        void Put(bool isLarge, int index)
        {
            if (!cache.Put(Snapshot(profile, seed, now, isLarge, index)))
                throw new InvalidOperationException("Valid synthetic cache entry refused.");
        }
    }

    private static HotelSearchSnapshot Snapshot(
        HotelM2LoadProfile profile,
        HotelM2SeedResult seed,
        DateTimeOffset now,
        bool large,
        int entry
    )
    {
        var count = large ? 100 : 1;
        var catalog = new HotelM2LoadProfile("L", profile.CheckIn);
        var left = new HotelSourceIdentity(
            "duffel-stays",
            "cache-proof-duffel",
            "Test",
            "Fictional"
        );
        var right = new HotelSourceIdentity(
            "hotelbeds",
            "cache-proof-hotelbeds",
            "Test",
            "Fictional"
        );
        var mappings = catalog.Map(left, right).Pairs.Take(count).ToArray();
        var offers = new List<HotelOffer>();
        var groups = new List<HotelPropertyGroup>();
        for (var index = 1; index <= count; index++)
        {
            var pair = mappings[index - 1];
            var pairOffers = new List<HotelOffer>();
            foreach (var source in new[] { "duffel-stays", "hotelbeds" })
            {
                var template = seed.Templates[source];
                var identity = source == "duffel-stays" ? left : right;
                var property = catalog.Property(source, index);
                var binding = template.Binding with
                {
                    Source = identity,
                    MappingPair = pair.Id,
                    MappingRevision = pair.Revision,
                    MappingEvidenceDigest = HotelPropertyMatching.MappingDigest(pair),
                    ValidUntil = now.AddMinutes(5),
                };
                var key = profile.Id($"cache-{large}:{source}:{entry}", index).ToString("N");
                ProviderContextHandle context;
                if (source == "hotelbeds")
                {
                    var raw =
                        $"cache-proof-{index:000}-" + new string('x', (large ? 2048 : 64) - 16);
                    context = new(
                        2,
                        JsonSerializer.Serialize(
                            new
                            {
                                source = identity,
                                rateKey = raw,
                                credentialIdentity = binding.TransportCredentialFingerprint,
                            },
                            new JsonSerializerOptions(JsonSerializerDefaults.Web)
                        )
                    );
                }
                else
                    context = template.Context;
                var terms = template.Terms with
                {
                    Property = property,
                    Occupancy = new(profile.Adults),
                };
                var offer = new HotelOffer(
                    source,
                    "Test",
                    key,
                    terms,
                    now.AddMinutes(5),
                    false,
                    "CacheProofOnly"
                )
                {
                    Source = identity,
                    SelectionContext = context,
                    AdmissionBinding = binding,
                    Price = new("SupplierCost", terms.Total, null, "WholeStay", source, [], []),
                };
                pairOffers.Add(offer);
                offers.Add(offer);
            }
            groups.Add(
                new(
                    $"cache-pair-{index:000}",
                    pair.Left,
                    pairOffers[0].Terms.Property,
                    pairOffers.ToArray(),
                    "Reviewed",
                    pair
                )
            );
        }
        return new(
            profile.Id("cache-" + (large ? "large" : "small"), entry),
            new(
                HotelSearchService.Areas[3],
                new(profile.CheckIn, profile.CheckOut),
                new(profile.Adults)
            ),
            new(ExecutionKind.Complete, offers.ToArray()),
            [
                new("duffel-stays", ExecutionKind.Complete, null),
                new("hotelbeds", ExecutionKind.Complete, null),
            ],
            now.AddMinutes(5)
        )
        {
            Groups = groups.ToArray(),
        };
    }

    private static string SpecimenHash(HotelSearchSnapshot snapshot) =>
        Convert.ToHexString(
            SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        snapshot,
                        handles = snapshot.Result.Items.Select(o => new
                        {
                            o.SelectionKey,
                            o.SelectionContext,
                            o.AdmissionBinding,
                        }),
                        mappings = snapshot.Groups.Select(g => g.Mapping),
                    }
                )
            )
        );
}
