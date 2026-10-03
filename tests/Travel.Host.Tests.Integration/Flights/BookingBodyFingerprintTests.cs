using System.Security.Cryptography;
using System.Text;
using Shouldly;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

public sealed class BookingBodyFingerprintTests(FlightsApiFixture fixture)
    : IClassFixture<FlightsApiFixture>
{
    [Theory]
    [InlineData("/api/flights/orders/hold")]
    [InlineData("/API/FLIGHTS/ORDERS/HOLD/")]
    public async Task Extracted_reader_keeps_original_method_path_and_raw_byte_fingerprint(
        string path
    )
    {
        fixture.IdempotencyStore.Reset();
        const string raw =
            "{ \"quoteRevision\":\"00000000-0000-0000-0000-000000000000\", \"passengers\":[] }";
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        request.Headers.Add(TestAuthHandler.ScopesHeader, "flights:book");
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        request.Content = new StringContent(raw, Encoding.UTF8, "application/json");
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        ((int)response.StatusCode).ShouldBe(400);
        fixture.IdempotencyStore.LastRoute.ShouldBe(path);
        fixture.IdempotencyStore.LastBodyHash.ShouldBe(
            Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes("POST\n" + path + "\n" + raw))
            )
        );
    }
}
