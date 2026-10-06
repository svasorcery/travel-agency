using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Application.Cancellation;

public sealed record CancellationStatusResult(
    Guid AggregateId,
    string BookingStatus,
    long BookingVersion,
    Guid? RequestedOperationId,
    Guid? CurrentOperationId,
    bool IsCurrentOperation,
    CancellationOperationStatus? Operation,
    BlockingConfirmationStatus? BlockingConfirmation,
    DateTimeOffset ServerNow,
    SupplierOrderCancellationStatus? SupplierOrderCancellation = null
);

public sealed record CancellationOperationStatus(
    Guid OperationId,
    long Revision,
    string Phase,
    string UnknownStage,
    string Outcome,
    string ResolutionSource,
    string ReasonCode,
    string DispatchState,
    CancellationTermsView? Terms,
    DateTimeOffset? NextRefreshAt,
    long? ConfirmedBookingVersion,
    bool ReadPending = false
);

public sealed record CancellationTermsView(
    long Revision,
    string Hash,
    string RefundAmount,
    string RefundCurrency,
    string RefundDestination,
    DateTimeOffset ExpiresAt,
    string NoticeVersion,
    int PassengerCount,
    Itinerary? WholeOrderItinerary,
    string FinancialSource
);

public sealed record BlockingConfirmationStatus(
    string Kind,
    Guid TargetId,
    long Revision,
    string Phase,
    string ReasonCode,
    bool CanCloseNotDispatched
);

public sealed record SupplierOrderCancellationStatus(
    DateTimeOffset CancelledAt,
    string Source,
    bool OwnConsentConfirmed
);
