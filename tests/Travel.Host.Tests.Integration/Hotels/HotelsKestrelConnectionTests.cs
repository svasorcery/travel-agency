using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Travel.Host.Tests.Integration.Hotels;

[Trait("Category", "Integration")]
[Collection(HostIntegrationCollection.Name)]
public sealed class HotelsKestrelConnectionTests
{
    [Fact]
    public async Task Owned_Kestrel_client_targets_its_listener_before_readonly_connectivity_probe()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var fixture = new HotelsHostFixture();
        await fixture.StartAsync(ct, 5111);
        var addresses = fixture
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Select(address => new Uri(address))
            .ToArray();
        var client = fixture.Client.BaseAddress!;
        var observed = JsonSerializer.Serialize(
            new
            {
                client = client.AbsoluteUri,
                listeners = addresses.Select(a => a.AbsoluteUri),
                supplierStarts = fixture.Supplier.Starts.Count,
                isolated = fixture.IsolatedConfiguration,
            }
        );
        TestContext.Current.TestOutputHelper?.WriteLine(observed);
        // Validate ownership before sending anything, including a request to a mistaken default port.
        addresses.ShouldNotBeEmpty();
        addresses.ShouldAllBe(address => address.IsLoopback && address.Port == 5111);
        client.IsLoopback.ShouldBeTrue(observed);
        client.Port.ShouldBe(5111, observed);
        fixture.Supplier.Starts.Count.ShouldBe(0);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/hotels/reservations");
        request.Headers.Authorization = new("Bearer", "fictional-hotels-owner");
        using var response = await fixture.Client.SendAsync(request, ct);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        using var content = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        content.RootElement.GetArrayLength().ShouldBe(0);
        fixture.Supplier.Starts.Count.ShouldBe(0);
    }
}
