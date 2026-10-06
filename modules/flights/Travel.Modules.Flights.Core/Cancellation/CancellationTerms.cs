using System.Collections.Frozen;
using System.Text.Json.Serialization;
using ErrorOr;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Core.Cancellation;

public enum CancellationRefundDestination
{
    Unknown = 0,
    Balance = 1,
    Card = 2,
    ArcBspCash = 3,
    AwaitingPayment = 4,
    OriginalFormOfPayment = 5,
}

public enum SettlementComposition
{
    Unknown = 0,
    CashOnly = 1,
    Unpaid = 2,
    CreditOnly = 3,
    Mixed = 4,
}

public enum CancellationResolutionSource
{
    None = 0,
    TravelAdmission = 1,
    SupplierApi = 2,
    OperatorVerified = 3,
}

public sealed record CancellationSettlementFacts(
    SettlementComposition Composition,
    bool CreditsKnownEmpty,
    CancellationResolutionSource Provenance,
    bool OriginalCashOnlyVerified,
    bool UnpaidOrderVerified
)
{
    internal bool MatchesEconomicFacts(CancellationSettlementFacts? other) =>
        other is not null
        && Provenance
            is CancellationResolutionSource.SupplierApi
                or CancellationResolutionSource.OperatorVerified
        && other.Provenance
            is CancellationResolutionSource.SupplierApi
                or CancellationResolutionSource.OperatorVerified
        && Composition == other.Composition
        && CreditsKnownEmpty == other.CreditsKnownEmpty
        && OriginalCashOnlyVerified == other.OriginalCashOnlyVerified
        && UnpaidOrderVerified == other.UnpaidOrderVerified;
}

public sealed record CancellationTermsInput(
    long Revision,
    Guid AggregateId,
    Guid OwnerId,
    Guid OperationId,
    string ProviderOrderRef,
    string ProviderCancellationRef,
    string ItineraryPartyHash,
    Money? Refund,
    CancellationRefundDestination Destination,
    CancellationSettlementFacts Settlement,
    DateTimeOffset? ExpiresAt,
    string NoticeVersion
);

public sealed record CancellationTerms
{
    public long Revision { get; }
    public Guid AggregateId { get; }
    public Guid OwnerId { get; }
    public Guid OperationId { get; }
    public string ProviderOrderRef { get; }
    public string ProviderCancellationRef { get; }
    public string ItineraryPartyHash { get; }
    public Money Refund { get; }
    public CancellationRefundDestination Destination { get; }
    public CancellationSettlementFacts Settlement { get; }
    public DateTimeOffset ExpiresAt { get; }
    public string NoticeVersion { get; }
    public string Hash { get; }

    // SIX ISO 4217 current list, published 2026-09-17; XTS/XXX are testing/no-currency.
    // Only fresh cancellation terms use this snapshot. General Money/history are unchanged.
    private static readonly FrozenSet<string> KnownCurrencies =
        "AED AFN ALL AMD AOA ARS AUD AWG AZN BAM BBD BDT BHD BIF BMD BND BOB BOV BRL BSD BTN BWP BYN BZD CAD CDF CHE CHF CHW CLF CLP CNY COP COU CRC CUP CVE CZK DJF DKK DOP DZD EGP ERN ETB EUR FJD FKP GBP GEL GHS GIP GMD GNF GTQ GYD HKD HNL HTG HUF IDR ILS INR IQD IRR ISK JMD JOD JPY KES KGS KHR KMF KPW KRW KWD KYD KZT LAK LBP LKR LRD LSL LYD MAD MDL MGA MKD MMK MNT MOP MRU MUR MVR MWK MXN MXV MYR MZN NAD NGN NIO NOK NPR NZD OMR PAB PEN PGK PHP PKR PLN PYG QAR RON RSD RUB RWF SAR SBD SCR SDG SEK SGD SHP SLE SOS SRD SSP STN SVC SYP SZL THB TJS TMT TND TOP TRY TTD TWD TZS UAH UGX USD USN UYI UYU UYW UZS VED VES VND VUV WST XAD XAF XAG XAU XBA XBB XBC XBD XCD XCG XDR XOF XPD XPF XPT XSU XUA YER ZAR ZMW ZWG"
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToFrozenSet(StringComparer.Ordinal);

    [JsonConstructor]
    private CancellationTerms(
        long revision,
        Guid aggregateId,
        Guid ownerId,
        Guid operationId,
        string providerOrderRef,
        string providerCancellationRef,
        string itineraryPartyHash,
        Money refund,
        CancellationRefundDestination destination,
        CancellationSettlementFacts settlement,
        DateTimeOffset expiresAt,
        string noticeVersion,
        string hash
    )
    {
        Revision = revision;
        AggregateId = aggregateId;
        OwnerId = ownerId;
        OperationId = operationId;
        ProviderOrderRef = providerOrderRef;
        ProviderCancellationRef = providerCancellationRef;
        ItineraryPartyHash = itineraryPartyHash;
        Refund = refund;
        Destination = destination;
        Settlement = settlement;
        ExpiresAt = expiresAt;
        NoticeVersion = noticeVersion;
        Hash = hash;
    }

