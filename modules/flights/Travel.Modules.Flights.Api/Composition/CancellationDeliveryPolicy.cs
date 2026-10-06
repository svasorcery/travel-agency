using JasperFx;
using JasperFx.CodeGeneration;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Cancellation;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace Travel.Modules.Flights.Api.Composition;

public sealed class CancellationDeliveryPolicy : IHandlerPolicy
{
    public const string Queue = "flights-cancellation-work";
    private static readonly HashSet<Type> WorkTypes =
    [
        typeof(ExecuteCancellationPreparation),
        typeof(ExecuteCancellationConfirmation),
        typeof(ObserveCancellation),
        typeof(AdmissionDeadline),
        typeof(RecoveryDeadline),
        typeof(ConfirmationBarrierDeadline),
    ];

    public static void Configure(WolverineOptions options)
    {
        options.LocalQueue(Queue).UseDurableInbox();
        options.PublishMessage<ExecuteCancellationPreparation>().ToLocalQueue(Queue);
        options.PublishMessage<ExecuteCancellationConfirmation>().ToLocalQueue(Queue);
        options.PublishMessage<ObserveCancellation>().ToLocalQueue(Queue);
        options.PublishMessage<AdmissionDeadline>().ToLocalQueue(Queue);
        options.PublishMessage<RecoveryDeadline>().ToLocalQueue(Queue);
        options.PublishMessage<ConfirmationBarrierDeadline>().ToLocalQueue(Queue);
        options.Policies.Add<CancellationDeliveryPolicy>();
    }

    public void Apply(
        IReadOnlyList<HandlerChain> chains,
        GenerationRules rules,
        IServiceContainer container
    )
    {
        foreach (var chain in chains.Where(chain => WorkTypes.Contains(chain.MessageType)))
        {
            chain
                .OnException(
                    error => Classify(error) is BookingProjectionTransientException,
                    "Cancellation transient storage failure"
                )
                .ScheduleRetry(
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(30)
                )
                .Then.MoveToErrorQueue();
            chain
                .OnException<BookingWriteConflictException>()
                .ScheduleRetry(
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(30)
                )
                .Then.MoveToErrorQueue();
            chain
                .OnException(
                    error => Classify(error) is BookingProjectionTerminalException,
                    "Cancellation terminal storage failure"
                )
                .MoveToErrorQueue();
            chain.OnException<BookingSourceOwnershipMissingException>().MoveToErrorQueue();
            chain
                .OnException(
                    error => error is not OperationCanceledException,
                    "Unexpected cancellation failure"
                )
                .MoveToErrorQueue();
        }
    }

    private static Exception Classify(Exception error) =>
        BookingStorageFailureClassifier.Classify(
            error,
            error is OperationCanceledException cancelled
                ? cancelled.CancellationToken
                : CancellationToken.None
        );
}
