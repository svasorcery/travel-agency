using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Travel.Modules.Hotels.Core;

public sealed record HotelPropertyReference(HotelSourceIdentity Source, string PropertyId)
{
    public string OrdinalKey => Source.OrdinalKey + "\u001f" + PropertyId;
}

public sealed record ReviewedHotelPair(
    string Id,
    string Revision,
    HotelPropertyReference Left,
    HotelPropertyReference Right,
    string Evidence,
    string Reviewer,
    string Reason,
    DateTimeOffset CheckedAt,
    DateTimeOffset ValidUntil,
    string UseScope,
    string? LeftPropertyFingerprint = null,
    string? RightPropertyFingerprint = null
);

public sealed record HotelBucketMinimum(string Bucket, StayMoney Total);

public sealed record HotelPropertyGroup(
    string Key,
    HotelPropertyReference Representative,
    HotelProperty Property,
    HotelOffer[] Offers,
    string MatchReason,
    [property: JsonIgnore] ReviewedHotelPair? Mapping
)
{
    public string? PrimaryBucket { get; init; }
    public HotelBucketMinimum[] Minima { get; init; } = [];
    public string RankingPolicyVersion { get; init; } = HotelCostRanking.Version;
}

public static class HotelPropertyMatching
{
    public static string MappingDigest(ReviewedHotelPair pair) =>
        Convert
            .ToHexString(
                SHA256.HashData(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(pair))
            )
            .ToLowerInvariant();

    public static bool ReviewedEvidence(ReviewedHotelPair pair) =>
        !string.IsNullOrWhiteSpace(pair.Id)
        && !string.IsNullOrWhiteSpace(pair.Evidence)
        && !string.IsNullOrWhiteSpace(pair.Reviewer)
        && !string.IsNullOrWhiteSpace(pair.Reason)
        && !string.IsNullOrWhiteSpace(pair.Revision)
        && pair.LeftPropertyFingerprint is { Length: 64 }
        && pair.LeftPropertyFingerprint.All(char.IsAsciiHexDigit)
        && pair.RightPropertyFingerprint is { Length: 64 }
        && pair.RightPropertyFingerprint.All(char.IsAsciiHexDigit)
        && pair.Left.Source.Provider != pair.Right.Source.Provider
        && pair.Left.Source.Profile == pair.UseScope
        && pair.Right.Source.Profile == pair.UseScope
        && pair.ValidUntil > pair.CheckedAt;

    public static string PropertyFingerprint(HotelProperty property) =>
        Convert
            .ToHexString(
                SHA256.HashData(
                    System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
                        new
                        {
                            property.Id,
                            property.Name,
                            property.Address,
                        }
                    )
                )
            )
            .ToLowerInvariant();

    public static HotelPropertyReference Reference(HotelOffer offer) =>
        new(
            offer.Source ?? new(offer.Provider, "legacy", offer.Environment, "Legacy"),
            offer.Terms.Property.Id
        );

    public static HotelPropertyGroup[] Group(
        HotelOffer[] offers,
        ReviewedHotelPair[] pairs,
        DateTimeOffset now
    )
    {
        var membership = pairs
            .SelectMany(p => new[] { p.Left.OrdinalKey, p.Right.OrdinalKey })
            .ToArray();
        var invalid =
            pairs.Length > 100
            || membership.Length > 200
            || membership.Distinct(StringComparer.Ordinal).Count() != membership.Length
            || pairs.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != pairs.Length;
        var contradictory = pairs
            .Where(p =>
                offers.Any(o =>
                    Reference(o) == p.Left
                        && PropertyFingerprint(o.Terms.Property) != p.LeftPropertyFingerprint
                    || Reference(o) == p.Right
                        && PropertyFingerprint(o.Terms.Property) != p.RightPropertyFingerprint
                )
            )
            .ToHashSet();
        var validPairs = invalid
            ? []
            : pairs
                .Where(p =>
                    p.CheckedAt <= now
                    && p.ValidUntil > now
                    && !contradictory.Contains(p)
                    && ReviewedEvidence(p)
                )
                .ToArray();
        var groups = new Dictionary<string, List<HotelOffer>>(StringComparer.Ordinal);
        foreach (var offer in offers.OrderBy(o => Reference(o).OrdinalKey, StringComparer.Ordinal))
        {
            var reference = Reference(offer);
            var pair = validPairs.SingleOrDefault(p => p.Left == reference || p.Right == reference);
            var key = pair is null
                ? "source:" + reference.OrdinalKey
                : "pair:" + pair.Id + ":" + pair.Revision;
            if (!groups.TryGetValue(key, out var group))
                groups[key] = group = [];
            group.Add(offer);
        }
        return groups
            .Select(g =>
            {
                var first = g.Value[0];
                var reference = Reference(first);
                var pair = validPairs.SingleOrDefault(p =>
                    p.Left == reference || p.Right == reference
                );
                var referenced = pairs.FirstOrDefault(p =>
                    p.Left == reference || p.Right == reference
                );
                var reason =
                    invalid ? "MappingConflict"
                    : referenced is not null && contradictory.Contains(referenced)
                        ? "EvidenceConflict"
                    : referenced is not null && referenced.ValidUntil <= now ? "MappingExpired"
                    : referenced is not null && pair is null ? "MappingUnverified"
                    : pair is null ? "Unmatched"
                    : "Reviewed";
                // Fixed-size group identity excludes supplier text and ephemeral selection handles.
                var key = Convert
                    .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(g.Key)))
                    .ToLowerInvariant();
                return new HotelPropertyGroup(
                    key,
                    reference,
                    first.Terms.Property,
                    g.Value.ToArray(),
                    reason,
                    pair
                );
            })
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToArray();
    }
}
