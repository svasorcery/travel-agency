using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using ErrorOr;
using Travel.Modules.Flights.Core.ValueObjects.Offer;

namespace Travel.Modules.Flights.Core.ValueObjects;

public sealed record QuotedFareSnapshot
{
    public Guid AggregateId { get; }
    public Guid OfferId { get; }
    public Guid QuoteRevision { get; }
    public int FingerprintVersion { get; }
    public string Fingerprint { get; }
    public FareRuleEvidence Evidence { get; }

    [JsonConstructor]
    private QuotedFareSnapshot(
        Guid aggregateId,
        Guid offerId,
        Guid quoteRevision,
        int fingerprintVersion,
        string fingerprint,
        FareRuleEvidence evidence
    )
    {
        AggregateId = aggregateId;
        OfferId = offerId;
        QuoteRevision = quoteRevision;
        FingerprintVersion = fingerprintVersion;
        Fingerprint = fingerprint;
        Evidence = evidence;
    }

    public static ErrorOr<QuotedFareSnapshot> Create(
        Guid aggregateId,
        BookableOffer offer,
        QuoteBinding binding
    )
    {
        if (
            aggregateId == Guid.Empty
            || offer is null
            || offer.Id.IsEmpty
            || offer.FareRuleEvidence is null
            || offer.Party is null
            || offer.Party.Validate().IsError
            || binding is null
            || binding.Validate().IsError
            || offer.FareRuleEvidence.Validate().IsError
            || offer.Itinerary is null
            || string.IsNullOrWhiteSpace(offer.ProviderOfferRef)
        )
            return Invalid();
        var source = offer.FareRuleEvidence;
        if (
            binding.Party.FirstDepartureLocalDate != offer.Party.FirstDepartureLocalDate
            || binding.Party.SupportsHold != offer.Party.SupportsHold
            || binding.Party.RequiresIdentityDocuments != offer.Party.RequiresIdentityDocuments
            || source.PassengerCount != offer.Party.PassengerCount
            || source.PassengerCount != binding.Slots.Count
            || !source.SegmentCounts.SequenceEqual(
                offer.Itinerary.Slices.Select(x => x.Segments.Count)
            )
            || source.OfferExpiresAt != offer.ExpiresAt
            || source.FirstDepartureAt
                != offer.Itinerary.Slices.SelectMany(x => x.Segments).Min(x => x.DepartAt)
        )
            return Invalid();
        var target = binding
            .Slots.Select((slot, index) => (slot.SupplierReference.Value, index))
            .ToDictionary(x => x.Value, x => x.index, StringComparer.Ordinal);
        var permutation = new int[source.PassengerCount];
        for (var i = 0; i < permutation.Length; i++)
            if (!target.TryGetValue(offer.Party.Passengers[i].Reference.Value, out permutation[i]))
                return Invalid();
        var remapped = source.RemapAdults(permutation);
        if (remapped.IsError)
            return remapped.Errors;
        var evidence = remapped.Value;
        var canonical = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                Provider = offer.Provider,
                offer.ProviderOfferRef,
                Route = offer.Itinerary.Slices.Select(
                    (leg, index) =>
                        new
                        {
                            Leg = index,
                            Segments = leg.Segments.Select(s => new
                            {
                                Origin = s.Origin.Value,
                                Destination = s.Destination.Value,
                                DepartAt = s.DepartAt.UtcDateTime,
                                ArriveAt = s.ArriveAt.UtcDateTime,
                                s.CarrierCode,
                                s.FlightNumber,
                                Cabin = s.Cabin.Code,
                            }),
                        }
                ),
                Facts = evidence.SemanticKey(),
                ObservedAt = evidence.ObservedAt.UtcDateTime,
                OfferExpiresAt = evidence.OfferExpiresAt.UtcDateTime,
                FirstDepartureAt = evidence.FirstDepartureAt.UtcDateTime,
            }
        );
        return new QuotedFareSnapshot(
            aggregateId,
            offer.Id.Value,
            binding.Revision,
            1,
            Convert.ToHexString(SHA256.HashData(canonical)).ToLowerInvariant(),
            evidence
        );
    }

    private static Error Invalid() =>
        Error.Validation(
            "Flights.QuotedFareSnapshotInvalid",
            "Fare snapshot must match the exact offer and existing passenger binding."
        );
}
