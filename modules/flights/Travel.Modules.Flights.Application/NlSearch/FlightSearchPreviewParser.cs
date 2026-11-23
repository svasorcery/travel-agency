using Travel.IntegrationContracts.AI.NlSearch;
using Wolverine;

namespace Travel.Modules.Flights.Application.NlSearch;

public static class FlightSearchPreviewParser
{
    public static async Task<FlightSearchPreviewDraft?> ParseAsync(
        string query,
        DateOnly referenceDate,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var request = new FlightSearchPreviewRequested(query, referenceDate, Guid.NewGuid());
        FlightSearchPreviewParsed reply;
        try
        {
            // Invoke the ephemeral AI responder directly. The description never enters a local work queue.
            reply = await bus.InvokeAsync<FlightSearchPreviewParsed>(
                request,
                ct,
                timeout: TimeSpan.FromSeconds(6)
            );
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }

        return
            reply?.CorrelationId == request.CorrelationId
            && reply.Fragments is { Restrictions: not null } fragments
            ? FlightSearchPreview.Normalize(query, referenceDate, fragments)
            : null;
    }
}
