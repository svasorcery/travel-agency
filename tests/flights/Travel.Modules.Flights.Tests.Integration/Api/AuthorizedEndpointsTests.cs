// Direct-method tests (not Alba full-host) — chosen because the host requires
// Postgres + Redis + NATS + Keycloak. The [Authorize] attribute enforcement is
// handled by ASP.NET Core middleware and is verified manually / in E2E tests.
// An unauthenticated call's 401 is therefore NOT tested here but noted above.

using System.Security.Claims;
using ErrorOr;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Travel.Modules.Flights.Api.Contracts;
using Travel.Modules.Flights.Api.Endpoints;
using Travel.Modules.Flights.Application.Commands;
using Travel.Modules.Flights.Application.Queries;
using Travel.Modules.Flights.Core.ValueObjects;
using Travel.Modules.Flights.Core.ValueObjects.Identifiers;
using Travel.Modules.Flights.Core.ValueObjects.Offer;
using Wolverine;
using Xunit;

namespace Travel.Modules.Flights.Tests.Integration.Api;

[Trait("Category", "Integration")]
public sealed class AuthorizedEndpointsTests
{
    // ── helpers ─────────────────────────────────────────────────────────────────

    private static readonly IataCode Led = IataCode.Create("LED").Value;
    private static readonly IataCode Dme = IataCode.Create("DME").Value;
    private static readonly CurrencyCode Rub = CurrencyCode.Create("RUB").Value;
    private static readonly Guid UserId = Guid.NewGuid();

