using System.Diagnostics;
using ErrorOr;
using Marten;
using Microsoft.Extensions.Logging;
using Travel.Modules.Flights.Application.Booking;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Contracts;
using Travel.Modules.Flights.Application.Observability;
using Travel.Modules.Flights.Application.Persistence;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.DomainEvents;
using Travel.Modules.Flights.Core.Errors;
using Travel.Modules.Flights.Core.Providers;
using Wolverine.Attributes;
using Wolverine.Marten;

namespace Travel.Modules.Flights.Application.Handlers.Booking;

public static class CancelOrderHandler
{
    // The explicit booking helper owns the commit and conflict translation.
    // Do not let generated middleware attempt a second save after a rejected write.
    [NonTransactional]
    [WolverineHandler]
    public static async Task<ErrorOr<CancelledOrderResult>> Handle(
        CancelOrderCommand cmd,
        IDocumentSession marten,
        IEnumerable<IFlightBookingProvider> bookingProviders,
        IFlightsMetrics metrics,
        IMartenOutbox outbox,
        TimeProvider time,
        ILogger<CancelOrderCommand> log,
        CancellationToken ct
    )
    {
        if (cmd.AggregateId == Guid.Empty)
            return Error.Validation(
                "Flights.CommandInvalid",
                "CancelOrderCommand.AggregateId is required."
            );
        if (cmd.UserId == Guid.Empty)
            return Error.Validation(
                "Flights.CommandInvalid",
                "CancelOrderCommand.UserId is required."
            );

        // 1. Load aggregate with optimistic concurrency tracking.
        var stream = await marten.Events.FetchForWriting<BookingAggregate>(cmd.AggregateId, ct);
        var agg = stream.Aggregate;
        if (agg is null)
            return FlightsErrors.OfferNotFound(cmd.AggregateId.ToString());

        // Bodyless legacy requests cannot approve current whole-order supplier terms.
        var ownerDecision = agg.DecideOwner(BookingTransition.Cancel, cmd.UserId);
        if (ownerDecision is BookingTransitionDecision.Rejected ownerRejected)
            return BookingTransitionErrorMapper.ToOwnerError(ownerRejected.Reason, cmd.AggregateId);
        if (agg.Status is BookingStatus.Cancelled or BookingStatus.Refunded)
            return CreateResult(agg);
        if (agg.Status is BookingStatus.Held or BookingStatus.Confirmed or BookingStatus.Ticketed)
            return Error.Conflict(
                "Flights.CancellationTermsRequired",
                "Review and accept current cancellation terms."
            );
        return FlightsErrors.InvalidState(BookingTransition.Cancel, agg.Status);
    }

    private static CancelledOrderResult CreateResult(BookingAggregate aggregate) =>
        new(
            aggregate.Id,
            aggregate.Status.ToString(),
            new OrderCommandSnapshot(
                aggregate.TotalAmount!,
                aggregate.Itinerary!,
                aggregate.TicketNumbers.ToArray(),
                aggregate.BookedAt ?? aggregate.ConfirmedAt ?? default,
                aggregate.TicketedAt,
                aggregate.CancelledAt,
                aggregate.RefundedAt,
                aggregate.PassengerCount
            )
        );
}
