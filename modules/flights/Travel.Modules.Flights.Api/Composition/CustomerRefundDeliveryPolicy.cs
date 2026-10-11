using JasperFx;
using JasperFx.CodeGeneration;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.ReadModels;
using Travel.Modules.Flights.Application.Refunds;
using Travel.Modules.Flights.Infrastructure.Persistence;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;

namespace Travel.Modules.Flights.Api.Composition;

public sealed class CustomerRefundDeliveryPolicy : IHandlerPolicy
{
    public const string Queue = "flights-customer-refund-work";
    private static readonly HashSet<Type> WorkTypes =
    [
        typeof(AssessCustomerRefund),
        typeof(ExecuteCustomerRefund),
        typeof(CustomerRefundDeadline),
    ];

    public static void Configure(WolverineOptions options)
    {
        options.LocalQueue(Queue).UseDurableInbox();
        options.PublishMessage<AssessCustomerRefund>().ToLocalQueue(Queue);
        options.PublishMessage<ExecuteCustomerRefund>().ToLocalQueue(Queue);
        options.PublishMessage<CustomerRefundDeadline>().ToLocalQueue(Queue);
        options.Policies.Add<CustomerRefundDeliveryPolicy>();
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
                    "Customer refund transient storage failure"
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
                    "Customer refund terminal storage failure"
                )
                .MoveToErrorQueue();
            chain.OnException<BookingSourceOwnershipMissingException>().MoveToErrorQueue();
            chain
                .OnException(
                    error => error is not OperationCanceledException,
                    "Unexpected customer refund failure"
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
