using JasperFx;
using JasperFx.CodeGeneration;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace Travel.Modules.Flights.Api.Composition;

public sealed class BookingCreationDeliveryPolicy : IHandlerPolicy
{
    public static void Configure(WolverineOptions options)
    {
        options.LocalQueue("flights-booking-creation-work").UseDurableInbox();
        options
            .PublishMessage<CheckBookingCreation>()
            .ToLocalQueue("flights-booking-creation-work");
        options.Policies.Add<BookingCreationDeliveryPolicy>();
    }

    public void Apply(
        IReadOnlyList<HandlerChain> chains,
        GenerationRules rules,
        IServiceContainer container
    )
    {
        foreach (var chain in chains.Where(c => c.MessageType == typeof(CheckBookingCreation)))
        {
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
                    error =>
                        BookingStorageFailureClassifier.Classify(
                            error,
                            error is OperationCanceledException cancelled
                                ? cancelled.CancellationToken
                                : CancellationToken.None
                        ) is BookingProjectionTransientException,
                    "Creation read storage failure"
                )
                .ScheduleRetry(
                    TimeSpan.FromSeconds(1),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(30)
                )
                .Then.MoveToErrorQueue();
            chain
                .OnException(
                    error => error is not OperationCanceledException,
                    "Unexpected creation read failure"
                )
                .MoveToErrorQueue();
        }
    }
}
