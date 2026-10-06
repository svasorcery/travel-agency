using Travel.Modules.Flights.Core.Cancellation;

namespace Travel.Modules.Flights.Application.Cancellation;

public sealed record PrepareCancellationCommand(
    Guid AggregateId,
    Guid UserId,
    Guid OperationId,
    long ExpectedBookingVersion,
    string Fingerprint
)
{
    public override string ToString() => nameof(PrepareCancellationCommand);
}

public sealed record ConsentCancellationCommand(
    Guid AggregateId,
    Guid UserId,
    Guid OperationId,
    long ExpectedOperationRevision,
    long TermsRevision,
    string TermsHash,
    string NoticeVersion,
    bool Accepted,
    string Fingerprint
)
{
    public override string ToString() => nameof(ConsentCancellationCommand);
}

public sealed record AbandonCancellationCommand(
    Guid AggregateId,
    Guid UserId,
    Guid OperationId,
    long ExpectedOperationRevision
)
{
    public override string ToString() => nameof(AbandonCancellationCommand);
}

public sealed record RefreshCancellationCommand(
    Guid AggregateId,
    Guid ActorUserId,
    Guid OperationId,
    long ExpectedOperationRevision,
    Guid RefreshRequestId,
    bool IsOperator
)
{
    public override string ToString() => nameof(RefreshCancellationCommand);
}

public sealed record GetCancellationStatusQuery(Guid AggregateId, Guid UserId)
{
    public override string ToString() => nameof(GetCancellationStatusQuery);
}

public sealed record OperatorActor(Guid UserId);

public sealed record GetCancellationReviewQuery(Guid AggregateId, OperatorActor Actor)
{
    public override string ToString() => nameof(GetCancellationReviewQuery);
}

public sealed record ResolveCancellationReviewCommand(
    OperatorActor Actor,
    ManualResolutionInput Input
)
{
    public override string ToString() => nameof(ResolveCancellationReviewCommand);
}
