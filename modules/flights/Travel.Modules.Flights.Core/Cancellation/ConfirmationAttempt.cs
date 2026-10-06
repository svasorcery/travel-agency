using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;

namespace Travel.Modules.Flights.Core.Cancellation;

public enum ConfirmationAttemptPhase
{
    None = 0,
    Started = 1,
    EffectsClaimed = 2,
    Unknown = 3,
    ManualReviewRequired = 4,
    Completed = 5,
    NotDispatched = 6,
}

public sealed record ConfirmationAttempt
{
    public Guid Id { get; init; }
    public Guid OwnerId { get; init; }
    public long Revision { get; init; }
    public Guid AdmissionId { get; init; }
    public string Fingerprint { get; init; } = "";
    public string ProviderOrderRef { get; init; } = "";
    public Money? AcceptedMoney { get; init; }
    public DateTimeOffset AdmittedAt { get; init; }
    public ConfirmationAttemptPhase Phase { get; init; }
    public Guid? DispatchOwnerInstanceId { get; init; }
    public DateTimeOffset? EffectsClaimedAt { get; init; }
    public PaymentRef? PaymentReference { get; init; }
    public DateTimeOffset? CapturedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public SupplierPaymentEvidence? SupplierPaymentEvidence { get; init; }
    public CancellationResolutionSource ResolutionSource { get; init; }
    public CancellationReason Reason { get; init; }
    public bool IsTerminal =>
        Phase is ConfirmationAttemptPhase.Completed or ConfirmationAttemptPhase.NotDispatched;

    public override string ToString() => nameof(ConfirmationAttempt);
}
