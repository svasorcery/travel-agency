using Travel.Shared.Abstractions;

namespace Travel.Modules.Trips.Core.DomainEvents;

public sealed record TripPlanCreated(Guid TripId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed record TripPlanRevised(Guid TripId, long Revision, DateTimeOffset OccurredAt)
    : IDomainEvent;

public sealed record TripPlanDeleted(Guid TripId, long Revision, DateTimeOffset OccurredAt)
    : IDomainEvent;
