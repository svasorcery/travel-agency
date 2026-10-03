using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

[Collection(HostIntegrationCollection.Name)]
public sealed class FlightsOrderPassengerCountHttpTests : IClassFixture<FlightsApiFixture>
{
    private readonly FlightsApiFixture fixture;

    public FlightsOrderPassengerCountHttpTests(FlightsApiFixture fixture)
    {
        this.fixture = fixture;
        fixture.Bus.Reset();
        fixture.IdempotencyStore.Reset();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(9)]
    public async Task Get_list_cancel_preserve_explicit_count_with_one_flat_ticket_and_owner_identity(
        int count
    )
    {
        var id = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var view = new OrderView(
            id,
            owner,
            "ord_fictional",
            "Held",
            21420,
            "RUB",
            "{}",
            ["ticket_fictional"],
            now,
            null,
            null,
            null,
            count
        );
        fixture.Bus.OnCapture<GetOrderQuery>(query =>
        {
            query.UserId.ShouldBe(owner);
            query.AggregateId.ShouldBe(id);
            return (ErrorOr<OrderView>)view;
        });
        fixture.Bus.OnCapture<ListOrdersQuery>(query =>
        {
            query.UserId.ShouldBe(owner);
            return new OrderListView([view], query.Limit, query.Offset);
        });
        var segment = Segment
            .Create(
                IataCode.Create("LED").Value,
                IataCode.Create("DME").Value,
                now.AddDays(1),
                now.AddDays(1).AddHours(2),
                "SU",
                "SU101",
                CabinClass.Economy
            )
            .Value;
        fixture.Bus.OnCapture<CancelOrderCommand>(command =>
        {
            command.UserId.ShouldBe(owner);
            command.AggregateId.ShouldBe(id);
            return (ErrorOr<CancelledOrderResult>)
                new CancelledOrderResult(
                    id,
                    "Cancelled",
                    new OrderCommandSnapshot(
                        Money.Create(21420, CurrencyCode.Create("RUB").Value).Value,
                        Itinerary.Create([Slice.Create([segment]).Value]).Value,
                        ["ticket_fictional"],
                        now,
                        null,
                        now,
                        null,
                        count
                    )
                );
        });
        foreach (
            var route in new[]
            {
                $"/api/flights/orders/{id}",
                $"/api/flights/orders?userId={Guid.NewGuid()}",
                $"/api/flights/orders/{id}/cancel",
            }
        )
        {
            var cancel = route.EndsWith("/cancel", StringComparison.Ordinal);
            using var request = new HttpRequestMessage(
                cancel ? HttpMethod.Post : HttpMethod.Get,
                route
            );
            request.Headers.Add(TestAuthHandler.UserIdHeader, owner.ToString());
            if (cancel)
            {
                request.Headers.Add(TestAuthHandler.ScopesHeader, "flights:book");
                request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
            }
            using var response = await fixture.Client.SendAsync(
                request,
                TestContext.Current.CancellationToken
            );
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            response.Headers.CacheControl!.NoStore.ShouldBeTrue();
            var wire = await response.Content.ReadFromJsonAsync<JsonElement>(
                TestContext.Current.CancellationToken
            );
            var item = wire.TryGetProperty("items", out var items) ? items[0] : wire;
            item.GetProperty("passengerCount").GetInt32().ShouldBe(count);
            item.GetProperty("ticketNumbers").GetArrayLength().ShouldBe(1);
            item.TryGetProperty("userId", out _).ShouldBeFalse();
        }
        fixture.Bus.InvocationCount.ShouldBe(3);
    }
}
