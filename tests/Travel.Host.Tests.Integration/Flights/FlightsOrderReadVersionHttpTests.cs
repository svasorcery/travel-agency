using System.Net;
using System.Text.Json;
using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Application.Queries;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

[Collection(HostIntegrationCollection.Name)]
public sealed class FlightsOrderReadVersionHttpTests(FlightsApiFixture fixture)
    : IClassFixture<FlightsApiFixture>
{
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(14)]
    public async Task Owned_no_store_reads_publish_current_or_unknown_checkpoint_without_disclosing_foreign_order(
        long version
    )
    {
        fixture.Bus.Reset();
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var view = new OrderView(
            id,
            owner,
            "ord_fictional",
            "Held",
            100m,
            "GBP",
            "{}",
            [],
            DateTimeOffset.UnixEpoch,
            null,
            null,
            null,
            1,
            version
        );
        fixture.Bus.OnCapture<GetOrderQuery>(query =>
            query.UserId == owner && query.AggregateId == id
                ? (ErrorOr<OrderView>)view
                : Error.NotFound("Flights.BookingNotFound", "Booking not found.")
        );
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/flights/orders/{id}");
        request.Headers.Add(TestAuthHandler.UserIdHeader, owner.ToString());
        using var response = await fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        using var json = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
        var checkpoint = json.RootElement.GetProperty("projectedStreamVersion");
        if (version <= 0)
            checkpoint.ValueKind.ShouldBe(JsonValueKind.Null);
        else
            checkpoint.GetInt64().ShouldBe(version);
        using var foreign = new HttpRequestMessage(HttpMethod.Get, $"/api/flights/orders/{id}");
        foreign.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        using var denied = await fixture.Client.SendAsync(
            foreign,
            TestContext.Current.CancellationToken
        );
        denied.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }
}
