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

    /// <summary>
    /// POST with additional per-request headers (e.g. <c>Idempotency-Key</c> on the
    /// Duffel payments endpoint). The <paramref name="extraHeaders"/> are added to this
    /// request only and do not affect the shared <see cref="HttpClient"/> defaults.
    /// </summary>
    public async Task<HttpResponseMessage> PostAsync(
        string path,
        object body,
        IReadOnlyDictionary<string, string> extraHeaders,
        CancellationToken ct
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Content = JsonContent.Create(new { data = body });
        foreach (var (key, value) in extraHeaders)
            request.Headers.TryAddWithoutValidation(key, value);
        return await _http.SendAsync(request, ct);
    }

    public Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct) =>
        _http.GetAsync(path, ct);
}
