using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorOr;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.ValueObjects;

[JsonConverter(typeof(JsonStringEnumConverter<FarePermission>))]
public enum FarePermission
{
    Unknown,
    Allowed,
    NotAllowed,
}

public sealed record FareCondition
{
    public FarePermission Permission { get; }
    public Money? Penalty { get; }
    public string PermissionReason { get; }
    public string PenaltyReason { get; }

    [JsonConstructor]
    private FareCondition(
        FarePermission permission,
        Money? penalty,
        string permissionReason,
        string penaltyReason
    )
    {
        Permission = permission;
        Penalty = penalty;
        PermissionReason = permissionReason;
        PenaltyReason = penaltyReason;
    }

    public static FareCondition NotProvided { get; } =
        new(FarePermission.Unknown, null, "NotProvided", "NotProvided");

    public static ErrorOr<FareCondition> Create(
        FarePermission permission,
        Money? penalty,
        string permissionReason,
        string penaltyReason
    )
    {
        var value = new FareCondition(permission, penalty, permissionReason, penaltyReason);
        return value.IsValid()
            ? value
            : Error.Validation("Flights.FareConditionInvalid", "Fare condition is invalid.");
    }

    internal bool IsValid() =>
        Enum.IsDefined(Permission)
        && Reason(PermissionReason)
        && Reason(PenaltyReason)
        && (
            Permission == FarePermission.Unknown
                ? PermissionReason != "Known"
                : PermissionReason == "Known"
        )
        && (
            Penalty is null
                ? PenaltyReason != "Known"
                : PenaltyReason == "Known"
                    && Penalty.Amount >= 0
                    && Penalty.Currency is not null
                    && !CurrencyCode.Create(Penalty.Currency.Value).IsError
        );

    private static bool Reason(string reason) =>
        reason is "Known" or "NotProvided" or "InvalidSupplierData";

    internal object Canonical() =>
        new
        {
            Permission,
            Amount = Penalty?.Amount.ToString("G29", CultureInfo.InvariantCulture),
            Currency = Penalty?.Currency.Value,
            PermissionReason,
            PenaltyReason,
        };
}

public sealed record FareLegFact(
    int Leg,
    string? FareBrandName,
    FareCondition ChangeBeforeDeparture
);

public sealed record FareSegmentFact(
    int Leg,
    int Segment,
    int Adult,
    string? FareBasisCode,
    string? CabinClassMarketing,
    int? CheckedBaggageQuantity,
    int? CarryOnBaggageQuantity
);

/// <summary>Bounded provider-neutral facts. Historical JSON reads preserve persisted facts without invoking current creation rules.</summary>
public sealed record FareRuleEvidence
{
    public int SchemaVersion { get; }
    public DateTimeOffset ObservedAt { get; }
    public DateTimeOffset OfferExpiresAt { get; }
    public DateTimeOffset FirstDepartureAt { get; }
    public int PassengerCount { get; }
    public EquatableArray<int> SegmentCounts { get; }
    public FareCondition ChangeBeforeDeparture { get; }
    public FareCondition RefundBeforeDeparture { get; }
    public EquatableArray<FareLegFact> Legs { get; }
    public EquatableArray<FareSegmentFact> Passengers { get; }
    public bool SupplierDataInvalid { get; }

    [JsonConstructor]
    private FareRuleEvidence(
        int schemaVersion,
        DateTimeOffset observedAt,
        DateTimeOffset offerExpiresAt,
        DateTimeOffset firstDepartureAt,
        int passengerCount,
        EquatableArray<int> segmentCounts,
        FareCondition changeBeforeDeparture,
        FareCondition refundBeforeDeparture,
        EquatableArray<FareLegFact> legs,
        EquatableArray<FareSegmentFact> passengers,
        bool supplierDataInvalid
    )
    {
        SchemaVersion = schemaVersion;
        ObservedAt = observedAt;
        OfferExpiresAt = offerExpiresAt;
        FirstDepartureAt = firstDepartureAt;
        PassengerCount = passengerCount;
        SegmentCounts = new(segmentCounts.ToArray());
        ChangeBeforeDeparture = changeBeforeDeparture;
        RefundBeforeDeparture = refundBeforeDeparture;
        Legs = new(legs.ToArray());
        Passengers = new(passengers.ToArray());
        SupplierDataInvalid = supplierDataInvalid;
    }

