using ErrorOr;

namespace Travel.Modules.Flights.Application.Webhooks;

public static class WebhookIngestionErrors
{
    public static Error UnsupportedProvider(string provider) =>
        Error.Validation(
            "Flights.Webhook.UnsupportedProvider",
            $"Webhook provider '{provider}' is not supported."
        );

    public static Error InvalidSignature =>
        Error.Unauthorized("Flights.Webhook.InvalidSignature", "Webhook signature is invalid.");

    public static Error InvalidPayload =>
        Error.Validation("Flights.Webhook.InvalidPayload", "Webhook payload is invalid.");
}
