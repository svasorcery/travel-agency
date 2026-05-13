namespace Travel.Modules.Flights.Core.Providers.Dtos;

public sealed record OrderStatus(
    string ProviderOrderId,
    string Status,
    IReadOnlyList<string> TicketNumbers
);
