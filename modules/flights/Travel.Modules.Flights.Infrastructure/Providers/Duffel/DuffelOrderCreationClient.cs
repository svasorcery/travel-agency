using Microsoft.Extensions.Options;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel;

/// <summary>Dedicated transport for the single inline create; no resilience retry or shorter read timeout.</summary>
public sealed class DuffelOrderCreationClient
{
    private readonly DuffelClient client;

    public DuffelOrderCreationClient(HttpClient http, IOptions<DuffelOptions> options)
    {
        http.Timeout = Timeout.InfiniteTimeSpan;
        client = new DuffelClient(http, options);
    }

    public Task<HttpResponseMessage> CreateAsync(object body, CancellationToken ct) =>
        client.PostAsync("/air/orders", body, HttpCompletionOption.ResponseHeadersRead, ct);
}
