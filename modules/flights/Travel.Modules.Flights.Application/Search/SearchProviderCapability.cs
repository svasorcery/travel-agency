namespace Travel.Modules.Flights.Application.Search;

public sealed record SearchProviderCapability(string Provider, bool Supported, string? ReasonCode);
