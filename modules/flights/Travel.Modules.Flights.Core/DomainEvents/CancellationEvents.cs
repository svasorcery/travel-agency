using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.DomainEvents;

public sealed record BookingMutationCoordinationEnabled(DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record CancellationPreparationStarted(
    Guid OperationId,
    Guid OwnerId,
    string ProviderOrderRef,
    string ItineraryPartyHash,
    string Fingerprint,
    Guid AdmissionId,
    DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record CancellationPreparationDispatched(
    Guid OperationId,
    Guid AdmissionId,
    Guid DispatchOwnerInstanceId,
    RecoverySchedule Recovery,
    DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record CancellationTermsObtained(
    Guid OperationId,
    CancellationTerms Terms,
    DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record CancellationTermsWereUnavailable(
    Guid OperationId,
    bool CreationCompleted,
    string? ProviderCancellationRef,
    CancellationReason Reason,
    DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record CancellationTermsAccepted(
    Guid OperationId,
    long TermsRevision,
    string TermsHash,
    string NoticeVersion,
    string Fingerprint,
    Guid AdmissionId,
    DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record CancellationConfirmationDispatched(
    Guid OperationId,
    Guid AdmissionId,
    Guid DispatchOwnerInstanceId,
    RecoverySchedule Recovery,
    DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record CancellationObservationStarted(
    Guid OperationId,
    Guid Epoch,
    int Slot,
    Guid ReadId,
    RecoverySchedule Recovery,
    DateTimeOffset OccurredAt,
    bool ReadAllowed = true
) : IDomainEvent;

public sealed record CancellationObservationRecorded(
    Guid OperationId,
    Guid ReadId,
    CancellationObservation Observation,
    DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record CancellationOutcomeBecameUnknown(
    Guid OperationId,
    CancellationUnknownStage Stage,
    CancellationReason Reason,
    DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record CancellationSucceeded(
    Guid OperationId,
    CancellationEvidence Evidence,
    DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record CancellationRejected(
    Guid OperationId,
    CancellationUnknownStage Stage,
    CancellationResolutionSource Source,
    CancellationReason Reason,
    DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record CancellationManualReviewRequired(
    Guid OperationId,
    CancellationUnknownStage Stage,
    CancellationReason Reason,
    bool NotDispatched,
    DateTimeOffset OccurredAt,
    SupplierOrderCancellationFact? OrderCancellation = null
) : IDomainEvent;

public sealed record CancellationReviewWasAbandoned(Guid OperationId, DateTimeOffset OccurredAt)
    : IDomainEvent;

public sealed record CancellationReviewExpired(Guid OperationId, DateTimeOffset OccurredAt)
    : IDomainEvent;

public sealed record CancellationRefreshRequested(
    Guid OperationId,
    Guid RefreshRequestId,
    string Fingerprint,
    DateTimeOffset NextRefreshAt,
    DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record BookingOperationReviewRecorded(
    ManualResolutionTargetKind TargetKind,
    Guid TargetId,
    Guid ResolutionId,
    Guid ActorId,
    string PayloadHash,
    ManualResolutionDecisionKind Decision,
    ManualResolutionEvidence Evidence,
    CancellationResolutionSource Source,
    DateTimeOffset OccurredAt
) : IDomainEvent;
