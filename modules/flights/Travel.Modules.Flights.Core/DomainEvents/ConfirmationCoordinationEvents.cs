using Travel.Modules.Flights.Core.Booking;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.DomainEvents;

public enum ConfirmationCaptureSource
{
    TestWalletObserved = 1,
}

public sealed record ConfirmationAttemptStarted(
    Guid AttemptId,
    Guid OwnerId,
    Guid AdmissionId,
    string Fingerprint,
    string ProviderOrderRef,
    Money AcceptedMoney,
    DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record ConfirmationEffectsClaimed(
    Guid AttemptId,
    Guid AdmissionId,
    Guid DispatchOwnerInstanceId,
    DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record ConfirmationPaymentReferenceRecorded(
    Guid AttemptId,
    PaymentRef PaymentReference,
    DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record ConfirmationCaptureObserved(
    Guid AttemptId,
    PaymentRef PaymentReference,
    Money AcceptedMoney,
    DateTimeOffset OccurredAt
) : IDomainEvent
{
    public ConfirmationCaptureSource Source => ConfirmationCaptureSource.TestWalletObserved;
}

public sealed record ConfirmationAttemptCompleted(
    Guid AttemptId,
    PaymentRef PaymentReference,
    Money AcceptedMoney,
    string ProviderOrderRef,
    SupplierPaymentEvidence SupplierPaymentEvidence,
    CancellationResolutionSource Source,
    DateTimeOffset OccurredAt,
    BookingServiceProof? ServiceProof = null
) : IDomainEvent;

public sealed record ConfirmationAttemptClosedWithoutEffects(
    Guid AttemptId,
    CancellationReason Reason,
    DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record ConfirmationAttemptRequiredManualReview(
    Guid AttemptId,
    CancellationReason Reason,
    DateTimeOffset OccurredAt
) : IDomainEvent;
