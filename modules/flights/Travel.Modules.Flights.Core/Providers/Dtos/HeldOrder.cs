namespace Travel.Modules.Flights.Core.Providers.Dtos;

public sealed record HeldOrder(string ProviderOrderId, DateTimeOffset HeldUntil);
