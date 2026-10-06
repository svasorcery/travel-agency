using System.Text.Json.Serialization;
using ErrorOr;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Core.Cancellation;

public sealed record CancellationConfirmationFacts(
    string ProviderOrderRef,
    string ProviderCancellationRef,
    string ItineraryPartyHash,
    Money? Refund,
    CancellationRefundDestination Destination,
    CancellationSettlementFacts Settlement,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset ObservedAt,
    CancellationResolutionSource Source
);

/// <summary>A matching supplier cancellation fact; it is not a customer payout.</summary>
public sealed record CancellationEvidence
{
    public string ProviderOrderRef { get; }
    public string ProviderCancellationRef { get; }
    public string ItineraryPartyHash { get; }
    public Money Refund { get; }
    public CancellationRefundDestination Destination { get; }
    public CancellationSettlementFacts Settlement { get; }
    public DateTimeOffset ConfirmedAt { get; }
    public DateTimeOffset ObservedAt { get; }
    public CancellationResolutionSource Source { get; }

    [JsonConstructor]
    private CancellationEvidence(
        string providerOrderRef,
        string providerCancellationRef,
        string itineraryPartyHash,
        Money refund,
        CancellationRefundDestination destination,
        CancellationSettlementFacts settlement,
        DateTimeOffset confirmedAt,
        DateTimeOffset observedAt,
        CancellationResolutionSource source
    )
    {
        ProviderOrderRef = providerOrderRef;
        ProviderCancellationRef = providerCancellationRef;
        ItineraryPartyHash = itineraryPartyHash;
        Refund = refund;
        Destination = destination;
        Settlement = settlement;
        ConfirmedAt = confirmedAt;
        ObservedAt = observedAt;
        Source = source;
    }

    public static ErrorOr<CancellationEvidence> FromSupplierConfirmation(
        CancellationTerms terms,
        CancellationConfirmationFacts facts,
        DateTimeOffset now
    ) => Create(terms, facts, now, CancellationResolutionSource.SupplierApi);

    public static ErrorOr<CancellationEvidence> FromOperatorConfirmation(
        CancellationTerms terms,
        CancellationConfirmationFacts facts,
        DateTimeOffset now
    ) => Create(terms, facts, now, CancellationResolutionSource.OperatorVerified);

    private static ErrorOr<CancellationEvidence> Create(
        CancellationTerms terms,
        CancellationConfirmationFacts facts,
        DateTimeOffset now,
        CancellationResolutionSource expectedSource
    )
    {
        if (
            terms is null
            || facts is null
            || facts.Source != expectedSource
            || !string.Equals(
                facts.ProviderOrderRef,
                terms.ProviderOrderRef,
                StringComparison.Ordinal
            )
            || !string.Equals(
                facts.ProviderCancellationRef,
                terms.ProviderCancellationRef,
                StringComparison.Ordinal
            )
            || !string.Equals(
                facts.ItineraryPartyHash,
                terms.ItineraryPartyHash,
                StringComparison.OrdinalIgnoreCase
            )
            || facts.Refund is null
            || facts.Refund != terms.Refund
            || facts.Destination != terms.Destination
            || facts.Settlement is null
            || !terms.Settlement.MatchesEconomicFacts(facts.Settlement)
            || facts.ConfirmedAt is null
            || facts.ConfirmedAt == DateTimeOffset.MinValue
            || facts.ObservedAt == DateTimeOffset.MinValue
            || facts.ConfirmedAt > facts.ObservedAt
            || facts.ObservedAt > now
        )
            return Error.Validation(
                "Flights.CancellationEvidenceMismatch",
                "Cancellation confirmation does not match accepted terms."
            );

        return new CancellationEvidence(
            facts.ProviderOrderRef,
            facts.ProviderCancellationRef,
            terms.ItineraryPartyHash,
            facts.Refund,
            facts.Destination,
            facts.Settlement,
            facts.ConfirmedAt.Value,
            facts.ObservedAt,
            expectedSource
        );
    }

    public override string ToString() => nameof(CancellationEvidence);
}
