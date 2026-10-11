using Travel.Modules.Flights.Core.Cancellation;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.DomainEvents;

public sealed record SupplierCancellationReported(
    Guid ReportId,
    Guid OwnerId,
    string ProviderOrderRef,
    DateTimeOffset OccurredAt
) : IDomainEvent;

public sealed record SupplierCancellationReportResolved(
    Guid ReportId,
    SupplierCancellationReportState State,
    DateTimeOffset? CancelledAt,
    DateTimeOffset OccurredAt
) : IDomainEvent;
