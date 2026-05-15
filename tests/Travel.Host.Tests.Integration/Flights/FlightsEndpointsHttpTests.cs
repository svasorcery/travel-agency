using System.Net;
using System.Net.Http.Json;
using ErrorOr;
using Shouldly;
using Travel.Modules.Flights.Application.Queries;
using Xunit;

namespace Travel.Host.Tests.Integration.Flights;

/// <summary>
/// HTTP-pipeline tests for the Flights endpoints: they exercise the real ASP.NET request
/// pipeline — routing, authentication, the <c>RequireAuthenticatedUser</c> fallback policy,
/// per-endpoint <c>[Authorize]</c> / <c>[AllowAnonymous]</c> intent, the idempotency
/// middleware and model binding — with the message bus stubbed by <see cref="FakeMessageBus"/>.
///
/// These complement the direct-method endpoint tests in the Flights integration project,
/// which invoke the endpoint methods directly and so cannot observe authorization or routing.
/// </summary>
[Trait("Category", "Integration")]
public sealed class FlightsEndpointsHttpTests : IClassFixture<FlightsApiFixture>
{
    private readonly FlightsApiFixture _fixture;

    public FlightsEndpointsHttpTests(FlightsApiFixture fixture)
    {
        _fixture = fixture;
        _fixture.Bus.Reset();
    }

    private static readonly SearchResult EmptySearchResult = new([], []);
    private static readonly object MinimalSearchBody = new
    {
        origin = "LED",
        destination = "DME",
        departureDate = "2026-07-15",
    };

    private HttpRequestMessage Authenticated(HttpRequestMessage request, Guid userId)
    {
        request.Headers.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        return request;
    }

    // ── [AllowAnonymous] endpoints are reachable without a token ─────────────────

    [Fact]
    public async Task Search_AnonymousRequest_Returns200()
    {
        _fixture.Bus.On<SearchFlightsQuery>((ErrorOr<SearchResult>)EmptySearchResult);

        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/flights/search?currency=RUB",
            new
            {
                origin = "LED",
                destination = "DME",
                departureDate = "2026-07-15",
                returnDate = (string?)null,
                passengerCount = 1,
                cabinClass = "economy",
            },
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NlSearch_AnonymousRequest_Returns200()
    {
        _fixture.Bus.On<NlSearchQuery>((ErrorOr<SearchResult>)EmptySearchResult);

        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/flights/search/nl",
            new { query = "из Москвы в Питер завтра" },
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ── [Authorize] endpoints reject anonymous requests with 401 ────────────────

    [Theory]
    [InlineData("GET", "/api/flights/orders")]
    [InlineData("GET", "/api/flights/orders/11111111-1111-1111-1111-111111111111")]
    [InlineData("POST", "/api/flights/orders/hold")]
    [InlineData("POST", "/api/flights/orders/confirm")]
    [InlineData("POST", "/api/flights/orders/11111111-1111-1111-1111-111111111111/cancel")]
    public async Task AuthorizedEndpoint_WithoutToken_Returns401(string method, string route)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), route);
        if (method == "POST")
            request.Content = JsonContent.Create(new { aggregateId = Guid.NewGuid() });

        var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ── currency query param + Accept-Language locale ───────────────────────────

    [Fact]
    public async Task Search_reads_currency_from_query()
    {
        Travel.Modules.Flights.Application.Queries.SearchFlightsQuery? captured = null;
        _fixture.Bus.OnCapture<Travel.Modules.Flights.Application.Queries.SearchFlightsQuery>(q =>
        {
            captured = q;
            return (ErrorOr<SearchResult>)EmptySearchResult;
        });

        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/flights/search?currency=USD",
            MinimalSearchBody,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        captured.ShouldNotBeNull();
        captured!.Criteria.Currency.Value.ShouldBe("USD");
    }