    public static ErrorOr<CancellationTerms> Create(
        CancellationTermsInput input,
        DateTimeOffset now
    )
    {
        var validation = Validate(input, now);
        if (validation.IsError)
            return validation.Errors;
        return CreateValidated(input, CancellationTermsFingerprint.Compute(input));
    }

    private static ErrorOr<Success> Validate(CancellationTermsInput input, DateTimeOffset now)
    {
        if (
            input is null
            || input.Revision <= 0
            || input.AggregateId == Guid.Empty
            || input.OwnerId == Guid.Empty
            || input.OperationId == Guid.Empty
            || !ValidReference(input.ProviderOrderRef)
            || !ValidReference(input.ProviderCancellationRef)
            || !ValidScope(input.ItineraryPartyHash)
            || !ValidReference(input.NoticeVersion, 64)
        )
            return Error.Validation(
                "Flights.CancellationTermsInvalid",
                "Cancellation proposal binding is invalid."
            );

        if (
            input.Refund is null
            || input.Refund.Amount < 0m
            || input.Refund.Currency is null
            || CurrencyCode.Create(input.Refund.Currency.Value).IsError
            || !KnownCurrencies.Contains(input.Refund.Currency.Value)
            || input.ExpiresAt is null
            || input.Settlement is null
            || !input.Settlement.CreditsKnownEmpty
            || input.Settlement.Provenance
                is not (
                    CancellationResolutionSource.SupplierApi
                    or CancellationResolutionSource.OperatorVerified
                )
            || input.Settlement.Composition
                is not (SettlementComposition.CashOnly or SettlementComposition.Unpaid)
            || input.Destination
                is not (
                    CancellationRefundDestination.Balance
                    or CancellationRefundDestination.Card
                    or CancellationRefundDestination.ArcBspCash
                    or CancellationRefundDestination.AwaitingPayment
                    or CancellationRefundDestination.OriginalFormOfPayment
                )
        )
            return UnsupportedFinancialTerms();

        if (input.ExpiresAt <= now)
            return Error.Conflict(
                "Flights.CancellationTermsExpired",
                "Cancellation proposal has expired."
            );

        if (input.Settlement.Composition == SettlementComposition.Unpaid)
        {
            if (
                !input.Settlement.UnpaidOrderVerified
                || input.Refund.Amount != 0m
                || input.Destination != CancellationRefundDestination.AwaitingPayment
                || input.Settlement.OriginalCashOnlyVerified
            )
                return UnsupportedFinancialTerms();
        }
        else if (input.Settlement.UnpaidOrderVerified)
            return UnsupportedFinancialTerms();

        if (
            input.Destination == CancellationRefundDestination.AwaitingPayment
            && input.Settlement.Composition != SettlementComposition.Unpaid
        )
            return UnsupportedFinancialTerms();
        if (
            input.Destination == CancellationRefundDestination.OriginalFormOfPayment
            && (
                input.Settlement.Composition != SettlementComposition.CashOnly
                || !input.Settlement.OriginalCashOnlyVerified
            )
        )
            return UnsupportedFinancialTerms();
        return Result.Success;
    }

    private static Error UnsupportedFinancialTerms() =>
        Error.Validation(
            "Flights.CancellationFinancialTermsUnsupported",
            "Cancellation financial facts require manual review."
        );

    internal static bool ValidReference(string value, int maximum = 256) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximum
        && value == value.Trim()
        && !value.Any(char.IsControl);

    private static bool ValidScope(string value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static CancellationTerms CreateValidated(CancellationTermsInput input, string hash) =>
        new(
            input.Revision,
            input.AggregateId,
            input.OwnerId,
            input.OperationId,
            input.ProviderOrderRef,
            input.ProviderCancellationRef,
            input.ItineraryPartyHash.ToLowerInvariant(),
            input.Refund!,
            input.Destination,
            input.Settlement,
            input.ExpiresAt!.Value,
            input.NoticeVersion,
            hash
        );

    public bool MatchesQuote(CancellationQuoteFacts quote) =>
        quote.ProviderOrderRef == ProviderOrderRef
        && quote.ProviderCancellationRef == ProviderCancellationRef
        && quote.ItineraryPartyHash == ItineraryPartyHash
        && quote.Refund == Refund
        && quote.Destination == Destination
        && Settlement.MatchesEconomicFacts(quote.Settlement)
        && quote.ExpiresAt == ExpiresAt;

    public override string ToString() => nameof(CancellationTerms);
}
