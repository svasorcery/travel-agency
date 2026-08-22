namespace Travel.Modules.Flights.Application.Webhooks;

public sealed record WebhookIngestionRequest(
    string Provider,
    ReadOnlyMemory<byte> Payload,
    IReadOnlyDictionary<string, string> Headers
);
