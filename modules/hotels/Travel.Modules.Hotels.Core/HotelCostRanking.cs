using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Travel.Modules.Hotels.Core;

public sealed record HotelRankingExplanation(
    string PolicyVersion,
    string Scope,
    string Reason,
    string? Bucket,
    StayMoney? Total
);

public static class HotelCostRanking
{
    public const string Version = "hotel-cost-first-v1";
    private static readonly JsonSerializerOptions SemanticJson = new()
    {
        Converters = { new SemanticMoneyConverter() },
    };

    public static string MaterialFacts(HotelOffer offer) =>
        JsonSerializer.Serialize(
            new
            {
                Terms = offer.Terms with
                {
                    MandatoryLocalPayments = offer
                        .Terms.MandatoryLocalPayments?.OrderBy(
                            m => m.Currency,
                            StringComparer.Ordinal
                        )
                        .ThenBy(m => m.Amount)
                        .ToArray(),
                    Board = SortFacts(offer.Terms.Board),
                    Conditions = SortFacts(offer.Terms.Conditions),
                    Cancellation = offer.Terms.Cancellation with
                    {
                        Rules = SortFacts(offer.Terms.Cancellation.Rules),
                    },
                },
                offer.Price,
                offer.ConditionEvidence,
            },
            SemanticJson
        );

    private static ScopedFact[] SortFacts(ScopedFact[] facts) =>
        facts
            .OrderBy(f => f.Scope, StringComparer.Ordinal)
            .ThenBy(f => f.Text, StringComparer.Ordinal)
            .ToArray();

    public static string SemanticKey(HotelOffer offer) =>
        HotelPropertyMatching.Reference(offer).OrdinalKey
        + "\u001f"
        + offer.RoomIdentity
        + "\u001f"
        + offer.Terms.RateId
        + "\u001f"
        + Fingerprint(offer);

    public static string Fingerprint(HotelOffer offer) =>
        Convert
            .ToHexString(
                SHA256.HashData(
                    JsonSerializer.SerializeToUtf8Bytes(
                        new
                        {
                            offer.Terms.Dates,
                            offer.Terms.Occupancy,
                            offer.Terms.Total,
                            Board = SortFacts(offer.Terms.Board),
                            Conditions = SortFacts(offer.Terms.Conditions),
                            Cancellation = offer.Terms.Cancellation with
                            {
                                Rules = SortFacts(offer.Terms.Cancellation.Rules),
                            },
                            offer.Price,
                            offer.ConditionEvidence,
                            offer.Terms.Eligible,
                            offer.Terms.PaymentTiming,
                        },
                        SemanticJson
                    )
                )
            )
            .ToLowerInvariant();

    public static HotelPropertyGroup[] Rank(HotelPropertyGroup[] groups) =>
        groups
            .Select(group =>
            {
                var offers = group
                    .Offers.Select(o =>
                        o with
                        {
                            Ranking = new(
                                Version,
                                "WholeStay",
                                o.Price?.ComparableTotal() is null ? "ObligationsIncomplete"
                                    : groups
                                        .SelectMany(g => g.Offers)
                                        .Count(other =>
                                            Bucket(other) == Bucket(o)
                                            && other.Price?.ComparableTotal()
                                                == o.Price?.ComparableTotal()
                                        ) > 1
                                        ? "EqualTotalTechnicalOrder"
                                    : "KnownWholeStayObligations",
                                Bucket(o),
                                o.Price?.ComparableTotal()
                            ),
                        }
                    )
                    .OrderBy(o => Bucket(o) ?? "~", StringComparer.Ordinal)
                    .ThenBy(o => o.Price?.ComparableTotal()?.Amount ?? 0)
                    .ThenBy(SemanticKey, StringComparer.Ordinal)
                    .ToArray();
                var minima = offers
                    .Where(o => Bucket(o) is not null)
                    .GroupBy(o => Bucket(o)!, StringComparer.Ordinal)
                    .Select(bucket => new HotelBucketMinimum(
                        bucket.Key,
                        bucket.Select(o => o.Price!.ComparableTotal()!).MinBy(m => m.Amount)!
                    ))
                    .OrderBy(m => m.Bucket, StringComparer.Ordinal)
                    .ToArray();
                return group with
                {
                    Offers = offers,
                    Minima = minima,
                    PrimaryBucket = minima.FirstOrDefault()?.Bucket,
                    RankingPolicyVersion = Version,
                };
            })
            .OrderBy(g => g.PrimaryBucket ?? "~", StringComparer.Ordinal)
            .ThenBy(g => g.Minima.FirstOrDefault()?.Total.Amount ?? 0)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToArray();

    private static string? Bucket(HotelOffer offer) =>
        offer.Price?.ComparableTotal() is { } money
            ? offer.Price.Basis + ":" + money.Currency
            : null;

    private sealed class SemanticMoneyConverter : JsonConverter<StayMoney>
    {
        public override StayMoney Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        ) => throw new NotSupportedException();

        public override void Write(
            Utf8JsonWriter writer,
            StayMoney value,
            JsonSerializerOptions options
        )
        {
            writer.WriteStartObject();
            writer.WriteString(
                "Amount",
                value.Amount.ToString("G29", CultureInfo.InvariantCulture)
            );
            writer.WriteString("Currency", value.Currency);
            writer.WriteEndObject();
        }
    }
}
