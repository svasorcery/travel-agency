using ErrorOr;

namespace Travel.Modules.Flights.Application.Webhooks;

public sealed class WebhookIngestionService(IWebhookIngestionPort port) : IWebhookIngestionService
{
    public Task<ErrorOr<WebhookIngestionOutcome>> IngestAsync(
        WebhookIngestionRequest request,
        CancellationToken ct
    )
    {
        if (!string.Equals(request.Provider, "duffel", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<ErrorOr<WebhookIngestionOutcome>>(
                WebhookIngestionErrors.UnsupportedProvider(request.Provider)
            );

        return port.IngestAsync(request, ct);
    }
}
