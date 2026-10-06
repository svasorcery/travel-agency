using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using Travel.Modules.Flights.Infrastructure.Privacy;

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

    public async Task<HttpResponseMessage> PostAsync(string path, object body, CancellationToken ct)
    {
        try
        {
            return await _http.PostAsJsonAsync(path, new { data = body }, ct);
        }
        catch (Exception ex)
        {
            throw PrivacySafeFailure.From(ex, "Supplier request failed.");
        }
    }

    /// <summary>
    /// POST with additional per-request headers. The <paramref name="extraHeaders"/> are added to this
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
        try
        {
            return await _http.SendAsync(request, ct);
        }
        catch (Exception ex)
        {
            throw PrivacySafeFailure.From(ex, "Supplier request failed.");
        }
    }

    public async Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct)
    {
        try
        {
            return await _http.GetAsync(path, ct);
        }
        catch (Exception ex)
        {
            throw PrivacySafeFailure.From(ex, "Supplier request failed.");
        }
    }

    public async Task<HttpResponseMessage> GetAsync(
        string path,
        HttpCompletionOption completion,
        CancellationToken ct
    )
    {
        try
        {
            return await _http.GetAsync(path, completion, ct);
        }
        catch (Exception ex)
        {
            throw PrivacySafeFailure.From(ex, "Supplier request failed.");
        }
    }

    public async Task<HttpResponseMessage> PostAsync(
        string path,
        object body,
        HttpCompletionOption completion,
        CancellationToken ct
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(new { data = body }),
        };
        try
        {
            return await _http.SendAsync(request, completion, ct);
        }
        catch (Exception ex)
        {
            throw PrivacySafeFailure.From(ex, "Supplier request failed.");
        }
    }

    public async Task<HttpResponseMessage> PostBodylessAsync(string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        try
        {
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            throw PrivacySafeFailure.From(ex, "Supplier request failed.");
        }
    }
}
