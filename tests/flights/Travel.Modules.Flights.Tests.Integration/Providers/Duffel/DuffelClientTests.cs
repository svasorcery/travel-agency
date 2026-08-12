using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Flights.Infrastructure.Providers.Duffel;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Providers.Duffel;

[Trait("Category", "Integration")]
public sealed class DuffelClientTests : IDisposable
{
    private readonly WireMockServer _server;

    public DuffelClientTests()
    {
        _server = WireMockServer.Start();
    }

    public void Dispose() => _server.Stop();

    [Fact]
    public async Task GetAsync_SendsAuthorizationAndVersionHeaders()
    {
        // Arrange
        _server
            .Given(Request.Create().WithPath("/air/offers/x").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("{}"));

        var opts = Options.Create(
            new DuffelOptions
            {
                BaseUrl = _server.Url!,
                ApiVersion = "v2",
                ApiKey = "test_key",
            }
        );

        var http = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        var client = new DuffelClient(http, opts);

        // Act
        var response = await client.GetAsync("/air/offers/x", CancellationToken.None);

        // Assert
        response.IsSuccessStatusCode.ShouldBeTrue();

        var logEntries = _server.LogEntries.ToList();
        logEntries.ShouldNotBeEmpty();

        var requestMessage = logEntries[0].RequestMessage;
        requestMessage.ShouldNotBeNull();
        var requestHeaders = requestMessage.Headers;
        requestHeaders.ShouldNotBeNull();

        requestHeaders.ShouldContainKey("Authorization");
        string.Join(" ", requestHeaders["Authorization"]).ShouldContain("Bearer test_key");

        requestHeaders.ShouldContainKey("Duffel-Version");
        string.Join(" ", requestHeaders["Duffel-Version"]).ShouldBe("v2");
    }
}
