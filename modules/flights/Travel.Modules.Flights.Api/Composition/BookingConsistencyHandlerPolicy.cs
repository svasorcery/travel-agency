using JasperFx;
using JasperFx.CodeGeneration;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace Travel.Modules.Flights.Api.Composition;

public sealed class BookingConsistencyHandlerPolicy : IHandlerPolicy
{
    public const string ReconcileQueue = "flights-booking-reconcile";

    public static void Configure(WolverineOptions options)
    {
        // The scoped reconciler depends on the module-owned EF options factory.
        options.CodeGeneration.AlwaysUseServiceLocationFor<IOrderReadModelReconciler>();
        options.LocalQueue(ReconcileQueue).UseDurableInbox();
        options.PublishMessage<ReconcileOrderReadModel>().ToLocalQueue(ReconcileQueue);
        options.Policies.Add<BookingConsistencyHandlerPolicy>();
    }

    public void Apply(
        IReadOnlyList<HandlerChain> chains,
        GenerationRules rules,
        IServiceContainer container
    )
    {
        foreach (var chain in chains)
        {
            var reconcile = chain.MessageType == typeof(ReconcileOrderReadModel);
            var webhook = chain.MessageType == typeof(ProcessDuffelWebhookCommand);
            var notification =
                chain.MessageType == typeof(OrderConfirmedNotification)
                || chain.MessageType == typeof(OrderTicketedNotification)
                || chain.MessageType == typeof(OrderCancelledNotification);
            if (!reconcile && !webhook && !notification)
                continue;

            chain
                .OnException(
                    error => Classify(error) is BookingProjectionTransientException,
                    "Booking transient storage failure"
                )
                .ScheduleRetry(
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(30)
                )
                .Then.MoveToErrorQueue();
            chain
                .OnException(
                    error => Classify(error) is BookingProjectionTerminalException,
                    "Booking terminal storage or source failure"
                )
                .MoveToErrorQueue();

            if (webhook)
            {
                chain
                    .OnException<BookingWriteConflictException>()
                    .ScheduleRetry(
                        TimeSpan.FromMilliseconds(100),
                        TimeSpan.FromMilliseconds(500),
                        TimeSpan.FromSeconds(1)
                    )
                    .Then.MoveToErrorQueue();
                chain
                    .OnException<BookingCorrelationNotReadyException>()
                    .Or<BookingTransitionPrerequisiteException>()
                    .ScheduleRetry(
                        TimeSpan.FromSeconds(2),
                        TimeSpan.FromSeconds(10),
                        TimeSpan.FromMinutes(1),
                        TimeSpan.FromMinutes(5)
                    )
                    .Then.MoveToErrorQueue();
                chain
                    .OnException<BookingSourceOwnershipMissingException>()
                    .Or<BookingTransitionRejectedException>()
                    .MoveToErrorQueue();
            }
            if (notification)
                chain
                    .OnException<BookingReadModelNotReadyException>()
                    .ScheduleRetry(
                        TimeSpan.FromSeconds(2),
                        TimeSpan.FromSeconds(10),
                        TimeSpan.FromMinutes(1),
                        TimeSpan.FromMinutes(5)
                    )
                    .Then.MoveToErrorQueue();
            if (reconcile)
                chain
                    .OnException(
                        error => error is not OperationCanceledException,
                        "Unexpected reconcile failure"
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
