using ErrorOr;

namespace Travel.Modules.Flights.Application.Webhooks;

public interface IWebhookIngestionPort
{
    Task<ErrorOr<WebhookIngestionOutcome>> IngestAsync(
        WebhookIngestionRequest request,
        CancellationToken ct
    );
}
