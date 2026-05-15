using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Core.Providers.Dtos;

public enum OrderStatusKind
{
    /// <summary>Order is confirmed and payment has been taken but tickets not yet issued.</summary>
    Confirmed,

    /// <summary>Order has been cancelled.</summary>
    Cancelled,

    /// <summary>Ticket documents have been issued (e-ticket numbers present).</summary>
    Ticketed,
}

public sealed record OrderStatus(
    string ProviderOrderId,
    OrderStatusKind Status,
    EquatableArray<string> TicketNumbers
);
