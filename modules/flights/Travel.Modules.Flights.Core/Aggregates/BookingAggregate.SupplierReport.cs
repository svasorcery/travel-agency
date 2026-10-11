using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.Aggregates;

public sealed partial class BookingAggregate
{
    private readonly Dictionary<Guid, SupplierCancellationReport> supplierCancellationReports =
        new();
    public IReadOnlyDictionary<Guid, SupplierCancellationReport> SupplierCancellationReports =>
        new System.Collections.ObjectModel.ReadOnlyDictionary<Guid, SupplierCancellationReport>(
            supplierCancellationReports
        );
    public Guid? CurrentSupplierCancellationReportId { get; private set; }
    public SupplierCancellationReport? CurrentSupplierCancellationReport =>
        CurrentSupplierCancellationReportId is { } id
        && supplierCancellationReports.TryGetValue(id, out var report)
            ? report
            : null;
    public bool HasSupplierCancellationReportBarrier =>
        CurrentSupplierCancellationReport is { State: SupplierCancellationReportState.Unverified };

    public CancellationDecision DecideSupplierCancellationReport(
        Guid reportId,
        string providerOrderRef,
        DateTimeOffset now
    )
    {
        if (
            !HasConsistentMutationOwner
            || Id == Guid.Empty
            || reportId == Guid.Empty
            || providerOrderRef != ProviderOrderId
            || now == default
        )
            return RejectCancellation(CancellationReason.InconsistentEvidence);
        if (
            supplierCancellationReports.ContainsKey(reportId)
            || HasSupplierCancellationReportBarrier
        )
            return NoOpCancellation();
        return AllowCancellation(
            reportId,
            new SupplierCancellationReported(reportId, OwnerUserId!.Value, providerOrderRef, now)
        );
    }

    public void Apply(SupplierCancellationReported e)
    {
        supplierCancellationReports[e.ReportId] = new(
            e.ReportId,
            e.OwnerId,
            e.ProviderOrderRef,
            e.OccurredAt
        );
        CurrentSupplierCancellationReportId = e.ReportId;
    }

    public void Apply(SupplierCancellationReportResolved e)
    {
        var report = supplierCancellationReports[e.ReportId];
        supplierCancellationReports[e.ReportId] = report with
        {
            State = e.State,
            CancelledAt = e.CancelledAt,
            ResolutionSource = CancellationResolutionSource.OperatorVerified,
        };
        if (
            e.State == SupplierCancellationReportState.ConfirmedCancellation
            && e.CancelledAt is { } at
        )
            ObservedSupplierCancellation = new SupplierOrderCancellationFact(
                report.ProviderOrderRef,
                null,
                at,
                e.OccurredAt,
                CancellationResolutionSource.OperatorVerified
            );
    }

