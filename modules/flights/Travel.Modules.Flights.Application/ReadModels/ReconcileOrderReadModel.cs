namespace Travel.Modules.Flights.Application.ReadModels;

/// <summary>
/// Internal durable request to catch the EF order read model up to its Marten source stream.
/// </summary>
public sealed record ReconcileOrderReadModel(Guid AggregateId);
