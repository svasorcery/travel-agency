using ErrorOr;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Core.Errors;

public static class FlightsErrors
{
    public static Error OfferExpired =>
        Error.Validation("Flights.OfferExpired", "Offer has expired, please refresh.");

    public static Error OfferNotFound(string offerRef) =>
        Error.NotFound("Flights.OfferNotFound", $"Offer '{offerRef}' not found.");

    public static Error PriceChanged(Money old, Money @new) =>
        Error.Conflict("Flights.PriceChanged", $"Price changed from {old} to {@new}.");

    public static Error ProviderUnavailable(string provider) =>
        Error.Failure("Flights.ProviderUnavailable", $"Provider {provider} unavailable.");

    public static Error ProviderRateLimited(string provider) =>
        Error.Failure("Flights.ProviderRateLimited", $"Provider {provider} rate limited.");

    public static Error PaymentFailed(string reason) =>
        Error.Failure("Flights.PaymentFailed", reason);

    public static Error PassengerInvalid(string detail) =>
        Error.Validation("Flights.PassengerInvalid", detail);

    public static Error OrderNotCancellable(string reason) =>
        Error.Conflict("Flights.OrderNotCancellable", reason);

    public static Error IdempotencyConflict =>
        Error.Conflict(
            "Flights.IdempotencyConflict",
            "Idempotency key reused with different payload."
        );

    public static Error ConcurrencyConflict =>
        Error.Conflict(
            "Flights.ConcurrencyConflict",
            "The booking was modified concurrently; retry."
        );

    public static Error NlSearchUnparseable =>
        Error.Validation("Flights.NlSearchUnparseable", "Could not parse the query.");

    /// <summary>
    /// NL search is administratively disabled via feature flag.
    /// Maps to HTTP 503 (Service Unavailable) — not 500 — so clients can distinguish
    /// "feature turned off" from a server crash.
    /// Uses <see cref="Error.Custom"/> with numeric type 503; <c>ErrorOrExtensions.ToProblemDetails</c>
    /// handles <c>Error.NumericType == 503</c> as status 503.
    /// </summary>
    public static Error NlSearchDisabled =>
        Error.Custom(
            503,
            "Flights.NlSearchDisabled",
            "Natural-language search is currently disabled."
        );
}
