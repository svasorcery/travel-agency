using Travel.Modules.Flights.Application.Persistence;
using Travel.Modules.Flights.Core.Cancellation;

namespace Travel.Modules.Flights.Application.Cancellation;

public static class CancellationWorkFactory
{
    public static IReadOnlyList<BookingWork> Recovery(
        Guid aggregateId,
        Guid operationId,
        RecoverySchedule schedule
    )
    {
        int[] seconds = [2, 10, 60, 300];
        return seconds
            .Select(
                (delay, slot) =>
                    new BookingWork(
                        new ObserveCancellation(aggregateId, operationId, schedule.Epoch, slot),
                        schedule.ClaimedAt.AddSeconds(delay)
                    )
            )
            .Append(
                new(
                    new RecoveryDeadline(aggregateId, operationId, schedule.Epoch),
                    schedule.Deadline
                )
            )
            .ToArray();
    }

    public static IReadOnlyList<BookingWork> PreparationAdmission(
        Guid aggregateId,
        Guid operationId,
        Guid admission,
        DateTimeOffset now
    ) =>
        [
            new(new ExecuteCancellationPreparation(aggregateId, operationId, admission), null),
            new(
                new AdmissionDeadline(
                    aggregateId,
                    operationId,
                    admission,
                    CancellationActionStage.Prepare
                ),
                now.AddSeconds(310)
            ),
        ];

    public static IReadOnlyList<BookingWork> ConfirmationAdmission(
        Guid aggregateId,
        Guid operationId,
        Guid admission,
        DateTimeOffset now
    ) =>
        [
            new(new ExecuteCancellationConfirmation(aggregateId, operationId, admission), null),
            new(
                new AdmissionDeadline(
                    aggregateId,
                    operationId,
                    admission,
                    CancellationActionStage.Confirm
                ),
                now.AddSeconds(310)
            ),
        ];
}