    private static Itinerary BuildItinerary()
    {
        var seg = Segment
            .Create(
                Led,
                Dme,
                new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero),
                "SU",
                "SU100",
                CabinClass.Economy
            )
            .Value;
        return Itinerary.Create([Slice.Create([seg]).Value]).Value;
    }

    private static HttpContext BuildHttpContext(Guid? userId = null)
    {
        var id = (userId ?? UserId).ToString();
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id)], "Bearer")
        );
        return new DefaultHttpContext { User = principal };
    }

    // ── fake bus ─────────────────────────────────────────────────────────────────

    private sealed class FakeBus(Func<object, Task<object?>> invokeFunc) : IMessageBus
    {
        public string? TenantId { get; set; }

        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        )
        {
            var result = invokeFunc(message).GetAwaiter().GetResult();
            return Task.FromResult((T)result!);
        }

        public Task InvokeAsync(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => invokeFunc(message);

        public Task InvokeAsync(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => invokeFunc(message);

        public Task<T> InvokeAsync<T>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        )
        {
            var result = invokeFunc(message).GetAwaiter().GetResult();
            return Task.FromResult((T)result!);
        }

        public Task InvokeForTenantAsync(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotSupportedException();

        public Task<T> InvokeForTenantAsync<T>(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotSupportedException();

        public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) =>
            ValueTask.CompletedTask;

        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null) =>
            ValueTask.CompletedTask;

        public ValueTask BroadcastToTopicAsync(
            string topicName,
            object message,
            DeliveryOptions? options = null
        ) => ValueTask.CompletedTask;

        public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotSupportedException();

        public IDestinationEndpoint EndpointFor(string endpointName) =>
            throw new NotSupportedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message) =>
            throw new NotSupportedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(
            object message,
            DeliveryOptions options
        ) => throw new NotSupportedException();
    }

    private static IMessageBus BusReturning<T>(T response) =>
        new FakeBus(_ => Task.FromResult<object?>((object?)response));

    // A bus dispatching by message type
    private static IMessageBus MultiResponseBus(Dictionary<Type, object> responses) =>
        new FakeBus(msg =>
            responses.TryGetValue(msg.GetType(), out var r)
                ? Task.FromResult<object?>(r)
                : throw new InvalidOperationException($"No response configured for {msg.GetType()}")
        );

    // ── HoldOfferEndpoint ─────────────────────────────────────────────────────────

    [Fact]
    public async Task HoldOfferEndpoint_TwoPassengers_ReturnsProblem()
    {
        var ct = TestContext.Current.CancellationToken;
        var bus = BusReturning(
            (ErrorOr<HeldOrderResult>)
                new HeldOrderResult(Guid.NewGuid(), "ord_1", DateTimeOffset.UtcNow.AddHours(1))
        );

        var req = new HoldOfferRequest(
            Guid.NewGuid(),
            [
                new PassengerInfoDto(
                    "Ivan",
                    "Petrov",
                    new DateOnly(1990, 1, 1),
                    "male",
                    "ivan@test.com",
                    "+79161234567"
                ),
                new PassengerInfoDto(
                    "Anna",
                    "Sidorova",
                    new DateOnly(1992, 5, 15),
                    "female",
                    "anna@test.com",
                    "+79167654321"
                ),
            ]
        );

        var result = await HoldOfferEndpoint.Post(req, bus, TimeProvider.System, ct);
        result.ShouldBeOfType<ProblemHttpResult>();
    }

    [Fact]
    public async Task HoldOfferEndpoint_OneValidPassenger_DispatchesCommandAndReturnsOk()
    {
        var ct = TestContext.Current.CancellationToken;
        var aggregateId = Guid.NewGuid();
        var heldResult =
            (ErrorOr<HeldOrderResult>)
                new HeldOrderResult(
                    aggregateId,
                    "ord_duffel_123",
                    DateTimeOffset.UtcNow.AddHours(1)
                );
        var bus = BusReturning(heldResult);

        var req = new HoldOfferRequest(
            aggregateId,
            [
                new PassengerInfoDto(
                    "Ivan",
                    "Petrov",
                    new DateOnly(1990, 1, 1),
                    "male",
                    "ivan@test.com",
                    "+79161234567"
                ),
            ]
        );

        var result = await HoldOfferEndpoint.Post(req, bus, TimeProvider.System, ct);
        var okResult = result.ShouldBeOfType<Ok<HeldOrderResponse>>();
        okResult.Value!.AggregateId.ShouldBe(aggregateId);
        okResult.Value.ProviderOrderId.ShouldBe("ord_duffel_123");
    }

    [Fact]
    public async Task HoldOfferEndpoint_InvalidGender_ReturnsProblem()
    {
        var ct = TestContext.Current.CancellationToken;
        var bus = BusReturning(
            (ErrorOr<HeldOrderResult>)
                new HeldOrderResult(Guid.NewGuid(), "ord_1", DateTimeOffset.UtcNow.AddHours(1))
        );

        var req = new HoldOfferRequest(
            Guid.NewGuid(),
            [
                new PassengerInfoDto(
                    "Ivan",
                    "Petrov",
                    new DateOnly(1990, 1, 1),
                    "alien",
                    "ivan@test.com",
                    "+79161234567"
                ),
            ]
        );

        var result = await HoldOfferEndpoint.Post(req, bus, TimeProvider.System, ct);
        result.ShouldBeOfType<ProblemHttpResult>();
    }

    // ── GetOrderEndpoint ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetOrderEndpoint_ValidOrderView_MapsToOrderResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var aggregateId = Guid.NewGuid();

        var itinerary = BuildItinerary();
        var itineraryJson = System.Text.Json.JsonSerializer.Serialize(
            itinerary,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        );

        var view = new OrderView(
            AggregateId: aggregateId,
            UserId: UserId,
            ProviderOrderId: "ord_duffel_456",
            Status: "Confirmed",
            TotalAmount: 5420m,
            Currency: "RUB",
            ItineraryJson: itineraryJson,
            PassengerInfoJson: "{}",
            TicketNumbers: ["TKT001"],
            BookedAt: DateTimeOffset.UtcNow.AddHours(-1),
            TicketedAt: DateTimeOffset.UtcNow,
            CancelledAt: null,
            RefundedAt: null
        );

        var fakeResult = (ErrorOr<OrderView>)view;
        var bus = BusReturning(fakeResult);
        var httpCtx = BuildHttpContext(UserId);

        var result = await GetOrderEndpoint.Get(
            aggregateId,
            httpCtx,
            bus,
            NullLogger<GetOrderEndpoint>.Instance,
            ct
        );

        var okResult = result.ShouldBeOfType<Ok<OrderResponse>>();
        okResult.Value!.AggregateId.ShouldBe(aggregateId);
        okResult.Value.Status.ShouldBe("Confirmed");
        okResult.Value.TotalAmount.ShouldBe(5420m);
        okResult.Value.Currency.ShouldBe("RUB");
        okResult.Value.TicketNumbers.ShouldContain("TKT001");
    }

    [Fact]
    public async Task GetOrderEndpoint_NotFound_ReturnsProblem()
    {
        var ct = TestContext.Current.CancellationToken;
        var fakeResult =
            (ErrorOr<OrderView>)Error.NotFound("Flights.OfferNotFound", "Order not found.");
        var bus = BusReturning(fakeResult);
        var httpCtx = BuildHttpContext(UserId);

        var result = await GetOrderEndpoint.Get(
            Guid.NewGuid(),
            httpCtx,
            bus,
            NullLogger<GetOrderEndpoint>.Instance,
            ct
        );
        result.ShouldBeOfType<ProblemHttpResult>();
    }
}
