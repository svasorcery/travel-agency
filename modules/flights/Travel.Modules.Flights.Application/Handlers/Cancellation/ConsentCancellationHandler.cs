using ErrorOr;
using Marten;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Core.Aggregates;
using Wolverine.Attributes;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Handlers.Cancellation;

public static class ConsentCancellationHandler
{
    [NonTransactional]
    [WolverineHandler]
    public static async Task<ErrorOr<CancellationStatusResult>> Handle(
        ConsentCancellationCommand command,
        IDocumentSession session,
        IMartenOutbox outbox,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (
            command.AggregateId == Guid.Empty
            || command.UserId == Guid.Empty
            || command.OperationId == Guid.Empty
            || command.ExpectedOperationRevision <= 0
            || command.TermsRevision <= 0
            || !command.Accepted
        )
            return CancellationDecisionWriter.InvalidCommand;
        var stream = await session.Events.FetchForWriting<BookingAggregate>(
            command.AggregateId,
            ct
        );
        if (!CancellationDecisionWriter.Owned(stream.Aggregate, command.UserId))
            return CancellationDecisionWriter.NotFound;
        var now = time.GetUtcNow();
        var admission = Guid.NewGuid();
        var decision = stream.Aggregate!.DecideCancellationConsent(
            command.UserId,
            command.OperationId,
            command.ExpectedOperationRevision,
            command.TermsRevision,
            command.TermsHash,
            command.NoticeVersion,
            command.Fingerprint,
            admission,
            now
        );
        return await CancellationDecisionWriter.Command(
            decision,
            stream,
            session,
            outbox,
            CancellationWorkFactory.ConfirmationAdmission(
                command.AggregateId,
                command.OperationId,
                admission,
                now
            ),
            now,
            ct
        );
    }
}
