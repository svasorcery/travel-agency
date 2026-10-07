using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.Aggregates;
using Travel.Modules.Flights.Core.Booking;
using BookingState = Travel.Modules.Flights.Core.Aggregates.BookingStatus;

namespace Travel.Modules.Flights.Api.Contracts;

public sealed record ActualPurchaseDto(MoneyStringDto Total, PurchaseServiceDto[] Services);

public sealed record BookingCreationResponse(
    Guid AggregateId,
    string State,
    string BookingStatus,
    int PassengerCount,
    Guid[] BookingPassengerIds,
    ItineraryDto? Itinerary,
    BookingPurchaseDto? Accepted,
    ActualPurchaseDto? Actual,
    DateTimeOffset? HeldUntil,
    bool CanConfirm,
    bool CanCancel,
    bool CanRefresh,
    DateTimeOffset ObservedAt
)
{
    public static BookingCreationResponse From(BookingCreationStatusResult result)
    {
        var b = result.Booking;
        var attempt = b.CurrentCreation;
        var accepted = attempt?.Accepted ?? b.Purchase;
        // A missing/dead-lettered worker cannot leave the owner waiting indefinitely.
        // This read-side diagnosis grants no new authority and does not rewrite evidence.
        var state =
            attempt is { Outcome: BookingCreationOutcome.InProgress }
            && result.ObservedAt >= attempt.StartedAt.AddSeconds(180)
                ? BookingCreationOutcome.ManualReviewRequired.ToString()
                : attempt?.Outcome.ToString() ?? "NotStarted";
        var canCancel =
            b.Status is BookingState.Held or BookingState.Confirmed or BookingState.Ticketed
            && !b.HasConfirmationBarrier
            && !b.HasUnresolvedCreation
            && (attempt is null || attempt.CreationCompleted && attempt.SenderCompleted)
            && (b.Status != BookingState.Held || b.MutationCoordinationEnabled)
            && b.CurrentCancellation is not { IsTerminal: false };
        return new(
            b.Id,
            state,
            b.Status.ToString(),
            b.PassengerCount,
            b.QuoteBinding?.Slots.Select(s => s.Id.Value).ToArray() ?? [],
            b.Itinerary is { } itinerary ? ItineraryDto.From(itinerary) : null,
            accepted is { } purchase ? BookingPurchaseDto.From(purchase) : null,
            attempt?.Actual is { } actual
                ? new(
                    MoneyStringDto.From(actual.Total),
                    actual.Services.Select(PurchaseServiceDto.From).ToArray()
                )
                : null,
            b.Status == BookingState.Held ? b.ExpiresAt : null,
            b.DecideConfirm(result.ObservedAt) is BookingTransitionDecision.Allowed
                && b.MutationCoordinationEnabled
                && !b.HasConfirmationBarrier
                && b.CurrentCancellation is not { IsTerminal: false },
            canCancel,
            b.Status == BookingState.OfferQuoted
                && !b.HasCreationBarrier
                && !b.HasConfirmationBarrier
                && b.CurrentCancellation is not { IsTerminal: false },
            result.ObservedAt
        );
    }
}
