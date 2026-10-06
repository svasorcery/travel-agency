using Travel.Modules.Flights.Application.Cancellation;

namespace Travel.Modules.Flights.Api.Contracts;

public sealed record CancellationStatusResponse(
    Guid AggregateId,
    string BookingStatus,
    long BookingVersion,
    Guid? RequestedOperationId,
    Guid? CurrentOperationId,
    bool IsCurrentOperation,
    CancellationOperationResponse? Operation,
    BlockingConfirmationStatus? BlockingConfirmation,
    DateTimeOffset ServerNow,
    SupplierOrderCancellationStatus? SupplierOrderCancellation
)
{
    public static CancellationStatusResponse From(CancellationStatusResult value) =>
        new(
            value.AggregateId,
            value.BookingStatus,
            value.BookingVersion,
            value.RequestedOperationId,
            value.CurrentOperationId,
            value.IsCurrentOperation,
            value.Operation is { } operation ? CancellationOperationResponse.From(operation) : null,
            value.BlockingConfirmation,
            value.ServerNow,
            value.SupplierOrderCancellation
        );
}

public sealed record CancellationOperationResponse(
    Guid OperationId,
    long Revision,
    string Phase,
    string UnknownStage,
    string Outcome,
    string ResolutionSource,
    string ReasonCode,
    string DispatchState,
    CancellationTermsResponse? Terms,
    DateTimeOffset? NextRefreshAt,
    long? ConfirmedBookingVersion,
    bool ReadPending
)
{
    public static CancellationOperationResponse From(CancellationOperationStatus value) =>
        new(
            value.OperationId,
            value.Revision,
            value.Phase,
            value.UnknownStage,
            value.Outcome,
            value.ResolutionSource,
            value.ReasonCode,
            value.DispatchState,
            value.Terms is { } terms ? CancellationTermsResponse.From(terms) : null,
            value.NextRefreshAt,
            value.ConfirmedBookingVersion,
            value.ReadPending
        );
}

public sealed record CancellationTermsResponse(
    long Revision,
    string Hash,
    string RefundAmount,
    string RefundCurrency,
    string RefundDestination,
    DateTimeOffset ExpiresAt,
    string NoticeVersion,
    int PassengerCount,
    ItineraryDto? WholeOrderItinerary,
    string FinancialSource
)
{
    public static CancellationTermsResponse From(CancellationTermsView value) =>
        new(
            value.Revision,
            value.Hash,
            value.RefundAmount,
            value.RefundCurrency,
            value.RefundDestination,
            value.ExpiresAt,
            value.NoticeVersion,
            value.PassengerCount,
            value.WholeOrderItinerary is { } itinerary ? ItineraryDto.From(itinerary) : null,
            value.FinancialSource
        );
}

public sealed record CancellationReviewResponse(
    CancellationStatusResponse Status,
    CancellationReviewTarget? Target,
    IReadOnlyList<CancellationReviewAudit> History
)
{
    public static CancellationReviewResponse From(CancellationReviewResult value) =>
        new(CancellationStatusResponse.From(value.Status), value.Target, value.History);
}
