using System.Net;
using System.Net.Http.Json;
using System.Text;
using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Application.Queries;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

[Collection(HostIntegrationCollection.Name)]
public sealed class NlSearchAdmissionHttpTests : IAsyncLifetime
{
    private readonly FlightsApiFixture fixture = new();

    public async ValueTask InitializeAsync()
    {
        await fixture.InitializeAsync();
        fixture.Bus.On<NlSearchQuery>((ErrorOr<SearchResult>)new SearchResult([], []));
    }

    public ValueTask DisposeAsync() => fixture.DisposeAsync();

    [Theory]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("whitespace")]
    [InlineData("too-long")]
    public async Task Invalid_query_is_rejected_before_bus_dispatch(string kind)
    {
        var query = kind switch
        {
            "null" => null,
            "empty" => "",
            "whitespace" => "  ",
            _ => new string('x', 1001),
        };
        using var response = await fixture.Client.PostAsJsonAsync(
            "/api/flights/search/nl",
            new { query },
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        fixture.Bus.InvocationCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("/api/flights/search/nl", false)]
    [InlineData("/API/FLIGHTS/SEARCH/NL/", false)]
    [InlineData("/api/flights/search/nl", true)]
    [InlineData("/Api/Flights/Search/Nl/", true)]
    public async Task Oversized_body_is_rejected_before_binding_for_known_and_chunked_input(
        string path,
        bool chunked
    )
    {
        var bytes = Encoding.UTF8.GetBytes(
            "{\"query\":\"Moscow to Kazan\",\"padding\":\"" + new string('x', 17 * 1024) + "\"}"
        );
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        request.Content = chunked ? new UnknownLengthContent(bytes) : new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new("application/json");
        if (chunked)
            request.Headers.TransferEncodingChunked = true;
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.RequestEntityTooLarge);
        fixture.Bus.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task Anonymous_burst_has_no_queue_and_stops_the_seventh_dispatch()
    {
        for (var index = 0; index < 6; index++)
        {
            using var accepted = await fixture.Client.PostAsJsonAsync(
                "/api/flights/search/nl",
                new { query = "Moscow to Kazan" },
                TestContext.Current.CancellationToken
            );
            accepted.StatusCode.ShouldBe(HttpStatusCode.OK);
        }
        using var rejected = await fixture.Client.PostAsJsonAsync(
            "/api/flights/search/nl",
            new { query = "Moscow to Kazan" },
            TestContext.Current.CancellationToken
        );
        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        fixture.Bus.InvocationCount.ShouldBe(6);
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes, TestContext.Current.CancellationToken).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
