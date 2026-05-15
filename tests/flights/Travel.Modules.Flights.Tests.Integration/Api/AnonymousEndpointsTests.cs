// Direct-method tests (not Alba full-host) — chosen because the host requires
// Postgres + Redis + NATS + Keycloak which are not wired in this test project.
// [Authorize] pipeline enforcement is handled by ASP.NET Core and verified manually.

using ErrorOr;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
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
public sealed class AnonymousEndpointsTests
{
    // ── helpers ─────────────────────────────────────────────────────────────────

    private static readonly IataCode Led = IataCode.Create("LED").Value;
    private static readonly IataCode Dme = IataCode.Create("DME").Value;
    private static readonly CurrencyCode Rub = CurrencyCode.Create("RUB").Value;

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

    private static BookableOffer BuildBookableOffer() =>
        new(
            Id: OfferId.New(),
            Itinerary: BuildItinerary(),
            TotalAmount: Money.Create(5420m, Rub).Value,
            Provider: ProviderId.Duffel,
            FetchedAt: DateTimeOffset.UtcNow,
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(20),
            FareConditions: new FareConditions(false, false, null, null),
            ProviderOfferRef: "off_test_abc"
        );

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
        new FakeBus(msg => Task.FromResult<object?>((object?)response));

    // ── SearchEndpoint ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchEndpoint_ValidRequest_ReturnsOkWithSearchResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var offer = BuildBookableOffer();
        var fakeResult = (ErrorOr<SearchResult>)new SearchResult([offer], []);
        var bus = BusReturning(fakeResult);

        var req = new SearchRequest("LED", "DME", new DateOnly(2026, 7, 15), null);
        var result = await SearchEndpoint.Post(
            req,
            bus,
            new DefaultHttpContext().Request,
            currency: null,
            ct
        );

        var okResult = result.ShouldBeOfType<Ok<SearchResponse>>();
        okResult.Value.ShouldNotBeNull();
        okResult.Value.Offers.Length.ShouldBe(1);
        okResult.Value.PartialFailures.Length.ShouldBe(0);
    }

    [Fact]
    public async Task SearchEndpoint_BadIataCode_ReturnsProblem()
    {
        var ct = TestContext.Current.CancellationToken;
        // bus should not be called for invalid IATA
        var bus = BusReturning((ErrorOr<SearchResult>)new SearchResult([], []));

        // "INVALID" is 7 chars — not a valid IATA
        var req = new SearchRequest("INVALID", "DME", new DateOnly(2026, 7, 15), null);
        var result = await SearchEndpoint.Post(
            req,
            bus,
            new DefaultHttpContext().Request,
            currency: null,
            ct
        );

        result.ShouldBeOfType<ProblemHttpResult>();
    }

    [Fact]
    public async Task SearchEndpoint_BadCurrency_ReturnsProblem()
    {
        var ct = TestContext.Current.CancellationToken;
        var bus = BusReturning((ErrorOr<SearchResult>)new SearchResult([], []));

        // "US" is only 2 chars — not a valid 3-letter currency code
        var req = new SearchRequest("LED", "DME", new DateOnly(2026, 7, 15), null);
        var result = await SearchEndpoint.Post(
            req,
            bus,
            new DefaultHttpContext().Request,
            currency: "US",
            ct
        );

        result.ShouldBeOfType<ProblemHttpResult>();
    }

    // ── QuoteOfferEndpoint ────────────────────────────────────────────────────────

    [Fact]
    public async Task QuoteOfferEndpoint_ValidRequest_ReturnsOkWithQuotedOfferResponse()
    {
        var ct = TestContext.Current.CancellationToken;
        var aggregateId = Guid.NewGuid();
        var offer = BuildBookableOffer();
        var fakeResult = (ErrorOr<QuotedOfferResult>)new QuotedOfferResult(aggregateId, offer);
        var bus = BusReturning(fakeResult);

        var req = new QuoteOfferRequest("off_test_ref", "duffel");
        var result = await QuoteOfferEndpoint.Post(req, bus, ct);

        var okResult = result.ShouldBeOfType<Ok<QuotedOfferResponse>>();
        okResult.Value.ShouldNotBeNull();
        okResult.Value.AggregateId.ShouldBe(aggregateId);
        okResult.Value.Offer.ProviderOfferRef.ShouldBe("off_test_abc");
    }

    [Fact]
    public async Task QuoteOfferEndpoint_BusReturnsError_ReturnsProblem()
    {
        var ct = TestContext.Current.CancellationToken;
        var fakeResult =
            (ErrorOr<QuotedOfferResult>)Error.NotFound("Flights.OfferNotFound", "Offer not found.");
        var bus = BusReturning(fakeResult);

        var req = new QuoteOfferRequest("off_expired_ref", "duffel");
        var result = await QuoteOfferEndpoint.Post(req, bus, ct);

        result.ShouldBeOfType<ProblemHttpResult>();
    }
}
