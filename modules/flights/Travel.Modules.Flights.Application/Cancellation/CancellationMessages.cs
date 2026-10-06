namespace Travel.Modules.Flights.Application.Cancellation;

public enum CancellationActionStage
{
    Prepare = 1,
    Confirm = 2,
}

public sealed record ExecuteCancellationPreparation(
    Guid AggregateId,
    Guid OperationId,
    Guid AdmissionId
)
{
    public override string ToString() => nameof(ExecuteCancellationPreparation);
}

public sealed record ExecuteCancellationConfirmation(
    Guid AggregateId,
    Guid OperationId,
    Guid AdmissionId
)
{
    public override string ToString() => nameof(ExecuteCancellationConfirmation);
}

public sealed record ObserveCancellation(
    Guid AggregateId,
    Guid OperationId,
    Guid RecoveryEpoch,
    int Slot,
    Guid? RefreshRequestId = null
)
{
    public override string ToString() => nameof(ObserveCancellation);
}

public sealed record AdmissionDeadline(
    Guid AggregateId,
    Guid OperationId,
    Guid AdmissionId,
    CancellationActionStage ActionStage
)
{
    public override string ToString() => nameof(AdmissionDeadline);
}

public sealed record RecoveryDeadline(Guid AggregateId, Guid OperationId, Guid RecoveryEpoch)
{
    public override string ToString() => nameof(RecoveryDeadline);
}

public sealed record ConfirmationBarrierDeadline(Guid AggregateId, Guid AttemptId, Guid AdmissionId)
{
    public override string ToString() => nameof(ConfirmationBarrierDeadline);
}
