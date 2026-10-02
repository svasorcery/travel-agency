using ErrorOr;
using Travel.Shared.Abstractions;

namespace Travel.Modules.Flights.Application.Webhooks;

public enum BookingWebhookKind
{
    TicketsAvailable,
    AirlineCancelled,
    AirlineChanged,
    Ignored,
}

public sealed record BookingWebhookFacts(
    BookingWebhookKind Kind,
    string? ProviderOrderId,
    EquatableArray<string> TicketNumbers
);

public interface IWebhookPayloadReader
{
    ErrorOr<BookingWebhookFacts> Read(WebhookInboxEntry entry);
}
