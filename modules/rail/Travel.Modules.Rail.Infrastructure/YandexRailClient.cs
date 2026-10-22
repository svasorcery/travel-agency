using System.Net;
using ErrorOr;
using Travel.Modules.Rail.Application;

namespace Travel.Modules.Rail.Infrastructure;

public sealed class YandexRailClient(
    HttpClient http,
    RailRuntime runtime,
    RailQuotaLedger quota,
    TimeProvider clock
)
{
    public async Task<ErrorOr<HttpResponseMessage>> GetAsync(
        string method,
        string query,
        RailProfile profile,
        CancellationToken cancellationToken
    )
    {
        if (method is not ("copyright" or "stations_list" or "search" or "thread"))
            return Error.Failure("method-denied");
        if (method == "thread" && !profile.JourneyDetailsEnabled)
            return Error.Failure("details-disabled");
        if (
            profile.Revoked.IsCancellationRequested
            || !profile.Enabled
            || runtime.Current.Epoch != profile.Epoch
        )
            return Error.Failure("source-disabled");
        var options = runtime.Options;
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.rasp.yandex-net.ru/v3.0/{method}/?format=json{query}"
        );
        try
        {
            request.Headers.Add(
                "Authorization",
                options.Fictional ? "fictional-no-key" : options.ApiKey
            );
        }
        catch (FormatException)
        {
            return Error.Failure("source-configuration");
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (profile.Revoked.IsCancellationRequested || runtime.Current.Epoch != profile.Epoch)
            return Error.Failure("source-disabled");
        using var dispatch = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            profile.Revoked
        );
        if (
            !quota.TryDebit(
                options.Fictional ? 100000 : options.MinuteAllowance,
                options.Fictional ? 100000 : options.DayAllowance
            )
        )
            return Error.Failure("quota");
        try
        {
            var response = await http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                dispatch.Token
            );
            if (response.Headers.Sum(x => x.Key.Length + x.Value.Sum(y => y.Length)) > 16384)
            {
                response.Dispose();
                return Error.Failure("headers-oversize");
            }
            if (response.IsSuccessStatusCode)
                return response;
            var status = response.StatusCode;
            if (status == HttpStatusCode.TooManyRequests)
                quota.RemoteThrottled(
                    response.Headers.TryGetValues("Retry-After", out var values)
                        ? values.FirstOrDefault()
                        : null
                );
            response.Dispose();
            return Error.Failure(
                status switch
                {
                    HttpStatusCode.Unauthorized => "supplier-unauthorized",
                    HttpStatusCode.Forbidden => "supplier-forbidden",
                    HttpStatusCode.TooManyRequests => "supplier-quota",
                    _ => "supplier-http",
                }
            );
        }
        catch (HttpRequestException error)
        {
            if (
                method == "thread"
                && error.HttpRequestError == HttpRequestError.ConfigurationLimitExceeded
            )
                return Error.Failure("headers-oversize");
            return Error.Failure("transport");
        }
    }

    internal DateTimeOffset ReceivedNow() => clock.GetUtcNow();

    internal static async Task<ErrorOr<byte[]>> ReadBoundedAsync(
        HttpResponseMessage response,
        int maximum,
        CancellationToken ct
    )
    {
        if (response.Content.Headers.ContentLength > maximum)
            return Error.Failure("body-oversize");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[16384];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0)
                break;
            if (output.Length + read > maximum)
                return Error.Failure("body-oversize");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}