    private CancellationDecision DecideSupplierReportResolution(
        Guid actor,
        ManualResolutionInput input,
        DateTimeOffset now
    )
    {
        var e = input.Evidence;
        if (
            actor == Guid.Empty
            || !HasConsistentMutationOwner
            || input.AggregateId != Id
            || input.TargetId == Guid.Empty
            || input.ResolutionId == Guid.Empty
            || input.ExpectedRevision <= 0
            || e is null
            || !EvidenceReference(e.EvidenceRef)
            || e.ObservedAt == default
            || e.ObservedAt > now
            || !Enum.IsDefined(e.Category)
            || !supplierCancellationReports.TryGetValue(input.TargetId, out var report)
            || report.ProviderOrderRef != ProviderOrderId
            || report.OwnerId != OwnerUserId
            || e.Refund is not null
            || e.AcceptedMoney is not null
            || e.PaymentReference is not null
            || e.ProviderOrderRef is not null
            || e.ProviderCancellationRef is not null
            || e.Destination is not null
            || e.Settlement is not null
            || e.SupplierReceiptRef is not null
            || e.CreationEvidence is not null
            || e.ServiceProof is not null
            || e.WalletCaptureConfirmed
            || e.SupplierBookingPaymentConfirmed
            || e.SupplierFinalNoEffectsConfirmed
            || e.WalletFinalNoEffectsConfirmed
            || e.PaymentCorrelationAttested
            || e.PreparationCorrelationAttested
            || e.TermsExpiresAt is not null
            || e.ConfirmedAt is not null
            || e.ItineraryPartyHash is not null
            || e.QuiescenceRef is not null
            || e.StoppedAt is not null
            || e.StoppedInstanceIds is not null
            || e.SenderEgressIsolated
            || e.OldInstancesCannotResume
            || e.LegacyMutationFleetDrained
        )
            return RejectCancellation(CancellationReason.InconsistentEvidence);
        var proof = e.ReportEvidence;
        if (
            proof is not null
            && (proof.ReportId != report.Id || proof.ProviderOrderRef != report.ProviderOrderRef)
        )
            return RejectCancellation(CancellationReason.InconsistentEvidence);
        var payload = Convert
            .ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        JsonSerializer.Serialize(
                            new
                            {
                                Version = 3,
                                input.AggregateId,
                                input.TargetId,
                                input.ExpectedRevision,
                                input.Decision,
                                e.EvidenceRef,
                                e.Category,
                                ObservedAt = e.ObservedAt.UtcTicks,
                                Report = proof is null
                                    ? null
                                    : new
                                    {
                                        proof.ReportId,
                                        proof.ProviderOrderRef,
                                        proof.CancellationConfirmed,
                                        proof.NotCancelledConfirmed,
                                        proof.NoPendingCancellationConfirmed,
                                        CancelledAt = proof.CancelledAt?.UtcTicks,
                                    },
                            }
                        )
                    )
                )
            )
            .ToLowerInvariant();
        if (manualResolutionReceipts.TryGetValue(input.ResolutionId, out var prior))
            return
                prior.ActorId == actor
                && prior.TargetKind == input.TargetKind
                && prior.TargetId == input.TargetId
                && prior.PayloadHash == payload
                ? NoOpCancellation()
                : RejectCancellation(CancellationReason.InconsistentEvidence);
        if (
            CurrentSupplierCancellationReportId != report.Id
            || report.Revision != input.ExpectedRevision
            || report.State != SupplierCancellationReportState.Unverified
            || e.ObservedAt < report.ObservedAt
        )
            return RejectCancellation(CancellationReason.StaleProposal);
        if (
            input.Decision == ManualResolutionDecisionKind.RecordInconclusive
            && e.Category
                is ManualEvidenceCategory.Inconclusive
                    or ManualEvidenceCategory.SupplierSupportAttestation
        )
            return AppendManualAudit(
                actor,
                input,
                payload,
                e,
                CancellationResolutionSource.OperatorVerified,
                AllowCancellation(report.Id),
                now
            );
        if (
            proof is null
            || e.Category
                is not (
                    ManualEvidenceCategory.SupplierSupportAttestation
                    or ManualEvidenceCategory.StoredSupplierObservation
                )
        )
            return RejectCancellation(CancellationReason.InconsistentEvidence);
        IDomainEvent[] facts;
        if (
            input.Decision == ManualResolutionDecisionKind.ConfirmCancellation
            && proof.CancellationConfirmed
            && !proof.NotCancelledConfirmed
            && proof.CancelledAt is { } cancelled
            && cancelled != default
            && cancelled <= e.ObservedAt
        )
        {
            var resolved = new SupplierCancellationReportResolved(
                report.Id,
                SupplierCancellationReportState.ConfirmedCancellation,
                cancelled,
                now
            );
            facts = Status is BookingStatus.Cancelled or BookingStatus.Refunded
                ? [resolved]
                : [resolved, new OrderCancelled(CancelReason.Airline, cancelled)];
        }
        else if (
            input.Decision == ManualResolutionDecisionKind.ConfirmNotCancelled
            && !proof.CancellationConfirmed
            && proof.NotCancelledConfirmed
            && proof.NoPendingCancellationConfirmed
            && proof.CancelledAt is null
            && Status is not (BookingStatus.Cancelled or BookingStatus.Refunded)
            && ObservedSupplierCancellation is null
        )
            facts =
            [
                new SupplierCancellationReportResolved(
                    report.Id,
                    SupplierCancellationReportState.ConfirmedNotCancelled,
                    null,
                    now
                ),
            ];
        else
            return RejectCancellation(CancellationReason.InconsistentEvidence);
        return AppendManualAudit(
            actor,
            input,
            payload,
            e,
            CancellationResolutionSource.OperatorVerified,
            AllowCancellation(report.Id, facts),
            now
        );
    }
}
