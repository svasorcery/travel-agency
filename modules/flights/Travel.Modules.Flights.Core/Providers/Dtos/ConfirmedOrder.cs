namespace Travel.Modules.Flights.Core.Providers.Dtos;

public sealed record ConfirmedOrder(string ProviderOrderId, DateTimeOffset ConfirmedAt);
