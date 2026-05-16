using Microsoft.Extensions.Options;
using Shouldly;
using Travel.Modules.Flights.Infrastructure.Providers.Travelpayouts;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Providers.Travelpayouts;

[Trait("Category", "Integration")]
public sealed class TravelpayoutsClientTests : IDisposable
{
    private readonly WireMockServer _server;

    public TravelpayoutsClientTests()
    {
        _server = WireMockServer.Start();
    }

    public void Dispose() => _server.Stop();

    [Fact]
    public async Task GetAsync_ReachesStub_Returns200()
    {
        // Arrange
        _server
            .Given(Request.Create().WithPath("/aviasales/v3/prices_for_dates").UsingGet())
            .RespondWith(
                Response
                    .Create()
                    .WithStatusCode(200)
                    .WithHeader("Content-Type", "application/json")
                    .WithBody("""{"success":true,"data":[],"currency":"rub"}""")
            );

        var opts = Options.Create(
            new TravelpayoutsOptions
            {
                BaseUrl = _server.Url!,
                ApiVersion = "v3",
                ApiToken = "test_token",
                PartnerMarker = "test_marker",
            }
        );

        var http = new HttpClient { BaseAddress = new Uri(_server.Url!) };
        var client = new TravelpayoutsClient(http, opts);

        // Act
        var response = await client.GetAsync(
            "/aviasales/v3/prices_for_dates?origin=LED&destination=DME&token=test_token",
            CancellationToken.None
        );

        // Assert
        response.IsSuccessStatusCode.ShouldBeTrue();
        _server.LogEntries.ShouldNotBeEmpty();
    }
}
