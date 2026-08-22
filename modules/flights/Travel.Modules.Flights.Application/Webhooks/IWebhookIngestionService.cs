using ErrorOr;

namespace Travel.Modules.Flights.Application.Webhooks;

public interface IWebhookIngestionService
{
    Task<ErrorOr<WebhookIngestionOutcome>> IngestAsync(
        WebhookIngestionRequest request,
        CancellationToken ct
    );
}
