namespace Travel.Modules.Hotels.Core;

// Account is a configured public alias, never an API key or a supplier credential.
public sealed record HotelSourceIdentity(
    string Provider,
    string Account,
    string Environment,
    string Profile
)
{
    public string OrdinalKey => string.Join('\u001f', Provider, Account, Environment, Profile);
}

// Persisted in StateJson, independently of accepted terms and the search cache.
public sealed record HotelAdmissionBinding(
    int Version,
    HotelSourceIdentity Source,
    string ConfigurationRevision,
    string PricingProfile,
    string ApprovalRevision,
    string? MappingPair,
    string? MappingRevision,
    DateTimeOffset ValidUntil,
    string? TransportAccountBinding = null,
    string? MappingEvidenceDigest = null,
    string? TransportCredentialFingerprint = null
);

public sealed record HotelSourceDescriptor(
    HotelSourceIdentity Identity,
    string ConfigurationRevision,
    string PricingProfile,
    string ApprovalRevision,
    bool NewPurchaseAllowed,
    string? TransportAccountBinding = null,
    string? TransportCredentialFingerprint = null
)
{
    public bool Matches(HotelAdmissionBinding binding) =>
        binding.Version == 2
        && Identity == binding.Source
        && ConfigurationRevision == binding.ConfigurationRevision
        && PricingProfile == binding.PricingProfile
        && ApprovalRevision == binding.ApprovalRevision
        && TransportAccountBinding == binding.TransportAccountBinding
        && TransportCredentialFingerprint == binding.TransportCredentialFingerprint;
}

public interface IHotelSourceDescriptor
{
    HotelSourceDescriptor CurrentSource { get; }
}

public interface IHotelLegacyContextConsumer
{
    bool AcceptsLegacyContext(ProviderContextHandle context);
}

public interface IHotelPartySupport
{
    CapabilitySupport GetPartySupport(HotelParty party);
}

public sealed record HotelObligation(string Inclusion, StayMoney? Amount, string Scope);

public sealed record HotelPriceEvidence(
    string Basis,
    StayMoney SupplierPayable,
    StayMoney? TravellerPrice,
    string Scope,
    string Source,
    HotelObligation[] Included,
    HotelObligation[]? LocalObligations
)
{
    public StayMoney? ComparableTotal()
    {
        if (
            Basis is not ("SupplierNet" or "SupplierCost" or "TravellerPrice")
            || Scope != "WholeStay"
            || LocalObligations is null
        )
            return null;
        var debit = Basis == "TravellerPrice" ? TravellerPrice : SupplierPayable;
        if (
            debit is null
            || LocalObligations.Any(o =>
                o.Inclusion != "Excluded"
                || o.Amount is null
                || o.Scope != "WholeStay"
                || o.Amount.Currency != debit.Currency
            )
        )
            return null;
        try
        {
            return new(debit.Amount + LocalObligations.Sum(o => o.Amount!.Amount), debit.Currency);
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}

public sealed record HotelBoardEvidence(string Code, int? Guests, int? Nights, string Source);

public sealed record HotelCancellationWindow(
    DateTimeOffset From,
    StayMoney Amount,
    string Scope,
    string Source,
    string AmountKind = "Penalty",
    string Boundary = "From"
);

public sealed record HotelConditionEvidence(
    string Kind,
    HotelBoardEvidence[] Board,
    HotelCancellationWindow[] Cancellation,
    string Source
);
