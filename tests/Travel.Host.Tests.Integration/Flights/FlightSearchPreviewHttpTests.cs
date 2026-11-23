using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using ErrorOr;
using Shouldly;
using Travel.IntegrationContracts.AI.NlSearch;
using Travel.Modules.Flights.Application.Queries;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

[Collection(HostIntegrationCollection.Name)]
public sealed class FlightSearchPreviewHttpTests : IAsyncLifetime
{
    private const string Path = "/api/flights/search/nl/preview";
    private readonly FlightsApiFixture fixture = new() { FlightSearchPreviewEnabled = true };
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => fixture.InitializeAsync();

    public ValueTask DisposeAsync() => fixture.DisposeAsync();

    [Fact]
    public async Task Disabled_preview_is_not_cacheable_and_never_dispatches()
    {
        var disabled = new FlightsApiFixture();
        try
        {
            await disabled.InitializeAsync();
            using var response = await disabled.Client.PostAsJsonAsync(
                Path,
                new { query = "Хочу в Сочи", referenceDate = "2026-11-23" },
                TestContext.Current.CancellationToken
            );
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            response.Headers.CacheControl?.NoStore.ShouldBe(true);
            disabled.Bus.InvocationCount.ShouldBe(0);
        }
        finally
        {
            await disabled.DisposeAsync();
        }
    }

    [Fact]
    public async Task Weekend_wire_shape_matches_review_fixture()
    {
        fixture.Bus.OnCapture<FlightSearchPreviewRequested>(
            request => new FlightSearchPreviewParsed(
                request.CorrelationId,
                new(null, "в Сочи", "на выходные", null, "нас двое", null, [])
            )
        );
        using var response = await PreviewAsync("Хочу в Сочи на выходные, нас двое");
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl?.NoStore.ShouldBe(true);
        var actual = await response.Content.ReadFromJsonAsync<JsonNode>(Ct);
        var expected = JsonNode.Parse(
            """
            {
              "tripType": "roundTrip",
              "origin": null,
              "destination": {"code": "AER", "name": "Сочи"},
              "departureDate": {"value": "2026-11-28", "source": "suggested"},
              "returnDate": {"value": "2026-11-29", "source": "suggested"},
              "passengerCount": 2,
              "issues": [{"field": "origin", "reason": "missing"}]
            }
            """
        );
        JsonNode.DeepEquals(actual, expected).ShouldBeTrue(actual?.ToJsonString());
        fixture.Bus.InvocationCount.ShouldBe(1);
    }

    [Theory]
    [InlineData("", "2026-11-23")]
    [InlineData("В Сочи", "2026-11-31")]
    [InlineData("В Сочи", "2026-1-01")]
    [InlineData("В Сочи", null)]
    public async Task Invalid_input_has_no_ai_dispatch(string query, string? referenceDate)
    {
        using var response = await fixture.Client.PostAsJsonAsync(
            Path,
            new { query, referenceDate },
            Ct
        );
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Headers.CacheControl?.NoStore.ShouldBe(true);
        fixture.Bus.InvocationCount.ShouldBe(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Oversized_body_is_rejected_before_dispatch(bool chunked)
    {
        var body = Encoding.UTF8.GetBytes(
            "{\"referenceDate\":\"2026-11-23\",\"query\":\"" + new string('x', 17 * 1024) + "\"}"
        );
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/API/Flights/Search/NL/Preview/"
        );
        request.Content = chunked ? new ChunkedBody(body) : new ByteArrayContent(body);
        request.Content.Headers.ContentType = new("application/json");
        if (chunked)
            request.Headers.TransferEncodingChunked = true;
        using var response = await fixture.Client.SendAsync(request, Ct);
        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        response.Headers.CacheControl?.NoStore.ShouldBe(true);
        fixture.Bus.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task Preview_and_v1_share_the_http_rate_limit()
    {
        fixture.Bus.On<NlSearchQuery>((ErrorOr<SearchResult>)new SearchResult([], []));
        for (var n = 0; n < 6; n++)
        {
            using var accepted = await fixture.Client.PostAsJsonAsync(
                "/api/flights/search/nl",
                new { query = "В Сочи" },
                Ct
            );
            accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        using var rejected = await PreviewAsync("В Сочи");
        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        rejected.Headers.CacheControl?.NoStore.ShouldBe(true);
        fixture.Bus.InvocationCount.ShouldBe(6);
    }

    [Fact]
    public async Task Transport_failure_has_a_typed_response_without_the_description()
    {
        const string marker = "FICTIONAL_PREVIEW_FAILURE_MARKER";
        fixture.Bus.OnCapture<FlightSearchPreviewRequested>(_ =>
            throw new TimeoutException(marker)
        );
        using var response = await PreviewAsync(marker);
        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        response.Headers.CacheControl?.NoStore.ShouldBe(true);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain(marker);
        fixture.Bus.InvocationCount.ShouldBe(1);
    }

    private Task<HttpResponseMessage> PreviewAsync(string query) =>
        fixture.Client.PostAsJsonAsync(Path, new { query, referenceDate = "2026-11-23" }, Ct);

    private sealed class ChunkedBody(byte[] body) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(body, Ct).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
