using Microsoft.Extensions.Options;

namespace Travel.Modules.Flights.Infrastructure.Providers.Travelpayouts;

public sealed class TravelpayoutsClient
{
    private readonly HttpClient _http;

    public TravelpayoutsClient(HttpClient http, IOptions<TravelpayoutsOptions> opts)
    {
        _http = http;
        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(opts.Value.BaseUrl);
        if (!_http.DefaultRequestHeaders.Accept.Any())
            _http.DefaultRequestHeaders.Accept.Add(new("application/json"));
    }

    public Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct) =>
        _http.GetAsync(path, ct);
}
