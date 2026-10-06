using System.Security.Cryptography;
using System.Text.Json;
using ErrorOr;
using Marten;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Application.Persistence;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Cancellation;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Providers.Dtos;
using Wolverine.Attributes;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Handlers.Cancellation;

public static class RefreshCancellationHandler
{
    [NonTransactional]
    [WolverineHandler]
    public static async Task<ErrorOr<CancellationStatusResult>> Handle(
        RefreshCancellationCommand command,
        IDocumentSession session,
        IMartenOutbox outbox,
        TimeProvider time,
        CancellationToken ct
    )
    {
        if (
            command.AggregateId == Guid.Empty
            || command.ActorUserId == Guid.Empty
            || command.OperationId == Guid.Empty
            || command.RefreshRequestId == Guid.Empty
            || command.ExpectedOperationRevision <= 0
        )
            return CancellationDecisionWriter.InvalidCommand;
        var stream = await session.Events.FetchForWriting<BookingAggregate>(
            command.AggregateId,
            ct
        );
        var booking = stream.Aggregate;
        // IsOperator is constructed only by the separately authorized review endpoint, never request body.
        if (
            booking?.OwnerUserId is not { } owner
            || owner == Guid.Empty
            || !booking.HasConsistentMutationOwner
            || (!command.IsOperator && owner != command.ActorUserId)
        )
            return CancellationDecisionWriter.NotFound;
        var fingerprint = Convert
            .ToHexString(
                SHA256.HashData(
                    JsonSerializer.SerializeToUtf8Bytes(
                        new
                        {
                            Version = 1,
                            command.AggregateId,
                            command.ActorUserId,
                            command.OperationId,
                            command.ExpectedOperationRevision,
                            command.RefreshRequestId,
                            command.IsOperator,
                        }
                    )
                )
            )
            .ToLowerInvariant();
        var now = time.GetUtcNow();
        var decision = booking.DecideCancellationRefresh(
            owner,
            command.OperationId,
            command.ExpectedOperationRevision,
            command.RefreshRequestId,
            fingerprint,
            now
        );
        if (
            decision.Kind == CancellationDecisionKind.Rejected
            && decision.Reason == CancellationReason.RefreshTooSoon
        )
            return Error.Custom(
                429,
                "Flights.CancellationRefreshTooSoon",
                "Cancellation refresh is temporarily limited.",
                new Dictionary<string, object>
                {
                    ["retryAfter"] = Math.Max(
                        1,
                        (int)Math.Ceiling((booking.NextOwnerRefreshAt!.Value - now).TotalSeconds)
                    ),
                    ["nextRefreshAt"] = booking.NextOwnerRefreshAt.Value,
                }
            );
        IReadOnlyList<BookingWork> work = [];
        if (
            decision.Kind == CancellationDecisionKind.Allowed
            && decision.Events.OfType<CancellationRefreshRequested>().Any()
        )
        {
            var operation = booking.CancellationOperations[command.OperationId];
            if (operation.Recovery is { } schedule)
                work =
                [
                    new(
                        new ObserveCancellation(
                            command.AggregateId,
                            command.OperationId,
                            schedule.Epoch,
                            -1,
                            command.RefreshRequestId
                        ),
                        null
                    ),
                ];
        }
        return await CancellationDecisionWriter.Command(
            decision,
            stream,
            session,
            outbox,
            work,
            now,
            ct
        );
    }
}