    [Fact]
    public async Task Search_reads_locale_from_accept_language()
    {
        Travel.Modules.Flights.Application.Queries.SearchFlightsQuery? captured = null;
        _fixture.Bus.OnCapture<Travel.Modules.Flights.Application.Queries.SearchFlightsQuery>(q =>
        {
            captured = q;
            return (ErrorOr<SearchResult>)EmptySearchResult;
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/flights/search");
        request.Headers.AcceptLanguage.Clear();
        request.Headers.AcceptLanguage.ParseAdd("en");
        request.Content = JsonContent.Create(MinimalSearchBody);

        var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        captured.ShouldNotBeNull();
        captured!.Criteria.Locale.ShouldBe("en");
    }

    [Fact]
    public async Task Search_defaults_currency_rub_and_locale_ru()
    {
        Travel.Modules.Flights.Application.Queries.SearchFlightsQuery? captured = null;
        _fixture.Bus.OnCapture<Travel.Modules.Flights.Application.Queries.SearchFlightsQuery>(q =>
        {
            captured = q;
            return (ErrorOr<SearchResult>)EmptySearchResult;
        });

        // No currency query param, no Accept-Language header
        var response = await _fixture.Client.PostAsJsonAsync(
            "/api/flights/search",
            MinimalSearchBody,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        captured.ShouldNotBeNull();
        captured!.Criteria.Currency.Value.ShouldBe("RUB");
        captured!.Criteria.Locale.ShouldBe("ru");
    }

    // ── flights:book scope enforcement on booking endpoints ─────────────────────

    [Fact]
    public async Task Hold_without_flights_book_scope_is_forbidden()
    {
        // Authenticated (has a user-id header) but the scope claim does NOT contain
        // "flights:book" — the "flights:book" authorization policy should return 403.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/flights/orders/hold");
        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        // Intentionally omit X-Test-Scopes (or use an unrelated scope)
        request.Content = JsonContent.Create(new { aggregateId = Guid.NewGuid() });

        var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Hold_with_flights_book_scope_is_allowed()
    {
        _fixture.Bus.On<Travel.Modules.Flights.Application.Commands.HoldOfferCommand>(
            (ErrorOr<Travel.Modules.Flights.Application.Commands.HeldOrderResult>)
                new Travel.Modules.Flights.Application.Commands.HeldOrderResult(
                    Guid.NewGuid(),
                    "ord_test",
                    DateTimeOffset.UtcNow.AddHours(1)
                )
        );

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/flights/orders/hold");
        request.Headers.Add(TestAuthHandler.UserIdHeader, Guid.NewGuid().ToString());
        request.Headers.Add(TestAuthHandler.ScopesHeader, "flights:book");
        // Minimal valid hold-offer body (single passenger)
        request.Content = JsonContent.Create(
            new
            {
                aggregateId = Guid.NewGuid(),
                passengers = new[]
                {
                    new
                    {
                        givenName = "Ivan",
                        familyName = "Petrov",
                        dateOfBirth = "1990-01-01",
                        gender = "male",
                        email = "ivan@test.com",
                        phone = "+79161234567",
                    },
                },
            }
        );

        var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        // 200 OK (scope accepted) — not 401 or 403
        ((int)response.StatusCode).ShouldNotBe(401);
        ((int)response.StatusCode).ShouldNotBe(403);
    }

    // ── [Authorize] endpoints accept an authenticated request ───────────────────

    [Fact]
    public async Task ListOrders_WithToken_Returns200()
    {
        _fixture.Bus.On<ListOrdersQuery>(new OrderListView([], 50, 0));

        using var request = Authenticated(
            new HttpRequestMessage(HttpMethod.Get, "/api/flights/orders"),
            Guid.NewGuid()
        );
        var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetOrder_WithToken_ReachesHandler()
    {
        var aggregateId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var view = new OrderView(
            AggregateId: aggregateId,
            UserId: userId,
            ProviderOrderId: "ord_1",
            Status: "Confirmed",
            TotalAmount: 5420m,
            Currency: "RUB",
            ItineraryJson: "{}",
            PassengerInfoJson: "{}",
            TicketNumbers: [],
            BookedAt: DateTimeOffset.UtcNow,
            TicketedAt: null,
            CancelledAt: null,
            RefundedAt: null
        );
        _fixture.Bus.On<GetOrderQuery>((ErrorOr<OrderView>)view);

        using var request = Authenticated(
            new HttpRequestMessage(HttpMethod.Get, $"/api/flights/orders/{aggregateId}"),
            userId
        );
        var response = await _fixture.Client.SendAsync(
            request,
            TestContext.Current.CancellationToken
        );

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}