    public static ErrorOr<FareRuleEvidence> Create(
        DateTimeOffset observedAt,
        DateTimeOffset offerExpiresAt,
        DateTimeOffset firstDepartureAt,
        int passengerCount,
        IEnumerable<int> segmentCounts,
        FareCondition changeBeforeDeparture,
        FareCondition refundBeforeDeparture,
        IEnumerable<FareLegFact> legs,
        IEnumerable<FareSegmentFact> passengers,
        bool supplierDataInvalid = false
    )
    {
        var value = new FareRuleEvidence(
            1,
            observedAt,
            offerExpiresAt,
            firstDepartureAt,
            passengerCount,
            new(segmentCounts?.Take(5).ToArray() ?? []),
            changeBeforeDeparture,
            refundBeforeDeparture,
            new(legs?.Take(5).ToArray() ?? []),
            new(passengers?.Take(577).ToArray() ?? []),
            supplierDataInvalid
        );
        var validation = value.Validate();
        return validation.IsError ? validation.Errors : value;
    }

    public ErrorOr<Success> Validate()
    {
        if (
            SchemaVersion != 1
            || ObservedAt == default
            || OfferExpiresAt == default
            || FirstDepartureAt == default
            || PassengerCount is < 1 or > 9
            || SegmentCounts.Count is < 1 or > 4
            || SegmentCounts.Any(x => x < 1)
            || SegmentCounts.Sum(x => (long)x) > 64
            || ChangeBeforeDeparture is null
            || !ChangeBeforeDeparture.IsValid()
            || RefundBeforeDeparture is null
            || !RefundBeforeDeparture.IsValid()
            || Legs.Count != SegmentCounts.Count
            || Legs.Any(x =>
                x is null
                || x.Leg < 0
                || x.Leg >= SegmentCounts.Count
                || !Label(x.FareBrandName, 256)
                || x.ChangeBeforeDeparture is null
                || !x.ChangeBeforeDeparture.IsValid()
            )
            || Legs.Select(x => x.Leg).Distinct().Count() != Legs.Count
        )
            return Invalid();
        if (
            Passengers.Count != SegmentCounts.Sum() * PassengerCount
            || Passengers.Any(x =>
                x is null
                || x.Leg < 0
                || x.Leg >= SegmentCounts.Count
                || x.Segment < 0
                || x.Segment >= SegmentCounts[x.Leg]
                || x.Adult < 0
                || x.Adult >= PassengerCount
                || !Label(x.FareBasisCode, 64)
                || !Label(x.CabinClassMarketing, 256)
                || x.CheckedBaggageQuantity is < 0
                || x.CarryOnBaggageQuantity is < 0
            )
            || Passengers.Select(x => (x.Leg, x.Segment, x.Adult)).Distinct().Count()
                != Passengers.Count
        )
            return Invalid();
        if (JsonSerializer.SerializeToUtf8Bytes(this).Length > 128 * 1024)
            return Invalid();
        return Result.Success;
    }

    private static Error Invalid() =>
        Error.Validation(
            "Flights.FareEvidenceInvalid",
            "Fare evidence is invalid or exceeds its bounds."
        );

    private static bool Label(string? value, int max) =>
        value is null
        || !string.IsNullOrWhiteSpace(value)
            && value.Length <= max
            && !value.Any(c =>
                char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format
            );

    /// <summary>Maps each source adult ordinal to its ordinal in the existing quote binding.</summary>
    public ErrorOr<FareRuleEvidence> RemapAdults(int[] permutation)
    {
        var validation = Validate();
        if (validation.IsError)
            return validation.Errors;
        if (
            permutation is null
            || permutation.Length != PassengerCount
            || permutation.Any(x => x < 0 || x >= PassengerCount)
            || permutation.Distinct().Count() != PassengerCount
        )
            return Invalid();
        return Create(
            ObservedAt,
            OfferExpiresAt,
            FirstDepartureAt,
            PassengerCount,
            SegmentCounts,
            ChangeBeforeDeparture,
            RefundBeforeDeparture,
            Legs,
            Passengers.Select(x => x with { Adult = permutation[x.Adult] }),
            SupplierDataInvalid
        );
    }

    public string SemanticKey() =>
        JsonSerializer.Serialize(
            new
            {
                PassengerCount,
                SegmentCounts,
                Change = ChangeBeforeDeparture.Canonical(),
                Refund = RefundBeforeDeparture.Canonical(),
                Legs = Legs.OrderBy(x => x.Leg)
                    .Select(x => new
                    {
                        x.Leg,
                        x.FareBrandName,
                        Change = x.ChangeBeforeDeparture.Canonical(),
                    }),
                Passengers = Passengers
                    .OrderBy(x => x.Leg)
                    .ThenBy(x => x.Segment)
                    .ThenBy(x => x.Adult),
                SupplierDataInvalid,
            }
        );
}
