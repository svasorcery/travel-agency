using System.Net.Http.Json;
using System.Text.Json.Serialization;
using ErrorOr;
using Travel.Modules.Flights.Core.ValueObjects;

namespace Travel.Modules.Flights.Infrastructure.ExternalServices;

public sealed class FrankfurterClient(HttpClient httpClient)
{
    public async Task<ErrorOr<decimal>> GetRateAsync(
        CurrencyCode from,
        CurrencyCode to,
        CancellationToken ct
    )
    {
        if (from == to)
            return 1m;

        var url = $"latest?base={from.Value}&symbols={to.Value}";

        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(url, ct);
        }
        catch (Exception ex)
        {
            return Error.Failure(
                "Flights.FxRateUnavailable",
                $"HTTP request to Frankfurter failed: {ex.Message}"
            );
        }

        if (!response.IsSuccessStatusCode)
            return Error.Failure(
                "Flights.FxRateUnavailable",
                $"Frankfurter returned {(int)response.StatusCode} for {from}/{to}."
            );

        var body = await response.Content.ReadFromJsonAsync<FrankfurterResponse>(
            cancellationToken: ct
        );

        if (body is null || !body.Rates.TryGetValue(to.Value, out var rate))
            return Error.Failure(
                "Flights.FxRateUnavailable",
                $"Frankfurter response did not contain rate for {to.Value}."
            );

        return rate;
    }

    private sealed record FrankfurterResponse(
        [property: JsonPropertyName("amount")] decimal Amount,
        [property: JsonPropertyName("base")] string Base,
        [property: JsonPropertyName("date")] string Date,
        [property: JsonPropertyName("rates")] Dictionary<string, decimal> Rates
    );
}
