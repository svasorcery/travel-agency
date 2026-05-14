using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Travel.Modules.Flights.Infrastructure.Providers.Duffel;

public sealed class DuffelClient
{
    private readonly HttpClient _http;

    public DuffelClient(HttpClient http, IOptions<DuffelOptions> opts)
    {
        _http = http;
        if (_http.BaseAddress is null)
            _http.BaseAddress = new Uri(opts.Value.BaseUrl);
        _http.DefaultRequestHeaders.Remove("Duffel-Version");
        _http.DefaultRequestHeaders.Add("Duffel-Version", opts.Value.ApiVersion);
        _http.DefaultRequestHeaders.Authorization = new("Bearer", opts.Value.ApiKey);
        if (!_http.DefaultRequestHeaders.Accept.Any())
            _http.DefaultRequestHeaders.Accept.Add(new("application/json"));
    }

    public Task<HttpResponseMessage> PostAsync(string path, object body, CancellationToken ct) =>
        _http.PostAsJsonAsync(path, new { data = body }, ct);

    public Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct) =>
        _http.GetAsync(path, ct);
}
